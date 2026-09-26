using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    public static class TitleMatchingService
    {
        public static bool IsReliableMatch(string expected, string candidate)
        {
            var left = NormalizeTitle(expected);
            var right = NormalizeTitle(candidate);
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            var comparableLeft = WithoutLeadingArticle(left);
            var comparableRight = WithoutLeadingArticle(right);
            return string.Equals(comparableLeft, comparableRight, StringComparison.OrdinalIgnoreCase) ||
                   HasOnlyAllowedStoreSuffix(comparableLeft, comparableRight) ||
                   HasOnlyAllowedStoreSuffix(comparableRight, comparableLeft);
        }

        /// <summary>
        /// A store label fits the game when they share a platform token
        /// (nintendo, windows). No shared token means a different release.
        /// Missing platform data does not reject the label.
        /// </summary>
        public static bool PlatformLabelsFit(IEnumerable<string> gameNames, IEnumerable<string> specificationIds, IEnumerable<string> storeLabels)
        {
            var gameTokens = PlatformTokens(gameNames).Concat(PlatformTokens(specificationIds)).Distinct(StringComparer.Ordinal).ToList();
            if (gameTokens.Count == 0)
            {
                return true;
            }

            var labels = (storeLabels ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (labels.Count == 0)
            {
                return true;
            }

            var storeTokens = PlatformTokens(labels).Distinct(StringComparer.Ordinal).ToList();
            if (storeTokens.Count == 0)
            {
                return true;
            }

            return gameTokens.Any(token => storeTokens.Contains(token));
        }

        private static IEnumerable<string> PlatformTokens(IEnumerable<string> values)
        {
            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                foreach (var token in NormalizeTitle(value).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.Length >= 3)
                    {
                        yield return token;
                    }
                }
            }
        }

        private static string WithoutLeadingArticle(string normalized)
        {
            if (string.IsNullOrEmpty(normalized))
            {
                return normalized ?? string.Empty;
            }

            if (normalized.StartsWith("the ", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(4);
            }

            if (normalized.EndsWith(" the", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 4).Trim();
            }

            return normalized;
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

            // Library imports often preserve machine-readable separators (for example
            // Watch_Dogs). IGDB search treats those less consistently than spaces even
            // though both spellings normalize to the same title locally.
            AddAlias(result, Regex.Replace(title, "[_\\-]+", " "));

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

            var stripped = title;
            string previous;
            do
            {
                previous = stripped;
                stripped = Regex.Replace(
                    stripped,
                    @"\s*[\[\(]([^\]\)]{1,40})[\]\)]\s*$",
                    match => IsLibraryDecoration(match.Groups[1].Value) ? string.Empty : match.Value,
                    RegexOptions.IgnoreCase).Trim();
            }
            while (!string.Equals(stripped, previous, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(stripped));

            AddAlias(result, stripped);
            return result;
        }

        public static string SearchTitle(string value)
        {
            var best = BuildAliases(value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderBy(x => x.Length)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(best) ? (value ?? string.Empty).Trim() : best.Trim();
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
                "pc",
                "usa",
                "europe",
                "japan",
                "world",
                "asia",
                "korea",
                "australia",
                "brazil",
                "spain",
                "france",
                "germany",
                "italy",
                "canada",
                "uk",
                "pal",
                "ntsc",
                "en",
                "fr",
                "de",
                "es",
                "it",
                "ja",
                "proto",
                "beta",
                "demo",
                "unl"
            };

            return suffix
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(x => !string.Equals(x, "the", StringComparison.Ordinal))
                .All(x => allowed.Contains(x));
        }

        private static List<string> Tokens(string value)
        {
            return NormalizeTitle(value).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static bool IsLibraryDecoration(string inner)
        {
            var parts = (inner ?? string.Empty).Split(new[] { ',', '/', '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            return parts.All(part => Regex.IsMatch(
                part.Trim(),
                @"^(usa|u\.s\.a|europe|japan|world|asia|korea|australia|brazil|spain|france|germany|italy|netherlands|sweden|canada|uk|pal|ntsc|en|fr|de|es|it|ja|ko|zh|pt|nl|sv|proto|beta|demo|sample|unl|pirate|rev(\s*[a-z0-9])?|v\d+(\.\d+)?|disc\s*\d+|b\d*|!)$",
                RegexOptions.IgnoreCase));
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
