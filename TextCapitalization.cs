using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Casing uses the culture of the plugin language.
    /// Uppercase is every letter. English capitalizes each word.
    /// Other languages capitalize the first letter of each sentence.
    /// HTML descriptions are handled separately so tags do not steal sentence capitals.
    /// </summary>
    public static class TextCapitalization
    {
        private static readonly Regex HtmlTagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

        public static string Apply(string value, string language, bool uppercase)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            // Leave user prefixes like "[MAI]" untouched; only case the label body.
            string marker;
            string separator;
            string body;
            if (VocabularyTermNormalizer.TrySplitLeadingMarker(value, out marker, out separator, out body))
            {
                return marker + separator + ApplyBody(body, language, uppercase);
            }

            return ApplyBody(value, language, uppercase);
        }

        /// <summary>
        /// Descriptions often embed the user HTML template. Sentence/title casing must not
        /// ToLower the whole string: the first letter of &lt;h3&gt;/&lt;p&gt; would consume the
        /// capitalize flag and leave real copy in lowercase.
        /// </summary>
        public static string ApplyDescription(string value, string language, bool uppercase)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            if (!LooksLikeHtml(value))
            {
                return Apply(value, language, uppercase);
            }

            if (!uppercase)
            {
                // Keep template headings and model token casing as composed.
                return value;
            }

            return ApplyToHtmlTextNodes(value, language, true);
        }

        private static string ApplyBody(string value, string language, bool uppercase)
        {
            if (uppercase)
            {
                return ToUpper(value, language);
            }

            var culture = CultureFor(language);
            if (string.Equals(culture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(value) ? value ?? string.Empty : culture.TextInfo.ToTitleCase(value.ToLower(culture));
            }

            // Preserve short all-caps acronyms (MMO, RPG, RTS) — sentence case would turn them into Mmo/Rpg.
            if (LooksLikeAcronym(value))
            {
                return value;
            }

            return ToSentence(value, language);
        }

        private static bool LooksLikeAcronym(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim();
            if (text.Length < 2 || text.Length > 5)
            {
                return false;
            }

            for (var i = 0; i < text.Length; i++)
            {
                if (!char.IsLetter(text[i]) || !char.IsUpper(text[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static List<string> ApplyList(IEnumerable<string> values, string language, bool uppercase)
        {
            return (values ?? Enumerable.Empty<string>())
                .Select(value => string.IsNullOrWhiteSpace(value) ? value : Apply(value, language, uppercase))
                .ToList();
        }

        public static string ToUpper(string value, string language)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            return value.ToUpper(CultureFor(language));
        }

        public static string ToSentence(string value, string language)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            var culture = CultureFor(language);
            var lower = value.ToLower(culture);
            var builder = new StringBuilder(lower.Length);
            var capitalize = true;
            foreach (var character in lower)
            {
                if (capitalize && char.IsLetter(character))
                {
                    builder.Append(char.ToUpper(character, culture));
                    capitalize = false;
                }
                else
                {
                    builder.Append(character);
                    if (character == '.' || character == '!' || character == '?' || character == '\n' || character == '\r')
                    {
                        capitalize = true;
                    }
                }
            }

            return builder.ToString();
        }

        public static List<string> ToUpperList(IEnumerable<string> values, string language)
        {
            return ApplyList(values, language, true);
        }

        public static bool LooksLikeHtml(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            return HtmlTagRegex.IsMatch(value);
        }

        private static string ApplyToHtmlTextNodes(string value, string language, bool uppercase)
        {
            var builder = new StringBuilder(value.Length);
            var last = 0;
            foreach (Match match in HtmlTagRegex.Matches(value))
            {
                if (match.Index > last)
                {
                    var text = value.Substring(last, match.Index - last);
                    builder.Append(ApplyBody(text, language, uppercase));
                }

                builder.Append(match.Value);
                last = match.Index + match.Length;
            }

            if (last < value.Length)
            {
                builder.Append(ApplyBody(value.Substring(last), language, uppercase));
            }

            return builder.ToString();
        }

        public static CultureInfo CultureFor(string language)
        {
            var code = (language ?? "en").Trim().Replace('_', '-');
            if (code.Length == 0)
            {
                return CultureInfo.InvariantCulture;
            }

            if (code.IndexOf('-') < 0)
            {
                switch (code.ToLowerInvariant())
                {
                    case "es": code = "es-ES"; break;
                    case "en": code = "en-US"; break;
                    case "fr": code = "fr-FR"; break;
                    case "de": code = "de-DE"; break;
                    case "it": code = "it-IT"; break;
                    case "pt": code = "pt-PT"; break;
                    case "pl": code = "pl-PL"; break;
                    case "nl": code = "nl-NL"; break;
                    case "ru": code = "ru-RU"; break;
                    case "ja": code = "ja-JP"; break;
                    case "ko": code = "ko-KR"; break;
                    case "zh": code = "zh-CN"; break;
                    case "tr": code = "tr-TR"; break;
                    case "sv": code = "sv-SE"; break;
                    case "cs": code = "cs-CZ"; break;
                }
            }

            try
            {
                return CultureInfo.GetCultureInfo(code);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }
}
