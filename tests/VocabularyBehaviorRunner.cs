using MetaDataIAPlugin;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Offline regression checks for metadata vocabulary behavior (no AI calls).
/// Compile and run via tests/run-vocabulary-behavior.ps1.
/// </summary>
internal static class VocabularyBehaviorRunner
{
    private static int failures;

    private static void Main()
    {
        Console.WriteLine("Metadata AI vocabulary behavior tests");
        Console.WriteLine("====================================");

        Test_NoLanguageRemap_EnglishOutputKeepsEnglish();
        Test_NoLanguageRemap_SpanishOutputKeepsSpanish();
        Test_NoLanguageRemap_MixedListNotTranslated();
        Test_PreferExisting_KeepsLibrarySpelling();
        Test_PreferExisting_DropsUnknownSpanishWhenLibraryIsEnglish();
        Test_PreferExisting_NormalizedAccentMatch();
        Test_PreferExisting_DoesNotMapAcrossLanguages();

        if (failures == 0)
        {
            Console.WriteLine();
            Console.WriteLine("ALL PASSED");
            Environment.Exit(0);
        }

        Console.WriteLine();
        Console.WriteLine("FAILED: " + failures + " assertion(s)");
        Environment.Exit(1);
    }

    private static void Test_NoLanguageRemap_EnglishOutputKeepsEnglish()
    {
        var settings = CreateSettings("en");
        var result = CreateResult(
            genres: new[] { "Action", "Adventure", "Racing" },
            tags: new[] { "Multiplayer", "Single-player", "Post-apocalyptic" });

        result.Normalize(settings, new Game { Name = "Test Game" });

        AssertEqual("en genres stay English", "Action, Adventure, Racing", Join(result.Genres));
        AssertEqual("en tags stay English", "Multiplayer, Single-player, Post-apocalyptic", Join(result.Tags));
    }

    private static void Test_NoLanguageRemap_SpanishOutputKeepsSpanish()
    {
        var settings = CreateSettings("es");
        var result = CreateResult(
            genres: new[] { "Accion", "Aventura", "Carreras" },
            tags: new[] { "Multijugador", "Un jugador" });

        result.Normalize(settings, new Game { Name = "Juego de prueba" });

        AssertEqual("es genres stay Spanish", "Accion, Aventura, Carreras", Join(result.Genres));
        AssertEqual("es tags stay Spanish", "Multijugador, Un jugador", Join(result.Tags));
    }

    private static void Test_NoLanguageRemap_MixedListNotTranslated()
    {
        // Plugin must not rewrite Action→Accion (or the reverse) after generation.
        var settings = CreateSettings("en");
        var result = CreateResult(
            genres: new[] { "Action", "Accion" },
            tags: new[] { "Multiplayer", "Multijugador" });

        result.Normalize(settings, new Game { Name = "Mixed" });

        AssertEqual("no Action→Accion remap", "Action, Accion", Join(result.Genres));
        AssertEqual("no Multiplayer→Multijugador remap", "Multiplayer, Multijugador", Join(result.Tags));
    }

    private static void Test_PreferExisting_KeepsLibrarySpelling()
    {
        var library = new[] { "Action", "Indie", "Racing", "Adventure" };
        var proposed = new[] { "action", "INDIE", "Racing" };
        var mapped = LibraryNameMatching.MapToExisting(proposed, library);

        AssertEqual("exact/case library spelling", "Action, Indie, Racing", Join(mapped));
    }

    private static void Test_PreferExisting_DropsUnknownSpanishWhenLibraryIsEnglish()
    {
        var library = new[] { "Action", "Indie", "Racing", "Adventure" };
        var proposed = new[] { "Accion", "Aventura", "Indie", "Carreras", "Multijugador" };
        var mapped = LibraryNameMatching.MapToExisting(proposed, library);

        AssertEqual("Spanish unknowns dropped; Indie kept", "Indie", Join(mapped));
    }

    private static void Test_PreferExisting_NormalizedAccentMatch()
    {
        var library = new[] { "Acción", "Simulación" };
        var proposed = new[] { "Accion", "Simulacion" };
        var mapped = LibraryNameMatching.MapToExisting(proposed, library);

        AssertEqual("accent-normalized reuse", "Acción, Simulación", Join(mapped));
    }

    private static void Test_PreferExisting_DoesNotMapAcrossLanguages()
    {
        var library = new[] { "Action", "Adventure" };
        var proposed = new[] { "Accion", "Aventura" };
        var mapped = LibraryNameMatching.MapToExisting(proposed, library);

        AssertEqual("no EN↔ES synonym mapping", string.Empty, Join(mapped));
    }

    private static MetaDataIASettings CreateSettings(string language)
    {
        return new MetaDataIASettings
        {
            Language = language,
            MaxGenres = 12,
            MaxTags = 20,
            MaxFeatures = 12,
            MaxCategories = 12,
            MaxAgeRatings = 4,
            MaxRegions = 4,
            MaxDevelopers = 2,
            MaxPublishers = 2,
            MaxSeries = 2,
            MaxLinks = 5,
            GenerateFeatures = true,
            GenerateGenres = true,
            GenerateTags = true
        };
    }

    private static AiMetadataResult CreateResult(string[] genres, string[] tags)
    {
        return new AiMetadataResult
        {
            Genres = genres.ToList(),
            Tags = tags.ToList(),
            Features = new List<string>(),
            Categories = new List<string>(),
            Developers = new List<string>(),
            Publishers = new List<string>(),
            AgeRatings = new List<string>(),
            Regions = new List<string>(),
            Series = new List<string>(),
            Links = new List<AiMetadataLink>(),
            SimilarGamesList = new List<string>()
        };
    }

    private static string Join(IEnumerable<string> values)
    {
        return string.Join(", ", (values ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static void AssertEqual(string name, string expected, string actual)
    {
        if (string.Equals(expected ?? string.Empty, actual ?? string.Empty, StringComparison.Ordinal))
        {
            Console.WriteLine("[PASS] " + name);
            return;
        }

        failures++;
        Console.WriteLine("[FAIL] " + name);
        Console.WriteLine("       expected: " + expected);
        Console.WriteLine("       actual:   " + actual);
    }
}
