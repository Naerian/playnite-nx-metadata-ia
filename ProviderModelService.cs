using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    public sealed class ProviderModelOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
        }
    }

    public static class ProviderModelService
    {
        public static async Task<IList<ProviderModelOption>> GetModelsAsync(
            MetaDataIASettings settings,
            CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            var requestUri = GetModelsUri(settings);
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
            using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
            {
                ConfigureAuthentication(request, settings);
                using (var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(string.Format(
                            PluginLocalization.GetString("MTDA_ProviderModelsRefreshFailedHttp", "Could not obtain the model list (HTTP {0})."),
                            (int)response.StatusCode));
                    }

                    return ParseModels(settings, responseText)
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                        .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.First())
                        .OrderBy(x => x.DisplayName ?? x.Id, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                }
            }
        }

        private static Uri GetModelsUri(MetaDataIASettings settings)
        {
            Uri endpoint;
            if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out endpoint))
            {
                throw new InvalidOperationException(
                    PluginLocalization.GetString("MTDA_ProviderModelsInvalidEndpoint", "Configure a valid provider endpoint first."));
            }

            if (settings.ProviderPreset == MetaDataIASettings.ProviderGemini)
            {
                return new Uri("https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000");
            }

            if (settings.ProviderPreset == MetaDataIASettings.ProviderClaude)
            {
                return new Uri("https://api.anthropic.com/v1/models?limit=1000");
            }

            var builder = new UriBuilder(endpoint.Scheme, endpoint.Host, endpoint.IsDefaultPort ? -1 : endpoint.Port);
            if (settings.ProviderPreset == MetaDataIASettings.ProviderOllama)
            {
                builder.Path = "/api/tags";
                return builder.Uri;
            }

            if (settings.ProviderPreset == MetaDataIASettings.ProviderLmStudio)
            {
                builder.Path = "/api/v1/models";
                return builder.Uri;
            }

            var path = endpoint.AbsolutePath.TrimEnd('/');
            var chatSuffix = "/chat/completions";
            if (path.EndsWith(chatSuffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - chatSuffix.Length);
            }
            else if (path.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - "/messages".Length);
            }

            builder.Path = path.TrimEnd('/') + "/models";
            return builder.Uri;
        }

        private static void ConfigureAuthentication(HttpRequestMessage request, MetaDataIASettings settings)
        {
            if (settings.ProviderPreset == MetaDataIASettings.ProviderGemini)
            {
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    request.Headers.TryAddWithoutValidation("x-goog-api-key", settings.ApiKey);
                }

                return;
            }

            if (settings.ProviderPreset == MetaDataIASettings.ProviderClaude)
            {
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    request.Headers.TryAddWithoutValidation("x-api-key", settings.ApiKey);
                }

                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                return;
            }

            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            }
        }

        private static IEnumerable<ProviderModelOption> ParseModels(MetaDataIASettings settings, string responseText)
        {
            var root = JToken.Parse(responseText);
            if (settings.ProviderPreset == MetaDataIASettings.ProviderGemini)
            {
                return ParseGemini(root["models"] as JArray);
            }

            if (settings.ProviderPreset == MetaDataIASettings.ProviderOllama)
            {
                return ParseOllama(root["models"] as JArray);
            }

            if (settings.ProviderPreset == MetaDataIASettings.ProviderLmStudio)
            {
                return ParseLmStudio(root["models"] as JArray);
            }

            var array = root["data"] as JArray ?? root["models"] as JArray ?? root as JArray;
            return ParseOpenAiCompatible(array);
        }

        private static IEnumerable<ProviderModelOption> ParseGemini(JArray models)
        {
            if (models == null)
            {
                return Enumerable.Empty<ProviderModelOption>();
            }

            return models.OfType<JObject>()
                .Where(model =>
                {
                    var methods = model["supportedGenerationMethods"] as JArray;
                    return methods == null || methods.Values<string>().Any(x => string.Equals(x, "generateContent", StringComparison.OrdinalIgnoreCase));
                })
                .Select(model =>
                {
                    var id = Value(model, "baseModelId");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        id = Value(model, "name");
                        if (id.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                        {
                            id = id.Substring("models/".Length);
                        }
                    }

                    return Option(id, Value(model, "displayName"));
                })
                .Where(model => model != null && IsRecommendedGeminiModel(model.Id));
        }

        private static bool IsRecommendedGeminiModel(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var normalized = id.ToLowerInvariant();
            var specialistOrUnstableMarkers = new[]
            {
                "embedding", "aqa", "imagen", "image", "vision", "veo", "tts", "audio", "live",
                "robotics", "computer-use", "deep-research", "experimental", "-exp", "preview"
            };
            if (specialistOrUnstableMarkers.Any(normalized.Contains))
            {
                return false;
            }

            var segments = normalized.Split('-');
            var lastSegment = segments.Length == 0 ? string.Empty : segments[segments.Length - 1];
            return lastSegment.Length != 3 || !lastSegment.All(char.IsDigit);
        }

        private static IEnumerable<ProviderModelOption> ParseOllama(JArray models)
        {
            if (models == null)
            {
                return Enumerable.Empty<ProviderModelOption>();
            }

            return models.OfType<JObject>().Select(model =>
            {
                var id = Value(model, "model");
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = Value(model, "name");
                }

                return Option(id, Value(model, "name"));
            });
        }

        private static IEnumerable<ProviderModelOption> ParseLmStudio(JArray models)
        {
            return models == null
                ? Enumerable.Empty<ProviderModelOption>()
                : models.OfType<JObject>()
                    .Where(model => string.Equals(Value(model, "type"), "llm", StringComparison.OrdinalIgnoreCase))
                    .Select(model => Option(Value(model, "key"), Value(model, "display_name")));
        }

        private static IEnumerable<ProviderModelOption> ParseOpenAiCompatible(JArray models)
        {
            if (models == null)
            {
                return Enumerable.Empty<ProviderModelOption>();
            }

            return models.OfType<JObject>()
                .Where(IsTextGenerationModel)
                .Select(model => Option(Value(model, "id"), Value(model, "name")));
        }

        private static bool IsTextGenerationModel(JObject model)
        {
            var chatCapability = model.SelectToken("capabilities.completion_chat");
            if (chatCapability != null && chatCapability.Type == JTokenType.Boolean)
            {
                return chatCapability.Value<bool>();
            }

            var outputModalities = model.SelectToken("architecture.output_modalities") as JArray;
            if (outputModalities != null && !outputModalities.Values<string>().Any(x => string.Equals(x, "text", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var id = Value(model, "id").ToLowerInvariant();
            var excluded = new[] { "embedding", "whisper", "transcri", "tts", "moderation", "dall-e", "image", "realtime", "audio" };
            return !excluded.Any(id.Contains);
        }

        private static ProviderModelOption Option(string id, string displayName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            id = id.Trim();
            displayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
            return new ProviderModelOption
            {
                Id = id,
                DisplayName = string.Equals(displayName, id, StringComparison.OrdinalIgnoreCase)
                    ? id
                    : displayName + " - " + id
            };
        }

        private static string Value(JObject model, string propertyName)
        {
            return model == null || model[propertyName] == null ? string.Empty : model[propertyName].ToString();
        }
    }
}
