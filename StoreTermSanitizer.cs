using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Cheap C# pre-filter for store/IGDB/Wikidata list labels: drop emulation/hardware
    /// noise and strip launcher brand wrappers. Does not translate or alias concepts.
    /// </summary>
    public static class StoreTermSanitizer
    {
        private static readonly HashSet<string> NoiseExactKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            LibraryNameMatching.NormalizeKey("ROM dump"),
            LibraryNameMatching.NormalizeKey("Parent ROM"),
            LibraryNameMatching.NormalizeKey("Clone ROM"),
            LibraryNameMatching.NormalizeKey("Device ROM"),
            LibraryNameMatching.NormalizeKey("CHD"),
            LibraryNameMatching.NormalizeKey("CHD dump"),
            LibraryNameMatching.NormalizeKey("JAMMA"),
            LibraryNameMatching.NormalizeKey("JAMMA PCB"),
            LibraryNameMatching.NormalizeKey("Arcade PCB"),
            LibraryNameMatching.NormalizeKey("Arcade cabinet"),
            LibraryNameMatching.NormalizeKey("MAME"),
            LibraryNameMatching.NormalizeKey("BIOS"),
            LibraryNameMatching.NormalizeKey("Emulator"),
            LibraryNameMatching.NormalizeKey("Emulation")
        };

        // Prefix: "Steam Achievements", "Epic Games Store Cloud", "Xbox Achievements"
        private static readonly Regex BrandPrefix = new Regex(
            @"^(?:Steam|Epic(?:\s+Games(?:\s+Store)?)?|GOG(?:\.com)?|Xbox|PlayStation|PSN|Ubisoft(?:\s+Connect)?|Origin|EA(?:\s+App)?|Battle\.?net|Nintendo)\s+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Suffix: "Logros de Steam", "Nube de Steam", "Achievements of Steam", "Cloud (Steam)"
        private static readonly Regex BrandSuffix = new Regex(
            @"\s+(?:de\s+|of\s+|del\s+)?(?:Steam|Epic(?:\s+Games)?|GOG(?:\.com)?|Xbox|PlayStation|PSN|Ubisoft(?:\s+Connect)?|Origin|EA(?:\s+App)?|Battle\.?net|Nintendo)\s*$|" +
            @"\s*[\(\[]\s*(?:Steam|Epic|GOG|Xbox|PlayStation|PSN|Ubisoft|Origin|EA|Battle\.?net|Nintendo)\s*[\)\]]\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsTechnicalNoise(string term)
        {
            var key = LibraryNameMatching.NormalizeKey(term);
            return key.Length > 0 && NoiseExactKeys.Contains(key);
        }

        public static string StripStoreBrand(string term)
        {
            var text = VocabularyTermNormalizer.CleanTerm(term);
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var previous = string.Empty;
            while (!string.Equals(previous, text, StringComparison.Ordinal))
            {
                previous = text;
                text = BrandPrefix.Replace(text, string.Empty).Trim();
                text = BrandSuffix.Replace(text, string.Empty).Trim();
                text = Regex.Replace(text, @"\s+", " ").Trim();
            }

            return text;
        }

        public static string SanitizeOne(string term)
        {
            if (IsTechnicalNoise(term))
            {
                return string.Empty;
            }

            var stripped = StripStoreBrand(term);
            if (string.IsNullOrWhiteSpace(stripped) || IsTechnicalNoise(stripped))
            {
                return string.Empty;
            }

            return stripped;
        }

        public static List<string> Sanitize(IEnumerable<string> terms)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in terms ?? Enumerable.Empty<string>())
            {
                var cleaned = SanitizeOne(raw);
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    continue;
                }

                var key = LibraryNameMatching.NormalizeKey(cleaned);
                if (key.Length == 0 || !seen.Add(key))
                {
                    continue;
                }

                result.Add(cleaned);
            }

            return result;
        }
    }
}
