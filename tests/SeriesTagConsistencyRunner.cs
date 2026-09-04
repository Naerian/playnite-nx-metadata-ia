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

    private static void AssertTrue(string name, bool condition)
    {
        Console.WriteLine((condition ? "[PASS] " : "[FAIL] ") + name);
        if (!condition)
        {
            failures++;
        }
    }
}
