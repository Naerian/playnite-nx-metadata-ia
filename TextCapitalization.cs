using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Casing uses the culture of the plugin language.
    /// Uppercase is every letter. English capitalizes each word.
    /// Other languages capitalize the first letter of each sentence.
    /// </summary>
    public static class TextCapitalization
    {
        public static string Apply(string value, string language, bool uppercase)
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

            return ToSentence(value, language);
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
