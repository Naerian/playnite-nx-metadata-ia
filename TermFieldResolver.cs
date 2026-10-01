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
        /// <summary>
        /// True when local-text fallback was enabled but the game description/facts were too thin
        /// to justify a knowledge call. Fields stay empty instead of inventing labels.
        /// </summary>
        public bool SkippedInsufficientLocalText { get; set; }

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
        // Policy lives in TermFieldPrompts (shared with the main metadata system prompt).
        public static string SystemPrompt
        {
            get { return TermFieldPrompts.OrganizeSystemPrompt; }
        }

        // Used when genres/tags/features have no store/IGDB list and Library
        // "Derive from local game text" is on with enough description/facts.
        public static string KnowledgePrompt
        {
            get { return TermFieldPrompts.KnowledgePrompt; }
        }

        public static bool ResponseIsParseableJson(string content)
        {
            JObject unused;
            return TryParseObject(content, out unused);
        }

        public static string BuildUserJson(
            string language,
            IList<string> platforms,
            IList<TermFieldRequest> fields,
            IEnumerable<string> keepLoanwords = null,
            IDictionary<string, List<string>> sessionVocabulary = null,
            IDictionary<string, List<string>> preferredSpellings = null)
        {
            var payload = new JObject();
            payload["language"] = language ?? "en";
            payload["languageName"] = LanguageDisplayName(language);
            payload["platform"] = new JArray(platforms ?? new List<string>());
            payload["keepLoanwords"] = new JArray(NormalizeKeepList(keepLoanwords));
            var session = BuildVocabularyObject(sessionVocabulary, fields);
            if (session != null)
            {
                payload["sessionVocabulary"] = session;
            }

            var preferred = BuildVocabularyObject(preferredSpellings, fields);
            if (preferred != null)
            {
                payload["preferredSpellings"] = preferred;
            }

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

        private static JObject BuildVocabularyObject(
            IDictionary<string, List<string>> vocabulary,
            IList<TermFieldRequest> fields)
        {
            if (vocabulary == null || vocabulary.Count == 0)
            {
                return null;
            }

            var requested = new HashSet<string>(
                (fields ?? new List<TermFieldRequest>()).Select(x => x.Field ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            var obj = new JObject();
            foreach (var pair in vocabulary)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Value == null ||
                    pair.Value.Count == 0 ||
                    (requested.Count > 0 && !requested.Contains(pair.Key)))
                {
                    continue;
                }

                obj[pair.Key] = new JArray(
                    pair.Value
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase));
            }

            return obj.Count == 0 ? null : obj;
        }

        public static string BuildKnowledgeJson(string language, JObject game, IList<TermFieldRequest> fields, IEnumerable<string> keepLoanwords = null)
        {
            var payload = new JObject();
            payload["language"] = language ?? "en";
            payload["languageName"] = LanguageDisplayName(language);
            payload["game"] = game ?? new JObject();
            payload["keepLoanwords"] = new JArray(NormalizeKeepList(keepLoanwords));
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

        private static List<string> NormalizeKeepList(IEnumerable<string> keepLoanwords)
        {
            return (keepLoanwords ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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
            return TryApplyResponse(content, fields, null, out resolved);
        }

        public static bool TryApplyResponse(
            string content,
            IList<TermFieldRequest> fields,
            IEnumerable<string> keepLoanwords,
            out Dictionary<string, List<string>> resolved)
        {
            resolved = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var requested = fields ?? new List<TermFieldRequest>();
            var keepKeys = GamingLoanwordVocabulary.ToKeySet(keepLoanwords);
            JObject json;
            if (!TryParseObject(content, out json))
            {
                foreach (var field in requested)
                {
                    resolved[field.Field] = field.FailedOrganizeTerms();
                }

                // English libraries already have FailedOrganizeTerms filled from store Incoming.
                // Treat that as a usable apply so a parse miss does not discard good store lists.
                return HasUsableEnglishFallback(requested, resolved);
            }

            var returned = ReadFields(json);
            var anyAccepted = false;
            foreach (var field in requested)
            {
                List<string> terms;
                if (!TryGetReturnedTerms(returned, field, requested, out terms) ||
                    !Accept(field, requested, terms))
                {
                    resolved[field.Field] = field.FailedOrganizeTerms();
                    continue;
                }

                var cleaned = DistinctTerms(terms);
                cleaned = StripRawForeignIncoming(cleaned, field, keepKeys).ToList();
                cleaned = GamingLoanwordVocabulary.EnforceKeepListSpelling(cleaned, field, keepLoanwords);
                cleaned = cleaned.Take(Math.Max(1, field.MaxItems)).ToList();
                if (cleaned.Count == 0)
                {
                    resolved[field.Field] = field.FailedOrganizeTerms();
                    continue;
                }

                resolved[field.Field] = cleaned;
                anyAccepted = true;
            }

            if (anyAccepted || requested.Count == 0)
            {
                return true;
            }

            return HasUsableEnglishFallback(requested, resolved);
        }

        private static bool HasUsableEnglishFallback(
            IList<TermFieldRequest> requested,
            Dictionary<string, List<string>> resolved)
        {
            if (requested == null || requested.Count == 0 || resolved == null)
            {
                return false;
            }

            var any = false;
            foreach (var field in requested)
            {
                if (RequiresTranslation(field.Language))
                {
                    return false;
                }

                List<string> terms;
                if (!resolved.TryGetValue(field.Field, out terms) || terms == null || terms.Count == 0)
                {
                    // Empty is fine when both Existing and Incoming were empty.
                    var union = DistinctTerms(
                        (field.Existing ?? new List<string>()).Concat(field.Incoming ?? new List<string>()));
                    if (union.Count > 0)
                    {
                        return false;
                    }

                    continue;
                }

                any = true;
            }

            return any;
        }

        private static bool TryGetReturnedTerms(
            Dictionary<string, List<string>> returned,
            TermFieldRequest field,
            IList<TermFieldRequest> requested,
            out List<string> terms)
        {
            terms = null;
            if (returned == null || field == null)
            {
                return false;
            }

            if (returned.TryGetValue(field.Field, out terms))
            {
                return true;
            }

            // Tiny local models often dump the only requested field under "genres".
            if (requested != null &&
                requested.Count == 1 &&
                returned.Count == 1)
            {
                terms = returned.Values.FirstOrDefault();
                return terms != null;
            }

            return false;
        }

        public static bool ResponseNeedsTranslationRetry(string content, IList<TermFieldRequest> fields)
        {
            return ResponseNeedsTranslationRetry(content, fields, null);
        }

        public static bool ResponseNeedsTranslationRetry(
            string content,
            IList<TermFieldRequest> fields,
            IEnumerable<string> keepLoanwords)
        {
            var requested = (fields ?? new List<TermFieldRequest>())
                .Where(field => RequiresTranslation(field.Language))
                .ToList();
            if (requested.Count == 0)
            {
                return false;
            }

            var keepKeys = GamingLoanwordVocabulary.ToKeySet(keepLoanwords);
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

                var leftoverKeys = cleaned
                    .Select(LibraryNameMatching.NormalizeKey)
                    .Where(key =>
                    {
                        if (key.Length == 0)
                        {
                            return false;
                        }

                        if (keepKeys.Contains(key))
                        {
                            return false;
                        }

                        // Locale aliases of a keep-list term (Acción when Action is kept)
                        // are rewritten after apply — do not force a translation retry for them.
                        foreach (var keepTerm in keepLoanwords ?? Enumerable.Empty<string>())
                        {
                            if (GamingLoanwordVocabulary.AliasKeysFor(keepTerm).Contains(key))
                            {
                                return false;
                            }
                        }

                        return true;
                    });
                if (RawForeignIncomingKeys(field).Overlaps(leftoverKeys))
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
            return StripRawForeignIncoming(values, field, null);
        }

        public static IEnumerable<string> StripRawForeignIncoming(
            IEnumerable<string> values,
            TermFieldRequest field,
            HashSet<string> keepLoanwordKeys)
        {
            var raw = RawForeignIncomingKeys(field);
            if (raw.Count == 0)
            {
                return values ?? Enumerable.Empty<string>();
            }

            var keep = keepLoanwordKeys ?? new HashSet<string>(StringComparer.Ordinal);
            return (values ?? Enumerable.Empty<string>())
                .Where(value =>
                {
                    var key = LibraryNameMatching.NormalizeKey(value);
                    if (keep.Contains(key))
                    {
                        return true;
                    }

                    return !raw.Contains(key);
                });
        }

        public static List<string> DistinctTerms(IEnumerable<string> values)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in ExpandCommaJoinedTerms(values))
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

        /// <summary>
        /// Models sometimes return ["Action, Comedy, jungle"] instead of ["Action","Comedy","jungle"].
        /// Expand only when every comma segment looks like a short label.
        /// </summary>
        internal static List<string> ExpandCommaJoinedTerms(IEnumerable<string> values)
        {
            var result = new List<string>();
            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (value.IndexOf(',') < 0)
                {
                    result.Add(value);
                    continue;
                }

                var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Trim())
                    .Where(part => part.Length > 0)
                    .ToList();
                if (parts.Count >= 2 && parts.All(LooksLikeShortLabel))
                {
                    result.AddRange(parts);
                    continue;
                }

                result.Add(value);
            }

            return result;
        }

        private static bool LooksLikeShortLabel(string part)
        {
            if (string.IsNullOrWhiteSpace(part) || part.Length > 40)
            {
                return false;
            }

            if (part.IndexOf('.') >= 0)
            {
                return false;
            }

            var words = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 0 && words.Length <= 6;
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

                    map[name] = ReadTermsToken(item["terms"]);
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

                if (map.ContainsKey(property.Name))
                {
                    continue;
                }

                if (property.Value is JArray ||
                    (property.Value != null && property.Value.Type == JTokenType.String))
                {
                    var terms = ReadTermsToken(property.Value);
                    if (terms.Count > 0 || property.Value is JArray)
                    {
                        map[property.Name] = terms;
                    }
                }
            }

            return map;
        }

        private static List<string> ReadTermsToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return new List<string>();
            }

            if (token.Type == JTokenType.String)
            {
                return ExpandCommaJoinedTerms(new[] { token.Value<string>() });
            }

            var array = token as JArray;
            if (array == null)
            {
                return new List<string>();
            }

            if (!array.All(item => item == null ||
                                   item.Type == JTokenType.String ||
                                   item.Type == JTokenType.Integer))
            {
                return new List<string>();
            }

            return ExpandCommaJoinedTerms(
                array.Select(item => item == null ? null : item.ToString()));
        }

        private static bool TryParseObject(string content, out JObject json)
        {
            return AiResponseJson.TryParseObject(content, out json);
        }
    }
}
