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
            if (metadata == null && TitleMatchingService.CanUsePlayModeBaseTitle(game.Name))
            {
                metadata = await FindVideoGameAsync(
                    game,
                    TitleMatchingService.WithoutPlayModeSuffix(game.Name),
                    cancellationToken).ConfigureAwait(false);
            }

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

    /// <summary>
    /// Optional PCGamingWiki factual context. Cargo queries are restricted for anonymous
    /// clients, so we resolve the page via Steam AppID redirect or title search, then
    /// parse the Infobox game wikitext (genres, modes, perspectives, companies, date).
    /// </summary>
    public sealed class PcGamingWikiMetadataService
    {
        private const string Api = "https://www.pcgamingwiki.com/w/api.php";
        private const string SteamRedirect = "https://www.pcgamingwiki.com/api/appid.php?appid=";
        private const string UserAgent = "MetadataAIPlugin/1.0 (Playnite; https://github.com/Naerian/playnite-nx-metadata-ia)";

        private static readonly Regex DeveloperRow = new Regex(
            @"\{\{\s*Infobox game/row/developer\s*\|\s*([^}|]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PublisherRow = new Regex(
            @"\{\{\s*Infobox game/row/publisher\s*\|\s*([^}|]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DateRow = new Regex(
            @"\{\{\s*Infobox game/row/date\s*\|\s*([^}|]+)\s*\|\s*([^}|]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TaxonomyRow = new Regex(
            @"\{\{\s*Infobox game/row/taxonomy/(?<kind>genres|modes|perspectives)\s*\|\s*(?<value>[^}]*)\}\}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public async Task<OfficialStoreMetadata> GetContextAsync(Game game, CancellationToken cancellationToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return null;
            }

            var pageTitle = await ResolvePageTitleAsync(game, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pageTitle))
            {
                return null;
            }

            if (!TitleMatchingService.IsReliableMatch(game.Name, pageTitle.Replace('_', ' ')))
            {
                return null;
            }

            var wikitext = await GetWikitextAsync(pageTitle, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(wikitext) ||
                wikitext.IndexOf("Infobox game", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            return BuildMetadata(pageTitle, wikitext);
        }

        /// <summary>Public for unit tests: parse Infobox game rows from wikitext.</summary>
        public static OfficialStoreMetadata BuildMetadata(string pageTitle, string wikitext)
        {
            var genres = new List<string>();
            var tags = new List<string>();
            var features = new List<string>();
            foreach (Match match in TaxonomyRow.Matches(wikitext ?? string.Empty))
            {
                var kind = match.Groups["kind"].Value.Trim().ToLowerInvariant();
                var values = SplitPipeList(match.Groups["value"].Value);
                if (kind == "genres")
                {
                    AddUnique(genres, values);
                }
                else if (kind == "perspectives")
                {
                    AddUnique(tags, values);
                }
                else if (kind == "modes")
                {
                    AddUnique(features, values);
                }
            }

            var developers = new List<string>();
            foreach (Match match in DeveloperRow.Matches(wikitext ?? string.Empty))
            {
                AddUnique(developers, new[] { CleanCell(match.Groups[1].Value) });
            }

            var publishers = new List<string>();
            foreach (Match match in PublisherRow.Matches(wikitext ?? string.Empty))
            {
                AddUnique(publishers, new[] { CleanCell(match.Groups[1].Value) });
            }

            var releaseDate = string.Empty;
            var dateMatch = DateRow.Match(wikitext ?? string.Empty);
            if (dateMatch.Success)
            {
                releaseDate = CleanCell(dateMatch.Groups[2].Value);
            }

            var displayTitle = (pageTitle ?? string.Empty).Replace('_', ' ').Trim();
            var pageUrl = "https://www.pcgamingwiki.com/wiki/" + Uri.EscapeDataString(pageTitle ?? string.Empty).Replace("%2F", "/");
            return new OfficialStoreMetadata
            {
                SourceName = MetaDataIASettings.SourcePcGamingWiki,
                StoreUrl = pageUrl,
                Title = displayTitle,
                Genres = genres,
                Tags = tags,
                Features = features,
                Developers = developers,
                Publishers = publishers,
                ReleaseDate = releaseDate,
                Links = new List<Link> { new Link("PCGamingWiki", pageUrl) },
                ListsMatchPluginLanguage = false,
                IsExactMatch = true
            };
        }

        private async Task<string> ResolvePageTitleAsync(Game game, CancellationToken cancellationToken)
        {
            var steamId = OfficialStoreDataService.TryGetSteamAppId(game);
            if (!string.IsNullOrWhiteSpace(steamId))
            {
                var fromSteam = await ResolveTitleFromSteamAppIdAsync(steamId.Trim(), cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(fromSteam))
                {
                    return fromSteam;
                }
            }

            return await ResolveTitleFromSearchAsync(game.Name, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> ResolveTitleFromSteamAppIdAsync(string appId, CancellationToken cancellationToken)
        {
            EnsureTls12();
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = (HttpWebRequest)WebRequest.Create(SteamRedirect + Uri.EscapeDataString(appId));
                request.Method = "GET";
                request.AllowAutoRedirect = true;
                request.UserAgent = UserAgent;
                request.Timeout = 30000;
                using (cancellationToken.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    return PageTitleFromWikiUrl(response.ResponseUri == null ? null : response.ResponseUri.AbsoluteUri);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> ResolveTitleFromSearchAsync(string gameName, CancellationToken cancellationToken)
        {
            var url = Api + "?action=opensearch&format=json&limit=8&search=" + Uri.EscapeDataString(TitleMatchingService.SearchTitle(gameName) ?? string.Empty);
            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false) as JArray;
            if (json == null || json.Count < 2)
            {
                return null;
            }

            var titles = json[1] as JArray;
            if (titles == null)
            {
                return null;
            }

            foreach (var titleToken in titles)
            {
                var title = titleToken == null ? null : titleToken.ToString();
                if (TitleMatchingService.IsReliableMatch(gameName, title))
                {
                    return (title ?? string.Empty).Trim().Replace(' ', '_');
                }
            }

            return null;
        }

        private static async Task<string> GetWikitextAsync(string pageTitle, CancellationToken cancellationToken)
        {
            var url = Api + "?action=parse&format=json&prop=wikitext&page=" + Uri.EscapeDataString(pageTitle.Replace(' ', '_'));
            var root = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false) as JObject;
            return root == null ? null : TokenText(root.SelectToken("parse.wikitext.*"));
        }

        private static async Task<JToken> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            EnsureTls12();
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                cancellationToken.ThrowIfCancellationRequested();
                using (cancellationToken.Register(client.CancelAsync))
                {
                    var text = await client.DownloadStringTaskAsync(url).ConfigureAwait(false);
                    return JToken.Parse(text);
                }
            }
        }

        private static void EnsureTls12()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            }
            catch
            {
            }
        }

        public static string PageTitleFromWikiUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return null;
            }

            var path = uri.AbsolutePath ?? string.Empty;
            const string marker = "/wiki/";
            var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            var slug = path.Substring(index + marker.Length);
            if (string.IsNullOrWhiteSpace(slug))
            {
                return null;
            }

            return Uri.UnescapeDataString(slug);
        }

        private static IEnumerable<string> SplitPipeList(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanCell)
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private static string CleanCell(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var cleaned = Regex.Replace(value, @"\{\{[^}]*\}\}", string.Empty);
            cleaned = Regex.Replace(cleaned, @"\[\[([^|\]]*\|)?([^\]]+)\]\]", "$2");
            cleaned = Regex.Replace(cleaned, @"<ref\b[^>]*>.*?</ref>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            cleaned = Regex.Replace(cleaned, @"['\[\]]+", string.Empty);
            return cleaned.Trim();
        }

        private static void AddUnique(List<string> target, IEnumerable<string> values)
        {
            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (target.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                target.Add(value.Trim());
            }
        }

        private static string TokenText(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }

            if (token.Type == JTokenType.String || token.Type == JTokenType.Integer ||
                token.Type == JTokenType.Float || token.Type == JTokenType.Boolean)
            {
                return token.ToString();
            }

            return null;
        }
    }
}
