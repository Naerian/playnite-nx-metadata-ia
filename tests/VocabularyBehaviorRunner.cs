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
        Test_PcGamingWiki_ParsesInfoboxTaxonomy();
        Test_PcGamingWiki_PageTitleFromWikiUrl();
        Test_TermResolve_AppendKeepsSpecificGenres();
        Test_TermResolve_AppendRejectsDroppingUnrelated();
        Test_TermResolve_LocalizedOverwriteSkipsModel();
        Test_TermResolve_InvalidJsonFallsBack();
        Test_TermResolve_AppendMergesExistingWhenModelOmitsThem();
        Test_AiJson_OverEscapedQuotesAreRepaired();
        Test_AiJson_WrappedJsonStringIsUnwrapped();
        Test_AiJson_CollapsedFieldsAndCommaTermsAreRepaired();
        Test_TermResolve_AcceptsTranslatedSpanishGenres();
        Test_RomTitle_MatchesStoreTitle();
        Test_ArticleAndPlatform_SeparateRomFromRemake();
        Test_Genres_AreOrganizedEvenWhenAlreadyLocalized();
        Test_MissingStoreLists_AskFromKnownFacts();
        Test_LocalTermFallback_ModeAndEvidence();
        Test_TermBlacklist_AppliesAfterOrganizeAssign();
        Test_Blacklist_ExactQuotedVsSubstring();
        Test_KeptLoanwords_ProtectsIndieFromStrip();
        Test_ModesAndLanguages();
        Test_Uppercase_UsesLanguageRules();
        Test_TagPrefix_PreservesBracketsAndCasing();
        Test_StoreTermSanitizer_NoiseAndBrands();
        Test_BatchTermSessionCache_PrefersFirstStoreSpelling();
        Test_ScopedTermVocabulary_KeyMatchesOnly_PlayniteWins();

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

    private static void Test_PcGamingWiki_ParsesInfoboxTaxonomy()
    {
        var wiki =
            "{{Infobox game\n" +
            "{{Infobox game/row/developer|Embark Studios}}\n" +
            "{{Infobox game/row/publisher|Embark Studios}}\n" +
            "{{Infobox game/row/date|Windows|October 30, 2025}}\n" +
            "{{Infobox game/row/taxonomy/modes             | Multiplayer }}\n" +
            "{{Infobox game/row/taxonomy/perspectives      | Third-person }}\n" +
            "{{Infobox game/row/taxonomy/genres            | Action, Open world, Shooter, Survival, TPS }}\n" +
            "}}";
        var meta = PcGamingWikiMetadataService.BuildMetadata("ARC_Raiders", wiki);
        AssertEqual("pcgw genres", "Action, Open world, Shooter, Survival, TPS", Join(meta.Genres));
        AssertEqual("pcgw perspective tags", "Third-person", Join(meta.Tags));
        AssertEqual("pcgw modes features", "Multiplayer", Join(meta.Features));
        AssertEqual("pcgw developer", "Embark Studios", Join(meta.Developers));
        AssertEqual("pcgw release", "October 30, 2025", meta.ReleaseDate);
        AssertEqual("pcgw source", MetaDataIASettings.SourcePcGamingWiki, meta.SourceName);
    }

    private static void Test_PcGamingWiki_PageTitleFromWikiUrl()
    {
        AssertEqual(
            "pcgw steam redirect title",
            "ARC_Raiders",
            PcGamingWikiMetadataService.PageTitleFromWikiUrl("https://www.pcgamingwiki.com/wiki/ARC_Raiders"));
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
        AssertTrue("invalid json uses English store fallback", ok);
        AssertEqual("fallback keeps both", "Un jugador, Cooperativo", Join(resolved["features"]));
    }

    private static void Test_TermResolve_AppendMergesExistingWhenModelOmitsThem()
    {
        var field = new TermFieldRequest
        {
            Field = "tags",
            Mode = "append",
            Language = "en",
            Existing = new List<string> { "[EMT] Video Micro missing", "[HLTB] 20 to 30 hours" },
            Incoming = new List<string> { "Action", "Comedy", "jungle", "boss fight" },
            MaxItems = 20,
            Organize = true
        };

        // Gemma-style: returns only incoming tags under the wrong field name, omits existing.
        Dictionary<string, List<string>> wrongField;
        var wrongOk = TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Action\",\"Comedy\",\"jungle\",\"boss fight\"]}]}",
            new List<TermFieldRequest> { field },
            out wrongField);
        AssertTrue("single-field wrong name is remapped", wrongOk);
        AssertTrue("existing EMT tag kept", wrongField["tags"].Any(t => t.IndexOf("EMT", StringComparison.OrdinalIgnoreCase) >= 0));
        AssertTrue("incoming jungle kept", wrongField["tags"].Any(t => string.Equals(t, "jungle", StringComparison.OrdinalIgnoreCase)));

        // Model omits existing user tags ([EMT]/[HLTB]). Accept fails KeepsExisting, but
        // English FailedOrganizeTerms must still apply existing + incoming instead of erroring.
        Dictionary<string, List<string>> omittedExisting;
        var omitOk = TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"tags\",\"terms\":[\"Action\",\"Comedy\",\"jungle\",\"boss fight\"]}]}",
            new List<TermFieldRequest> { field },
            out omittedExisting);
        AssertTrue("append that omits existing falls back to existing+incoming", omitOk);
        AssertTrue(
            "HLTB existing kept via English fallback",
            omittedExisting["tags"].Any(t => t.IndexOf("HLTB", StringComparison.OrdinalIgnoreCase) >= 0));
        AssertTrue(
            "incoming jungle kept via English fallback",
            omittedExisting["tags"].Any(t => string.Equals(t, "jungle", StringComparison.OrdinalIgnoreCase)));
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

        AssertTrue(
            "plain prose is not parseable JSON",
            !TermFieldResolver.ResponseIsParseableJson("Sure, here are the genres: Action, Adventure"));
        AssertTrue(
            "valid organize JSON is parseable",
            TermFieldResolver.ResponseIsParseableJson(
                "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Acción\",\"Aventura\"]}]}"));
    }

    private static void Test_AiJson_CollapsedFieldsAndCommaTermsAreRepaired()
    {
        // Discord DK64-style: bare "json" label, duplicate field keys in one object,
        // and comma-joined terms blobs instead of separate strings.
        var genres = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Language = "en",
            Existing = new List<string>(),
            Incoming = new List<string> { "Action", "Platform", "Adventure" },
            MaxItems = 8,
            Organize = true
        };
        var tags = new TermFieldRequest
        {
            Field = "tags",
            Mode = "overwrite",
            Language = "en",
            Existing = new List<string>(),
            Incoming = new List<string>
            {
                "Action", "Comedy", "gravity", "photography", "minigames", "guitar playing",
                "death", "fairy", "multiple protagonists", "multiple endings", "artificial intelligence",
                "jungle", "camera", "level selection", "giant insects", "high score",
                "day/night cycle", "hip-hop", "boss fight", "death match"
            },
            MaxItems = 20,
            Organize = true
        };

        var content =
            "json\n{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Action, Comedy\"],\"field\":\"tags\",\"terms\":[\"Action, Comedy, gravity, photography, minigames, guitar playing, death, fairy, multiple protagonists, multiple endings, artificial intelligence, jungle, camera, level selection, giant insects, high score, day/night cycle, hip-hop, boss fight, death match\"]}]}\n";

        AssertTrue(
            "DK64-style collapsed organize JSON is parseable after repair",
            TermFieldResolver.ResponseIsParseableJson(content));

        Dictionary<string, List<string>> resolved;
        var ok = TermFieldResolver.TryApplyResponse(
            content,
            new List<TermFieldRequest> { genres, tags },
            out resolved);
        AssertTrue("collapsed fields + comma terms are accepted", ok);
        AssertEqual("genres recovered from collapsed object", "Action, Comedy", Join(resolved["genres"]));
        AssertTrue("tags expanded from comma blob", resolved["tags"].Count >= 10);
        AssertTrue("tags include jungle", resolved["tags"].Any(t => string.Equals(t, "jungle", StringComparison.OrdinalIgnoreCase)));
        AssertTrue("tags include boss fight", resolved["tags"].Any(t => string.Equals(t, "boss fight", StringComparison.OrdinalIgnoreCase)));

        var stringTerms = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Language = "en",
            Incoming = new List<string> { "Action", "Adventure" },
            MaxItems = 8,
            Organize = true
        };
        Dictionary<string, List<string>> fromString;
        var stringOk = TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"genres\",\"terms\":\"Action, Adventure\"}]}",
            new List<TermFieldRequest> { stringTerms },
            out fromString);
        AssertTrue("string terms value is expanded", stringOk);
        AssertEqual("string terms become separate labels", "Action, Adventure", Join(fromString["genres"]));
    }

    private static void Test_TermResolve_AcceptsTranslatedSpanishGenres()
    {
        var field = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Language = "es",
            Existing = new List<string>(),
            Incoming = new List<string> { "Acción", "Aventura", "Shooter", "Puzzle", "Strategy", "Adventure" },
            LocalizedIncoming = new List<string> { "Acción", "Aventura" },
            MaxItems = 4,
            Organize = true
        };

        Dictionary<string, List<string>> clean;
        var cleanOk = TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Acción\",\"Aventura\",\"Disparos\",\"Puzle\",\"Estrategia\"]}]}",
            new List<TermFieldRequest> { field },
            out clean);
        AssertTrue("translated Gemini-style response is accepted", cleanOk);
        AssertEqual("max items keeps first four", "Acción, Aventura, Disparos, Puzle", Join(clean["genres"]));

        Dictionary<string, List<string>> flat;
        var flatOk = TermFieldResolver.TryApplyResponse(
            "{\"genres\":[\"Acción\",\"Disparos\",\"Puzle\",\"Estrategia\"]}",
            new List<TermFieldRequest> { field },
            out flat);
        AssertTrue("flat main-call JSON shape is accepted", flatOk);
        AssertEqual("flat genres kept", "Acción, Disparos, Puzle, Estrategia", Join(flat["genres"]));

        Dictionary<string, List<string>> mixed;
        var mixedOk = TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Acción\",\"Shooter\",\"Disparos\",\"Indie\"]}]}",
            new List<TermFieldRequest> { field },
            out mixed);
        AssertTrue("mixed response keeps translated terms", mixedOk);
        // Shooter is in non-localized Incoming and was copied unchanged → stripped.
        // Indie was not in Incoming here, so the safety strip does not touch it.
        AssertEqual("raw non-localized Incoming leftovers are stripped", "Acción, Disparos, Indie", Join(mixed["genres"]));
        AssertTrue(
            "mixed English leftovers need a translation retry",
            TermFieldResolver.ResponseNeedsTranslationRetry(
                "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Acción\",\"Shooter\"]}]}",
                new List<TermFieldRequest> { field }));

        var loanwordField = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Language = "es",
            Incoming = new List<string> { "Acción", "Indie", "Shooter" },
            LocalizedIncoming = new List<string> { "Acción", "Indie" },
            MaxItems = 4,
            Organize = true
        };
        Dictionary<string, List<string>> loanwords;
        TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Acción\",\"Indie\",\"Disparos\"]}]}",
            new List<TermFieldRequest> { loanwordField },
            out loanwords);
        AssertEqual("loanword already in localized incoming is kept", "Acción, Indie, Disparos", Join(loanwords["genres"]));

        Dictionary<string, List<string>> fallback;
        TermFieldResolver.TryApplyResponse(
            "not-json",
            new List<TermFieldRequest> { field },
            out fallback);
        AssertEqual(
            "invalid JSON leaves non-English genres empty (no localized false-success)",
            string.Empty,
            Join(fallback["genres"]));

        var onlyEnglishIncoming = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Language = "es",
            Incoming = new List<string> { "Action", "Adventure", "Indie" },
            LocalizedIncoming = new List<string>(),
            MaxItems = 4,
            Organize = true
        };
        AssertEqual(
            "es with no localized store data does not fall back to English Incoming",
            string.Empty,
            Join(onlyEnglishIncoming.FallbackTerms()));
        Dictionary<string, List<string>> onlyEnFallback;
        TermFieldResolver.TryApplyResponse(
            "not-json",
            new List<TermFieldRequest> { onlyEnglishIncoming },
            out onlyEnFallback);
        AssertEqual(
            "failed organize without usable model leaves genres empty (no localized false-success)",
            string.Empty,
            Join(onlyEnFallback["genres"]));
        AssertEqual(
            "FailedOrganizeTerms is empty for non-English",
            string.Empty,
            Join(onlyEnglishIncoming.FailedOrganizeTerms()));

        var steamEnglishLeftover = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Language = "es",
            Incoming = new List<string> { "Acción", "Aventura", "Shooter", "Indie" },
            LocalizedIncoming = new List<string> { "Acción", "Aventura", "Shooter", "Indie" },
            NonLocalizedIncoming = new List<string> { "Shooter", "Adventure" },
            MaxItems = 4,
            Organize = true
        };
        // DirectTerms may still prefer LocalizedIncoming when the model is not needed.
        // Failed organizes must not apply that list (incomplete vs IGDB/other sources).
        AssertEqual(
            "localized steam list remains available for direct apply",
            "Acción, Aventura, Shooter, Indie",
            Join(steamEnglishLeftover.FallbackTerms()));
        AssertEqual(
            "failed organize does not apply localized-only Steam list",
            string.Empty,
            Join(steamEnglishLeftover.FailedOrganizeTerms()));
        Dictionary<string, List<string>> steamFailApply;
        TermFieldResolver.TryApplyResponse(
            "not-json",
            new List<TermFieldRequest> { steamEnglishLeftover },
            out steamFailApply);
        AssertEqual(
            "unparseable organize response leaves non-English genres empty",
            string.Empty,
            Join(steamFailApply["genres"]));

        var igdbOnlyEnglish = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Language = "es",
            Incoming = new List<string> { "Acción", "Aventura", "Shooter", "Indie" },
            LocalizedIncoming = new List<string> { "Acción", "Aventura", "Indie" },
            NonLocalizedIncoming = new List<string> { "Shooter" },
            MaxItems = 4,
            Organize = true
        };
        Dictionary<string, List<string>> mixedIgdb;
        TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Acción\",\"Shooter\",\"Indie\",\"Disparos\"]}]}",
            new List<TermFieldRequest> { igdbOnlyEnglish },
            out mixedIgdb);
        AssertEqual(
            "unchanged non-localized Incoming leftover is stripped",
            "Acción, Indie, Disparos",
            Join(mixedIgdb["genres"]));
        AssertTrue(
            "non-localized Incoming leftover needs a translation retry",
            TermFieldResolver.ResponseNeedsTranslationRetry(
                "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Acción\",\"Shooter\"]}]}",
                new List<TermFieldRequest> { igdbOnlyEnglish }));

        var knowledgeField = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Language = "es",
            Incoming = new List<string>(),
            LocalizedIncoming = new List<string>(),
            MaxItems = 4,
            Organize = true,
            FromKnowledge = true
        };
        AssertTrue(
            "knowledge answers always need a localization retry when language is not English",
            TermFieldResolver.ResponseNeedsTranslationRetry(
                "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Shooter\"]}]}",
                new List<TermFieldRequest> { knowledgeField }));
        AssertTrue(
            "knowledge Spanish answers still get one localization confirmation retry",
            TermFieldResolver.ResponseNeedsTranslationRetry(
                "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Disparos\",\"Acción\"]}]}",
                new List<TermFieldRequest> { knowledgeField }));
    }

    private static void Test_RomTitle_MatchesStoreTitle()
    {
        AssertTrue(
            "region tag matches the store title",
            TitleMatchingService.IsReliableMatch("Battle of Olympus (USA)", "Battle of Olympus"));
        AssertEqual(
            "search title keeps the region tag",
            "Battle of Olympus (USA)",
            TitleMatchingService.SearchTitle("Battle of Olympus (USA)"));
        AssertEqual(
            "remastered stays in the search title",
            "Tomb Raider IV-VI Remastered",
            TitleMatchingService.SearchTitle("Tomb Raider IV-VI Remastered"));
        AssertTrue(
            "IV-VI matches IGDB IV•V•VI plus a year",
            TitleMatchingService.IsSameReleaseTitle(
                "Tomb Raider IV-VI Remastered",
                "Tomb Raider IV•V•VI Remastered (2025)"));
        AssertTrue(
            "deluxe edition is a different IGDB entry",
            !TitleMatchingService.IsSameReleaseTitle(
                "Tomb Raider IV-VI Remastered",
                "Tomb Raider IV•V•VI Remastered: Deluxe Edition (2025)"));
        AssertTrue(
            "I-III is not IV-VI",
            !TitleMatchingService.IsSameReleaseTitle(
                "Tomb Raider IV-VI Remastered",
                "Tomb Raider I•II•III Remastered (2024)"));
        AssertTrue(
            "expanded roman query is one of the fallbacks",
            TitleMatchingService.IgdbSearchQueries("Tomb Raider IV-VI Remastered")
                .Any(x => string.Equals(x, "Tomb Raider IV V VI Remastered", StringComparison.Ordinal)));
        AssertTrue(
            "IGDB search stops at four queries",
            TitleMatchingService.IgdbSearchQueries("A-B-C-D-E-F Game").Count <= 4);
        AssertTrue(
            "a subtitle is not treated as a region tag",
            !TitleMatchingService.IsReliableMatch("Trine 4: The Nightmare Prince", "Trine 4"));
        AssertTrue(
            "year-anchored Multiplayer matches the base release",
            TitleMatchingService.IsReliableMatch(
                "Call of Duty: Modern Warfare 3 (2011) - Multiplayer",
                "Call of Duty: Modern Warfare 3 (2011)"));
        AssertTrue(
            "year-anchored Multiplayer is the same IGDB release as the base",
            TitleMatchingService.IsSameReleaseTitle(
                "Call of Duty: Modern Warfare 3 (2011) - Multiplayer",
                "Call of Duty: Modern Warfare 3 (2011)"));
        AssertEqual(
            "play-mode suffix strips for the base title",
            "Call of Duty: Modern Warfare 3 (2011)",
            TitleMatchingService.WithoutPlayModeSuffix(
                "Call of Duty: Modern Warfare 3 (2011) - Multiplayer"));
        AssertTrue(
            "year-anchored play-mode base may be searched",
            TitleMatchingService.CanUsePlayModeBaseTitle(
                "Call of Duty: Modern Warfare 3 (2011) - Multiplayer"));
        AssertTrue(
            "Medal of Honor Multiplayer without a year is not a safe base alias",
            !TitleMatchingService.CanUsePlayModeBaseTitle("Medal of Honor(TM) Multiplayer"));
        AssertTrue(
            "Medal of Honor Multiplayer must not match a random sequel year",
            !TitleMatchingService.IsReliableMatch(
                "Medal of Honor(TM) Multiplayer",
                "Medal of Honor (1999)"));
        AssertTrue(
            "Medal of Honor Multiplayer must not match bare franchise title",
            !TitleMatchingService.IsReliableMatch(
                "Medal of Honor(TM) Multiplayer",
                "Medal of Honor"));
        AssertTrue(
            "year-anchored Medal of Honor Multiplayer matches that year",
            TitleMatchingService.IsReliableMatch(
                "Medal of Honor (2010) - Multiplayer",
                "Medal of Honor (2010)"));
        AssertTrue(
            "IGDB queries include the year-anchored base for Multiplayer packages",
            TitleMatchingService.IgdbSearchQueries(
                "Call of Duty: Modern Warfare 3 (2011) - Multiplayer")
                .Any(x => string.Equals(
                    x,
                    "Call of Duty: Modern Warfare 3 (2011)",
                    StringComparison.OrdinalIgnoreCase)));
        AssertTrue(
            "IGDB queries do not add a bare Medal of Honor alias",
            !TitleMatchingService.IgdbSearchQueries("Medal of Honor(TM) Multiplayer")
                .Any(x => string.Equals(x, "Medal of Honor", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(x, "Medal of Honor(TM)", StringComparison.OrdinalIgnoreCase)));
        AssertTrue(
            "Pokemon without accent matches IGDB Pokémon",
            TitleMatchingService.IsSameReleaseTitle("Pokemon Stadium 2", "Pokémon Stadium 2"));
        AssertTrue(
            "Pokemon Snap matches accented store title",
            TitleMatchingService.IsReliableMatch("Pokemon Snap (USA)", "Pokémon Snap"));
        AssertTrue(
            "accent-only difference is the same release key",
            string.Equals(
                TitleMatchingService.NormalizeTitle("Pokémon"),
                TitleMatchingService.NormalizeTitle("Pokemon"),
                StringComparison.Ordinal));
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

    private static void Test_LocalTermFallback_ModeAndEvidence()
    {
        AssertEqual(
            "default local-term fallback is leave empty",
            MetaDataIASettings.LocalTermFallbackOff,
            MetaDataIASettings.NormalizeLocalTermFallbackModeValue(null));
        AssertTrue(
            "local mode enables fallback",
            MetaDataIASettings.AllowsLocalTermFallback(MetaDataIASettings.LocalTermFallbackFromLocalText));
        AssertTrue(
            "off mode disables fallback",
            !MetaDataIASettings.AllowsLocalTermFallback(MetaDataIASettings.LocalTermFallbackOff));

        var thin = new Game { Name = "Thin Title Only" };
        AssertTrue(
            "title alone is not enough local evidence",
            !MetadataGenerationService.HasSufficientLocalTermEvidence(thin));

        var rich = new Game
        {
            Name = "Rich Local Game",
            Description =
                "An open-world action adventure where you explore ruins, solve environmental puzzles, " +
                "and battle creatures across a sprawling jungle. Play solo or with a friend in local co-op, " +
                "collect relics, unlock new traversal abilities, and uncover the story of a lost civilization " +
                "through temples, side quests, and optional challenge arenas."
        };
        AssertTrue(
            "a substantial description is enough local evidence",
            MetadataGenerationService.HasSufficientLocalTermEvidence(rich));

        var offRequest = new TermFieldRequest
        {
            Field = "tags",
            Mode = "overwrite",
            Incoming = new List<string>(),
            Organize = true,
            FromKnowledge = false,
            SkippedInsufficientLocalText = false
        };
        AssertTrue("leave-empty with no store list does not ask the model", !offRequest.NeedsModel);

        var localReady = new TermFieldRequest
        {
            Field = "tags",
            Mode = "overwrite",
            Incoming = new List<string>(),
            Organize = true,
            FromKnowledge = true
        };
        AssertTrue("local-text with evidence asks the model", localReady.NeedsModel);
    }

    private static void Test_Blacklist_ExactQuotedVsSubstring()
    {
        var exactSettings = CreateSettings("en");
        exactSettings.Blacklist = "\"Death\"";
        var exactRule = exactSettings.GetBlacklistRules().Single();
        AssertTrue("quoted rule is exact match", exactRule.ExactMatch);
        AssertTrue(
            "quoted Death is exact-only",
            !MetaDataIASettings.IsBlockedByBlacklist("Deathmatch", exactRule));
        AssertTrue(
            "quoted Death blocks exact tag",
            MetaDataIASettings.IsBlockedByBlacklist("Death", exactRule));

        var substringSettings = CreateSettings("en");
        substringSettings.Blacklist = "Death";
        var substringRule = substringSettings.GetBlacklistRules().Single();
        AssertTrue("unquoted rule is substring", !substringRule.ExactMatch);
        AssertTrue(
            "unquoted Death is substring",
            MetaDataIASettings.IsBlockedByBlacklist("Deathmatch", substringRule));

        var settings = CreateSettings("en");
        settings.Blacklist = "\"Death\", Retro";
        var result = CreateResult(
            new[] { "Action" },
            new[] { "Death", "Deathmatch", "RetroAchievements" });
        result.ApplyTermBlacklist(settings);
        AssertEqual("exact Death only", "Deathmatch", Join(result.Tags));
    }

    private static void Test_TermBlacklist_AppliesAfterOrganizeAssign()
    {
        var settings = CreateSettings("en");
        settings.Blacklist = "Achievements, Death, Retro";
        var result = CreateResult(
            genres: new[] { "Action", "Indie" },
            tags: new[] { "Action", "Death", "jungle", "boss fight", "RetroAchievements" });

        // Simulate organize / English fallback overwriting Normalize with raw IGDB lists.
        result.Tags = new List<string> { "Action", "Death", "jungle", "boss fight", "RetroAchievements" };
        result.ApplyTermBlacklist(settings);

        AssertEqual(
            "blacklist drops Death after organize assign",
            "Action, jungle, boss fight",
            Join(result.Tags));
        AssertTrue(
            "substring blacklist also drops RetroAchievements",
            !result.Tags.Any(t => t.IndexOf("Retro", StringComparison.OrdinalIgnoreCase) >= 0));
    }

    private static void Test_KeptLoanwords_ProtectsIndieFromStrip()
    {
        var settings = CreateSettings("es");
        settings.EnsureKeptLoanwordsDefaults();
        AssertTrue("defaults include Indie", settings.GetKeptLoanwordTerms().Any(t =>
            string.Equals(t, "Indie", StringComparison.OrdinalIgnoreCase)));
        AssertTrue("defaults include Party", settings.GetKeptLoanwordTerms().Any(t =>
            string.Equals(t, "Party", StringComparison.OrdinalIgnoreCase)));

        // Among Us style: Steam Casual (localized) + IGDB Strategy/Indie (English only).
        var amongUs = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Language = "es",
            Incoming = new List<string> { "Casual", "Strategy", "Indie" },
            LocalizedIncoming = new List<string> { "Casual" },
            NonLocalizedIncoming = new List<string> { "Strategy", "Indie" },
            MaxItems = 4,
            Organize = true
        };

        Dictionary<string, List<string>> withoutKeep;
        TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Casual\",\"Estrategia\",\"Indie\"]}]}",
            new List<TermFieldRequest> { amongUs },
            null,
            out withoutKeep);
        AssertEqual(
            "without keep-list Indie is stripped as raw foreign",
            "Casual, Estrategia",
            Join(withoutKeep["genres"]));

        Dictionary<string, List<string>> withKeep;
        TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Casual\",\"Estrategia\",\"Indie\"]}]}",
            new List<TermFieldRequest> { amongUs },
            settings.GetKeptLoanwordTerms(),
            out withKeep);
        AssertEqual(
            "keep-list preserves Indie with translated Strategy",
            "Casual, Estrategia, Indie",
            Join(withKeep["genres"]));

        AssertTrue(
            "Indie alone does not force translation retry when in keep-list",
            !TermFieldResolver.ResponseNeedsTranslationRetry(
                "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Casual\",\"Estrategia\",\"Indie\"]}]}",
                new List<TermFieldRequest> { amongUs },
                settings.GetKeptLoanwordTerms()));
        AssertTrue(
            "Strategy leftover still forces translation retry",
            TermFieldResolver.ResponseNeedsTranslationRetry(
                "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Casual\",\"Strategy\",\"Indie\"]}]}",
                new List<TermFieldRequest> { amongUs },
                settings.GetKeptLoanwordTerms()));

        var json = TermFieldResolver.BuildUserJson(
            "es",
            new[] { "PC" },
            new List<TermFieldRequest> { amongUs },
            settings.GetKeptLoanwordTerms());
        AssertTrue("user JSON includes keepLoanwords", json.IndexOf("keepLoanwords", StringComparison.Ordinal) >= 0);
        AssertTrue("user JSON lists Indie", json.IndexOf("Indie", StringComparison.Ordinal) >= 0);

        // User added Action to the keep-list: model/store "Acción" must become Action.
        var keepWithAction = settings.GetKeptLoanwordTerms().Concat(new[] { "Action" }).ToList();
        var arcRaiders = new TermFieldRequest
        {
            Field = "genres",
            Mode = "overwrite",
            Language = "es",
            Incoming = new List<string> { "Action", "Shooter", "Indie" },
            LocalizedIncoming = new List<string>(),
            NonLocalizedIncoming = new List<string> { "Action", "Shooter", "Indie" },
            MaxItems = 4,
            Organize = true
        };
        Dictionary<string, List<string>> actionForced;
        TermFieldResolver.TryApplyResponse(
            "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Acción\",\"Disparos\",\"Indie\"]}]}",
            new List<TermFieldRequest> { arcRaiders },
            keepWithAction,
            out actionForced);
        AssertEqual(
            "keep-list Action replaces Acción alias",
            "Disparos, Indie, Action",
            Join(actionForced["genres"]));
        AssertTrue(
            "Acción alias of kept Action does not force translation retry",
            !TermFieldResolver.ResponseNeedsTranslationRetry(
                "{\"fields\":[{\"field\":\"genres\",\"terms\":[\"Acción\",\"Disparos\",\"Indie\"]}]}",
                new List<TermFieldRequest> { arcRaiders },
                keepWithAction));

        var featureSettings = CreateSettings("es");
        featureSettings.EnsureKeptLoanwordsDefaults();
        featureSettings.KeptLoanwords = string.Join(", ", featureSettings.GetKeptLoanwordTerms().Concat(new[] { "PvP", "PvE" }));
        var features = CreateResult(new[] { "Action" }, new[] { "multiplayer" });
        features.Features = new List<string> { "Multijugador", "JcJ", "JcJ en línea" };
        features.ApplyKeptLoanwords(featureSettings);
        AssertEqual(
            "keep-list PvP rewrites Steam JcJ features",
            "Multijugador, PvP",
            Join(features.Features));
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
        AssertEqual("es keeps MMO acronym uppercase", "MMO", TextCapitalization.Apply("MMO", "es", false));
        AssertEqual("es keeps RPG acronym uppercase", "RPG", TextCapitalization.Apply("RPG", "es", false));
        AssertEqual("es sentence-cases ordinary words", "Disparos", TextCapitalization.Apply("DISPAROS", "es", false));
        AssertEqual("fr sentence case", "Coopératif en ligne", TextCapitalization.ToSentence("COOPÉRATIF EN LIGNE", "fr"));
        AssertEqual("second sentence starts with a capital", "Empieza aquí. Sigue allá.", TextCapitalization.ToSentence("EMPIEZA AQUÍ. SIGUE ALLÁ.", "es"));
        var html = "<h3>Descripcion breve</h3>\n<p>Disfruta de la trilogía clásica.</p>";
        AssertEqual("html description keeps template casing", html, TextCapitalization.ApplyDescription(html, "es", false));
        AssertEqual(
            "html description uppercase only touches text nodes",
            "<h3>DESCRIPCION BREVE</h3>\n<p>DISFRUTA DE LA TRILOGÍA CLÁSICA.</p>",
            TextCapitalization.ApplyDescription(html, "es", true));
        AssertTrue(
            "broken html casing would lowercase real copy",
            TextCapitalization.Apply(html, "es", false).IndexOf("disfruta", StringComparison.Ordinal) >= 0);
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

    private static void Test_StoreTermSanitizer_NoiseAndBrands()
    {
        AssertTrue("ROM dump is noise", StoreTermSanitizer.IsTechnicalNoise("ROM dump"));
        AssertTrue("JAMMA PCB is noise", StoreTermSanitizer.IsTechnicalNoise("JAMMA PCB"));
        AssertTrue("Arcade genre is not noise", !StoreTermSanitizer.IsTechnicalNoise("Arcade"));

        AssertEqual("strip Steam prefix", "Achievements", StoreTermSanitizer.StripStoreBrand("Steam Achievements"));
        AssertEqual("strip Steam Spanish suffix", "Logros", StoreTermSanitizer.StripStoreBrand("Logros de Steam"));
        AssertEqual("strip Steam Cloud", "Cloud", StoreTermSanitizer.StripStoreBrand("Steam Cloud"));
        AssertEqual("keep Workshop after strip", "Workshop", StoreTermSanitizer.StripStoreBrand("Steam Workshop"));

        var sanitized = StoreTermSanitizer.Sanitize(new[]
        {
            "Steam Achievements",
            "Logros de Steam",
            "JAMMA PCB",
            "ROM dump",
            "Un jugador",
            "Steam Achievements"
        });
        AssertEqual(
            "sanitize drops noise and brand duplicates by key",
            "Achievements, Logros, Un jugador",
            Join(sanitized));
    }

    private static void Test_BatchTermSessionCache_PrefersFirstStoreSpelling()
    {
        var cache = new BatchTermSessionCache();
        cache.RememberLocalizedStoreTerms("features", new[] { "Logros", "Nube", "Compatibilidad total con mando" });
        // Later brand-wrapped form of Logros must not overwrite the first session spelling.
        cache.RememberLocalizedStoreTerms("features", new[] { "Logros de Steam" });

        AssertEqual(
            "session keeps first localized store spelling",
            "Logros, Nube, Compatibilidad total con mando",
            Join(cache.GetPreferred("features")));

        var preferred = cache.PreferSpellings(
            "features",
            new[] { "logros", "nube", "Soporte total para mando" });
        AssertEqual(
            "prefer remaps matching keys to session spelling",
            "Logros, Nube, Soporte total para mando",
            Join(preferred));

        var json = TermFieldResolver.BuildUserJson(
            "es",
            new[] { "PC" },
            new List<TermFieldRequest>
            {
                new TermFieldRequest
                {
                    Field = "features",
                    Mode = "overwrite",
                    Language = "es",
                    Incoming = new List<string> { "Steam Achievements" },
                    MaxItems = 8
                }
            },
            null,
            null,
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "features", new List<string> { "Logros" } }
            });
        AssertTrue("organize JSON omits full session dump by default", !json.Contains("sessionVocabulary"));
        AssertTrue("organize JSON includes preferredSpellings", json.Contains("preferredSpellings"));
        AssertTrue("organize JSON carries preferred Logros", json.Contains("Logros"));
    }

    private static void Test_ScopedTermVocabulary_KeyMatchesOnly_PlayniteWins()
    {
        var preferred = ScopedTermVocabulary.BuildPreferredSpellings(
            new[] { "Steam Achievements", "Cloud", "Full Controller Support", "Unrelated ignore" },
            playniteLibraryNames: new[] { "Logros", "Nube", "Soporte total para mando", "Simulador de vuelo" },
            sessionNames: new[] { "Achievements", "Cloud", "Compatibilidad total con mando" });

        // Achievements key matches session Achievements (Playnite has Logros — different key, no cross-lang merge).
        // Cloud matches Playnite Nube? No — different keys. Cloud matches session Cloud.
        // Full Controller Support key != Soporte total... — no match; session Compatibilidad... different key.
        // Simulador de vuelo not in incoming keys — excluded.
        AssertEqual(
            "scoped prefers key matches only; Playnite wins when same key",
            "Achievements, Cloud",
            Join(preferred));

        var playniteWins = ScopedTermVocabulary.BuildPreferredSpellings(
            new[] { "logros", "nube" },
            new[] { "Logros", "Nube" },
            new[] { "LOGROS", "Steam Cloud" });
        AssertEqual(
            "Playnite spelling wins over session on same key",
            "Logros, Nube",
            Join(playniteWins));

        var json = TermFieldResolver.BuildUserJson(
            "es",
            new[] { "PC" },
            new List<TermFieldRequest>
            {
                new TermFieldRequest
                {
                    Field = "features",
                    Mode = "overwrite",
                    Language = "es",
                    Incoming = new List<string> { "logros" },
                    MaxItems = 8
                }
            },
            null,
            null,
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "features", new List<string> { "Logros" } }
            });
        AssertTrue("organize JSON includes preferredSpellings", json.Contains("preferredSpellings"));
        AssertTrue("organize JSON carries preferred Logros", json.Contains("Logros"));
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
