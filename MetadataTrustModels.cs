using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    public class MetadataConflictValue
    {
        public string Source { get; set; }
        public string Value { get; set; }
    }

    public class MetadataFieldConflict
    {
        public string Field { get; set; }
        public List<MetadataConflictValue> Values { get; set; }

        public MetadataFieldConflict()
        {
            Values = new List<MetadataConflictValue>();
        }
    }

    public class MetadataChangeItem
    {
        public string Field { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
        public MetadataFieldProvenance Provenance { get; set; }
        public string Recommendation { get; set; }
        public string RecommendationReason { get; set; }
        public bool IsSelected { get; set; }
        public MetadataFieldConflict Conflict { get; set; }
    }

    public class MetadataSimulationResult
    {
        public Game Game { get; set; }
        public AiMetadataResult Result { get; set; }
        public List<MetadataChangeItem> Changes { get; set; }
        public List<MediaSimulationChange> MediaChanges { get; set; }
        public string Error { get; set; }

        public MetadataSimulationResult()
        {
            Changes = new List<MetadataChangeItem>();
            MediaChanges = new List<MediaSimulationChange>();
        }
    }

    public class MediaSimulationChange
    {
        public MediaKind Kind { get; set; }
        public MediaPreviewOption Option { get; set; }
        public MetaDataIASettings Settings { get; set; }
        public bool IsSelected { get; set; }
        public bool IsUserChosen { get; set; }

        public MediaSimulationChange()
        {
            IsSelected = true;
        }
    }

    public static class MetadataChangePreviewService
    {
        public static List<MetadataChangeItem> Build(IPlayniteAPI api, Game game, AiMetadataResult result, MetaDataIASettings settings)
        {
            var changes = new List<MetadataChangeItem>();
            if (api == null || game == null || result == null || settings == null)
            {
                return changes;
            }

            AddScalar(changes, "description", game.Description, result.Description, settings.GenerateDescription, settings.DescriptionApplyMode, result);
            AddList(changes, "genres", Names(game.Genres), result.Genres, settings.GenerateGenres, settings.GenresApplyMode, settings.MaxGenres, settings.PreferExistingGenres, api.Database.Genres.Select(x => x.Name), result);
            AddList(changes, "tags", Names(game.Tags), result.Tags, settings.GenerateTags, settings.TagsApplyMode, settings.MaxTags, settings.PreferExistingTags, api.Database.Tags.Select(x => x.Name), result);
            AddList(changes, "features", Names(game.Features), result.Features, settings.GenerateFeatures, settings.FeaturesApplyMode, settings.MaxFeatures, settings.PreferExistingFeatures, api.Database.Features.Select(x => x.Name), result);
            AddList(changes, "developers", Names(game.Developers), result.Developers, settings.GenerateDevelopers, settings.DevelopersApplyMode, settings.MaxDevelopers, settings.StrictCompanyAgeRegion, api.Database.Companies.Select(x => x.Name), result);
            AddList(changes, "publishers", Names(game.Publishers), result.Publishers, settings.GeneratePublishers, settings.PublishersApplyMode, settings.MaxPublishers, settings.StrictCompanyAgeRegion, api.Database.Companies.Select(x => x.Name), result);
            AddList(changes, "ageRatings", Names(game.AgeRatings), result.AgeRatings, settings.GenerateAgeRatings, settings.AgeRatingsApplyMode, settings.MaxAgeRatings, settings.PreferExistingAgeRatings, api.Database.AgeRatings.Select(x => x.Name), result);
            AddList(changes, "regions", Names(game.Regions), result.Regions, settings.GenerateRegions, settings.RegionsApplyMode, settings.MaxRegions, settings.StrictCompanyAgeRegion, api.Database.Regions.Select(x => x.Name), result);
            AddList(changes, "categories", Names(game.Categories), result.Categories, settings.GenerateCategories, settings.CategoriesApplyMode, settings.MaxCategories, settings.PreferExistingCategories, api.Database.Categories.Select(x => x.Name), result);
            AddScalar(changes, "releaseDate", game.ReleaseDate.HasValue ? game.ReleaseDate.Value.ToString() : string.Empty, result.ReleaseDate, settings.GenerateReleaseDate, settings.ReleaseDateApplyMode, result);
            AddList(changes, "series", Names(game.Series), result.Series, settings.GenerateSeries, settings.SeriesApplyMode, settings.MaxSeries, false, api.Database.Series.Select(x => x.Name), result);

            if (settings.GenerateSortingName && settings.SortingNameApplyMode != MetaDataIASettings.ApplySkip)
            {
                var sortingName = string.IsNullOrWhiteSpace(result.SortingName)
                    ? SortingNameService.Generate(api, game)
                    : result.SortingName;
                AddScalar(changes, "sortingName", game.SortingName, sortingName, true, settings.SortingNameApplyMode, result);
            }

            if (settings.GenerateLinks && settings.LinksApplyMode != MetaDataIASettings.ApplySkip)
            {
                var before = FormatLinks(game.Links);
                var current = game.Links == null ? new List<AiMetadataLink>() : game.Links.Select(x => new AiMetadataLink { Name = x.Name, Url = x.Url }).ToList();
                var generated = result.Links ?? new List<AiMetadataLink>();
                List<AiMetadataLink> after;
                if (settings.LinksApplyMode == MetaDataIASettings.ApplyEmptyOnly && current.Count > 0)
                {
                    after = current;
                }
                else if (settings.LinksApplyMode == MetaDataIASettings.ApplyOverwrite)
                {
                    after = generated.Take(Math.Max(1, settings.MaxLinks)).ToList();
                }
                else
                {
                    after = current.Concat(generated)
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url))
                        .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.First())
                        .Take(Math.Max(1, settings.MaxLinks))
                        .ToList();
                }

                AddIfChanged(changes, "links", before, FormatLinks(after), FindProvenance(result, "links"));
            }

            foreach (var change in changes)
            {
                change.Conflict = (result.Conflicts ?? new List<MetadataFieldConflict>()).FirstOrDefault(x => string.Equals(x.Field, change.Field, StringComparison.OrdinalIgnoreCase));
                MetadataChangeRecommendationService.Evaluate(change);
            }

            return changes;
        }

        private static void AddScalar(List<MetadataChangeItem> changes, string field, string current, string generated, bool enabled, string mode, AiMetadataResult result)
        {
            if (!enabled || mode == MetaDataIASettings.ApplySkip || string.IsNullOrWhiteSpace(generated))
            {
                return;
            }

            var after = mode == MetaDataIASettings.ApplyEmptyOnly && !string.IsNullOrWhiteSpace(current) ? current : generated;
            AddIfChanged(changes, field, current, after, FindProvenance(result, field));
        }

        private static void AddList(List<MetadataChangeItem> changes, string field, IEnumerable<string> currentValues, IEnumerable<string> generatedValues, bool enabled, string mode, int maxItems, bool existingOnly, IEnumerable<string> knownValues, AiMetadataResult result)
        {
            if (!enabled || mode == MetaDataIASettings.ApplySkip)
            {
                return;
            }

            var current = Clean(currentValues);
            var generated = Clean(generatedValues).Take(Math.Max(1, maxItems)).ToList();
            if (existingOnly)
            {
                generated = LibraryNameMatching.MapToExisting(generated, knownValues);
            }

            List<string> after;
            if (mode == MetaDataIASettings.ApplyEmptyOnly && current.Count > 0)
            {
                after = current;
            }
            else if (mode == MetaDataIASettings.ApplyOverwrite)
            {
                after = generated;
            }
            else
            {
                after = current.Concat(generated).Distinct(StringComparer.OrdinalIgnoreCase).Take(Math.Max(1, maxItems)).ToList();
            }

            AddIfChanged(changes, field, string.Join(", ", current), string.Join(", ", after), FindProvenance(result, field));
        }

        private static void AddIfChanged(List<MetadataChangeItem> changes, string field, string before, string after, MetadataFieldProvenance provenance)
        {
            before = before ?? string.Empty;
            after = after ?? string.Empty;
            if (string.Equals(before.Trim(), after.Trim(), StringComparison.Ordinal))
            {
                return;
            }

            changes.Add(new MetadataChangeItem { Field = field, Before = before, After = after, Provenance = provenance });
        }

        private static MetadataFieldProvenance FindProvenance(AiMetadataResult result, string field)
        {
            return (result.Provenance ?? new List<MetadataFieldProvenance>()).FirstOrDefault(x => string.Equals(x.Field, field, StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> Names(IEnumerable<DatabaseObject> values)
        {
            return values == null ? new List<string>() : values.Where(x => x != null).Select(x => x.Name).ToList();
        }

        private static List<string> Clean(IEnumerable<string> values)
        {
            return (values ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string FormatLinks(IEnumerable<Link> links)
        {
            return links == null ? string.Empty : string.Join(Environment.NewLine, links.Where(x => x != null).Select(x => x.Name + " | " + x.Url));
        }

        private static string FormatLinks(IEnumerable<AiMetadataLink> links)
        {
            return links == null ? string.Empty : string.Join(Environment.NewLine, links.Where(x => x != null).Select(x => x.Name + " | " + x.Url));
        }
    }

    public static class MetadataChangeRecommendationService
    {
        public const string Recommended = "recommended";
        public const string Review = "review";
        public const string KeepCurrent = "keep-current";

        public const string ReasonMissing = "fills-missing";
        public const string ReasonTrusted = "trusted-source";
        public const string ReasonDeterministic = "deterministic";
        public const string ReasonAddsInformation = "adds-information";
        public const string ReasonLowConfidence = "low-confidence";
        public const string ReasonRemovesInformation = "removes-information";
        public const string ReasonShorterDescription = "shorter-description";
        public const string ReasonReplacesExisting = "replaces-existing";
        public const string ReasonEmptyResult = "empty-result";
        public const string ReasonSourceConflict = "source-conflict";

        public static void Evaluate(MetadataChangeItem change)
        {
            if (change == null)
            {
                return;
            }

            var before = (change.Before ?? string.Empty).Trim();
            var after = (change.After ?? string.Empty).Trim();
            if (change.Conflict != null && change.Conflict.Values != null && change.Conflict.Values.Count > 1)
            {
                Set(change, Review, ReasonSourceConflict);
                return;
            }
            if (string.IsNullOrWhiteSpace(after))
            {
                Set(change, KeepCurrent, ReasonEmptyResult);
                return;
            }

            var confidence = change.Provenance == null ? string.Empty : change.Provenance.Confidence ?? string.Empty;
            var method = change.Provenance == null ? string.Empty : change.Provenance.Method ?? string.Empty;
            if (string.Equals(confidence, "low", StringComparison.OrdinalIgnoreCase) ||
                (IsStrictFactualField(change.Field) &&
                 (change.Provenance == null || string.Equals(method, "generated-from-identity", StringComparison.OrdinalIgnoreCase))))
            {
                Set(change, Review, ReasonLowConfidence);
                return;
            }

            if (string.IsNullOrWhiteSpace(before))
            {
                Set(change, Recommended, ReasonMissing);
                return;
            }

            if (IsListField(change.Field))
            {
                var beforeCount = CountItems(before, change.Field);
                var afterCount = CountItems(after, change.Field);
                if (afterCount < beforeCount)
                {
                    Set(change, Review, ReasonRemovesInformation);
                    return;
                }

                if (afterCount > beforeCount)
                {
                    Set(change, Recommended, ReasonAddsInformation);
                    return;
                }
            }

            if (string.Equals(change.Field, "description", StringComparison.OrdinalIgnoreCase))
            {
                var beforeLength = VisibleLength(before);
                var afterLength = VisibleLength(after);
                if (beforeLength >= 180 && afterLength < beforeLength * 0.65)
                {
                    Set(change, Review, ReasonShorterDescription);
                    return;
                }
            }

            if (string.Equals(method, "deterministic", StringComparison.OrdinalIgnoreCase))
            {
                Set(change, Recommended, ReasonDeterministic);
                return;
            }

            if (string.Equals(method, "trusted-context", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(confidence, "high", StringComparison.OrdinalIgnoreCase))
            {
                Set(change, Recommended, ReasonTrusted);
                return;
            }

            Set(change, Review, ReasonReplacesExisting);
        }

        private static void Set(MetadataChangeItem change, string recommendation, string reason)
        {
            change.Recommendation = recommendation;
            change.RecommendationReason = reason;
            change.IsSelected = string.Equals(recommendation, Recommended, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsListField(string field)
        {
            return !string.Equals(field, "description", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(field, "sortingName", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStrictFactualField(string field)
        {
            return string.Equals(field, "developers", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field, "publishers", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field, "ageRatings", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field, "regions", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field, "releaseDate", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field, "series", StringComparison.OrdinalIgnoreCase);
        }

        private static int CountItems(string value, string field)
        {
            if (string.Equals(field, "links", StringComparison.OrdinalIgnoreCase))
            {
                return value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            }

            return value.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Count(x => !string.IsNullOrWhiteSpace(x));
        }

        private static int VisibleLength(string value)
        {
            return Regex.Replace(value ?? string.Empty, "<[^>]+>", string.Empty).Trim().Length;
        }
    }
}
