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
        public List<string> Existing { get; set; }
        public List<string> Incoming { get; set; }
        public int MaxItems { get; set; }
        public bool AlreadyInLanguage { get; set; }
        public bool Organize { get; set; }
        public bool FromKnowledge { get; set; }

        public TermFieldRequest()
        {
            Existing = new List<string>();
            Incoming = new List<string>();
            MaxItems = 12;
            AlreadyInLanguage = true;
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
                return Take(Incoming);
            }

            if (string.Equals(Mode, "append", StringComparison.OrdinalIgnoreCase) && Existing.Count == 0)
            {
                return Take(Incoming);
            }

            if (string.Equals(Mode, "overwrite", StringComparison.OrdinalIgnoreCase))
            {
                return Take(Incoming);
            }

            return Take(Existing);
        }

        public List<string> FallbackTerms()
        {
            if (string.Equals(Mode, "overwrite", StringComparison.OrdinalIgnoreCase))
            {
                return Take(Incoming);
            }

            if (string.Equals(Mode, "empty", StringComparison.OrdinalIgnoreCase))
            {
                return Existing.Count > 0 ? Take(Existing) : Take(Incoming);
            }

            return Take(Existing.Concat(Incoming));
        }

        private List<string> Take(IEnumerable<string> values)
        {
            return TermFieldResolver.DistinctTerms(values).Take(Math.Max(1, MaxItems)).ToList();
        }
    }

    public static class TermFieldResolver
    {
        public const string SystemPrompt =
            "You edit short game metadata labels. Return only JSON, no markdown. " +
            "Response shape: {\"fields\":[{\"field\":\"genres\",\"terms\":[\"...\"]}]} " +
            "Echo each input field once. " +
            "terms are the final labels in the requested language (the language field). " +
            "Translate every common-noun label into that language. Do not leave English store or IGDB wording " +
            "such as Shooter, Action, Adventure, Science fiction, Single-player or Multiplayer when the language is not English. " +
            "Proper names (series, franchises, game titles) stay unchanged. " +
            "Use only concepts present in that field's existing and incoming lists. " +
            "You may translate an incoming label into the requested language. Do not add a concept that is not in those lists. " +
            "platform is the game's real platform. Drop a label that names a different platform. Keep store themes, genres and play styles that were in the lists. " +
            "Keep one label per concept. " +
            "If a broad label is only the sum of more specific labels in the same lists, keep the specific labels and drop the broad one. " +
            "Example: existing \"Action and adventure\" plus incoming \"Action\" and \"Adventure\" becomes \"Action\" and \"Adventure\" in English, or the equivalent pair in the requested language. " +
            "mode overwrite: ignore existing. " +
            "mode append: keep unrelated existing labels, add incoming labels, and drop only labels that repeat a concept you kept. " +
            "Do not copy the same concept into more than one field when several fields are present. " +
            "If the destination field is not in this request, keep the label in the field where it arrived. " +
            "Player count, input, co-op, achievements and controller support stay in features when features is in the request. Store genres stay in genres when genres is in the request. Theme and style stay in tags when tags is in the request.";

        public const string KnowledgePrompt =
            "No store returned a list for the fields in this request. Choose short labels from the game facts. Return only JSON, no markdown. " +
            "Response shape: {\"fields\":[{\"field\":\"genres\",\"terms\":[\"...\"]}]} " +
            "Echo each input field once. terms are the final labels in the requested language (the language field). " +
            "Write every common-noun label in that language; do not leave English genre, tag or feature wording when the language is not English. " +
            "game and editions identify which release this is: title, platform, library source, release date, developers, publishers, series, age ratings, description and links. " +
            "Use that release. Do not use a remake, port or edition on a different platform. " +
            "If the facts are not enough to tell which game this is, return an empty terms array. " +
            "Genres are store-style genres. Tags are themes and play style. " +
            "Features are how this release is played: player count, co-op and controller support. Do not copy a feature that belongs to another platform. " +
            "Do not put player count, controller support or achievements into genres or tags. Put those in features when features is in this request. If features is not in this request, leave them out. " +
            "Do not repeat a concept across fields. Keep one short label per concept. " +
            "mode overwrite: ignore existing. " +
            "mode append: keep unrelated existing labels and add labels for this release.";

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
                    resolved[field.Field] = field.FallbackTerms();
                }

                return false;
            }

            var returned = ReadFields(json);
            foreach (var field in requested)
            {
                List<string> terms;
                if (!returned.TryGetValue(field.Field, out terms) || !Accept(field, requested, terms))
                {
                    resolved[field.Field] = field.FallbackTerms();
                    continue;
                }

                resolved[field.Field] = DistinctTerms(terms).Take(Math.Max(1, field.MaxItems)).ToList();
            }

            return true;
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

            if (field.FromKnowledge)
            {
                if (cleaned.Count > Math.Max(1, field.MaxItems))
                {
                    return false;
                }

                return !string.Equals(field.Mode, "append", StringComparison.OrdinalIgnoreCase) || KeepsExisting(field, cleaned);
            }

            if (cleaned.Count > Math.Max(union.Count, Math.Max(1, field.MaxItems)))
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
            var fields = json["fields"] as JArray;
            if (fields == null)
            {
                return map;
            }

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

            return map;
        }

        private static bool TryParseObject(string content, out JObject json)
        {
            json = null;
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return false;
            }

            try
            {
                json = JObject.Parse(content.Substring(start, end - start + 1));
                return true;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return false;
            }
        }
    }
}
