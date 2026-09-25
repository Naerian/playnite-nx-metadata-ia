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

            NormalizeResultAgainstLibrary(api, result, settings);

            if (settings.GenerateDescription &&
                !string.IsNullOrWhiteSpace(result.Description) &&
                ShouldApplyScalar(settings.DescriptionApplyMode, game.Description))
            {
                game.Description = result.Description;
            }

            if (settings.GenerateGenres && settings.GenresApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.GenreIds = MergeIds(
                    api.Database.Genres,
                    game.GenreIds,
                    Ensure(api.Database.Genres, Limit(result.Genres, settings.MaxGenres), settings.PreferExistingGenres),
                    settings.GenresApplyMode,
                    settings.MaxGenres,
                    "genres",
                    settings.Language);
            }

            if (settings.GenerateTags && settings.TagsApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.TagIds = MergeIds(
                    api.Database.Tags,
                    game.TagIds,
                    Ensure(api.Database.Tags, Limit(result.Tags, settings.MaxTags), settings.PreferExistingTags),
                    settings.TagsApplyMode,
                    settings.MaxTags,
                    "tags",
                    settings.Language);
            }

            if (settings.GenerateFeatures && settings.FeaturesApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.FeatureIds = MergeIds(
                    api.Database.Features,
                    game.FeatureIds,
                    Ensure(api.Database.Features, Limit(result.Features, settings.MaxFeatures), settings.PreferExistingFeatures),
                    settings.FeaturesApplyMode,
                    settings.MaxFeatures,
                    "features",
                    settings.Language);
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
                if (!HasConflict(result, "ageRatings")) game.AgeRatingIds = MergeIds(game.AgeRatingIds, Ensure(api.Database.AgeRatings, Limit(result.AgeRatings, settings.MaxAgeRatings), settings.PreferExistingAgeRatings), settings.AgeRatingsApplyMode, settings.MaxAgeRatings);
            }

            if (settings.GenerateRegions && settings.RegionsApplyMode != MetaDataIASettings.ApplySkip)
            {
                if (!HasConflict(result, "regions")) game.RegionIds = MergeIds(game.RegionIds, Ensure(api.Database.Regions, Limit(result.Regions, settings.MaxRegions), false), settings.RegionsApplyMode, settings.MaxRegions);
            }

            if (settings.GenerateCategories && settings.CategoriesApplyMode != MetaDataIASettings.ApplySkip)
            {
                game.CategoryIds = MergeIds(
                    api.Database.Categories,
                    game.CategoryIds,
                    Ensure(api.Database.Categories, Limit(result.Categories, settings.MaxCategories), settings.PreferExistingCategories),
                    settings.CategoriesApplyMode,
                    settings.MaxCategories,
                    "categories",
                    settings.Language);
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

        private static void NormalizeResultAgainstLibrary(IPlayniteAPI api, AiMetadataResult result, MetaDataIASettings settings)
        {
            if (api == null || api.Database == null || result == null || settings == null)
            {
                return;
            }

            var learned = settings.GetVocabularyTerms(settings.Language) ?? new Dictionary<string, List<string>>();
            List<string> learnedField;

            learned.TryGetValue("genres", out learnedField);
            result.Genres = VocabularyTermNormalizer.NormalizeField(
                result.Genres, "genres", settings.Language, api.Database.Genres.Select(x => x.Name), learnedField,
                settings.MaxGenres, settings.PreferExistingGenres);

            learned.TryGetValue("tags", out learnedField);
            result.Tags = VocabularyTermNormalizer.NormalizeField(
                result.Tags, "tags", settings.Language, api.Database.Tags.Select(x => x.Name), learnedField,
                settings.MaxTags, settings.PreferExistingTags);

            learned.TryGetValue("features", out learnedField);
            result.Features = VocabularyTermNormalizer.NormalizeField(
                result.Features, "features", settings.Language, api.Database.Features.Select(x => x.Name), learnedField,
                settings.MaxFeatures, settings.PreferExistingFeatures);

            learned.TryGetValue("categories", out learnedField);
            result.Categories = VocabularyTermNormalizer.NormalizeField(
                result.Categories, "categories", settings.Language, api.Database.Categories.Select(x => x.Name), learnedField,
                settings.MaxCategories, settings.PreferExistingCategories);
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

                var trimmed = name.Trim();
                var existing = collection.FirstOrDefault(x => string.Equals(x.Name, trimmed, StringComparison.OrdinalIgnoreCase));
                if (existing == null && preferExistingOnly)
                {
                    var matchedName = LibraryNameMatching.FindExisting(trimmed, collection.Select(x => x.Name));
                    if (matchedName == null)
                    {
                        continue;
                    }

                    existing = collection.FirstOrDefault(x => string.Equals(x.Name, matchedName, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        continue;
                    }
                }

                var item = existing ?? collection.Add(trimmed);
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

        private static List<Guid> MergeIds<T>(
            IItemCollection<T> collection,
            List<Guid> current,
            IEnumerable<Guid> generated,
            string mode,
            int maxItems,
            string field,
            string language) where T : DatabaseObject
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

            // Append: drop current IDs that are vocabulary-equivalent to an incoming clean term,
            // so dirty launcher spellings do not remain next to their canonical form.
            var incomingNames = generatedList
                .Select(id =>
                {
                    var item = collection.Get(id);
                    return item == null ? null : item.Name;
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var keptCurrent = (current ?? new List<Guid>())
                .Where(id =>
                {
                    var item = collection.Get(id);
                    if (item == null || string.IsNullOrWhiteSpace(item.Name))
                    {
                        return false;
                    }

                    return !incomingNames.Any(incoming =>
                        VocabularyTermNormalizer.AreEquivalent(item.Name, incoming, field, language));
                })
                .ToList();

            return keptCurrent.Concat(generatedList).Distinct().Take(max).ToList();
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

            return true;
        }

        private static ObservableCollection<Link> MergeLinks(ObservableCollection<Link> current, IEnumerable<AiMetadataLink> generated, string mode, int maxItems)
        {
            var generatedLinks = (generated ?? Enumerable.Empty<AiMetadataLink>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url))
                .Select(x => new Link(string.IsNullOrWhiteSpace(x.Name) ? x.Url : x.Name, x.Url))
                .ToList();

            if (mode == MetaDataIASettings.ApplySkip)
            {
                return current ?? new ObservableCollection<Link>();
            }

            if (mode == MetaDataIASettings.ApplyEmptyOnly && current != null && current.Count > 0)
            {
                return current;
            }

            IEnumerable<Link> merged;
            if (mode == MetaDataIASettings.ApplyOverwrite)
            {
                merged = generatedLinks;
            }
            else
            {
                merged = (current ?? new ObservableCollection<Link>()).Concat(generatedLinks);
            }

            var result = new ObservableCollection<Link>();
            foreach (var link in merged)
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
                if (result.Count >= Math.Max(1, maxItems))
                {
                    break;
                }
            }

            return result;
        }
    }
}
