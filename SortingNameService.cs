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
            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return string.Empty;
            }

            var current = Analyze(game.Name);
            var assignedSeries = GetAssignedSeriesName(api, game);
            if (current.Number > 0)
            {
                return Format(string.IsNullOrWhiteSpace(assignedSeries) ? current.BaseName : assignedSeries, current.Number);
            }

            if (!string.IsNullOrWhiteSpace(assignedSeries))
            {
                return Format(assignedSeries, GetSeriesOrder(api, game));
            }

            var allGames = api == null ? new List<Game>() : api.Database.Games.GetClone().ToList();
            var hasSequels = allGames
                .Where(x => x != null && x.Id != game.Id)
                .Select(x => Analyze(x.Name))
                .Any(x => x.Number > 1 && SameBase(x.BaseName, current.BaseName));

            return hasSequels ? Format(current.BaseName, 1) : string.Empty;
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

        private static int GetSeriesOrder(IPlayniteAPI api, Game game)
        {
            if (api == null || game == null || game.SeriesIds == null || game.SeriesIds.Count == 0)
            {
                return 1;
            }

            var seriesIds = new HashSet<Guid>(game.SeriesIds);
            var related = api.Database.Games.GetClone()
                .Where(x => x != null && x.SeriesIds != null && x.SeriesIds.Any(seriesIds.Contains))
                .OrderBy(x => x.ReleaseDate.HasValue ? x.ReleaseDate.Value.Year : int.MaxValue)
                .ThenBy(x => x.ReleaseDate.HasValue && x.ReleaseDate.Value.Month.HasValue ? x.ReleaseDate.Value.Month.Value : 13)
                .ThenBy(x => x.ReleaseDate.HasValue && x.ReleaseDate.Value.Day.HasValue ? x.ReleaseDate.Value.Day.Value : 32)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var index = related.FindIndex(x => x.Id == game.Id);
            return index < 0 ? 1 : index + 1;
        }

        private static string Format(string baseName, int number)
        {
            return (baseName ?? string.Empty).Trim() + " " + number.ToString("00", CultureInfo.InvariantCulture);
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
    }
}
