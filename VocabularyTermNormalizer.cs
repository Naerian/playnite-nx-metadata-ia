using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Resolves proposed list terms against the plugin canonical vocabulary first,
    /// then against existing Playnite library names, otherwise keeps a cleaned new term.
    /// Dirty launcher spellings that alias a canonical term never become the preferred spelling.
    /// </summary>
    public static class VocabularyTermNormalizer
    {
        private static readonly HashSet<string> NoiseTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "al", "de", "del", "el", "la", "las", "los", "en", "y", "o", "the", "and", "or", "of", "for", "with",
            "steam", "xbox", "playstation", "epic", "gog", "full", "partial", "support", "compatible", "compatibilidad"
        };

        public static List<string> NormalizeField(
            IEnumerable<string> proposed,
            string field,
            string language,
            IEnumerable<string> libraryNames,
            IEnumerable<string> learnedVocabulary,
            int maxItems,
            bool preferExistingOnly)
        {
            var terms = CanonicalVocabulary.GetFieldTerms(language, field);
            if (learnedVocabulary != null)
            {
                foreach (var learned in learnedVocabulary.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    if (terms.Any(t => string.Equals(t.Preferred, learned.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    terms.Add(new CanonicalVocabulary.Term(learned.Trim()));
                }
            }

            var library = (libraryNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolved = new List<string>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in proposed ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var chosen = ResolveOne(raw.Trim(), terms, library, preferExistingOnly);
                if (string.IsNullOrWhiteSpace(chosen))
                {
                    continue;
                }

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

        public static string ResolveOne(
            string proposed,
            IList<CanonicalVocabulary.Term> terms,
            IEnumerable<string> libraryNames,
            bool preferExistingOnly)
        {
            if (string.IsNullOrWhiteSpace(proposed))
            {
                return null;
            }

            var library = (libraryNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var canonical = FindCanonical(proposed, terms);
            if (canonical != null)
            {
                var cleanLibrary = FindCleanLibraryMatch(canonical, library);
                // Canonical wins over dirty launcher spellings already in Playnite.
                // PreferExisting must not freeze those spellings as the library authority.
                return cleanLibrary ?? canonical.Preferred;
            }

            var existing = LibraryNameMatching.FindExisting(proposed, library);
            if (existing != null)
            {
                // If that library entry itself aliases a canonical term, promote to preferred spelling.
                var libraryCanonical = FindCanonical(existing, terms);
                if (libraryCanonical != null)
                {
                    var clean = FindCleanLibraryMatch(libraryCanonical, library);
                    return clean ?? libraryCanonical.Preferred;
                }

                return existing;
            }

            if (preferExistingOnly)
            {
                return null;
            }

            return CleanNewTerm(proposed);
        }

        public static bool AreEquivalent(string left, string right, string field, string language)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            if (string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(LibraryNameMatching.NormalizeKey(left), LibraryNameMatching.NormalizeKey(right), StringComparison.Ordinal))
            {
                return true;
            }

            var terms = CanonicalVocabulary.GetFieldTerms(language, field);
            var leftCanonical = FindCanonical(left, terms);
            var rightCanonical = FindCanonical(right, terms);
            return leftCanonical != null && rightCanonical != null &&
                   string.Equals(leftCanonical.Preferred, rightCanonical.Preferred, StringComparison.OrdinalIgnoreCase);
        }

        private static CanonicalVocabulary.Term FindCanonical(string proposed, IList<CanonicalVocabulary.Term> terms)
        {
            if (terms == null || terms.Count == 0 || string.IsNullOrWhiteSpace(proposed))
            {
                return null;
            }

            var proposedKey = LibraryNameMatching.NormalizeKey(proposed);
            if (string.IsNullOrWhiteSpace(proposedKey))
            {
                return null;
            }

            foreach (var term in terms)
            {
                foreach (var name in term.AllNames)
                {
                    if (string.Equals(name, proposed, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(LibraryNameMatching.NormalizeKey(name), proposedKey, StringComparison.Ordinal))
                    {
                        return term;
                    }
                }
            }

            var proposedTokens = MeaningfulTokens(proposedKey);
            CanonicalVocabulary.Term best = null;
            var bestScore = 0.0;
            foreach (var term in terms)
            {
                foreach (var name in term.AllNames)
                {
                    var nameKey = LibraryNameMatching.NormalizeKey(name);
                    var nameTokens = MeaningfulTokens(nameKey);
                    if (nameTokens.Count == 0 || proposedTokens.Count == 0)
                    {
                        continue;
                    }

                    // Canonical/alias fully covered by the proposed text (e.g. "Logros de Steam" → "Logros").
                    if (nameTokens.All(proposedTokens.Contains) && nameTokens.Count >= 1)
                    {
                        var coverage = (double)nameTokens.Count / Math.Max(proposedTokens.Count, nameTokens.Count);
                        var score = 2.0 + coverage + (nameTokens.Count * 0.1);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = term;
                        }

                        continue;
                    }

                    var intersection = nameTokens.Intersect(proposedTokens, StringComparer.Ordinal).Count();
                    if (intersection == 0)
                    {
                        continue;
                    }

                    var union = nameTokens.Union(proposedTokens, StringComparer.Ordinal).Count();
                    var jaccard = union == 0 ? 0 : (double)intersection / union;
                    if (jaccard >= 0.66 && jaccard > bestScore)
                    {
                        bestScore = jaccard;
                        best = term;
                    }
                }
            }

            return best;
        }

        private static string FindCleanLibraryMatch(CanonicalVocabulary.Term canonical, List<string> library)
        {
            foreach (var name in library)
            {
                if (string.Equals(name, canonical.Preferred, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(LibraryNameMatching.NormalizeKey(name), LibraryNameMatching.NormalizeKey(canonical.Preferred), StringComparison.Ordinal))
                {
                    return name;
                }
            }

            return null;
        }

        private static List<string> MeaningfulTokens(string normalizedKey)
        {
            return (normalizedKey ?? string.Empty)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ExpandToken)
                .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length > 1 && !NoiseTokens.Contains(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string ExpandToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return token;
            }

            if (token == "coop" || token == "co")
            {
                return "cooperativo";
            }

            if (token == "multiplayer")
            {
                return "multijugador";
            }

            if (token == "online" || token == "linea")
            {
                return "online";
            }

            if (token == "singleplayer" || token == "single")
            {
                return "singleplayer";
            }

            return token;
        }

        private static string CleanNewTerm(string value)
        {
            var text = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            text = text.Trim(' ', '.', ';', ':', '-', '/', '\\', '(', ')', '[', ']', '{', '}');
            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length <= 60)
            {
                return text;
            }

            var lastSpace = text.LastIndexOf(' ', 60);
            return lastSpace > 20 ? text.Substring(0, lastSpace).Trim() : text.Substring(0, 60).Trim();
        }
    }
}
