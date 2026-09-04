using MetaDataIAPlugin;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// Offline regression checks for trusted metadata boundaries and vocabulary.
/// Compile and run via tests/run-metadata-regressions.ps1.
/// </summary>
internal static class MetadataRegressionRunner
{
    private static int failures;

    private static void Main()
    {
        Test_DisabledXboxCannotContributeMetadata();
        Test_OfficialFeaturesNeverReplaceAiFeatures();
        Test_GenresAndFeaturesCanBeGeneratedTogether();
        Test_FlexibleUsersAreNotForcedIntoControlledGenres();
        Test_ControlledFeaturesCanBeCustomized();
        Test_PrimaryTagClassificationIsOptionalAndConfigurable();
        Test_FreshInstallDefaultsToFlexibleTaxonomy();
        Test_OldEnglishInstallDefaultsToFlexibleTaxonomy();
        Test_OldNonEnglishInstallDefaultsToFlexibleTaxonomy();
        Test_ExplicitControlledPresetIsPreserved();
        Test_ExplicitCustomPresetIsPreserved();
        Test_LanguageChangesDoNotChangeTaxonomyPreset();
        Test_EmptyFeaturesDoNotBorrowGenresOrTags();
        Test_OfficialLinksMergeAcrossSources();
        Test_VocabularyIsFieldSafeAndTermOnly();

        Console.WriteLine();
        if (failures == 0)
        {
            Console.WriteLine("ALL PASSED");
            Environment.Exit(0);
        }

        Console.WriteLine("FAILED: " + failures + " assertion(s)");
        Environment.Exit(1);
    }

    private static void Test_DisabledXboxCannotContributeMetadata()
    {
        var settings = CreateSettings();
        settings.MediaUseXboxStore = false;
        settings.MediaUseSteamOfficial = false;

        var stores = new OfficialStoreDataService(settings);
        AssertTrue("disabled Xbox source is not enabled", !stores.IsMetadataSourceEnabled(OfficialStoreDataService.SourceXboxStore));
        AssertTrue("disabled Steam source is not enabled", !stores.IsMetadataSourceEnabled(OfficialStoreDataService.SourceSteamOfficial));
        var sourceOrderMethod = typeof(OfficialStoreDataService).GetMethod("GetOfficialContextSourceOrder", BindingFlags.Instance | BindingFlags.NonPublic);
        var sourceOrder = ((IEnumerable<string>)sourceOrderMethod.Invoke(stores, new object[] { new Game() })).ToList();
        AssertTrue("disabled Xbox is absent from official fetch order", !sourceOrder.Contains(OfficialStoreDataService.SourceXboxStore));

        var service = new MetadataGenerationService(settings);
        SetOfficialContexts(service, new OfficialStoreMetadata
        {
            SourceName = OfficialStoreDataService.SourceXboxStore,
            IsExactMatch = true,
            Features = new List<string> { "Xbox Play Anywhere" }
        });
        var result = CreateResult(new[] { "Single Player", "Controller Support" });
        ApplyTrustedFactualFields(service, result);

        AssertEqual("disabled Xbox cannot appear in conflict set", 0, result.Conflicts.Count(x => x.Field == "features"));
        AssertEqual("disabled Xbox cannot replace features", "Single Player, Controller Support", Join(result.Features));
    }

    private static void Test_OfficialFeaturesNeverReplaceAiFeatures()
    {
        var settings = CreateSettings();
        settings.MediaUseXboxStore = true;

        var service = new MetadataGenerationService(settings);
        SetOfficialContexts(service, new OfficialStoreMetadata
        {
            SourceName = OfficialStoreDataService.SourceXboxStore,
            IsExactMatch = true,
            Features = new List<string> { "4K Ultra HD", "Optimized for Xbox Series X|S", "PC Game Pad", "Xbox Play Anywhere" }
        });
        var result = CreateResult(new[] { "Single Player", "Controller Support", "HDR", "Ultrawide", "Ray Tracing" });
        ApplyTrustedFactualFields(service, result);
        result.Normalize(settings, null);

        AssertEqual("official raw features do not replace AI features", "Single Player, Controller Support, HDR, Ultrawide, Ray Tracing", Join(result.Features));

        var rawResult = CreateResult(new[] { "4K Ultra HD", "Action-Adventure", "Exploration", "Xbox Play Anywhere" });
        rawResult.Normalize(settings, null);
        AssertEqual("raw feature labels are rejected during normalization", string.Empty, Join(rawResult.Features));

        var crossFieldResult = CreateResult(new[] { "Single Player" });
        crossFieldResult.Tags = new List<string> { "Controller Support", "Combat" };
        crossFieldResult.Normalize(settings, null);
        AssertEqual("feature labels do not leak into Tags", "Combat", Join(crossFieldResult.Tags));
    }

    private static void Test_GenresAndFeaturesCanBeGeneratedTogether()
    {
        var settings = CreateSettings();
        settings.GenerateGenres = true;
        settings.GenerateFeatures = true;
        settings.UseControlledGenreVocabulary = true;
        settings.UseControlledFeatureVocabulary = true;
        settings.MediaUseXboxStore = true;

        var service = new MetadataGenerationService(settings);
        SetOfficialContexts(service, new OfficialStoreMetadata
        {
            SourceName = OfficialStoreDataService.SourceXboxStore,
            IsExactMatch = true,
            Genres = new List<string> { "Action & adventure" },
            Features = new List<string> { "4K Ultra HD", "Xbox Play Anywhere" }
        });
        var result = CreateResult(new[] { "Single Player", "Controller Support" });
        result.Genres = new List<string> { "Action", "Adventure" };
        ApplyTrustedFactualFields(service, result);
        result.Normalize(settings, null);

        AssertEqual("AI genres remain normalized", "Action, Adventure", Join(result.Genres));
        AssertNotContains("raw official genre taxonomy is not returned", result.Genres, "Action & adventure");
        AssertEqual("genres do not contaminate features", "Single Player, Controller Support", Join(result.Features));
    }

    private static void Test_FlexibleUsersAreNotForcedIntoControlledGenres()
    {
        var settings = CreateSettings();
        settings.UseControlledGenreVocabulary = false;
        var result = CreateResult(new[] { "Achievements" });
        result.Genres = new List<string> { "Metroidvania" };

        result.Normalize(settings, null);

        AssertEqual("fresh settings use flexible taxonomy", MetaDataIASettings.TaxonomyFlexible, settings.TaxonomyPreset);
        AssertEqual("flexible Genres preserve general labels", "Metroidvania", Join(result.Genres));
        AssertEqual("flexible Features allow normal labels", "Achievements", Join(result.Features));

        settings.TaxonomyPreset = MetaDataIASettings.TaxonomyControlled;
        AssertTrue("controlled preset enables controlled Genres", settings.UseControlledGenreVocabulary);
        AssertTrue("controlled preset enables controlled Features", settings.UseControlledFeatureVocabulary);
        AssertTrue("controlled preset enables primary Tags", settings.UsePrimaryTagClassification);
        settings.TaxonomyPreset = MetaDataIASettings.TaxonomyFlexible;
        AssertTrue("flexible preset disables controlled Genres", !settings.UseControlledGenreVocabulary);
        AssertTrue("flexible preset disables primary Tags", !settings.UsePrimaryTagClassification);

        settings.UseControlledGenreVocabulary = true;
        settings.ControlledGenreVocabulary = "Action\nMetroidvania";
        var custom = CreateResult(new[] { "Achievements" });
        custom.Genres = new List<string> { "Metroidvania", "Horror" };
        custom.Normalize(settings, null);
        AssertEqual("custom controlled Genres use the editable list", "Metroidvania", Join(custom.Genres));
    }

    private static void Test_ControlledFeaturesCanBeCustomized()
    {
        var settings = CreateSettings();
        settings.UseControlledFeatureVocabulary = true;
        settings.ControlledFeatureVocabulary = "Achievements\nCloud Saves\n4K\nWorkshop";
        var result = CreateResult(new[] { "Achievements", "Cloud Saves", "4K", "Single Player", "Exploration" });

        result.Normalize(settings, null);

        AssertEqual("custom controlled Features are accepted", "Achievements, Cloud Saves, 4K", Join(result.Features));
        AssertNotContains("custom controlled Features reject tag values", result.Features, "Exploration");
        AssertNotContains("custom controlled Features reject values outside the list", result.Features, "Single Player");
    }

    private static void Test_FreshInstallDefaultsToFlexibleTaxonomy()
    {
        var settings = new MetaDataIASettings { Language = "en" };
        settings.EnsureDefaults(false);

        AssertEqual("fresh install defaults to Flexible", MetaDataIASettings.TaxonomyFlexible, settings.TaxonomyPreset);
        AssertTrue("fresh install leaves controlled Genres off", !settings.UseControlledGenreVocabulary);
        AssertTrue("fresh install leaves controlled Features off", !settings.UseControlledFeatureVocabulary);
        AssertTrue("fresh install leaves primary Tags off", !settings.UsePrimaryTagClassification);
    }

    private static void Test_OldEnglishInstallDefaultsToFlexibleTaxonomy()
    {
        var settings = new MetaDataIASettings { Language = "en" };
        settings.EnsureDefaults(true);

        AssertEqual("old English install defaults to Flexible", MetaDataIASettings.TaxonomyFlexible, settings.TaxonomyPreset);
        AssertTrue("old English install does not infer controlled Genres", !settings.UseControlledGenreVocabulary);
        AssertTrue("old English install does not infer controlled Features", !settings.UseControlledFeatureVocabulary);
        AssertTrue("old English install does not infer primary Tags", !settings.UsePrimaryTagClassification);
    }

    private static void Test_OldNonEnglishInstallDefaultsToFlexibleTaxonomy()
    {
        var settings = new MetaDataIASettings { Language = "es" };
        settings.EnsureDefaults(true);

        AssertEqual("old non-English install defaults to Flexible", MetaDataIASettings.TaxonomyFlexible, settings.TaxonomyPreset);
        AssertTrue("old non-English install leaves controlled Genres off", !settings.UseControlledGenreVocabulary);
        AssertTrue("old non-English install leaves controlled Features off", !settings.UseControlledFeatureVocabulary);
        AssertTrue("old non-English install leaves primary Tags off", !settings.UsePrimaryTagClassification);
    }

    private static void Test_ExplicitControlledPresetIsPreserved()
    {
        var settings = new MetaDataIASettings
        {
            Language = "en",
            TaxonomyPreset = MetaDataIASettings.TaxonomyControlled
        };
        settings.EnsureDefaults(true);

        AssertEqual("explicit Controlled preset is preserved", MetaDataIASettings.TaxonomyControlled, settings.TaxonomyPreset);
        AssertTrue("explicit Controlled preset enables controlled Genres", settings.UseControlledGenreVocabulary);
        AssertTrue("explicit Controlled preset enables controlled Features", settings.UseControlledFeatureVocabulary);
        AssertTrue("explicit Controlled preset enables primary Tags", settings.UsePrimaryTagClassification);
        AssertEqual("explicit Controlled preset retains primary Tag prefix", "- ", settings.PrimaryTagPrefix);
    }

    private static void Test_ExplicitCustomPresetIsPreserved()
    {
        var settings = new MetaDataIASettings
        {
            Language = "en",
            TaxonomyPreset = MetaDataIASettings.TaxonomyCustom,
            UseControlledGenreVocabulary = true,
            UseControlledFeatureVocabulary = false,
            UsePrimaryTagClassification = true,
            ControlledGenreVocabulary = "Action\nMetroidvania",
            ControlledFeatureVocabulary = "Achievements\nCloud Saves",
            PrimaryTagPrefix = "Core: "
        };
        settings.EnsureDefaults(true);

        AssertEqual("explicit Custom preset is preserved", MetaDataIASettings.TaxonomyCustom, settings.TaxonomyPreset);
        AssertTrue("Custom preset preserves controlled Genre choice", settings.UseControlledGenreVocabulary);
        AssertTrue("Custom preset preserves flexible Feature choice", !settings.UseControlledFeatureVocabulary);
        AssertTrue("Custom preset preserves primary Tag choice", settings.UsePrimaryTagClassification);
        AssertEqual("Custom preset preserves primary Tag prefix", "Core: ", settings.PrimaryTagPrefix);
        AssertContains("Custom preset preserves Genre vocabulary", settings.GetControlledVocabularyTerms("genres", "en"), "Metroidvania");
        AssertContains("Custom preset preserves Feature vocabulary", settings.GetControlledVocabularyTerms("features", "en"), "Cloud Saves");
    }

    private static void Test_LanguageChangesDoNotChangeTaxonomyPreset()
    {
        var controlled = new MetaDataIASettings
        {
            Language = "en",
            TaxonomyPreset = MetaDataIASettings.TaxonomyControlled
        };
        controlled.EnsureDefaults(true);
        controlled.Language = "es";
        controlled.EnsureDefaults(true);
        AssertEqual("changing language preserves Controlled preset", MetaDataIASettings.TaxonomyControlled, controlled.TaxonomyPreset);

        var custom = new MetaDataIASettings
        {
            Language = "es",
            TaxonomyPreset = MetaDataIASettings.TaxonomyCustom
        };
        custom.EnsureDefaults(true);
        custom.Language = "en";
        custom.EnsureDefaults(true);
        AssertEqual("changing language preserves Custom preset", MetaDataIASettings.TaxonomyCustom, custom.TaxonomyPreset);
    }

    private static void Test_PrimaryTagClassificationIsOptionalAndConfigurable()
    {
        var settings = CreateSettings();
        settings.UsePrimaryTagClassification = false;
        var flexible = CreateResult(new[] { "Single Player" });
        flexible.Tags = new List<string> { "FPS" };
        flexible.Normalize(settings, null);
        AssertEqual("disabled primary classification keeps normal Tags", "FPS", Join(flexible.Tags));

        settings.UsePrimaryTagClassification = true;
        settings.PrimaryTagPrefix = "Core: ";
        var controlled = CreateResult(new[] { "Single Player" });
        controlled.Tags = new List<string> { "- FPS" };
        controlled.Normalize(settings, null);
        AssertEqual("primary classification uses configured prefix", "Core: FPS", Join(controlled.Tags));
    }

    private static void Test_OfficialLinksMergeAcrossSources()
    {
        var settings = CreateSettings();
        settings.GenerateLinks = true;
        settings.MediaUseSteamOfficial = true;
        settings.MediaUseXboxStore = true;

        var service = new MetadataGenerationService(settings);
        SetOfficialContexts(
            service,
            new OfficialStoreMetadata
            {
                SourceName = OfficialStoreDataService.SourceSteamOfficial,
                IsExactMatch = true,
                Links = new List<Link> { new Link("Steam", "https://store.steampowered.com/app/1") }
            },
            new OfficialStoreMetadata
            {
                SourceName = OfficialStoreDataService.SourceXboxStore,
                IsExactMatch = true,
                Links = new List<Link>
                {
                    new Link("Xbox", "https://www.xbox.com/games/1"),
                    new Link("Duplicate", "https://store.steampowered.com/app/1")
                }
            });
        var result = CreateResult(new[] { "Single Player" });
        ApplyTrustedFactualFields(service, result);

        AssertEqual("links merge from enabled sources", 2, result.Links.Count);
        AssertTrue("merged links keep Steam", result.Links.Any(x => x.Url.Contains("steampowered.com")));
        AssertTrue("merged links keep Xbox", result.Links.Any(x => x.Url.Contains("xbox.com")));
    }

    private static void Test_EmptyFeaturesDoNotBorrowGenresOrTags()
    {
        var settings = CreateSettings();
        var result = CreateResult(Enumerable.Empty<string>());
        result.Genres = new List<string> { "Action-Adventure" };
        result.Tags = new List<string> { "Exploration", "Combat" };

        result.Normalize(settings, null);

        AssertEqual("empty features do not borrow genres or tags", string.Empty, Join(result.Features));
    }

    private static void Test_VocabularyIsFieldSafeAndTermOnly()
    {
        var settings = CreateSettings();
        settings.Language = "en";
        settings.UseControlledGenreVocabulary = true;
        settings.UseControlledFeatureVocabulary = true;
        settings.ControlledFeatureVocabulary = "Single Player\nHDR\nAchievements";
        settings.VocabularyMemory = string.Empty;
        settings.LearnVocabulary("en", new AiMetadataResult
        {
            Genres = new List<string> { "Action & adventure", "Survival Horror", "This is a complete free-form sentence" },
            Tags = new List<string> { "Combat", "Controller Support", "Achievements", "This game is excellent" },
            Features = new List<string> { "Single Player", "HDR", "Exploration", "Xbox Play Anywhere", "A complete feature sentence" },
            Categories = new List<string>()
        });

        var terms = settings.GetVocabularyTerms("en");
        AssertContains("genres learn genres", terms["genres"], "Action");
        AssertContains("genres expand compound taxonomy", terms["genres"], "Adventure");
        AssertContains("genres normalize survival horror", terms["genres"], "Horror");
        AssertNotContains("genres reject raw compound taxonomy", terms["genres"], "Action & adventure");
        AssertNotContains("genres reject prose", terms["genres"], "This is a complete free-form sentence");
        AssertContains("tags learn tags", terms["tags"], "Combat");
        AssertNotContains("tags reject feature value", terms["tags"], "Controller Support");
        AssertNotContains("tags reject configured feature value", terms["tags"], "Achievements");
        AssertNotContains("tags reject prose", terms["tags"], "This game is excellent");
        AssertContains("features learn canonical values", terms["features"], "Single Player");
        AssertContains("features learn canonical graphics value", terms["features"], "HDR");
        AssertNotContains("features reject tag value", terms["features"], "Exploration");
        AssertNotContains("features reject console marketing", terms["features"], "Xbox Play Anywhere");
    }

    private static MetaDataIASettings CreateSettings()
    {
        var settings = new MetaDataIASettings
        {
            Language = "en",
            GenerateGenres = true,
            GenerateFeatures = true,
            GenerateLinks = true,
            MaxGenres = 8,
            MaxFeatures = 8,
            MaxLinks = 8,
            MaxTags = 12,
            MediaUseSteamOfficial = true,
            MediaUseXboxStore = false
        };
        settings.EnsureDefaults(false);
        return settings;
    }

    private static AiMetadataResult CreateResult(IEnumerable<string> features)
    {
        return new AiMetadataResult
        {
            Features = features.ToList(),
            Genres = new List<string>(),
            Tags = new List<string>(),
            Links = new List<AiMetadataLink>(),
            Developers = new List<string>(),
            Publishers = new List<string>(),
            AgeRatings = new List<string>(),
            Regions = new List<string>(),
            Categories = new List<string>(),
            Series = new List<string>()
        };
    }

    private static void SetOfficialContexts(MetadataGenerationService service, params OfficialStoreMetadata[] contexts)
    {
        var field = typeof(MetadataGenerationService).GetField("officialContextForCurrentRequest", BindingFlags.Instance | BindingFlags.NonPublic);
        field.SetValue(service, contexts.ToList());
    }

    private static void ApplyTrustedFactualFields(MetadataGenerationService service, AiMetadataResult result)
    {
        var method = typeof(MetadataGenerationService).GetMethod("ApplyTrustedFactualFields", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(service, new object[] { result, null });
    }

    private static string Join(IEnumerable<string> values)
    {
        return string.Join(", ", values ?? Enumerable.Empty<string>());
    }

    private static void AssertContains(string name, IEnumerable<string> values, string expected)
    {
        AssertTrue(name, (values ?? Enumerable.Empty<string>()).Any(x => string.Equals(x, expected, StringComparison.OrdinalIgnoreCase)));
    }

    private static void AssertNotContains(string name, IEnumerable<string> values, string unexpected)
    {
        AssertTrue(name, !(values ?? Enumerable.Empty<string>()).Any(x => string.Equals(x, unexpected, StringComparison.OrdinalIgnoreCase)));
    }

    private static void AssertEqual(string name, object expected, object actual)
    {
        AssertTrue(name, object.Equals(expected, actual));
    }

    private static void AssertTrue(string name, bool condition)
    {
        Console.WriteLine((condition ? "[PASS] " : "[FAIL] ") + name);
        if (!condition)
        {
            failures++;
        }
    }
}
