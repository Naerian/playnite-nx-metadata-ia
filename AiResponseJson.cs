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

            var prepared = StripMarkdownFence(content.Trim());
            prepared = ExtractObjectSpan(prepared);
            var unwrapped = TryUnwrapJsonString(prepared);
            if (!string.IsNullOrWhiteSpace(unwrapped))
            {
                prepared = ExtractObjectSpan(unwrapped.Trim());
            }

            if (LooksOverEscaped(prepared))
            {
                prepared = UnescapeOverEscaped(prepared);
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
            var trimmed = StripMarkdownFence(content.Trim());
            var extracted = ExtractObjectSpan(trimmed);
            yield return extracted;
            yield return trimmed;

            var unwrapped = TryUnwrapJsonString(extracted);
            if (!string.IsNullOrWhiteSpace(unwrapped))
            {
                var inner = ExtractObjectSpan(unwrapped.Trim());
                yield return inner;
                if (LooksOverEscaped(inner))
                {
                    yield return UnescapeOverEscaped(inner);
                }
            }

            if (LooksOverEscaped(extracted))
            {
                yield return UnescapeOverEscaped(extracted);
            }

            var repairedControls = EscapeRawControlCharactersInJsonStrings(extracted);
            if (!string.Equals(repairedControls, extracted, StringComparison.Ordinal))
            {
                yield return repairedControls;
            }

            if (LooksOverEscaped(extracted))
            {
                var unescapedThenControls = EscapeRawControlCharactersInJsonStrings(UnescapeOverEscaped(extracted));
                yield return unescapedThenControls;
            }
        }

        private static string StripMarkdownFence(string content)
        {
            if (string.IsNullOrWhiteSpace(content) || !content.StartsWith("```", StringComparison.Ordinal))
            {
                return content;
            }

            var cleaned = content.Trim('`').Trim();
            if (cleaned.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(4).Trim();
            }

            return cleaned;
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
