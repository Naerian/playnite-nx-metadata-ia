using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Cleans list terms and reuses an existing Playnite name when it is the same
    /// aside from case, accents and punctuation. Does not translate or alias terms.
    /// </summary>
    public static class VocabularyTermNormalizer
    {
        public static List<string> NormalizeField(
            IEnumerable<string> proposed,
            string field,
            string language,
            IEnumerable<string> libraryNames,
            IEnumerable<string> learnedVocabulary,
            int maxItems,
            bool preferExistingOnly)
        {
            var library = (libraryNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolved = new List<string>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in proposed ?? Enumerable.Empty<string>())
            {
                var cleaned = CleanTerm(raw);
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    continue;
                }

                var existing = LibraryNameMatching.FindExisting(cleaned, library);
                if (existing == null && preferExistingOnly)
                {
                    continue;
                }

                var chosen = existing ?? cleaned;
                var key = LibraryNameMatching.NormalizeKey(chosen);
                if (string.IsNullOrWhiteSpace(key) || !seenKeys.Add(key))
                {
                    continue;
                }

                resolved.Add(chosen);
                if (resolved.Count >= Math.Max(1, maxItems))
                {
                    break;
                }
            }

            return resolved;
        }

        public static bool AreEquivalent(string left, string right, string field, string language)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(
                LibraryNameMatching.NormalizeKey(left),
                LibraryNameMatching.NormalizeKey(right),
                StringComparison.Ordinal);
        }

        public static string CleanTerm(string value)
        {
            var text = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            text = text.Trim(' ', '.', ';', ':', '-', '/', '\\', '(', ')', '[', ']', '{', '}', '"', '\'');
            return Regex.Replace(text, @"\s+", " ").Trim();
        }
    }
}
