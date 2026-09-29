using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Parses AI chat content into a JSON object, repairing common model mistakes
    /// such as markdown fences, a JSON string wrapping the object, or over-escaped
    /// quotes like { \"genres\": [...] }.
    /// </summary>
    internal static class AiResponseJson
    {
        private static readonly Regex OverEscapedObjectStart = new Regex(
            @"\{\s*\\""",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // One object that packs several field/terms pairs (duplicate "field" keys). Newtonsoft
        // keeps only the last pair; split into separate array elements before parse.
        private static readonly Regex CollapsedFieldObject = new Regex(
            @"\{(?:\s*""field""\s*:\s*""[^""]*""\s*,\s*""terms""\s*:\s*(?:\[[^\]]*\]|""[^""]*"")\s*,?){2,}\s*\}",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex FieldTermsPair = new Regex(
            @"""field""\s*:\s*""(?<name>[^""]*)""\s*,\s*""terms""\s*:\s*(?<terms>\[[^\]]*\]|""[^""]*"")",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool TryParseObject(string content, out JObject json)
        {
            JsonReaderException unused;
            return TryParseObject(content, out json, out unused);
        }

        public static bool TryParseObject(string content, out JObject json, out JsonReaderException error)
        {
            json = null;
            error = null;
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            foreach (var candidate in EnumerateCandidates(content))
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                try
                {
                    json = JObject.Parse(candidate);
                    return true;
                }
                catch (JsonReaderException ex)
                {
                    error = ex;
                }
            }

            return false;
        }

        /// <summary>
        /// Best-effort repair used before loose field scraping when strict parse fails.
        /// </summary>
        public static string PrepareForLooseParse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return content ?? string.Empty;
            }

            var prepared = StripLanguageFence(content.Trim());
            prepared = ExtractObjectSpan(prepared);
            prepared = RepairCollapsedFieldEntries(prepared);
            var unwrapped = TryUnwrapJsonString(prepared);
            if (!string.IsNullOrWhiteSpace(unwrapped))
            {
                prepared = ExtractObjectSpan(unwrapped.Trim());
                prepared = RepairCollapsedFieldEntries(prepared);
            }

            if (LooksOverEscaped(prepared))
            {
                prepared = UnescapeOverEscaped(prepared);
                prepared = RepairCollapsedFieldEntries(prepared);
            }

            return prepared;
        }

        private static IEnumerable<string> EnumerateCandidates(string content)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in BuildCandidates(content))
            {
                if (string.IsNullOrWhiteSpace(candidate) || !seen.Add(candidate))
                {
                    continue;
                }

                yield return candidate;
            }
        }

        private static IEnumerable<string> BuildCandidates(string content)
        {
            var trimmed = StripLanguageFence(content.Trim());
            var extracted = RepairCollapsedFieldEntries(ExtractObjectSpan(trimmed));
            yield return extracted;
            yield return trimmed;
            yield return RepairCollapsedFieldEntries(trimmed);

            var unwrapped = TryUnwrapJsonString(extracted);
            if (!string.IsNullOrWhiteSpace(unwrapped))
            {
                var inner = RepairCollapsedFieldEntries(ExtractObjectSpan(unwrapped.Trim()));
                yield return inner;
                if (LooksOverEscaped(inner))
                {
                    yield return RepairCollapsedFieldEntries(UnescapeOverEscaped(inner));
                }
            }

            if (LooksOverEscaped(extracted))
            {
                yield return RepairCollapsedFieldEntries(UnescapeOverEscaped(extracted));
            }

            var repairedControls = EscapeRawControlCharactersInJsonStrings(extracted);
            if (!string.Equals(repairedControls, extracted, StringComparison.Ordinal))
            {
                yield return RepairCollapsedFieldEntries(repairedControls);
            }

            if (LooksOverEscaped(extracted))
            {
                var unescapedThenControls = EscapeRawControlCharactersInJsonStrings(UnescapeOverEscaped(extracted));
                yield return RepairCollapsedFieldEntries(unescapedThenControls);
            }
        }

        /// <summary>
        /// Strips ``` / ```json fences and a bare leading "json" label (models often emit
        /// "json\n{...}" without backticks).
        /// </summary>
        private static string StripLanguageFence(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            var cleaned = content.Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                cleaned = cleaned.Trim('`').Trim();
            }

            if (cleaned.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                var after = cleaned.Length == 4 ? string.Empty : cleaned.Substring(4);
                if (after.Length == 0 || char.IsWhiteSpace(after[0]) || after[0] == '\n' || after[0] == '\r')
                {
                    cleaned = after.Trim();
                }
            }

            return cleaned;
        }

        /// <summary>
        /// Turns {"field":"genres","terms":[...],"field":"tags","terms":[...]} into two
        /// separate objects so both fields survive Newtonsoft's last-key-wins parse.
        /// </summary>
        internal static string RepairCollapsedFieldEntries(string content)
        {
            if (string.IsNullOrEmpty(content) ||
                content.IndexOf("\"field\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return content ?? string.Empty;
            }

            return CollapsedFieldObject.Replace(content, match =>
            {
                var pairs = FieldTermsPair.Matches(match.Value);
                if (pairs.Count < 2)
                {
                    return match.Value;
                }

                var parts = new List<string>(pairs.Count);
                foreach (Match pair in pairs)
                {
                    parts.Add(
                        "{\"field\":\"" + pair.Groups["name"].Value +
                        "\",\"terms\":" + pair.Groups["terms"].Value + "}");
                }

                return string.Join(",", parts);
            });
        }

        private static string ExtractObjectSpan(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return content ?? string.Empty;
            }

            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return content.Substring(start, end - start + 1);
            }

            return content;
        }

        private static string TryUnwrapJsonString(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var trimmed = content.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '"')
            {
                // Common model mistake: object body with escaped quotes but no outer quotes.
                if (LooksOverEscaped(trimmed))
                {
                    return UnescapeOverEscaped(trimmed);
                }

                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<string>(trimmed);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool LooksOverEscaped(string content)
        {
            return !string.IsNullOrEmpty(content) && OverEscapedObjectStart.IsMatch(content);
        }

        private static string UnescapeOverEscaped(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content;
            }

            var builder = new StringBuilder(content.Length);
            for (var i = 0; i < content.Length; i++)
            {
                var character = content[i];
                if (character != '\\' || i + 1 >= content.Length)
                {
                    builder.Append(character);
                    continue;
                }

                var next = content[i + 1];
                switch (next)
                {
                    case '"':
                        builder.Append('"');
                        i++;
                        break;
                    case 'n':
                        builder.Append('\n');
                        i++;
                        break;
                    case 'r':
                        builder.Append('\r');
                        i++;
                        break;
                    case 't':
                        builder.Append('\t');
                        i++;
                        break;
                    case '\\':
                        builder.Append('\\');
                        i++;
                        break;
                    case '/':
                        builder.Append('/');
                        i++;
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }

            return builder.ToString();
        }

        private static string EscapeRawControlCharactersInJsonStrings(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content;
            }

            var builder = new StringBuilder(content.Length + 32);
            var inString = false;
            var escaped = false;
            var changed = false;

            foreach (var character in content)
            {
                if (escaped)
                {
                    builder.Append(character);
                    escaped = false;
                    continue;
                }

                if (inString && character == '\\')
                {
                    builder.Append(character);
                    escaped = true;
                    continue;
                }

                if (character == '"')
                {
                    inString = !inString;
                    builder.Append(character);
                    continue;
                }

                if (inString)
                {
                    if (character == '\r')
                    {
                        changed = true;
                        continue;
                    }

                    if (character == '\n')
                    {
                        builder.Append("\\n");
                        changed = true;
                        continue;
                    }

                    if (character == '\t')
                    {
                        builder.Append("\\t");
                        changed = true;
                        continue;
                    }

                    if (char.IsControl(character))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4"));
                        changed = true;
                        continue;
                    }
                }

                builder.Append(character);
            }

            return changed ? builder.ToString() : content;
        }
    }
}
