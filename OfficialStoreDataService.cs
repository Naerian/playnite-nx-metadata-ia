using Newtonsoft.Json.Linq;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    public class OfficialStoreMetadata
    {
        public string SourceName { get; set; }
        public string StoreUrl { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public List<string> Genres { get; set; }
        public List<string> Features { get; set; }
        public List<string> Developers { get; set; }
        public List<string> Publishers { get; set; }
        public List<string> Regions { get; set; }
        public List<Link> Links { get; set; }
        public string AgeRating { get; set; }
        public string ReleaseDate { get; set; }
        public List<string> Series { get; set; }
        public bool IsExactMatch { get; set; }

        public OfficialStoreMetadata()
        {
            Genres = new List<string>();
            Features = new List<string>();
            Developers = new List<string>();
            Publishers = new List<string>();
            Regions = new List<string>();
            Links = new List<Link>();
            Series = new List<string>();
        }

        public bool HasUsefulData()
        {
            return !string.IsNullOrWhiteSpace(Description) ||
                   Genres.Count > 0 ||
                   Features.Count > 0 ||
                   Developers.Count > 0 ||
                   Publishers.Count > 0 ||
                   Regions.Count > 0 ||
                   Links.Count > 0 ||
                   !string.IsNullOrWhiteSpace(AgeRating) ||
                   !string.IsNullOrWhiteSpace(ReleaseDate) ||
                   Series.Count > 0;
        }
    }

    public class OfficialMediaCandidate
    {
        public string Url { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Style { get; set; }
        public int Score { get; set; }
        public string SourceName { get; set; }
        public bool IsOfficial { get; set; }
        public string Extension { get; set; }
        public string Mime { get; set; }
    }

    public class OfficialStoreDataService
    {
        public const string SourceSteamOfficial = "Steam oficial";
        public const string SourcePsnStore = "PlayStation Store";
        public const string SourceXboxStore = "Xbox Store";
        public const string SourceEpicStore = "Epic Store";

        private static readonly HttpClient Client = new HttpClient();
        private readonly MetaDataIASettings settings;

        static OfficialStoreDataService()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
        }

        public OfficialStoreDataService(MetaDataIASettings settings)
        {
            this.settings = settings;
        }

        public async Task<List<OfficialStoreMetadata>> GetOfficialContextsAsync(Game game, CancellationToken cancelToken)
        {
            var result = new List<OfficialStoreMetadata>();
            foreach (var source in GetOfficialContextSourceOrder(game))
            {
                OfficialStoreMetadata metadata = null;
                try
                {
                    metadata = await GetMetadataAsync(game, source, cancelToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }

                if (metadata != null && metadata.HasUsefulData())
                {
                    metadata.IsExactMatch = true;
                    result.Add(metadata);
                }
            }

            return result;
        }

        public async Task<List<OfficialMediaCandidate>> GetMediaCandidatesAsync(Game game, MediaKind kind, string source, CancellationToken cancelToken)
        {
            try
            {
                if (string.Equals(source, SourcePsnStore, StringComparison.OrdinalIgnoreCase))
                {
                    return await GetPsnMediaCandidatesAsync(game, kind, cancelToken).ConfigureAwait(false);
                }

                if (string.Equals(source, SourceXboxStore, StringComparison.OrdinalIgnoreCase))
                {
                    return await GetXboxMediaCandidatesAsync(game, kind, cancelToken).ConfigureAwait(false);
                }

                if (string.Equals(source, SourceEpicStore, StringComparison.OrdinalIgnoreCase))
                {
                    return await GetEpicMediaCandidatesAsync(game, kind, cancelToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }

            return new List<OfficialMediaCandidate>();
        }

        private async Task<OfficialStoreMetadata> GetMetadataAsync(Game game, string source, CancellationToken cancelToken)
        {
            if (string.Equals(source, SourceSteamOfficial, StringComparison.OrdinalIgnoreCase))
            {
                return await GetSteamMetadataAsync(game, cancelToken).ConfigureAwait(false);
            }

            if (string.Equals(source, SourcePsnStore, StringComparison.OrdinalIgnoreCase))
            {
                return await GetPsnMetadataAsync(game, cancelToken).ConfigureAwait(false);
            }

            if (string.Equals(source, SourceXboxStore, StringComparison.OrdinalIgnoreCase))
            {
                return await GetXboxMetadataAsync(game, cancelToken).ConfigureAwait(false);
            }

            if (string.Equals(source, SourceEpicStore, StringComparison.OrdinalIgnoreCase))
            {
                return await GetEpicMetadataAsync(game, cancelToken).ConfigureAwait(false);
            }

            return null;
        }

        private IEnumerable<string> GetOfficialContextSourceOrder(Game game)
        {
            var order = new List<string>();
            var sourceName = game == null || game.Source == null ? string.Empty : game.Source.Name ?? string.Empty;
            AddSourceForName(order, sourceName);

            foreach (var link in GetGameLinks(game))
            {
                AddSourceForName(order, link);
            }

            AddUnique(order, SourceSteamOfficial);
            AddUnique(order, SourceXboxStore);
            AddUnique(order, SourcePsnStore);
            AddUnique(order, SourceEpicStore);
            return order;
        }

        private static void AddSourceForName(List<string> order, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (value.IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUnique(order, SourceSteamOfficial);
            }
            else if (value.IndexOf("xbox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     value.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUnique(order, SourceXboxStore);
            }
            else if (value.IndexOf("playstation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     value.IndexOf("psn", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUnique(order, SourcePsnStore);
            }
            else if (value.IndexOf("epic", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUnique(order, SourceEpicStore);
            }
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (!list.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(value);
            }
        }

        private async Task<OfficialStoreMetadata> GetSteamMetadataAsync(Game game, CancellationToken cancelToken)
        {
            var appId = await ResolveSteamAppIdAsync(game, cancelToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(appId))
            {
                return null;
            }

            var url = "https://store.steampowered.com/api/appdetails?appids=" + Uri.EscapeDataString(appId) +
                      "&l=" + Uri.EscapeDataString(GetStoreLanguage()) +
                      "&cc=" + Uri.EscapeDataString(GetCountryCode());
            var json = await GetJsonAsync(url, cancelToken).ConfigureAwait(false);
            var data = json[appId] == null ? null : json[appId]["data"] as JObject;
            if (data == null)
            {
                return null;
            }

            return new OfficialStoreMetadata
            {
                SourceName = SourceSteamOfficial,
                StoreUrl = "https://store.steampowered.com/app/" + appId,
                Title = CleanText((string)data["name"]),
                Description = CleanHtml((string)data["detailed_description"] ?? (string)data["short_description"]),
                Genres = ReadNameArray(data["genres"]),
                Developers = ReadStringArray(data["developers"]),
                Publishers = ReadStringArray(data["publishers"]),
                ReleaseDate = data["release_date"] == null ? string.Empty : NormalizeReleaseDate((string)data["release_date"]["date"])
            };
        }

        private async Task<List<OfficialMediaCandidate>> GetPsnMediaCandidatesAsync(Game game, MediaKind kind, CancellationToken cancelToken)
        {
            var metadata = await GetPsnMetadataAsync(game, cancelToken).ConfigureAwait(false);
            return metadata == null || string.IsNullOrWhiteSpace(metadata.StoreUrl)
                ? new List<OfficialMediaCandidate>()
                : await GetPsnMediaCandidatesFromUrlAsync(metadata.StoreUrl, kind, cancelToken).ConfigureAwait(false);
        }

        private async Task<OfficialStoreMetadata> GetPsnMetadataAsync(Game game, CancellationToken cancelToken)
        {
            var result = await ResolvePsnStoreUrlAsync(game, cancelToken).ConfigureAwait(false);
            if (result == null || string.IsNullOrWhiteSpace(result.Url))
            {
                return null;
            }

            var html = await GetStringAsync(result.Url, cancelToken).ConfigureAwait(false);
            return new OfficialStoreMetadata
            {
                SourceName = SourcePsnStore,
                StoreUrl = result.Url,
                Title = result.Title,
                Description = CleanText(GetMetaContent(html, "description"))
            };
        }

        private async Task<List<OfficialMediaCandidate>> GetPsnMediaCandidatesFromUrlAsync(string url, MediaKind kind, CancellationToken cancelToken)
        {
            var html = await GetStringAsync(url, cancelToken).ConfigureAwait(false);
            var roleCandidates = GetPsnRoleCandidates(html, kind);
            if (roleCandidates.Count > 0)
            {
                return roleCandidates;
            }

            var urls = Regex.Matches(html ?? string.Empty, "https://image\\.api\\.playstation\\.com/[^\\\"'<>\\\\]+")
                .Cast<Match>()
                .Select(x => WebUtility.HtmlDecode(x.Value).Split('?')[0])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (kind == MediaKind.Cover || kind == MediaKind.Icon)
            {
                return urls
                    .Where(x => x.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                    .Take(6)
                    .Select(x => CreateOfficialCandidate(x, kind == MediaKind.Icon ? 512 : 1200, kind == MediaKind.Icon ? 512 : 1200, kind == MediaKind.Icon ? "store icon/cover" : "store cover", 76, SourcePsnStore, 64, true))
                    .ToList();
            }

            return urls
                .Where(x => x.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .Skip(1)
                .Take(12)
                .Select(x => CreateOfficialCandidate(x, 1920, 1080, "store artwork", 66, SourcePsnStore, 60, true))
                .ToList();
        }

        private static List<OfficialMediaCandidate> GetPsnRoleCandidates(string html, MediaKind kind)
        {
            var candidates = new List<OfficialMediaCandidate>();
            var mediaObjects = Regex.Matches(html ?? string.Empty, "\\{\\\"__typename\\\":\\\"Media\\\"[^{}]*\\}")
                .Cast<Match>()
                .Select(x => ParsePsnMediaObject(x.Value))
                .Where(x => x != null)
                .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.OrderByDescending(y => PsnRolePriority(y.Role, kind)).First())
                .Where(x => PsnRolePriority(x.Role, kind) > 0)
                .OrderByDescending(x => PsnRolePriority(x.Role, kind))
                .ToList();

            foreach (var media in mediaObjects)
            {
                var role = (media.Role ?? string.Empty).ToUpperInvariant();
                var score = PsnRolePriority(role, kind);
                var style = GetPsnRoleStyle(role, kind);
                candidates.Add(CreateOfficialCandidate(media.Url, 0, 0, style, score, SourcePsnStore, 64, true));
            }

            return candidates;
        }

        private static PsnMediaObject ParsePsnMediaObject(string json)
        {
            try
            {
                var media = JObject.Parse(json);
                var type = (string)media["type"];
                var role = (string)media["role"];
                var url = WebUtility.HtmlDecode((string)media["url"] ?? string.Empty).Split('?')[0];
                if (!string.Equals(type, "IMAGE", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(role) ||
                    string.IsNullOrWhiteSpace(url) ||
                    url.IndexOf("image.api.playstation.com", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return null;
                }

                return new PsnMediaObject { Role = role, Url = url };
            }
            catch
            {
                return null;
            }
        }

        private static int PsnRolePriority(string role, MediaKind kind)
        {
            role = (role ?? string.Empty).ToUpperInvariant();
            if (kind == MediaKind.Cover || kind == MediaKind.Icon)
            {
                if (role == "MASTER") return 100;
                if (role == "GAMEHUB_COVER_ART") return 96;
                if (role == "PORTRAIT_BANNER") return 84;
                if (role == "FOUR_BY_THREE_BANNER") return 60;
                return 0;
            }

            if (role == "GAMEHUB_COVER_ART") return 104;
            if (role == "BACKGROUND") return 100;
            if (role == "BACKGROUND_LAYER_ART") return 92;
            if (role == "SCREENSHOT") return 76;
            if (role == "FOUR_BY_THREE_BANNER") return 58;
            return 0;
        }

        private static string GetPsnRoleStyle(string role, MediaKind kind)
        {
            role = (role ?? string.Empty).ToUpperInvariant();
            if (kind == MediaKind.Background)
            {
                if (role == "GAMEHUB_COVER_ART") return "official game hub background no_logo";
                if (role == "BACKGROUND") return "official background no_logo";
                if (role == "BACKGROUND_LAYER_ART") return "official layered background";
                if (role == "SCREENSHOT") return "official screenshot no_logo";
                return "official banner";
            }

            if (role == "MASTER") return "official cover";
            if (role == "GAMEHUB_COVER_ART") return "official game hub cover";
            if (role == "PORTRAIT_BANNER") return "official portrait banner";
            return "official banner";
        }

        private async Task<StoreSearchMatch> ResolvePsnStoreUrlAsync(Game game, CancellationToken cancelToken)
        {
            var direct = GetFirstLink(game, "store.playstation.com");
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return new StoreSearchMatch { Url = direct, Title = game == null ? null : game.Name };
            }

            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return null;
            }

            var culture = GetPsnCulture();
            foreach (var title in BuildTitleAliases(game.Name))
            {
                var html = await GetStringAsync("https://store.playstation.com/" + culture + "/search/" + Uri.EscapeDataString(title), cancelToken).ConfigureAwait(false);
                var matches = Regex.Matches(html ?? string.Empty, "data-telemetry-meta=\\\"(?<meta>[^\\\"]+)\\\"(?:(?!data-telemetry-meta).)*?href=\\\"(?<href>[^\\\"]+)\\\"", RegexOptions.Singleline)
                    .Cast<Match>()
                    .Select(x => CreatePsnMatch(x.Groups["meta"].Value, x.Groups["href"].Value))
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url))
                    .ToList();
                var selected = PickBestMatch(title, matches);
                if (selected != null)
                {
                    return selected;
                }
            }

            return null;
        }

        private static StoreSearchMatch CreatePsnMatch(string encodedMeta, string href)
        {
            try
            {
                var json = WebUtility.HtmlDecode(encodedMeta);
                var meta = JObject.Parse(json);
                var title = (string)meta["name"];
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href))
                {
                    return null;
                }

                return new StoreSearchMatch
                {
                    Title = title,
                    Url = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? href
                        : "https://store.playstation.com" + href
                };
            }
            catch
            {
                return null;
            }
        }

        private async Task<List<OfficialMediaCandidate>> GetXboxMediaCandidatesAsync(Game game, MediaKind kind, CancellationToken cancelToken)
        {
            var product = await GetXboxProductSummaryAsync(game, cancelToken).ConfigureAwait(false);
            if (product == null)
            {
                return new List<OfficialMediaCandidate>();
            }

            var result = new List<OfficialMediaCandidate>();
            var images = product["images"] as JObject;
            if (images == null)
            {
                return result;
            }

            if (kind == MediaKind.Cover)
            {
                AddXboxImage(result, images["poster"], "poster", 82);
                AddXboxImage(result, images["boxArt"], "box art", 76);
            }
            else if (kind == MediaKind.Icon)
            {
                AddXboxImage(result, images["boxArt"], "box art icon", 72);
                AddXboxImage(result, images["poster"], "poster icon", 62);
            }
            else
            {
                AddXboxImage(result, images["superHeroArt"], "super hero art", 82);
                foreach (var shot in (images["screenshots"] as JArray ?? new JArray()).OfType<JObject>().Take(12))
                {
                    AddXboxImage(result, shot, "screenshot", 62);
                }
            }

            return result;
        }

        private async Task<OfficialStoreMetadata> GetXboxMetadataAsync(Game game, CancellationToken cancelToken)
        {
            var product = await GetXboxProductSummaryAsync(game, cancelToken).ConfigureAwait(false);
            if (product == null)
            {
                return null;
            }

            var rating = product["contentRating"] as JObject;
            var board = rating == null ? null : (string)rating["boardName"];
            var value = rating == null ? null : (string)rating["rating"];
            var capabilities = product["capabilities"] as JObject;
            return new OfficialStoreMetadata
            {
                SourceName = SourceXboxStore,
                StoreUrl = (string)product["_metadataAiUrl"],
                Title = CleanText((string)product["title"]),
                Description = CleanText((string)product["description"] ?? (string)product["shortDescription"]),
                Genres = ReadStringArray(product["categories"]),
                Features = capabilities == null ? new List<string>() : ReadStringArray(capabilities.Properties().Select(x => x.Value)),
                Developers = SplitCompanies((string)product["developerName"]),
                Publishers = SplitCompanies((string)product["publisherName"]),
                AgeRating = CombineAgeRating(board, value),
                ReleaseDate = NormalizeReleaseDate((string)product["releaseDate"] ?? (string)product["originalReleaseDate"])
            };
        }

        private async Task<JObject> GetXboxProductSummaryAsync(Game game, CancellationToken cancelToken)
        {
            var match = await ResolveXboxStoreMatchAsync(game, cancelToken).ConfigureAwait(false);
            if (match == null || string.IsNullOrWhiteSpace(match.Url) || string.IsNullOrWhiteSpace(match.Id))
            {
                return null;
            }

            var html = await GetStringAsync(match.Url, cancelToken).ConfigureAwait(false);
            var stateJson = ExtractJavaScriptObject(html, "window.__PRELOADED_STATE__");
            if (string.IsNullOrWhiteSpace(stateJson))
            {
                return null;
            }

            var root = JObject.Parse(stateJson);
            var summary = root["core2"] == null || root["core2"]["products"] == null || root["core2"]["products"]["productSummaries"] == null
                ? null
                : root["core2"]["products"]["productSummaries"][match.Id] as JObject;
            if (summary != null)
            {
                summary["_metadataAiUrl"] = match.Url;
            }

            return summary;
        }

        private async Task<StoreSearchMatch> ResolveXboxStoreMatchAsync(Game game, CancellationToken cancelToken)
        {
            var direct = GetFirstLink(game, "xbox.com", "microsoft.com");
            if (!string.IsNullOrWhiteSpace(direct))
            {
                var id = ExtractXboxProductId(direct);
                return string.IsNullOrWhiteSpace(id) ? null : new StoreSearchMatch { Url = direct, Id = id, Title = game == null ? null : game.Name };
            }

            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return null;
            }

            var market = GetXboxMarket();
            foreach (var title in BuildTitleAliases(game.Name))
            {
                var url = "https://www.microsoft.com/msstoreapiprod/api/autosuggest?market=" + Uri.EscapeDataString(market) +
                          "&sources=DCatAll-Products,xSearch-Products&filter=+ClientType:StoreWeb&counts=20,20&query=" + Uri.EscapeDataString(title);
                var json = await GetJsonAsync(url, cancelToken).ConfigureAwait(false);
                var matches = (json["ResultSets"] as JArray ?? new JArray())
                    .OfType<JObject>()
                    .Where(x => string.Equals((string)x["Type"], "product", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(x => (x["Suggests"] as JArray ?? new JArray()).OfType<JObject>())
                    .Where(x => string.Equals((string)x["Source"], "Game", StringComparison.OrdinalIgnoreCase))
                    .Select(CreateXboxMatch)
                    .Where(x => x != null)
                    .ToList();
                var selected = PickBestMatch(title, matches);
                if (selected != null)
                {
                    return selected;
                }
            }

            return null;
        }

        private static StoreSearchMatch CreateXboxMatch(JObject item)
        {
            var title = (string)item["Title"];
            var url = (string)item["Url"];
            var metas = item["Metas"] as JArray;
            var id = metas == null ? null : metas.OfType<JObject>().Where(x => string.Equals((string)x["Key"], "BigCatalogId", StringComparison.OrdinalIgnoreCase)).Select(x => (string)x["Value"]).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            return new StoreSearchMatch
            {
                Title = title,
                Id = id,
                Url = MakeAbsoluteUrl(url, "https://www.xbox.com")
            };
        }

        private async Task<List<OfficialMediaCandidate>> GetEpicMediaCandidatesAsync(Game game, MediaKind kind, CancellationToken cancelToken)
        {
            var metadata = await GetEpicMetadataAsync(game, cancelToken).ConfigureAwait(false);
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.StoreUrl))
            {
                return new List<OfficialMediaCandidate>();
            }

            try
            {
                var html = await GetStringAsync(metadata.StoreUrl, cancelToken).ConfigureAwait(false);
                if (LooksLikeCloudflareChallenge(html))
                {
                    return new List<OfficialMediaCandidate>();
                }

                var urls = Regex.Matches(html, "https://[^\\\"'<>\\\\]+(?:epicgames|akamai|cloudfront)[^\\\"'<>\\\\]+\\.(?:jpg|jpeg|png|webp)[^\\\"'<>\\\\]*", RegexOptions.IgnoreCase)
                    .Cast<Match>()
                    .Select(x => WebUtility.HtmlDecode(x.Value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return urls
                    .Take(kind == MediaKind.Background ? 12 : 6)
                    .Select(x => CreateOfficialCandidate(x, kind == MediaKind.Background ? 1920 : 1200, kind == MediaKind.Background ? 1080 : 1200, kind == MediaKind.Background ? "store artwork" : "store image", 54, SourceEpicStore, 42, true))
                    .ToList();
            }
            catch
            {
                return new List<OfficialMediaCandidate>();
            }
        }

        private async Task<OfficialStoreMetadata> GetEpicMetadataAsync(Game game, CancellationToken cancelToken)
        {
            var direct = GetFirstLink(game, "store.epicgames.com", "epicgames.com/store");
            if (string.IsNullOrWhiteSpace(direct))
            {
                return null;
            }

            var html = await GetStringAsync(direct, cancelToken).ConfigureAwait(false);
            if (LooksLikeCloudflareChallenge(html))
            {
                return null;
            }

            return new OfficialStoreMetadata
            {
                SourceName = SourceEpicStore,
                StoreUrl = direct,
                Title = CleanText(GetMetaContent(html, "og:title")),
                Description = CleanText(GetMetaContent(html, "description") ?? GetMetaContent(html, "og:description"))
            };
        }

        private static void AddXboxImage(List<OfficialMediaCandidate> result, JToken token, string style, int score)
        {
            var image = token as JObject;
            if (image == null)
            {
                return;
            }

            var url = (string)image["url"];
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            result.Add(CreateOfficialCandidate(
                url,
                (int?)image["width"] ?? 0,
                (int?)image["height"] ?? 0,
                style,
                score,
                SourceXboxStore,
                66,
                true));
        }

        private static OfficialMediaCandidate CreateOfficialCandidate(string url, int width, int height, string style, int score, string sourceName, int sourcePriority, bool official)
        {
            var extension = ExtensionFromUrl(url);
            return new OfficialMediaCandidate
            {
                Url = url,
                Width = width,
                Height = height,
                Style = style,
                Score = score,
                SourceName = sourceName,
                IsOfficial = official,
                Extension = extension,
                Mime = extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg"
            };
        }

        private async Task<string> ResolveSteamAppIdAsync(Game game, CancellationToken cancelToken)
        {
            if (game != null && game.Source != null &&
                game.Source.Name.IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0 &&
                !string.IsNullOrWhiteSpace(game.GameId))
            {
                return game.GameId;
            }

            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return null;
            }

            foreach (var title in BuildTitleAliases(game.Name))
            {
                var url = "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(title) + "&cc=" + Uri.EscapeDataString(GetCountryCode()) + "&l=" + Uri.EscapeDataString(GetStoreLanguage());
                var json = await GetJsonAsync(url, cancelToken).ConfigureAwait(false);
                var items = json["items"] as JArray;
                var matches = (items ?? new JArray())
                    .OfType<JObject>()
                    .Select(x => new StoreSearchMatch { Id = ((int?)x["id"] ?? 0).ToString(), Title = (string)x["name"] })
                    .Where(x => x.Id != "0")
                    .ToList();
                var selected = PickBestMatch(title, matches);
                if (selected != null)
                {
                    return selected.Id;
                }
            }

            return null;
        }

        private static StoreSearchMatch PickBestMatch(string gameName, List<StoreSearchMatch> matches)
        {
            if (matches == null || matches.Count == 0)
            {
                return null;
            }

            return matches.FirstOrDefault(x => IsReliableStoreTitleMatch(gameName, x.Title));
        }

        private static bool IsReliableStoreTitleMatch(string expected, string candidate)
        {
            return TitleMatchingService.IsReliableMatch(expected, candidate);
        }

        private static List<string> BuildTitleAliases(string value)
        {
            return TitleMatchingService.BuildAliases(value);
        }

        private static List<string> GetGameLinks(Game game)
        {
            return game == null || game.Links == null
                ? new List<string>()
                : game.Links.Select(x => (x.Name ?? string.Empty) + " " + (x.Url ?? string.Empty)).ToList();
        }

        private static string GetFirstLink(Game game, params string[] contains)
        {
            if (game == null || game.Links == null)
            {
                return null;
            }

            foreach (var link in game.Links)
            {
                var url = link == null ? null : link.Url;
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (contains.Any(x => url.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return url;
                }
            }

            return null;
        }

        private string GetStoreLanguage()
        {
            var language = settings == null ? "en" : settings.Language ?? "en";
            return language.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "en";
        }

        private string GetCountryCode()
        {
            var language = settings == null ? "en" : settings.Language ?? "en";
            var parts = language.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 ? parts[1].ToLowerInvariant() : "us";
        }

        private string GetPsnCulture()
        {
            var language = settings == null ? "en" : settings.Language ?? "en";
            var parts = language.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var lang = parts.Length > 0 ? parts[0].ToLowerInvariant() : "en";
            var country = parts.Length > 1 ? parts[1].ToLowerInvariant() : (lang == "es" ? "es" : "us");
            return lang + "-" + country;
        }

        private string GetXboxMarket()
        {
            var language = settings == null ? "en" : settings.Language ?? "en";
            var parts = language.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var lang = parts.Length > 0 ? parts[0].ToLowerInvariant() : "en";
            var country = parts.Length > 1 ? parts[1].ToLowerInvariant() : (lang == "es" ? "es" : "us");
            return lang + "-" + country;
        }

        private static string GetMetaContent(string html, string nameOrProperty)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(nameOrProperty))
            {
                return null;
            }

            var pattern = "<meta\\s+(?:name|property)=\\\"" + Regex.Escape(nameOrProperty) + "\\\"\\s+content=\\\"(?<value>[^\\\"]*)\\\"";
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                pattern = "<meta\\s+content=\\\"(?<value>[^\\\"]*)\\\"\\s+(?:name|property)=\\\"" + Regex.Escape(nameOrProperty) + "\\\"";
                match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
            }

            return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
        }

        private static string CleanHtml(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = Regex.Replace(value, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ");
            return CleanText(text);
        }

        private static string CleanText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var decoded = WebUtility.HtmlDecode(value);
            decoded = Regex.Replace(decoded, "\\s+", " ").Trim();
            return decoded;
        }

        private static string NormalizeReleaseDate(string value)
        {
            var text = CleanText(value);
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            DateTime parsed;
            foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("es-ES"), CultureInfo.CurrentCulture })
            {
                if (DateTime.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out parsed))
                    return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            var year = Regex.Match(text, @"\b(19|20)\d{2}\b");
            return year.Success ? year.Value : string.Empty;
        }

        private static List<string> ReadNameArray(JToken token)
        {
            return (token as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(x => CleanText((string)x["description"] ?? (string)x["name"]))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> ReadStringArray(JToken token)
        {
            return (token as JArray ?? new JArray())
                .Select(x => CleanText((string)x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> ReadStringArray(IEnumerable<JToken> tokens)
        {
            return (tokens ?? new List<JToken>())
                .Select(x => CleanText((string)x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> SplitCompanies(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { '/', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanText)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string MakeAbsoluteUrl(string url, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (url.StartsWith("//"))
            {
                return "https:" + url;
            }

            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            return new Uri(new Uri(baseUrl), url).ToString();
        }

        private static string ExtractXboxProductId(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var match = Regex.Match(url, "/([0-9a-z]{12})(?:[/?#]|$)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }

        private static string CombineAgeRating(string board, string value)
        {
            board = CleanText(board);
            value = CleanText(value);
            if (string.IsNullOrWhiteSpace(board) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.StartsWith(board, StringComparison.OrdinalIgnoreCase) ? value : board + " " + value;
        }

        // Xbox embeds the product payload in a JavaScript assignment followed by more scripts.
        // A greedy regular expression can therefore consume subsequent JavaScript and make the
        // JSON invalid. Read the balanced object instead, respecting JSON string escaping.
        private static string ExtractJavaScriptObject(string html, string assignmentName)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(assignmentName))
            {
                return null;
            }

            var assignmentIndex = html.IndexOf(assignmentName, StringComparison.Ordinal);
            if (assignmentIndex < 0)
            {
                return null;
            }

            var objectStart = html.IndexOf('{', assignmentIndex + assignmentName.Length);
            if (objectStart < 0)
            {
                return null;
            }

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var index = objectStart; index < html.Length; index++)
            {
                var character = html[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                }
                else if (character == '{')
                {
                    depth++;
                }
                else if (character == '}' && --depth == 0)
                {
                    return html.Substring(objectStart, index - objectStart + 1);
                }
            }

            return null;
        }

        private static bool LooksLikeCloudflareChallenge(string html)
        {
            return !string.IsNullOrWhiteSpace(html) &&
                   html.IndexOf("cf_challenge", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   html.IndexOf("Enable JavaScript and cookies", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static async Task<JObject> GetJsonAsync(string url, CancellationToken cancelToken)
        {
            var text = await GetStringAsync(url, cancelToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken cancelToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.UserAgent.ParseAdd("MetaDataIAPlugin/1.0");
                using (var response = await Client.SendAsync(request, cancelToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return string.Empty;
                    }

                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        private static string ExtensionFromUrl(string url)
        {
            try
            {
                var path = new Uri(url).AbsolutePath;
                var ext = System.IO.Path.GetExtension(path);
                return string.IsNullOrWhiteSpace(ext) ? ".jpg" : ext;
            }
            catch
            {
                return ".jpg";
            }
        }

        private class StoreSearchMatch
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Url { get; set; }
        }

        private class PsnMediaObject
        {
            public string Role { get; set; }
            public string Url { get; set; }
        }
    }
}
