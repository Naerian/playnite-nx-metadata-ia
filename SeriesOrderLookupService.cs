using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        public string Detail { get; set; }
        public string FailureReason { get; set; }
        public int CatalogGameId { get; set; }

        public bool IsResolved
        {
            get { return HasSeries && HasOrder; }
        }

        public bool HasSeries { get { return !string.IsNullOrWhiteSpace(SeriesName); } }
        public bool HasOrder { get { return Order > 0; } }
    }

    internal sealed class SeriesOrderLookupService
    {
        private static readonly HttpClient Client = new HttpClient();
        private static readonly ILogger Logger = LogManager.GetLogger();
        private static readonly SemaphoreSlim RequestGate = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim TokenGate = new SemaphoreSlim(1, 1);
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, SeriesOrderLookupResult> ResultCache = new Dictionary<string, SeriesOrderLookupResult>(StringComparer.OrdinalIgnoreCase);
        private static DateTime lastRequestUtc = DateTime.MinValue;
        private static string sharedClientId;
        private static string sharedAccessToken;
        private static DateTime sharedAccessTokenExpiresUtc = DateTime.MinValue;

        private readonly MetaDataIASettings settings;
        private string generatedAccessToken;

        public SeriesOrderLookupService(MetaDataIASettings settings)
        {
            this.settings = settings;
        }

        public async Task<SeriesOrderLookupResult> ResolveAsync(Game game, CancellationToken cancellationToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return Failure(Loc("MTDA_SeriesLookupNoTitle", "The game has no title to identify it."));
            }

            if (settings == null || string.IsNullOrWhiteSpace(settings.IgdbClientId) ||
                (string.IsNullOrWhiteSpace(settings.IgdbAccessToken) && string.IsNullOrWhiteSpace(settings.IgdbClientSecret)))
            {
                return Failure(Loc("MTDA_SeriesLookupNotConfigured", "IGDB is not configured. Add its Client ID and access token or client secret in Media > Sources."));
            }

            var cacheKey = BuildCacheKey(game);
            lock (CacheLock)
            {
                SeriesOrderLookupResult cached;
                if (ResultCache.TryGetValue(cacheKey, out cached))
                {
                    return Clone(cached);
                }
            }

            SeriesOrderLookupResult result;
            try
            {
                var selected = await FindGameAsync(game, cancellationToken).ConfigureAwait(false);
                if (selected == null)
                {
                    result = Failure(Loc("MTDA_SeriesLookupNoMatch", "IGDB could not identify the game reliably from its title, year and alternative names."));
                }
                else
                {
                    selected = await ResolveVersionParentAsync(selected, cancellationToken).ConfigureAwait(false);
                    result = await ResolveSeriesAsync(game, selected, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "IGDB series/order lookup failed for " + game.Name + ".");
                return Failure(Loc("MTDA_SeriesLookupError", "IGDB returned an error while checking the series and its release order."));
            }

            lock (CacheLock)
            {
                ResultCache[cacheKey] = Clone(result);
            }

            return result;
        }

        private async Task<JObject> FindGameAsync(Game game, CancellationToken cancellationToken)
        {
            JObject best = null;
            var bestScore = int.MinValue;
            foreach (var alias in TitleMatchingService.BuildAliases(game.Name))
            {
                var body = "search \"" + Escape(alias) + "\"; " +
                           "fields id,name,alternative_names.name,collections,collection,franchise,franchises,first_release_date,game_type.type,category,version_parent; limit 20;";
                var matches = await PostAsync("games", body, cancellationToken).ConfigureAwait(false);
                foreach (var candidate in matches.OfType<JObject>())
                {
                    var score = ScoreMatch(game, alias, candidate);
                    if (score > bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }

                if (bestScore >= 130)
                {
                    break;
                }
            }

            return bestScore >= 100 ? best : null;
        }

        private static int ScoreMatch(Game game, string alias, JObject candidate)
        {
            var names = new List<string> { (string)candidate["name"] };
            var alternatives = candidate["alternative_names"] as JArray;
            if (alternatives != null)
            {
                names.AddRange(alternatives.OfType<JObject>().Select(x => (string)x["name"]));
            }

            var exact = names.Any(x => TitleMatchingService.IsReliableMatch(alias, x));
            if (!exact)
            {
                return int.MinValue;
            }

            var score = 100;
            if (TitleMatchingService.IsReliableMatch(game.Name, (string)candidate["name"]))
            {
                score += 20;
            }

            var candidateYear = ReadReleaseYear((long?)candidate["first_release_date"]);
            if (game.ReleaseYear.HasValue && game.ReleaseYear.Value > 0 && candidateYear > 0)
            {
                var difference = Math.Abs(game.ReleaseYear.Value - candidateYear);
                score += difference == 0 ? 15 : difference == 1 ? 8 : difference > 3 ? -20 : 0;
            }

            if ((int?)candidate["version_parent"] > 0)
            {
                score -= 4;
            }

            return score;
        }

        private async Task<JObject> ResolveVersionParentAsync(JObject selected, CancellationToken cancellationToken)
        {
            var parentId = ReadId(selected["version_parent"]);
            if (parentId <= 0)
            {
                return selected;
            }

            var parents = await PostAsync(
                "games",
                "where id = " + parentId + "; fields id,name,alternative_names.name,collections,collection,franchise,franchises,first_release_date,game_type.type,category; limit 1;",
                cancellationToken).ConfigureAwait(false);
            return parents.OfType<JObject>().FirstOrDefault() ?? selected;
        }

        private async Task<SeriesOrderLookupResult> ResolveSeriesAsync(Game game, JObject selected, CancellationToken cancellationToken)
        {
            var selectedId = (int?)selected["id"] ?? 0;
            if (selectedId <= 0)
            {
                return Failure(Loc("MTDA_SeriesLookupNoId", "IGDB found the title but did not return a usable game identifier."));
            }

            var collectionIds = ReadIds(selected["collections"]);
            var legacyCollection = ReadId(selected["collection"]);
            if (legacyCollection > 0 && !collectionIds.Contains(legacyCollection))
            {
                collectionIds.Add(legacyCollection);
            }

            var groups = new List<SeriesGroup>();
            if (collectionIds.Count > 0)
            {
                var collections = await PostAsync(
                    "collections",
                    "where id = (" + string.Join(",", collectionIds) + "); fields id,name,games,type; limit 50;",
                    cancellationToken).ConfigureAwait(false);
                groups.AddRange(collections.OfType<JObject>().Select(x => SeriesGroup.From(x, "IGDB collection")));
            }

            if (groups.Count == 0)
            {
                var franchiseIds = ReadIds(selected["franchises"]);
                var mainFranchise = ReadId(selected["franchise"]);
                if (mainFranchise > 0 && !franchiseIds.Contains(mainFranchise))
                {
                    franchiseIds.Insert(0, mainFranchise);
                }

                if (franchiseIds.Count > 0)
                {
                    var franchises = await PostAsync(
                        "franchises",
                        "where id = (" + string.Join(",", franchiseIds) + "); fields id,name,games; limit 50;",
                        cancellationToken).ConfigureAwait(false);
                    groups.AddRange(franchises.OfType<JObject>().Select(x => SeriesGroup.From(x, "IGDB franchise")));
                }
            }

            if (groups.Count == 0)
            {
                return Failure(Loc("MTDA_SeriesLookupNoGroup", "IGDB identified the game, but it is not assigned to a collection or franchise."));
            }

            var candidateGroups = groups
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .OrderByDescending(x => ScoreGroup(game, x))
                .ThenBy(x => x.GameIds.Count)
                .ToList();
            var orderedGroups = candidateGroups.Where(x => x.GameIds.Count >= 2).ToList();

            foreach (var group in orderedGroups)
            {
                var entries = await PostAsync(
                    "games",
                    "where id = (" + string.Join(",", group.GameIds.Take(500)) + "); fields id,name,first_release_date,game_type.type,category,version_parent; limit 500;",
                    cancellationToken).ConfigureAwait(false);
                var mainGames = entries.OfType<JObject>()
                    .Where(IsMainCatalogGame)
                    .OrderBy(x => (long?)x["first_release_date"] ?? long.MaxValue)
                    .ThenBy(x => (string)x["name"], StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var index = mainGames.FindIndex(x => ((int?)x["id"] ?? 0) == selectedId);
                if (index < 0)
                {
                    var aliases = TitleMatchingService.BuildAliases(game.Name);
                    var exactIndexes = mainGames
                        .Select((entry, entryIndex) => new { Entry = entry, Index = entryIndex })
                        .Where(x => aliases.Any(alias => TitleMatchingService.IsReliableMatch(alias, (string)x.Entry["name"])))
                        .Select(x => x.Index)
                        .ToList();
                    if (exactIndexes.Count == 1)
                    {
                        index = exactIndexes[0];
                    }
                    else if (exactIndexes.Count == 0)
                    {
                        var ordinalIndexes = mainGames
                            .Select((entry, entryIndex) => new { Entry = entry, Index = entryIndex })
                            .Where(x => aliases.Any(alias => TitleMatchingService.IsOrdinalVariant(alias, (string)x.Entry["name"])))
                            .Select(x => x.Index)
                            .ToList();
                        if (ordinalIndexes.Count == 1)
                        {
                            index = ordinalIndexes[0];
                        }
                    }
                }

                if (index >= 0 && !string.IsNullOrWhiteSpace(group.Name))
                {
                    return new SeriesOrderLookupResult
                    {
                        SeriesName = group.Name.Trim(),
                        Order = index + 1,
                        Source = group.Source,
                        Detail = "Matched IGDB game " + selectedId + " and ordered the base games by their first release date.",
                        CatalogGameId = selectedId
                    };
                }
            }

            var identifiedSeries = candidateGroups.FirstOrDefault();
            if (identifiedSeries != null)
            {
                return new SeriesOrderLookupResult
                {
                    SeriesName = identifiedSeries.Name.Trim(),
                    Source = identifiedSeries.Source,
                    Detail = "Matched the game's IGDB collection, but no safe base-game ordinal was available.",
                    FailureReason = Loc("MTDA_SeriesLookupNoOrder", "IGDB found the series, but the game could not be placed safely among its base releases."),
                    CatalogGameId = selectedId
                };
            }

            return Failure(Loc("MTDA_SeriesLookupNoOrder", "IGDB found the series, but the game could not be placed safely among its base releases."));
        }

        private static bool IsMainCatalogGame(JObject value)
        {
            if ((int?)value["version_parent"] > 0)
            {
                return false;
            }

            var gameTypeObject = value["game_type"] as JObject;
            var gameType = gameTypeObject == null ? string.Empty : (string)gameTypeObject["type"] ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(gameType))
            {
                var normalized = gameType.Replace("_", " ").Replace("-", " ").Trim();
                return string.Equals(normalized, "main game", StringComparison.OrdinalIgnoreCase);
            }

            return ((int?)value["category"] ?? 0) == 0;
        }

        private static int ScoreGroup(Game game, SeriesGroup group)
        {
            var gameTitle = TitleMatchingService.NormalizeTitle(game.Name);
            var groupName = TitleMatchingService.NormalizeTitle(group.Name);
            var score = group.Source == "IGDB collection" ? 20 : 0;
            if (!string.IsNullOrWhiteSpace(groupName) &&
                (string.Equals(gameTitle, groupName, StringComparison.OrdinalIgnoreCase) || gameTitle.StartsWith(groupName + " ", StringComparison.OrdinalIgnoreCase)))
            {
                score += 100;
            }

            var assigned = game.Series == null ? null : game.Series.FirstOrDefault(x => x != null && !string.IsNullOrWhiteSpace(x.Name));
            if (assigned != null && string.Equals(TitleMatchingService.NormalizeTitle(assigned.Name), groupName, StringComparison.OrdinalIgnoreCase))
            {
                score += 200;
            }

            return score;
        }

        private async Task<JArray> PostAsync(string endpoint, string body, CancellationToken cancellationToken)
        {
            var accessToken = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("IGDB access token is unavailable.");
            }

            await RequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var remaining = TimeSpan.FromMilliseconds(275) - (DateTime.UtcNow - lastRequestUtc);
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }

                using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/" + endpoint))
                {
                    request.Headers.Add("Client-ID", settings.IgdbClientId);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
                    using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        lastRequestUtc = DateTime.UtcNow;
                        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new HttpRequestException("IGDB " + endpoint + " returned HTTP " + (int)response.StatusCode + ". " + responseText);
                        }

                        return JArray.Parse(responseText);
                    }
                }
            }
            finally
            {
                RequestGate.Release();
            }
        }

        private async Task<string> EnsureAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(generatedAccessToken))
            {
                return generatedAccessToken;
            }

            if (string.IsNullOrWhiteSpace(settings.IgdbClientSecret))
            {
                generatedAccessToken = settings.IgdbAccessToken;
                return generatedAccessToken;
            }

            await TokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (string.Equals(sharedClientId, settings.IgdbClientId, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(sharedAccessToken) &&
                    sharedAccessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(1))
                {
                    generatedAccessToken = sharedAccessToken;
                    settings.IgdbAccessToken = generatedAccessToken;
                    return generatedAccessToken;
                }

                var url = "https://id.twitch.tv/oauth2/token?client_id=" + Uri.EscapeDataString(settings.IgdbClientId) +
                    "&client_secret=" + Uri.EscapeDataString(settings.IgdbClientSecret) + "&grant_type=client_credentials";
                using (var response = await Client.PostAsync(url, null, cancellationToken).ConfigureAwait(false))
                {
                    var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException("Twitch authentication returned HTTP " + (int)response.StatusCode + ".");
                    }

                    var json = JObject.Parse(responseText);
                    generatedAccessToken = (string)json["access_token"];
                    if (!string.IsNullOrWhiteSpace(generatedAccessToken))
                    {
                        var expiresIn = (int?)json["expires_in"] ?? 3600;
                        sharedClientId = settings.IgdbClientId;
                        sharedAccessToken = generatedAccessToken;
                        sharedAccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn));
                        settings.IgdbAccessToken = generatedAccessToken;
                    }
                }
            }
            finally
            {
                TokenGate.Release();
            }

            return generatedAccessToken;
        }

        private static int ReadId(JToken token)
        {
            if (token == null)
            {
                return 0;
            }

            return token.Type == JTokenType.Object ? ((int?)token["id"] ?? 0) : ((int?)token ?? 0);
        }

        private static List<int> ReadIds(JToken token)
        {
            var array = token as JArray;
            if (array == null)
            {
                var single = ReadId(token);
                return single > 0 ? new List<int> { single } : new List<int>();
            }

            return array.Select(ReadId).Where(x => x > 0).Distinct().ToList();
        }

        private static int ReadReleaseYear(long? timestamp)
        {
            if (!timestamp.HasValue || timestamp.Value <= 0)
            {
                return 0;
            }

            return DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).Year;
        }

        private static string BuildCacheKey(Game game)
        {
            return TitleMatchingService.NormalizeTitle(game.Name) + "|" +
                   (game.ReleaseYear.HasValue ? game.ReleaseYear.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
        }

        private static SeriesOrderLookupResult Failure(string reason)
        {
            return new SeriesOrderLookupResult { Source = "IGDB", FailureReason = reason ?? string.Empty };
        }

        private static SeriesOrderLookupResult Clone(SeriesOrderLookupResult value)
        {
            if (value == null)
            {
                return null;
            }

            return new SeriesOrderLookupResult
            {
                SeriesName = value.SeriesName,
                Order = value.Order,
                Source = value.Source,
                Detail = value.Detail,
                FailureReason = value.FailureReason,
                CatalogGameId = value.CatalogGameId
            };
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string Loc(string key, string fallback)
        {
            return PluginLocalization.GetString(key, fallback);
        }

        private sealed class SeriesGroup
        {
            public string Name { get; private set; }
            public string Source { get; private set; }
            public List<int> GameIds { get; private set; }

            public static SeriesGroup From(JObject value, string source)
            {
                return new SeriesGroup
                {
                    Name = (string)value["name"],
                    Source = source,
                    GameIds = ReadIds(value["games"])
                };
            }
        }
    }
}
