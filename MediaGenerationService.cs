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
        private readonly MetaDataIASettings settings;
        private string generatedIgdbAccessToken;

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

        public MediaGenerationService(MetaDataIASettings settings)
        {
            this.settings = settings;
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
                throw new InvalidOperationException(Loc("MTDA_ErrorNoImagesFound", "No images were found for this game in the configured media sources."));
            }

            var selected = ChooseCandidate(candidates, kind);
            var selectedBytes = await DownloadBestBytes(candidates, selected, kind, cancelToken).ConfigureAwait(false);
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

            return OrderCandidates(candidates, kind)
                .Take(Math.Max(1, settings.MediaSearchMaxResults))
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

            var bytes = await DownloadBytes(option.Url, cancelToken).ConfigureAwait(false);
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
            var steamId = await ResolveSteamAppId(game, cancelToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(steamId))
            {
                if (settings.MediaUseSteamOfficial)
                {
                    candidates.AddRange(GetSteamOfficialCandidates(steamId, kind));
                }

                if (settings.MediaUseSteamScreenshots)
                {
                    candidates.AddRange(await GetSteamStoreCandidates(steamId, kind, cancelToken).ConfigureAwait(false));
                }
            }

            if (settings.MediaUseSteamGridDb && !string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
            {
                try
                {
                    var gameId = await ResolveSteamGridDbGameId(game, cancelToken).ConfigureAwait(false);
                    if (gameId > 0)
                    {
                        candidates.AddRange(await GetCandidates(gameId, kind, cancelToken).ConfigureAwait(false));
                        if (kind == MediaKind.Icon &&
                            candidates.Count == 0 &&
                            settings.IconPreset == MetaDataIASettings.IconPresetSquare &&
                            settings.IconSquarePreferGrid)
                        {
                            candidates.AddRange(await GetGridIconCandidates(gameId, cancelToken).ConfigureAwait(false));
                        }

                        if (kind == MediaKind.Background && settings.MediaUseSteamGridDbBackgroundGrids)
                        {
                            candidates.AddRange(await GetSteamGridDbBackgroundGridCandidates(gameId, cancelToken).ConfigureAwait(false));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // SteamGridDB can reject specific filters with 400. Keep candidates from Steam or other sources.
                }
            }

            if (settings.MediaUseRawg && !string.IsNullOrWhiteSpace(settings.RawgApiKey))
            {
                try
                {
                    candidates.AddRange(await GetRawgCandidates(game, kind, cancelToken).ConfigureAwait(false));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }

            if (settings.MediaUseMobyGames && !string.IsNullOrWhiteSpace(settings.MobyGamesApiKey))
            {
                try
                {
                    candidates.AddRange(await GetMobyGamesCandidates(game, kind, cancelToken).ConfigureAwait(false));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }

            if (settings.MediaUseIgdb &&
                !string.IsNullOrWhiteSpace(settings.IgdbClientId) &&
                (!string.IsNullOrWhiteSpace(settings.IgdbAccessToken) || !string.IsNullOrWhiteSpace(settings.IgdbClientSecret)))
            {
                try
                {
                    candidates.AddRange(await GetIgdbCandidates(game, kind, cancelToken).ConfigureAwait(false));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }

            if (candidates.Count == 0 && settings.MediaUseSteamGridDb && string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey) && string.IsNullOrWhiteSpace(steamId))
            {
                EnsureConfigured();
            }

            var result = FilterCandidatesByKindSource(DeduplicateCandidates(candidates), kind);
            lock (CandidateCacheLock)
            {
                if (CandidateCache.Count > 120)
                {
                    CandidateCache.Clear();
                }

                CandidateCache[cacheKey] = result.Select(CloneCandidate).ToList();
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
                settings.MediaUseSteamGridDb.ToString(),
                settings.MediaUseSteamGridDbBackgroundGrids.ToString(),
                settings.MediaUseRawg.ToString(),
                settings.MediaUseMobyGames.ToString(),
                settings.MediaUseIgdb.ToString(),
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
                var url = "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(game.Name) + "&cc=us&l=en";
                var json = await GetPublicJson(url, cancelToken).ConfigureAwait(false);
                var items = json["items"] as JArray;
                if (items == null || items.Count == 0)
                {
                    return null;
                }

                var exact = items
                    .OfType<JObject>()
                    .FirstOrDefault(x => IsGoodSteamTitleMatch(game.Name, (string)x["name"]));
                var selected = exact ?? items.OfType<JObject>().FirstOrDefault();
                var id = selected == null ? 0 : ((int?)selected["id"] ?? 0);
                return id > 0 ? id.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsGoodSteamTitleMatch(string expected, string candidate)
        {
            var left = NormalizeTitle(expected);
            var right = NormalizeTitle(candidate);
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
                   right.StartsWith(left + " ", StringComparison.OrdinalIgnoreCase) ||
                   left.StartsWith(right + " ", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var chars = value
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
                .ToArray();
            return string.Join(" ", new string(chars).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
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

            var search = await GetJson(ApiBase + "/search/autocomplete/" + Uri.EscapeDataString(game.Name ?? string.Empty), cancelToken).ConfigureAwait(false);
            var data = search["data"] as JArray;
            if (data == null || data.Count == 0)
            {
                return 0;
            }

            var exact = data
                .OfType<JObject>()
                .FirstOrDefault(x => string.Equals((string)x["name"], game.Name, StringComparison.OrdinalIgnoreCase));

            var selected = exact ?? data.OfType<JObject>().FirstOrDefault();
            return selected == null ? 0 : ((int?)selected["id"] ?? 0);
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

            var searchUrl = "https://api.rawg.io/api/games?key=" + Uri.EscapeDataString(settings.RawgApiKey) +
                            "&search=" + Uri.EscapeDataString(game.Name) + "&page_size=1";
            var search = await GetPublicJson(searchUrl, cancelToken).ConfigureAwait(false);
            var selected = (search["results"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault();
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

            var url = "https://api.mobygames.com/v1/games?api_key=" + Uri.EscapeDataString(settings.MobyGamesApiKey) +
                      "&title=" + Uri.EscapeDataString(game.Name) + "&format=normal&limit=1";
            var json = await GetPublicJson(url, cancelToken).ConfigureAwait(false);
            var selected = (json["games"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault();
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

            var gameQuery = "search \"" + EscapeIgdbString(game.Name) + "\"; fields id,name,cover,screenshots,artworks; limit 1;";
            var games = await PostIgdb("games", gameQuery, cancelToken).ConfigureAwait(false);
            var selected = games.OfType<JObject>().FirstOrDefault();
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

        private IEnumerable<MediaCandidate> OrderCandidates(List<MediaCandidate> candidates, MediaKind kind)
        {
            var target = GetTargetSize(kind);
            return (candidates ?? new List<MediaCandidate>())
                .OrderByDescending(x => FormatScore(x, kind, target))
                .ThenByDescending(x => UserSourcePriorityScore(x, kind))
                .ThenByDescending(x => SourceScore(x))
                .ThenByDescending(x => LogoScore(x, kind))
                .ThenBy(x => target.Width <= 0 || target.Height <= 0 ? 0 : Math.Abs(x.Width - target.Width) + Math.Abs(x.Height - target.Height))
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => x.Width * x.Height);
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

        private static async Task<byte[]> DownloadBytes(string url, CancellationToken cancelToken)
        {
            using (var response = await Client.GetAsync(url, cancelToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
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
                return new ProcessedImage { Content = bytes, Extension = string.IsNullOrWhiteSpace(originalExtension) ? ".jpg" : originalExtension };
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
                    : GetCropRectangle(source.Width, source.Height, target.Width, target.Height);
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

                output.Save(ms, forcePng || kind == MediaKind.Icon ? ImageFormat.Png : ImageFormat.Jpeg);
                return new ProcessedImage
                {
                    Content = ms.ToArray(),
                    Extension = forcePng || kind == MediaKind.Icon ? ".png" : ".jpg"
                };
            }
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

        private Size GetTargetSize(MediaKind kind)
        {
            if (kind == MediaKind.Cover)
            {
                if (settings.CoverImagePreset == MetaDataIASettings.CoverPresetSquare)
                {
                    return new Size(600, 600);
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

        private static Rectangle GetCropRectangle(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
        {
            var sourceRatio = (double)sourceWidth / sourceHeight;
            var targetRatio = (double)targetWidth / targetHeight;
            if (sourceRatio > targetRatio)
            {
                var width = (int)Math.Round(sourceHeight * targetRatio);
                var x = (sourceWidth - width) / 2;
                return new Rectangle(x, 0, width, sourceHeight);
            }

            var height = (int)Math.Round(sourceWidth / targetRatio);
            var y = (sourceHeight - height) / 2;
            return new Rectangle(0, y, sourceWidth, height);
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
