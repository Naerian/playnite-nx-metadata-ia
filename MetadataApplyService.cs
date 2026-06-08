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

            if (ShouldApplyScalar(settings.DescriptionApplyMode, game.Description))
            {
                game.Description = result.Description;
            }

            if (settings.GenerateGenres && settings.GenresApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.GenreIds = MergeIds(game.GenreIds, Ensure(api.Database.Genres, result.Genres, settings.PreferExistingGenres), settings.GenresApplyMode);
            }

            if (settings.GenerateTags && settings.TagsApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.TagIds = MergeIds(game.TagIds, Ensure(api.Database.Tags, result.Tags, settings.PreferExistingTags), settings.TagsApplyMode);
            }

            if (settings.GenerateFeatures && settings.FeaturesApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.FeatureIds = MergeIds(game.FeatureIds, Ensure(api.Database.Features, result.Features, settings.PreferExistingFeatures), settings.FeaturesApplyMode);
            }

            if (settings.GenerateDevelopers && settings.DevelopersApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.DeveloperIds = MergeIds(game.DeveloperIds, Ensure(api.Database.Companies, result.Developers, false), settings.DevelopersApplyMode);
            }

            if (settings.GeneratePublishers && settings.PublishersApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.PublisherIds = MergeIds(game.PublisherIds, Ensure(api.Database.Companies, result.Publishers, false), settings.PublishersApplyMode);
            }

            if (settings.GenerateAgeRatings && settings.AgeRatingsApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.AgeRatingIds = MergeIds(game.AgeRatingIds, Ensure(api.Database.AgeRatings, result.AgeRatings, false), settings.AgeRatingsApplyMode);
            }

            if (settings.GenerateRegions && settings.RegionsApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.RegionIds = MergeIds(game.RegionIds, Ensure(api.Database.Regions, result.Regions, false), settings.RegionsApplyMode);
            }

            if (settings.GenerateCategories && settings.CategoriesApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.CategoryIds = MergeIds(game.CategoryIds, Ensure(api.Database.Categories, result.Categories, settings.PreferExistingCategories), settings.CategoriesApplyMode);
            }

            if (settings.GenerateSortingName && settings.SortingNameApplyMode != MetaDataIASettings.ApplySkip)
            {
                var sortingName = SortingNameService.Generate(api, game);
                if (!string.IsNullOrWhiteSpace(sortingName) && ShouldApplyScalar(settings.SortingNameApplyMode, game.SortingName))
                {
                    game.SortingName = sortingName;
                }
            }

            if (settings.GenerateLinks && settings.LinksApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.Links = MergeLinks(game.Links, result.Links, settings.LinksApplyMode);
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

        private static List<Guid> MergeIds(List<Guid> current, IEnumerable<Guid> generated, string mode)
        {
            var generatedList = generated == null ? new List<Guid>() : generated.Where(x => x != Guid.Empty).Distinct().ToList();
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
                return generatedList;
            }

            return (current ?? new List<Guid>()).Concat(generatedList).Distinct().ToList();
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

        private static ObservableCollection<Link> MergeLinks(ObservableCollection<Link> current, IEnumerable<AiMetadataLink> generated, string mode)
        {
            var currentList = current == null ? new List<Link>() : current.Where(x => x != null).ToList();
            var generatedList = (generated ?? Enumerable.Empty<AiMetadataLink>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url))
                .Select(x => new Link(x.Name, x.Url))
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
                return new ObservableCollection<Link>(DeduplicateLinks(generatedList));
            }

            return new ObservableCollection<Link>(DeduplicateLinks(currentList.Concat(generatedList)));
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
