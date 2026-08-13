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
                filters = new object[] { "search", "=", game.Name.Trim() },
                fields = "title,alttitle,description,released,developers{name},tags{name,category}",
                results = 10,
                sort = "searchrank"
            };
            var response = await PostJsonAsync(Endpoint, request, cancellationToken).ConfigureAwait(false);
            var item = response == null ? null : response["results"].OfType<JObject>()
                .FirstOrDefault(x => Exact(game.Name, (string)x["title"], (string)x["alttitle"]));
            if (item == null) return null;

            var id = (string)item["id"];
            return new OfficialStoreMetadata
            {
                SourceName = MetaDataIASettings.SourceVndb,
                StoreUrl = string.IsNullOrWhiteSpace(id) ? string.Empty : "https://vndb.org/" + id,
                Title = (string)item["title"],
                Description = StripVndbMarkup((string)item["description"]),
                Developers = Names(item.SelectTokens("developers[*].name")),
                // VNDB tags are useful factual context for the model, but are not
                // automatically treated as Playnite tags. Features is the existing
                // structured-context channel for that supplemental information.
                Features = Names(item.SelectTokens("tags[*].name")).Take(12).ToList(),
                ReleaseDate = (string)item["released"],
                Links = string.IsNullOrWhiteSpace(id) ? new List<Link>() : new List<Link> { new Link("VNDB", "https://vndb.org/" + id) },
                IsExactMatch = true
            };
        }

        private static async Task<JObject> PostJsonAsync(string url, object payload, CancellationToken cancellationToken)
        {
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

        private static string StripVndbMarkup(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return Regex.Replace(Regex.Replace(value, @"\[(?:/?(?:b|i|u)|url(?:=[^\]]*)?)\]", string.Empty, RegexOptions.IgnoreCase), @"\s+", " ").Trim();
        }

        private static List<string> Names(IEnumerable<JToken> values)
        {
            return (values ?? Enumerable.Empty<JToken>()).Select(x => (string)x).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool Exact(string title, params string[] candidates)
        {
            var expected = Normalize(title);
            return !string.IsNullOrWhiteSpace(expected) && candidates.Any(x => string.Equals(expected, Normalize(x), StringComparison.Ordinal));
        }

        private static string Normalize(string value)
        {
            return new string((value ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }
    }

    internal sealed class WikidataMetadataService
    {
        private const string Api = "https://www.wikidata.org/w/api.php";

        public async Task<OfficialStoreMetadata> GetContextAsync(Game game, CancellationToken cancellationToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name)) return null;
            var search = await GetJsonAsync(Api + "?action=wbsearchentities&format=json&language=en&type=item&limit=10&search=" + Uri.EscapeDataString(game.Name.Trim()), cancellationToken).ConfigureAwait(false);
            var result = search == null ? null : search["search"].OfType<JObject>()
                .FirstOrDefault(x => Exact(game.Name, (string)x["label"], (string)x["match"]));
            var id = result == null ? null : (string)result["id"];
            if (string.IsNullOrWhiteSpace(id)) return null;

            var entityRoot = await GetJsonAsync(Api + "?action=wbgetentities&format=json&props=labels|descriptions|claims&languages=en|es&ids=" + Uri.EscapeDataString(id), cancellationToken).ConfigureAwait(false);
            var entity = entityRoot == null ? null : entityRoot.SelectToken("entities." + id) as JObject;
            if (entity == null || !IsVideoGame(entity)) return null;

            var labels = await GetLabelsAsync(EntityIds(entity, "P178").Concat(EntityIds(entity, "P123")).Concat(EntityIds(entity, "P136")).Concat(EntityIds(entity, "P179")), cancellationToken).ConfigureAwait(false);
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
                .Select(x => (string)x).Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private static List<string> LabelValues(JObject entity, string property, Dictionary<string, string> labels)
        {
            return EntityIds(entity, property).Select(id => labels.ContainsKey(id) ? labels[id] : null).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string FirstTimeClaim(JObject entity, string property)
        {
            var value = entity == null ? null : entity.SelectTokens("claims." + property + "[*].mainsnak.datavalue.value.time").Select(x => (string)x).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.TrimStart('+').Split('T')[0];
        }

        private static string FirstStringClaim(JObject entity, string property)
        {
            return entity == null ? string.Empty : entity.SelectTokens("claims." + property + "[*].mainsnak.datavalue.value").Select(x => (string)x).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        }

        private static string Label(JObject entity, string language)
        {
            return entity == null ? null : (string)entity.SelectToken("labels." + language + ".value");
        }

        private static string Description(JObject entity, string language)
        {
            return entity == null ? null : (string)entity.SelectToken("descriptions." + language + ".value");
        }

        private static bool Exact(string title, params string[] candidates)
        {
            var expected = Normalize(title);
            return !string.IsNullOrWhiteSpace(expected) && candidates.Any(x => string.Equals(expected, Normalize(x), StringComparison.Ordinal));
        }

        private static string Normalize(string value)
        {
            return new string((value ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }
    }
}
