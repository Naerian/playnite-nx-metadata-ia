using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// A small, local-only view of a sibling game used to keep series tags stable.
    /// It intentionally contains no store or community-source data.
    /// </summary>
    public sealed class SeriesRelatedGameContext
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("genres")]
        public List<string> Genres { get; set; }

        [JsonProperty("features")]
        public List<string> Features { get; set; }

        public SeriesRelatedGameContext()
        {
            Tags = new List<string>();
            Genres = new List<string>();
            Features = new List<string>();
        }
    }

    public sealed class SeriesBaseline
    {
        [JsonProperty("primaryTags")]
        public List<string> PrimaryTags { get; set; }

        [JsonProperty("secondaryTags")]
        public List<string> SecondaryTags { get; set; }

        public SeriesBaseline()
        {
            PrimaryTags = new List<string>();
            SecondaryTags = new List<string>();
        }
    }

    public sealed class SeriesLibraryContext
    {
        [JsonProperty("seriesName")]
        public string SeriesName { get; set; }

        [JsonProperty("relatedGames")]
        public List<SeriesRelatedGameContext> RelatedGames { get; set; }

        [JsonIgnore]
        public SeriesBaseline Baseline { get; set; }

        public SeriesLibraryContext()
        {
            RelatedGames = new List<SeriesRelatedGameContext>();
        }
    }

    /// <summary>
    /// Pure series-tag consensus logic plus the thin Playnite database adapter.
    /// The pure overload makes the omission/consensus rules testable without a
    /// running Playnite instance.
    /// </summary>
    public static class SeriesTagConsistencyService
    {
        private const int MaxRelatedGames = 8;
        private const int MaxAnalysisGames = 24;
        private const int MaxTagsPerGame = 20;
        private const int MaxGenresPerGame = 10;
        private const int MaxFeaturesPerGame = 12;
        private const int MaxPrimaryTags = 10;
        private const int MaxSecondaryTags = 14;

        private static readonly HashSet<string> PerspectiveTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "First Person",
            "Third Person",
            "Top-Down",
            "Isometric",
            "Side-Scrolling"
        };

        public static SeriesLibraryContext Build(IPlayniteAPI playniteApi, Game currentGame)
        {
            if (playniteApi == null || playniteApi.Database == null || playniteApi.Database.Games == null ||
                playniteApi.Database.Series == null || currentGame == null ||
                currentGame.SeriesIds == null || currentGame.SeriesIds.Count == 0)
            {
                return null;
            }

            var seriesIds = new HashSet<Guid>(currentGame.SeriesIds);
            var seriesName = GetSeriesName(playniteApi, currentGame, seriesIds);
            if (string.IsNullOrWhiteSpace(seriesName))
            {
                return null;
            }

            var siblings = playniteApi.Database.Games.GetClone()
                .Where(x => x != null && x.Id != currentGame.Id && HasSeries(x, seriesIds))
                .Select(ToSnapshot)
                .Where(x => x != null && x.HasUsefulMetadata)
                .OrderByDescending(x => x.MetadataScore)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxAnalysisGames)
                .ToList();

            return Build(seriesName, siblings);
        }

        /// <summary>
        /// Builds context from local snapshots. Empty-metadata siblings are
        /// excluded from the consensus because they are not evidence either for
        /// or against a tag.
        /// </summary>
        public static SeriesLibraryContext Build(
            string seriesName,
            IEnumerable<SeriesTagGameSnapshot> siblingGames)
        {
            if (string.IsNullOrWhiteSpace(seriesName))
            {
                return null;
            }

            var useful = (siblingGames ?? Enumerable.Empty<SeriesTagGameSnapshot>())
                .Where(x => x != null && x.HasUsefulMetadata && !string.IsNullOrWhiteSpace(x.Name))
                .Select(NormalizeSnapshot)
                .OrderByDescending(x => x.MetadataScore)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxAnalysisGames)
                .ToList();

            if (useful.Count == 0)
            {
                return null;
            }

            var context = new SeriesLibraryContext
            {
                SeriesName = seriesName.Trim(),
                RelatedGames = useful
                    .Take(MaxRelatedGames)
                    .Select(ToContext)
                    .ToList()
            };

            context.Baseline = DeriveBaseline(useful);
            return context;
        }

        private static SeriesBaseline DeriveBaseline(IList<SeriesTagGameSnapshot> games)
        {
            var cohort = SelectMechanicallySimilarCohort(games);
            if (cohort.Count < 2)
            {
                return null;
            }

            var primary = Consensus(
                cohort,
                x => x.Tags.Where(IsPrimaryTag).Select(NormalizePrimaryTag),
                PrimarySupportThreshold(cohort.Count),
                MaxPrimaryTags,
                true);

            var secondary = Consensus(
                cohort,
                game => game.Tags.Where(tag => !IsPrimaryTag(tag)),
                SecondarySupportThreshold(cohort.Count),
                MaxSecondaryTags,
                false);

            if (primary.Count == 0 && secondary.Count == 0)
            {
                return null;
            }

            return new SeriesBaseline
            {
                PrimaryTags = primary,
                SecondaryTags = secondary
            };
        }

        private static List<SeriesTagGameSnapshot> SelectMechanicallySimilarCohort(IList<SeriesTagGameSnapshot> games)
        {
            if (games == null || games.Count < 2)
            {
                return new List<SeriesTagGameSnapshot>();
            }

            List<SeriesTagGameSnapshot> best = null;
            var bestScore = -1;
            foreach (var seed in games)
            {
                var candidate = games
                    .Where(candidateGame => ReferenceEquals(candidateGame, seed) || IsMechanicallySimilar(seed, candidateGame))
                    .ToList();
                var score = candidate.Sum(candidateGame => ReferenceEquals(candidateGame, seed) ? 0 : SimilarityScore(seed, candidateGame));

                if (best == null || candidate.Count > best.Count ||
                    (candidate.Count == best.Count && score > bestScore))
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best ?? new List<SeriesTagGameSnapshot>();
        }

        private static bool IsMechanicallySimilar(SeriesTagGameSnapshot left, SeriesTagGameSnapshot right)
        {
            var sharedPrimary = SharedCount(left.Tags.Where(IsPrimaryTag).Select(NormalizePrimaryTag), right.Tags.Where(IsPrimaryTag).Select(NormalizePrimaryTag));
            var sharedPerspective = SharedCount(left.Tags.Where(IsPerspectiveTag).Select(NormalizeTag), right.Tags.Where(IsPerspectiveTag).Select(NormalizeTag));
            var sharedSecondary = SharedCount(left.Tags.Where(x => !IsPrimaryTag(x) && !IsPerspectiveTag(x)).Select(NormalizeTag), right.Tags.Where(x => !IsPrimaryTag(x) && !IsPerspectiveTag(x)).Select(NormalizeTag));
            var sharedFeatures = SharedCount(left.Features.Select(NormalizeTag), right.Features.Select(NormalizeTag));
            var sharedGenres = SharedCount(left.Genres.Select(NormalizeTag), right.Genres.Select(NormalizeTag));

            if (sharedPrimary > 0 && (sharedSecondary > 0 || sharedPerspective > 0 || sharedFeatures > 0))
            {
                return true;
            }

            if (sharedSecondary >= 2 || (sharedPerspective > 0 && sharedFeatures > 0))
            {
                return true;
            }

            return sharedGenres > 0 && sharedFeatures >= 2;
        }

        private static int SimilarityScore(SeriesTagGameSnapshot left, SeriesTagGameSnapshot right)
        {
            var sharedPrimary = SharedCount(left.Tags.Where(IsPrimaryTag).Select(NormalizePrimaryTag), right.Tags.Where(IsPrimaryTag).Select(NormalizePrimaryTag));
            var sharedPerspective = SharedCount(left.Tags.Where(IsPerspectiveTag).Select(NormalizeTag), right.Tags.Where(IsPerspectiveTag).Select(NormalizeTag));
            var sharedSecondary = SharedCount(left.Tags.Where(x => !IsPrimaryTag(x) && !IsPerspectiveTag(x)).Select(NormalizeTag), right.Tags.Where(x => !IsPrimaryTag(x) && !IsPerspectiveTag(x)).Select(NormalizeTag));
            var sharedFeatures = SharedCount(left.Features.Select(NormalizeTag), right.Features.Select(NormalizeTag));
            var sharedGenres = SharedCount(left.Genres.Select(NormalizeTag), right.Genres.Select(NormalizeTag));
            return Math.Min(sharedPrimary, 3) * 4 + Math.Min(sharedPerspective, 1) * 3 +
                   Math.Min(sharedSecondary, 4) * 2 + Math.Min(sharedFeatures, 3) + Math.Min(sharedGenres, 2);
        }

        private static List<string> Consensus(
            IEnumerable<SeriesTagGameSnapshot> games,
            Func<SeriesTagGameSnapshot, IEnumerable<string>> selector,
            int minimumSupport,
            int maxItems,
            bool primary)
        {
            var candidates = new Dictionary<string, TagCount>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var game in games)
            {
                var seenInGame = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var value in selector(game).Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    var key = NormalizeTag(value);
                    if (key.Length == 0 || !seenInGame.Add(key))
                    {
                        continue;
                    }

                    TagCount count;
                    if (!candidates.TryGetValue(key, out count))
                    {
                        count = new TagCount
                        {
                            Value = primary ? NormalizePrimaryTag(value) : value.Trim(),
                            FirstIndex = index++
                        };
                        candidates[key] = count;
                    }

                    count.Count++;
                }
            }

            return candidates.Values
                .Where(x => x.Count >= minimumSupport)
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.FirstIndex)
                .Take(maxItems)
                .Select(x => primary ? NormalizePrimaryTag(x.Value) : x.Value)
                .ToList();
        }

        private static int PrimarySupportThreshold(int cohortSize)
        {
            // With two mechanically similar entries, one missing primary tag is
            // treated as an omission rather than a contradiction. Larger cohorts
            // need a real majority and at least two supporting entries.
            return cohortSize <= 2 ? 1 : Math.Max(2, (int)Math.Ceiling(cohortSize * 0.6));
        }

        private static int SecondarySupportThreshold(int cohortSize)
        {
            if (cohortSize <= 2)
            {
                return 2;
            }

            return Math.Max(2, (int)Math.Ceiling(cohortSize * 0.66));
        }

        private static bool HasSeries(Game game, HashSet<Guid> seriesIds)
        {
            return game != null && game.SeriesIds != null && game.SeriesIds.Any(seriesIds.Contains);
        }

        private static string GetSeriesName(IPlayniteAPI api, Game game, HashSet<Guid> seriesIds)
        {
            var assigned = game.Series == null
                ? null
                : game.Series.FirstOrDefault(x => x != null && seriesIds.Contains(x.Id) && !string.IsNullOrWhiteSpace(x.Name));
            if (assigned != null)
            {
                return assigned.Name.Trim();
            }

            foreach (var id in seriesIds)
            {
                var series = api.Database.Series.Get(id);
                if (series != null && !string.IsNullOrWhiteSpace(series.Name))
                {
                    return series.Name.Trim();
                }
            }

            return string.Empty;
        }

        private static SeriesTagGameSnapshot ToSnapshot(Game game)
        {
            return new SeriesTagGameSnapshot
            {
                Id = game.Id,
                Name = game.Name,
                Tags = Names(game.Tags, MaxTagsPerGame),
                Genres = Names(game.Genres, MaxGenresPerGame),
                Features = Names(game.Features, MaxFeaturesPerGame)
            };
        }

        private static SeriesRelatedGameContext ToContext(SeriesTagGameSnapshot game)
        {
            return new SeriesRelatedGameContext
            {
                Name = game.Name,
                Tags = game.Tags,
                Genres = game.Genres,
                Features = game.Features
            };
        }

        private static SeriesTagGameSnapshot NormalizeSnapshot(SeriesTagGameSnapshot game)
        {
            return new SeriesTagGameSnapshot
            {
                Id = game.Id,
                Name = game.Name.Trim(),
                Tags = CleanStrings(game.Tags, MaxTagsPerGame),
                Genres = CleanStrings(game.Genres, MaxGenresPerGame),
                Features = CleanStrings(game.Features, MaxFeaturesPerGame)
            };
        }

        private static List<string> Names<T>(IEnumerable<T> values, int maxItems) where T : DatabaseObject
        {
            return values == null
                ? new List<string>()
                : CleanStrings(values.Where(x => x != null).Select(x => x.Name), maxItems);
        }

        private static List<string> CleanStrings(IEnumerable<string> values, int maxItems)
        {
            return (values ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => Regex.Replace(x.Trim(), @"\s+", " "))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxItems)
                .ToList();
        }

        private static bool IsPrimaryTag(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.TrimStart().StartsWith("-", StringComparison.Ordinal);
        }

        private static bool IsPerspectiveTag(string value)
        {
            var normalized = NormalizeTag(value).TrimStart('-').Trim();
            return PerspectiveTags.Contains(normalized);
        }

        private static string NormalizePrimaryTag(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            normalized = Regex.Replace(normalized, @"^[-\s]+", string.Empty);
            return normalized.Length == 0 ? string.Empty : "- " + normalized;
        }

        private static string NormalizeTag(string value)
        {
            return Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ").ToLowerInvariant();
        }

        private static int SharedCount(IEnumerable<string> left, IEnumerable<string> right)
        {
            var rightSet = new HashSet<string>(right.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            return left.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(rightSet.Contains);
        }

        private sealed class TagCount
        {
            public string Value { get; set; }
            public int Count { get; set; }
            public int FirstIndex { get; set; }
        }

        public sealed class SeriesTagGameSnapshot
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public List<string> Tags { get; set; }
            public List<string> Genres { get; set; }
            public List<string> Features { get; set; }

            [JsonIgnore]
            public bool HasUsefulMetadata
            {
                get
                {
                    return Tags != null && Tags.Any(x => !string.IsNullOrWhiteSpace(x)) ||
                           Genres != null && Genres.Any(x => !string.IsNullOrWhiteSpace(x)) ||
                           Features != null && Features.Any(x => !string.IsNullOrWhiteSpace(x));
                }
            }

            [JsonIgnore]
            public int MetadataScore
            {
                get
                {
                    return (Tags == null ? 0 : Tags.Count(x => !string.IsNullOrWhiteSpace(x)) * 4) +
                           (Genres == null ? 0 : Genres.Count(x => !string.IsNullOrWhiteSpace(x)) * 2) +
                           (Features == null ? 0 : Features.Count(x => !string.IsNullOrWhiteSpace(x)));
                }
            }

            public SeriesTagGameSnapshot()
            {
                Tags = new List<string>();
                Genres = new List<string>();
                Features = new List<string>();
            }
        }
    }
}
