using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    public enum MediaKind
    {
        Cover,
        Icon,
        Background
    }

    public class GeneratedMediaFile
    {
        public MediaKind Kind { get; set; }
        public string FileName { get; set; }
        public byte[] Content { get; set; }
        public string SourceUrl { get; set; }
    }

    public class MediaPreviewOption
    {
        public MediaKind Kind { get; set; }
        public string Url { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Style { get; set; }
        public int Score { get; set; }
        public string Extension { get; set; }
        public string SourceName { get; set; }
        public bool IsOfficial { get; set; }

        public string DisplayText
        {
            get
            {
                var size = Width > 0 && Height > 0 ? Width + "x" + Height : "tamano desconocido";
                var style = string.IsNullOrWhiteSpace(Style) ? "sin estilo" : Style;
                var source = string.IsNullOrWhiteSpace(SourceName) ? "Fuente desconocida" : SourceName;
                return source + " - " + size + " - " + style + " - score " + Score;
            }
        }
    }

    public class MediaGenerationService
    {
        private const string ApiBase = "https://www.steamgriddb.com/api/v2";
        private static readonly HttpClient Client = new HttpClient();
        private static readonly object CandidateCacheLock = new object();
        private static readonly Dictionary<string, List<MediaCandidate>> CandidateCache = new Dictionary<string, List<MediaCandidate>>();
        private static readonly Dictionary<string, List<string>> CandidateDiagnosticsCache = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, CandidateProbeResult> CandidateProbeCache = new Dictionary<string, CandidateProbeResult>(StringComparer.OrdinalIgnoreCase);
        private readonly MetaDataIASettings settings;
        private readonly IPlayniteAPI playniteApi;
        private string generatedIgdbAccessToken;

        public int StrictQualitySkipCount { get; private set; }

        static MediaGenerationService()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
        }

        public MediaGenerationService(MetaDataIASettings settings, IPlayniteAPI playniteApi = null)
        {
            this.settings = settings;
            this.playniteApi = playniteApi;
        }

        public async Task<GeneratedMediaFile> GenerateAsync(Game game, MediaKind kind, CancellationToken cancelToken = default(CancellationToken))
        {
            if (game == null)
            {
                throw new ArgumentNullException("game");
            }

            var candidates = await GetCandidates(game, kind, cancelToken).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException(BuildNoMediaFoundMessage(game, kind));
            }

            var automaticCandidates = GetAutomaticCandidates(candidates, kind);
            if (automaticCandidates.Count == 0)
            {
                StrictQualitySkipCount++;
                return null;
            }

            var selected = ChooseCandidate(automaticCandidates, kind);
            var selectedBytes = await DownloadBestBytes(automaticCandidates, selected, kind, cancelToken).ConfigureAwait(false);
            if (selectedBytes.Candidate == null || string.IsNullOrWhiteSpace(selectedBytes.Candidate.Url))
            {
                throw new InvalidOperationException(Loc("MTDA_ErrorMediaWithoutUrl", "The media source returned an image without a usable URL."));
            }

            var processed = ProcessImage(selectedBytes.Content, kind, selectedBytes.Candidate.Extension);
            return new GeneratedMediaFile
            {
                Kind = kind,
                FileName = BuildFileName(game.Name, kind, processed.Extension),
                Content = processed.Content,
                SourceUrl = selectedBytes.Candidate.Url
            };
        }

        public async Task<List<MediaPreviewOption>> GetPreviewOptionsAsync(Game game, MediaKind kind, CancellationToken cancelToken = default(CancellationToken))
        {
            if (game == null)
            {
                throw new ArgumentNullException("game");
            }

            var candidates = await GetCandidates(game, kind, cancelToken).ConfigureAwait(false);
            var maximum = Math.Max(1, settings.MediaSearchMaxResults);
            var validated = await ValidatePreviewCandidatesAsync(OrderCandidates(candidates, kind).ToList(), maximum, cancelToken).ConfigureAwait(false);

            return OrderCandidates(validated, kind)
                .Take(maximum)
                .Select(x => new MediaPreviewOption
                {
                    Kind = kind,
                    Url = x.Url,
                    Width = x.Width,
                    Height = x.Height,
                    Style = x.Style,
                    Score = x.Score,
                    Extension = x.Extension,
                    SourceName = x.SourceName,
                    IsOfficial = x.IsOfficial
                })
                .ToList();
        }

        public async Task<int> CountPreviewOptionsAsync(Game game, MediaKind kind, CancellationToken cancelToken = default(CancellationToken))
        {
            if (game == null)
            {
                throw new ArgumentNullException("game");
            }

            var candidates = await GetCandidates(game, kind, cancelToken).ConfigureAwait(false);
            return candidates.Count;
        }

        public string GetLastDiagnostics(Game game, MediaKind kind)
        {
            var cacheKey = BuildCandidateCacheKey(game, kind);
            lock (CandidateCacheLock)
            {
                List<string> diagnostics;
                if (CandidateDiagnosticsCache.TryGetValue(cacheKey, out diagnostics) && diagnostics.Count > 0)
                {
                    return string.Join(Environment.NewLine, diagnostics);
                }
            }

            return string.Empty;
        }

        public async Task<GeneratedMediaFile> GenerateFromOptionAsync(Game game, MediaPreviewOption option, CancellationToken cancelToken = default(CancellationToken))
        {
            if (game == null)
            {
                throw new ArgumentNullException("game");
            }

            if (option == null || string.IsNullOrWhiteSpace(option.Url))
            {
                throw new ArgumentNullException("option");
            }

            byte[] bytes;
            try
            {
                bytes = await DownloadBytes(option.Url, cancelToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    Loc("MTDA_ErrorMediaUnavailable", "This image is no longer available from its source. Reopen the media selector to choose another candidate."),
                    ex);
            }

            var processed = ProcessImage(bytes, option.Kind, option.Extension);
            return new GeneratedMediaFile
            {
                Kind = option.Kind,
                FileName = BuildFileName(game.Name, option.Kind, processed.Extension),
                Content = processed.Content,
                SourceUrl = option.Url
            };
        }

        public bool ShouldGenerate(MediaKind kind)
        {
            if (kind == MediaKind.Cover)
            {
                return settings.DownloadCoverImage && settings.CoverImageApplyMode != MetaDataIASettings.ApplySkip;
            }

            if (kind == MediaKind.Icon)
            {
                return settings.DownloadIcon && settings.IconApplyMode != MetaDataIASettings.ApplySkip;
            }

            return settings.DownloadBackgroundImage && settings.BackgroundImageApplyMode != MetaDataIASettings.ApplySkip;
        }

        public bool ShouldApply(Game game, MediaKind kind)
        {
            var mode = GetApplyMode(kind);
            if (mode == MetaDataIASettings.ApplySkip)
            {
                return false;
            }

            if (mode == MetaDataIASettings.ApplyEmptyOnly)
            {
                return string.IsNullOrWhiteSpace(GetCurrentImage(game, kind));
            }

            return mode == MetaDataIASettings.ApplyAppend || mode == MetaDataIASettings.ApplyOverwrite;
        }

        public async Task<int> ApplyEnabledMediaAsync(IPlayniteAPI api, Game game, CancellationToken cancelToken = default(CancellationToken))
        {
            if (api == null || game == null)
            {
                return 0;
            }

            var applied = 0;
            foreach (var kind in new[] { MediaKind.Cover, MediaKind.Icon, MediaKind.Background })
            {
                if (!ShouldGenerate(kind) || !ShouldApply(game, kind))
                {
                    continue;
                }

                var generated = await GenerateAsync(game, kind, cancelToken).ConfigureAwait(false);
                if (generated == null)
                {
                    continue;
                }

                ApplyMediaFile(api, game, generated);
                applied++;
            }

            if (applied > 0)
            {
                api.Database.Games.Update(game);
            }

            return applied;
        }

        public static MetadataFile ToMetadataFile(GeneratedMediaFile file)
        {
            if (file == null || file.Content == null || file.Content.Length == 0)
            {
                return null;
            }

            return new MetadataFile(file.FileName, file.Content);
        }

        public static void ApplyMediaFile(IPlayniteAPI api, Game game, GeneratedMediaFile file)
        {
            if (api == null || game == null || file == null || file.Content == null || file.Content.Length == 0)
            {
                return;
            }

            var previousPath = GetCurrentImage(game, file.Kind);
            var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName));
            File.WriteAllBytes(tempPath, file.Content);
            try
            {
                var storagePath = api.Database.AddFile(tempPath, game.Id);
                if (file.Kind == MediaKind.Cover)
                {
                    game.CoverImage = storagePath;
                }
                else if (file.Kind == MediaKind.Icon)
                {
                    game.Icon = storagePath;
                }
                else
                {
                    game.BackgroundImage = storagePath;
                }

                api.Database.Games.Update(game);
                if (!string.Equals(previousPath, storagePath, StringComparison.OrdinalIgnoreCase))
                {
                    MediaStorageCleanupService.TryRemoveUnreferencedMedia(api, previousPath);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }
        }

        private void EnsureConfigured()
        {
            if (string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
            {
                throw new InvalidOperationException(Loc("MTDA_ErrorSteamGridDbApiKeyRequired", "Configure the SteamGridDB API key in the Media tab before downloading images."));
            }
        }

        private string BuildNoMediaFoundMessage(Game game, MediaKind kind)
        {
            var message = Loc("MTDA_ErrorNoImagesFound", "No images were found for this game in the configured media sources.");
            var diagnostics = GetLastDiagnostics(game, kind);
            if (string.IsNullOrWhiteSpace(diagnostics))
            {
                return message;
            }

            return message + Environment.NewLine + Environment.NewLine +
                   Loc("MTDA_MediaDiagnosticsTitle", "Source diagnostics:") + Environment.NewLine +
                   diagnostics;
        }

        private static void AddSourceCandidates(List<MediaCandidate> candidates, List<string> diagnostics, string sourceName, IEnumerable<MediaCandidate> sourceCandidates)
        {
            var validCandidates = (sourceCandidates ?? new List<MediaCandidate>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url))
                .ToList();

            candidates.AddRange(validCandidates);
            AddDiagnostic(
                diagnostics,
                sourceName,
                validCandidates.Count == 0
                    ? Loc("MTDA_MediaDiagNoCandidates", "no reliable match or no usable candidates")
                    : string.Format(Loc("MTDA_MediaDiagCandidatesFound", "{0} candidate(s) found"), validCandidates.Count));
        }

        private static void AddDiagnostic(List<string> diagnostics, string sourceName, string detail)
        {
            if (diagnostics == null || string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(detail))
            {
                return;
            }

            diagnostics.Add("- " + sourceName + ": " + detail);
        }

        private async Task<List<MediaCandidate>> GetCandidates(Game game, MediaKind kind, CancellationToken cancelToken)
        {
            var cacheKey = BuildCandidateCacheKey(game, kind);
            lock (CandidateCacheLock)
            {
                List<MediaCandidate> cached;
                if (CandidateCache.TryGetValue(cacheKey, out cached))
                {
                    return cached.Select(CloneCandidate).ToList();
                }
            }

            var candidates = new List<MediaCandidate>();
            var diagnostics = new List<string>();
            if (settings.UseOriginIntegrationForMedia && playniteApi != null)
            {
                try
                {
                    var integrationService = new PlayniteIntegrationService(playniteApi, settings);
                    var integrationResult = await integrationService.GetOriginMetadataAsync(game, cancelToken).ConfigureAwait(false);
                    var integrationMedia = await integrationService.GetMediaAsync(integrationResult, kind, cancelToken).ConfigureAwait(false);
                    var sourceCandidates = integrationMedia == null
                        ? new List<MediaCandidate>()
                        : new List<MediaCandidate>
                        {
                            new MediaCandidate
                            {
                                Url = integrationMedia.Path,
                                Style = (string.IsNullOrWhiteSpace(integrationMedia.IntegrationName) ? string.Empty : integrationMedia.IntegrationName + " - ") +
                                        Loc("MTDA_OriginIntegrationExactMedia", "Exact media from the game's library integration"),
                                Score = 100,
                                Extension = integrationMedia.Extension,
                                SourceName = MetaDataIASettings.SourceOriginIntegration,
                                SourcePriority = 110,
                                IsOfficial = true
                            }
                        };
                    AddSourceCandidates(candidates, diagnostics, MetaDataIASettings.SourceOriginIntegration, sourceCandidates);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    AddDiagnostic(diagnostics, MetaDataIASettings.SourceOriginIntegration, Loc("MTDA_MediaDiagSourceError", "source error or rejected filters"));
                }
            }

            var steamId = await ResolveSteamAppId(game, cancelToken).ConfigureAwait(false);

            if (settings.MediaUseSteamOfficial || settings.MediaUseSteamScreenshots)
            {
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    AddDiagnostic(diagnostics, "Steam", Loc("MTDA_MediaDiagNoReliableMatch", "no reliable game match"));
                }
                else
                {
                    if (settings.MediaUseSteamOfficial)
                    {
                        var sourceCandidates = GetSteamOfficialCandidates(steamId, kind);
                        AddSourceCandidates(candidates, diagnostics, "Steam", sourceCandidates);
                    }

                    if (settings.MediaUseSteamScreenshots && kind == MediaKind.Background)
                    {
                        var sourceCandidates = await GetSteamStoreCandidates(steamId, kind, cancelToken).ConfigureAwait(false);
                        AddSourceCandidates(candidates, diagnostics, "Steam screenshots", sourceCandidates);
                    }
                }
            }

            var officialStores = new OfficialStoreDataService(settings);
            if (settings.MediaUsePsnStore)
            {
                var sourceCandidates = (await officialStores.GetMediaCandidatesAsync(game, kind, OfficialStoreDataService.SourcePsnStore, cancelToken).ConfigureAwait(false)).Select(CreateOfficialStoreCandidate).ToList();
                AddSourceCandidates(candidates, diagnostics, OfficialStoreDataService.SourcePsnStore, sourceCandidates);
            }

            if (settings.MediaUseXboxStore)
            {
                var sourceCandidates = (await officialStores.GetMediaCandidatesAsync(game, kind, OfficialStoreDataService.SourceXboxStore, cancelToken).ConfigureAwait(false)).Select(CreateOfficialStoreCandidate).ToList();
                AddSourceCandidates(candidates, diagnostics, OfficialStoreDataService.SourceXboxStore, sourceCandidates);
            }

            if (settings.MediaUseEpicStore)
            {
                var sourceCandidates = (await officialStores.GetMediaCandidatesAsync(game, kind, OfficialStoreDataService.SourceEpicStore, cancelToken).ConfigureAwait(false)).Select(CreateOfficialStoreCandidate).ToList();
                AddSourceCandidates(candidates, diagnostics, OfficialStoreDataService.SourceEpicStore, sourceCandidates);
            }

            if (settings.MediaUseSteamGridDb)
            {
                if (string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
                {
                    AddDiagnostic(diagnostics, "SteamGridDB", Loc("MTDA_MediaDiagMissingApiKey", "missing API key"));
                }
                else
                {
                    try
                    {
                        var gameId = await ResolveSteamGridDbGameId(game, cancelToken).ConfigureAwait(false);
                        if (gameId > 0)
                        {
                            var sourceCandidates = await GetCandidates(gameId, kind, cancelToken).ConfigureAwait(false);
                            AddSourceCandidates(candidates, diagnostics, "SteamGridDB", sourceCandidates);

                            if (kind == MediaKind.Icon &&
                                sourceCandidates.Count == 0 &&
                                settings.IconPreset == MetaDataIASettings.IconPresetSquare &&
                                settings.IconSquarePreferGrid)
                            {
                                var gridIconCandidates = await GetGridIconCandidates(gameId, cancelToken).ConfigureAwait(false);
                                AddSourceCandidates(candidates, diagnostics, "SteamGridDB grids", gridIconCandidates);
                            }

                            if (kind == MediaKind.Background && settings.MediaUseSteamGridDbBackgroundGrids)
                            {
                                var gridBackgroundCandidates = await GetSteamGridDbBackgroundGridCandidates(gameId, cancelToken).ConfigureAwait(false);
                                AddSourceCandidates(candidates, diagnostics, "SteamGridDB grids", gridBackgroundCandidates);
                            }
                        }
                        else
                        {
                            AddDiagnostic(diagnostics, "SteamGridDB", Loc("MTDA_MediaDiagNoReliableMatch", "no reliable game match"));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        AddDiagnostic(diagnostics, "SteamGridDB", Loc("MTDA_MediaDiagSourceError", "source error or rejected filters"));
                    }
                }
            }

            if (settings.MediaUseRawg)
            {
                if (string.IsNullOrWhiteSpace(settings.RawgApiKey))
                {
                    AddDiagnostic(diagnostics, "RAWG", Loc("MTDA_MediaDiagMissingApiKey", "missing API key"));
                }
                else
                {
                    try
                    {
                        var sourceCandidates = await GetRawgCandidates(game, kind, cancelToken).ConfigureAwait(false);
                        AddSourceCandidates(candidates, diagnostics, "RAWG", sourceCandidates);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        AddDiagnostic(diagnostics, "RAWG", Loc("MTDA_MediaDiagSourceError", "source error or rejected filters"));
                    }
                }
            }

            if (settings.MediaUseMobyGames)
            {
                if (string.IsNullOrWhiteSpace(settings.MobyGamesApiKey))
                {
                    AddDiagnostic(diagnostics, "MobyGames", Loc("MTDA_MediaDiagMissingApiKey", "missing API key"));
                }
                else
                {
                    try
                    {
                        var sourceCandidates = await GetMobyGamesCandidates(game, kind, cancelToken).ConfigureAwait(false);
                        AddSourceCandidates(candidates, diagnostics, "MobyGames", sourceCandidates);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        AddDiagnostic(diagnostics, "MobyGames", Loc("MTDA_MediaDiagSourceError", "source error or rejected filters"));
                    }
                }
            }

            if (settings.MediaUseIgdb)
            {
                if (string.IsNullOrWhiteSpace(settings.IgdbClientId) ||
                    (string.IsNullOrWhiteSpace(settings.IgdbAccessToken) && string.IsNullOrWhiteSpace(settings.IgdbClientSecret)))
                {
                    AddDiagnostic(diagnostics, "IGDB", Loc("MTDA_MediaDiagMissingApiKey", "missing API key"));
                }
                else
                {
                    try
                    {
                        var sourceCandidates = await GetIgdbCandidates(game, kind, cancelToken).ConfigureAwait(false);
                        AddSourceCandidates(candidates, diagnostics, "IGDB", sourceCandidates);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        AddDiagnostic(diagnostics, "IGDB", Loc("MTDA_MediaDiagSourceError", "source error or rejected filters"));
                    }
                }
            }

            if (candidates.Count == 0 && settings.MediaUseSteamGridDb && string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey) && string.IsNullOrWhiteSpace(steamId))
            {
                EnsureConfigured();
            }

            var deduplicated = DeduplicateCandidates(candidates);
            var result = FilterCandidatesByKindSource(deduplicated, kind);
            if (deduplicated.Count > 0 && result.Count == 0)
            {
                AddDiagnostic(diagnostics, Loc("MTDA_MediaDiagFilters", "Filters"), Loc("MTDA_MediaDiagFiltersRemovedAll", "all candidates were removed by current media filters or format preferences"));
            }

            lock (CandidateCacheLock)
            {
                if (CandidateCache.Count > 120)
                {
                    CandidateCache.Clear();
                    CandidateDiagnosticsCache.Clear();
                }

                CandidateCache[cacheKey] = result.Select(CloneCandidate).ToList();
                CandidateDiagnosticsCache[cacheKey] = diagnostics.ToList();
            }

            return result;
        }

        private string BuildCandidateCacheKey(Game game, MediaKind kind)
        {
            var parts = new[]
            {
                (game == null ? string.Empty : game.Id.ToString()),
                (game == null ? string.Empty : game.Name),
                (game == null ? string.Empty : game.GameId),
                (game == null ? string.Empty : game.PluginId.ToString()),
                (game == null || game.Source == null ? string.Empty : game.Source.Name),
                kind.ToString(),
                settings.CoverImagePreset,
                settings.IconPreset,
                settings.BackgroundImagePreset,
                settings.BackgroundLogoPreference,
                settings.MediaAvoidNsfw.ToString(),
                settings.MediaAvoidBlurred.ToString(),
                settings.MediaPreferOfficial.ToString(),
                settings.MediaAvoidConsoleCovers.ToString(),
                settings.IconSquarePreferGrid.ToString(),
                settings.MediaUseSteamOfficial.ToString(),
                settings.MediaUseSteamScreenshots.ToString(),
                settings.UseOriginIntegrationForMedia.ToString(),
                string.Join(",", (settings.DisabledOriginIntegrationIds ?? new List<Guid>()).OrderBy(x => x).Select(x => x.ToString("N"))),
                settings.MediaUsePsnStore.ToString(),
                settings.MediaUseXboxStore.ToString(),
                settings.MediaUseEpicStore.ToString(),
                settings.MediaUseSteamGridDb.ToString(),
                settings.MediaUseSteamGridDbBackgroundGrids.ToString(),
                settings.MediaUseRawg.ToString(),
                settings.MediaUseMobyGames.ToString(),
                settings.MediaUseIgdb.ToString(),
                GetPlayniteCoverRatioCacheKey(),
                settings.MediaCoverSourcePriority,
                settings.MediaIconSourcePriority,
                settings.MediaBackgroundSourcePriority
            };

            return string.Join("|", parts.Select(x => x ?? string.Empty));
        }

        private static MediaCandidate CloneCandidate(MediaCandidate candidate)
        {
            return candidate == null
                ? null
                : new MediaCandidate
                {
                    Url = candidate.Url,
                    Width = candidate.Width,
                    Height = candidate.Height,
                    Style = candidate.Style,
                    Mime = candidate.Mime,
                    IsNsfw = candidate.IsNsfw,
                    IsHumor = candidate.IsHumor,
                    Score = candidate.Score,
                    Extension = candidate.Extension,
                    SourceName = candidate.SourceName,
                    SourcePriority = candidate.SourcePriority,
                    IsOfficial = candidate.IsOfficial
                };
        }

        private async Task<string> ResolveSteamAppId(Game game, CancellationToken cancelToken)
        {
            var direct = GetSteamAppId(game);
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return null;
            }

            try
            {
                foreach (var title in BuildTitleAliases(game.Name))
                {
                    var url = "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(title) + "&cc=us&l=en";
                    var json = await GetPublicJson(url, cancelToken).ConfigureAwait(false);
                    var items = json["items"] as JArray;
                    if (items == null || items.Count == 0)
                    {
                        continue;
                    }

                    var exact = items
                        .OfType<JObject>()
                        .FirstOrDefault(x => IsGoodSteamTitleMatch(title, (string)x["name"]));
                    var id = exact == null ? 0 : ((int?)exact["id"] ?? 0);
                    if (id > 0)
                    {
                        return id.ToString();
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsGoodSteamTitleMatch(string expected, string candidate)
        {
            return TitleMatchingService.IsReliableMatch(expected, candidate);
        }

        private static List<string> BuildTitleAliases(string value)
        {
            return TitleMatchingService.BuildAliases(value);
        }

        private static List<MediaCandidate> DeduplicateCandidates(List<MediaCandidate> candidates)
        {
            var result = new List<MediaCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates ?? new List<MediaCandidate>())
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.Url) || seen.Contains(candidate.Url))
                {
                    continue;
                }

                seen.Add(candidate.Url);
                result.Add(candidate);
            }

            return result;
        }

        private List<MediaCandidate> GetSteamOfficialCandidates(string steamId, MediaKind kind)
        {
            var candidates = new List<MediaCandidate>();
            if (string.IsNullOrWhiteSpace(steamId))
            {
                return candidates;
            }

            var baseUrl = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + Uri.EscapeDataString(steamId) + "/";
            if (kind == MediaKind.Cover)
            {
                candidates.Add(CreateSteamCandidate(baseUrl + "library_600x900.jpg", 600, 900, "oficial vertical", 100, ".jpg"));
                candidates.Add(CreateSteamCandidate(baseUrl + "library_header.jpg", 920, 430, "oficial horizontal", 92, ".jpg"));
                candidates.Add(CreateSteamCandidate(baseUrl + "capsule_616x353.jpg", 616, 353, "oficial horizontal", 86, ".jpg"));
                candidates.Add(CreateSteamCandidate(baseUrl + "header.jpg", 460, 215, "oficial horizontal", 82, ".jpg"));
            }
            else if (kind == MediaKind.Icon)
            {
                candidates.Add(CreateSteamCandidate(baseUrl + "logo.png", 640, 360, "oficial logo", 96, ".png"));
            }
            else
            {
                candidates.Add(CreateSteamCandidate(baseUrl + "library_hero.jpg", 3840, 1240, "oficial hero", 100, ".jpg"));
                candidates.Add(CreateSteamCandidate(baseUrl + "page_bg_generated_v6b.jpg", 1438, 810, "oficial fondo", 82, ".jpg"));
                candidates.Add(CreateSteamCandidate(baseUrl + "page_bg_generated.jpg", 1438, 810, "oficial fondo", 80, ".jpg"));
                candidates.Add(CreateSteamCandidate(baseUrl + "library_header.jpg", 920, 430, "oficial horizontal", 68, ".jpg"));
            }

            return candidates;
        }

        private static MediaCandidate CreateSteamCandidate(string url, int width, int height, string style, int score, string extension)
        {
            return new MediaCandidate
            {
                Url = url,
                Width = width,
                Height = height,
                Style = style,
                Mime = string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg",
                IsNsfw = false,
                IsHumor = false,
                Score = score,
                Extension = extension,
                SourceName = "Steam oficial",
                SourcePriority = 100,
                IsOfficial = true
            };
        }

        private async Task<List<MediaCandidate>> GetSteamStoreCandidates(string steamId, MediaKind kind, CancellationToken cancelToken)
        {
            if (kind != MediaKind.Background || string.IsNullOrWhiteSpace(steamId))
            {
                return new List<MediaCandidate>();
            }

            try
            {
                var json = await GetPublicJson("https://store.steampowered.com/api/appdetails?appids=" + Uri.EscapeDataString(steamId) + "&filters=screenshots", cancelToken).ConfigureAwait(false);
                var screenshots = json[steamId] == null ? null : json[steamId]["data"] == null ? null : json[steamId]["data"]["screenshots"] as JArray;
                if (screenshots == null)
                {
                    return new List<MediaCandidate>();
                }

                return screenshots
                    .OfType<JObject>()
                    .Select(x => (string)x["path_full"])
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(10)
                    .Select(x => new MediaCandidate
                    {
                        Url = x,
                        Width = 1920,
                        Height = 1080,
                        Style = "captura oficial",
                        Mime = "image/jpeg",
                        IsNsfw = false,
                        IsHumor = false,
                        Score = 58,
                        Extension = ".jpg",
                        SourceName = "Steam capturas",
                        SourcePriority = 55,
                        IsOfficial = true
                    })
                    .ToList();
            }
            catch
            {
                return new List<MediaCandidate>();
            }
        }

        private async Task<int> ResolveSteamGridDbGameId(Game game, CancellationToken cancelToken)
        {
            var steamId = GetSteamAppId(game);
            if (!string.IsNullOrWhiteSpace(steamId))
            {
                var bySteam = await GetJson(ApiBase + "/games/steam/" + Uri.EscapeDataString(steamId), cancelToken).ConfigureAwait(false);
                var id = ExtractGameId(bySteam);
                if (id > 0)
                {
                    return id;
                }
            }

            foreach (var title in BuildTitleAliases(game.Name))
            {
                var search = await GetJson(ApiBase + "/search/autocomplete/" + Uri.EscapeDataString(title), cancelToken).ConfigureAwait(false);
                var data = search["data"] as JArray;
                if (data == null || data.Count == 0)
                {
                    continue;
                }

                var exact = data
                    .OfType<JObject>()
                    .FirstOrDefault(x => IsGoodSteamTitleMatch(title, (string)x["name"]));
                var id = exact == null ? 0 : ((int?)exact["id"] ?? 0);
                if (id > 0)
                {
                    return id;
                }
            }

            return 0;
        }

        private static int ExtractGameId(JToken token)
        {
            if (token == null)
            {
                return 0;
            }

            var data = token["data"];
            if (data is JObject)
            {
                return (int?)data["id"] ?? 0;
            }

            if (data is JArray)
            {
                var first = ((JArray)data).FirstOrDefault();
                return first == null ? 0 : ((int?)first["id"] ?? 0);
            }

            return 0;
        }

        private async Task<List<MediaCandidate>> GetCandidates(int gameId, MediaKind kind, CancellationToken cancelToken)
        {
            var path = kind == MediaKind.Cover ? "grids" : kind == MediaKind.Icon ? "icons" : "heroes";
            var query = BuildAssetQuery(kind, true, true);
            var json = await TryGetJson(ApiBase + "/" + path + "/game/" + gameId + query, cancelToken).ConfigureAwait(false);
            var data = json["data"] as JArray;
            if ((data == null || data.Count == 0) && query.IndexOf("styles=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                query = BuildAssetQuery(kind, false, true);
                json = await TryGetJson(ApiBase + "/" + path + "/game/" + gameId + query, cancelToken).ConfigureAwait(false);
                data = json["data"] as JArray;
            }

            if ((data == null || data.Count == 0) && query.IndexOf("dimensions=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                query = BuildAssetQuery(kind, false, false);
                json = await TryGetJson(ApiBase + "/" + path + "/game/" + gameId + query, cancelToken).ConfigureAwait(false);
                data = json["data"] as JArray;
            }

            if (data == null)
            {
                return new List<MediaCandidate>();
            }

            return data
                .OfType<JObject>()
                .Select(ParseCandidate)
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .ToList();
        }

        private async Task<List<MediaCandidate>> GetSteamGridDbBackgroundGridCandidates(int gameId, CancellationToken cancelToken)
        {
            try
            {
                var query = "?types=static";
                if (settings.MediaAvoidNsfw)
                {
                    query += "&nsfw=false";
                }

                var json = await GetJson(ApiBase + "/grids/game/" + gameId + query, cancelToken).ConfigureAwait(false);
                var data = json["data"] as JArray;
                if (data == null || data.Count == 0)
                {
                    return new List<MediaCandidate>();
                }

                return data
                    .OfType<JObject>()
                    .Select(ParseCandidate)
                    .Where(x => !string.IsNullOrWhiteSpace(x.Url) && x.Width > x.Height)
                    .Select(x =>
                    {
                        x.SourceName = "SteamGridDB grid";
                        x.SourcePriority = 62;
                        x.Style = string.IsNullOrWhiteSpace(x.Style) ? "grid horizontal" : x.Style + " grid";
                        return x;
                    })
                    .Take(Math.Max(1, Math.Min(16, settings.MediaSearchMaxResults)))
                    .ToList();
            }
            catch
            {
                return new List<MediaCandidate>();
            }
        }

        private async Task<JObject> TryGetJson(string url, CancellationToken cancelToken)
        {
            try
            {
                return await GetJson(url, cancelToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new JObject();
            }
        }

        private static MediaCandidate ParseCandidate(JObject item)
        {
            var url = (string)item["url"];
            var mime = (string)item["mime"] ?? string.Empty;
            return new MediaCandidate
            {
                Url = url,
                Width = (int?)item["width"] ?? 0,
                Height = (int?)item["height"] ?? 0,
                Style = ((string)item["style"] ?? string.Empty).ToLowerInvariant(),
                Mime = mime,
                IsNsfw = (bool?)item["nsfw"] ?? false,
                IsHumor = (bool?)item["humor"] ?? false,
                Score = (int?)item["score"] ?? 0,
                Extension = ExtensionFromUrl(url, mime),
                SourceName = "SteamGridDB",
                SourcePriority = 70,
                IsOfficial = (((string)item["style"] ?? string.Empty).IndexOf("official", StringComparison.OrdinalIgnoreCase) >= 0)
            };
        }

        private async Task<List<MediaCandidate>> GetRawgCandidates(Game game, MediaKind kind, CancellationToken cancelToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name) || string.IsNullOrWhiteSpace(settings.RawgApiKey))
            {
                return new List<MediaCandidate>();
            }

            JObject selected = null;
            foreach (var title in BuildTitleAliases(game.Name))
            {
                var searchUrl = "https://api.rawg.io/api/games?key=" + Uri.EscapeDataString(settings.RawgApiKey) +
                                "&search=" + Uri.EscapeDataString(title) + "&page_size=5";
                var search = await GetPublicJson(searchUrl, cancelToken).ConfigureAwait(false);
                selected = (search["results"] as JArray ?? new JArray())
                    .OfType<JObject>()
                    .FirstOrDefault(x => IsGoodSteamTitleMatch(title, (string)x["name"]));
                if (selected != null)
                {
                    break;
                }
            }

            if (selected == null)
            {
                return new List<MediaCandidate>();
            }

            var result = new List<MediaCandidate>();
            var background = (string)selected["background_image"];
            if (!string.IsNullOrWhiteSpace(background) && kind != MediaKind.Icon)
            {
                result.Add(CreateExternalCandidate(background, kind, 1920, 1080, "imagen principal", 70, "RAWG", 52, false));
            }

            if (kind == MediaKind.Background)
            {
                var id = (int?)selected["id"] ?? 0;
                if (id > 0)
                {
                    var screenshotsUrl = "https://api.rawg.io/api/games/" + id + "/screenshots?key=" + Uri.EscapeDataString(settings.RawgApiKey) + "&page_size=12";
                    var screenshots = await GetPublicJson(screenshotsUrl, cancelToken).ConfigureAwait(false);
                    foreach (var shot in (screenshots["results"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        var url = (string)shot["image"];
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            result.Add(CreateExternalCandidate(url, kind, 1920, 1080, "captura", 58, "RAWG", 48, false));
                        }
                    }
                }
            }

            return result;
        }

        private async Task<List<MediaCandidate>> GetMobyGamesCandidates(Game game, MediaKind kind, CancellationToken cancelToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name) || string.IsNullOrWhiteSpace(settings.MobyGamesApiKey))
            {
                return new List<MediaCandidate>();
            }

            JObject selected = null;
            foreach (var title in BuildTitleAliases(game.Name))
            {
                var url = "https://api.mobygames.com/v1/games?api_key=" + Uri.EscapeDataString(settings.MobyGamesApiKey) +
                          "&title=" + Uri.EscapeDataString(title) + "&format=normal&limit=5";
                var json = await GetPublicJson(url, cancelToken).ConfigureAwait(false);
                selected = (json["games"] as JArray ?? new JArray())
                    .OfType<JObject>()
                    .FirstOrDefault(x => IsGoodSteamTitleMatch(title, (string)x["title"]));
                if (selected != null)
                {
                    break;
                }
            }

            if (selected == null)
            {
                return new List<MediaCandidate>();
            }

            var result = new List<MediaCandidate>();
            if (kind == MediaKind.Cover)
            {
                var cover = selected["sample_cover"] as JObject;
                var coverUrl = cover == null ? null : (string)cover["image"];
                if (!string.IsNullOrWhiteSpace(coverUrl))
                {
                    result.Add(CreateExternalCandidate(
                        coverUrl,
                        kind,
                        (int?)cover["width"] ?? 800,
                        (int?)cover["height"] ?? 1000,
                        "cover",
                        62,
                        "MobyGames",
                        44,
                        false));
                }
            }

            if (kind == MediaKind.Background)
            {
                foreach (var shot in (selected["sample_screenshots"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var shotUrl = (string)shot["image"];
                    if (!string.IsNullOrWhiteSpace(shotUrl))
                    {
                        result.Add(CreateExternalCandidate(
                            shotUrl,
                            kind,
                            (int?)shot["width"] ?? 640,
                            (int?)shot["height"] ?? 480,
                            "captura",
                            46,
                            "MobyGames",
                            40,
                            false));
                    }
                }
            }

            return result;
        }

        private async Task<List<MediaCandidate>> GetIgdbCandidates(Game game, MediaKind kind, CancellationToken cancelToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name) ||
                string.IsNullOrWhiteSpace(settings.IgdbClientId) ||
                (string.IsNullOrWhiteSpace(settings.IgdbAccessToken) && string.IsNullOrWhiteSpace(settings.IgdbClientSecret)))
            {
                return new List<MediaCandidate>();
            }

            JObject selected = null;
            foreach (var title in BuildTitleAliases(game.Name))
            {
                var gameQuery = "search \"" + EscapeIgdbString(title) + "\"; fields id,name,cover,screenshots,artworks; limit 5;";
                var games = await PostIgdb("games", gameQuery, cancelToken).ConfigureAwait(false);
                selected = games.OfType<JObject>().FirstOrDefault(x => IsGoodSteamTitleMatch(title, (string)x["name"]));
                if (selected != null)
                {
                    break;
                }
            }

            if (selected == null)
            {
                return new List<MediaCandidate>();
            }

            var result = new List<MediaCandidate>();
            if (kind == MediaKind.Cover && selected["cover"] != null)
            {
                var coverId = (int?)selected["cover"] ?? 0;
                if (coverId > 0)
                {
                    var covers = await PostIgdb("covers", "where id = " + coverId + "; fields height,image_id,width;", cancelToken).ConfigureAwait(false);
                    foreach (var cover in covers.OfType<JObject>())
                    {
                        var imageId = (string)cover["image_id"];
                        if (!string.IsNullOrWhiteSpace(imageId))
                        {
                            result.Add(CreateExternalCandidate(
                                BuildIgdbImageUrl(imageId),
                                kind,
                                (int?)cover["width"] ?? 800,
                                (int?)cover["height"] ?? 1000,
                                "cover",
                                70,
                                "IGDB",
                                50,
                                true));
                        }
                    }
                }
            }

            if (kind == MediaKind.Background)
            {
                result.AddRange(await GetIgdbImageCandidates("artworks", selected["artworks"] as JArray, kind, "artwork", 65, cancelToken).ConfigureAwait(false));
                result.AddRange(await GetIgdbImageCandidates("screenshots", selected["screenshots"] as JArray, kind, "captura", 55, cancelToken).ConfigureAwait(false));
            }

            return result;
        }

        private async Task<List<MediaCandidate>> GetIgdbImageCandidates(string endpoint, JArray ids, MediaKind kind, string style, int score, CancellationToken cancelToken)
        {
            if (ids == null || ids.Count == 0)
            {
                return new List<MediaCandidate>();
            }

            var idList = string.Join(",", ids.Select(x => ((int?)x ?? 0).ToString()).Where(x => x != "0"));
            if (string.IsNullOrWhiteSpace(idList))
            {
                return new List<MediaCandidate>();
            }

            var items = await PostIgdb(endpoint, "where id = (" + idList + "); fields height,image_id,width; limit 20;", cancelToken).ConfigureAwait(false);
            return items
                .OfType<JObject>()
                .Select(x => new
                {
                    ImageId = (string)x["image_id"],
                    Width = (int?)x["width"] ?? 1920,
                    Height = (int?)x["height"] ?? 1080
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.ImageId))
                .Select(x => CreateExternalCandidate(BuildIgdbImageUrl(x.ImageId), kind, x.Width, x.Height, style, score, "IGDB", 50, true))
                .ToList();
        }

        private static MediaCandidate CreateExternalCandidate(string url, MediaKind kind, int width, int height, string style, int score, string sourceName, int sourcePriority, bool isOfficial)
        {
            return new MediaCandidate
            {
                Url = url,
                Width = width,
                Height = height,
                Style = style,
                Mime = ExtensionFromUrl(url).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg",
                IsNsfw = false,
                IsHumor = false,
                Score = score,
                Extension = ExtensionFromUrl(url),
                SourceName = sourceName,
                SourcePriority = sourcePriority,
                IsOfficial = isOfficial
            };
        }

        private static MediaCandidate CreateOfficialStoreCandidate(OfficialMediaCandidate candidate)
        {
            if (candidate == null)
            {
                return null;
            }

            var extension = string.IsNullOrWhiteSpace(candidate.Extension) ? ExtensionFromUrl(candidate.Url) : candidate.Extension;
            return new MediaCandidate
            {
                Url = candidate.Url,
                Width = candidate.Width,
                Height = candidate.Height,
                Style = candidate.Style,
                Mime = string.IsNullOrWhiteSpace(candidate.Mime)
                    ? (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg")
                    : candidate.Mime,
                IsNsfw = false,
                IsHumor = false,
                Score = candidate.Score,
                Extension = extension,
                SourceName = candidate.SourceName,
                SourcePriority = 60,
                IsOfficial = candidate.IsOfficial
            };
        }

        private static string BuildIgdbImageUrl(string imageId)
        {
            return "https://images.igdb.com/igdb/image/upload/t_original/" + imageId + ".jpg";
        }

        private static string EscapeIgdbString(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private MediaCandidate ChooseCandidate(List<MediaCandidate> candidates, MediaKind kind)
        {
            var ordered = OrderCandidates(candidates, kind)
                .Take(settings.MediaSearchMaxResults)
                .ToList();

            var selected = ordered.FirstOrDefault();
            return selected ?? candidates.First();
        }

        private List<MediaCandidate> GetAutomaticCandidates(List<MediaCandidate> candidates, MediaKind kind)
        {
            var available = candidates ?? new List<MediaCandidate>();
            if (settings.MediaAutomaticPriority != MetaDataIASettings.MediaPriorityStrictQuality)
            {
                return available;
            }

            var target = GetTargetSize(kind);
            return available.Where(x => IsCandidateLargeEnoughForTarget(x, target)).ToList();
        }

        private IEnumerable<MediaCandidate> OrderCandidates(List<MediaCandidate> candidates, MediaKind kind)
        {
            var target = GetTargetSize(kind);
            var available = candidates ?? new List<MediaCandidate>();

            if (settings.MediaAutomaticPriority == MetaDataIASettings.MediaPrioritySourceFirst)
            {
                return available
                    .OrderByDescending(x => UserSourcePriorityScore(x, kind))
                    .ThenByDescending(x => SourceScore(x))
                    .ThenByDescending(x => FormatScore(x, kind, target))
                    .ThenByDescending(x => LogoScore(x, kind))
                    .ThenByDescending(x => IsCandidateLargeEnoughForTarget(x, target))
                    .ThenByDescending(x => x.Score)
                    .ThenByDescending(x => UsablePixelArea(x, target));
            }

            if (settings.MediaAutomaticPriority == MetaDataIASettings.MediaPriorityResolutionFirst ||
                settings.MediaAutomaticPriority == MetaDataIASettings.MediaPriorityStrictQuality)
            {
                return available
                    .OrderByDescending(x => IsCandidateLargeEnoughForTarget(x, target))
                    .ThenByDescending(x => UsablePixelArea(x, target))
                    .ThenByDescending(x => FormatScore(x, kind, target))
                    .ThenByDescending(x => UserSourcePriorityScore(x, kind))
                    .ThenByDescending(x => SourceScore(x))
                    .ThenByDescending(x => LogoScore(x, kind))
                    .ThenByDescending(x => x.Score);
            }

            return available
                .OrderByDescending(x => FormatScore(x, kind, target))
                .ThenByDescending(x => UserSourcePriorityScore(x, kind))
                .ThenByDescending(x => SourceScore(x))
                .ThenByDescending(x => LogoScore(x, kind))
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => (long)x.Width * x.Height)
                .ThenBy(x => target.Width <= 0 || target.Height <= 0 ? 0 : Math.Abs(x.Width - target.Width) + Math.Abs(x.Height - target.Height));
        }

        private static bool IsCandidateLargeEnoughForTarget(MediaCandidate candidate, Size target)
        {
            if (target.Width <= 0 || target.Height <= 0)
            {
                return true;
            }

            if (candidate == null || candidate.Width <= 0 || candidate.Height <= 0)
            {
                return false;
            }

            var crop = GetCropRectangle(candidate.Width, candidate.Height, target.Width, target.Height, MetaDataIASettings.CropAnchorCenter);
            return crop.Width >= target.Width && crop.Height >= target.Height;
        }

        private static long UsablePixelArea(MediaCandidate candidate, Size target)
        {
            if (candidate == null || candidate.Width <= 0 || candidate.Height <= 0)
            {
                return 0;
            }

            if (target.Width <= 0 || target.Height <= 0)
            {
                return (long)candidate.Width * candidate.Height;
            }

            var crop = GetCropRectangle(candidate.Width, candidate.Height, target.Width, target.Height, MetaDataIASettings.CropAnchorCenter);
            return (long)crop.Width * crop.Height;
        }

        private int SourceScore(MediaCandidate candidate)
        {
            if (candidate == null)
            {
                return 0;
            }

            var score = candidate.SourcePriority;
            if (settings.MediaPreferOfficial && candidate.IsOfficial)
            {
                score += 35;
            }

            return score;
        }

        private int UserSourcePriorityScore(MediaCandidate candidate, MediaKind kind)
        {
            var order = GetSourcePriorityOrder(kind);
            if (candidate == null || order.Count == 0)
            {
                return 0;
            }

            var source = NormalizeSourceName(candidate.SourceName);
            for (var index = 0; index < order.Count; index++)
            {
                if (string.Equals(order[index], source, StringComparison.OrdinalIgnoreCase))
                {
                    return (order.Count - index) * 100;
                }
            }

            return 0;
        }

        private List<MediaCandidate> FilterCandidatesByKindSource(List<MediaCandidate> candidates, MediaKind kind)
        {
            var order = GetSourcePriorityOrder(kind);
            if (order.Count == 0)
            {
                return candidates ?? new List<MediaCandidate>();
            }

            return (candidates ?? new List<MediaCandidate>())
                .Where(x => order.Contains(NormalizeSourceName(x == null ? null : x.SourceName), StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        private List<string> GetSourcePriorityOrder(MediaKind kind)
        {
            var value = kind == MediaKind.Cover
                ? settings.MediaCoverSourcePriority
                : kind == MediaKind.Icon
                    ? settings.MediaIconSourcePriority
                    : settings.MediaBackgroundSourcePriority;

            if (string.IsNullOrWhiteSpace(value))
            {
                value = kind == MediaKind.Cover
                    ? MetaDataIASettings.DefaultCoverSourcePriority
                    : kind == MediaKind.Icon
                        ? MetaDataIASettings.DefaultIconSourcePriority
                        : MetaDataIASettings.DefaultBackgroundSourcePriority;
            }

            return value
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeSourceName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeSourceName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().ToLowerInvariant();
            if (normalized.Contains("integracion de origen") || normalized.Contains("origin integration"))
            {
                return MetaDataIASettings.SourceOriginIntegration.ToLowerInvariant();
            }

            if (normalized.Contains("steamgriddb"))
            {
                return "steamgriddb";
            }

            if (normalized.Contains("steam capturas") || normalized.Contains("steam screenshots"))
            {
                return "steam capturas";
            }

            if (normalized.Contains("steam oficial") || normalized.Contains("official steam"))
            {
                return "steam oficial";
            }

            if (normalized.Contains("playstation") || normalized.Contains("psn"))
            {
                return "playstation store";
            }

            if (normalized.Contains("xbox") || normalized.Contains("microsoft store"))
            {
                return "xbox store";
            }

            if (normalized.Contains("epic"))
            {
                return "epic store";
            }

            if (normalized.Contains("rawg"))
            {
                return "rawg";
            }

            if (normalized.Contains("moby"))
            {
                return "mobygames";
            }

            if (normalized.Contains("igdb"))
            {
                return "igdb";
            }

            return normalized;
        }

        private int FormatScore(MediaCandidate candidate, MediaKind kind, Size target)
        {
            if (kind == MediaKind.Cover)
            {
                if (settings.CoverImagePreset == MetaDataIASettings.CoverPresetSquare)
                {
                    return candidate.Width == candidate.Height && candidate.Width >= 512 ? 100 : -100;
                }

                if (settings.CoverImagePreset == MetaDataIASettings.CoverPresetPlayniteDefined)
                {
                    var playniteTarget = GetPlayniteCoverTargetSize();
                    if (playniteTarget.Width > 0 && playniteTarget.Height > 0 && candidate.Width > 0 && candidate.Height > 0)
                    {
                        var targetRatio = (double)playniteTarget.Width / playniteTarget.Height;
                        var candidateRatio = (double)candidate.Width / candidate.Height;
                        return Math.Abs(candidateRatio - targetRatio) < 0.05 ? 90 : -60;
                    }

                    return candidate.Height > candidate.Width ? 70 : -40;
                }

                if (settings.CoverImagePreset == MetaDataIASettings.CoverPresetHorizontal)
                {
                    return candidate.Width > candidate.Height ? 80 : -80;
                }

                if (settings.CoverImagePreset == MetaDataIASettings.CoverPresetPlayniteVertical)
                {
                    return candidate.Height > candidate.Width ? 80 : -80;
                }
            }

            if (kind == MediaKind.Icon)
            {
                var score = candidate.Mime.IndexOf("png", StringComparison.OrdinalIgnoreCase) >= 0 ? 40 : 0;
                score += LooksLikeIconMime(candidate.Mime) ? 25 : 0;
                score += settings.MediaPreferOfficial && candidate.Style.IndexOf("official", StringComparison.OrdinalIgnoreCase) >= 0 ? 35 : 0;
                score += candidate.Width == candidate.Height ? 20 : -20;
                score += candidate.Width >= 256 ? 10 : 0;
                score += candidate.IsNsfw && settings.MediaAvoidNsfw ? -1000 : 0;
                return score;
            }

            if (target.Width > 0 && target.Height > 0)
            {
                var targetRatio = (double)target.Width / target.Height;
                var candidateRatio = candidate.Height == 0 ? 0 : (double)candidate.Width / candidate.Height;
                return Math.Abs(candidateRatio - targetRatio) < 0.05 ? 50 : 0;
            }

            var styleScore = 0;
            styleScore += settings.MediaPreferOfficial && candidate.Style.IndexOf("official", StringComparison.OrdinalIgnoreCase) >= 0 ? 35 : 0;
            styleScore += settings.MediaAvoidBlurred && candidate.Style.IndexOf("blurred", StringComparison.OrdinalIgnoreCase) >= 0 ? -60 : 0;
            styleScore += candidate.IsNsfw && settings.MediaAvoidNsfw ? -1000 : 0;
            styleScore += candidate.IsHumor ? -20 : 0;
            return styleScore;
        }

        private int LogoScore(MediaCandidate candidate, MediaKind kind)
        {
            if (kind != MediaKind.Background)
            {
                return 0;
            }

            var style = candidate.Style ?? string.Empty;
            var looksNoLogo = style.Contains("no_logo") || style.Contains("nologo") || style.Contains("no logo");
            var looksLogo = style.Contains("logo") && !looksNoLogo;

            if (settings.BackgroundLogoPreference == MetaDataIASettings.BackgroundLogoPreferNoLogo)
            {
                return looksNoLogo ? 2 : looksLogo ? -1 : 0;
            }

            if (settings.BackgroundLogoPreference == MetaDataIASettings.BackgroundLogoPreferLogo)
            {
                return looksLogo ? 2 : looksNoLogo ? -1 : 0;
            }

            return 0;
        }

        private async Task<JObject> GetJson(string url, CancellationToken cancelToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SteamGridDbApiKey);
                request.Headers.UserAgent.ParseAdd("MetaDataIAPlugin/1.0");
                using (var response = await Client.SendAsync(request, cancelToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(string.Format(Loc("MTDA_ErrorSteamGridDbImagesFailed", "SteamGridDB could not return images ({0}). Check the API key or try again later."), (int)response.StatusCode));
                    }

                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JObject.Parse(text);
                }
            }
        }

        private static async Task<JObject> GetPublicJson(string url, CancellationToken cancelToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.UserAgent.ParseAdd("MetaDataIAPlugin/1.0");
                using (var response = await Client.SendAsync(request, cancelToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return new JObject();
                    }

                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JObject.Parse(text);
                }
            }
        }

        private async Task<JArray> PostIgdb(string endpoint, string body, CancellationToken cancelToken)
        {
            var accessToken = await EnsureIgdbAccessToken(cancelToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new JArray();
            }

            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/" + endpoint))
            {
                request.Headers.Add("Client-ID", settings.IgdbClientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "text/plain");
                using (var response = await Client.SendAsync(request, cancelToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return new JArray();
                    }

                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JArray.Parse(text);
                }
            }
        }

        private async Task<string> EnsureIgdbAccessToken(CancellationToken cancelToken)
        {
            if (!string.IsNullOrWhiteSpace(generatedIgdbAccessToken))
            {
                return generatedIgdbAccessToken;
            }

            if (!string.IsNullOrWhiteSpace(settings.IgdbClientId) && !string.IsNullOrWhiteSpace(settings.IgdbClientSecret))
            {
                generatedIgdbAccessToken = await RequestIgdbAccessToken(cancelToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(generatedIgdbAccessToken))
                {
                    return generatedIgdbAccessToken;
                }
            }

            if (!string.IsNullOrWhiteSpace(settings.IgdbAccessToken))
            {
                return settings.IgdbAccessToken;
            }

            return null;
        }

        private async Task<string> RequestIgdbAccessToken(CancellationToken cancelToken)
        {
            var url = "https://id.twitch.tv/oauth2/token?client_id=" + Uri.EscapeDataString(settings.IgdbClientId) +
                "&client_secret=" + Uri.EscapeDataString(settings.IgdbClientSecret) +
                "&grant_type=client_credentials";

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.UserAgent.ParseAdd("MetaDataIAPlugin/1.0");
                using (var response = await Client.SendAsync(request, cancelToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var json = JObject.Parse(text);
                    var token = (string)json["access_token"];
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        settings.IgdbAccessToken = token;
                    }

                    return token;
                }
            }
        }

        private async Task<List<MediaCandidate>> ValidatePreviewCandidatesAsync(List<MediaCandidate> candidates, int maximum, CancellationToken cancelToken)
        {
            var validated = new List<MediaCandidate>();
            const int batchSize = 6;
            for (var offset = 0; offset < candidates.Count && validated.Count < maximum; offset += batchSize)
            {
                cancelToken.ThrowIfCancellationRequested();
                var batch = candidates.Skip(offset).Take(batchSize).ToList();
                var checks = batch.Select(x => ProbeMediaCandidateAsync(x, cancelToken)).ToArray();
                var results = await Task.WhenAll(checks).ConfigureAwait(false);
                for (var index = 0; index < batch.Count; index++)
                {
                    if (results[index])
                    {
                        validated.Add(batch[index]);
                    }
                }
            }

            return validated;
        }

        private async Task<bool> ProbeMediaCandidateAsync(MediaCandidate candidate, CancellationToken cancelToken)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.Url))
            {
                return false;
            }

            CandidateProbeResult cached;
            lock (CandidateCacheLock)
            {
                if (CandidateProbeCache.TryGetValue(candidate.Url, out cached))
                {
                    ApplyProbeResult(candidate, cached);
                    return cached.IsValid;
                }
            }

            CandidateProbeResult result;
            try
            {
                result = await ProbeImageAsync(candidate.Url, cancelToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                result = new CandidateProbeResult();
            }

            lock (CandidateCacheLock)
            {
                if (CandidateProbeCache.Count > 600)
                {
                    CandidateProbeCache.Clear();
                }

                CandidateProbeCache[candidate.Url] = result;
            }

            ApplyProbeResult(candidate, result);
            return result.IsValid;
        }

        private static async Task<CandidateProbeResult> ProbeImageAsync(string url, CancellationToken cancelToken)
        {
            var localPath = LocalFilePath(url);
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                var buffer = new byte[131072];
                var read = 0;
                using (var stream = File.OpenRead(localPath))
                {
                    while (read < buffer.Length)
                    {
                        var count = await stream.ReadAsync(buffer, read, buffer.Length - read, cancelToken).ConfigureAwait(false);
                        if (count <= 0)
                        {
                            break;
                        }

                        read += count;
                    }
                }

                var width = 0;
                var height = 0;
                var recognized = read > 0 && TryReadImageDimensions(buffer, read, out width, out height);
                return new CandidateProbeResult
                {
                    IsValid = recognized,
                    Width = width,
                    Height = height
                };
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.UserAgent.ParseAdd("MetaDataIAPlugin/1.0");
                request.Headers.Range = new RangeHeaderValue(0, 131071);
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancelToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return new CandidateProbeResult();
                    }

                    var buffer = new byte[131072];
                    var read = 0;
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        while (read < buffer.Length)
                        {
                            var count = await stream.ReadAsync(buffer, read, buffer.Length - read, cancelToken).ConfigureAwait(false);
                            if (count <= 0)
                            {
                                break;
                            }

                            read += count;
                        }
                    }

                    int width;
                    int height;
                    var recognized = TryReadImageDimensions(buffer, read, out width, out height);
                    var contentType = response.Content.Headers.ContentType == null
                        ? string.Empty
                        : response.Content.Headers.ContentType.MediaType ?? string.Empty;
                    var isImage = recognized || contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
                    return new CandidateProbeResult
                    {
                        IsValid = read > 0 && isImage,
                        Width = width,
                        Height = height
                    };
                }
            }
        }

        private static void ApplyProbeResult(MediaCandidate candidate, CandidateProbeResult result)
        {
            if (candidate == null || result == null || !result.IsValid)
            {
                return;
            }

            if (result.Width > 0 && result.Height > 0)
            {
                candidate.Width = result.Width;
                candidate.Height = result.Height;
            }
        }

        private static bool TryReadImageDimensions(byte[] bytes, int count, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (bytes == null || count < 10)
            {
                return false;
            }

            if (count >= 24 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                width = ReadBigEndianInt32(bytes, 16);
                height = ReadBigEndianInt32(bytes, 20);
                return width > 0 && height > 0;
            }

            if (count >= 10 &&
                bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            {
                width = bytes[6] | (bytes[7] << 8);
                height = bytes[8] | (bytes[9] << 8);
                return width > 0 && height > 0;
            }

            if (count >= 26 && bytes[0] == 0x42 && bytes[1] == 0x4D)
            {
                width = BitConverter.ToInt32(bytes, 18);
                height = Math.Abs(BitConverter.ToInt32(bytes, 22));
                return width > 0 && height > 0;
            }

            if (bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                var offset = 2;
                while (offset + 8 < count)
                {
                    if (bytes[offset] != 0xFF)
                    {
                        offset++;
                        continue;
                    }

                    while (offset < count && bytes[offset] == 0xFF)
                    {
                        offset++;
                    }

                    if (offset >= count)
                    {
                        break;
                    }

                    var marker = bytes[offset++];
                    if (marker == 0xD8 || marker == 0xD9)
                    {
                        continue;
                    }

                    if (offset + 1 >= count)
                    {
                        break;
                    }

                    var length = (bytes[offset] << 8) + bytes[offset + 1];
                    if (length < 2 || offset + length > count)
                    {
                        break;
                    }

                    if ((marker >= 0xC0 && marker <= 0xC3) ||
                        (marker >= 0xC5 && marker <= 0xC7) ||
                        (marker >= 0xC9 && marker <= 0xCB) ||
                        (marker >= 0xCD && marker <= 0xCF))
                    {
                        height = (bytes[offset + 3] << 8) + bytes[offset + 4];
                        width = (bytes[offset + 5] << 8) + bytes[offset + 6];
                        return width > 0 && height > 0;
                    }

                    offset += length;
                }

                return true;
            }

            if (count >= 12 &&
                bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            {
                return true;
            }

            if (count >= 8 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0)
            {
                width = bytes[6] == 0 ? 256 : bytes[6];
                height = bytes[7] == 0 ? 256 : bytes[7];
                return true;
            }

            return false;
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) |
                   (bytes[offset + 1] << 16) |
                   (bytes[offset + 2] << 8) |
                   bytes[offset + 3];
        }

        private static async Task<byte[]> DownloadBytes(string url, CancellationToken cancelToken)
        {
            var localPath = LocalFilePath(url);
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                return await Task.Run(() => File.ReadAllBytes(localPath), cancelToken).ConfigureAwait(false);
            }

            using (var response = await Client.GetAsync(url, cancelToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        private static string LocalFilePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (File.Exists(value))
            {
                return value;
            }

            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.IsFile && File.Exists(uri.LocalPath)
                ? uri.LocalPath
                : null;
        }

        private async Task<DownloadedCandidate> DownloadBestBytes(List<MediaCandidate> candidates, MediaCandidate preferred, MediaKind kind, CancellationToken cancelToken)
        {
            if (kind == MediaKind.Cover && settings.MediaAvoidConsoleCovers)
            {
                var cover = await TryDownloadNonConsoleCover(candidates, preferred, cancelToken).ConfigureAwait(false);
                if (cover != null)
                {
                    return cover;
                }
            }

            if (kind != MediaKind.Icon || settings.IconPreset != MetaDataIASettings.IconPresetOriginal)
            {
                if (kind == MediaKind.Icon && settings.IconPreset == MetaDataIASettings.IconPresetSquare)
                {
                    var icon = await TryDownloadNonCircularIcon(candidates, preferred, cancelToken).ConfigureAwait(false);
                    if (icon != null)
                    {
                        return icon;
                    }
                }

                var orderedFallback = candidates
                    .OrderByDescending(x => x == preferred ? 1 : 0)
                    .ThenByDescending(x => SourceScore(x))
                    .ThenByDescending(x => FormatScore(x, kind, GetTargetSize(kind)))
                    .ThenByDescending(x => x.Score)
                    .ToList();
                return await DownloadFirstAvailable(orderedFallback, cancelToken).ConfigureAwait(false);
            }

            var ordered = candidates
                .OrderByDescending(x => x == preferred ? 1 : 0)
                .ThenByDescending(x => FormatScore(x, kind, Size.Empty))
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => x.Width * x.Height)
                .Take(Math.Max(1, Math.Min(8, settings.MediaSearchMaxResults)))
                .ToList();

            DownloadedCandidate fallback = null;
            foreach (var candidate in ordered)
            {
                byte[] bytes;
                try
                {
                    bytes = await DownloadBytes(candidate.Url, cancelToken).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                var downloaded = new DownloadedCandidate { Candidate = candidate, Content = bytes };
                if (fallback == null)
                {
                    fallback = downloaded;
                }

                if (ImageHasUsefulAlpha(bytes))
                {
                    return downloaded;
                }
            }

            return fallback ?? await DownloadFirstAvailable(ordered, cancelToken).ConfigureAwait(false);
        }

        private static async Task<DownloadedCandidate> DownloadFirstAvailable(IEnumerable<MediaCandidate> candidates, CancellationToken cancelToken)
        {
            Exception lastError = null;
            foreach (var candidate in candidates ?? new List<MediaCandidate>())
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.Url))
                {
                    continue;
                }

                try
                {
                    return new DownloadedCandidate
                    {
                        Candidate = candidate,
                        Content = await DownloadBytes(candidate.Url, cancelToken).ConfigureAwait(false)
                    };
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new InvalidOperationException(Loc("MTDA_ErrorNoMediaCandidateDownloaded", "No media candidate could be downloaded."));
        }

        private async Task<DownloadedCandidate> TryDownloadNonConsoleCover(List<MediaCandidate> candidates, MediaCandidate preferred, CancellationToken cancelToken)
        {
            var ordered = OrderCandidates(candidates, MediaKind.Cover)
                .OrderByDescending(x => x == preferred ? 1 : 0)
                .Take(Math.Max(1, Math.Min(24, settings.MediaSearchMaxResults)))
                .ToList();

            DownloadedCandidate fallback = null;
            foreach (var candidate in ordered)
            {
                byte[] bytes;
                try
                {
                    bytes = await DownloadBytes(candidate.Url, cancelToken).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                var downloaded = new DownloadedCandidate { Candidate = candidate, Content = bytes };
                if (fallback == null)
                {
                    fallback = downloaded;
                }

                if (!LooksLikeConsoleCover(bytes))
                {
                    return downloaded;
                }
            }

            return fallback;
        }

        private ProcessedImage ProcessImage(byte[] bytes, MediaKind kind, string originalExtension)
        {
            var target = GetTargetSize(kind);
            var forcePng = kind == MediaKind.Icon && settings.IconPreset != MetaDataIASettings.IconPresetOriginal;
            if (target.Width <= 0 || target.Height <= 0)
            {
                return new ProcessedImage { Content = bytes, Extension = DetectImageExtension(bytes, originalExtension) };
            }

            using (var source = LoadImage(bytes))
            using (var output = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(output))
            using (var ms = new MemoryStream())
            {
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.Clear(GetCanvasBackground(kind));

                var dest = new Rectangle(0, 0, target.Width, target.Height);
                var sourceRect = kind == MediaKind.Icon
                    ? new Rectangle(0, 0, source.Width, source.Height)
                    : GetCropRectangle(source.Width, source.Height, target.Width, target.Height, GetCropAnchor(kind));
                var drawRect = kind == MediaKind.Icon
                    ? GetFitRectangle(source.Width, source.Height, target.Width, target.Height)
                    : dest;

                if (kind == MediaKind.Icon && settings.IconPreset == MetaDataIASettings.IconPresetCircle)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(dest);
                        graphics.SetClip(path);
                        graphics.DrawImage(source, drawRect, sourceRect, GraphicsUnit.Pixel);
                    }
                }
                else if (kind == MediaKind.Icon && settings.IconPreset == MetaDataIASettings.IconPresetRounded)
                {
                    using (var path = RoundedRectangle(dest, 40))
                    {
                        graphics.SetClip(path);
                        graphics.DrawImage(source, drawRect, sourceRect, GraphicsUnit.Pixel);
                    }
                }
                else
                {
                    graphics.DrawImage(source, drawRect, sourceRect, GraphicsUnit.Pixel);
                }

                var saveAsPng = forcePng || kind == MediaKind.Icon;
                SaveProcessedImage(output, ms, saveAsPng);
                return new ProcessedImage
                {
                    Content = ms.ToArray(),
                    Extension = saveAsPng ? ".png" : ".jpg"
                };
            }
        }

        private void SaveProcessedImage(Image image, Stream stream, bool saveAsPng)
        {
            if (saveAsPng)
            {
                image.Save(stream, ImageFormat.Png);
                return;
            }

            var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(x => x.FormatID == ImageFormat.Jpeg.Guid);
            if (codec == null)
            {
                image.Save(stream, ImageFormat.Jpeg);
                return;
            }

            using (var parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, GetJpegQuality());
                image.Save(stream, codec, parameters);
            }
        }

        private static string DetectImageExtension(byte[] bytes, string fallback)
        {
            if (bytes != null)
            {
                if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                {
                    return ".jpg";
                }

                if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                {
                    return ".png";
                }

                if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
                {
                    return ".gif";
                }

                if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
                {
                    return ".bmp";
                }

                if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00)
                {
                    return ".ico";
                }

                if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                    bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                {
                    return ".webp";
                }
            }

            if (string.IsNullOrWhiteSpace(fallback))
            {
                return ".jpg";
            }

            return fallback.StartsWith(".", StringComparison.Ordinal) ? fallback : "." + fallback;
        }

        private long GetJpegQuality()
        {
            if (settings.ProcessedImageQuality == MetaDataIASettings.ImageQualitySpaceSaving)
            {
                return 72L;
            }

            if (settings.ProcessedImageQuality == MetaDataIASettings.ImageQualityHigh)
            {
                return 92L;
            }

            if (settings.ProcessedImageQuality == MetaDataIASettings.ImageQualityMaximum)
            {
                return 98L;
            }

            return 85L;
        }

        private string GetCropAnchor(MediaKind kind)
        {
            return kind == MediaKind.Cover ? settings.CoverCropAnchor : settings.BackgroundCropAnchor;
        }

        private async Task<DownloadedCandidate> TryDownloadSquareGridAsIcon(int gameId, CancellationToken cancelToken)
        {
            var candidates = await GetGridIconCandidates(gameId, cancelToken).ConfigureAwait(false);
            var selected = candidates.FirstOrDefault();
            if (selected == null)
            {
                return null;
            }

            return new DownloadedCandidate
            {
                Candidate = selected,
                Content = await DownloadBytes(selected.Url, cancelToken).ConfigureAwait(false)
            };
        }

        private async Task<List<MediaCandidate>> GetGridIconCandidates(int gameId, CancellationToken cancelToken)
        {
            var json = await GetJson(ApiBase + "/grids/game/" + gameId + "?types=static&dimensions=512x512,1024x1024&nsfw=false", cancelToken).ConfigureAwait(false);
            var data = json["data"] as JArray;
            if (data == null || data.Count == 0)
            {
                return new List<MediaCandidate>();
            }

            return data
                .OfType<JObject>()
                .Select(ParseCandidate)
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .Select(x =>
                {
                    x.SourceName = "SteamGridDB cover fallback";
                    x.SourcePriority = 32;
                    x.Style = string.IsNullOrWhiteSpace(x.Style) ? "square cover fallback" : x.Style + " square cover fallback";
                    return x;
                })
                .OrderByDescending(x => FormatScore(x, MediaKind.Cover, new Size(600, 600)))
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => x.Width * x.Height)
                .Take(Math.Max(1, Math.Min(8, settings.MediaSearchMaxResults)))
                .ToList();
        }

        private async Task<DownloadedCandidate> TryDownloadNonCircularIcon(List<MediaCandidate> candidates, MediaCandidate preferred, CancellationToken cancelToken)
        {
            var ordered = candidates
                .OrderByDescending(x => x == preferred ? 1 : 0)
                .ThenByDescending(x => FormatScore(x, MediaKind.Icon, Size.Empty))
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => x.Width * x.Height)
                .Take(Math.Max(1, Math.Min(12, settings.MediaSearchMaxResults)))
                .ToList();

            DownloadedCandidate fallback = null;
            foreach (var candidate in ordered)
            {
                byte[] bytes;
                try
                {
                    bytes = await DownloadBytes(candidate.Url, cancelToken).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                var analysis = AnalyzeAlpha(bytes);
                var downloaded = new DownloadedCandidate { Candidate = candidate, Content = bytes };
                if (fallback == null)
                {
                    fallback = downloaded;
                }

                if (!analysis.LooksCircularTransparent && !analysis.IsMostlyTransparent)
                {
                    return downloaded;
                }
            }

            return fallback;
        }

        private string BuildAssetQuery(MediaKind kind, bool includeStyleFilters, bool includeDimensionFilters)
        {
            var parts = new List<string> { "types=static" };
            if (settings.MediaAvoidNsfw)
            {
                parts.Add("nsfw=false");
            }

            if (kind == MediaKind.Cover)
            {
                if (includeDimensionFilters && settings.CoverImagePreset == MetaDataIASettings.CoverPresetSquare)
                {
                    parts.Add("dimensions=512x512,1024x1024");
                }
                else if (includeDimensionFilters && settings.CoverImagePreset == MetaDataIASettings.CoverPresetPlayniteDefined)
                {
                    var dimensions = GetPlayniteCoverSteamGridDimensions();
                    if (!string.IsNullOrWhiteSpace(dimensions))
                    {
                        parts.Add("dimensions=" + dimensions);
                    }
                }
                else if (includeDimensionFilters && settings.CoverImagePreset == MetaDataIASettings.CoverPresetHorizontal)
                {
                    parts.Add("dimensions=920x430,460x215");
                }
                else if (includeDimensionFilters && settings.CoverImagePreset == MetaDataIASettings.CoverPresetPlayniteVertical)
                {
                    parts.Add("dimensions=600x900,660x930,342x482");
                }

                if (includeStyleFilters && settings.MediaAvoidBlurred)
                {
                    parts.Add("styles=alternate,material,no_logo,white_logo");
                }
            }
            else if (kind == MediaKind.Icon)
            {
                if (includeDimensionFilters && settings.IconPreset != MetaDataIASettings.IconPresetOriginal)
                {
                    parts.Add("dimensions=256,512,1024,128");
                }
            }
            else if (kind == MediaKind.Background)
            {
                if (includeDimensionFilters && settings.BackgroundImagePreset == MetaDataIASettings.BackgroundPresetSteamHero)
                {
                    parts.Add("dimensions=3840x1240,1920x620,1600x650");
                }
                else if (includeDimensionFilters && settings.BackgroundImagePreset == MetaDataIASettings.BackgroundPresetSteamHeroSmall)
                {
                    parts.Add("dimensions=1920x620,1600x650,3840x1240");
                }

                if (includeStyleFilters)
                {
                    if (settings.BackgroundLogoPreference == MetaDataIASettings.BackgroundLogoPreferNoLogo)
                    {
                        parts.Add("styles=no_logo,material");
                    }
                    else if (settings.BackgroundLogoPreference == MetaDataIASettings.BackgroundLogoPreferLogo)
                    {
                        parts.Add("styles=alternate,white_logo");
                    }
                    else if (settings.MediaAvoidBlurred)
                    {
                        parts.Add("styles=alternate,material,no_logo,white_logo");
                    }
                }
            }

            return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
        }

        private string GetPlayniteCoverRatioCacheKey()
        {
            var ratio = GetPlayniteCoverRatio();
            return ratio.Width + "x" + ratio.Height;
        }

        private Size GetPlayniteCoverRatio()
        {
            try
            {
                var appSettings = playniteApi == null ? null : playniteApi.ApplicationSettings;
                var width = appSettings == null ? 0 : appSettings.GridItemWidthRatio;
                var height = appSettings == null ? 0 : appSettings.GridItemHeightRatio;
                if (width > 0 && height > 0)
                {
                    return new Size(width, height);
                }
            }
            catch
            {
            }

            return new Size(2, 3);
        }

        private Size GetPlayniteCoverTargetSize()
        {
            var ratio = GetPlayniteCoverRatio();
            if (ratio.Width <= 0 || ratio.Height <= 0)
            {
                return new Size(600, 900);
            }

            if (ratio.Width == ratio.Height)
            {
                return new Size(600, 600);
            }

            if (ratio.Width > ratio.Height)
            {
                var width = 920;
                var height = Math.Max(1, (int)Math.Round(width * ((double)ratio.Height / ratio.Width)));
                return new Size(width, height);
            }

            var targetHeight = 900;
            var targetWidth = Math.Max(1, (int)Math.Round(targetHeight * ((double)ratio.Width / ratio.Height)));
            return new Size(targetWidth, targetHeight);
        }

        private string GetPlayniteCoverSteamGridDimensions()
        {
            var ratio = GetPlayniteCoverRatio();
            if (ratio.Width <= 0 || ratio.Height <= 0)
            {
                return "600x900,660x930,342x482";
            }

            var normalized = (double)ratio.Width / ratio.Height;
            if (Math.Abs(normalized - 1.0) < 0.02)
            {
                return "512x512,1024x1024";
            }

            if (Math.Abs(normalized - (2.0 / 3.0)) < 0.05 ||
                Math.Abs(normalized - (3.0 / 4.0)) < 0.05)
            {
                return "600x900,660x930,342x482";
            }

            if (Math.Abs(normalized - (92.0 / 43.0)) < 0.08 ||
                normalized > 1.2)
            {
                return "920x430,460x215";
            }

            return string.Empty;
        }

        private Size GetTargetSize(MediaKind kind)
        {
            if (kind == MediaKind.Cover)
            {
                if (settings.CoverImagePreset == MetaDataIASettings.CoverPresetSquare)
                {
                    return new Size(600, 600);
                }

                if (settings.CoverImagePreset == MetaDataIASettings.CoverPresetPlayniteDefined)
                {
                    return GetPlayniteCoverTargetSize();
                }

                if (settings.CoverImagePreset == MetaDataIASettings.CoverPresetHorizontal)
                {
                    return new Size(920, 430);
                }

                if (settings.CoverImagePreset == MetaDataIASettings.CoverPresetOriginal)
                {
                    return Size.Empty;
                }

                return new Size(600, 900);
            }

            if (kind == MediaKind.Icon)
            {
                return settings.IconPreset == MetaDataIASettings.IconPresetOriginal ? Size.Empty : new Size(256, 256);
            }

            if (settings.BackgroundImagePreset == MetaDataIASettings.BackgroundPresetOriginal)
            {
                return Size.Empty;
            }

            if (settings.BackgroundImagePreset == MetaDataIASettings.BackgroundPresetSteamHeroSmall)
            {
                return new Size(1920, 620);
            }

            if (settings.BackgroundImagePreset == MetaDataIASettings.BackgroundPresetFullHd)
            {
                return new Size(1920, 1080);
            }

            if (settings.BackgroundImagePreset == MetaDataIASettings.BackgroundPresetQhd)
            {
                return new Size(2560, 1440);
            }

            if (settings.BackgroundImagePreset == MetaDataIASettings.BackgroundPreset4K)
            {
                return new Size(3840, 2160);
            }

            return new Size(3840, 1240);
        }

        private string GetApplyMode(MediaKind kind)
        {
            if (kind == MediaKind.Cover)
            {
                return settings.CoverImageApplyMode;
            }

            if (kind == MediaKind.Icon)
            {
                return settings.IconApplyMode;
            }

            return settings.BackgroundImageApplyMode;
        }

        private static string GetCurrentImage(Game game, MediaKind kind)
        {
            if (kind == MediaKind.Cover)
            {
                return game.CoverImage;
            }

            if (kind == MediaKind.Icon)
            {
                return game.Icon;
            }

            return game.BackgroundImage;
        }

        private static Rectangle GetCropRectangle(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, string anchor)
        {
            var sourceRatio = (double)sourceWidth / sourceHeight;
            var targetRatio = (double)targetWidth / targetHeight;
            if (sourceRatio > targetRatio)
            {
                var width = (int)Math.Round(sourceHeight * targetRatio);
                var remaining = sourceWidth - width;
                var x = IsLeftAnchor(anchor) ? 0 : IsRightAnchor(anchor) ? remaining : remaining / 2;
                return new Rectangle(x, 0, width, sourceHeight);
            }

            var height = (int)Math.Round(sourceWidth / targetRatio);
            var verticalRemaining = sourceHeight - height;
            var y = IsTopAnchor(anchor) ? 0 : IsBottomAnchor(anchor) ? verticalRemaining : verticalRemaining / 2;
            return new Rectangle(0, y, sourceWidth, height);
        }

        private static bool IsLeftAnchor(string anchor)
        {
            return anchor == MetaDataIASettings.CropAnchorLeft ||
                   anchor == MetaDataIASettings.CropAnchorTopLeft ||
                   anchor == MetaDataIASettings.CropAnchorBottomLeft;
        }

        private static bool IsRightAnchor(string anchor)
        {
            return anchor == MetaDataIASettings.CropAnchorRight ||
                   anchor == MetaDataIASettings.CropAnchorTopRight ||
                   anchor == MetaDataIASettings.CropAnchorBottomRight;
        }

        private static bool IsTopAnchor(string anchor)
        {
            return anchor == MetaDataIASettings.CropAnchorTop ||
                   anchor == MetaDataIASettings.CropAnchorTopLeft ||
                   anchor == MetaDataIASettings.CropAnchorTopRight;
        }

        private static bool IsBottomAnchor(string anchor)
        {
            return anchor == MetaDataIASettings.CropAnchorBottom ||
                   anchor == MetaDataIASettings.CropAnchorBottomLeft ||
                   anchor == MetaDataIASettings.CropAnchorBottomRight;
        }

        private static Rectangle GetFitRectangle(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
        {
            var scale = Math.Min((double)targetWidth / sourceWidth, (double)targetHeight / sourceHeight);
            var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            return new Rectangle((targetWidth - width) / 2, (targetHeight - height) / 2, width, height);
        }

        private Color GetCanvasBackground(MediaKind kind)
        {
            if (kind == MediaKind.Icon && settings.IconPreset == MetaDataIASettings.IconPresetSquare)
            {
                return Color.FromArgb(255, 32, 32, 32);
            }

            return Color.Transparent;
        }

        private static bool ImageHasUsefulAlpha(byte[] bytes)
        {
            return AnalyzeAlpha(bytes).HasUsefulAlpha;
        }

        private static Image LoadImage(byte[] bytes)
        {
            try
            {
                return Image.FromStream(new MemoryStream(bytes));
            }
            catch
            {
                using (var iconStream = new MemoryStream(bytes))
                using (var icon = new System.Drawing.Icon(iconStream))
                {
                    return icon.ToBitmap();
                }
            }
        }

        private static bool LooksLikeConsoleCover(byte[] bytes)
        {
            try
            {
                using (var image = LoadImage(bytes))
                using (var bitmap = new Bitmap(image))
                {
                    if (bitmap.Width < 80 || bitmap.Height < 120)
                    {
                        return false;
                    }

                    var bandHeight = Math.Max(8, bitmap.Height / 9);
                    var belowY = Math.Min(bitmap.Height - 1, bandHeight + Math.Max(4, bitmap.Height / 20));
                    var samples = 0;
                    var blue = 0;
                    var green = 0;
                    var white = 0;
                    var black = 0;
                    double bandBrightness = 0;
                    double belowBrightness = 0;

                    var stepX = Math.Max(1, bitmap.Width / 40);
                    var stepY = Math.Max(1, bandHeight / 6);
                    for (var y = 0; y < bandHeight; y += stepY)
                    {
                        for (var x = 0; x < bitmap.Width; x += stepX)
                        {
                            var color = bitmap.GetPixel(x, y);
                            var below = bitmap.GetPixel(x, belowY);
                            samples++;
                            bandBrightness += Brightness(color);
                            belowBrightness += Brightness(below);

                            if (color.B > 110 && color.B > color.R * 1.35 && color.B > color.G * 1.15)
                            {
                                blue++;
                            }

                            if (color.G > 100 && color.G > color.R * 1.25 && color.G > color.B * 1.25)
                            {
                                green++;
                            }

                            if (color.R > 210 && color.G > 210 && color.B > 210)
                            {
                                white++;
                            }

                            if (color.R < 45 && color.G < 45 && color.B < 45)
                            {
                                black++;
                            }
                        }
                    }

                    if (samples == 0)
                    {
                        return false;
                    }

                    var dominant = Math.Max(Math.Max(blue, green), Math.Max(white, black)) / (double)samples;
                    var brightnessDelta = Math.Abs((bandBrightness - belowBrightness) / samples);
                    var strongConsoleColor = blue / (double)samples > 0.42 || green / (double)samples > 0.42;
                    var possibleWhiteOrBlackBand = (white / (double)samples > 0.58 || black / (double)samples > 0.58) && brightnessDelta > 55;

                    if (dominant > 0.42 && (strongConsoleColor || possibleWhiteOrBlackBand))
                    {
                        return true;
                    }

                    return LooksLikeConsoleSideSpine(bitmap);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeConsoleSideSpine(Bitmap bitmap)
        {
            var spineWidth = Math.Max(10, bitmap.Width / 5);
            return LooksLikeConsoleSideSpine(bitmap, 0, spineWidth) ||
                   LooksLikeConsoleSideSpine(bitmap, bitmap.Width - spineWidth, spineWidth);
        }

        private static bool LooksLikeConsoleSideSpine(Bitmap bitmap, int startX, int width)
        {
            var samples = 0;
            var green = 0;
            var xboxGreen = 0;
            var blue = 0;
            var white = 0;
            var gray = 0;
            var dark = 0;
            var artSamples = 0;
            var spineBrightness = 0.0;
            var artBrightness = 0.0;

            var endX = Math.Min(bitmap.Width, startX + width);
            var artX = startX == 0
                ? Math.Min(bitmap.Width - 1, endX + Math.Max(4, bitmap.Width / 20))
                : Math.Max(0, startX - Math.Max(4, bitmap.Width / 20));
            var stepX = Math.Max(1, width / 8);
            var stepY = Math.Max(1, bitmap.Height / 42);

            for (var y = 0; y < bitmap.Height; y += stepY)
            {
                for (var x = startX; x < endX; x += stepX)
                {
                    var color = bitmap.GetPixel(x, y);
                    var art = bitmap.GetPixel(artX, y);
                    samples++;
                    artSamples++;
                    spineBrightness += Brightness(color);
                    artBrightness += Brightness(art);

                    if (color.G > 85 && color.G > color.R * 1.18 && color.G > color.B * 1.08)
                    {
                        green++;
                    }

                    if (color.G > 115 && color.R < 130 && color.B < 115)
                    {
                        xboxGreen++;
                    }

                    if (color.B > 110 && color.B > color.R * 1.25 && color.B > color.G * 1.08)
                    {
                        blue++;
                    }

                    if (color.R > 188 && color.G > 188 && color.B > 188)
                    {
                        white++;
                    }

                    if (Math.Abs(color.R - color.G) < 18 && Math.Abs(color.G - color.B) < 18 && color.R > 80 && color.R < 210)
                    {
                        gray++;
                    }

                    if (color.R < 55 && color.G < 55 && color.B < 55)
                    {
                        dark++;
                    }
                }
            }

            if (samples == 0 || artSamples == 0)
            {
                return false;
            }

            var greenRatio = green / (double)samples;
            var xboxGreenRatio = xboxGreen / (double)samples;
            var blueRatio = blue / (double)samples;
            var whiteRatio = white / (double)samples;
            var grayRatio = gray / (double)samples;
            var darkRatio = dark / (double)samples;
            var brightnessDelta = Math.Abs(spineBrightness / samples - artBrightness / artSamples);

            var xbox360Like = greenRatio > 0.20 && (whiteRatio + grayRatio) > 0.32 && brightnessDelta > 18;
            var xboxOneLike = xboxGreenRatio > 0.28 && darkRatio > 0.12;
            var playstationLike = blueRatio > 0.34 && brightnessDelta > 15;
            var neutralConsoleSpine = (whiteRatio > 0.54 || grayRatio > 0.54 || darkRatio > 0.54) && brightnessDelta > 42;

            return xbox360Like || xboxOneLike || playstationLike || neutralConsoleSpine;
        }

        private static double Brightness(Color color)
        {
            return color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
        }

        private static ImageAlphaAnalysis AnalyzeAlpha(byte[] bytes)
        {
            try
            {
                using (var image = LoadImage(bytes))
                using (var bitmap = new Bitmap(image))
                {
                    if (!Image.IsAlphaPixelFormat(bitmap.PixelFormat))
                    {
                        return new ImageAlphaAnalysis();
                    }

                    var stepX = Math.Max(1, bitmap.Width / 24);
                    var stepY = Math.Max(1, bitmap.Height / 24);
                    var transparent = 0;
                    var sampled = 0;
                    var cornerTransparent = 0;
                    var cornerSampled = 0;
                    var centerOpaque = 0;
                    var centerSampled = 0;
                    for (var y = 0; y < bitmap.Height; y += stepY)
                    {
                        for (var x = 0; x < bitmap.Width; x += stepX)
                        {
                            var alpha = bitmap.GetPixel(x, y).A;
                            var isTransparent = alpha < 250;
                            sampled++;
                            if (isTransparent)
                            {
                                transparent++;
                            }

                            var inCorner = (x < bitmap.Width * 0.18 || x > bitmap.Width * 0.82) &&
                                           (y < bitmap.Height * 0.18 || y > bitmap.Height * 0.82);
                            if (inCorner)
                            {
                                cornerSampled++;
                                if (isTransparent)
                                {
                                    cornerTransparent++;
                                }
                            }

                            var inCenter = x > bitmap.Width * 0.35 && x < bitmap.Width * 0.65 &&
                                           y > bitmap.Height * 0.35 && y < bitmap.Height * 0.65;
                            if (inCenter)
                            {
                                centerSampled++;
                                if (alpha > 220)
                                {
                                    centerOpaque++;
                                }
                            }
                        }
                    }

                    var transparentRatio = sampled == 0 ? 0 : (double)transparent / sampled;
                    var cornerTransparentRatio = cornerSampled == 0 ? 0 : (double)cornerTransparent / cornerSampled;
                    var centerOpaqueRatio = centerSampled == 0 ? 0 : (double)centerOpaque / centerSampled;
                    return new ImageAlphaAnalysis
                    {
                        HasUsefulAlpha = transparentRatio > 0.02,
                        IsMostlyTransparent = transparentRatio > 0.72,
                        LooksCircularTransparent = cornerTransparentRatio > 0.65 &&
                                                   centerOpaqueRatio > 0.20 &&
                                                   transparentRatio > 0.12 &&
                                                   transparentRatio < 0.72
                    };
                }
            }
            catch
            {
            }

            return new ImageAlphaAnalysis();
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static string GetSteamAppId(Game game)
        {
            if (game == null || game.Source == null || string.IsNullOrWhiteSpace(game.GameId))
            {
                return null;
            }

            return game.Source.Name.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0
                ? game.GameId
                : null;
        }

        private static string BuildFileName(string gameName, MediaKind kind, string extension)
        {
            var safeName = string.Join("_", (gameName ?? "game").Split(Path.GetInvalidFileNameChars()));
            var suffix = kind == MediaKind.Cover ? "cover" : kind == MediaKind.Icon ? "icon" : "background";
            return safeName + "_" + suffix + (string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension);
        }

        private static string ExtensionFromUrl(string url)
        {
            return ExtensionFromUrl(url, string.Empty);
        }

        private static string ExtensionFromUrl(string url, string mime)
        {
            try
            {
                var ext = Path.GetExtension(new Uri(url).AbsolutePath);
                if (!string.IsNullOrWhiteSpace(ext))
                {
                    return ext;
                }
            }
            catch
            {
            }

            return ExtensionFromMime(mime);
        }

        private static string ExtensionFromMime(string mime)
        {
            if (string.IsNullOrWhiteSpace(mime))
            {
                return ".jpg";
            }

            if (mime.IndexOf("png", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ".png";
            }

            if (LooksLikeIconMime(mime))
            {
                return ".ico";
            }

            if (mime.IndexOf("webp", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ".webp";
            }

            return ".jpg";
        }

        private static bool LooksLikeIconMime(string mime)
        {
            return !string.IsNullOrWhiteSpace(mime) &&
                   (mime.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mime.IndexOf("ico", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private class MediaCandidate
        {
            public string Url { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public string Style { get; set; }
            public string Mime { get; set; }
            public bool IsNsfw { get; set; }
            public bool IsHumor { get; set; }
            public int Score { get; set; }
            public string Extension { get; set; }
            public string SourceName { get; set; }
            public int SourcePriority { get; set; }
            public bool IsOfficial { get; set; }
        }

        private static string Loc(string key, string fallback)
        {
            return PluginLocalization.GetString(key, fallback);
        }

        private class DownloadedCandidate
        {
            public MediaCandidate Candidate { get; set; }
            public byte[] Content { get; set; }
        }

        private class CandidateProbeResult
        {
            public bool IsValid { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        private class ProcessedImage
        {
            public byte[] Content { get; set; }
            public string Extension { get; set; }
        }

        private class ImageAlphaAnalysis
        {
            public bool HasUsefulAlpha { get; set; }
            public bool LooksCircularTransparent { get; set; }
            public bool IsMostlyTransparent { get; set; }
        }
    }
}
