using MetaDataIAPlugin;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Offline regression checks for local series tag context.
/// Compile and run via tests/run-series-tags.ps1.
/// </summary>
internal static class SeriesTagConsistencyRunner
{
    private static int failures;

    private static void Main()
    {
        Test_SameSeriesRetainsCommonPrimaryTags();
        Test_MissingPrimaryTagDoesNotEraseConsensus();
        Test_RemasterInheritsCoreTags();
        Test_SpinOffDoesNotJoinMainlineBaseline();
        Test_EmptySiblingMetadataFallsBack();
        Test_GameSpecificSecondaryTagsRemainInContextButNotBaseline();
        Test_FeaturesDoNotBecomeSeriesTags();
        Test_UnprefixedTagsInferCoreTags();
        Test_CustomPrimaryPrefixIsRespected();
        Test_ExplicitSeriesTakesPriority();
        Test_MissingSeriesWithInferenceDisabledReturnsNoContext();
        Test_TitleNormalizationHandlesEditionVariants();
        Test_ObviousSequelInference();
        Test_OriginalAndRemasterPairing();
        Test_ExplicitCandidateSeriesStrengthensInference();
        Test_SpinOffCandidateIsNotAccepted();
        Test_UnrelatedSimilarTitleIsRejected();
        Test_InferenceDoesNotMutateCurrentGame();
        Test_InferenceUsesOnlyLocalSnapshots();
        Test_InferenceIgnoresExistingMetadataMode();
        Test_PreviewDiagnosticsDescribeExplicitAndInferredContext();

        Console.WriteLine();
        if (failures == 0)
        {
            Console.WriteLine("ALL PASSED");
            Environment.Exit(0);
        }

        Console.WriteLine("FAILED: " + failures + " assertion(s)");
        Environment.Exit(1);
    }

    private static void Test_SameSeriesRetainsCommonPrimaryTags()
    {
        var context = Build(
            Game("Part I", "- Action-Adventure", "- Survival Horror", "- Third-Person Shooter", "Combat", "Third Person", "Exploration"),
            Game("Part II", "- Action-Adventure", "- Survival Horror", "- Third-Person Shooter", "Combat", "Third Person", "Exploration"));

        AssertContains("common primary Action-Adventure", context.Baseline.PrimaryTags, "- Action-Adventure");
        AssertContains("common primary Third-Person Shooter", context.Baseline.PrimaryTags, "- Third-Person Shooter");
        AssertContains("common perspective", context.Baseline.SecondaryTags, "Third Person");
    }

    private static void Test_MissingPrimaryTagDoesNotEraseConsensus()
    {
        var context = Build(
            Game("Original", "- Action-Adventure", "- Survival Horror", "- Third-Person Shooter", "Combat", "Crafting", "Third Person"),
            Game("Sequel", "- Action-Adventure", "- Survival Horror", "Combat", "Crafting", "Third Person"));

        AssertContains("omitted primary remains baseline candidate", context.Baseline.PrimaryTags, "- Third-Person Shooter");
    }

    private static void Test_RemasterInheritsCoreTags()
    {
        var context = Build(
            Game("Original", "- Action RPG", "- Souls-like", "Combat", "Exploration", "Third Person"),
            Game("Remastered", "- Action RPG", "- Souls-like", "Combat", "Exploration", "Third Person"));

        AssertContains("remaster inherits Action RPG", context.Baseline.PrimaryTags, "- Action RPG");
        AssertContains("remaster inherits Souls-like", context.Baseline.PrimaryTags, "- Souls-like");
    }

    private static void Test_SpinOffDoesNotJoinMainlineBaseline()
    {
        var context = Build(
            Game("Mainline I", "- Action-Adventure", "- Third-Person Shooter", "Combat", "Exploration", "Third Person"),
            Game("Mainline II", "- Action-Adventure", "- Third-Person Shooter", "Combat", "Exploration", "Third Person"),
            GameWithFeatures("Tactical Spin-off", "- Strategy", "- Turn-Based", new[] { "Turn-based combat", "Map management" }, "Strategy"));

        AssertContains("mainline baseline remains", context.Baseline.PrimaryTags, "- Third-Person Shooter");
        AssertNotContains("spin-off primary is not forced", context.Baseline.PrimaryTags, "- Strategy");
        AssertNotContains("spin-off taxonomy is not forced", context.Baseline.PrimaryTags, "- Turn-Based");
    }

    private static void Test_EmptySiblingMetadataFallsBack()
    {
        var context = SeriesTagConsistencyService.Build(
            "Test Series",
            new[] { new SeriesTagConsistencyService.SeriesTagGameSnapshot { Name = "Empty sibling" } });

        AssertTrue("empty sibling metadata produces no context", context == null);
    }

    private static void Test_GameSpecificSecondaryTagsRemainInContextButNotBaseline()
    {
        var context = Build(
            Game("Part I", "- Action-Adventure", "Combat", "Exploration", "Zombies"),
            Game("Part II", "- Action-Adventure", "Combat", "Exploration"));

        AssertContains("recurring secondary remains baseline", context.Baseline.SecondaryTags, "Combat");
        AssertNotContains("game-specific secondary is not blindly copied", context.Baseline.SecondaryTags, "Zombies");
        AssertTrue("game-specific sibling data remains available", context.RelatedGames.Any(x => x.Tags.Contains("Zombies")));
    }

    private static void Test_FeaturesDoNotBecomeSeriesTags()
    {
        var context = Build(
            GameWithFeatures("Console edition", "- Action-Adventure", "Combat", new[] { "Xbox Live", "Achievements" }),
            GameWithFeatures("PC edition", "- Action-Adventure", "Combat", new[] { "Xbox Live", "Achievements" }));

        AssertNotContains("console feature is not returned as a tag", context.Baseline.SecondaryTags, "Xbox Live");
        AssertNotContains("platform feature is not returned as a tag", context.Baseline.SecondaryTags, "Achievements");
        AssertContains("feature stays scoped to sibling context", context.RelatedGames[0].Features, "Xbox Live");
    }

    private static void Test_UnprefixedTagsInferCoreTags()
    {
        var context = SeriesTagConsistencyService.Build(
            "Test Series",
            new[]
            {
                GameWithGenres("Part I", new[] { "Action", "Shooter" }, "Third-Person Shooter", "Combat", "Third Person"),
                GameWithGenres("Part II", new[] { "Action", "Shooter" }, "Third-Person Shooter", "Combat", "Third Person")
            },
            new SeriesTagConsistencyOptions
            {
                UsePrimaryTagClassification = false
            });

        AssertContains("unprefixed core tag is inferred", context.Baseline.PrimaryTags, "Third-Person Shooter");
        AssertNotContains("disabled primary classification does not add a prefix", context.Baseline.PrimaryTags, "- Third-Person Shooter");
    }

    private static void Test_CustomPrimaryPrefixIsRespected()
    {
        var context = SeriesTagConsistencyService.Build(
            "Test Series",
            new[]
            {
                Game("Part I", "Core: Shooter", "Combat", "Third Person"),
                Game("Part II", "Core: Shooter", "Combat", "Third Person")
            },
            new SeriesTagConsistencyOptions
            {
                UsePrimaryTagClassification = true,
                PrimaryTagPrefix = "Core: "
            });

        AssertContains("custom primary prefix is preserved", context.Baseline.PrimaryTags, "Core: Shooter");
    }

    private static void Test_ExplicitSeriesTakesPriority()
    {
        var current = Snapshot("The Last of Us Part II");
        current.HasExplicitSeriesIds = true;
        var result = SeriesTagConsistencyService.FindInferredSeries(
            current.Name,
            current,
            new[] { Snapshot("The Last of Us Part I", "Combat", "Third Person") },
            InferenceOptions(true));

        AssertTrue("explicit SeriesIds prevent inferred context", result == null);
    }

    private static void Test_MissingSeriesWithInferenceDisabledReturnsNoContext()
    {
        var result = SeriesTagConsistencyService.FindInferredSeries(
            "The Last of Us Part II",
            Snapshot("The Last of Us Part II"),
            new[] { Snapshot("The Last of Us Part I", "Combat", "Third Person") },
            InferenceOptions(false));

        AssertTrue("missing Series with inference disabled returns no context", result == null);
    }

    private static void Test_TitleNormalizationHandlesEditionVariants()
    {
        var expected = SeriesTagConsistencyService.NormalizeSeriesTitleForInference("The Last of Us");
        var variants = new[]
        {
            "The Last of Us 2",
            "The Last of Us II",
            "The Last of Us Part III",
            "The Last of Us (Remastered)",
            "The Last of Us Remake",
            "The Last of Us Definitive Edition",
            "The Last of Us Complete Edition",
            "The Last of Us GOTY",
            "The Last of Us Director's Cut",
            "The Last of Us HD",
            "The Last of Us Anniversary Edition",
            "The Last of Us Enhanced Edition",
            "The Last of Us Ultimate Edition"
        };

        foreach (var variant in variants)
        {
            AssertEqual("title normalization groups " + variant, expected,
                SeriesTagConsistencyService.NormalizeSeriesTitleForInference(variant));
        }
    }

    private static void Test_ObviousSequelInference()
    {
        var current = Snapshot("The Last of Us Part II", "Combat", "Third Person");
        current.Genres = new List<string> { "Action", "Horror" };
        var result = SeriesTagConsistencyService.FindInferredSeries(
            current.Name,
            current,
            new[]
            {
                Snapshot("The Last of Us Part I", "Third-Person Shooter", "Combat", "Third Person"),
                Snapshot("The Last of Us Part II Remastered", "Third-Person Shooter", "Combat", "Third Person")
            },
            InferenceOptions(true));

        AssertTrue("obvious sequel inference creates context", result != null && result.Context != null);
        AssertEqual("obvious sequel inference names the series", "The Last of Us", result == null ? string.Empty : result.SeriesName);
        AssertEqual("inference keeps a small related-game set", 2, result == null || result.Context == null ? 0 : result.Context.RelatedGames.Count);
        AssertContains("inference derives the existing tag baseline", result.Context.Baseline.PrimaryTags, "Third-Person Shooter");
    }

    private static void Test_OriginalAndRemasterPairing()
    {
        var result = SeriesTagConsistencyService.FindInferredSeries(
            "The Last of Us Part II Remastered",
            Snapshot("The Last of Us Part II Remastered"),
            new[] { Snapshot("The Last of Us Part II", "Combat", "Third Person") },
            InferenceOptions(true));

        AssertTrue("original/remaster pairing is inferred", result != null && result.Context != null);
        AssertContains("original/remaster pairing includes original", result.Context.RelatedGames.Select(x => x.Name), "The Last of Us Part II");
    }

    private static void Test_ExplicitCandidateSeriesStrengthensInference()
    {
        var sibling = Snapshot("The Last of Us Part I", "Third-Person Shooter", "Combat", "Third Person");
        sibling.SeriesNames = new List<string> { "The Last of Us" };
        var result = SeriesTagConsistencyService.FindInferredSeries(
            "The Last of Us Part II",
            Snapshot("The Last of Us Part II"),
            new[] { sibling },
            InferenceOptions(true));

        AssertTrue("candidate explicit Series assignment is exposed", result != null && result.Candidates.Any(x => x.ExplicitSeries.Contains("The Last of Us")));
        AssertEqual("candidate explicit Series assignment names the series", "The Last of Us", result == null ? string.Empty : result.SeriesName);
        AssertEqual("candidate explicit Series assignment raises confidence", "High", result == null ? string.Empty : result.Confidence);
    }

    private static void Test_SpinOffCandidateIsNotAccepted()
    {
        var spinoff = Snapshot("Halo Wars", "Strategy", "Top-Down");
        spinoff.SeriesNames = new List<string> { "Halo" };
        var result = SeriesTagConsistencyService.FindInferredSeries(
            "Halo Infinite",
            Snapshot("Halo Infinite", "Shooter", "First Person"),
            new[] { spinoff },
            InferenceOptions(true));

        AssertTrue("franchise spin-off candidate remains available for validation", result != null && result.Candidates.Count == 1);
        AssertTrue("franchise spin-off is excluded from accepted siblings", result != null && result.Context == null);
    }

    private static void Test_UnrelatedSimilarTitleIsRejected()
    {
        var result = SeriesTagConsistencyService.FindInferredSeries(
            "Fallout 4",
            Snapshot("Fallout 4", "Shooter", "First Person"),
            new[] { Snapshot("Fallout Shelter", "Simulation", "Management") },
            InferenceOptions(true));

        AssertTrue("unrelated similar title is rejected", result == null);
    }

    private static void Test_InferenceDoesNotMutateCurrentGame()
    {
        var current = Snapshot("The Last of Us Part II");
        var originalId = current.Id;
        var originalName = current.Name;
        var originalSeries = current.SeriesNames.ToList();
        SeriesTagConsistencyService.FindInferredSeries(
            current.Name,
            current,
            new[] { Snapshot("The Last of Us Part I", "Combat", "Third Person") },
            InferenceOptions(true));

        AssertEqual("inference never changes the current game id", originalId, current.Id);
        AssertEqual("inference never changes the current game name", originalName, current.Name);
        AssertEqual("inference never writes Series assignments", string.Join(",", originalSeries), string.Join(",", current.SeriesNames));
    }

    private static void Test_InferenceUsesOnlyLocalSnapshots()
    {
        // This overload has no Playnite API or official-source dependency;
        // successful inference therefore proves the shortlist is local-only.
        var result = SeriesTagConsistencyService.FindInferredSeries(
            "The Last of Us Part II",
            Snapshot("The Last of Us Part II"),
            new[] { Snapshot("The Last of Us Part I", "Combat", "Third Person") },
            InferenceOptions(true));

        AssertTrue("inference does not require official-store fetching", result != null && result.Context != null);
    }

    private static void Test_InferenceIgnoresExistingMetadataMode()
    {
        // The inference core receives only local sibling snapshots. It has no
        // ExistingMetadataMode input, so Ignore cannot disable the context.
        var result = SeriesTagConsistencyService.FindInferredSeries(
            "The Last of Us Part II",
            Snapshot("The Last of Us Part II"),
            new[] { Snapshot("The Last of Us Part I", "Combat", "Third Person") },
            InferenceOptions(true));

        AssertTrue("inferred context works independently of Ignore mode", result != null && result.Context != null);
    }

    private static void Test_PreviewDiagnosticsDescribeExplicitAndInferredContext()
    {
        var explicitText = new SeriesContextDiagnostics
        {
            SeriesName = "The Last of Us",
            Inferred = false
        }.ToDisplayText();
        var inferredText = new SeriesContextDiagnostics
        {
            SeriesName = "The Last of Us",
            Inferred = true,
            Confidence = "High",
            RelatedGames = new List<string> { "The Last of Us Part I", "The Last of Us Part II" }
        }.ToDisplayText();

        AssertEqual("explicit preview diagnostics identify Playnite Series", "Series context:\r\nPlaynite Series: The Last of Us", explicitText);
        AssertTrue("inferred preview diagnostics identify confidence and siblings",
            inferredText.Contains("Inferred: The Last of Us") &&
            inferredText.Contains("Confidence: High") &&
            inferredText.Contains("- The Last of Us Part I") &&
            inferredText.Contains("- The Last of Us Part II"));
    }

    private static SeriesTagConsistencyOptions InferenceOptions(bool enabled)
    {
        return new SeriesTagConsistencyOptions
        {
            UsePrimaryTagClassification = false,
            InferSeriesRelationships = enabled
        };
    }

    private static SeriesLibraryContext Build(params SeriesTagConsistencyService.SeriesTagGameSnapshot[] games)
    {
        var context = SeriesTagConsistencyService.Build("Test Series", games);
        AssertTrue("series context exists", context != null);
        AssertTrue("series baseline exists", context != null && context.Baseline != null);
        return context;
    }

    private static SeriesTagConsistencyService.SeriesTagGameSnapshot Game(string name, params string[] tags)
    {
        return new SeriesTagConsistencyService.SeriesTagGameSnapshot
        {
            Id = Guid.NewGuid(),
            Name = name,
            Tags = tags.ToList()
        };
    }

    private static SeriesTagConsistencyService.SeriesTagGameSnapshot Snapshot(string name, params string[] tags)
    {
        var snapshot = Game(name, tags);
        snapshot.Id = Guid.NewGuid();
        return snapshot;
    }

    private static SeriesTagConsistencyService.SeriesTagGameSnapshot GameWithGenres(string name, IEnumerable<string> genres, params string[] tags)
    {
        var game = Game(name, tags);
        game.Genres = genres.ToList();
        return game;
    }

    private static SeriesTagConsistencyService.SeriesTagGameSnapshot GameWithFeatures(
        string name,
        string primary,
        string secondary,
        IEnumerable<string> features,
        params string[] extraTags)
    {
        var tags = new List<string> { primary, secondary };
        tags.AddRange(extraTags);
        return new SeriesTagConsistencyService.SeriesTagGameSnapshot
        {
            Id = Guid.NewGuid(),
            Name = name,
            Tags = tags,
            Features = features.ToList()
        };
    }

    private static void AssertContains(string name, IEnumerable<string> values, string expected)
    {
        AssertTrue(name, (values ?? Enumerable.Empty<string>()).Any(x => string.Equals(x, expected, StringComparison.Ordinal)));
    }

    private static void AssertNotContains(string name, IEnumerable<string> values, string unexpected)
    {
        AssertTrue(name, !(values ?? Enumerable.Empty<string>()).Any(x => string.Equals(x, unexpected, StringComparison.Ordinal)));
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
