using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    public sealed class ProviderUsageSnapshot
    {
        public string Provider { get; set; }
        public string Model { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string Source { get; set; }
        public bool IsLocal { get; set; }
        public bool IsFreeTier { get; set; }
        public string RequestsLimit { get; set; }
        public string RequestsRemaining { get; set; }
        public string RequestsReset { get; set; }
        public string TokensLimit { get; set; }
        public string TokensRemaining { get; set; }
        public string TokensReset { get; set; }
        public string InputTokensLimit { get; set; }
        public string InputTokensRemaining { get; set; }
        public string InputTokensReset { get; set; }
        public string OutputTokensLimit { get; set; }
        public string OutputTokensRemaining { get; set; }
        public string OutputTokensReset { get; set; }
        public string CreditsLimit { get; set; }
        public string CreditsRemaining { get; set; }
        public string UsageDaily { get; set; }
        public string UsageMonthly { get; set; }
        public string RetryAfter { get; set; }

        public bool HasLimitData
        {
            get
            {
                return !string.IsNullOrWhiteSpace(RequestsLimit) ||
                       !string.IsNullOrWhiteSpace(RequestsRemaining) ||
                       !string.IsNullOrWhiteSpace(TokensLimit) ||
                       !string.IsNullOrWhiteSpace(TokensRemaining) ||
                       !string.IsNullOrWhiteSpace(InputTokensRemaining) ||
                       !string.IsNullOrWhiteSpace(OutputTokensRemaining) ||
                       !string.IsNullOrWhiteSpace(CreditsLimit) ||
                       !string.IsNullOrWhiteSpace(CreditsRemaining) ||
                       !string.IsNullOrWhiteSpace(UsageDaily) ||
                       !string.IsNullOrWhiteSpace(UsageMonthly) ||
                       !string.IsNullOrWhiteSpace(RetryAfter);
            }
        }
    }

    public static class ProviderUsageService
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, ProviderUsageSnapshot> Cache =
            new Dictionary<string, ProviderUsageSnapshot>(StringComparer.OrdinalIgnoreCase);

        public static bool IsLocalProvider(MetaDataIASettings settings)
        {
            return settings != null &&
                   (settings.ProviderPreset == MetaDataIASettings.ProviderLmStudio ||
                    settings.ProviderPreset == MetaDataIASettings.ProviderOllama);
        }

        public static bool SupportsDirectRefresh(MetaDataIASettings settings)
        {
            return settings != null &&
                   (settings.ProviderPreset == MetaDataIASettings.ProviderOpenRouter ||
                    settings.ProviderPreset == MetaDataIASettings.ProviderOpenRouterFree);
        }

        public static bool UsesDashboardOnly(MetaDataIASettings settings)
        {
            return settings != null &&
                   (settings.ProviderPreset == MetaDataIASettings.ProviderGemini ||
                    settings.ProviderPreset == MetaDataIASettings.ProviderMistral);
        }

        public static ProviderUsageSnapshot GetCached(MetaDataIASettings settings)
        {
            if (settings == null)
            {
                return null;
            }

            ProviderUsageSnapshot snapshot;
            lock (SyncRoot)
            {
                Cache.TryGetValue(GetCacheKey(settings), out snapshot);
            }

            return snapshot;
        }

        public static ProviderUsageSnapshot CreateLocalSnapshot(MetaDataIASettings settings)
        {
            var snapshot = new ProviderUsageSnapshot
            {
                Provider = settings == null ? string.Empty : settings.ProviderPreset,
                Model = settings == null ? string.Empty : settings.Model,
                UpdatedAtUtc = DateTime.UtcNow,
                Source = "local",
                IsLocal = true
            };

            Store(settings, snapshot);
            return snapshot;
        }

        public static void CaptureResponseHeaders(MetaDataIASettings settings, HttpResponseMessage response)
        {
            if (settings == null || response == null)
            {
                return;
            }

            var snapshot = new ProviderUsageSnapshot
            {
                Provider = settings.ProviderPreset,
                Model = settings.Model,
                UpdatedAtUtc = DateTime.UtcNow,
                Source = "response headers",
                RequestsLimit = Header(response,
                    "x-ratelimit-limit-requests",
                    "x-ratelimit-limit-requests-day",
                    "anthropic-ratelimit-requests-limit"),
                RequestsRemaining = Header(response,
                    "x-ratelimit-remaining-requests",
                    "x-ratelimit-remaining-requests-day",
                    "anthropic-ratelimit-requests-remaining"),
                RequestsReset = Header(response,
                    "x-ratelimit-reset-requests",
                    "x-ratelimit-reset-requests-day",
                    "anthropic-ratelimit-requests-reset"),
                TokensLimit = Header(response,
                    "x-ratelimit-limit-tokens",
                    "x-ratelimit-limit-tokens-minute",
                    "anthropic-ratelimit-tokens-limit"),
                TokensRemaining = Header(response,
                    "x-ratelimit-remaining-tokens",
                    "x-ratelimit-remaining-tokens-minute",
                    "anthropic-ratelimit-tokens-remaining"),
                TokensReset = Header(response,
                    "x-ratelimit-reset-tokens",
                    "x-ratelimit-reset-tokens-minute",
                    "anthropic-ratelimit-tokens-reset"),
                InputTokensLimit = Header(response, "anthropic-ratelimit-input-tokens-limit"),
                InputTokensRemaining = Header(response, "anthropic-ratelimit-input-tokens-remaining"),
                InputTokensReset = Header(response, "anthropic-ratelimit-input-tokens-reset"),
                OutputTokensLimit = Header(response, "anthropic-ratelimit-output-tokens-limit"),
                OutputTokensRemaining = Header(response, "anthropic-ratelimit-output-tokens-remaining"),
                OutputTokensReset = Header(response, "anthropic-ratelimit-output-tokens-reset"),
                RetryAfter = Header(response, "retry-after")
            };

            if (snapshot.HasLimitData)
            {
                Store(settings, snapshot);
            }
        }

        public static async Task<ProviderUsageSnapshot> RefreshOpenRouterAsync(
            MetaDataIASettings settings,
            CancellationToken cancellationToken)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new InvalidOperationException(
                    PluginLocalization.GetString("MTDA_ProviderUsageApiKeyRequired", "Enter the provider API key first."));
            }

            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/key"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                using (var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    CaptureResponseHeaders(settings, response);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(string.Format(
                            PluginLocalization.GetString("MTDA_ProviderUsageRefreshFailed", "Could not obtain usage information from the provider (HTTP {0})."),
                            (int)response.StatusCode));
                    }

                    var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var data = JObject.Parse(responseText)["data"] as JObject;
                    if (data == null)
                    {
                        throw new InvalidOperationException(
                            PluginLocalization.GetString("MTDA_ProviderUsageUnavailable", "The provider did not return usage or limit information."));
                    }

                    var snapshot = new ProviderUsageSnapshot
                    {
                        Provider = settings.ProviderPreset,
                        Model = settings.Model,
                        UpdatedAtUtc = DateTime.UtcNow,
                        Source = "OpenRouter API key",
                        IsFreeTier = BoolValue(data["is_free_tier"]),
                        CreditsLimit = TokenValue(data["limit"]),
                        CreditsRemaining = TokenValue(data["limit_remaining"]),
                        UsageDaily = TokenValue(data["usage_daily"]),
                        UsageMonthly = TokenValue(data["usage_monthly"])
                    };

                    Store(settings, snapshot);
                    return snapshot;
                }
            }
        }

        private static string GetCacheKey(MetaDataIASettings settings)
        {
            return (settings.ProviderPreset ?? string.Empty).Trim() + "|" +
                   (settings.Model ?? string.Empty).Trim();
        }

        private static void Store(MetaDataIASettings settings, ProviderUsageSnapshot snapshot)
        {
            if (settings == null || snapshot == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                Cache[GetCacheKey(settings)] = snapshot;
            }
        }

        private static string Header(HttpResponseMessage response, params string[] names)
        {
            foreach (var name in names)
            {
                IEnumerable<string> values;
                if (response.Headers.TryGetValues(name, out values) ||
                    response.Content != null && response.Content.Headers.TryGetValues(name, out values))
                {
                    var value = values.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }

            return null;
        }

        private static string TokenValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            var number = token as JValue;
            if (number != null && number.Value is IFormattable)
            {
                return ((IFormattable)number.Value).ToString(null, CultureInfo.InvariantCulture);
            }

            return token.ToString();
        }

        private static bool BoolValue(JToken token)
        {
            return token != null && token.Type != JTokenType.Null && token.Value<bool>();
        }
    }
}
