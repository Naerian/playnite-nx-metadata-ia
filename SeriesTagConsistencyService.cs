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

        [JsonProperty("explicitSeries")]
        public List<string> ExplicitSeries { get; set; }

        public SeriesRelatedGameContext()
        {
            Tags = new List<string>();
            Genres = new List<string>();
            Features = new List<string>();
            ExplicitSeries = new List<string>();
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

    public sealed class SeriesTagConsistencyOptions
    {
        public bool UsePrimaryTagClassification { get; set; }
        public string PrimaryTagPrefix { get; set; }
        public bool InferSeriesRelationships { get; set; }

        public SeriesTagConsistencyOptions()
        {
            PrimaryTagPrefix = "- ";
        }
    }

    public sealed class SeriesInferenceCandidate
    {
        [JsonProperty("candidateId")]
        public string CandidateId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("explicitSeries")]
        public List<string> ExplicitSeries { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("genres")]
        public List<string> Genres { get; set; }

        [JsonProperty("features")]
        public List<string> Features { get; set; }

        [JsonProperty("evidence")]
        public string Evidence { get; set; }

        [JsonIgnore]
        public SeriesTagConsistencyService.SeriesTagGameSnapshot Snapshot { get; set; }

        [JsonIgnore]
        public int LocalScore { get; set; }

        [JsonIgnore]
        public bool SameNormalizedTitle { get; set; }

        public SeriesInferenceCandidate()
        {
            ExplicitSeries = new List<string>();
            Tags = new List<string>();
            Genres = new List<string>();
            Features = new List<string>();
        }
    }

    public sealed class SeriesInferenceResult
    {
        public string SeriesName { get; set; }
        public string Confidence { get; set; }
        public List<SeriesInferenceCandidate> Candidates { get; set; }
        public List<SeriesTagConsistencyService.SeriesTagGameSnapshot> AcceptedSiblings { get; set; }
        public SeriesLibraryContext Context { get; set; }

        public SeriesInferenceResult()
        {
            Candidates = new List<SeriesInferenceCandidate>();
            AcceptedSiblings = new List<SeriesTagConsistencyService.SeriesTagGameSnapshot>();
        }
    }

    public sealed class SeriesContextDiagnostics
    {
        public bool Inferred { get; set; }
        public string SeriesName { get; set; }
        public string Confidence { get; set; }
        public List<string> RelatedGames { get; set; }

        public SeriesContextDiagnostics()
        {
            RelatedGames = new List<string>();
        }

        public string ToDisplayText()
        {
            if (string.IsNullOrWhiteSpace(SeriesName))
            {
                return string.Empty;
            }

            var lines = new List<string> { "Series context:" };
            if (Inferred)
            {
                lines.Add("Inferred: " + SeriesName.Trim());
                lines.Add("Confidence: " + (string.IsNullOrWhiteSpace(Confidence) ? "Unknown" : Confidence.Trim()));
                lines.Add("Related games:");
                lines.AddRange((RelatedGames ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => "- " + x.Trim()));
            }
            else
            {
                lines.Add("Playnite Series: " + SeriesName.Trim());
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public sealed class SeriesLibraryContext
    {
        [JsonProperty("seriesName")]
        public string SeriesName { get; set; }

        [JsonProperty("inferred")]
        public bool Inferred { get; set; }

        [JsonProperty("confidence")]
        public string Confidence { get; set; }

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
        private const int MaxInferenceCandidates = 6;
        private const string SeriesEditionSuffixPattern = "(?:game of the year(?: edition)?|goty(?: edition)?|director'?s cut|definitive edition|complete edition|remastered(?: edition)?|remaster|remake|anniversary(?: edition)?|enhanced edition|ultimate edition|deluxe edition|hd)";

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
            // Keep the original overload's behavior for callers that already
            // relied on the branch's prefixed primary-tag convention.
            return Build(playniteApi, currentGame, new SeriesTagConsistencyOptions
            {
                UsePrimaryTagClassification = true,
                PrimaryTagPrefix = "- "
            });
        }

        public static SeriesLibraryContext Build(
            IPlayniteAPI playniteApi,
            Game currentGame,
            SeriesTagConsistencyOptions options)
        {
            var effectiveOptions = options ?? new SeriesTagConsistencyOptions();
            if (playniteApi == null || playniteApi.Database == null || playniteApi.Database.Games == null ||
                currentGame == null)
            {
                return null;
            }

            if (currentGame.SeriesIds == null || currentGame.SeriesIds.Count == 0)
            {
                if (!effectiveOptions.InferSeriesRelationships)
                {
                    return null;
                }

                var inferred = FindInferredSeries(playniteApi, currentGame, effectiveOptions);
                return inferred == null ? null : inferred.Context;
            }

            if (playniteApi.Database.Series == null)
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

            return Build(seriesName, siblings, effectiveOptions);
        }

        /// <summary>
        /// Finds a small, local-only shortlist for a game without explicit
        /// Playnite SeriesIds. Candidate selection is deliberately conservative:
        /// normalized sequel/edition titles are accepted as siblings, while a
        /// candidate connected only by a broad franchise assignment is exposed
        /// for AI validation but is not used in the baseline automatically.
        /// </summary>
        public static SeriesInferenceResult FindInferredSeries(
            IPlayniteAPI playniteApi,
            Game currentGame,
            SeriesTagConsistencyOptions options)
        {
            var effectiveOptions = options ?? new SeriesTagConsistencyOptions();
            if (!effectiveOptions.InferSeriesRelationships || playniteApi == null ||
                playniteApi.Database == null || playniteApi.Database.Games == null ||
                currentGame == null || string.IsNullOrWhiteSpace(currentGame.Name) ||
                (currentGame.SeriesIds != null && currentGame.SeriesIds.Count > 0))
            {
                return null;
            }

            var currentSnapshot = ToSnapshot(playniteApi, currentGame);
            var librarySnapshots = playniteApi.Database.Games.GetClone()
                .Where(x => x != null && x.Id != currentGame.Id && !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => ToSnapshot(playniteApi, x))
                .ToList();
            return FindInferredSeries(currentSnapshot.Name, currentSnapshot, librarySnapshots, effectiveOptions);
        }

        /// <summary>
        /// Dependency-light inference core. The Playnite adapter above only
        /// supplies local snapshots; this overload is also used by regression
        /// tests so title and candidate rules can be checked without a live
        /// Playnite database or any network source.
        /// </summary>
        public static SeriesInferenceResult FindInferredSeries(
            string currentGameName,
            SeriesTagGameSnapshot currentGame,
            IEnumerable<SeriesTagGameSnapshot> libraryGames,
            SeriesTagConsistencyOptions options)
        {
            var effectiveOptions = options ?? new SeriesTagConsistencyOptions();
            if (!effectiveOptions.InferSeriesRelationships || currentGame == null ||
                string.IsNullOrWhiteSpace(currentGameName) || currentGame.HasExplicitSeriesIds)
            {
                return null;
            }

            var currentTitleKey = NormalizeSeriesTitleForInference(currentGameName);
            if (string.IsNullOrWhiteSpace(currentTitleKey))
            {
                return null;
            }

            var currentTags = currentGame.Tags ?? new List<string>();
            var currentGenres = currentGame.Genres ?? new List<string>();
            var currentFeatures = currentGame.Features ?? new List<string>();
            var candidates = new List<SeriesInferenceCandidate>();
            foreach (var snapshotValue in libraryGames ?? Enumerable.Empty<SeriesTagGameSnapshot>())
            {
                if (snapshotValue == null || snapshotValue.Id == currentGame.Id || string.IsNullOrWhiteSpace(snapshotValue.Name))
                {
                    continue;
                }

                var snapshot = NormalizeSnapshot(snapshotValue);
                var candidateTitleKey = NormalizeSeriesTitleForInference(snapshot.Name);
                var sameTitle = string.Equals(currentTitleKey, candidateTitleKey, StringComparison.OrdinalIgnoreCase);
                var explicitSeries = snapshot.SeriesNames ?? new List<string>();
                var compatibleSeries = explicitSeries.Any(x => IsSeriesNameCompatible(x, currentTitleKey));
                if (!sameTitle && !compatibleSeries)
                {
                    continue;
                }

                var localScore = sameTitle ? 100 : 42;
                if (explicitSeries.Count > 0)
                {
                    localScore += sameTitle ? 25 : 30;
                }

                localScore += Math.Min(20, SharedCount(snapshot.Tags.Select(NormalizeTag), currentTags.Select(NormalizeTag)) * 3);
                localScore += Math.Min(12, SharedCount(snapshot.Genres.Select(NormalizeTag), currentGenres.Select(NormalizeTag)) * 4);
                localScore += Math.Min(9, SharedCount(snapshot.Features.Select(NormalizeTag), currentFeatures.Select(NormalizeTag)) * 3);

                candidates.Add(new SeriesInferenceCandidate
                {
                    CandidateId = snapshot.Id.ToString("N"),
                    Name = snapshot.Name.Trim(),
                    ExplicitSeries = explicitSeries,
                    Tags = snapshot.Tags,
                    Genres = snapshot.Genres,
                    Features = snapshot.Features,
                    Evidence = BuildInferenceEvidence(sameTitle, explicitSeries),
                    Snapshot = snapshot,
                    LocalScore = localScore,
                    SameNormalizedTitle = sameTitle
                });
            }

            var shortlist = candidates
                .OrderByDescending(x => x.LocalScore)
                .ThenByDescending(x => x.Snapshot.MetadataScore)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxInferenceCandidates)
                .ToList();
            var accepted = shortlist
                .Where(x => x.SameNormalizedTitle && x.Snapshot.HasUsefulMetadata)
                .Select(x => x.Snapshot)
                .ToList();

            if (shortlist.Count == 0)
            {
                return null;
            }

            var seriesName = shortlist
                .Where(x => x.SameNormalizedTitle || x.ExplicitSeries.Count > 0)
                .SelectMany(x => x.ExplicitSeries ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(seriesName))
            {
                seriesName = DisplaySeriesName(currentGame.Name);
            }

            var context = accepted.Count == 0 ? null : Build(seriesName, accepted, effectiveOptions);
            if (accepted.Count > 0 && context == null)
            {
                return null;
            }

            if (context != null)
            {
                context.Inferred = true;
                context.Confidence = "High";
            }

            return new SeriesInferenceResult
            {
                SeriesName = context == null ? seriesName : context.SeriesName,
                Confidence = context == null ? "Medium" : context.Confidence,
                Candidates = shortlist,
                AcceptedSiblings = accepted,
                Context = context
            };
        }

        public static string NormalizeSeriesTitleForInference(string title)
        {
            var value = Regex.Replace((title ?? string.Empty).Trim(), @"\s+", " ");
            if (value.Length == 0)
            {
                return string.Empty;
            }

            value = RemoveSeriesEditionSuffixes(value);
            value = Regex.Replace(value, @"\b(?:part|episode|chapter)\s+(?:\d+|[ivxlcdm]+)\b", string.Empty, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"(?:\s|[-:])(?:\d+|i{1,3}|iv|v|vi{0,3}|ix|x|xi|xii)\s*$", string.Empty, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"[\(\[\{][^\)\]\}]*[\)\]\}]", " ");
            value = Regex.Replace(value, @"[^\p{L}\p{N}]+", " ");
            return Regex.Replace(value.Trim(), @"\s+", " ").ToLowerInvariant();
        }

        private static string DisplaySeriesName(string title)
        {
            var value = Regex.Replace((title ?? string.Empty).Trim(), @"\s+", " ");
            value = RemoveSeriesEditionSuffixes(value);
            value = Regex.Replace(value, @"\b(?:part|episode|chapter)\s+(?:\d+|[ivxlcdm]+)\b", string.Empty, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"(?:\s|[-:])(?:\d+|i{1,3}|iv|v|vi{0,3}|ix|x|xi|xii)\s*$", string.Empty, RegexOptions.IgnoreCase);
            return Regex.Replace(value.Trim().Trim(',', ':', '-', '–', '—'), @"\s+", " ");
        }

        private static string RemoveSeriesEditionSuffixes(string value)
        {
            var normalized = value ?? string.Empty;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var updated = Regex.Replace(
                    normalized,
                    @"\s*[\(\[]\s*" + SeriesEditionSuffixPattern + @"\s*[\)\]]\s*$",
                    string.Empty,
                    RegexOptions.IgnoreCase);
                updated = Regex.Replace(
                    updated,
                    @"[\s,:\-–—]+" + SeriesEditionSuffixPattern + @"(?=\s*$)",
                    string.Empty,
                    RegexOptions.IgnoreCase);
                if (string.Equals(normalized, updated, StringComparison.Ordinal))
                {
                    break;
                }

                normalized = updated.Trim();
            }

            return normalized;
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
            // Preserve the existing pure-overload contract for tests and
            // integrations that use the established prefixed convention.
            return Build(seriesName, siblingGames, new SeriesTagConsistencyOptions
            {
                UsePrimaryTagClassification = true,
                PrimaryTagPrefix = "- "
            });
        }

        public static SeriesLibraryContext Build(
            string seriesName,
            IEnumerable<SeriesTagGameSnapshot> siblingGames,
            SeriesTagConsistencyOptions options)
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

            context.Baseline = DeriveBaseline(useful, options ?? new SeriesTagConsistencyOptions());
            return context;
        }

        private static SeriesBaseline DeriveBaseline(IList<SeriesTagGameSnapshot> games, SeriesTagConsistencyOptions options)
        {
            var cohort = SelectMechanicallySimilarCohort(games, options);
            if (cohort.Count < 2)
            {
                return null;
            }

            var primary = options.UsePrimaryTagClassification
                ? Consensus(
                    cohort,
                    x => x.Tags.Where(tag => IsPrimaryTag(tag, options)).Select(tag => NormalizePrimaryTag(tag, options)),
                    PrimarySupportThreshold(cohort.Count),
                    MaxPrimaryTags,
                    true,
                    options)
                : InferUnprefixedCoreTags(cohort, options);
            var inferredCoreKeys = options.UsePrimaryTagClassification
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(primary.Select(NormalizeTag), StringComparer.OrdinalIgnoreCase);

            var secondary = Consensus(
                cohort,
                game => game.Tags.Where(tag => !IsPrimaryTag(tag, options) && !inferredCoreKeys.Contains(NormalizeTag(tag))),
                SecondarySupportThreshold(cohort.Count),
                MaxSecondaryTags,
                false,
                options);

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

        private static List<SeriesTagGameSnapshot> SelectMechanicallySimilarCohort(IList<SeriesTagGameSnapshot> games, SeriesTagConsistencyOptions options)
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
                    .Where(candidateGame => ReferenceEquals(candidateGame, seed) || IsMechanicallySimilar(seed, candidateGame, options))
                    .ToList();
                var score = candidate.Sum(candidateGame => ReferenceEquals(candidateGame, seed) ? 0 : SimilarityScore(seed, candidateGame, options));

                if (best == null || candidate.Count > best.Count ||
                    (candidate.Count == best.Count && score > bestScore))
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best ?? new List<SeriesTagGameSnapshot>();
        }

        private static bool IsMechanicallySimilar(SeriesTagGameSnapshot left, SeriesTagGameSnapshot right, SeriesTagConsistencyOptions options)
        {
            var sharedPrimary = SharedCount(left.Tags.Where(x => IsPrimaryTag(x, options)).Select(x => NormalizePrimaryTag(x, options)), right.Tags.Where(x => IsPrimaryTag(x, options)).Select(x => NormalizePrimaryTag(x, options)));
            var sharedPerspective = SharedCount(left.Tags.Where(x => IsPerspectiveTag(x, options)).Select(NormalizeTag), right.Tags.Where(x => IsPerspectiveTag(x, options)).Select(NormalizeTag));
            var sharedSecondary = SharedCount(left.Tags.Where(x => !IsPrimaryTag(x, options) && !IsPerspectiveTag(x, options)).Select(NormalizeTag), right.Tags.Where(x => !IsPrimaryTag(x, options) && !IsPerspectiveTag(x, options)).Select(NormalizeTag));
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

            if (options != null && !options.UsePrimaryTagClassification &&
                sharedGenres > 0 && (sharedSecondary > 0 || sharedPerspective > 0))
            {
                return true;
            }

            return sharedGenres > 0 && sharedFeatures >= 2;
        }

        private static int SimilarityScore(SeriesTagGameSnapshot left, SeriesTagGameSnapshot right, SeriesTagConsistencyOptions options)
        {
            var sharedPrimary = SharedCount(left.Tags.Where(x => IsPrimaryTag(x, options)).Select(x => NormalizePrimaryTag(x, options)), right.Tags.Where(x => IsPrimaryTag(x, options)).Select(x => NormalizePrimaryTag(x, options)));
            var sharedPerspective = SharedCount(left.Tags.Where(x => IsPerspectiveTag(x, options)).Select(NormalizeTag), right.Tags.Where(x => IsPerspectiveTag(x, options)).Select(NormalizeTag));
            var sharedSecondary = SharedCount(left.Tags.Where(x => !IsPrimaryTag(x, options) && !IsPerspectiveTag(x, options)).Select(NormalizeTag), right.Tags.Where(x => !IsPrimaryTag(x, options) && !IsPerspectiveTag(x, options)).Select(NormalizeTag));
            var sharedFeatures = SharedCount(left.Features.Select(NormalizeTag), right.Features.Select(NormalizeTag));
            var sharedGenres = SharedCount(left.Genres.Select(NormalizeTag), right.Genres.Select(NormalizeTag));
            return Math.Min(sharedPrimary, 3) * 4 + Math.Min(sharedPerspective, 1) * 3 +
                   Math.Min(sharedSecondary, 4) * 2 + Math.Min(sharedFeatures, 3) + Math.Min(sharedGenres, 2);
        }

        private static List<string> InferUnprefixedCoreTags(
            IList<SeriesTagGameSnapshot> cohort,
            SeriesTagConsistencyOptions options)
        {
            var genreHints = new HashSet<string>(
                cohort.SelectMany(x => x.Genres ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeTag),
                StringComparer.OrdinalIgnoreCase);
            var candidates = new Dictionary<string, TagCount>(StringComparer.OrdinalIgnoreCase);
            var index = 0;

            foreach (var game in cohort)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var value in (game.Tags ?? new List<string>())
                    .Where(x => !IsPrimaryTag(x, options) && !string.IsNullOrWhiteSpace(x)))
                {
                    var key = NormalizeTag(value);
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    TagCount count;
                    if (!candidates.TryGetValue(key, out count))
                    {
                        count = new TagCount { Value = value.Trim(), FirstIndex = index++ };
                        candidates[key] = count;
                    }

                    count.Count++;
                }
            }

            var minimumSupport = Math.Max(2, PrimarySupportThreshold(cohort.Count));
            return candidates.Values
                .Where(x => x.Count >= minimumSupport)
                .OrderByDescending(x => x.Count * 10 +
                    (IsPerspectiveTag(x.Value, options) ? 3 : 0) +
                    (genreHints.Contains(NormalizeTag(x.Value)) ? 2 : 0))
                .ThenBy(x => x.FirstIndex)
                .Take(MaxPrimaryTags)
                .Select(x => x.Value)
                .ToList();
        }

        private static List<string> Consensus(
            IEnumerable<SeriesTagGameSnapshot> games,
            Func<SeriesTagGameSnapshot, IEnumerable<string>> selector,
            int minimumSupport,
            int maxItems,
            bool primary,
            SeriesTagConsistencyOptions options)
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
                            Value = primary ? NormalizePrimaryTag(value, options) : value.Trim(),
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
                .Select(x => primary ? NormalizePrimaryTag(x.Value, options) : x.Value)
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

        private static bool IsSeriesNameCompatible(string seriesName, string currentTitleKey)
        {
            var seriesKey = NormalizeSeriesTitleForInference(seriesName);
            return seriesKey.Length >= 4 &&
                   (string.Equals(seriesKey, currentTitleKey, StringComparison.OrdinalIgnoreCase) ||
                    currentTitleKey.StartsWith(seriesKey + " ", StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildInferenceEvidence(bool sameTitle, IEnumerable<string> explicitSeries)
        {
            var evidence = new List<string>();
            if (sameTitle)
            {
                evidence.Add("normalized sequel or edition title");
            }

            if ((explicitSeries ?? Enumerable.Empty<string>()).Any(x => !string.IsNullOrWhiteSpace(x)))
            {
                evidence.Add("existing Playnite Series assignment");
            }

            return string.Join("; ", evidence);
        }

        private static SeriesTagGameSnapshot ToSnapshot(Game game)
        {
            return ToSnapshot(null, game);
        }

        private static SeriesTagGameSnapshot ToSnapshot(IPlayniteAPI playniteApi, Game game)
        {
            return new SeriesTagGameSnapshot
            {
                Id = game.Id,
                Name = game.Name,
                Tags = Names(game.Tags, MaxTagsPerGame),
                Genres = Names(game.Genres, MaxGenresPerGame),
                Features = Names(game.Features, MaxFeaturesPerGame),
                SeriesNames = GetExplicitSeriesNames(playniteApi, game),
                HasExplicitSeriesIds = game.SeriesIds != null && game.SeriesIds.Count > 0
            };
        }

        private static List<string> GetExplicitSeriesNames(IPlayniteAPI playniteApi, Game game)
        {
            var result = Names(game == null ? null : game.Series, MaxRelatedGames);
            if (playniteApi == null || playniteApi.Database == null || playniteApi.Database.Series == null ||
                game == null || game.SeriesIds == null)
            {
                return result;
            }

            foreach (var seriesId in game.SeriesIds)
            {
                var series = playniteApi.Database.Series.Get(seriesId);
                if (series != null && !string.IsNullOrWhiteSpace(series.Name))
                {
                    result.Add(series.Name.Trim());
                }
            }

            return result
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRelatedGames)
                .ToList();
        }

        private static SeriesRelatedGameContext ToContext(SeriesTagGameSnapshot game)
        {
            return new SeriesRelatedGameContext
            {
                Name = game.Name,
                Tags = game.Tags,
                Genres = game.Genres,
                Features = game.Features,
                ExplicitSeries = game.SeriesNames
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
                Features = CleanStrings(game.Features, MaxFeaturesPerGame),
                SeriesNames = CleanStrings(game.SeriesNames, MaxRelatedGames),
                HasExplicitSeriesIds = game.HasExplicitSeriesIds
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

        private static bool IsPrimaryTag(string value, SeriesTagConsistencyOptions options)
        {
            if (options == null || !options.UsePrimaryTagClassification || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var prefix = EffectivePrimaryTagPrefix(options);
            return value.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                   (string.Equals(prefix, "- ", StringComparison.Ordinal) && value.TrimStart().StartsWith("-", StringComparison.Ordinal));
        }

        private static bool IsPerspectiveTag(string value, SeriesTagConsistencyOptions options)
        {
            var normalized = NormalizeTag(RemovePrimaryTagPrefix(value, options));
            return PerspectiveTags.Contains(normalized);
        }

        private static string NormalizePrimaryTag(string value, SeriesTagConsistencyOptions options)
        {
            var normalized = RemovePrimaryTagPrefix(value, options);
            return normalized.Length == 0 ? string.Empty : EffectivePrimaryTagPrefix(options) + normalized;
        }

        private static string RemovePrimaryTagPrefix(string value, SeriesTagConsistencyOptions options)
        {
            var normalized = (value ?? string.Empty).Trim();
            var prefix = EffectivePrimaryTagPrefix(options);
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(prefix.Length).Trim();
            }

            return Regex.Replace(normalized, @"^[-\s]+", string.Empty).Trim();
        }

        private static string EffectivePrimaryTagPrefix(SeriesTagConsistencyOptions options)
        {
            var prefix = options == null ? string.Empty : options.PrimaryTagPrefix;
            return string.IsNullOrWhiteSpace(prefix) ? "- " : prefix;
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
            public List<string> SeriesNames { get; set; }

            [JsonIgnore]
            public bool HasExplicitSeriesIds { get; set; }

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
                SeriesNames = new List<string>();
            }
        }
    }
}
