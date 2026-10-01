using System;
using System.Collections.Generic;
using System.Linq;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Soft preferred spellings for organize: only Playnite/session labels whose
    /// normalized key matches sanitized incoming. Playnite wins over session on
    /// the same key. No fuzzy / semantic merge; unrelated library tags stay out.
    /// </summary>
    public static class ScopedTermVocabulary
    {
        public static List<string> BuildPreferredSpellings(
            IEnumerable<string> incoming,
            IEnumerable<string> playniteLibraryNames,
            IEnumerable<string> sessionNames)
        {
            var incomingKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var term in StoreTermSanitizer.Sanitize(incoming))
            {
                var key = LibraryNameMatching.NormalizeKey(term);
                if (key.Length > 0)
                {
                    incomingKeys.Add(key);
                }
            }

            if (incomingKeys.Count == 0)
            {
                return new List<string>();
            }

            var byKey = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var name in playniteLibraryNames ?? Enumerable.Empty<string>())
            {
                TryAddPreferred(byKey, incomingKeys, name);
            }

            foreach (var name in sessionNames ?? Enumerable.Empty<string>())
            {
                TryAddPreferred(byKey, incomingKeys, name);
            }

            return byKey.Values.ToList();
        }

        private static void TryAddPreferred(
            Dictionary<string, string> byKey,
            HashSet<string> incomingKeys,
            string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var trimmed = name.Trim();
            var key = LibraryNameMatching.NormalizeKey(trimmed);
            if (key.Length == 0 || !incomingKeys.Contains(key) || byKey.ContainsKey(key))
            {
                return;
            }

            byKey[key] = trimmed;
        }

        public static Dictionary<string, List<string>> BuildForFields(
            IEnumerable<TermFieldRequest> fields,
            Func<string, IEnumerable<string>> playniteNamesForField,
            BatchTermSessionCache sessionCache)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (fields == null)
            {
                return result;
            }

            foreach (var field in fields)
            {
                if (field == null || string.IsNullOrWhiteSpace(field.Field))
                {
                    continue;
                }

                var preferred = BuildPreferredSpellings(
                    field.Incoming,
                    playniteNamesForField == null ? null : playniteNamesForField(field.Field),
                    sessionCache == null ? null : sessionCache.GetPreferred(field.Field));

                if (preferred.Count == 0)
                {
                    continue;
                }

                result[field.Field] = preferred;
            }

            return result;
        }
    }
}
