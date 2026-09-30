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
        public List<string> Tags { get; set; }
        public bool ListsMatchPluginLanguage { get; set; }
        public List<string> Developers { get; set; }
        public List<string> Publishers { get; set; }
        public List<string> Regions { get; set; }
        public List<Link> Links { get; set; }
        public string AgeRating { get; set; }
        public string ReleaseDate { get; set; }
        public List<string> Series { get; set; }
        public string MinimumSystemRequirements { get; set; }
        public string RecommendedSystemRequirements { get; set; }
        public bool IsExactMatch { get; set; }

        public OfficialStoreMetadata()
        {
            Genres = new List<string>();
            Features = new List<string>();
            Tags = new List<string>();
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
                   (Tags != null && Tags.Count > 0) ||
                   Developers.Count > 0 ||
                   Publishers.Count > 0 ||
                   Regions.Count > 0 ||
                   Links.Count > 0 ||
                   !string.IsNullOrWhiteSpace(AgeRating) ||
                   !string.IsNullOrWhiteSpace(ReleaseDate) ||
                   Series.Count > 0 ||
                   !string.IsNullOrWhiteSpace(MinimumSystemRequirements) ||
                   !string.IsNullOrWhiteSpace(RecommendedSystemRequirements);
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
        public const string SourceEsrb = "ESRB";

        private static readonly HttpClient Client = CreateClient();
        private readonly MetaDataIASettings settings;
        private StoreSearchMatch psnMatchCache;
        private string psnMatchCacheKey;
        private JObject epicProductCache;
        private string epicProductCacheKey;

        // Persisted GraphQL query used by the public PlayStation Store storefront (same approach as Universal PSN Metadata).
        private const string PsnGraphqlSearchUrl = "https://web.np.playstation.com/api/graphql/v1//op";
        private const string PsnSearchQueryHash = "4df6284f982e57bec70f23c77e2c219dc792eb19af7fb3d3a81767aa3f1958aa";
        private const string PsnStoreApplicationName = "@sie-ppr-web-store/app";
        private const string PsnStoreApplicationVersion = "0.113.0";
        private const string PsnBrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36";

        private static HttpClient CreateClient()
        {
            return new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        static OfficialStoreDataService()
        {
            try
            {
                // Explicit numeric flags keep TLS 1.2 available on older .NET 4.x hosts.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
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
            List<OfficialStoreMetadata> cached;
            if (OfficialStoreContextCache.TryGetOfficial(game, GetStoreLanguage(), out cached))
            {
                var playModeNeedsGenres = game != null &&
                    TitleMatchingService.CanUsePlayModeBaseTitle(game.Name) &&
                    (cached == null || !cached.Any(x => x != null && x.Genres != null && x.Genres.Count > 0));
                if (!playModeNeedsGenres)
                {
                    return cached;
                }
            }

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
                    if (cancelToken.IsCancellationRequested)
                    {
                        throw;
                    }
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

            OfficialStoreContextCache.SetOfficial(game, GetStoreLanguage(), result);
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
                if (cancelToken.IsCancellationRequested)
                {
                    throw;
                }
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

            if (string.Equals(source, SourceEsrb, StringComparison.OrdinalIgnoreCase))
            {
                return await GetEsrbMetadataAsync(game, cancelToken).ConfigureAwait(false);
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

            var allowed = GetAllowedOfficialSources(game);
            foreach (var source in new[]
                     {
                         SourceSteamOfficial,
                         SourceXboxStore,
                         SourcePsnStore,
                         SourceEpicStore,
                         SourceEsrb
                     })
            {
                if (allowed.Contains(source))
                {
                    AddUnique(order, source);
                }
            }

            // Drop preferred sources that do not fit this game's platform family
            // (e.g. an xbox.com link on an Epic PC game must not inject console capabilities).
            // Then honour per-storefront metadata toggles from Fuentes.
            return order.Where(allowed.Contains).Where(IsStoreMetadataEnabled).ToList();
        }

        private bool IsStoreMetadataEnabled(string source)
        {
            if (settings == null || string.IsNullOrWhiteSpace(source))
            {
                return true;
            }

            if (string.Equals(source, SourceSteamOfficial, StringComparison.OrdinalIgnoreCase))
            {
                return settings.UseSteamMetadata;
            }

            if (string.Equals(source, SourcePsnStore, StringComparison.OrdinalIgnoreCase))
            {
                return settings.UsePsnStoreMetadata;
            }

            if (string.Equals(source, SourceXboxStore, StringComparison.OrdinalIgnoreCase))
            {
                return settings.UseXboxStoreMetadata;
            }

            if (string.Equals(source, SourceEpicStore, StringComparison.OrdinalIgnoreCase))
            {
                return settings.UseEpicStoreMetadata;
            }

            return true;
        }

        private static HashSet<string> GetAllowedOfficialSources(Game game)
        {
            var families = DetectPlatformFamilies(game);
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SourceEsrb };
            var sourceName = game == null || game.Source == null ? string.Empty : game.Source.Name ?? string.Empty;

            if (families.Count == 0)
            {
                // A NES/ROM platform is known, it is just not a desktop storefront.
                // Leave Steam, Epic, Xbox and PlayStation out so a PC remake is not
                // applied to the cartridge. No platform at all keeps the broad search.
                if (!HasOnlyNonDesktopPlatforms(game))
                {
                    allowed.Add(SourceSteamOfficial);
                    allowed.Add(SourceXboxStore);
                    allowed.Add(SourcePsnStore);
                    allowed.Add(SourceEpicStore);
                }

                return allowed;
            }

            if (families.Contains(PlatformFamily.Pc))
            {
                allowed.Add(SourceSteamOfficial);
                allowed.Add(SourceEpicStore);
                // Xbox Store only for Xbox/Microsoft library (Game Pass / MS Store PC),
                // or when the game also has an Xbox console platform.
                if (families.Contains(PlatformFamily.Xbox) || IsXboxLibrarySource(sourceName))
                {
                    allowed.Add(SourceXboxStore);
                }
            }

            if (families.Contains(PlatformFamily.Xbox))
            {
                allowed.Add(SourceXboxStore);
            }

            if (families.Contains(PlatformFamily.PlayStation))
            {
                allowed.Add(SourcePsnStore);
            }

            return allowed;
        }

        private enum PlatformFamily
        {
            Pc,
            Xbox,
            PlayStation
        }

        private static HashSet<PlatformFamily> DetectPlatformFamilies(Game game)
        {
            var families = new HashSet<PlatformFamily>();
            if (game != null && game.Platforms != null)
            {
                foreach (var platform in game.Platforms)
                {
                    var name = platform == null ? string.Empty : platform.Name ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var fromSpec = FamilyFromSpecificationId(platform.SpecificationId);
                    if (fromSpec.HasValue)
                    {
                        families.Add(fromSpec.Value);
                        continue;
                    }

                    // Custom platforms have no SpecificationId. Name matching is only the fallback.
                    if (IsPlayStationPlatformName(name))
                    {
                        families.Add(PlatformFamily.PlayStation);
                    }
                    else if (IsXboxPlatformName(name))
                    {
                        families.Add(PlatformFamily.Xbox);
                    }
                    else if (IsPcPlatformName(name))
                    {
                        families.Add(PlatformFamily.Pc);
                    }
                }
            }

            var sourceName = game == null || game.Source == null ? string.Empty : game.Source.Name ?? string.Empty;
            if (IsPcLibrarySource(sourceName))
            {
                families.Add(PlatformFamily.Pc);
            }
            else if (IsXboxLibrarySource(sourceName) && families.Count == 0)
            {
                // Xbox library with no platforms yet: treat as Xbox family (PC Game Pass
                // still gets Xbox Store; console-only capability filtering happens later).
                families.Add(PlatformFamily.Xbox);
            }
            else if (IsPlayStationLibrarySource(sourceName) && families.Count == 0)
            {
                families.Add(PlatformFamily.PlayStation);
            }

            return families;
        }

        private static PlatformFamily? FamilyFromSpecificationId(string specificationId)
        {
            if (string.IsNullOrWhiteSpace(specificationId))
            {
                return null;
            }

            var id = specificationId.Trim().ToLowerInvariant();
            if (id == "pc_windows" || id == "macintosh" || id == "linux" || id.StartsWith("pc_", StringComparison.Ordinal))
            {
                return PlatformFamily.Pc;
            }

            if (id.StartsWith("xbox", StringComparison.Ordinal))
            {
                return PlatformFamily.Xbox;
            }

            if (id.StartsWith("playstation", StringComparison.Ordinal) ||
                id.StartsWith("ps_", StringComparison.Ordinal) ||
                id == "psp" ||
                id == "psvita")
            {
                return PlatformFamily.PlayStation;
            }

            return null;
        }

        // Fallback when Platform.SpecificationId is empty (custom or legacy platforms).
        private static bool IsPcPlatformName(string name)
        {
            var value = (name ?? string.Empty).ToLowerInvariant();
            return value.Contains("windows") ||
                   value == "pc" ||
                   value.Contains("pc (") ||
                   value.Contains("linux") ||
                   value.Contains("mac") ||
                   value.Contains("steam deck") ||
                   value.Contains("steamdeck");
        }

        // Fallback when Platform.SpecificationId is empty (custom or legacy platforms).
        private static bool IsXboxPlatformName(string name)
        {
            var value = (name ?? string.Empty).ToLowerInvariant();
            return value.Contains("xbox");
        }

        // Fallback when Platform.SpecificationId is empty (custom or legacy platforms).
        private static bool IsPlayStationPlatformName(string name)
        {
            var value = (name ?? string.Empty).ToLowerInvariant();
            return value.Contains("playstation") ||
                   value.Contains("ps5") ||
                   value.Contains("ps4") ||
                   value.Contains("ps3") ||
                   value.Contains("ps vita") ||
                   value.Contains("psp");
        }

        // Fallback when the game has no platform family yet (no platforms, or none recognized).
        private static bool IsPcLibrarySource(string sourceName)
        {
            var value = (sourceName ?? string.Empty).ToLowerInvariant();
            return value.Contains("steam") ||
                   value.Contains("epic") ||
                   value.Contains("gog") ||
                   value.Contains("battle.net") ||
                   value.Contains("battlenet") ||
                   value.Contains("origin") ||
                   value.Contains("ea app") ||
                   value.Contains("ubisoft") ||
                   value.Contains("uplay") ||
                   value.Contains("itch") ||
                   value.Contains("amazon") ||
                   value.Contains("humble");
        }

        // Fallback when no platform family was detected. Also keeps Xbox Store for an Xbox/Microsoft PC library.
        private static bool IsXboxLibrarySource(string sourceName)
        {
            var value = (sourceName ?? string.Empty).ToLowerInvariant();
            return value.Contains("xbox") || value.Contains("microsoft");
        }

        // Fallback when the game has no platform family yet (no platforms, or none recognized).
        private static bool IsPlayStationLibrarySource(string sourceName)
        {
            var value = (sourceName ?? string.Empty).ToLowerInvariant();
            return value.Contains("playstation") || value.Contains("psn");
        }

        public static bool HasOnlyNonDesktopPlatforms(Game game)
        {
            if (DetectPlatformFamilies(game).Count > 0)
            {
                return false;
            }

            return game != null && game.Platforms != null && game.Platforms.Any(platform =>
                platform != null &&
                (!string.IsNullOrWhiteSpace(platform.Name) || !string.IsNullOrWhiteSpace(platform.SpecificationId)));
        }

        internal static bool IsPcOrientedGame(Game game)
        {
            return DetectPlatformFamilies(game).Contains(PlatformFamily.Pc);
        }

        /// <summary>
        /// Xbox product capabilities include console marketing labels (Smart Delivery, Series X|S, …).
        /// Keep those only when the game is treated as an Xbox console title.
        /// </summary>
        internal static bool IsConsoleOnlyXboxFeature(string feature)
        {
            if (string.IsNullOrWhiteSpace(feature))
            {
                return false;
            }

            var value = feature.Trim().ToLowerInvariant();
            return value.Contains("smart delivery") ||
                   value.Contains("xbox series") ||
                   value.Contains("series x") ||
                   value.Contains("series s") ||
                   value.Contains("optimized for xbox") ||
                   value.Contains("console keyboard") ||
                   value.Contains("console mouse") ||
                   value.Contains("xbox cloud") ||
                   value.Contains("xcloud") ||
                   value.Contains("xbox one") ||
                   value.Contains("xbox 360") ||
                   value.Contains("kinect");
        }

        private static List<string> FilterXboxFeaturesForGame(Game game, IEnumerable<string> features)
        {
            var list = (features ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var families = DetectPlatformFamilies(game);
            // Pure console Xbox keeps marketing capabilities. Any PC family game drops them
            // (Epic/Steam/GOG PC and Xbox PC / Game Pass on Windows).
            if (!families.Contains(PlatformFamily.Pc))
            {
                return list;
            }

            return list.Where(x => !IsConsoleOnlyXboxFeature(x)).ToList();
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
            OfficialStoreMetadata cached;
            if (OfficialStoreContextCache.TryGetSteam(game, GetStoreLanguage(), out cached))
            {
                // Play-mode packages may have been cached with empty genres before
                // parent/base-title fill existed; refresh those once.
                if ((cached.Genres != null && cached.Genres.Count > 0) ||
                    game == null ||
                    !TitleMatchingService.CanUsePlayModeBaseTitle(game.Name))
                {
                    return cached;
                }
            }

            var appId = await ResolveSteamAppIdAsync(game, cancelToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(appId))
            {
                return null;
            }

            var data = await GetSteamAppDataAsync(appId, cancelToken).ConfigureAwait(false);
            if (data == null || IsNonGameSteamApp(data))
            {
                return null;
            }

            var metadata = new OfficialStoreMetadata
            {
                SourceName = SourceSteamOfficial,
                StoreUrl = "https://store.steampowered.com/app/" + appId,
                Title = CleanText(TokenText(data["name"])),
                Description = CleanHtml(TokenText(data["detailed_description"]) ?? TokenText(data["short_description"])),
                Genres = ReadNameArray(data["genres"]),
                // Steam categories are the PC feature list (Single-player, Co-op, Controller, …).
                Features = ReadNameArray(data["categories"]),
                Tags = new List<string>(),
                ListsMatchPluginLanguage = true,
                Developers = ReadStringArray(data["developers"]),
                Publishers = ReadStringArray(data["publishers"]),
                ReleaseDate = data["release_date"] == null ? string.Empty : NormalizeReleaseDate(TokenText(data["release_date"]["date"])),
                MinimumSystemRequirements = ReadPcRequirement(data["pc_requirements"], "minimum"),
                RecommendedSystemRequirements = ReadPcRequirement(data["pc_requirements"], "recommended")
            };
            try
            {
                metadata.Tags = await ReadSteamUserTagsAsync(appId, cancelToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (cancelToken.IsCancellationRequested)
                {
                    throw;
                }
            }
            catch
            {
            }

            if (metadata.Genres.Count == 0 &&
                game != null &&
                TitleMatchingService.CanUsePlayModeBaseTitle(game.Name))
            {
                try
                {
                    await TryFillSteamGenresFromRelatedAsync(metadata, data, game, appId, cancelToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        throw;
                    }
                }
                catch
                {
                }
            }

            OfficialStoreContextCache.SetSteam(game, GetStoreLanguage(), metadata);
            return metadata;
        }

        /// <summary>
        /// Multiplayer / Single Player Steam packages often omit genres. Prefer the
        /// store parent (fullgame), else a year-anchored base-title lookup.
        /// </summary>
        private async Task TryFillSteamGenresFromRelatedAsync(
            OfficialStoreMetadata metadata,
            JObject childData,
            Game game,
            string childAppId,
            CancellationToken cancelToken)
        {
            if (metadata == null)
            {
                return;
            }

            var parentId = ReadSteamFullGameAppId(childData);
            if (!string.IsNullOrWhiteSpace(parentId) &&
                !string.Equals(parentId, childAppId, StringComparison.OrdinalIgnoreCase))
            {
                var parentGenres = await ReadSteamGenresForAppAsync(parentId, cancelToken).ConfigureAwait(false);
                if (parentGenres.Count > 0)
                {
                    metadata.Genres = parentGenres;
                    return;
                }
            }

            if (game == null || !TitleMatchingService.CanUsePlayModeBaseTitle(game.Name))
            {
                return;
            }

            var baseTitle = TitleMatchingService.WithoutPlayModeSuffix(game.Name);
            string baseAppId = null;
            foreach (var alias in TitleMatchingService.BuildAliases(baseTitle))
            {
                var url = "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(alias) +
                          "&cc=" + Uri.EscapeDataString(GetCountryCode()) +
                          "&l=" + Uri.EscapeDataString(GetSteamStoreLanguage());
                var json = await GetJsonAsync(url, cancelToken).ConfigureAwait(false);
                if (json == null)
                {
                    continue;
                }

                var items = json["items"] as JArray;
                var matches = (items ?? new JArray())
                    .OfType<JObject>()
                    .Select(x => new StoreSearchMatch { Id = ((int?)x["id"] ?? 0).ToString(), Title = (string)x["name"] })
                    .Where(x => x.Id != "0" && !IsNonGameSteamSearchTitle(x.Title))
                    .ToList();
                var selected = PickBestMatch(baseTitle, matches);
                if (selected != null &&
                    !string.Equals(selected.Id, childAppId, StringComparison.OrdinalIgnoreCase))
                {
                    baseAppId = selected.Id;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(baseAppId))
            {
                return;
            }

            var baseGenres = await ReadSteamGenresForAppAsync(baseAppId, cancelToken).ConfigureAwait(false);
            if (baseGenres.Count > 0)
            {
                metadata.Genres = baseGenres;
            }
        }

        private static string ReadSteamFullGameAppId(JObject data)
        {
            if (data == null)
            {
                return null;
            }

            var fullgame = data["fullgame"] as JObject;
            if (fullgame != null)
            {
                var id = TokenText(fullgame["appid"]);
                if (!string.IsNullOrWhiteSpace(id) && Regex.IsMatch(id.Trim(), @"^\d+$"))
                {
                    return id.Trim();
                }
            }

            // Some payloads expose only a numeric fullgame / parent field.
            var raw = TokenText(data["fullgame"]);
            if (!string.IsNullOrWhiteSpace(raw) && Regex.IsMatch(raw.Trim(), @"^\d+$"))
            {
                return raw.Trim();
            }

            return null;
        }

        private async Task<List<string>> ReadSteamGenresForAppAsync(string appId, CancellationToken cancelToken)
        {
            var data = await GetSteamAppDataAsync(appId, cancelToken).ConfigureAwait(false);
            if (data == null || IsNonGameSteamApp(data))
            {
                return new List<string>();
            }

            return ReadNameArray(data["genres"]);
        }

        private async Task<JObject> GetSteamAppDataAsync(string appId, CancellationToken cancelToken)
        {
            if (string.IsNullOrWhiteSpace(appId))
            {
                return null;
            }

            var url = "https://store.steampowered.com/api/appdetails?appids=" + Uri.EscapeDataString(appId) +
                      "&l=" + Uri.EscapeDataString(GetSteamStoreLanguage()) +
                      "&cc=" + Uri.EscapeDataString(GetCountryCode());
            var json = await GetJsonAsync(url, cancelToken).ConfigureAwait(false);
            return ResolveSteamAppData(json, appId);
        }

        /// <summary>
        /// Steam's appdetails object is keyed by an id that is no longer always the requested app id.
        /// The payload still carries steam_appid for the game that was requested.
        /// </summary>
        public static JObject ResolveSteamAppData(JObject json, string appId)
        {
            var entry = FindSteamAppEntry(json, appId);
            if (entry == null)
            {
                return null;
            }

            var success = entry["success"];
            if (success != null && success.Type == JTokenType.Boolean && !(bool)success)
            {
                return null;
            }

            return entry["data"] as JObject;
        }

        private static JObject FindSteamAppEntry(JObject json, string appId)
        {
            if (json == null || string.IsNullOrWhiteSpace(appId))
            {
                return null;
            }

            var requested = appId.Trim();
            var direct = json[requested] as JObject;
            if (direct != null)
            {
                return direct;
            }

            JObject only = null;
            var count = 0;
            foreach (var property in json.Properties())
            {
                var candidate = property.Value as JObject;
                if (candidate == null)
                {
                    continue;
                }

                count++;
                only = candidate;
                var steamAppId = SteamAppIdOf(candidate);
                if (string.Equals(steamAppId, requested, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            if (count == 1 && only != null && string.IsNullOrEmpty(SteamAppIdOf(only)))
            {
                return only;
            }

            return null;
        }

        private static string SteamAppIdOf(JObject entry)
        {
            var data = entry == null ? null : entry["data"] as JObject;
            if (data == null || data["steam_appid"] == null || data["steam_appid"].Type == JTokenType.Null)
            {
                return string.Empty;
            }

            return data["steam_appid"].ToString().Trim();
        }

        private static bool IsNonGameSteamApp(JObject data)
        {
            if (data == null)
            {
                return true;
            }

            var type = ((string)data["type"] ?? string.Empty).Trim().ToLowerInvariant();
            if (type == "music" || type == "video" || type == "hardware" || type == "advertising")
            {
                return true;
            }

            var name = (string)data["name"] ?? string.Empty;
            return Regex.IsMatch(name, @"\b(soundtrack|ost|original\s+sound\s+track)\b", RegexOptions.IgnoreCase);
        }

        private async Task<List<OfficialMediaCandidate>> GetPsnMediaCandidatesAsync(Game game, MediaKind kind, CancellationToken cancelToken)
        {
            var match = await ResolvePsnStoreMatchAsync(game, cancelToken).ConfigureAwait(false);
            if (match == null || string.IsNullOrWhiteSpace(match.Url))
            {
                return new List<OfficialMediaCandidate>();
            }

            var fromSearch = GetPsnRoleCandidatesFromMedia(match.Media, kind);
            if (fromSearch.Count > 0)
            {
                return fromSearch;
            }

            return await GetPsnMediaCandidatesFromUrlAsync(match.Url, kind, cancelToken).ConfigureAwait(false);
        }

        private async Task<OfficialStoreMetadata> GetPsnMetadataAsync(Game game, CancellationToken cancelToken)
        {
            var result = await ResolvePsnStoreMatchAsync(game, cancelToken).ConfigureAwait(false);
            if (result == null || string.IsNullOrWhiteSpace(result.Url))
            {
                return null;
            }

            var html = await GetPsnStringAsync(result.Url, cancelToken).ConfigureAwait(false);
            var description = ExtractPsnDescription(html);
            var genres = ExtractPsnGenres(html);
            var publishers = SplitCompanies(ExtractPsnPublisher(html));
            var releaseDate = NormalizeReleaseDate(ExtractPsnReleaseDate(html));
            var metadata = new OfficialStoreMetadata
            {
                SourceName = SourcePsnStore,
                StoreUrl = result.Url,
                Title = string.IsNullOrWhiteSpace(result.Title) ? ExtractPsnTitle(html) : result.Title,
                Description = description,
                Genres = genres,
                Publishers = publishers,
                ReleaseDate = releaseDate,
                ListsMatchPluginLanguage = true
            };
            metadata.Links.Add(new Link("PlayStation Store", result.Url));
            return metadata.HasUsefulData() || !string.IsNullOrWhiteSpace(metadata.StoreUrl) ? metadata : null;
        }

        private async Task<List<OfficialMediaCandidate>> GetPsnMediaCandidatesFromUrlAsync(string url, MediaKind kind, CancellationToken cancelToken)
        {
            var html = await GetPsnStringAsync(url, cancelToken).ConfigureAwait(false);
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
            var mediaObjects = Regex.Matches(html ?? string.Empty, "\\{\\\"__typename\\\":\\\"Media\\\"[^{}]*\\}")
                .Cast<Match>()
                .Select(x => ParsePsnMediaObject(x.Value))
                .Where(x => x != null)
                .ToList();
            return GetPsnRoleCandidatesFromMedia(mediaObjects, kind);
        }

        private static List<OfficialMediaCandidate> GetPsnRoleCandidatesFromMedia(IEnumerable<PsnMediaObject> mediaObjects, MediaKind kind)
        {
            var candidates = (mediaObjects ?? Enumerable.Empty<PsnMediaObject>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url))
                .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.OrderByDescending(y => PsnRolePriority(y.Role, kind)).First())
                .Where(x => PsnRolePriority(x.Role, kind) > 0)
                .OrderByDescending(x => PsnRolePriority(x.Role, kind))
                .ToList();

            var result = new List<OfficialMediaCandidate>();
            foreach (var media in candidates)
            {
                var role = (media.Role ?? string.Empty).ToUpperInvariant();
                var score = PsnRolePriority(role, kind);
                var style = GetPsnRoleStyle(role, kind);
                result.Add(CreateOfficialCandidate(media.Url, 0, 0, style, score, SourcePsnStore, 64, true));
            }

            return result;
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
                if (role == "EDITION_KEY_ART") return 90;
                if (role == "PORTRAIT_BANNER") return 84;
                if (role == "FOUR_BY_THREE_BANNER") return 60;
                return 0;
            }

            if (role == "GAMEHUB_COVER_ART") return 104;
            if (role == "BACKGROUND") return 100;
            if (role == "BACKGROUND_LAYER_ART") return 92;
            if (role == "SIXTEEN_BY_NINE_BANNER") return 88;
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
                if (role == "SIXTEEN_BY_NINE_BANNER") return "official wide banner";
                if (role == "SCREENSHOT") return "official screenshot no_logo";
                return "official banner";
            }

            if (role == "MASTER") return "official cover";
            if (role == "GAMEHUB_COVER_ART") return "official game hub cover";
            if (role == "EDITION_KEY_ART") return "official edition key art";
            if (role == "PORTRAIT_BANNER") return "official portrait banner";
            return "official banner";
        }

        private async Task<StoreSearchMatch> ResolvePsnStoreMatchAsync(Game game, CancellationToken cancelToken)
        {
            var cacheKey = (game == null ? string.Empty : game.Name ?? string.Empty) + "|" + GetPsnCulture();
            if (psnMatchCache != null && string.Equals(psnMatchCacheKey, cacheKey, StringComparison.Ordinal))
            {
                return psnMatchCache;
            }

            StoreSearchMatch resolved = null;
            var direct = GetFirstLink(game, "store.playstation.com");
            if (!string.IsNullOrWhiteSpace(direct))
            {
                resolved = new StoreSearchMatch
                {
                    Url = direct,
                    Title = game == null ? null : game.Name,
                    Classification = "FULL_GAME"
                };
            }
            else if (game != null && !string.IsNullOrWhiteSpace(game.Name))
            {
                var culture = GetPsnCulture();
                foreach (var title in BuildTitleAliases(game.Name))
                {
                    var json = await GetPsnStringAsync(BuildPsnSearchUrl(title, culture), cancelToken).ConfigureAwait(false);
                    var matches = ParsePsnSearchResults(json, culture);
                    var selected = PickBestPsnMatch(title, matches);
                    if (selected != null)
                    {
                        resolved = selected;
                        break;
                    }
                }
            }

            psnMatchCacheKey = cacheKey;
            psnMatchCache = resolved;
            return resolved;
        }

        private static string BuildPsnSearchUrl(string searchTerm, string storeLocale)
        {
            string countryCode;
            string languageCode;
            SplitPsnLocale(storeLocale, out countryCode, out languageCode);
            var escapedSearchTerm = (searchTerm ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            var variables = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"countryCode\":\"{0}\",\"languageCode\":\"{1}\",\"nextCursor\":\"\",\"pageOffset\":0,\"pageSize\":24,\"searchTerm\":\"{2}\"}}",
                countryCode,
                languageCode,
                escapedSearchTerm);
            var extensions = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"persistedQuery\":{{\"version\":1,\"sha256Hash\":\"{0}\"}}}}",
                PsnSearchQueryHash);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}?operationName=getSearchResults&variables={1}&extensions={2}",
                PsnGraphqlSearchUrl,
                Uri.EscapeDataString(variables),
                Uri.EscapeDataString(extensions));
        }

        private static void SplitPsnLocale(string storeLocale, out string countryCode, out string languageCode)
        {
            var locale = (storeLocale ?? "en-us").Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            languageCode = locale.Length > 0 ? locale[0].ToLowerInvariant() : "en";
            countryCode = locale.Length > 1 ? locale[locale.Length - 1].ToUpperInvariant() : "US";
            if (languageCode == "zh" && locale.Length > 2 &&
                locale[1].Equals("hant", StringComparison.OrdinalIgnoreCase))
            {
                languageCode = "ch";
            }
        }

        private static List<StoreSearchMatch> ParsePsnSearchResults(string response, string storeLocale)
        {
            var results = new List<StoreSearchMatch>();
            if (string.IsNullOrWhiteSpace(response))
            {
                return results;
            }

            try
            {
                var root = JObject.Parse(response);
                var data = root["data"] as JObject;
                var search = data == null ? null : data["universalSearch"] as JObject;
                var items = search == null ? null : search["results"] as JArray;
                if (items == null)
                {
                    return results;
                }

                var locale = string.IsNullOrWhiteSpace(storeLocale) ? "en-us" : storeLocale.ToLowerInvariant();
                foreach (var item in items.OfType<JObject>())
                {
                    var name = (string)item["name"];
                    var id = (string)item["id"];
                    var typeName = (string)item["__typename"];
                    var classification = (string)item["storeDisplayClassification"];
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || PsnClassificationScore(classification) < 0)
                    {
                        continue;
                    }

                    var media = ParsePsnSearchMedia(item["media"] as JArray);
                    if (media.Count == 0)
                    {
                        continue;
                    }

                    var route = string.Equals(typeName, "Concept", StringComparison.OrdinalIgnoreCase) ? "concept" : "product";
                    results.Add(new StoreSearchMatch
                    {
                        Id = id,
                        Title = name,
                        Classification = classification,
                        Url = string.Format(CultureInfo.InvariantCulture, "https://store.playstation.com/{0}/{1}/{2}", locale, route, id),
                        Media = media
                    });
                }
            }
            catch
            {
            }

            return results;
        }

        private static List<PsnMediaObject> ParsePsnSearchMedia(JArray media)
        {
            var result = new List<PsnMediaObject>();
            if (media == null)
            {
                return result;
            }

            foreach (var item in media.OfType<JObject>())
            {
                var type = (string)item["type"];
                var role = (string)item["role"];
                var url = WebUtility.HtmlDecode((string)item["url"] ?? string.Empty).Split('?')[0];
                if (!string.Equals(type, "IMAGE", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(role) ||
                    string.IsNullOrWhiteSpace(url) ||
                    url.IndexOf("image.api.playstation.com", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                result.Add(new PsnMediaObject { Role = role, Url = url });
            }

            return result;
        }

        private static StoreSearchMatch PickBestPsnMatch(string gameName, List<StoreSearchMatch> matches)
        {
            if (matches == null || matches.Count == 0)
            {
                return null;
            }

            return matches
                .Where(x => IsReliableStoreTitleMatch(gameName, x.Title))
                .OrderByDescending(x => PsnClassificationScore(x.Classification))
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static int PsnClassificationScore(string classification)
        {
            switch ((classification ?? string.Empty).ToUpperInvariant())
            {
                case "FULL_GAME":
                    return 100;
                case "GAME_BUNDLE":
                    return 40;
                case "PREMIUM_EDITION":
                    return 30;
                case "ADD_ON":
                case "ADD_ON_PACK":
                case "CHARACTER":
                case "COSTUME":
                case "GAME_LEVEL":
                case "ITEM":
                case "VIRTUAL_CURRENCY":
                case "DEMO":
                    return -100;
                default:
                    return 10;
            }
        }

        private static string ExtractPsnDescription(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var overview = Regex.Match(
                html,
                "data-qa=[\"']mfe-game-overview#description[\"'][^>]*>(?<value>.*?)</(?:div|p|section)>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (overview.Success)
            {
                return CleanHtmlText(overview.Groups["value"].Value);
            }

            return CleanHtmlText(GetMetaContent(html, "description"));
        }

        private static List<string> ExtractPsnGenres(string html)
        {
            var genres = new List<string>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return genres;
            }

            var qa = Regex.Match(
                html,
                "data-qa=[\"']gameInfo#releaseInformation#genre-value[\"'][^>]*>(?<value>[^<]*)",
                RegexOptions.IgnoreCase);
            if (qa.Success)
            {
                genres.AddRange(
                    qa.Groups["value"].Value
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(CleanText)
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            if (genres.Count == 0)
            {
                foreach (var match in Regex.Matches(html, "\"localizedGenres\"\\s*:\\s*\\[(?<arr>[^\\]]*)\\]", RegexOptions.IgnoreCase).Cast<Match>())
                {
                    foreach (var value in Regex.Matches(match.Groups["arr"].Value, "\"value\"\\s*:\\s*\"(?<v>[^\"]+)\"").Cast<Match>())
                    {
                        var genre = CleanText(value.Groups["v"].Value);
                        if (!string.IsNullOrWhiteSpace(genre) &&
                            !genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
                        {
                            genres.Add(genre);
                        }
                    }

                    if (genres.Count > 0)
                    {
                        break;
                    }
                }
            }

            return genres;
        }

        private static string ExtractPsnPublisher(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var qa = Regex.Match(
                html,
                "data-qa=[\"'](?:gameInfo#releaseInformation#publisher-value|mfe-game-title#publisher)[\"'][^>]*>(?<value>[^<]*)",
                RegexOptions.IgnoreCase);
            if (qa.Success)
            {
                return CleanText(qa.Groups["value"].Value);
            }

            var json = Regex.Match(html, "\"publisherName\"\\s*:\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);
            return json.Success ? CleanText(json.Groups["value"].Value) : string.Empty;
        }

        private static string ExtractPsnReleaseDate(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var json = Regex.Match(html, "\"releaseDate\"\\s*:\\s*\"(?<value>\\d{4}-\\d{2}-\\d{2})", RegexOptions.IgnoreCase);
            if (json.Success)
            {
                return json.Groups["value"].Value;
            }

            var qa = Regex.Match(
                html,
                "data-qa=[\"']gameInfo#releaseInformation#releaseDate-value[\"'][^>]*>(?<value>[^<]*)",
                RegexOptions.IgnoreCase);
            return qa.Success ? CleanText(qa.Groups["value"].Value) : string.Empty;
        }

        private static string ExtractPsnTitle(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var og = GetMetaContent(html, "og:title");
            if (!string.IsNullOrWhiteSpace(og))
            {
                return CleanText(og);
            }

            var json = Regex.Match(html, "\"invariantName\"\\s*:\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);
            return json.Success ? CleanText(json.Groups["value"].Value) : string.Empty;
        }

        private static string CleanHtmlText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = WebUtility.HtmlDecode(value);
            text = Regex.Replace(text, "<\\s*br\\s*/?\\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ");
            text = Regex.Replace(text, "\\s+", " ").Trim();
            return text;
        }

        private async Task<string> GetPsnStringAsync(string url, CancellationToken cancelToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", PsnBrowserUserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "application/json,text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
                request.Headers.TryAddWithoutValidation("Origin", "https://store.playstation.com");
                request.Headers.TryAddWithoutValidation("Referer", "https://store.playstation.com/");
                // CSRF guard on the Store GraphQL endpoint requires a non-form content-type or Apollo op name.
                request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
                request.Headers.TryAddWithoutValidation("x-apollo-operation-name", "getSearchResults");
                request.Headers.TryAddWithoutValidation("apollographql-client-name", PsnStoreApplicationName);
                request.Headers.TryAddWithoutValidation("apollographql-client-version", PsnStoreApplicationVersion);
                request.Headers.TryAddWithoutValidation(
                    "X-PSN-App-Ver",
                    string.Format(CultureInfo.InvariantCulture, "{0}/{1}-", PsnStoreApplicationName, PsnStoreApplicationVersion));
                request.Headers.TryAddWithoutValidation("X-PSN-Correlation-ID", Guid.NewGuid().ToString());
                request.Headers.TryAddWithoutValidation("X-PSN-Request-ID", Guid.NewGuid().ToString());
                request.Headers.TryAddWithoutValidation("X-PSN-Store-Locale-Override", ToPsnLocaleHeader(GetPsnCulture()));

                try
                {
                    using (var response = await Client.SendAsync(request, cancelToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            return string.Empty;
                        }

                        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    return string.Empty;
                }
            }
        }

        private static string ToPsnLocaleHeader(string storeLocale)
        {
            var parts = (storeLocale ?? "en-us").Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = i == parts.Length - 1
                    ? parts[i].ToUpperInvariant()
                    : parts[i].ToLowerInvariant();
            }

            return string.Join("-", parts);
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
                Features = FilterXboxFeaturesForGame(
                    game,
                    capabilities == null
                        ? new List<string>()
                        : ReadStringArray(capabilities.Properties().Select(x => x.Value))),
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
            var product = await ResolveEpicProductAsync(game, cancelToken).ConfigureAwait(false);
            if (product == null)
            {
                return new List<OfficialMediaCandidate>();
            }

            return BuildEpicMediaCandidates(product, kind);
        }

        private async Task<OfficialStoreMetadata> GetEpicMetadataAsync(Game game, CancellationToken cancelToken)
        {
            var product = await ResolveEpicProductAsync(game, cancelToken).ConfigureAwait(false);
            if (product == null)
            {
                return null;
            }

            var page = GetEpicHomePage(product);
            var pageData = page == null ? null : page["data"] as JObject;
            var about = pageData == null ? null : pageData["about"] as JObject;
            var meta = pageData == null ? null : pageData["meta"] as JObject;
            var title = CleanMarkdownText(FirstNonEmpty(
                TokenText(about == null ? null : about["title"]),
                TokenText(product["productName"]),
                TokenText(product["_title"])));
            var description = CleanHtmlText(FirstNonEmpty(
                TokenText(about == null ? null : about["shortDescription"]),
                TokenText(about == null ? null : about["description"])));
            var developers = SplitCompanies(FirstNonEmpty(
                TokenText(about == null ? null : about["developerAttribution"]),
                TokenText(meta == null ? null : meta["developer"])));
            var publishers = SplitCompanies(FirstNonEmpty(
                TokenText(about == null ? null : about["publisherAttribution"]),
                TokenText(meta == null ? null : meta["publisher"])));
            var tags = ReadStringArray(meta == null ? null : meta["tags"]);
            var genres = new List<string>();
            var features = new List<string>();
            MapEpicTags(tags, genres, features);

            var storeUrl = TokenText(product["_metadataAiUrl"]);
            var result = new OfficialStoreMetadata
            {
                SourceName = SourceEpicStore,
                StoreUrl = storeUrl,
                Title = title,
                Description = description,
                Developers = developers,
                Publishers = publishers,
                Genres = genres,
                Features = features,
                ReleaseDate = NormalizeReleaseDate(TokenText(meta == null ? null : meta["releaseDate"])),
                ListsMatchPluginLanguage = false
            };
            if (!string.IsNullOrWhiteSpace(storeUrl))
            {
                result.Links.Add(new Link("Epic Store", storeUrl));
            }

            return result.HasUsefulData() || !string.IsNullOrWhiteSpace(result.StoreUrl) ? result : null;
        }

        private async Task<JObject> ResolveEpicProductAsync(Game game, CancellationToken cancelToken)
        {
            var cacheKey = (game == null ? string.Empty : game.Name ?? string.Empty) + "|" + GetEpicLocale();
            if (epicProductCache != null && string.Equals(epicProductCacheKey, cacheKey, StringComparison.Ordinal))
            {
                return epicProductCache;
            }

            JObject resolved = null;
            var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var slug in BuildEpicSlugCandidates(game))
            {
                if (string.IsNullOrWhiteSpace(slug) || !tried.Add(slug))
                {
                    continue;
                }

                var product = await GetEpicProductBySlugAsync(slug, cancelToken).ConfigureAwait(false);
                if (product == null)
                {
                    continue;
                }

                var productName = (string)product["productName"] ?? (string)product["_title"];
                if (game != null &&
                    !string.IsNullOrWhiteSpace(game.Name) &&
                    !string.IsNullOrWhiteSpace(productName) &&
                    !IsReliableStoreTitleMatch(game.Name, productName) &&
                    !HasEpicStoreLink(game))
                {
                    // Slug guess without a store link must still match the library title.
                    continue;
                }

                product["_metadataAiUrl"] = "https://store.epicgames.com/p/" + slug;
                resolved = product;
                break;
            }

            epicProductCacheKey = cacheKey;
            epicProductCache = resolved;
            return resolved;
        }

        private async Task<JObject> GetEpicProductBySlugAsync(string slug, CancellationToken cancelToken)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return null;
            }

            var locale = GetEpicLocale();
            var url = string.Format(
                CultureInfo.InvariantCulture,
                "https://store-content-ipv4.ak.epicgames.com/api/{0}/content/products/{1}",
                locale,
                Uri.EscapeDataString(slug));
            try
            {
                var json = await GetStringAsync(url, cancelToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith("<", StringComparison.Ordinal))
                {
                    return null;
                }

                var product = JObject.Parse(json);
                if (product["productName"] == null && product["_title"] == null && product["pages"] == null)
                {
                    return null;
                }

                return product;
            }
            catch
            {
                return null;
            }
        }

        private static List<OfficialMediaCandidate> BuildEpicMediaCandidates(JObject product, MediaKind kind)
        {
            var result = new List<OfficialMediaCandidate>();
            var page = GetEpicHomePage(product);
            var pageData = page == null ? null : page["data"] as JObject;
            var hero = pageData == null ? null : pageData["hero"] as JObject;
            if (hero != null)
            {
                if (kind == MediaKind.Cover || kind == MediaKind.Icon)
                {
                    AddEpicImage(result, (string)hero["portraitBackgroundImageUrl"], kind == MediaKind.Icon ? "epic portrait icon" : "epic portrait cover", 88);
                    AddEpicImage(result, GetEpicImageUrl(hero["logoImage"]), "epic logo", 40);
                }
                else
                {
                    AddEpicImage(result, (string)hero["backgroundImageUrl"], "epic hero background", 90);
                    AddEpicImage(result, (string)hero["portraitBackgroundImageUrl"], "epic portrait art", 70);
                }
            }

            if (result.Count == 0)
            {
                // Fallback: collect CDN image URLs from the product JSON payload.
                var urls = Regex.Matches(product == null ? string.Empty : product.ToString(Newtonsoft.Json.Formatting.None),
                        "https://(?:cdn2\\.unrealengine\\.com|cdn1\\.epicgames\\.com|media-cdn\\.epicgames\\.com)[^\\\"'\\\\]+\\.(?:jpg|jpeg|png|webp)",
                        RegexOptions.IgnoreCase)
                    .Cast<Match>()
                    .Select(x => WebUtility.HtmlDecode(x.Value).Split('?')[0])
                    .Where(x => !string.IsNullOrWhiteSpace(x) && !IsEpicRatingBadgeUrl(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(kind == MediaKind.Background ? 12 : 6)
                    .ToList();
                foreach (var url in urls)
                {
                    AddEpicImage(result, url, kind == MediaKind.Background ? "epic artwork" : "epic image", 54);
                }
            }

            return result;
        }

        private static void AddEpicImage(List<OfficialMediaCandidate> result, string url, string style, int score)
        {
            if (string.IsNullOrWhiteSpace(url) || IsEpicRatingBadgeUrl(url))
            {
                return;
            }

            result.Add(CreateOfficialCandidate(
                url.Split('?')[0],
                0,
                0,
                style,
                score,
                SourceEpicStore,
                48,
                true));
        }

        private static bool IsEpicRatingBadgeUrl(string url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                   (url.IndexOf("product/ratings", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    url.IndexOf("ESRB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    url.IndexOf("PEGI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    url.IndexOf("USK_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    url.IndexOf("CERO_", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static JObject GetEpicHomePage(JObject product)
        {
            var pages = product == null ? null : product["pages"] as JArray;
            if (pages == null || pages.Count == 0)
            {
                return null;
            }

            foreach (var page in pages.OfType<JObject>())
            {
                if (string.Equals((string)page["_slug"], "home", StringComparison.OrdinalIgnoreCase))
                {
                    return page;
                }
            }

            return pages.OfType<JObject>().FirstOrDefault();
        }

        private static IEnumerable<string> BuildEpicSlugCandidates(Game game)
        {
            var direct = GetFirstLink(game, "store.epicgames.com", "epicgames.com/store");
            var fromLink = ExtractEpicProductSlug(direct);
            if (!string.IsNullOrWhiteSpace(fromLink))
            {
                yield return fromLink;
            }

            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                yield break;
            }

            foreach (var title in BuildTitleAliases(game.Name))
            {
                var slug = SlugifyEpicProduct(title);
                if (!string.IsNullOrWhiteSpace(slug))
                {
                    yield return slug;
                }
            }
        }

        private static string ExtractEpicProductSlug(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var match = Regex.Match(url, @"store\.epicgames\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?p/(?<slug>[^/?#]+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = Regex.Match(url, @"epicgames\.com/store/(?:[a-z]{2}(?:-[a-z]{2})/)?product/(?<slug>[^/?#]+)", RegexOptions.IgnoreCase);
            }

            if (!match.Success)
            {
                return null;
            }

            var slug = Uri.UnescapeDataString(match.Groups["slug"].Value).Trim().Trim('/');
            var offerSeparator = slug.IndexOf("--", StringComparison.Ordinal);
            if (offerSeparator > 0)
            {
                slug = slug.Substring(0, offerSeparator);
            }

            return string.IsNullOrWhiteSpace(slug) ? null : slug;
        }

        private static string SlugifyEpicProduct(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var text = value.ToLowerInvariant();
            text = text.Replace("&", " and ");
            text = Regex.Replace(text, @"['’]", string.Empty);
            text = Regex.Replace(text, @"[^a-z0-9]+", "-");
            text = Regex.Replace(text, @"-+", "-").Trim('-');
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static bool HasEpicStoreLink(Game game)
        {
            return !string.IsNullOrWhiteSpace(GetFirstLink(game, "store.epicgames.com", "epicgames.com/store"));
        }

        private static string GetEpicImageUrl(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return (string)token;
            }

            var obj = token as JObject;
            if (obj == null)
            {
                return null;
            }

            return (string)obj["src"] ?? (string)obj["url"] ?? (string)obj["image"];
        }

        private static void MapEpicTags(IEnumerable<string> tags, List<string> genres, List<string> features)
        {
            if (tags == null)
            {
                return;
            }

            foreach (var raw in tags)
            {
                var tag = (raw ?? string.Empty).Trim().ToUpperInvariant().Replace(' ', '_');
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                switch (tag)
                {
                    case "ACTION":
                    case "ADVENTURE":
                    case "RPG":
                    case "STRATEGY":
                    case "SIMULATION":
                    case "SPORTS":
                    case "RACING":
                    case "HORROR":
                    case "PUZZLE":
                    case "SHOOTER":
                    case "PLATFORMER":
                    case "FIGHTING":
                    case "STEALTH":
                        AddUniqueString(genres, ToTitleCaseTag(tag));
                        break;
                    case "SINGLE_PLAYER":
                        AddUniqueString(features, "Single-player");
                        break;
                    case "MULTI_PLAYER":
                    case "MULTIPLAYER":
                        AddUniqueString(features, "Multiplayer");
                        break;
                    case "CO_OP":
                    case "COOP":
                    case "ONLINE_CO_OP":
                        AddUniqueString(features, "Co-op");
                        break;
                    case "CONTROLLER_SUPPORT":
                        AddUniqueString(features, "Controller Support");
                        break;
                    case "CLOUD_SAVES":
                        AddUniqueString(features, "Cloud Saves");
                        break;
                    default:
                        break;
                }
            }
        }

        private static string ToTitleCaseTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return string.Empty;
            }

            if (tag == "RPG")
            {
                return "RPG";
            }

            var words = tag.ToLowerInvariant().Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++)
            {
                var word = words[i];
                words[i] = char.ToUpperInvariant(word[0]) + word.Substring(1);
            }

            return string.Join(" ", words);
        }

        private static void AddUniqueString(List<string> list, string value)
        {
            if (list == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!list.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(value);
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string CleanMarkdownText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = Regex.Replace(value, @"\*+", string.Empty);
            return CleanText(text);
        }

        private string GetEpicLocale()
        {
            var language = settings == null ? "en" : settings.Language ?? "en";
            var parts = language.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var lang = parts.Length > 0 ? parts[0].ToLowerInvariant() : "en";
            var country = parts.Length > 1 ? parts[1].ToUpperInvariant() : (lang == "es" ? "ES" : "US");
            if (lang == "en" && parts.Length == 1)
            {
                country = "US";
            }

            return lang + "-" + country;
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
            // Year-anchored Multiplayer / Single Player packages: prefer the base
            // store app (has genres) over the mode-specific AppID when searchable.
            if (game != null && TitleMatchingService.CanUsePlayModeBaseTitle(game.Name))
            {
                var baseId = await ResolveSteamAppIdByTitleSearchAsync(
                    TitleMatchingService.WithoutPlayModeSuffix(game.Name),
                    cancelToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(baseId))
                {
                    return baseId;
                }
            }

            if (game != null && IsSteamLibrarySource(game) &&
                !string.IsNullOrWhiteSpace(game.GameId) &&
                Regex.IsMatch(game.GameId.Trim(), @"^\d+$"))
            {
                return game.GameId.Trim();
            }

            var linkedId = ExtractSteamAppIdFromGame(game);
            if (!string.IsNullOrWhiteSpace(linkedId))
            {
                return linkedId;
            }

            // Numeric GameId is often the Steam AppID even when Source is missing/renamed.
            // Validate against the store title so GOG/other numeric IDs are not reused blindly.
            if (game != null &&
                !string.IsNullOrWhiteSpace(game.GameId) &&
                Regex.IsMatch(game.GameId.Trim(), @"^\d{3,}$"))
            {
                var candidateId = game.GameId.Trim();
                var data = await GetSteamAppDataAsync(candidateId, cancelToken).ConfigureAwait(false);
                if (data != null &&
                    !IsNonGameSteamApp(data) &&
                    (string.IsNullOrWhiteSpace(game.Name) ||
                     IsReliableStoreTitleMatch(game.Name, (string)data["name"])))
                {
                    return candidateId;
                }
            }

            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return null;
            }

            return await ResolveSteamAppIdByTitleSearchAsync(game.Name, cancelToken).ConfigureAwait(false);
        }

        private async Task<string> ResolveSteamAppIdByTitleSearchAsync(string title, CancellationToken cancelToken)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            foreach (var alias in BuildTitleAliases(title))
            {
                var url = "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(alias) +
                          "&cc=" + Uri.EscapeDataString(GetCountryCode()) +
                          "&l=" + Uri.EscapeDataString(GetSteamStoreLanguage());
                var json = await GetJsonAsync(url, cancelToken).ConfigureAwait(false);
                if (json == null)
                {
                    continue;
                }

                var items = json["items"] as JArray;
                var matches = (items ?? new JArray())
                    .OfType<JObject>()
                    .Select(x => new StoreSearchMatch { Id = ((int?)x["id"] ?? 0).ToString(), Title = (string)x["name"] })
                    .Where(x => x.Id != "0" && !IsNonGameSteamSearchTitle(x.Title))
                    .ToList();
                var selected = PickBestMatch(title, matches);
                if (selected == null)
                {
                    continue;
                }

                var data = await GetSteamAppDataAsync(selected.Id, cancelToken).ConfigureAwait(false);
                if (data != null && !IsNonGameSteamApp(data))
                {
                    return selected.Id;
                }
            }

            return null;
        }

        private static bool IsSteamLibrarySource(Game game)
        {
            if (game == null)
            {
                return false;
            }

            var sourceName = game.Source == null ? string.Empty : game.Source.Name ?? string.Empty;
            if (sourceName.IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Playnite Steam library plugin id (stable across installs).
            return string.Equals(
                game.PluginId.ToString(),
                "cb91dfc9-b977-43bf-8e70-55f46e410fab",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNonGameSteamSearchTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            return Regex.IsMatch(
                title,
                @"\b(soundtrack|ost|original\s+sound\s+track|trailer|demo|playtest|dedicated\s+server)\b",
                RegexOptions.IgnoreCase);
        }

        public static string TryGetSteamAppId(Game game)
        {
            return ExtractSteamAppIdFromGame(game);
        }

        private static string ExtractSteamAppIdFromGame(Game game)
        {
            if (game == null)
            {
                return null;
            }

            if (game.Links != null)
            {
                foreach (var link in game.Links)
                {
                    if (link == null)
                    {
                        continue;
                    }

                    var fromUrl = ExtractSteamAppIdFromText(link.Url);
                    if (!string.IsNullOrWhiteSpace(fromUrl))
                    {
                        return fromUrl;
                    }

                    var fromName = ExtractSteamAppIdFromText(link.Name);
                    if (!string.IsNullOrWhiteSpace(fromName))
                    {
                        return fromName;
                    }
                }
            }

            return ExtractSteamAppIdFromText(game.GameId);
        }

        private static string ExtractSteamAppIdFromText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = Regex.Match(
                value,
                @"(?:store\.steampowered\.com/app/|steamcommunity\.com/app/|steamdb\.info/app/|steam://(?:run|rungameid|openurl)/|apps/steam/|/steam/apps/)(\d{3,})",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        public async Task<OfficialStoreMetadata> TryGetSteamContextAsync(Game game, CancellationToken cancelToken)
        {
            if (settings != null && !settings.UseSteamMetadata)
            {
                return null;
            }

            try
            {
                return await GetSteamMetadataAsync(game, cancelToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (cancelToken.IsCancellationRequested)
                {
                    throw;
                }

                return null;
            }
            catch
            {
                return null;
            }
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

        private string GetSteamStoreLanguage()
        {
            return ToSteamStoreLanguage(settings == null ? "en" : settings.Language);
        }

        public static string ToSteamStoreLanguage(string pluginLanguage)
        {
            var raw = (pluginLanguage ?? "en").Trim().ToLowerInvariant().Replace('_', '-');
            if (raw == "pt-br")
            {
                return "brazilian";
            }

            if (raw == "zh-tw" || raw == "zh-hant")
            {
                return "tchinese";
            }

            var code = raw.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "en";
            switch (code)
            {
                case "es": return "spanish";
                case "en": return "english";
                case "de": return "german";
                case "fr": return "french";
                case "it": return "italian";
                case "pt": return "portuguese";
                case "br": return "brazilian";
                case "ru": return "russian";
                case "ja": return "japanese";
                case "ko": return "koreana";
                case "zh": return "schinese";
                case "pl": return "polish";
                case "tr": return "turkish";
                case "nl": return "dutch";
                case "sv": return "swedish";
                case "no": return "norwegian";
                case "da": return "danish";
                case "fi": return "finnish";
                case "cs": return "czech";
                case "hu": return "hungarian";
                case "ro": return "romanian";
                case "th": return "thai";
                case "vi": return "vietnamese";
                case "uk": return "ukrainian";
                default: return string.IsNullOrWhiteSpace(code) ? "english" : code;
            }
        }

        private string GetCountryCode()
        {
            var language = settings == null ? "en" : settings.Language ?? "en";
            var parts = language.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                return parts[1].ToLowerInvariant();
            }

            var lang = parts.Length > 0 ? parts[0].ToLowerInvariant() : "en";
            switch (lang)
            {
                case "es": return "es";
                case "en": return "us";
                case "de": return "de";
                case "fr": return "fr";
                case "it": return "it";
                case "pt": return "br";
                case "br": return "br";
                case "ru": return "ru";
                case "ja": return "jp";
                case "ko": return "kr";
                case "zh": return "cn";
                case "pl": return "pl";
                case "tr": return "tr";
                default: return "us";
            }
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

        private string ReadPcRequirement(JToken requirements, string key)
        {
            if (requirements == null || requirements.Type != JTokenType.Object || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return FormatSystemRequirements((string)requirements[key], GetStoreLanguage());
        }

        internal static string FormatSystemRequirements(string html)
        {
            return FormatSystemRequirements(html, "en");
        }

        internal static string FormatSystemRequirements(string html, string language)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var text = WebUtility.HtmlDecode(html);
            text = Regex.Replace(text, @"</li\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</p\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", " ");
            var lines = Regex.Split(text.Replace("\r", string.Empty), @"\n+")
                .Select(x => Regex.Replace(x ?? string.Empty, @"\s+", " ").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(StripSystemRequirementHeading)
                .Select(x => NormalizeSystemRequirementLine(x, language))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            return string.Join("\n", lines);
        }

        internal static string NormalizeSystemRequirementsText(string value, string language)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var lines = Regex.Split(value.Replace("\r", string.Empty), @"\n+")
                .Select(x => NormalizeSystemRequirementLine(x, language))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            return string.Join("\n", lines);
        }

        private static string NormalizeSystemRequirementLine(string line, string language)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            line = WebUtility.HtmlDecode(line).Trim();
            line = line.Replace("\u00a0", " ");
            line = Regex.Replace(line, @"[•·∙]+", " ");
            line = Regex.Replace(line, @"\s+", " ").Trim();
            var separator = line.IndexOf(':');
            if (separator <= 0 || separator >= line.Length - 1)
            {
                return line.Trim(' ', '*', '-', '–');
            }

            var rawLabel = line.Substring(0, separator).Trim();
            var detail = line.Substring(separator + 1).Trim().TrimStart('*', ' ');
            rawLabel = Regex.Replace(rawLabel, @"[\s\*†‡※]+$", string.Empty).Trim();
            rawLabel = rawLabel.Trim('*', ' ');
            if (string.IsNullOrWhiteSpace(rawLabel) || string.IsNullOrWhiteSpace(detail))
            {
                return line.Trim(' ', '*', '-', '–');
            }

            return rawLabel + ": " + detail;
        }

        private static string StripSystemRequirementHeading(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            var stripped = Regex.Replace(
                line.Trim(),
                @"^(Minimum|Recommended|M[ií]nimo|Recomendado|Minimale|Empfohlen|Minimi|Raccomandati)\s*:?\s*",
                string.Empty,
                RegexOptions.IgnoreCase);
            return stripped.Trim();
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

        private async Task<List<string>> ReadSteamUserTagsAsync(string appId, CancellationToken cancelToken)
        {
            if (string.IsNullOrWhiteSpace(appId))
            {
                return new List<string>();
            }

            var url = "https://store.steampowered.com/app/" + Uri.EscapeDataString(appId) +
                      "/?l=" + Uri.EscapeDataString(GetSteamStoreLanguage());
            var html = await GetSteamStorePageAsync(appId, url, cancelToken).ConfigureAwait(false);
            return ParseSteamAppTags(html).Take(20).ToList();
        }

        /// <summary>
        /// Mature Steam pages redirect to an age gate. Cookies alone no longer pass it;
        /// the store expects a birth date posted to agecheckset before the tags are in the HTML.
        /// </summary>
        private static async Task<string> GetSteamStorePageAsync(string appId, string url, CancellationToken cancelToken)
        {
            var cookies = new CookieContainer();
            var store = new Uri("https://store.steampowered.com/");
            cookies.Add(store, new Cookie("birthtime", "631152000"));
            cookies.Add(store, new Cookie("lastagecheckage", "1-January-1990"));
            cookies.Add(store, new Cookie("mature_content", "1"));
            cookies.Add(store, new Cookie("wants_mature_content", "1"));

            using (var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MetaDataIAPlugin/1.0");
                var body = await ReadSteamPageAsync(client, url, cancelToken).ConfigureAwait(false);
                if (!IsSteamAgeGate(body))
                {
                    return body;
                }

                var sessionId = ExtractSteamSessionId(body);
                if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(sessionId))
                {
                    return string.Empty;
                }

                using (var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("sessionid", sessionId),
                    new KeyValuePair<string, string>("ageDay", "1"),
                    new KeyValuePair<string, string>("ageMonth", "January"),
                    new KeyValuePair<string, string>("ageYear", "1990")
                }))
                using (var post = await client.PostAsync("https://store.steampowered.com/agecheckset/app/" + Uri.EscapeDataString(appId) + "/", form, cancelToken).ConfigureAwait(false))
                {
                    await post.Content.ReadAsStringAsync().ConfigureAwait(false);
                }

                body = await ReadSteamPageAsync(client, url, cancelToken).ConfigureAwait(false);
                return IsSteamAgeGate(body) ? string.Empty : body;
            }
        }

        private static async Task<string> ReadSteamPageAsync(HttpClient client, string url, CancellationToken cancelToken)
        {
            using (var response = await client.GetAsync(url, cancelToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return string.Empty;
                }

                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        public static bool IsSteamAgeGate(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return false;
            }

            return html.IndexOf("app_agegate", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractSteamSessionId(string html)
        {
            var match = Regex.Match(html ?? string.Empty, "g_sessionID\\s*=\\s*\"(?<id>[^\"]+)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["id"].Value : string.Empty;
        }

        public static List<string> ParseSteamAppTags(string html)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return result;
            }

            var modal = Regex.Match(
                html,
                "InitAppTagModal\\s*\\(\\s*\\d+\\s*,\\s*(?<json>\\[.*?\\])\\s*,",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (modal.Success)
            {
                try
                {
                    foreach (var item in JArray.Parse(modal.Groups["json"].Value).OfType<JObject>())
                    {
                        AddSteamTag(result, (string)item["name"]);
                    }
                }
                catch (Newtonsoft.Json.JsonException)
                {
                }
            }

            if (result.Count > 0)
            {
                return result;
            }

            var matches = Regex.Matches(
                html,
                "class\\s*=\\s*\"app_tag\"[^>]*>\\s*(?<name>[^<]+?)\\s*<",
                RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                AddSteamTag(result, match.Groups["name"].Value);
            }

            return result;
        }

        private static void AddSteamTag(List<string> result, string raw)
        {
            var name = CleanText(WebUtility.HtmlDecode(raw ?? string.Empty));
            if (string.IsNullOrWhiteSpace(name) || name == "+" ||
                result.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            result.Add(name);
        }

        private static string TokenText(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return (string)token;
            }

            if (token.Type == JTokenType.Array)
            {
                var parts = token
                    .Children()
                    .Select(TokenText)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return parts.Count == 0 ? null : string.Join(", ", parts);
            }

            if (token.Type == JTokenType.Object)
            {
                return null;
            }

            return token.ToString();
        }

        private static List<string> ReadNameArray(JToken token)
        {
            var result = new List<string>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in (token as JArray ?? new JArray()).OfType<JObject>())
            {
                var id = TokenText(item["id"]);
                if (!string.IsNullOrWhiteSpace(id) && !seenIds.Add(id.Trim()))
                {
                    continue;
                }

                var name = CleanText(TokenText(item["description"]) ?? TokenText(item["name"]));
                if (string.IsNullOrWhiteSpace(name) ||
                    result.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result.Add(name);
            }

            return result;
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

        private async Task<OfficialStoreMetadata> GetEsrbMetadataAsync(Game game, CancellationToken cancelToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return null;
            }

            foreach (var title in BuildTitleAliases(game.Name))
            {
                var url = "https://www.esrb.org/search/?searchKeyword=" + Uri.EscapeDataString(title);
                var html = await GetStringAsync(url, cancelToken).ConfigureAwait(false);
                var matches = Regex.Matches(html ?? string.Empty,
                    "<div\\s+class=\\\"game\\\">(?<body>[\\s\\S]*?<h2>\\s*<a\\s+href=\\\"(?<url>https://www\\.esrb\\.org/ratings/[^\\\"]+)\\\"[^>]*>(?<title>[^<]+)</a>[\\s\\S]*?</table>)\\s*</div>",
                    RegexOptions.IgnoreCase)
                    .Cast<Match>()
                    .Select(x => new
                    {
                        Url = x.Groups["url"].Value,
                        Title = CleanText(WebUtility.HtmlDecode(x.Groups["title"].Value)),
                        Platforms = CleanText(Regex.Match(x.Groups["body"].Value, "<div\\s+class=\\\"platforms\\\">(?<value>[\\s\\S]*?)</div>", RegexOptions.IgnoreCase).Groups["value"].Value),
                        Rating = WebUtility.HtmlDecode(Regex.Match(x.Groups["body"].Value, "<img[^>]+alt=\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.IgnoreCase).Groups["value"].Value)
                    })
                    .Where(x => IsReliableStoreTitleMatch(game.Name, x.Title) &&
                                !string.IsNullOrWhiteSpace(x.Rating) &&
                                IsEsrbPlatformCompatible(game, x.Platforms))
                    .ToList();
                var selected = matches.FirstOrDefault();
                if (selected != null)
                {
                    return new OfficialStoreMetadata
                    {
                        SourceName = SourceEsrb,
                        StoreUrl = selected.Url,
                        Title = selected.Title,
                        AgeRating = "ESRB " + selected.Rating,
                        IsExactMatch = true
                    };
                }
            }

            return null;
        }

        private static bool IsEsrbPlatformCompatible(Game game, string esrbPlatforms)
        {
            if (game == null || game.Platforms == null || game.Platforms.Count == 0 || string.IsNullOrWhiteSpace(esrbPlatforms))
            {
                return true;
            }

            var target = esrbPlatforms.ToLowerInvariant();
            return game.Platforms.Any(platform =>
            {
                var name = (platform == null ? string.Empty : platform.Name ?? string.Empty).ToLowerInvariant();
                return (name.Contains("windows") || name == "pc")
                    ? target.Contains("windows pc")
                    : target.Contains(name);
            });
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
                // Helps Steam appdetails succeed for age-gated titles in some regions.
                if (url.IndexOf("steampowered.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    request.Headers.TryAddWithoutValidation(
                        "Cookie",
                        "birthtime=0; lastagecheckage=1-January-1990; mature_content=1; wants_mature_content=1");
                }

                try
                {
                    using (var response = await Client.SendAsync(request, cancelToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            return string.Empty;
                        }

                        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    // HttpClient timeouts surface as TaskCanceledException; treat as empty store data.
                    return string.Empty;
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
            public string Classification { get; set; }
            public List<PsnMediaObject> Media { get; set; }

            public StoreSearchMatch()
            {
                Media = new List<PsnMediaObject>();
            }
        }

        private class PsnMediaObject
        {
            public string Role { get; set; }
            public string Url { get; set; }
        }
    }
}
