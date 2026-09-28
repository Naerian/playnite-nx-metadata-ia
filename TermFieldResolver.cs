using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// One closed JSON request and one closed JSON response for genres, tags,
    /// features and categories. The model may translate and collapse duplicates.
    /// It may not invent a concept that was not in the lists it received.
    /// </summary>
    public sealed class TermFieldRequest
    {
        public string Field { get; set; }
        public string Mode { get; set; }
        public string Language { get; set; }
        public List<string> Existing { get; set; }
        public List<string> Incoming { get; set; }
        /// <summary>
        /// Preferred fallback when the model fails: store lists already in the plugin language.
        /// Avoids writing raw English IGDB/Steam labels into a non-English library.
        /// </summary>
        public List<string> LocalizedIncoming { get; set; }
        /// <summary>
        /// Labels taken only from non-plugin-language sources (typically IGDB English).
        /// Used for debug and to expand the raw-foreign safety net.
        /// </summary>
        public List<string> NonLocalizedIncoming { get; set; }
        public int MaxItems { get; set; }
        public bool AlreadyInLanguage { get; set; }
        public bool Organize { get; set; }
        public bool FromKnowledge { get; set; }

        public TermFieldRequest()
        {
            Existing = new List<string>();
            Incoming = new List<string>();
            LocalizedIncoming = new List<string>();
            NonLocalizedIncoming = new List<string>();
            MaxItems = 12;
            AlreadyInLanguage = true;
            Language = "en";
        }

        public bool NeedsModel
        {
            get
            {
                if (string.Equals(Mode, "skip", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (FromKnowledge)
                {
                    if (string.Equals(Mode, "empty", StringComparison.OrdinalIgnoreCase) && Existing.Count > 0)
                    {
                        return false;
                    }

                    return string.Equals(Mode, "overwrite", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Mode, "append", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Mode, "empty", StringComparison.OrdinalIgnoreCase);
                }

                if (Incoming.Count == 0)
                {
                    return false;
                }

                // Empty-only with a filled field: keep current labels, no model.
                if (string.Equals(Mode, "empty", StringComparison.OrdinalIgnoreCase) && Existing.Count > 0)
                {
                    return false;
                }

                if (Organize &&
                    (string.Equals(Mode, "overwrite", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(Mode, "append", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(Mode, "empty", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (string.Equals(Mode, "append", StringComparison.OrdinalIgnoreCase) && Existing.Count > 0)
                {
                    return true;
                }

                return !AlreadyInLanguage &&
                       (string.Equals(Mode, "overwrite", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Mode, "append", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Mode, "empty", StringComparison.OrdinalIgnoreCase));
            }
        }

        public List<string> DirectTerms()
        {
            if (string.Equals(Mode, "empty", StringComparison.OrdinalIgnoreCase) && Existing.Count > 0)
            {
                return Take(Existing);
            }

            if (string.Equals(Mode, "empty", StringComparison.OrdinalIgnoreCase) && Existing.Count == 0)
            {
                return Take(PreferredIncoming());
            }

            if (string.Equals(Mode, "append", StringComparison.OrdinalIgnoreCase) && Existing.Count == 0)
            {
                return Take(PreferredIncoming());
            }

            if (string.Equals(Mode, "overwrite", StringComparison.OrdinalIgnoreCase))
            {
                return Take(PreferredIncoming());
            }

            return Take(Existing);
        }

        public List<string> FallbackTerms()
        {
            if (string.Equals(Mode, "overwrite", StringComparison.OrdinalIgnoreCase))
            {
                return Take(PreferredIncoming());
            }

            if (string.Equals(Mode, "empty", StringComparison.OrdinalIgnoreCase))
            {
                return Existing.Count > 0 ? Take(Existing) : Take(PreferredIncoming());
            }

            return Take(Existing.Concat(PreferredIncoming()));
        }

        private IEnumerable<string> PreferredIncoming()
        {
            // Used when the model is not needed (DirectTerms) or as a last-resort source for
            // English libraries. Provider/organize failures must not apply this for non-English
            // languages — that looks updated while omitting non-localized sources (e.g. IGDB).
            var localized = LocalizedIncoming ?? new List<string>();
            if (TermFieldResolver.RequiresTranslation(Language))
            {
                return localized;
            }

            var incoming = Incoming ?? new List<string>();
            if (incoming.Count > 0)
            {
                return incoming;
            }

            return localized;
        }

        /// <summary>
        /// Terms to keep when the model answer is unusable. Non-English libraries get empty
        /// lists so a failed organize never looks like a successful partial Steam-only apply.
        /// </summary>
        public List<string> FailedOrganizeTerms()
        {
            if (TermFieldResolver.RequiresTranslation(Language))
            {
                return new List<string>();
            }

            return FallbackTerms();
        }

        private List<string> Take(IEnumerable<string> values)
        {
            return TermFieldResolver.DistinctTerms(values).Take(Math.Max(1, MaxItems)).ToList();
        }
    }

    public static class TermFieldResolver
    {
        // Keep policy in sync with MetadataGenerationService.BuildSystemPrompt Field Categorization Rules
        // (same organize rules; this call uses language/languageName + fields[].terms JSON shape).
        public const string SystemPrompt =
            "You edit short game metadata labels. Return ONLY one JSON object. " +
            "The response must start with { and end with }. No markdown fences, no prose before/after, no JSON wrapped inside a string. " +
            "Output shape: {\"fields\":[{\"field\":\"genres\",\"terms\":[\"...\"]}]} " +
            "Rules: " +
            "1. Output format: Echo each input field once. In \"terms\", return only the final normalized labels as a flat string array (not nested arrays). " +
            "2. Source and Grounding: Organize strictly from each field's existing and incoming lists. " +
            "Never invent concepts absent from those lists. If an incoming list is empty, return an empty terms array for that field. " +
            "3. Language and universal gaming loanwords: Read target language from \"language\" and \"languageName\". " +
            "If not English: translate EVERY common noun and descriptive term into the requested language " +
            "(e.g., Adventure -> Aventura, Shooter -> Disparos, Strategy -> Estrategia, Puzzle -> Puzle, Action -> Acción, or the equivalent). " +
            "Do not leave any of those English store strings unchanged. Do not mix languages in the same terms array " +
            "(never return both \"Aventura\" and \"Adventure\", and never leave \"Shooter\" beside \"Disparos\"). " +
            "If English: keep standard English labels. " +
            "Universal gaming terms (loanwords): keep recognized industry subgenres and gameplay mechanics as-is in their standard form in every language. " +
            "Examples include subgenre neologisms (Roguelike, Roguelite, Metroidvania, Soulslike), " +
            "gameplay formats and styles (Hack and slash, Battle Royale, MOBA, Deckbuilder, Sandbox, Auto Battler, Bullet Hell), " +
            "and production/format scope (Indie). " +
            "Acronyms: keep standard universal industry acronyms in UPPERCASE (e.g., RPG, MMO, RTS) unless a clear native canonical counterpart is already established in the vocabulary. " +
            "Copying an English incoming string unchanged is allowed ONLY for those true loanwords/acronyms — never for ordinary nouns like Shooter/Adventure/Puzzle. " +
            "4. Canonical label and Deduplication: Exactly one label per concept. " +
            "Never mix synonyms or languages for the same concept (pick only one: e.g., \"Aventura\" and never \"Adventure\"; \"Rol\" and never \"Role-playing (rpg)\" when Rol is the chosen native form; \"Puzle\" and never \"Puzzle\" / \"Rompecabezas\"). " +
            "Keep labels concise (1-3 words max for genres/tags; features 1-5 words, Steam-style, no full sentences, no final punctuation). " +
            "Do not repeat the same concept across multiple fields (player count/modes belong to features; store genres stay in genres; theme/style stay in tags). " +
            "5. Handling mode: " +
            "overwrite: terms must contain only normalized concepts from incoming. " +
            "append: merge unique concepts from existing and incoming; if a concept already exists in existing, do not add a translated/synonym duplicate from incoming. " +
            "empty: if existing already has items, return existing unchanged; if existing is empty, populate from incoming. " +
            "Item caps are applied by the plugin after your response — return the full normalized set from incoming; do not pretuncate.";

        public const string KnowledgePrompt =
            "No store returned a list for the fields in this request. Propose short, accurate metadata labels strictly using the provided game release facts. " +
            "Return ONLY one JSON object. The response must start with { and end with }. No markdown fences, no prose, no JSON wrapped inside a string. " +
            "Output shape: {\"fields\":[{\"field\":\"genres\",\"terms\":[\"...\"]}]} " +
            "Rules: " +
            "1. Target Language and Universal Gaming Loanwords: Echo each input field once. In \"terms\", return a flat string array. " +
            "Read target language from \"language\" and \"languageName\". " +
            "If language is NOT English: translate EVERY common noun and descriptive term into the requested language " +
            "(e.g., Shooter -> Disparos, Action -> Acción, Adventure -> Aventura, Puzzle -> Puzle, or the equivalent). " +
            "Do not leave those English strings unchanged. Do not mix languages in the same terms array. " +
            "If English: keep standard English labels. " +
            "Universal gaming terms (loanwords): keep recognized industry subgenres and gameplay mechanics as-is " +
            "(e.g., Roguelike, Roguelite, Metroidvania, Soulslike, Hack and slash, Battle Royale, MOBA, Deckbuilder, Sandbox, Auto Battler, Bullet Hell, Indie). " +
            "Keep standard universal acronyms in UPPERCASE (e.g., RPG, MMO, RTS) unless a clear native canonical counterpart is already established. " +
            "2. Strict Concept Normalization: Exactly one label per concept. Never output mixed languages or synonyms " +
            "(pick one: \"Aventura\", never \"Adventure\"; \"Rol\", never \"Role-playing (rpg)\" when Rol is the chosen native form; \"Puzle\", never \"Puzzle\" or \"Rompecabezas\"). " +
            "Concise labels: 1 to 3 words max. Never repeat the same concept across multiple fields. " +
            "3. Platform and Release Grounding (Zero Hallucination): Anchor labels strictly to the specific platform and release facts provided. " +
            "Do NOT extrapolate features or genres from modern remakes or subsequent ports. " +
            "For retro releases, never invent modern technical features (e.g., no cloud saves or online co-op for 8/16-bit console titles). " +
            "If game facts are insufficient to identify the game with high confidence, return an empty terms array. " +
            "4. Field Categorization: genres: Core video game store genres only. tags: Setting, theme, and gameplay mechanics. " +
            "features: Functional gameplay traits for this specific platform release only (player count, local co-op, controller support). " +
            "At most 4 concise feature items. Never put player counts or features into genres or tags. " +
            "5. Handling mode: overwrite: terms must contain only new normalized labels. " +
            "append: preserve existing labels and add missing unique labels for this release without adding synonyms or language duplicates. " +
            "empty: if existing already has items, return existing unchanged; otherwise populate.";

        public static bool ResponseIsParseableJson(string content)
        {
            JObject unused;
            return TryParseObject(content, out unused);
        }

        public static string BuildUserJson(string language, IList<string> platforms, IList<TermFieldRequest> fields)
        {
            var payload = new JObject();
            payload["language"] = language ?? "en";
            payload["languageName"] = LanguageDisplayName(language);
            payload["platform"] = new JArray(platforms ?? new List<string>());
            payload["fields"] = new JArray((fields ?? new List<TermFieldRequest>()).Select(field =>
            {
                var item = new JObject();
                item["field"] = field.Field;
                item["mode"] = field.Mode;
                item["existing"] = new JArray(field.Existing ?? new List<string>());
                item["incoming"] = new JArray(field.Incoming ?? new List<string>());
                item["alreadyInLanguage"] = field.AlreadyInLanguage;
                return item;
            }));
            return payload.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string BuildKnowledgeJson(string language, JObject game, IList<TermFieldRequest> fields)
        {
            var payload = new JObject();
            payload["language"] = language ?? "en";
            payload["languageName"] = LanguageDisplayName(language);
            payload["game"] = game ?? new JObject();
            payload["fields"] = new JArray((fields ?? new List<TermFieldRequest>()).Select(field =>
            {
                var item = new JObject();
                item["field"] = field.Field;
                item["mode"] = field.Mode;
                item["existing"] = new JArray(field.Existing ?? new List<string>());
                item["max"] = Math.Max(1, field.MaxItems);
                return item;
            }));
            return payload.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string LanguageDisplayName(string language)
        {
            var code = (language ?? "en").Trim().ToLowerInvariant();
            if (code.StartsWith("es", StringComparison.Ordinal)) return "Spanish";
            if (code.StartsWith("fr", StringComparison.Ordinal)) return "French";
            if (code.StartsWith("de", StringComparison.Ordinal)) return "German";
            if (code.StartsWith("it", StringComparison.Ordinal)) return "Italian";
            if (code.StartsWith("pt", StringComparison.Ordinal)) return "Portuguese";
            if (code.StartsWith("pl", StringComparison.Ordinal)) return "Polish";
            if (code.StartsWith("nl", StringComparison.Ordinal)) return "Dutch";
            if (code.StartsWith("ru", StringComparison.Ordinal)) return "Russian";
            if (code.StartsWith("ja", StringComparison.Ordinal)) return "Japanese";
            if (code.StartsWith("ko", StringComparison.Ordinal)) return "Korean";
            if (code.StartsWith("zh", StringComparison.Ordinal)) return "Chinese";
            if (code.StartsWith("tr", StringComparison.Ordinal)) return "Turkish";
            return "English";
        }

        public static bool TryApplyResponse(string content, IList<TermFieldRequest> fields, out Dictionary<string, List<string>> resolved)
        {
            resolved = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var requested = fields ?? new List<TermFieldRequest>();
            JObject json;
            if (!TryParseObject(content, out json))
            {
                foreach (var field in requested)
                {
                    resolved[field.Field] = field.FailedOrganizeTerms();
                }

                return false;
            }

            var returned = ReadFields(json);
            var anyAccepted = false;
            foreach (var field in requested)
            {
                List<string> terms;
                if (!returned.TryGetValue(field.Field, out terms) || !Accept(field, requested, terms))
                {
                    resolved[field.Field] = field.FailedOrganizeTerms();
                    continue;
                }

                var cleaned = DistinctTerms(terms);
                cleaned = StripRawForeignIncoming(cleaned, field).ToList();
                cleaned = cleaned.Take(Math.Max(1, field.MaxItems)).ToList();
                if (cleaned.Count == 0)
                {
                    resolved[field.Field] = field.FailedOrganizeTerms();
                    continue;
                }

                resolved[field.Field] = cleaned;
                anyAccepted = true;
            }

            return anyAccepted || requested.Count == 0;
        }

        public static bool ResponseNeedsTranslationRetry(string content, IList<TermFieldRequest> fields)
        {
            var requested = (fields ?? new List<TermFieldRequest>())
                .Where(field => RequiresTranslation(field.Language))
                .ToList();
            if (requested.Count == 0)
            {
                return false;
            }

            JObject json;
            if (!TryParseObject(content, out json))
            {
                return true;
            }

            var returned = ReadFields(json);
            foreach (var field in requested)
            {
                List<string> terms;
                if (!returned.TryGetValue(field.Field, out terms))
                {
                    return true;
                }

                var cleaned = DistinctTerms(terms);
                if (cleaned.Count == 0)
                {
                    continue;
                }

                // Knowledge has no store Incoming to compare against, so mixed/untranslated
                // English labels (e.g. "Shooter") would never trigger the Incoming-based check.
                // Force one localization retry when the plugin language is not English.
                if (field.FromKnowledge)
                {
                    return true;
                }

                if (RawForeignIncomingKeys(field).Overlaps(cleaned.Select(LibraryNameMatching.NormalizeKey)))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool RequiresTranslation(string language)
        {
            var code = (language ?? "en").Trim();
            return code.Length > 0 && !code.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Labels that appear in Incoming but not in LocalizedIncoming for this request.
        /// Dynamic per request (store/IGDB pool), not a hard-coded English dictionary.
        /// Used only when the model copies those non-localized store strings unchanged.
        /// </summary>
        public static HashSet<string> RawForeignIncomingKeys(TermFieldRequest field)
        {
            var raw = new HashSet<string>(StringComparer.Ordinal);
            if (field == null || !RequiresTranslation(field.Language))
            {
                return raw;
            }

            var localized = new HashSet<string>(
                (field.LocalizedIncoming ?? new List<string>())
                    .Select(LibraryNameMatching.NormalizeKey)
                    .Where(key => key.Length > 0),
                StringComparer.Ordinal);
            foreach (var term in field.Incoming ?? new List<string>())
            {
                var key = LibraryNameMatching.NormalizeKey(term);
                if (key.Length == 0 || localized.Contains(key))
                {
                    continue;
                }

                raw.Add(key);
            }

            return raw;
        }

        public static IEnumerable<string> StripRawForeignIncoming(IEnumerable<string> values, TermFieldRequest field)
        {
            var raw = RawForeignIncomingKeys(field);
            if (raw.Count == 0)
            {
                return values ?? Enumerable.Empty<string>();
            }

            return (values ?? Enumerable.Empty<string>())
                .Where(value => !raw.Contains(LibraryNameMatching.NormalizeKey(value)));
        }

        public static List<string> DistinctTerms(IEnumerable<string> values)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                var cleaned = VocabularyTermNormalizer.CleanTerm(value);
                if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length > 80 || !seen.Add(cleaned))
                {
                    continue;
                }

                result.Add(cleaned);
            }

            return result;
        }

        private static bool Accept(TermFieldRequest field, IList<TermFieldRequest> allFields, List<string> terms)
        {
            var cleaned = DistinctTerms(terms);
            var union = DistinctTerms((field.Existing ?? new List<string>()).Concat(field.Incoming ?? new List<string>()));
            if (cleaned.Count == 0)
            {
                return union.Count == 0;
            }

            // Leave raw foreign Incoming labels for StripRawForeignIncoming after Accept so a
            // mixed answer can keep already-translated terms (Acción + Disparos) instead of
            // discarding the whole field when one English leftover remains.

            if (field.FromKnowledge)
            {
                if (cleaned.Count > Math.Max(1, field.MaxItems))
                {
                    return false;
                }

                return !string.Equals(field.Mode, "append", StringComparison.OrdinalIgnoreCase) || KeepsExisting(field, cleaned);
            }

            // MaxItems is enforced after Accept via Take. Do not reject a valid overshoot
            // (e.g. model returns 5 labels when MaxItems is 4) — truncate instead.
            if (cleaned.Count > Math.Max(union.Count, 1))
            {
                return false;
            }

            var allowed = new HashSet<string>(
                allFields.SelectMany(item => (item.Existing ?? new List<string>()).Concat(item.Incoming ?? new List<string>()))
                    .Select(LibraryNameMatching.NormalizeKey)
                    .Where(key => key.Length > 0),
                StringComparer.Ordinal);
            // Translations change spelling (Shooter → Disparos), so they look "novel" under
            // NormalizeKey. Allow up to one translated label per incoming concept.
            var novel = cleaned.Count(term => !allowed.Contains(LibraryNameMatching.NormalizeKey(term)));
            var incomingCount = Math.Max(1, (field.Incoming ?? new List<string>()).Count);
            if (novel > incomingCount)
            {
                return false;
            }

            if (!string.Equals(field.Mode, "append", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return KeepsExisting(field, cleaned);
        }

        private static bool KeepsExisting(TermFieldRequest field, List<string> cleaned)
        {
            var keptTokens = TokenSet(cleaned);
            foreach (var existing in field.Existing ?? new List<string>())
            {
                if (cleaned.Any(term => string.Equals(LibraryNameMatching.NormalizeKey(term), LibraryNameMatching.NormalizeKey(existing), StringComparison.Ordinal)))
                {
                    continue;
                }

                if (!SharesToken(existing, keptTokens))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SharesToken(string value, HashSet<string> keptTokens)
        {
            foreach (var token in Tokens(value))
            {
                if (keptTokens.Contains(token))
                {
                    return true;
                }

                foreach (var kept in keptTokens)
                {
                    if (token.Length >= 5 && kept.Length >= 5 &&
                        (token.StartsWith(kept, StringComparison.Ordinal) || kept.StartsWith(token, StringComparison.Ordinal)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static HashSet<string> TokenSet(IEnumerable<string> values)
        {
            return new HashSet<string>(values.SelectMany(Tokens), StringComparer.Ordinal);
        }

        private static IEnumerable<string> Tokens(string value)
        {
            return (LibraryNameMatching.NormalizeKey(value) ?? string.Empty)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length >= 4);
        }

        private static Dictionary<string, List<string>> ReadFields(JObject json)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (json == null)
            {
                return map;
            }

            var fields = json["fields"] as JArray;
            if (fields != null)
            {
                foreach (var item in fields.OfType<JObject>())
                {
                    var name = ((string)item["field"] ?? string.Empty).Trim();
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    var terms = (item["terms"] as JArray ?? new JArray())
                        .Select(token => (string)token)
                        .ToList();
                    map[name] = terms;
                }
            }

            // Some models reuse the main-call shape: {"genres":["..."],"tags":["..."]}
            // instead of {"fields":[{"field":"genres","terms":["..."]}]}.
            foreach (var property in json.Properties())
            {
                if (string.Equals(property.Name, "fields", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var array = property.Value as JArray;
                if (array == null || map.ContainsKey(property.Name))
                {
                    continue;
                }

                if (!array.All(token => token == null || token.Type == JTokenType.String || token.Type == JTokenType.Integer))
                {
                    continue;
                }

                map[property.Name] = array.Select(token => token == null ? null : token.ToString()).ToList();
            }

            return map;
        }

        private static bool TryParseObject(string content, out JObject json)
        {
            return AiResponseJson.TryParseObject(content, out json);
        }
    }
}
