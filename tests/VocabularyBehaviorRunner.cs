using MetaDataIAPlugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        Test_StoreTerms_ReuseExactLibrarySpellingOnly();
        Test_StoreTerms_DoesNotSwapForASimilarLibraryName();
        Test_StoreTerms_KeepsStoreWordingWhenLibraryIsEmpty();
        Test_StoreTerms_EquivalentOnlyWhenNormalizedTextMatches();
        Test_SteamAppTags_ReadsLocalizedLabels();
        Test_SteamAppDetails_ReadsDataWhenResponseKeyDiffers();
        Test_SteamAgeGate_IsRecognized();
        Test_TermResolve_AppendKeepsSpecificGenres();
        Test_TermResolve_AppendRejectsDroppingUnrelated();
        Test_TermResolve_LocalizedOverwriteSkipsModel();
        Test_TermResolve_InvalidJsonFallsBack();
        Test_AiJson_OverEscapedQuotesAreRepaired();
        Test_AiJson_WrappedJsonStringIsUnwrapped();
        Test_RomTitle_MatchesStoreTitle();
        Test_ArticleAndPlatform_SeparateRomFromRemake();
        Test_Genres_AreOrganizedEvenWhenAlreadyLocalized();
        Test_MissingStoreLists_AskFromKnownFacts();
        Test_ModesAndLanguages();
        Test_Uppercase_UsesLanguageRules();
        Test_TagPrefix_PreservesBracketsAndCasing();

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

    private static void Test_StoreTerms_ReuseExactLibrarySpellingOnly()
    {
        var library = new[] { "Coop. A Pantalla (Com)Partida", "Logros de Steam", "Un jugador" };
        var proposed = new[]
        {
            "Coop. A Pantalla (Com)Partida",
            "Pantalla Partida/Compartida",
            "Cooperativo en línea",
            "Logros De",
            "Un jugador",
            "Multijugador"
        };

        var mapped = VocabularyTermNormalizer.NormalizeField(
            proposed, "features", "es", library, null, 12, false);

        AssertEqual(
            "same name reused, different wording kept",
            "Coop. A Pantalla (Com)Partida, Pantalla Partida/Compartida, Cooperativo en línea, Logros De, Un jugador, Multijugador",
            Join(mapped));
    }

    private static void Test_StoreTerms_DoesNotSwapForASimilarLibraryName()
    {
        var library = new[] { "Pantalla dividida", "Coop. A Pantalla (Com)Partida" };
        var mapped = VocabularyTermNormalizer.NormalizeField(
            new[] { "Pantalla Partida/Compartida" }, "features", "es", library, null, 12, false);

        AssertEqual("similar library name is not substituted", "Pantalla Partida/Compartida", Join(mapped));
    }

    private static void Test_StoreTerms_KeepsStoreWordingWhenLibraryIsEmpty()
    {
        var mapped = VocabularyTermNormalizer.NormalizeField(
            new[] { "Shared/Split Screen" }, "features", "en", new string[0], null, 12, false);

        AssertEqual("store wording kept", "Shared/Split Screen", Join(mapped));
    }

    private static void Test_StoreTerms_EquivalentOnlyWhenNormalizedTextMatches()
    {
        AssertTrue(
            "accent and case are the same term",
            VocabularyTermNormalizer.AreEquivalent("Logros de Steam", "logros de steam", "features", "es"));
        AssertTrue(
            "different phrases stay different",
            !VocabularyTermNormalizer.AreEquivalent("Coop. A Pantalla (Com)Partida", "Pantalla dividida", "features", "es"));
    }

    private static void Test_SteamAppTags_ReadsLocalizedLabels()
    {
        var html = "<a class=\"app_tag\" href=\"/tags/es/Acci%C3%B3n\">Acci&oacute;n</a>" +
                   "<a class=\"app_tag\" href=\"/tags/es/Plataformas\"> Plataformas </a>" +
                   "<a class=\"app_tag\">+</a>" +
                   "InitAppTagModal( 690640, [{\"tagid\":1,\"name\":\"Fantasía\"},{\"tagid\":2,\"name\":\"Cooperativos locales\"}], 1);";
        AssertEqual(
            "steam user tags",
            "Fantasía, Cooperativos locales",
            Join(OfficialStoreDataService.ParseSteamAppTags(html)));
        AssertEqual(
            "steam tags fall back to the tag links",
            "Acción",
            Join(OfficialStoreDataService.ParseSteamAppTags(
                "<a class=\"app_tag\" style=\"display: none;\">\nAcci&oacute;n\n</a><a class=\"app_tag\">+</a>")));
    }

    private static void Test_SteamAppDetails_ReadsDataWhenResponseKeyDiffers()
    {
        var wrapped = JObject.Parse("{\"1113990\":{\"success\":true,\"data\":{\"type\":\"game\",\"name\":\"Trine 4: The Nightmare Prince\",\"steam_appid\":690640,\"genres\":[{\"id\":1,\"description\":\"Acción\"}]}}}");
        var data = OfficialStoreDataService.ResolveSteamAppData(wrapped, "690640");
        AssertEqual("steam data when the response key is not the app id", "Trine 4: The Nightmare Prince", data == null ? string.Empty : (string)data["name"]);

        var direct = JObject.Parse("{\"620\":{\"success\":true,\"data\":{\"name\":\"Portal 2\",\"steam_appid\":620}}}");
        var portal = OfficialStoreDataService.ResolveSteamAppData(direct, "620");
        AssertEqual("steam data when the response key is the app id", "Portal 2", portal == null ? string.Empty : (string)portal["name"]);

        AssertTrue(
            "a different steam_appid is not accepted",
            OfficialStoreDataService.ResolveSteamAppData(wrapped, "999") == null);
        AssertTrue(
            "success false stays empty",
            OfficialStoreDataService.ResolveSteamAppData(JObject.Parse("{\"690640\":{\"success\":false}}"), "690640") == null);
    }

    private static void Test_SteamAgeGate_IsRecognized()
    {
        AssertTrue("age gate page", OfficialStoreDataService.IsSteamAgeGate("<div id=\"app_agegate\"></div>"));
        AssertTrue(
            "store page with tags is not an age gate",
            !OfficialStoreDataService.IsSteamAgeGate("InitAppTagModal(1583230, [{\"tagid\":1,\"name\":\"Comedia\"}], 1);"));
    }

    private static void Test_TermResolve_AppendKeepsSpecificGenres()
    {
        var field = new TermFieldRequest
        {
            Field = "genres",
            Mode = "append",
            Existing = new List<string> { "Acción y aventura" },
            Incoming = new List<string> { "Acción", "Aventura" },
            MaxItems = 8,
            AlreadyInLanguage = true
        };
        AssertTrue("append with both sides needs the model", field.NeedsModel);

        Dictionary<string, List<string>> resolved;
        var ok = TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Acción\",\"Aventura\"]}]}",
            new List<TermFieldRequest> { field },
            out resolved);
        AssertTrue("append response parses", ok);
        AssertEqual("broad genre dropped", "Acción, Aventura", Join(resolved["genres"]));
    }

    private static void Test_TermResolve_AppendRejectsDroppingUnrelated()
    {
        var field = new TermFieldRequest
        {
            Field = "tags",
            Mode = "append",
            Existing = new List<string> { "Fantasía" },
            Incoming = new List<string> { "Acción" },
            MaxItems = 8
        };
        Dictionary<string, List<string>> resolved;
        TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"tags\",\"terms\":[\"Acción\"]}]}",
            new List<TermFieldRequest> { field },
            out resolved);
        AssertEqual("unrelated existing tag stays", "Fantasía, Acción", Join(resolved["tags"]));
    }

    private static void Test_TermResolve_LocalizedOverwriteSkipsModel()
    {
        var field = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Existing = new List<string> { "Viejo" },
            Incoming = new List<string> { "Acción", "Aventura" },
            AlreadyInLanguage = true
        };
        AssertTrue("localized overwrite skips the model", !field.NeedsModel);
        AssertEqual("overwrite uses incoming", "Acción, Aventura", Join(field.DirectTerms()));

        var translate = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Incoming = new List<string> { "Action" },
            AlreadyInLanguage = false
        };
        AssertTrue("foreign overwrite asks the model", translate.NeedsModel);

        var empty = new TermFieldRequest
        {
            Field = "tags",
            Mode = "empty",
            Existing = new List<string> { "Retro" },
            Incoming = new List<string> { "Mythology" }
        };
        AssertTrue("filled empty-only skips the model", !empty.NeedsModel);
        AssertEqual("empty-only keeps current", "Retro", Join(empty.DirectTerms()));

        var emptyFill = new TermFieldRequest
        {
            Field = "genres",
            Mode = "empty",
            Existing = new List<string>(),
            Incoming = new List<string> { "Shooter", "Action" },
            AlreadyInLanguage = false,
            Organize = true
        };
        AssertTrue("empty-only with empty field and English store asks the model", emptyFill.NeedsModel);
        AssertEqual("empty-only fill uses incoming as direct fallback", "Shooter, Action", Join(emptyFill.DirectTerms()));
    }

    private static void Test_TermResolve_InvalidJsonFallsBack()
    {
        var field = new TermFieldRequest
        {
            Field = "features",
            Mode = "append",
            Existing = new List<string> { "Un jugador" },
            Incoming = new List<string> { "Cooperativo" },
            MaxItems = 8
        };
        Dictionary<string, List<string>> resolved;
        var ok = TermFieldResolver.TryApplyResponse("not json", new List<TermFieldRequest> { field }, out resolved);
        AssertTrue("invalid json is a fallback", !ok);
        AssertEqual("fallback keeps both", "Un jugador, Cooperativo", Join(resolved["features"]));
    }

    private static void Test_AiJson_OverEscapedQuotesAreRepaired()
    {
        var field = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Existing = new List<string>(),
            Incoming = new List<string> { "Adventure", "Cooperative" },
            MaxItems = 8,
            Organize = true
        };
        // Models sometimes return object literals with escaped quotes:
        // { \"genres\": [\"Adventure\"] }
        var content =
            "{\n  \\\"fields\\\": [\n    {\n      \\\"field\\\": \\\"genres\\\",\n      \\\"terms\\\": [\\\"Adventure\\\", \\\"Cooperative\\\"]\n    }\n  ]\n}";
        Dictionary<string, List<string>> resolved;
        var ok = TermFieldResolver.TryApplyResponse(content, new List<TermFieldRequest> { field }, out resolved);
        AssertTrue("over-escaped JSON is repaired", ok);
        AssertEqual("over-escaped genres parse", "Adventure, Cooperative", Join(resolved["genres"]));
    }

    private static void Test_AiJson_WrappedJsonStringIsUnwrapped()
    {
        var field = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Existing = new List<string>(),
            Incoming = new List<string> { "Puzzle", "Adventure" },
            MaxItems = 8,
            Organize = true
        };
        var inner = "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Puzzle\",\"Adventure\"]}]}";
        var content = JsonConvert.SerializeObject(inner);
        Dictionary<string, List<string>> resolved;
        var ok = TermFieldResolver.TryApplyResponse(content, new List<TermFieldRequest> { field }, out resolved);
        AssertTrue("JSON string wrapper is unwrapped", ok);
        AssertEqual("wrapped genres parse", "Puzzle, Adventure", Join(resolved["genres"]));
    }

    private static void Test_RomTitle_MatchesStoreTitle()
    {
        AssertTrue(
            "region tag matches the store title",
            TitleMatchingService.IsReliableMatch("Battle of Olympus (USA)", "Battle of Olympus"));
        AssertEqual(
            "search title drops the region tag",
            "Battle of Olympus",
            TitleMatchingService.SearchTitle("Battle of Olympus (USA)"));
        AssertTrue(
            "a subtitle is not treated as a region tag",
            !TitleMatchingService.IsReliableMatch("Trine 4: The Nightmare Prince", "Trine 4"));
    }

    private static void Test_ArticleAndPlatform_SeparateRomFromRemake()
    {
        AssertTrue(
            "leading article matches the store title",
            TitleMatchingService.IsReliableMatch("Battle of Olympus (USA)", "The Battle of Olympus"));
        AssertTrue(
            "trailing article matches the store title",
            TitleMatchingService.IsReliableMatch("Battle of Olympus, The (USA)", "The Battle Of Olympus"));
        AssertTrue(
            "nes labels fit the cartridge",
            TitleMatchingService.PlatformLabelsFit(
                new[] { "Nintendo Entertainment System" },
                new[] { "nintendo_nes" },
                new[] { "Nintendo Entertainment System" }));
        AssertTrue(
            "windows labels do not fit the cartridge",
            !TitleMatchingService.PlatformLabelsFit(
                new[] { "Nintendo Entertainment System" },
                new[] { "nintendo_nes" },
                new[] { "PC (Microsoft Windows)" }));
        AssertTrue(
            "windows labels fit the steam release",
            TitleMatchingService.PlatformLabelsFit(
                new[] { "PC (Windows)" },
                new[] { "pc_windows" },
                new[] { "PC (Microsoft Windows)" }));
    }

    private static void Test_Genres_AreOrganizedEvenWhenAlreadyLocalized()
    {
        var genres = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Incoming = new List<string> { "Action", "Adventure" },
            AlreadyInLanguage = true,
            Organize = true
        };
        AssertTrue("localized genres still go to the model", genres.NeedsModel);
        Dictionary<string, List<string>> organized;
        TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"genres\",\"terms\":[]}]}",
            new List<TermFieldRequest> { genres },
            out organized);
        AssertEqual("an empty organize response keeps the store list", "Action, Adventure", Join(organized["genres"]));

        Dictionary<string, List<string>> translated;
        var translate = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Incoming = new List<string> { "Shooter", "Action", "Adventure" },
            AlreadyInLanguage = false,
            Organize = true
        };
        AssertTrue(
            "english store/IGDB genres ask the model",
            translate.NeedsModel);
        AssertTrue(
            "translated spanish genres are accepted",
            TermFieldResolver.TryApplyResponse(
                "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Disparos\",\"Acción\",\"Aventura\"]}]}",
                new List<TermFieldRequest> { translate },
                out translated));
        AssertEqual("shooter becomes disparos", "Disparos, Acción, Aventura", Join(translated["genres"]));

        var features = new TermFieldRequest
        {
            Field = "features",
            Mode = "overwrite",
            Incoming = new List<string> { "Single-player" },
            AlreadyInLanguage = true
        };
        AssertTrue("localized features stay on the store list", !features.NeedsModel);

        var englishFeatures = new TermFieldRequest
        {
            Field = "features",
            Mode = "overwrite",
            Incoming = new List<string> { "Single-player", "Multiplayer" },
            AlreadyInLanguage = false
        };
        AssertTrue("english features ask the model to translate", englishFeatures.NeedsModel);

        var append = new TermFieldRequest
        {
            Field = "tags",
            Mode = "append",
            Existing = new List<string> { "Fantasy" },
            Incoming = new List<string> { "Mythology" },
            AlreadyInLanguage = true,
            Organize = true
        };
        AssertTrue("append tags go to the model", append.NeedsModel);
        var json = TermFieldResolver.BuildUserJson("en", new[] { "Nintendo Entertainment System nintendo_nes" }, new List<TermFieldRequest> { append });
        AssertTrue("the request names the platform", json.Contains("nintendo_nes"));
        AssertTrue("append sends both lists", json.Contains("Fantasy") && json.Contains("Mythology"));
    }

    private static void Test_MissingStoreLists_AskFromKnownFacts()
    {
        var genres = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Incoming = new List<string>(),
            MaxItems = 4,
            Organize = true,
            FromKnowledge = true
        };
        AssertTrue("missing genres ask the model", genres.NeedsModel);

        var features = new TermFieldRequest
        {
            Field = "features",
            Mode = "overwrite",
            Incoming = new List<string>(),
            MaxItems = 8,
            FromKnowledge = true
        };
        AssertTrue("missing features ask the model", features.NeedsModel);
        Dictionary<string, List<string>> featureTerms;
        TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"features\",\"terms\":[\"Single-player\"]}]}",
            new List<TermFieldRequest> { features },
            out featureTerms);
        AssertEqual("a feature from the model is kept", "Single-player", Join(featureTerms["features"]));

        var filled = new TermFieldRequest
        {
            Field = "tags",
            Mode = "empty",
            Existing = new List<string> { "Comedy" },
            Incoming = new List<string>(),
            FromKnowledge = true
        };
        AssertTrue("filled empty-only does not ask from memory", !filled.NeedsModel);

        Dictionary<string, List<string>> resolved;
        TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Action\",\"Adventure\"]}]}",
            new List<TermFieldRequest> { genres },
            out resolved);
        AssertEqual("labels from the model are kept", "Action, Adventure", Join(resolved["genres"]));

        var append = new TermFieldRequest
        {
            Field = "tags",
            Mode = "append",
            Existing = new List<string> { "Comedy" },
            Incoming = new List<string>(),
            MaxItems = 8,
            FromKnowledge = true
        };
        TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"tags\",\"terms\":[]}]}",
            new List<TermFieldRequest> { append },
            out resolved);
        AssertEqual("an empty guess keeps the current tags", "Comedy", Join(resolved["tags"]));

        var facts = new JObject();
        facts["title"] = "High On Life";
        facts["platform"] = new JArray("PC (Windows) pc_windows");
        var json = TermFieldResolver.BuildKnowledgeJson("es", facts, new List<TermFieldRequest> { genres });
        AssertTrue("the guess names the title", json.Contains("High On Life"));
        AssertTrue("the guess names the platform", json.Contains("pc_windows"));
    }

    private static void Test_ModesAndLanguages()
    {
        var languages = new[] { "es", "en", "fr", "de", "it", "pt", "pt-BR", "ja", "tr", "pl", "nl", "ru" };
        var steam = new Dictionary<string, string>
        {
            { "es", "spanish" },
            { "en", "english" },
            { "fr", "french" },
            { "de", "german" },
            { "it", "italian" },
            { "pt", "portuguese" },
            { "pt-BR", "brazilian" },
            { "ja", "japanese" },
            { "tr", "turkish" },
            { "pl", "polish" },
            { "nl", "dutch" },
            { "ru", "russian" }
        };

        foreach (var language in languages)
        {
            AssertEqual(language + " steam language", steam[language], OfficialStoreDataService.ToSteamStoreLanguage(language));

            var overwrite = new TermFieldRequest
            {
                Field = "features",
                Mode = "overwrite",
                Existing = new List<string> { "Co-Operative", "Cooperativo" },
                Incoming = new List<string> { "Un jugador", "Cooperativo" },
                AlreadyInLanguage = true,
                MaxItems = 8
            };
            AssertTrue(language + " overwrite skips the model", !overwrite.NeedsModel);
            AssertEqual(language + " overwrite drops what was already there", "Un jugador, Cooperativo", Join(overwrite.DirectTerms()));

            var empty = new TermFieldRequest
            {
                Field = "genres",
                Mode = "empty",
                Existing = new List<string> { "Aventura" },
                Incoming = new List<string> { "Action" },
                AlreadyInLanguage = true,
                MaxItems = 8
            };
            AssertTrue(language + " empty-only skips the model", !empty.NeedsModel);
            AssertEqual(language + " empty-only keeps the current value", "Aventura", Join(empty.DirectTerms()));

            var append = new TermFieldRequest
            {
                Field = "tags",
                Mode = "append",
                Existing = new List<string> { "Fantasía" },
                Incoming = new List<string> { "Mitología" },
                AlreadyInLanguage = true,
                MaxItems = 8
            };
            AssertTrue(language + " append asks the model", append.NeedsModel);

            var foreign = new TermFieldRequest
            {
                Field = "genres",
                Mode = "overwrite",
                Incoming = new List<string> { "Action" },
                AlreadyInLanguage = false,
                MaxItems = 8
            };
            AssertTrue(language + " foreign overwrite asks the model", foreign.NeedsModel);
        }
    }

    private static void Test_Uppercase_UsesLanguageRules()
    {
        AssertEqual("es uppercase keeps accents", "ACCIÓN", TextCapitalization.ToUpper("Acción", "es"));
        AssertEqual("fr uppercase", "COOPÉRATIF", TextCapitalization.ToUpper("coopératif", "fr"));
        AssertEqual(
            "de uppercase follows de-DE",
            "Straße".ToUpper(System.Globalization.CultureInfo.GetCultureInfo("de-DE")),
            TextCapitalization.ToUpper("Straße", "de"));
        AssertEqual("tr uppercase uses turkish i", "İSTANBUL", TextCapitalization.ToUpper("istanbul", "tr"));
        AssertEqual("es sentence case is not title case", "Un jugador", TextCapitalization.Apply("UN JUGADOR", "es", false));
        AssertEqual("en capitalizes each word", "Full Controller Support", TextCapitalization.Apply("FULL CONTROLLER SUPPORT", "en", false));
        AssertEqual("en-GB uses the same word capitals", "Action And Adventure", TextCapitalization.Apply("action and adventure", "en-GB", false));
        AssertEqual("es keeps a single capital", "Acción y aventura", TextCapitalization.ToSentence("Acción Y Aventura", "es"));
        AssertEqual("fr sentence case", "Coopératif en ligne", TextCapitalization.ToSentence("COOPÉRATIF EN LIGNE", "fr"));
        AssertEqual("second sentence starts with a capital", "Empieza aquí. Sigue allá.", TextCapitalization.ToSentence("EMPIEZA AQUÍ. SIGUE ALLÁ.", "es"));
    }

    private static void Test_TagPrefix_PreservesBracketsAndCasing()
    {
        AssertEqual(
            "clean keeps bracket prefix",
            "[MAI] Open world",
            VocabularyTermNormalizer.CleanTerm("[MAI] Open world"));
        AssertEqual(
            "clean keeps doubled open brackets in prefix text",
            "[[MAI] Open world",
            VocabularyTermNormalizer.CleanTerm("[[MAI] Open world"));
        AssertEqual(
            "clean unwraps a fully bracketed term",
            "Action",
            VocabularyTermNormalizer.CleanTerm("[Action]"));
        AssertEqual(
            "clean unwraps quoted term",
            "Indie",
            VocabularyTermNormalizer.CleanTerm("\"Indie\""));

        AssertEqual(
            "title case leaves [MAI] alone",
            "[MAI] Open World",
            TextCapitalization.Apply("[MAI] open world", "en", false));
        AssertEqual(
            "uppercase option only uppercases the body",
            "[MAI] OPEN WORLD",
            TextCapitalization.Apply("[MAI] open world", "en", true));
        AssertEqual(
            "sentence case leaves [MAI] alone",
            "[MAI] Mundo abierto",
            TextCapitalization.Apply("[MAI] Mundo Abierto", "es", false));

        var settings = CreateSettings("en");
        settings.TagPrefix = "[MAI] ";
        settings.CategoryPrefix = "[META] ";
        var result = CreateResult(new[] { "Action" }, new[] { "extraction shooter", "sci-fi" });
        result.Categories = new List<string> { "co-op" };
        result.Normalize(settings, new Game { Name = "ARC Raiders" });
        result.Tags = VocabularyTermNormalizer.NormalizeField(
            result.Tags, "tags", "en", new string[0], null, 20, false);
        result.Categories = VocabularyTermNormalizer.NormalizeField(
            result.Categories, "categories", "en", new string[0], null, 12, false);
        result.Tags = TextCapitalization.ApplyList(result.Tags, "en", false);
        result.Categories = TextCapitalization.ApplyList(result.Categories, "en", false);
        result.ApplyConfiguredPrefixes(settings);

        AssertEqual(
            "prefix applied after casing",
            "[MAI] Extraction Shooter, [MAI] Sci-Fi",
            Join(result.Tags));
        AssertEqual(
            "category prefix applied after casing",
            "[META] Co-Op",
            Join(result.Categories));

        var append = CreateResult(new[] { "Action" }, new[] { "Fantasy", "Extraction Shooter", "Sci-Fi" });
        append.Tags = TextCapitalization.ApplyList(append.Tags, "en", false);
        append.ApplyConfiguredPrefixes(settings, new[] { "Fantasy", "Local Co-op" });
        AssertEqual(
            "prefix skips tags that already existed on the game",
            "Fantasy, [MAI] Extraction Shooter, [MAI] Sci-Fi",
            Join(append.Tags));
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

    private static void AssertTrue(string name, bool condition)
    {
        if (condition)
        {
            Console.WriteLine("[PASS] " + name);
            return;
        }

        failures++;
        Console.WriteLine("[FAIL] " + name);
        Console.WriteLine("       expected: true");
        Console.WriteLine("       actual:   false");
    }
}
