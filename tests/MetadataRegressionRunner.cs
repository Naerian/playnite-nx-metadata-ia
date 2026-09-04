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
    }

    private static void Test_GenresAndFeaturesCanBeGeneratedTogether()
    {
        var settings = CreateSettings();
        settings.GenerateGenres = true;
        settings.GenerateFeatures = true;
        settings.MediaUseXboxStore = true;

        var service = new MetadataGenerationService(settings);
        SetOfficialContexts(service, new OfficialStoreMetadata
        {
            SourceName = OfficialStoreDataService.SourceXboxStore,
            IsExactMatch = true,
            Genres = new List<string> { "Action" },
            Features = new List<string> { "4K Ultra HD", "Xbox Play Anywhere" }
        });
        var result = CreateResult(new[] { "Single Player", "Controller Support" });
        result.Genres = new List<string> { "Action-Adventure" };
        ApplyTrustedFactualFields(service, result);

        AssertEqual("trusted genre remains available", "Action", Join(result.Genres));
        AssertEqual("genres do not contaminate features", "Single Player, Controller Support", Join(result.Features));
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
                Links = new List<AiMetadataLink> { new AiMetadataLink("Steam", "https://store.steampowered.com/app/1") }
            },
            new OfficialStoreMetadata
            {
                SourceName = OfficialStoreDataService.SourceXboxStore,
                IsExactMatch = true,
                Links = new List<AiMetadataLink>
                {
                    new AiMetadataLink("Xbox", "https://www.xbox.com/games/1"),
                    new AiMetadataLink("Duplicate", "https://store.steampowered.com/app/1")
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
        settings.VocabularyMemory = string.Empty;
        settings.LearnVocabulary("en", new AiMetadataResult
        {
            Genres = new List<string> { "Action", "This is a complete free-form sentence" },
            Tags = new List<string> { "Combat", "Controller Support", "This game is excellent" },
            Features = new List<string> { "Single Player", "HDR", "Exploration", "Xbox Play Anywhere", "A complete feature sentence" },
            Categories = new List<string>()
        });

        var terms = settings.GetVocabularyTerms("en");
        AssertContains("genres learn genres", terms["genres"], "Action");
        AssertNotContains("genres reject prose", terms["genres"], "This is a complete free-form sentence");
        AssertContains("tags learn tags", terms["tags"], "Combat");
        AssertNotContains("tags reject feature value", terms["tags"], "Controller Support");
        AssertNotContains("tags reject prose", terms["tags"], "This game is excellent");
        AssertContains("features learn canonical values", terms["features"], "Single Player");
        AssertContains("features learn canonical graphics value", terms["features"], "HDR");
        AssertNotContains("features reject tag value", terms["features"], "Exploration");
        AssertNotContains("features reject console marketing", terms["features"], "Xbox Play Anywhere");
    }

    private static MetaDataIASettings CreateSettings()
    {
        return new MetaDataIASettings
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
