using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            AddAlias(result, SearchTitle(value));
            return result;
        }

        /// <summary>
        /// Search uses the library title as written. Edition, year, region and
        /// remaster words stay in the query so a shorter alias cannot select another release.
        /// </summary>
        public static string SearchTitle(string value)
        {
            return Regex.Replace(value ?? string.Empty, "\\s+", " ").Trim();
        }

        /// <summary>
        /// Up to four IGDB search strings. The library title is first.
        /// Later queries only change separators or fill a roman-numeral range
        /// (IV-VI and IV•V•VI are the same release). Words are not removed.
        /// </summary>
        public static List<string> IgdbSearchQueries(string value)
        {
            var queries = new List<string>();
            var title = SearchTitle(value);
            AddSearchQuery(queries, title);
            var spaced = Regex.Replace(title, @"[_‐‑‒–—―\-]+", " ");
            spaced = Regex.Replace(spaced, "\\s+", " ").Trim();
            AddSearchQuery(queries, spaced);
            AddSearchQuery(queries, ExpandRomanSeparators(title));
            AddSearchQuery(queries, ExpandRomanSeparators(spaced));
            return queries;
        }

        /// <summary>
        /// Same release when the only differences are punctuation, a trailing year,
        /// or how a roman range is written (IV-VI vs IV•V•VI).
        /// </summary>
        public static bool IsSameReleaseTitle(string expected, string candidate)
        {
            var left = ReleaseKey(expected);
            var right = ReleaseKey(candidate);
            return left.Length > 0 && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
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

        private static void AddSearchQuery(List<string> queries, string value)
        {
            var cleaned = Regex.Replace(value ?? string.Empty, "\\s+", " ").Trim();
            if (cleaned.Length == 0 || queries.Count >= 4 ||
                queries.Any(x => string.Equals(x, cleaned, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            queries.Add(cleaned);
        }

        private static string ReleaseKey(string value)
        {
            var normalized = NormalizeTitle(ExpandRomanSeparators(value));
            return Regex.Replace(normalized, @"\s+(19|20)\d{2}$", string.Empty).Trim();
        }

        private static string ExpandRomanSeparators(string value)
        {
            var text = value ?? string.Empty;
            text = Regex.Replace(
                text,
                @"\b([IVXLCDM]{1,8})\s*[-‐‑‒–—―]\s*([IVXLCDM]{1,8})\b",
                match =>
                {
                    var expanded = ExpandRomanRange(match.Groups[1].Value, match.Groups[2].Value);
                    return expanded ?? match.Value;
                },
                RegexOptions.IgnoreCase);

            for (var pass = 0; pass < 6; pass++)
            {
                var next = Regex.Replace(
                    text,
                    @"\b([IVXLCDM]{1,8})\s*[•·∙]\s*([IVXLCDM]{1,8})\b",
                    "$1 $2",
                    RegexOptions.IgnoreCase);
                if (string.Equals(next, text, StringComparison.Ordinal))
                {
                    break;
                }

                text = next;
            }

            return text;
        }

        private static string ExpandRomanRange(string startText, string endText)
        {
            var start = RomanToInt(startText);
            var end = RomanToInt(endText);
            if (start < 1 || end <= start || end - start > 12)
            {
                return null;
            }

            var parts = new List<string>();
            for (var number = start; number <= end; number++)
            {
                parts.Add(ToRoman(number));
            }

            return string.Join(" ", parts);
        }

        private static int RomanToInt(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var value = text.Trim().ToUpperInvariant();
            var total = 0;
            var previous = 0;
            for (var index = value.Length - 1; index >= 0; index--)
            {
                int current;
                switch (value[index])
                {
                    case 'I': current = 1; break;
                    case 'V': current = 5; break;
                    case 'X': current = 10; break;
                    case 'L': current = 50; break;
                    case 'C': current = 100; break;
                    case 'D': current = 500; break;
                    case 'M': current = 1000; break;
                    default: return 0;
                }

                if (current < previous)
                {
                    total -= current;
                }
                else
                {
                    total += current;
                    previous = current;
                }
            }

            return total > 0 && total <= 39 && string.Equals(ToRoman(total), value, StringComparison.OrdinalIgnoreCase) ? total : 0;
        }

        private static string ToRoman(int number)
        {
            var map = new[]
            {
                new[] { "M", "1000" }, new[] { "CM", "900" }, new[] { "D", "500" }, new[] { "CD", "400" },
                new[] { "C", "100" }, new[] { "XC", "90" }, new[] { "L", "50" }, new[] { "XL", "40" },
                new[] { "X", "10" }, new[] { "IX", "9" }, new[] { "V", "5" }, new[] { "IV", "4" }, new[] { "I", "1" }
            };
            var result = new StringBuilder();
            foreach (var pair in map)
            {
                var arabic = int.Parse(pair[1]);
                while (number >= arabic)
                {
                    result.Append(pair[0]);
                    number -= arabic;
                }
            }

            return result.ToString();
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
