using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    public static class SortingNameService
    {
        private static readonly Dictionary<string, int> RomanValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "I", 1 }, { "II", 2 }, { "III", 3 }, { "IV", 4 }, { "V", 5 },
            { "VI", 6 }, { "VII", 7 }, { "VIII", 8 }, { "IX", 9 }, { "X", 10 },
            { "XI", 11 }, { "XII", 12 }, { "XIII", 13 }, { "XIV", 14 }, { "XV", 15 }
        };

        public static string Generate(IPlayniteAPI api, Game game)
        {
            return Generate(api, game, null);
        }

        public static string Generate(IPlayniteAPI api, Game game, SeriesOrderLookupResult verifiedOrder)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return string.Empty;
            }

            var current = Analyze(game.Name);
            var assignedSeries = GetAssignedSeriesName(api, game);
            if (verifiedOrder != null && verifiedOrder.Order > 0)
            {
                var verifiedSeries = string.IsNullOrWhiteSpace(verifiedOrder.SeriesName) ? assignedSeries : verifiedOrder.SeriesName;
                if (!string.IsNullOrWhiteSpace(verifiedSeries))
                {
                    return Format(api, game, verifiedSeries, verifiedOrder.Order, game.Name);
                }
            }

            if (current.Number > 0)
            {
                return Format(api, game, string.IsNullOrWhiteSpace(assignedSeries) ? current.BaseName : assignedSeries, current.Number, game.Name);
            }

            if (!string.IsNullOrWhiteSpace(assignedSeries))
            {
                // A series assignment proves membership, but not the ordinal. Numbering only
                // the games present in the library compresses gaps (for example 2 and 5 into 1 and 2).
                return string.Empty;
            }

            var allGames = api == null ? new List<Game>() : api.Database.Games.GetClone().ToList();
            var hasSequels = allGames
                .Where(x => x != null && x.Id != game.Id)
                .Select(x => Analyze(x.Name))
                .Any(x => x.Number > 1 && SameBase(x.BaseName, current.BaseName));

            return hasSequels ? Format(api, game, current.BaseName, 1, game.Name) : string.Empty;
        }

        public static string GenerateSeriesName(IPlayniteAPI api, Game game)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return string.Empty;
            }

            var current = Analyze(game.Name);
            var assignedSeries = GetAssignedSeriesName(api, game);
            if (!string.IsNullOrWhiteSpace(assignedSeries))
            {
                return assignedSeries;
            }

            if (current.Number > 0)
            {
                return current.BaseName;
            }

            var allGames = api == null ? new List<Game>() : api.Database.Games.GetClone().ToList();
            var hasNumberedEntry = allGames
                .Where(x => x != null && x.Id != game.Id)
                .Select(x => Analyze(x.Name))
                .Any(x => x.Number > 0 && SameBase(x.BaseName, current.BaseName));

            return hasNumberedEntry ? current.BaseName : string.Empty;
        }

        public static bool HasSeriesEvidence(IPlayniteAPI api, Game game)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(GetAssignedSeriesName(api, game)))
            {
                return true;
            }

            if (api == null)
            {
                return false;
            }

            var current = Analyze(game.Name);
            var related = api.Database.Games.GetClone()
                .Where(x => x != null && x.Id != game.Id)
                .Select(x => Analyze(x.Name))
                .Where(x => SameBase(x.BaseName, current.BaseName))
                .ToList();

            if (current.Number > 0)
            {
                return related.Any();
            }

            return related.Any(x => x.Number > 0);
        }

        private static string GetAssignedSeriesName(IPlayniteAPI api, Game game)
        {
            if (game == null || game.SeriesIds == null || game.SeriesIds.Count == 0)
            {
                return string.Empty;
            }

            var firstSeries = game.Series == null ? null : game.Series.FirstOrDefault(x => x != null && !string.IsNullOrWhiteSpace(x.Name));
            if (firstSeries != null)
            {
                return firstSeries.Name.Trim();
            }

            if (api == null)
            {
                return string.Empty;
            }

            var series = api.Database.Series.Get(game.SeriesIds[0]);
            return series == null || string.IsNullOrWhiteSpace(series.Name) ? string.Empty : series.Name.Trim();
        }

        private static string Format(IPlayniteAPI api, Game game, string baseName, int number, string gameName)
        {
            var siblings = SiblingSortingNames(api, game, baseName);
            return FormatWithLibraryPattern(baseName, number, gameName, siblings);
        }

        /// <summary>
        /// Copies the sorting-name shape already used by the rest of the series
        /// (pad width and whether the full title is appended). No series-specific rules.
        /// </summary>
        internal static string FormatWithLibraryPattern(string baseName, int number, string gameName, IEnumerable<string> siblingSortingNames)
        {
            var pattern = InferPattern(baseName, siblingSortingNames);
            var width = Math.Max(pattern.PadWidth, number.ToString(CultureInfo.InvariantCulture).Length);
            var prefix = pattern.Prefix.Trim() + " " + number.ToString(new string('0', width), CultureInfo.InvariantCulture);
            if (!pattern.AppendGameName || string.IsNullOrWhiteSpace(gameName))
            {
                return prefix;
            }

            return prefix + " - " + gameName.Trim();
        }

        private static List<string> SiblingSortingNames(IPlayniteAPI api, Game game, string seriesName)
        {
            if (api == null || api.Database == null || game == null)
            {
                return new List<string>();
            }

            return api.Database.Games
                .Where(other => other != null && other.Id != game.Id && SharesSeries(game, other, seriesName))
                .Select(other => other.SortingName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .ToList();
        }

        private static bool SharesSeries(Game game, Game other, string seriesName)
        {
            if (game.SeriesIds != null && other.SeriesIds != null &&
                game.SeriesIds.Any(id => id != Guid.Empty && other.SeriesIds.Contains(id)))
            {
                return true;
            }

            var otherParts = Analyze(other.Name);
            return SameBase(otherParts.BaseName, seriesName) || SameBase(otherParts.BaseName, Analyze(game.Name).BaseName);
        }

        private static SortPattern InferPattern(string baseName, IEnumerable<string> siblingSortingNames)
        {
            var samples = new List<SortSample>();
            foreach (var sortingName in siblingSortingNames ?? Enumerable.Empty<string>())
            {
                var sample = ParseSortingName(sortingName);
                if (sample != null && SameBase(sample.Prefix, baseName))
                {
                    samples.Add(sample);
                }
            }

            if (samples.Count == 0)
            {
                return new SortPattern((baseName ?? string.Empty).Trim(), 2, false);
            }

            var prefix = samples
                .GroupBy(x => x.Prefix, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => x.Count())
                .ThenByDescending(x => x.Key.Length)
                .First().Key;
            var padWidth = samples
                .GroupBy(x => x.PadWidth)
                .OrderByDescending(x => x.Count())
                .ThenByDescending(x => x.Key)
                .First().Key;
            var appendGameName = samples.Count(x => x.HasSuffix) > samples.Count(x => !x.HasSuffix);
            return new SortPattern(prefix, padWidth, appendGameName);
        }

        private static SortSample ParseSortingName(string sortingName)
        {
            var match = Regex.Match(
                (sortingName ?? string.Empty).Trim(),
                @"^(?<prefix>.+?)\s+(?<digits>0*\d{1,4})(?:\s+-\s+(?<suffix>.+))?$");
            if (!match.Success)
            {
                return null;
            }

            var digits = match.Groups["digits"].Value;
            int number;
            if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) || number <= 0)
            {
                return null;
            }

            return new SortSample(
                match.Groups["prefix"].Value.Trim(),
                Math.Max(1, digits.Length),
                match.Groups["suffix"].Success && !string.IsNullOrWhiteSpace(match.Groups["suffix"].Value));
        }

        private static bool SameBase(string left, string right)
        {
            return string.Equals(NormalizeKey(left), NormalizeKey(right), StringComparison.OrdinalIgnoreCase);
        }

        private static SortParts Analyze(string name)
        {
            var clean = Regex.Replace(name ?? string.Empty, @"\s+", " ").Trim();
            clean = RemoveEditionNoise(clean);
            var beforeSubtitle = Regex.Split(clean, @"\s*[:\-]\s+").FirstOrDefault() ?? clean;

            var version = Regex.Match(beforeSubtitle, @"^(?<base>.+?)\s+v(?<num>\d{1,2})$", RegexOptions.IgnoreCase);
            if (version.Success)
            {
                return new SortParts(CleanBase(version.Groups["base"].Value), int.Parse(version.Groups["num"].Value, CultureInfo.InvariantCulture));
            }

            var arabic = Regex.Match(beforeSubtitle, @"^(?<base>.+?)\s+(?<num>\d{1,2})$", RegexOptions.IgnoreCase);
            if (arabic.Success)
            {
                return new SortParts(CleanBase(arabic.Groups["base"].Value), int.Parse(arabic.Groups["num"].Value, CultureInfo.InvariantCulture));
            }

            var roman = Regex.Match(beforeSubtitle, @"^(?<base>.+?)\s+(?<roman>I|II|III|IV|V|VI|VII|VIII|IX|X|XI|XII|XIII|XIV|XV)$", RegexOptions.IgnoreCase);
            if (roman.Success)
            {
                int value;
                if (RomanValues.TryGetValue(roman.Groups["roman"].Value, out value))
                {
                    return new SortParts(CleanBase(roman.Groups["base"].Value), value);
                }
            }

            return new SortParts(CleanBase(beforeSubtitle), 0);
        }

        private static string RemoveEditionNoise(string value)
        {
            var cleaned = Regex.Replace(
                value ?? string.Empty,
                @"\s*[\(\[](?:\d{4}|classic|original|legacy|remastered|remake|definitive|complete|ultimate|deluxe|goty|game of the year|director'?s cut|special edition|anniversary edition|enhanced edition)[\)\]]\s*$",
                string.Empty,
                RegexOptions.IgnoreCase).Trim();
            return Regex.Replace(
                cleaned,
                @"\s*\b(Game of the Year|GOTY|Definitive|Complete|Ultimate|Deluxe|Remastered|Remake|Director'?s Cut|Special Edition|Anniversary Edition|Enhanced Edition)\b.*$",
                string.Empty,
                RegexOptions.IgnoreCase).Trim();
        }

        private static string CleanBase(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim(' ', '-', ':');
        }

        private static string NormalizeKey(string value)
        {
            var normalized = RemoveDiacritics(value ?? string.Empty).ToLowerInvariant();
            normalized = Regex.Replace(normalized, @"\b(the|a|an|el|la|los|las|un|una)\b", " ");
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

        private class SortParts
        {
            public string BaseName { get; private set; }
            public int Number { get; private set; }

            public SortParts(string baseName, int number)
            {
                BaseName = baseName;
                Number = number;
            }
        }

        private class SortPattern
        {
            public string Prefix { get; private set; }
            public int PadWidth { get; private set; }
            public bool AppendGameName { get; private set; }

            public SortPattern(string prefix, int padWidth, bool appendGameName)
            {
                Prefix = prefix;
                PadWidth = Math.Max(1, padWidth);
                AppendGameName = appendGameName;
            }
        }

        private class SortSample
        {
            public string Prefix { get; private set; }
            public int PadWidth { get; private set; }
            public bool HasSuffix { get; private set; }

            public SortSample(string prefix, int padWidth, bool hasSuffix)
            {
                Prefix = prefix;
                PadWidth = padWidth;
                HasSuffix = hasSuffix;
            }
        }
    }
}
