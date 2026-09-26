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
            // Keep user prefixes like "[MAI] Open world". Only unwrap when the whole
            // term is wrapped in one pair of quotes or brackets: "Action", (Indie), [RPG].
            string marker;
            string body;
            string separator;
            if (TrySplitLeadingMarker(text, out marker, out separator, out body))
            {
                body = TrimDecorativeEdges(body);
                return string.IsNullOrWhiteSpace(body) ? marker : marker + separator + body;
            }

            return TrimDecorativeEdges(text);
        }

        /// <summary>
        /// Splits a leading "[MAI]" / "(meta)" / "{ai}" marker from the rest of the label.
        /// Returns false when the whole value is only the wrapped term (unwrap that instead).
        /// </summary>
        public static bool TrySplitLeadingMarker(string value, out string marker, out string separator, out string body)
        {
            marker = string.Empty;
            separator = string.Empty;
            body = string.Empty;
            var text = value ?? string.Empty;
            var match = Regex.Match(text, @"^(?<marker>\[[^\]]+\]|\([^\)]+\)|\{[^\}]+\})(?<sep>\s*)(?<body>.+)$");
            if (!match.Success)
            {
                return false;
            }

            marker = match.Groups["marker"].Value;
            separator = match.Groups["sep"].Value;
            body = match.Groups["body"].Value;
            return !string.IsNullOrWhiteSpace(body);
        }

        private static string TrimDecorativeEdges(string value)
        {
            var text = (value ?? string.Empty).Trim();
            // Whole-term wrappers only (balanced), then leftover edge punctuation.
            while (text.Length >= 2)
            {
                var first = text[0];
                var last = text[text.Length - 1];
                if ((first == '"' && last == '"') ||
                    (first == '\'' && last == '\'') ||
                    (first == '(' && last == ')') ||
                    (first == '[' && last == ']') ||
                    (first == '{' && last == '}'))
                {
                    text = text.Substring(1, text.Length - 2).Trim();
                    continue;
                }

                break;
            }

            text = text.Trim(' ', '.', ';', ':', '-', '/', '\\', '"', '\'');
            return Regex.Replace(text, @"\s+", " ").Trim();
        }
    }
}
