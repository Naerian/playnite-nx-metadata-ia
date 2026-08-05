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
            AddAlias(result, Regex.Replace(
                title,
                @"\s*\b(?:hd|remastered|remaster|remake|definitive|enhanced|anniversary|director'?s cut)\b.*$",
                string.Empty,
                RegexOptions.IgnoreCase));
            return result;
        }

        public static bool IsOrdinalVariant(string expected, string candidate)
        {
            var left = Tokens(expected);
            var right = Tokens(candidate);
            if (left.Count == 0 || right.Count == 0 || Math.Abs(left.Count - right.Count) != 1)
            {
                return false;
            }

            var shorter = left.Count < right.Count ? left : right;
            var longer = left.Count < right.Count ? right : left;
            for (var index = 0; index < longer.Count; index++)
            {
                if (!IsOrdinalToken(longer[index]))
                {
                    continue;
                }

                var withoutOrdinal = longer.Where((value, itemIndex) => itemIndex != index).ToList();
                if (withoutOrdinal.SequenceEqual(shorter, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string NormalizeTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            // Imports differ on apostrophes (Assassin's/Assassins, Clancy's/Clancys).
            // Removing an apostrophe between letters maps those spellings to one key.
            var comparable = Regex.Replace(value, @"(?<=\p{L})['’`´](?=\p{L})", string.Empty);
            comparable = Regex.Replace(comparable, @"[®™©]", string.Empty);
            var chars = comparable
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

        private static List<string> Tokens(string value)
        {
            return NormalizeTitle(value).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static bool IsOrdinalToken(string value)
        {
            int number;
            if (int.TryParse(value, out number))
            {
                return number > 0;
            }

            return !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(
                value,
                @"^m{0,3}(?:cm|cd|d?c{0,3})(?:xc|xl|l?x{0,3})(?:ix|iv|v?i{0,3})$",
                RegexOptions.IgnoreCase) && Regex.IsMatch(value, @"[ivxlcdm]", RegexOptions.IgnoreCase);
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
