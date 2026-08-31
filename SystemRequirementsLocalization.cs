using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    public static class SystemRequirementsLocalization
    {
        private static readonly Regex Number = new Regex(@"\d{2,}", RegexOptions.Compiled);
        private static readonly Regex Sku = new Regex(@"[A-Za-z]+\d+[A-Za-z0-9\-]*", RegexOptions.Compiled);

        public static bool IsEnglishOutput(string language)
        {
            var code = (language ?? string.Empty).Trim().ToLowerInvariant();
            if (code.Length == 0)
            {
                return false;
            }

            return code == "en" || code.StartsWith("en-", StringComparison.Ordinal);
        }

        public static string AcceptOrEmpty(string source, string localized, string language)
        {
            var sourceText = OfficialStoreDataService.NormalizeSystemRequirementsText(source, language);
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                return string.Empty;
            }

            var localizedText = OfficialStoreDataService.NormalizeSystemRequirementsText(localized, language);
            if (!IsAcceptable(sourceText, localizedText))
            {
                return string.Empty;
            }

            return localizedText;
        }

        public static bool IsSameRequirementText(string left, string right)
        {
            return string.Equals(Collapse(left), Collapse(right), StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryParseResponse(string content, out string minimum, out string recommended)
        {
            minimum = string.Empty;
            recommended = string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            var cleaned = content.Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                cleaned = cleaned.Trim('`').Trim();
                if (cleaned.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned.Substring(4).Trim();
                }
            }

            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                cleaned = cleaned.Substring(start, end - start + 1);
            }

            try
            {
                var json = JObject.Parse(cleaned);
                minimum = TokenText(json, "minimumSystemRequirements", "min_sys_req");
                recommended = TokenText(json, "recommendedSystemRequirements", "recommended_sys_req");
                return !string.IsNullOrWhiteSpace(minimum) || !string.IsNullOrWhiteSpace(recommended);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsAcceptable(string source, string localized)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(localized))
            {
                return false;
            }

            if (localized.IndexOf('<') >= 0 || localized.IndexOf('>') >= 0)
            {
                return false;
            }

            var sourceLines = SplitLines(source);
            var localizedLines = SplitLines(localized);
            if (sourceLines.Count == 0 || sourceLines.Count != localizedLines.Count)
            {
                return false;
            }

            for (var i = 0; i < sourceLines.Count; i++)
            {
                if (!LinePreservesFacts(sourceLines[i], localizedLines[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LinePreservesFacts(string sourceLine, string localizedLine)
        {
            if (string.IsNullOrWhiteSpace(localizedLine))
            {
                return false;
            }

            if (sourceLine.IndexOf(':') > 0 && localizedLine.IndexOf(':') <= 0)
            {
                return false;
            }

            var maxLength = Math.Max(sourceLine.Length * 3, sourceLine.Length + 80);
            if (localizedLine.Length > maxLength)
            {
                return false;
            }

            foreach (Match match in Number.Matches(sourceLine))
            {
                if (localizedLine.IndexOf(match.Value, StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }

            foreach (Match match in Sku.Matches(sourceLine))
            {
                if (localizedLine.IndexOf(match.Value, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static string Collapse(string value)
        {
            return Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ");
        }

        private static List<string> SplitLines(string value)
        {
            return Regex.Split((value ?? string.Empty).Replace("\r", string.Empty), @"\n+")
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static string TokenText(JObject json, params string[] names)
        {
            if (json == null)
            {
                return string.Empty;
            }

            foreach (var name in names)
            {
                var token = json[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    continue;
                }

                if (token.Type == JTokenType.Array)
                {
                    var lines = token.Children()
                        .Select(x => x == null ? string.Empty : x.ToString())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                    if (lines.Count > 0)
                    {
                        return string.Join("\n", lines);
                    }

                    continue;
                }

                var text = token.Type == JTokenType.String ? token.ToString() : token.ToString().Trim('"');
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return string.Empty;
        }
    }
}
