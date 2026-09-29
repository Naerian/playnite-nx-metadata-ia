using Newtonsoft.Json.Linq;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    internal static class RetroDatabaseHttp
    {
        private static readonly HttpClient Client = new HttpClient();

        public static async Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return new JObject();
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.UserAgent.ParseAdd("MetaDataIAPlugin/1.0");
                using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return new JObject();
                    }

                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return new JObject();
                    }

                    return JObject.Parse(text);
                }
            }
        }
    }

    internal static class ScreenScraperPlatformMap
    {
        private static readonly Dictionary<string, int> SpecificationToSystemId =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "sega_megadrive", 1 },
                { "sega_mastersystem", 2 },
                { "nintendo_nes", 3 },
                { "nintendo_snes", 4 },
                { "sony_playstation", 7 },
                { "sony_playstation2", 8 },
                { "nintendo_gameboy", 9 },
                { "nintendo_gameboycolor", 10 },
                { "nintendo_gameboyadvance", 12 },
                { "nintendo_64", 14 },
                { "nintendo_gamecube", 21 },
                { "sega_saturn", 19 },
                { "sega_dreamcast", 23 },
                { "nintendo_wii", 36 },
                { "nintendo_ds", 15 },
                { "sony_psp", 13 },
                { "atari_2600", 26 },
                { "atari_7800", 27 },
                { "neogeo", 142 },
                { "nintendo_3ds", 17 },
                { "sony_playstation3", 38 },
                { "microsoft_xbox", 32 },
                { "microsoft_xbox360", 33 }
            };

        public static int? ResolveSystemId(Game game)
        {
            if (game == null || game.Platforms == null)
            {
                return null;
            }

            foreach (var platform in game.Platforms.Where(x => x != null))
            {
                var spec = (platform.SpecificationId ?? string.Empty).Trim();
                int id;
                if (spec.Length > 0 && SpecificationToSystemId.TryGetValue(spec, out id))
                {
                    return id;
                }

                var name = (platform.Name ?? string.Empty).Trim();
                if (name.IndexOf("nintendo 64", StringComparison.OrdinalIgnoreCase) >= 0) return 14;
                if (name.IndexOf("super nintendo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("super famicom", StringComparison.OrdinalIgnoreCase) >= 0) return 4;
                if (name.IndexOf("nes", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    name.IndexOf("genesis", StringComparison.OrdinalIgnoreCase) < 0) return 3;
                if (name.IndexOf("playstation 2", StringComparison.OrdinalIgnoreCase) >= 0) return 8;
                if (name.IndexOf("playstation", StringComparison.OrdinalIgnoreCase) >= 0) return 7;
                if (name.IndexOf("game boy advance", StringComparison.OrdinalIgnoreCase) >= 0) return 12;
                if (name.IndexOf("game boy", StringComparison.OrdinalIgnoreCase) >= 0) return 9;
                if (name.IndexOf("mega drive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("genesis", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            }

            return null;
        }
    }

    internal sealed class ScreenScraperMetadataContextService
    {
        private readonly MetaDataIASettings settings;

        public ScreenScraperMetadataContextService(MetaDataIASettings settings)
        {
            this.settings = settings;
        }

        public static bool IsConfigured(MetaDataIASettings settings)
        {
            return settings != null &&
                   !string.IsNullOrWhiteSpace(settings.ScreenScraperUserName) &&
                   !string.IsNullOrWhiteSpace(settings.ScreenScraperPassword) &&
                   !string.IsNullOrWhiteSpace(settings.ScreenScraperDeveloperId) &&
                   !string.IsNullOrWhiteSpace(settings.ScreenScraperDeveloperPassword);
        }

        public async Task<OfficialStoreMetadata> GetContextAsync(Game game, CancellationToken cancellationToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name) || !IsConfigured(settings))
            {
                return null;
            }

            var systemId = ScreenScraperPlatformMap.ResolveSystemId(game);
            JObject jeu = null;
            foreach (var title in TitleMatchingService.BuildAliases(game.Name))
            {
                var json = await RetroDatabaseHttp.GetJsonAsync(BuildQuery(title, systemId), cancellationToken).ConfigureAwait(false);
                var candidate = (json["response"] == null ? null : json["response"]["jeu"]) as JObject ?? json["jeu"] as JObject;
                if (candidate == null)
                {
                    continue;
                }

                var candidateTitle = ReadPrimaryTitle(candidate);
                if (TitleMatchingService.IsReliableMatch(title, candidateTitle) ||
                    TitleMatchingService.IsReliableMatch(game.Name, candidateTitle))
                {
                    jeu = candidate;
                    break;
                }
            }

            if (jeu == null)
            {
                return null;
            }

            return MapGame(jeu);
        }

        private string BuildQuery(string title, int? systemId)
        {
            var query = "https://api.screenscraper.fr/api2/jeuInfos.php?devid=" + Uri.EscapeDataString(settings.ScreenScraperDeveloperId) +
                        "&devpassword=" + Uri.EscapeDataString(settings.ScreenScraperDeveloperPassword) +
                        "&softname=MetadataAI&ssid=" + Uri.EscapeDataString(settings.ScreenScraperUserName) +
                        "&sspassword=" + Uri.EscapeDataString(settings.ScreenScraperPassword) +
                        "&romnom=" + Uri.EscapeDataString(title) +
                        "&output=json";
            if (systemId.HasValue)
            {
                query += "&systeme=" + systemId.Value;
            }

            return query;
        }

        private OfficialStoreMetadata MapGame(JObject jeu)
        {
            var language = settings == null ? "en" : settings.Language ?? "en";
            var title = ReadPrimaryTitle(jeu);
            var genres = ReadLocalizedLabels(jeu["genres"], language);
            var tags = ReadLocalizedLabels(jeu["themes"], language)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList();
            var features = ReadPlayerFeatures(jeu["joueurs"])
                .Concat(ReadLocalizedLabels(jeu["modes"], language))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            var developers = ReadTextList(jeu["developpeur"]);
            var publishers = ReadTextList(jeu["editeur"]);
            var releaseDate = ReadReleaseDate(jeu["dates"]);
            var description = ReadSynopsis(jeu["synopsis"], language);
            var gameId = (string)jeu["id"] ?? string.Empty;
            var storeUrl = string.IsNullOrWhiteSpace(gameId)
                ? string.Empty
                : "https://www.screenscraper.fr/index.php?game=" + Uri.EscapeDataString(gameId);

            var pluginIsEnglish = (language ?? "en").Trim().StartsWith("en", StringComparison.OrdinalIgnoreCase);
            return new OfficialStoreMetadata
            {
                SourceName = MetaDataIASettings.SourceScreenScraper,
                StoreUrl = storeUrl,
                Title = title,
                Description = description,
                Genres = genres,
                Tags = tags,
                Features = features,
                Developers = developers,
                Publishers = publishers,
                ReleaseDate = releaseDate,
                ListsMatchPluginLanguage = pluginIsEnglish || (genres.Count == 0 && tags.Count == 0 && features.Count == 0),
                IsExactMatch = true
            };
        }

        private static string ReadPrimaryTitle(JObject jeu)
        {
            var noms = jeu["noms"] as JArray;
            if (noms != null)
            {
                foreach (var item in noms.OfType<JObject>())
                {
                    var region = ((string)item["region"] ?? string.Empty).ToUpperInvariant();
                    if (region == "EU" || region == "US" || region == "WLD")
                    {
                        var text = ((string)item["text"] ?? string.Empty).Trim();
                        if (text.Length > 0)
                        {
                            return text;
                        }
                    }
                }

                var any = noms.OfType<JObject>()
                    .Select(x => ((string)x["text"] ?? string.Empty).Trim())
                    .FirstOrDefault(x => x.Length > 0);
                if (!string.IsNullOrWhiteSpace(any))
                {
                    return any;
                }
            }

            return ((string)jeu["nom"] ?? string.Empty).Trim();
        }

        private static List<string> ReadLocalizedLabels(JToken token, string language)
        {
            var result = new List<string>();
            var array = token as JArray ?? (token == null ? null : new JArray(token));
            if (array == null)
            {
                return result;
            }

            foreach (var item in array.OfType<JObject>())
            {
                var label = PickLocalizedText(item["noms"], language);
                if (string.IsNullOrWhiteSpace(label))
                {
                    label = ((string)item["nom"] ?? (string)item["text"] ?? string.Empty).Trim();
                }

                if (label.Length > 0)
                {
                    result.Add(label);
                }
            }

            return TermFieldResolver.DistinctTerms(result);
        }

        private static string PickLocalizedText(JToken noms, string language)
        {
            if (noms == null)
            {
                return string.Empty;
            }

            var obj = noms as JObject;
            if (obj == null)
            {
                return ((string)noms ?? string.Empty).Trim();
            }

            var code = (language ?? "en").Trim().ToLowerInvariant();
            var two = code.Length >= 2 ? code.Substring(0, 2) : code;
            foreach (var key in new[] { two, "en", "fr", "de", "es", "it", "pt" })
            {
                var value = ((string)obj[key] ?? string.Empty).Trim();
                if (value.Length > 0)
                {
                    return value;
                }
            }

            return obj.Properties()
                .Select(p => (p.Value == null ? string.Empty : p.Value.ToString()).Trim())
                .FirstOrDefault(x => x.Length > 0) ?? string.Empty;
        }

        private static List<string> ReadTextList(JToken token)
        {
            var text = token == null ? string.Empty : ((string)token["text"] ?? token.ToString()).Trim();
            if (text.Length == 0)
            {
                return new List<string>();
            }

            return new List<string> { text };
        }

        private static List<string> ReadPlayerFeatures(JToken joueurs)
        {
            var text = joueurs == null ? string.Empty : ((string)joueurs["text"] ?? joueurs.ToString()).Trim();
            if (text.Length == 0)
            {
                return new List<string>();
            }

            return new List<string> { text };
        }

        private static string ReadReleaseDate(JToken dates)
        {
            if (dates == null)
            {
                return string.Empty;
            }

            foreach (var key in new[] { "date_europe", "date_usa", "date_japon", "date_world" })
            {
                var value = ((string)dates[key] ?? string.Empty).Trim();
                if (value.Length > 0)
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string ReadSynopsis(JToken synopsis, string language)
        {
            if (synopsis == null)
            {
                return string.Empty;
            }

            var obj = synopsis as JObject;
            if (obj != null && obj["synopsis"] != null)
            {
                return PickLocalizedText(obj["synopsis"], language);
            }

            return PickLocalizedText(synopsis, language);
        }

    }

    internal sealed class MobyGamesMetadataContextService
    {
        private readonly MetaDataIASettings settings;

        public MobyGamesMetadataContextService(MetaDataIASettings settings)
        {
            this.settings = settings;
        }

        public static bool IsConfigured(MetaDataIASettings settings)
        {
            return settings != null && !string.IsNullOrWhiteSpace(settings.MobyGamesApiKey);
        }

        public async Task<OfficialStoreMetadata> GetContextAsync(Game game, CancellationToken cancellationToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name) || !IsConfigured(settings))
            {
                return null;
            }

            JObject selected = null;
            foreach (var title in TitleMatchingService.BuildAliases(game.Name))
            {
                var searchUrl = "https://api.mobygames.com/v1/games?api_key=" + Uri.EscapeDataString(settings.MobyGamesApiKey) +
                                "&title=" + Uri.EscapeDataString(title) + "&format=normal&limit=8";
                var json = await RetroDatabaseHttp.GetJsonAsync(searchUrl, cancellationToken).ConfigureAwait(false);
                selected = (json["games"] as JArray ?? new JArray())
                    .OfType<JObject>()
                    .FirstOrDefault(x => TitleMatchingService.IsReliableMatch(title, (string)x["title"]));
                if (selected != null)
                {
                    break;
                }
            }

            var gameId = selected == null ? 0 : ((int?)selected["game_id"] ?? (int?)selected["id"] ?? 0);
            if (gameId <= 0)
            {
                return null;
            }

            var detailUrl = "https://api.mobygames.com/v1/games/" + gameId + "?api_key=" +
                            Uri.EscapeDataString(settings.MobyGamesApiKey) + "&format=normal";
            var detail = await RetroDatabaseHttp.GetJsonAsync(detailUrl, cancellationToken).ConfigureAwait(false);
            return MapGame(detail, game);
        }

        private OfficialStoreMetadata MapGame(JObject detail, Game game)
        {
            if (detail == null || detail.Count == 0)
            {
                return null;
            }

            var title = ((string)detail["title"] ?? game.Name ?? string.Empty).Trim();
            var genres = (detail["genres"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(x => ((string)x["genre_name"] ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .ToList();
            genres = TermFieldResolver.DistinctTerms(genres);

            var tags = (detail["themes"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(x => ((string)x["theme_name"] ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .ToList();
            tags = TermFieldResolver.DistinctTerms(tags);

            var developers = ReadMobyCompanies(detail["developers"]);
            var publishers = ReadMobyCompanies(detail["publishers"]);
            var releaseDate = ReadMobyReleaseDate(detail["platforms"], game);
            var description = StripHtml((string)detail["overview"] ?? (string)detail["description"] ?? string.Empty);
            var storeUrl = ((string)detail["moby_url"] ?? string.Empty).Trim();

            return new OfficialStoreMetadata
            {
                SourceName = MetaDataIASettings.SourceMobyGames,
                StoreUrl = storeUrl,
                Title = title,
                Description = description,
                Genres = genres,
                Tags = tags,
                Developers = developers,
                Publishers = publishers,
                ReleaseDate = releaseDate,
                ListsMatchPluginLanguage = (settings.Language ?? "en").Trim().StartsWith("en", StringComparison.OrdinalIgnoreCase),
                IsExactMatch = true
            };
        }

        private static List<string> ReadMobyCompanies(JToken token)
        {
            return (token as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(x => ((string)x["company_name"] ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();
        }

        private static string ReadMobyReleaseDate(JToken platforms, Game game)
        {
            var platformNames = game == null || game.Platforms == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(game.Platforms.Where(p => p != null).Select(p => p.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in (platforms as JArray ?? new JArray()).OfType<JObject>())
            {
                var platform = ((string)entry["platform_name"] ?? string.Empty).Trim();
                var date = ((string)entry["first_release_date"] ?? string.Empty).Trim();
                if (date.Length == 0)
                {
                    continue;
                }

                if (platformNames.Count == 0 || platformNames.Contains(platform))
                {
                    return date;
                }
            }

            var first = (platforms as JArray ?? new JArray()).OfType<JObject>()
                .Select(x => ((string)x["first_release_date"] ?? string.Empty).Trim())
                .FirstOrDefault(x => x.Length > 0);
            return first ?? string.Empty;
        }

        private static string StripHtml(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = Regex.Replace(value, "<[^>]+>", " ");
            return Regex.Replace(text, @"\s+", " ").Trim();
        }
    }

    internal sealed class TheGamesDbMetadataContextService
    {
        private readonly MetaDataIASettings settings;

        public TheGamesDbMetadataContextService(MetaDataIASettings settings)
        {
            this.settings = settings;
        }

        public static bool IsConfigured(MetaDataIASettings settings)
        {
            return settings != null && !string.IsNullOrWhiteSpace(settings.TheGamesDbApiKey);
        }

        public async Task<OfficialStoreMetadata> GetContextAsync(Game game, CancellationToken cancellationToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name) || !IsConfigured(settings))
            {
                return null;
            }

            JObject selected = null;
            foreach (var title in TitleMatchingService.BuildAliases(game.Name))
            {
                var searchUrl = "https://api.thegamesdb.net/v1/Games/ByGameName?apikey=" + Uri.EscapeDataString(settings.TheGamesDbApiKey) +
                                "&name=" + Uri.EscapeDataString(title) + "&fields=overview,release_date,players,publishers,developers,genres";
                var search = await RetroDatabaseHttp.GetJsonAsync(searchUrl, cancellationToken).ConfigureAwait(false);
                selected = (search.SelectToken("data.games") as JArray ?? new JArray())
                    .OfType<JObject>()
                    .FirstOrDefault(x => TitleMatchingService.IsReliableMatch(title, (string)x["game_title"]));
                if (selected != null)
                {
                    break;
                }
            }

            var gameId = selected == null ? 0 : ((int?)selected["id"] ?? 0);
            if (gameId <= 0)
            {
                return null;
            }

            var detailUrl = "https://api.thegamesdb.net/v1/Games/ByGameID?apikey=" + Uri.EscapeDataString(settings.TheGamesDbApiKey) +
                            "&id=" + gameId + "&fields=overview,release_date,players,publishers,developers,genres,youtube,platform";
            var detail = await RetroDatabaseHttp.GetJsonAsync(detailUrl, cancellationToken).ConfigureAwait(false);
            var record = (detail.SelectToken("data.games") as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault();
            if (record == null)
            {
                record = selected;
            }

            return await MapGameAsync(record, cancellationToken).ConfigureAwait(false);
        }

        private async Task<OfficialStoreMetadata> MapGameAsync(JObject record, CancellationToken cancellationToken)
        {
            if (record == null)
            {
                return null;
            }

            var title = ((string)record["game_title"] ?? string.Empty).Trim();
            var genreIds = ReadIntList(record["genres"]);
            var genreNames = await ResolveGenreNamesAsync(genreIds, cancellationToken).ConfigureAwait(false);
            var features = new List<string>();
            var players = ((string)record["players"] ?? string.Empty).Trim();
            if (players.Length > 0)
            {
                features.Add(players);
            }

            var developers = SplitCsv((string)record["developers"]);
            var publishers = SplitCsv((string)record["publishers"]);
            var releaseDate = ((string)record["release_date"] ?? string.Empty).Trim();
            var description = ((string)record["overview"] ?? string.Empty).Trim();
            var storeUrl = "https://thegamesdb.net/game.php?id=" + ((int?)record["id"] ?? 0);

            return new OfficialStoreMetadata
            {
                SourceName = MetaDataIASettings.SourceTheGamesDb,
                StoreUrl = storeUrl,
                Title = title,
                Description = description,
                Genres = genreNames,
                Features = TermFieldResolver.DistinctTerms(features),
                Developers = developers,
                Publishers = publishers,
                ReleaseDate = releaseDate,
                ListsMatchPluginLanguage = (settings.Language ?? "en").Trim().StartsWith("en", StringComparison.OrdinalIgnoreCase),
                IsExactMatch = true
            };
        }

        private async Task<List<string>> ResolveGenreNamesAsync(IList<int> genreIds, CancellationToken cancellationToken)
        {
            if (genreIds == null || genreIds.Count == 0)
            {
                return new List<string>();
            }

            var map = await LoadGenreMapAsync(cancellationToken).ConfigureAwait(false);
            var names = genreIds
                .Select(id =>
                {
                    string name;
                    return map.TryGetValue(id, out name) ? name : null;
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            return TermFieldResolver.DistinctTerms(names);
        }

        private static Dictionary<int, string> genreCache;

        private async Task<Dictionary<int, string>> LoadGenreMapAsync(CancellationToken cancellationToken)
        {
            if (genreCache != null)
            {
                return genreCache;
            }

            var url = "https://api.thegamesdb.net/v1/Genres?apikey=" + Uri.EscapeDataString(settings.TheGamesDbApiKey);
            var json = await RetroDatabaseHttp.GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            var map = new Dictionary<int, string>();
            foreach (var genre in (json.SelectToken("data.genres") as JArray ?? new JArray()).OfType<JObject>())
            {
                var id = (int?)genre["id"] ?? 0;
                var name = ((string)genre["name"] ?? string.Empty).Trim();
                if (id > 0 && name.Length > 0)
                {
                    map[id] = name;
                }
            }

            genreCache = map;
            return map;
        }

        private static List<int> ReadIntList(JToken token)
        {
            if (token == null)
            {
                return new List<int>();
            }

            if (token.Type == JTokenType.String)
            {
                return SplitCsv(token.ToString())
                    .Select(x =>
                    {
                        int id;
                        return int.TryParse(x, out id) ? id : 0;
                    })
                    .Where(x => x > 0)
                    .ToList();
            }

            return (token as JArray ?? new JArray())
                .Select(x =>
                {
                    int id;
                    return int.TryParse(x == null ? string.Empty : x.ToString(), out id) ? id : 0;
                })
                .Where(x => x > 0)
                .ToList();
        }

        private static List<string> SplitCsv(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
