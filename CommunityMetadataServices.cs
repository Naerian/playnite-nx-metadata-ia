using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    // Optional catalogues deliberately kept separate from official stores. Both
    // services require an exact title match and return only structured facts;
    // they are disabled by default to avoid adding noise to ordinary libraries.
    internal sealed class VndbMetadataService
    {
        private const string Endpoint = "https://api.vndb.org/kana/vn";

        public async Task<OfficialStoreMetadata> GetContextAsync(Game game, CancellationToken cancellationToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name)) return null;
            var request = new
            {
                filters = new object[] { "search", "=", TitleMatchingService.SearchTitle(game.Name) },
                fields = "title,alttitle,description,released,developers{name},tags{name,category}",
                results = 10,
                sort = "searchrank"
            };
            var response = await PostJsonAsync(Endpoint, request, cancellationToken).ConfigureAwait(false);
            var item = response == null ? null : response["results"].OfType<JObject>()
                .FirstOrDefault(x => Exact(game.Name, TokenText(x["title"]), TokenText(x["alttitle"])));
            if (item == null) return null;

            var id = TokenText(item["id"]);
            return new OfficialStoreMetadata
            {
                SourceName = MetaDataIASettings.SourceVndb,
                StoreUrl = string.IsNullOrWhiteSpace(id) ? string.Empty : "https://vndb.org/" + id,
                Title = TokenText(item["title"]),
                Description = StripVndbMarkup(TokenText(item["description"])),
                Developers = Names(item.SelectTokens("developers[*].name")),
                Tags = Names(item.SelectTokens("tags[*].name")).Take(12).ToList(),
                ReleaseDate = TokenText(item["released"]),
                Links = string.IsNullOrWhiteSpace(id) ? new List<Link>() : new List<Link> { new Link("VNDB", "https://vndb.org/" + id) },
                IsExactMatch = true
            };
        }

        private static async Task<JObject> PostJsonAsync(string url, object payload, CancellationToken cancellationToken)
        {
            EnsureTls12();
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                client.Headers[HttpRequestHeader.UserAgent] = "MetadataAIPlugin/1.0";
                var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
                cancellationToken.ThrowIfCancellationRequested();
                using (cancellationToken.Register(client.CancelAsync))
                {
                    var response = await client.UploadDataTaskAsync(url, "POST", body).ConfigureAwait(false);
                    return JObject.Parse(Encoding.UTF8.GetString(response));
                }
            }
        }

        private static void EnsureTls12()
        {
            try
            {
                // Explicit numeric flags keep TLS 1.2 available on older .NET 4.x hosts.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            }
            catch
            {
                // Ignore if the host runtime already restricts protocol changes.
            }
        }

        private static string StripVndbMarkup(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return Regex.Replace(Regex.Replace(value, @"\[(?:/?(?:b|i|u)|url(?:=[^\]]*)?)\]", string.Empty, RegexOptions.IgnoreCase), @"\s+", " ").Trim();
        }

        private static List<string> Names(IEnumerable<JToken> values)
        {
            return (values ?? Enumerable.Empty<JToken>()).Select(TokenText).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool Exact(string title, params string[] candidates)
        {
            return candidates.Any(x => TitleMatchingService.IsReliableMatch(title, x));
        }

        // Newtonsoft throws "Can not convert Object to String" on (string)JObject.
        private static string TokenText(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }

            if (token.Type == JTokenType.String || token.Type == JTokenType.Integer ||
                token.Type == JTokenType.Float || token.Type == JTokenType.Boolean ||
                token.Type == JTokenType.Guid || token.Type == JTokenType.Uri ||
                token.Type == JTokenType.Date)
            {
                return token.ToString();
            }

            if (token.Type == JTokenType.Object)
            {
                return TokenText(token["text"]) ?? TokenText(token["value"]) ?? TokenText(token["name"]) ?? TokenText(token["id"]);
            }

            return null;
        }
    }

    internal sealed class WikidataMetadataService
    {
        private const string Api = "https://www.wikidata.org/w/api.php";

        public async Task<OfficialStoreMetadata> GetContextAsync(Game game, CancellationToken cancellationToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name)) return null;
            var title = TitleMatchingService.SearchTitle(game.Name);
            var metadata = await FindVideoGameAsync(game, title, cancellationToken).ConfigureAwait(false);
            if (metadata != null || title.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
            {
                return metadata;
            }

            return await FindVideoGameAsync(game, "The " + title, cancellationToken).ConfigureAwait(false);
        }

        private async Task<OfficialStoreMetadata> FindVideoGameAsync(Game game, string searchTitle, CancellationToken cancellationToken)
        {
            var search = await GetJsonAsync(Api + "?action=wbsearchentities&format=json&language=en&type=item&limit=10&search=" + Uri.EscapeDataString(searchTitle ?? string.Empty), cancellationToken).ConfigureAwait(false);
            var hits = search == null ? null : search["search"] as JArray;
            var ids = hits == null
                ? new List<string>()
                : hits.OfType<JObject>()
                    .Where(x => Exact(game.Name, TokenText(x["label"]), TokenText(x["match"]), TokenText(x.SelectToken("match.text"))))
                    .Select(x => TokenText(x["id"]))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            foreach (var id in ids)
            {
                var metadata = await ReadVideoGameAsync(game, id, cancellationToken).ConfigureAwait(false);
                if (metadata != null)
                {
                    return metadata;
                }
            }

            return null;
        }

        private async Task<OfficialStoreMetadata> ReadVideoGameAsync(Game game, string id, CancellationToken cancellationToken)
        {
            var entityRoot = await GetJsonAsync(Api + "?action=wbgetentities&format=json&props=labels|descriptions|claims&languages=en|es&ids=" + Uri.EscapeDataString(id), cancellationToken).ConfigureAwait(false);
            var entity = entityRoot == null ? null : entityRoot.SelectToken("entities." + id) as JObject;
            if (entity == null || !IsVideoGame(entity)) return null;

            var platformIds = EntityIds(entity, "P400").ToList();
            var labels = await GetLabelsAsync(EntityIds(entity, "P178").Concat(EntityIds(entity, "P123")).Concat(EntityIds(entity, "P136")).Concat(EntityIds(entity, "P179")).Concat(platformIds), cancellationToken).ConfigureAwait(false);
            var platformLabels = platformIds.Select(platformId => labels.ContainsKey(platformId) ? labels[platformId] : null).ToList();
            var names = game == null || game.Platforms == null ? Enumerable.Empty<string>() : game.Platforms.Select(x => x == null ? null : x.Name);
            var specifications = game == null || game.Platforms == null ? Enumerable.Empty<string>() : game.Platforms.Select(x => x == null ? null : x.SpecificationId);
            if (!TitleMatchingService.PlatformLabelsFit(names, specifications, platformLabels)) return null;

            var title = Label(entity, "en") ?? Label(entity, "es");
            var description = Description(entity, "en") ?? Description(entity, "es");
            var url = FirstStringClaim(entity, "P856");
            return new OfficialStoreMetadata
            {
                SourceName = MetaDataIASettings.SourceWikidata,
                StoreUrl = "https://www.wikidata.org/wiki/" + id,
                Title = title,
                Description = description,
                Developers = LabelValues(entity, "P178", labels),
                Publishers = LabelValues(entity, "P123", labels),
                Genres = LabelValues(entity, "P136", labels),
                Series = LabelValues(entity, "P179", labels),
                ReleaseDate = FirstTimeClaim(entity, "P577"),
                Links = new[] { "https://www.wikidata.org/wiki/" + id, url }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Select(x => new Link("Wikidata", x)).ToList(),
                IsExactMatch = true
            };
        }

        private static async Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            EnsureTls12();
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "MetadataAIPlugin/1.0 (Playnite metadata plugin)";
                cancellationToken.ThrowIfCancellationRequested();
                using (cancellationToken.Register(client.CancelAsync))
                {
                    return JObject.Parse(await client.DownloadStringTaskAsync(url).ConfigureAwait(false));
                }
            }
        }

        private static void EnsureTls12()
        {
            try
            {
                // Explicit numeric flags keep TLS 1.2 available on older .NET 4.x hosts.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            }
            catch
            {
                // Ignore if the host runtime already restricts protocol changes.
            }
        }

        private static async Task<Dictionary<string, string>> GetLabelsAsync(IEnumerable<string> ids, CancellationToken cancellationToken)
        {
            var requested = (ids ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(30).ToList();
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (requested.Count == 0) return labels;
            var root = await GetJsonAsync(Api + "?action=wbgetentities&format=json&props=labels&languages=en|es&ids=" + Uri.EscapeDataString(string.Join("|", requested)), cancellationToken).ConfigureAwait(false);
            foreach (var entity in root["entities"].OfType<JProperty>())
            {
                labels[entity.Name] = Label(entity.Value as JObject, "en") ?? Label(entity.Value as JObject, "es");
            }
            return labels;
        }

        private static bool IsVideoGame(JObject entity)
        {
            return EntityIds(entity, "P31").Any(x => string.Equals(x, "Q7889", StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> EntityIds(JObject entity, string property)
        {
            return (entity == null ? Enumerable.Empty<JToken>() : entity.SelectTokens("claims." + property + "[*].mainsnak.datavalue.value.id"))
                .Select(TokenText).Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private static List<string> LabelValues(JObject entity, string property, Dictionary<string, string> labels)
        {
            return EntityIds(entity, property).Select(id => labels.ContainsKey(id) ? labels[id] : null).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string FirstTimeClaim(JObject entity, string property)
        {
            var value = entity == null
                ? null
                : entity.SelectTokens("claims." + property + "[*].mainsnak.datavalue.value.time")
                    .Select(TokenText)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.TrimStart('+').Split('T')[0];
        }

        private static string FirstStringClaim(JObject entity, string property)
        {
            return entity == null
                ? string.Empty
                : entity.SelectTokens("claims." + property + "[*].mainsnak.datavalue.value")
                    .Select(TokenText)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        }

        private static string Label(JObject entity, string language)
        {
            return entity == null ? null : TokenText(entity.SelectToken("labels." + language + ".value"));
        }

        private static string Description(JObject entity, string language)
        {
            return entity == null ? null : TokenText(entity.SelectToken("descriptions." + language + ".value"));
        }

        private static bool Exact(string title, params string[] candidates)
        {
            return candidates.Any(x => TitleMatchingService.IsReliableMatch(title, x));
        }

        // Newtonsoft throws "Can not convert Object to String" on (string)JObject.
        // Wikidata often returns objects for match / datavalue.value.
        private static string TokenText(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }

            if (token.Type == JTokenType.String || token.Type == JTokenType.Integer ||
                token.Type == JTokenType.Float || token.Type == JTokenType.Boolean ||
                token.Type == JTokenType.Guid || token.Type == JTokenType.Uri ||
                token.Type == JTokenType.Date)
            {
                return token.ToString();
            }

            if (token.Type == JTokenType.Object)
            {
                return TokenText(token["text"]) ?? TokenText(token["value"]) ?? TokenText(token["name"]) ??
                       TokenText(token["id"]) ?? TokenText(token["time"]);
            }

            return null;
        }
    }
}
