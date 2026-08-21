using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Maps AI-proposed names onto names that already exist in the Playnite library
    /// (exact, normalized, or shared alias group). Used when PreferExisting* is enabled.
    /// </summary>
    public static class LibraryNameMatching
    {
        public static List<string> MapToExisting(IEnumerable<string> proposed, IEnumerable<string> knownNames, Dictionary<string, string> aliases)
        {
            var known = (knownNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var mapped = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in proposed ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var match = FindExisting(name.Trim(), known, aliases);
                if (match == null || !seen.Add(match))
                {
                    continue;
                }

                mapped.Add(match);
            }

            return mapped;
        }

        public static string FindExisting(string proposed, IEnumerable<string> knownNames, Dictionary<string, string> aliases)
        {
            if (string.IsNullOrWhiteSpace(proposed))
            {
                return null;
            }

            var known = knownNames == null
                ? new List<string>()
                : knownNames.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

            var exact = known.FirstOrDefault(x => string.Equals(x, proposed, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }

            var proposedKey = NormalizeKey(proposed);
            var byKey = known.FirstOrDefault(x => string.Equals(NormalizeKey(x), proposedKey, StringComparison.Ordinal));
            if (byKey != null)
            {
                return byKey;
            }

            var proposedGroup = AliasGroup(proposed, aliases);
            var byAlias = known.FirstOrDefault(x => string.Equals(AliasGroup(x, aliases), proposedGroup, StringComparison.Ordinal));
            if (byAlias != null)
            {
                return byAlias;
            }

            return null;
        }

        public static Dictionary<string, string> AliasesForField(string field)
        {
            if (string.Equals(field, "genres", StringComparison.OrdinalIgnoreCase))
            {
                return AiMetadataResult.GenreAliases;
            }

            if (string.Equals(field, "tags", StringComparison.OrdinalIgnoreCase))
            {
                return AiMetadataResult.TagAliases;
            }

            if (string.Equals(field, "features", StringComparison.OrdinalIgnoreCase))
            {
                return AiMetadataResult.FeatureAliases;
            }

            if (string.Equals(field, "categories", StringComparison.OrdinalIgnoreCase))
            {
                return AiMetadataResult.CategoryAliases;
            }

            if (string.Equals(field, "ageRatings", StringComparison.OrdinalIgnoreCase))
            {
                return AiMetadataResult.AgeRatingAliases;
            }

            return null;
        }

        private static string AliasGroup(string value, Dictionary<string, string> aliases)
        {
            var key = NormalizeKey(value);
            if (aliases != null)
            {
                string canonical;
                if (aliases.TryGetValue(key, out canonical))
                {
                    return NormalizeKey(canonical);
                }
            }

            return key;
        }

        internal static string NormalizeKey(string value)
        {
            var normalized = RemoveDiacritics(value ?? string.Empty).ToLowerInvariant();
            normalized = Regex.Replace(normalized, @"[^a-z0-9]+", " ").Trim();
            return Regex.Replace(normalized, @"\s+", " ");
        }

        private static string RemoveDiacritics(string value)
        {
            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
