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
    public sealed class SeriesOrderLookupResult
    {
        public string SeriesName { get; set; }
        public int Order { get; set; }
        public string Source { get; set; }
    }

    internal sealed class SeriesOrderLookupService
    {
        private static readonly HttpClient Client = new HttpClient();
        private readonly MetaDataIASettings settings;
        private string generatedAccessToken;

        public SeriesOrderLookupService(MetaDataIASettings settings)
        {
            this.settings = settings;
        }

        public async Task<SeriesOrderLookupResult> ResolveAsync(Game game, CancellationToken cancellationToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name) ||
                string.IsNullOrWhiteSpace(settings.IgdbClientId) ||
                (string.IsNullOrWhiteSpace(settings.IgdbAccessToken) && string.IsNullOrWhiteSpace(settings.IgdbClientSecret)))
            {
                return null;
            }

            try
            {
                JObject selected = null;
                foreach (var alias in TitleMatchingService.BuildAliases(game.Name))
                {
                    var matches = await PostAsync("games", "search \"" + Escape(alias) + "\"; fields id,name,collection,first_release_date,category,version_parent; limit 10;", cancellationToken).ConfigureAwait(false);
                    selected = matches.OfType<JObject>().FirstOrDefault(x => TitleMatchingService.IsReliableMatch(alias, (string)x["name"]));
                    if (selected != null)
                    {
                        break;
                    }
                }

                if (selected == null)
                {
                    return null;
                }

                var selectedId = (int?)selected["version_parent"] ?? (int?)selected["id"] ?? 0;
                if (selectedId <= 0)
                {
                    return null;
                }

                if ((int?)selected["version_parent"] > 0)
                {
                    var parents = await PostAsync("games", "where id = " + selectedId + "; fields id,name,collection,first_release_date,category; limit 1;", cancellationToken).ConfigureAwait(false);
                    selected = parents.OfType<JObject>().FirstOrDefault() ?? selected;
                }

                var collectionId = ReadId(selected["collection"]);
                if (collectionId <= 0)
                {
                    return null;
                }

                var collections = await PostAsync("collections", "where id = " + collectionId + "; fields name,games; limit 1;", cancellationToken).ConfigureAwait(false);
                var collection = collections.OfType<JObject>().FirstOrDefault();
                var ids = collection == null || collection["games"] == null
                    ? new List<int>()
                    : collection["games"].Values<int>().Where(x => x > 0).Distinct().ToList();
                if (ids.Count < 2)
                {
                    return null;
                }

                var entries = await PostAsync("games", "where id = (" + string.Join(",", ids) + "); fields id,name,first_release_date,category,version_parent; limit 500;", cancellationToken).ConfigureAwait(false);
                var mainGames = entries.OfType<JObject>()
                    .Where(x => ((int?)x["category"] ?? 0) == 0 && !((int?)x["version_parent"] > 0))
                    .OrderBy(x => (long?)x["first_release_date"] ?? long.MaxValue)
                    .ThenBy(x => (string)x["name"], StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var index = mainGames.FindIndex(x => ((int?)x["id"] ?? 0) == selectedId);
                if (index < 0)
                {
                    return null;
                }

                return new SeriesOrderLookupResult
                {
                    SeriesName = (string)collection["name"],
                    Order = index + 1,
                    Source = "IGDB"
                };
            }
            catch
            {
                // This lookup enriches a result but must never make metadata generation fail.
                return null;
            }
        }

        private async Task<JArray> PostAsync(string endpoint, string body, CancellationToken cancellationToken)
        {
            var accessToken = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new JArray();
            }

            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/" + endpoint))
            {
                request.Headers.Add("Client-ID", settings.IgdbClientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
                using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return new JArray();
                    }

                    return JArray.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
        }

        private async Task<string> EnsureAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(generatedAccessToken))
            {
                return generatedAccessToken;
            }

            if (!string.IsNullOrWhiteSpace(settings.IgdbClientSecret))
            {
                var url = "https://id.twitch.tv/oauth2/token?client_id=" + Uri.EscapeDataString(settings.IgdbClientId) +
                    "&client_secret=" + Uri.EscapeDataString(settings.IgdbClientSecret) + "&grant_type=client_credentials";
                using (var response = await Client.PostAsync(url, null, cancellationToken).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                        generatedAccessToken = (string)json["access_token"];
                        if (!string.IsNullOrWhiteSpace(generatedAccessToken))
                        {
                            settings.IgdbAccessToken = generatedAccessToken;
                            return generatedAccessToken;
                        }
                    }
                }
            }

            return settings.IgdbAccessToken;
        }

        private static int ReadId(JToken token)
        {
            if (token == null)
            {
                return 0;
            }

            return token.Type == JTokenType.Object ? ((int?)token["id"] ?? 0) : ((int?)token ?? 0);
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
