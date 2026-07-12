using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    internal static class TitleMatchingService
    {
        public static bool IsReliableMatch(string expected, string candidate)
        {
            var left = NormalizeTitle(expected);
            var right = NormalizeTitle(candidate);
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
                   HasOnlyAllowedStoreSuffix(left, right) ||
                   HasOnlyAllowedStoreSuffix(right, left);
        }

        public static List<string> BuildAliases(string value)
        {
            var result = new List<string>();
            AddAlias(result, value);

            var title = value ?? string.Empty;
            title = Regex.Replace(title, "\\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return result;
            }

            AddAlias(result, Regex.Replace(
                title,
                "\\s*[\\(\\[](?:\\d{4}|classic|original|legacy|[^\\)\\]]*(?:edition|deluxe|standard|ultimate|goty|game of the year|complete|collector|collectors|premium|gold|digital)[^\\)\\]]*)[\\)\\]]\\s*$",
                string.Empty,
                RegexOptions.IgnoreCase));

            var editionWords = "(?:digital\\s+)?(?:standard|deluxe|ultimate|goty|game\\s+of\\s+the\\s+year|complete|collector|collectors|premium|gold|special|limited)(?:\\s+edition)?";
            AddAlias(result, Regex.Replace(title, "\\s*[:\\-\\u2013\\u2014]\\s*" + editionWords + "\\s*$", string.Empty, RegexOptions.IgnoreCase));
            AddAlias(result, Regex.Replace(title, "\\s+" + editionWords + "\\s*$", string.Empty, RegexOptions.IgnoreCase));
            AddSeriesNumberAliases(result, title);

            return result;
        }

        public static string NormalizeTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var chars = value
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
                .ToArray();
            return string.Join(" ", new string(chars).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static bool HasOnlyAllowedStoreSuffix(string baseTitle, string fullTitle)
        {
            if (string.IsNullOrWhiteSpace(baseTitle) || string.IsNullOrWhiteSpace(fullTitle) ||
                !fullTitle.StartsWith(baseTitle + " ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var suffix = fullTitle.Substring(baseTitle.Length).Trim();
            if (string.IsNullOrWhiteSpace(suffix))
            {
                return false;
            }

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "standard",
                "edition",
                "base",
                "game",
                "digital",
                "version",
                "classic",
                "original",
                "legacy",
                "hd",
                "ps4",
                "ps5",
                "xbox",
                "one",
                "series",
                "x",
                "s",
                "windows",
                "pc"
            };

            return suffix
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .All(x => allowed.Contains(x));
        }

        private static void AddSeriesNumberAliases(List<string> result, string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            var words = title.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 3 || words.Any(IsNumberToken))
            {
                return;
            }

            var suffix = words[words.Length - 1];
            if (suffix.Length < 4 || IsGenericTrailingToken(suffix))
            {
                return;
            }

            var prefix = string.Join(" ", words.Take(words.Length - 1));
            foreach (var roman in new[] { "II", "III", "IV", "V" })
            {
                AddAlias(result, prefix + " " + roman + " " + suffix);
            }

            AddAlias(result, title + " HD");
        }

        private static bool IsNumberToken(string value)
        {
            var normalized = NormalizeTitle(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return Regex.IsMatch(normalized, "^\\d+$") ||
                   Regex.IsMatch(normalized, "^(?:i|ii|iii|iv|v|vi|vii|viii|ix|x)$", RegexOptions.IgnoreCase);
        }

        private static bool IsGenericTrailingToken(string value)
        {
            var normalized = NormalizeTitle(value);
            return string.Equals(normalized, "edition", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "collection", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "bundle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "pack", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "game", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddAlias(List<string> result, string value)
        {
            if (result == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var cleaned = Regex.Replace(value, "\\s+", " ").Trim();
            if (cleaned.Length == 0)
            {
                return;
            }

            var normalized = NormalizeTitle(cleaned);
            if (string.IsNullOrWhiteSpace(normalized) || result.Any(x => string.Equals(NormalizeTitle(x), normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            result.Add(cleaned);
        }
    }
}
