using System;
using System.Collections.Generic;
using System.Linq;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Intra-batch preferred spellings harvested only from official store lists that
    /// already match the plugin language. Knowledge / IGDB inventiones never enter.
    /// Lives for one batch run; discarded when the progress dialog finishes.
    /// </summary>
    public sealed class BatchTermSessionCache
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, Dictionary<string, string>> byField =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public void RememberLocalizedStoreTerms(string field, IEnumerable<string> terms)
        {
            if (string.IsNullOrWhiteSpace(field) || terms == null)
            {
                return;
            }

            var sanitized = StoreTermSanitizer.Sanitize(terms);
            if (sanitized.Count == 0)
            {
                return;
            }

            lock (gate)
            {
                Dictionary<string, string> map;
                if (!byField.TryGetValue(field, out map))
                {
                    map = new Dictionary<string, string>(StringComparer.Ordinal);
                    byField[field] = map;
                }

                foreach (var term in sanitized)
                {
                    var key = LibraryNameMatching.NormalizeKey(term);
                    if (key.Length == 0 || map.ContainsKey(key))
                    {
                        continue;
                    }

                    map[key] = term;
                }
            }
        }

        public IReadOnlyList<string> GetPreferred(string field)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                return new List<string>();
            }

            lock (gate)
            {
                Dictionary<string, string> map;
                if (!byField.TryGetValue(field, out map) || map.Count == 0)
                {
                    return new List<string>();
                }

                return map.Values.ToList();
            }
        }

        public Dictionary<string, List<string>> Snapshot()
        {
            lock (gate)
            {
                return byField.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Values.ToList(),
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Soft prefer: if a proposed term's normalized key matches a session spelling,
        /// reuse that exact string. Unknown keys pass through unchanged.
        /// </summary>
        public List<string> PreferSpellings(string field, IEnumerable<string> proposed)
        {
            var preferred = GetPreferred(field);
            var cleaned = StoreTermSanitizer.Sanitize(proposed);
            if (preferred.Count == 0)
            {
                return cleaned;
            }

            return VocabularyTermNormalizer.NormalizeField(
                cleaned,
                field,
                null,
                preferred,
                null,
                Math.Max(1, cleaned.Count + preferred.Count),
                false);
        }
    }
}
