using Newtonsoft.Json.Linq;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    // IGDB is a factual fallback only. It is used after an official/origin lookup did not
    // identify the game, and only when its returned title is an exact normalized match.
    internal sealed class IgdbMetadataContextService
    {
        private static readonly HttpClient Client = new HttpClient();
        private readonly MetaDataIASettings settings;

        public IgdbMetadataContextService(MetaDataIASettings settings)
        {
            this.settings = settings;
        }

        public async Task<OfficialStoreMetadata> GetContextAsync(Game game, CancellationToken cancellationToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name)) return null;

            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token)) return null;

            var title = Escape(game.Name.Trim());
            var body = "search \"" + title + "\"; fields name,first_release_date,genres.name,involved_companies.company.name,involved_companies.developer,involved_companies.publisher,franchises.name,collections.name,websites.category,websites.url,age_ratings.category,age_ratings.rating,age_ratings.organization.name,age_ratings.rating_category.rating; limit 5;";
            var matches = await PostAsync("games", body, token, cancellationToken).ConfigureAwait(false);
            var selected = matches.OfType<JObject>().FirstOrDefault(x => IsExactTitleMatch(game.Name, (string)x["name"]));
            if (selected == null) return null;

            var developers = ReadCompanies(selected, "developer");
            var publishers = ReadCompanies(selected, "publisher");
            var series = ReadNames(selected["franchises"])
                .Concat(ReadNames(selected["collections"]))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new OfficialStoreMetadata
            {
                SourceName = "IGDB",
                Title = (string)selected["name"],
                Genres = ReadNames(selected["genres"]),
                Developers = developers,
                Publishers = publishers,
                AgeRating = ReadAgeRating(selected["age_ratings"]),
                ReleaseDate = FromUnixDate((long?)selected["first_release_date"]),
                Series = series,
                Links = ReadOfficialLinks(selected["websites"]),
                IsExactMatch = true
            };
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(settings.IgdbAccessToken)) return settings.IgdbAccessToken;
            if (string.IsNullOrWhiteSpace(settings.IgdbClientSecret)) return null;

            var url = "https://id.twitch.tv/oauth2/token?client_id=" + Uri.EscapeDataString(settings.IgdbClientId) +
                      "&client_secret=" + Uri.EscapeDataString(settings.IgdbClientSecret) + "&grant_type=client_credentials";
            using (var response = await Client.PostAsync(url, null, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) return null;
                var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                settings.IgdbAccessToken = (string)json["access_token"] ?? string.Empty;
                return settings.IgdbAccessToken;
            }
        }

        private async Task<JArray> PostAsync(string endpoint, string body, string token, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/" + endpoint))
            {
                request.Headers.Add("Client-ID", settings.IgdbClientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
                using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return new JArray();
                    return JArray.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
        }

        private static List<string> ReadNames(JToken token)
        {
            return (token as JArray ?? new JArray()).OfType<JObject>()
                .Select(x => ((string)x["name"] ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> ReadCompanies(JObject game, string role)
        {
            return (game["involved_companies"] as JArray ?? new JArray()).OfType<JObject>()
                .Where(x => (bool?)x[role] == true)
                .Select(x => ((string)x.SelectToken("company.name") ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<Link> ReadOfficialLinks(JToken token)
        {
            return (token as JArray ?? new JArray()).OfType<JObject>()
                .Where(x => (int?)x["category"] == 1 && Uri.IsWellFormedUriString((string)x["url"], UriKind.Absolute))
                .Select(x => new Link("Official website", (string)x["url"]))
                .Take(1)
                .ToList();
        }

        private static string ReadAgeRating(JToken token)
        {
            var values = (token as JArray ?? new JArray()).OfType<JObject>()
                .Select(ToAgeRating)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return values.FirstOrDefault(x => x.StartsWith("PEGI ", StringComparison.OrdinalIgnoreCase))
                ?? values.FirstOrDefault(x => x.StartsWith("ESRB ", StringComparison.OrdinalIgnoreCase))
                ?? values.FirstOrDefault();
        }

        private static string ToAgeRating(JObject value)
        {
            if (value == null) return null;
            var category = value["rating_category"] as JObject;
            var organization = ((string)value.SelectToken("organization.name") ?? string.Empty).Trim();
            var rating = ((string)(category == null ? null : category["rating"]) ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(organization) && !string.IsNullOrWhiteSpace(rating))
            {
                return organization + " " + rating;
            }

            var board = LegacyBoard((int?)value["category"]);
            var legacyRating = LegacyRating((int?)value["rating"]);
            return string.IsNullOrWhiteSpace(board) || string.IsNullOrWhiteSpace(legacyRating) ? null : board + " " + legacyRating;
        }

        private static string LegacyBoard(int? value)
        {
            switch (value)
            {
                case 1: return "ESRB";
                case 2: return "PEGI";
                case 3: return "CERO";
                case 4: return "USK";
                case 5: return "GRAC";
                case 6: return "CLASSIND";
                case 7: return "ACB";
                default: return null;
            }
        }

        private static string LegacyRating(int? value)
        {
            switch (value)
            {
                case 1: return "3";
                case 2: return "7";
                case 3: return "12";
                case 4: return "16";
                case 5: return "18";
                case 6: return "RP";
                case 7: return "EC";
                case 8: return "E";
                case 9: return "E10+";
                case 10: return "T";
                case 11: return "M";
                case 12: return "AO";
                case 13: return "A";
                case 14: return "B";
                case 15: return "C";
                case 16: return "D";
                case 17: return "Z";
                case 18: return "0";
                case 19: return "6";
                case 20: return "12";
                case 21: return "16";
                case 22: return "18";
                case 23: return "All";
                case 24: return "12";
                case 25: return "15";
                case 26: return "18";
                case 28: return "L";
                case 29: return "10";
                case 30: return "12";
                case 31: return "14";
                case 32: return "16";
                case 33: return "18";
                case 34: return "G";
                case 35: return "PG";
                case 36: return "M";
                case 37: return "MA15+";
                case 38: return "R18+";
                case 39: return "RC";
                default: return null;
            }
        }

        private static bool IsExactTitleMatch(string gameName, string candidate)
        {
            var left = NormalizeTitle(gameName);
            var right = NormalizeTitle(candidate);
            return !string.IsNullOrWhiteSpace(left) && string.Equals(left, right, StringComparison.Ordinal);
        }

        private static string NormalizeTitle(string value)
        {
            return new string((value ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string FromUnixDate(long? seconds)
        {
            if (!seconds.HasValue || seconds.Value <= 0) return string.Empty;
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds.Value).ToString("yyyy-MM-dd");
        }
    }
}
