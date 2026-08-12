using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MetaDataIAPlugin
{
    public static class MetadataApplyService
    {
        public static void Apply(IPlayniteAPI api, Game game, AiMetadataResult result, MetaDataIASettings settings)
        {
            if (api == null || game == null || result == null)
            {
                return;
            }

            if (settings.GenerateDescription &&
                !string.IsNullOrWhiteSpace(result.Description) &&
                ShouldApplyScalar(settings.DescriptionApplyMode, game.Description))
            {
                game.Description = result.Description;
            }

            if (settings.GenerateGenres && settings.GenresApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.GenreIds = MergeIds(game.GenreIds, Ensure(api.Database.Genres, Limit(result.Genres, settings.MaxGenres), settings.PreferExistingGenres), settings.GenresApplyMode, settings.MaxGenres);
            }

            if (settings.GenerateTags && settings.TagsApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.TagIds = MergeIds(game.TagIds, Ensure(api.Database.Tags, Limit(result.Tags, settings.MaxTags), settings.PreferExistingTags), settings.TagsApplyMode, settings.MaxTags);
            }

            if (settings.GenerateFeatures && settings.FeaturesApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.FeatureIds = MergeIds(game.FeatureIds, Ensure(api.Database.Features, Limit(result.Features, settings.MaxFeatures), settings.PreferExistingFeatures), settings.FeaturesApplyMode, settings.MaxFeatures);
            }

            if (settings.GenerateDevelopers && settings.DevelopersApplyMode != MetaDataIASettings.ApplySkip)
            {
                if (!HasConflict(result, "developers")) game.DeveloperIds = MergeIds(game.DeveloperIds, Ensure(api.Database.Companies, Limit(result.Developers, settings.MaxDevelopers), false), settings.DevelopersApplyMode, settings.MaxDevelopers);
            }

            if (settings.GeneratePublishers && settings.PublishersApplyMode != MetaDataIASettings.ApplySkip)
            {
                if (!HasConflict(result, "publishers")) game.PublisherIds = MergeIds(game.PublisherIds, Ensure(api.Database.Companies, Limit(result.Publishers, settings.MaxPublishers), false), settings.PublishersApplyMode, settings.MaxPublishers);
            }

            if (settings.GenerateAgeRatings && settings.AgeRatingsApplyMode != MetaDataIASettings.ApplySkip)
            {
                if (!HasConflict(result, "ageRatings")) game.AgeRatingIds = MergeIds(game.AgeRatingIds, Ensure(api.Database.AgeRatings, Limit(result.AgeRatings, settings.MaxAgeRatings), false), settings.AgeRatingsApplyMode, settings.MaxAgeRatings);
            }

            if (settings.GenerateRegions && settings.RegionsApplyMode != MetaDataIASettings.ApplySkip)
            {
                if (!HasConflict(result, "regions")) game.RegionIds = MergeIds(game.RegionIds, Ensure(api.Database.Regions, Limit(result.Regions, settings.MaxRegions), false), settings.RegionsApplyMode, settings.MaxRegions);
            }

            if (settings.GenerateCategories && settings.CategoriesApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.CategoryIds = MergeIds(game.CategoryIds, Ensure(api.Database.Categories, Limit(result.Categories, settings.MaxCategories), settings.PreferExistingCategories), settings.CategoriesApplyMode, settings.MaxCategories);
            }

            if (settings.GenerateSortingName && settings.SortingNameApplyMode != MetaDataIASettings.ApplySkip)
            {
                var sortingName = string.IsNullOrWhiteSpace(result.SortingName)
                    ? SortingNameService.Generate(api, game)
                    : result.SortingName;
                if (!string.IsNullOrWhiteSpace(sortingName) && ShouldApplyScalar(settings.SortingNameApplyMode, game.SortingName))
                {
                    game.SortingName = sortingName;
                }
            }

            if (settings.GenerateLinks && settings.LinksApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.Links = MergeLinks(game.Links, result.Links, settings.LinksApplyMode, settings.MaxLinks);
            }

            if (settings.GenerateReleaseDate && !HasConflict(result, "releaseDate") && !string.IsNullOrWhiteSpace(result.ReleaseDate))
            {
                ReleaseDate parsed;
                if (ReleaseDate.TryDeserialize(result.ReleaseDate, out parsed) &&
                    (settings.ReleaseDateApplyMode == MetaDataIASettings.ApplyOverwrite || !game.ReleaseDate.HasValue))
                {
                    game.ReleaseDate = parsed;
                }
            }

            if (settings.GenerateSeries && !HasConflict(result, "series") && settings.SeriesApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.SeriesIds = MergeIds(game.SeriesIds, Ensure(api.Database.Series, Limit(result.Series, settings.MaxSeries), false), settings.SeriesApplyMode, settings.MaxSeries);
            }

            api.Database.Games.Update(game);
        }

        private static List<Guid> Ensure<T>(IItemCollection<T> collection, IEnumerable<string> names, bool preferExistingOnly) where T : DatabaseObject
        {
            var ids = new List<Guid>();
            foreach (var name in names ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var existing = collection.FirstOrDefault(x => string.Equals(x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existing == null && preferExistingOnly)
                {
                    continue;
                }

                var item = existing ?? collection.Add(name.Trim());
                ids.Add(item.Id);
            }

            return ids;
        }

        private static IEnumerable<string> Limit(IEnumerable<string> names, int maxItems)
        {
            return (names ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(Math.Max(1, maxItems));
        }

        private static bool HasConflict(AiMetadataResult result, string field)
        {
            return result != null && (result.Conflicts ?? new List<MetadataFieldConflict>())
                .Any(x => string.Equals(x.Field, field, StringComparison.OrdinalIgnoreCase));
        }

        private static List<Guid> MergeIds(List<Guid> current, IEnumerable<Guid> generated, string mode, int maxItems)
        {
            var generatedList = generated == null ? new List<Guid>() : generated.Where(x => x != Guid.Empty).Distinct().ToList();
            var max = Math.Max(1, maxItems);
            if (mode == MetaDataIASettings.ApplySkip)
            {
                return current ?? new List<Guid>();
            }

            if (mode == MetaDataIASettings.ApplyEmptyOnly && current != null && current.Count > 0)
            {
                return current;
            }

            if (mode == MetaDataIASettings.ApplyOverwrite)
            {
                return generatedList.Take(max).ToList();
            }

            return (current ?? new List<Guid>()).Concat(generatedList).Distinct().Take(max).ToList();
        }

        private static bool ShouldApplyScalar(string mode, string current)
        {
            if (mode == MetaDataIASettings.ApplySkip)
            {
                return false;
            }

            if (mode == MetaDataIASettings.ApplyEmptyOnly)
            {
                return string.IsNullOrWhiteSpace(current);
            }

            return mode == MetaDataIASettings.ApplyAppend || mode == MetaDataIASettings.ApplyOverwrite;
        }

        private static ObservableCollection<Link> MergeLinks(ObservableCollection<Link> current, IEnumerable<AiMetadataLink> generated, string mode, int maxItems)
        {
            var max = Math.Max(1, maxItems);
            var currentList = current == null ? new List<Link>() : current.Where(x => x != null).ToList();
            var generatedList = (generated ?? Enumerable.Empty<AiMetadataLink>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url))
                .Select(x => new Link(x.Name, x.Url))
                .Take(max)
                .ToList();

            if (mode == MetaDataIASettings.ApplySkip)
            {
                return new ObservableCollection<Link>(currentList);
            }

            if (mode == MetaDataIASettings.ApplyEmptyOnly && currentList.Count > 0)
            {
                return new ObservableCollection<Link>(currentList);
            }

            if (mode == MetaDataIASettings.ApplyOverwrite)
            {
                return new ObservableCollection<Link>(DeduplicateLinks(generatedList).Take(max));
            }

            return new ObservableCollection<Link>(DeduplicateLinks(currentList.Concat(generatedList)).Take(max));
        }

        private static List<Link> DeduplicateLinks(IEnumerable<Link> links)
        {
            var result = new List<Link>();
            foreach (var link in links ?? Enumerable.Empty<Link>())
            {
                if (link == null || string.IsNullOrWhiteSpace(link.Url))
                {
                    continue;
                }

                if (result.Any(x => string.Equals(x.Url, link.Url, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result.Add(link);
            }

            return result;
        }
    }
}
