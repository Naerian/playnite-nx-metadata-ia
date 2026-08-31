using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    public class MetadataGenerationService
    {
        private readonly MetaDataIASettings settings;
        private readonly IPlayniteAPI playniteApi;
        private List<OfficialStoreMetadata> officialContextForCurrentRequest = new List<OfficialStoreMetadata>();

        public MetadataGenerationService(MetaDataIASettings settings, IPlayniteAPI playniteApi = null)
        {
            this.settings = settings;
            this.playniteApi = playniteApi;
        }

        public async Task<AiMetadataResult> GenerateAsync(Game game, CancellationToken cancellationToken = default(CancellationToken))
        {
            Exception primaryError = null;

            try
            {
                return await GenerateCurrentAsync(game, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!ShouldTryLocalFallback(ex))
                {
                    throw;
                }

                primaryError = ex;
            }

            return await TryLocalFallbacksAsync(game, primaryError, cancellationToken).ConfigureAwait(false);
        }

        private async Task<AiMetadataResult> GenerateCurrentAsync(Game game, CancellationToken cancellationToken)
        {
            if (settings.ProviderPreset == MetaDataIASettings.ProviderClaude)
            {
                return await GenerateAnthropicAsync(game, cancellationToken).ConfigureAwait(false);
            }

            return await GenerateOpenAICompatibleAsync(game, cancellationToken).ConfigureAwait(false);
        }

        private bool ShouldTryLocalFallback(Exception ex)
        {
            if (!settings.EnableLocalFallback ||
                settings.ProviderPreset == MetaDataIASettings.ProviderLmStudio ||
                settings.ProviderPreset == MetaDataIASettings.ProviderOllama)
            {
                return false;
            }

            var providerException = ex as AiProviderException;
            if (providerException != null && providerException.StopBatch)
            {
                return true;
            }

            return ex is HttpRequestException;
        }

        private async Task<AiMetadataResult> TryLocalFallbacksAsync(Game game, Exception primaryError, CancellationToken cancellationToken)
        {
            var errors = new List<string>();

            if (settings.TryLmStudioFallback)
            {
                var result = await TryFallbackAsync(game, MetaDataIASettings.ProviderLmStudio, errors, cancellationToken).ConfigureAwait(false);
                if (result != null)
                {
                    return result;
                }
            }

            if (settings.TryOllamaFallback)
            {
                var result = await TryFallbackAsync(game, MetaDataIASettings.ProviderOllama, errors, cancellationToken).ConfigureAwait(false);
                if (result != null)
                {
                    return result;
                }
            }

            throw new AiProviderException(
                primaryError.Message +
                "\n\n" +
                string.Format(
                    Loc("MTDA_ErrorLocalFallbackUnavailable", "Metadata AI tried to use the free local fallback, but no local provider was available.\n\nCheck that LM Studio has the local server active at http://localhost:1234 or that Ollama is running at http://localhost:11434.\n\nFallback errors:\n{0}"),
                    string.Join("\n", errors.Select(SanitizeForUser))),
                true,
                string.Join("\n", errors));
        }

        private async Task<AiMetadataResult> TryFallbackAsync(Game game, string provider, List<string> errors, CancellationToken cancellationToken)
        {
            try
            {
                var fallbackSettings = settings.CreateLocalFallbackSettings(provider);
                return await new MetadataGenerationService(fallbackSettings, playniteApi).GenerateCurrentAsync(game, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                errors.Add(provider + ": " + ex.Message);
                return null;
            }
        }

        private async Task<AiMetadataResult> GenerateOpenAICompatibleAsync(Game game, CancellationToken cancellationToken)
        {
            var userPrompt = await BuildUserPromptAsync(game, cancellationToken).ConfigureAwait(false);
            var result = await SendOpenAICompatibleRequestAsync(userPrompt, cancellationToken).ConfigureAwait(false);
            PrepareResult(result, game);

            if (RequiresGeneratedDescription() && !HasRequestedDescriptionContent(result, game))
            {
                var requestedTokens = ExtractTemplateTokens(settings.ResolveTemplate(game));
                var retryPrompt = userPrompt +
                    "\n\nRETRY REQUIREMENT: The previous response left every token used by the active description template empty. " +
                    "Return useful text for at least one of these requested description tokens when the supplied context supports it: " +
                    string.Join(", ", requestedTokens) + ". " +
                    "Keep the exact JSON shape, do not add headings, and do not invent unsupported facts. If reliable context is genuinely insufficient, keep the values empty.";

                result = await SendOpenAICompatibleRequestAsync(retryPrompt, cancellationToken).ConfigureAwait(false);
                PrepareResult(result, game);

                if (!HasRequestedDescriptionContent(result, game))
                {
                    throw new InvalidOperationException(
                        Loc(
                            "MTDA_ErrorAiDescriptionEmpty",
                            "The provider returned metadata but did not generate content for the active description template. No empty description was applied. Try again, choose a model that follows structured output more reliably, or enable official context for this game."));
                }
            }

            await LocalizeSystemRequirementsAsync(result, game, cancellationToken).ConfigureAwait(false);
            await ApplyVerifiedSeriesOrderAsync(result, game, cancellationToken).ConfigureAwait(false);
            return result;
        }

        private async Task<AiMetadataResult> SendOpenAICompatibleRequestAsync(string userPrompt, CancellationToken cancellationToken)
        {
            return await SendOpenAICompatibleRequestAsync(userPrompt, SupportsJsonObjectResponse(), cancellationToken).ConfigureAwait(false);
        }

        private async Task<AiMetadataResult> SendOpenAICompatibleRequestAsync(string userPrompt, bool jsonObject, CancellationToken cancellationToken)
        {
            var request = JObject.FromObject(new
            {
                model = settings.Model,
                max_tokens = ResolveCompletionMaxTokens(),
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = BuildSystemPrompt()
                    },
                    new
                    {
                        role = "user",
                        content = userPrompt
                    }
                }
            });
            if (settings.ProviderPreset != MetaDataIASettings.ProviderGemini)
            {
                request["temperature"] = 0.0;
            }

            if (jsonObject)
            {
                request["response_format"] = JObject.FromObject(new { type = "json_object" });
            }

            using (var client = new HttpClient())
            using (var message = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint))
            {
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                }

                message.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    throw CreateConnectionException(new TimeoutException("The provider request timed out."));
                }
                catch (HttpRequestException ex)
                {
                    throw CreateConnectionException(ex);
                }

                ProviderUsageService.CaptureResponseHeaders(settings, response);

                var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    if (jsonObject && (int)response.StatusCode == 400)
                    {
                        return await SendOpenAICompatibleRequestAsync(userPrompt, false, cancellationToken).ConfigureAwait(false);
                    }

                    throw CreateProviderException((int)response.StatusCode, responseText);
                }

                var content = ExtractAssistantContent(responseText);
                return ParseResult(content);
            }
        }

        private void PrepareResult(AiMetadataResult result, Game game)
        {
            ApplyStrictFactualGuard(result, game);
            ApplyTrustedFactualFields(result, game);
            EnsureSystemRequirements(result, game);
            result.Normalize(settings, game);
            ApplyStrictFactualGuard(result, game);
            AttachProvenance(result, game);
        }

        private void ApplyTrustedFactualFields(AiMetadataResult result, Game game)
        {
            if (result == null)
            {
                return;
            }

            result.Conflicts = new List<MetadataFieldConflict>();
            var sources = (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Where(x => x != null && x.IsExactMatch)
                .ToList();

            DetectListConflict(result, "developers", sources, x => x.Developers);
            DetectListConflict(result, "publishers", sources, x => x.Publishers);
            DetectListConflict(result, "genres", sources, x => x.Genres);
            DetectListConflict(result, "features", sources, x => x.Features);
            DetectListConflict(result, "ageRatings", sources, x => string.IsNullOrWhiteSpace(x.AgeRating) ? new List<string>() : new List<string> { x.AgeRating });
            DetectListConflict(result, "regions", sources, x => x.Regions);

            if (settings.GenerateGenres)
            {
                var genres = FirstOfficialList(x => x.Genres);
                if (genres.Count > 0) result.Genres = genres.Take(settings.MaxGenres).ToList();
            }

            if (settings.GenerateFeatures)
            {
                var features = FirstOfficialList(x => x.Features);
                if (features.Count > 0) result.Features = features.Take(settings.MaxFeatures).ToList();
            }

            if (settings.GenerateLinks)
            {
                var links = FirstOfficialLinks();
                if (links.Count > 0) result.Links = links;
            }

            var dates = sources
                .Where(x => !string.IsNullOrWhiteSpace(x.ReleaseDate))
                .Select(x => new MetadataConflictValue { Source = x.SourceName, Value = x.ReleaseDate.Trim() })
                .ToList();
            AddConflictIfNeeded(result, "releaseDate", dates);
            if (settings.GenerateReleaseDate && dates.Count > 0)
            {
                result.ReleaseDate = dates[0].Value;
            }
            else if (!settings.GenerateReleaseDate)
            {
                result.ReleaseDate = string.Empty;
            }

            var series = sources
                .Where(x => x.Series != null && x.Series.Count > 0)
                .Select(x => new MetadataConflictValue { Source = x.SourceName, Value = string.Join(", ", x.Series.Where(y => !string.IsNullOrWhiteSpace(y))) })
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .ToList();
            AddConflictIfNeeded(result, "series", series);
            if (!settings.GenerateSeries)
            {
                result.Series = new List<string>();
            }
            else if (series.Count > 0)
            {
                result.Series = series[0].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Take(settings.MaxSeries).ToList();
            }

            result.Series = ResolveKnownSeries(result.Series, game, settings.MaxSeries);

            ApplyOfficialSystemRequirements(result);
        }

        private void EnsureSystemRequirements(AiMetadataResult result, Game game)
        {
            if (result == null || game == null)
            {
                return;
            }

            ApplyOfficialSystemRequirements(result);
            if (!TemplateNeedsSystemRequirements(ExtractTemplateTokens(settings.ResolveTemplate(game))))
            {
                NormalizeResultSystemRequirements(result);
                return;
            }

            var needsMinimum = string.IsNullOrWhiteSpace(result.MinimumSystemRequirements);
            var needsRecommended = string.IsNullOrWhiteSpace(result.RecommendedSystemRequirements);
            if (!needsMinimum && !needsRecommended)
            {
                NormalizeResultSystemRequirements(result);
                return;
            }

            OfficialStoreMetadata steam = null;
            try
            {
                steam = new OfficialStoreDataService(settings)
                    .TryGetSteamContextAsync(game, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                steam = null;
            }

            if (steam == null)
            {
                return;
            }

            if (needsMinimum && !string.IsNullOrWhiteSpace(steam.MinimumSystemRequirements))
            {
                result.MinimumSystemRequirements = steam.MinimumSystemRequirements;
            }

            if (needsRecommended && !string.IsNullOrWhiteSpace(steam.RecommendedSystemRequirements))
            {
                result.RecommendedSystemRequirements = steam.RecommendedSystemRequirements;
            }

            NormalizeResultSystemRequirements(result);

            if (officialContextForCurrentRequest == null)
            {
                officialContextForCurrentRequest = new List<OfficialStoreMetadata>();
            }

            if (!officialContextForCurrentRequest.Any(x =>
                    x != null &&
                    string.Equals(x.SourceName, OfficialStoreDataService.SourceSteamOfficial, StringComparison.OrdinalIgnoreCase) &&
                    (!string.IsNullOrWhiteSpace(x.MinimumSystemRequirements) || !string.IsNullOrWhiteSpace(x.RecommendedSystemRequirements))))
            {
                steam.IsExactMatch = true;
                officialContextForCurrentRequest.Add(steam);
            }
        }

        private void ApplyOfficialSystemRequirements(AiMetadataResult result)
        {
            var sources = (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Where(x => x != null)
                .ToList();
            if (sources.Count == 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(result.MinimumSystemRequirements))
            {
                result.MinimumSystemRequirements = sources
                    .Select(x => x.MinimumSystemRequirements)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(result.RecommendedSystemRequirements))
            {
                result.RecommendedSystemRequirements = sources
                    .Select(x => x.RecommendedSystemRequirements)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
            }

            NormalizeResultSystemRequirements(result);
        }

        private void NormalizeResultSystemRequirements(AiMetadataResult result)
        {
            if (result == null)
            {
                return;
            }

            var language = settings == null ? "en" : settings.Language;
            result.MinimumSystemRequirements = OfficialStoreDataService.NormalizeSystemRequirementsText(result.MinimumSystemRequirements, language);
            result.RecommendedSystemRequirements = OfficialStoreDataService.NormalizeSystemRequirementsText(result.RecommendedSystemRequirements, language);
        }

        private void PreferStoreSystemRequirements(AiMetadataResult result)
        {
            var sources = (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Where(x => x != null)
                .ToList();
            if (sources.Count == 0)
            {
                return;
            }

            var minimum = sources.Select(x => x.MinimumSystemRequirements).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(minimum))
            {
                result.MinimumSystemRequirements = minimum;
            }

            var recommended = sources.Select(x => x.RecommendedSystemRequirements).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(recommended))
            {
                result.RecommendedSystemRequirements = recommended;
            }

            NormalizeResultSystemRequirements(result);
        }

        private async Task LocalizeSystemRequirementsAsync(AiMetadataResult result, Game game, CancellationToken cancellationToken)
        {
            if (result == null || game == null)
            {
                return;
            }

            if (!TemplateNeedsSystemRequirements(ExtractTemplateTokens(settings.ResolveTemplate(game))))
            {
                return;
            }

            PreferStoreSystemRequirements(result);
            NormalizeResultSystemRequirements(result);

            var language = settings == null ? "en" : settings.Language;
            try
            {
                if (SystemRequirementsLocalization.IsEnglishOutput(language))
                {
                    return;
                }

                var sourceMinimum = result.MinimumSystemRequirements ?? string.Empty;
                var sourceRecommended = result.RecommendedSystemRequirements ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sourceMinimum) && string.IsNullOrWhiteSpace(sourceRecommended))
                {
                    return;
                }

                if (!await TryLocalizeSystemRequirementsOnceAsync(result, sourceMinimum, sourceRecommended, language, false, cancellationToken).ConfigureAwait(false))
                {
                    await TryLocalizeSystemRequirementsOnceAsync(result, sourceMinimum, sourceRecommended, language, true, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                result.RefreshDescription(settings, game);
            }
        }

        private async Task<bool> TryLocalizeSystemRequirementsOnceAsync(
            AiMetadataResult result,
            string sourceMinimum,
            string sourceRecommended,
            string language,
            bool retry,
            CancellationToken cancellationToken)
        {
            string content;
            try
            {
                var userPrompt = BuildSystemRequirementsLocalizationUserPrompt(sourceMinimum, sourceRecommended, language);
                if (retry)
                {
                    userPrompt += "\n\nThe previous attempt copied the source text. Rewrite every user-facing phrase into " +
                                  TargetLanguageName(language) +
                                  ". Do not copy the source wording. Keep every product name, SKU and number unchanged.";
                }

                content = await SendConstrainedPromptAsync(
                    BuildSystemRequirementsLocalizationSystemPrompt(),
                    userPrompt,
                    1024,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                ClearUnlocalizedSystemRequirements(result, sourceMinimum, sourceRecommended, language);
                return false;
            }

            string localizedMinimum;
            string localizedRecommended;
            if (!SystemRequirementsLocalization.TryParseResponse(content, out localizedMinimum, out localizedRecommended))
            {
                ClearUnlocalizedSystemRequirements(result, sourceMinimum, sourceRecommended, language);
                return false;
            }

            result.MinimumSystemRequirements = string.IsNullOrWhiteSpace(sourceMinimum)
                ? string.Empty
                : SystemRequirementsLocalization.AcceptOrEmpty(sourceMinimum, localizedMinimum, language);
            result.RecommendedSystemRequirements = string.IsNullOrWhiteSpace(sourceRecommended)
                ? string.Empty
                : SystemRequirementsLocalization.AcceptOrEmpty(sourceRecommended, localizedRecommended, language);

            var minimumOk = string.IsNullOrWhiteSpace(sourceMinimum) || !string.IsNullOrWhiteSpace(result.MinimumSystemRequirements);
            var recommendedOk = string.IsNullOrWhiteSpace(sourceRecommended) || !string.IsNullOrWhiteSpace(result.RecommendedSystemRequirements);
            if (!minimumOk || !recommendedOk)
            {
                result.MinimumSystemRequirements = sourceMinimum;
                result.RecommendedSystemRequirements = sourceRecommended;
                return false;
            }

            var copiedSource = (!string.IsNullOrWhiteSpace(sourceMinimum) &&
                                SystemRequirementsLocalization.IsSameRequirementText(sourceMinimum, result.MinimumSystemRequirements)) ||
                               (!string.IsNullOrWhiteSpace(sourceRecommended) &&
                                SystemRequirementsLocalization.IsSameRequirementText(sourceRecommended, result.RecommendedSystemRequirements));
            if (copiedSource && !retry && !SystemRequirementsLocalization.IsEnglishOutput(language))
            {
                result.MinimumSystemRequirements = sourceMinimum;
                result.RecommendedSystemRequirements = sourceRecommended;
                return false;
            }

            SyncSystemRequirementProvenance(result);
            return true;
        }

        private static void ClearUnlocalizedSystemRequirements(AiMetadataResult result, string sourceMinimum, string sourceRecommended, string language)
        {
            if (!SystemRequirementsLocalization.IsEnglishOutput(language))
            {
                if (!string.IsNullOrWhiteSpace(sourceMinimum))
                {
                    result.MinimumSystemRequirements = string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(sourceRecommended))
                {
                    result.RecommendedSystemRequirements = string.Empty;
                }
            }

            SyncSystemRequirementProvenance(result);
        }

        private static void SyncSystemRequirementProvenance(AiMetadataResult result)
        {
            if (result == null || result.Provenance == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(result.MinimumSystemRequirements))
            {
                result.Provenance.RemoveAll(x => string.Equals(x.Field, "min_sys_req", StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrWhiteSpace(result.RecommendedSystemRequirements))
            {
                result.Provenance.RemoveAll(x => string.Equals(x.Field, "recommended_sys_req", StringComparison.OrdinalIgnoreCase));
            }
        }

        private string BuildSystemRequirementsLocalizationSystemPrompt()
        {
            var languageName = TargetLanguageName(settings.Language);
            return "You localize PC game system requirement lists. " +
                   "Output language: " + languageName + " (" + settings.Language + "). " +
                   "Return only a JSON object with minimumSystemRequirements and recommendedSystemRequirements. " +
                   "Each value must be plain text, one requirement per line, in the form Label: value. " +
                   "Translate labels and any leftover English connecting phrases into that language. " +
                   "Keep hardware names, product names, SKUs and all numbers unchanged. " +
                   "Do not add, remove or reorder lines. Do not invent specs. Do not use HTML or markdown.";
        }

        private string BuildSystemRequirementsLocalizationUserPrompt(string minimum, string recommended, string language)
        {
            return "Localize these store system requirements into " + TargetLanguageName(language) +
                   ". Keep the same line count and the same facts.\n\n" +
                   "minimumSystemRequirements:\n" + (minimum ?? string.Empty) + "\n\n" +
                   "recommendedSystemRequirements:\n" + (recommended ?? string.Empty);
        }

        private async Task<string> SendConstrainedPromptAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken cancellationToken)
        {
            if (settings.ProviderPreset == MetaDataIASettings.ProviderClaude)
            {
                return await SendAnthropicTextAsync(systemPrompt, userPrompt, maxTokens, cancellationToken).ConfigureAwait(false);
            }

            return await SendOpenAICompatibleTextAsync(systemPrompt, userPrompt, maxTokens, SupportsJsonObjectResponse(), cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> SendOpenAICompatibleTextAsync(string systemPrompt, string userPrompt, int maxTokens, bool jsonObject, CancellationToken cancellationToken)
        {
            var request = JObject.FromObject(new
            {
                model = settings.Model,
                max_tokens = maxTokens,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            });
            if (settings.ProviderPreset != MetaDataIASettings.ProviderGemini)
            {
                request["temperature"] = 0.0;
            }

            if (jsonObject)
            {
                request["response_format"] = JObject.FromObject(new { type = "json_object" });
            }

            using (var client = new HttpClient())
            using (var message = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint))
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                }

                message.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    throw CreateConnectionException(new TimeoutException("The provider request timed out."));
                }
                catch (HttpRequestException ex)
                {
                    throw CreateConnectionException(ex);
                }

                ProviderUsageService.CaptureResponseHeaders(settings, response);
                var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (jsonObject && (int)response.StatusCode == 400)
                    {
                        return await SendOpenAICompatibleTextAsync(systemPrompt, userPrompt, maxTokens, false, cancellationToken).ConfigureAwait(false);
                    }

                    throw CreateProviderException((int)response.StatusCode, responseText);
                }

                return ExtractAssistantContent(responseText);
            }
        }

        private async Task<string> SendAnthropicTextAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken cancellationToken)
        {
            var request = new
            {
                model = settings.Model,
                max_tokens = maxTokens,
                temperature = 0.0,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            using (var client = new HttpClient())
            using (var message = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint))
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    message.Headers.Add("x-api-key", settings.ApiKey);
                }

                message.Headers.Add("anthropic-version", "2023-06-01");
                message.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    throw CreateConnectionException(new TimeoutException("The provider request timed out."));
                }
                catch (HttpRequestException ex)
                {
                    throw CreateConnectionException(ex);
                }

                ProviderUsageService.CaptureResponseHeaders(settings, response);
                var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw CreateProviderException((int)response.StatusCode, responseText);
                }

                return ExtractAnthropicContent(responseText);
            }
        }

        private static void DetectListConflict(AiMetadataResult result, string field, IEnumerable<OfficialStoreMetadata> sources, Func<OfficialStoreMetadata, List<string>> selector)
        {
            var values = sources
                .Select(x => new MetadataConflictValue
                {
                    Source = x.SourceName,
                    Value = string.Join(", ", (selector(x) ?? new List<string>()).Where(y => !string.IsNullOrWhiteSpace(y)).Select(y => y.Trim()))
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .ToList();
            AddConflictIfNeeded(result, field, values);
        }

        private static void AddConflictIfNeeded(AiMetadataResult result, string field, List<MetadataConflictValue> values)
        {
            var distinct = (values ?? new List<MetadataConflictValue>())
                .GroupBy(x => NormalizeConflictValue(x.Value), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
            if (distinct.Count > 1)
            {
                result.Conflicts.Add(new MetadataFieldConflict { Field = field, Values = values });
            }
        }

        private static string NormalizeConflictValue(string value)
        {
            return string.Join("|", (value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToLowerInvariant()).OrderBy(x => x));
        }

        private void AttachProvenance(AiMetadataResult result, Game game)
        {
            if (result == null)
            {
                return;
            }

            result.Provenance = new List<MetadataFieldProvenance>();
            AddTextProvenance(result, game, "description", result.Description, x => x.Description, game == null ? null : game.Description);
            AddListProvenance(result, game, "genres", result.Genres, x => x.Genres, ExistingNames(game == null ? null : game.Genres));
            AddListProvenance(result, game, "tags", result.Tags, null, ExistingNames(game == null ? null : game.Tags));
            AddListProvenance(result, game, "features", result.Features, x => x.Features, ExistingNames(game == null ? null : game.Features));
            AddListProvenance(result, game, "developers", result.Developers, x => x.Developers, ExistingNames(game == null ? null : game.Developers));
            AddListProvenance(result, game, "publishers", result.Publishers, x => x.Publishers, ExistingNames(game == null ? null : game.Publishers));
            AddListProvenance(result, game, "ageRatings", result.AgeRatings, x => string.IsNullOrWhiteSpace(x.AgeRating) ? new List<string>() : new List<string> { x.AgeRating }, ExistingNames(game == null ? null : game.AgeRatings));
            AddListProvenance(result, game, "regions", result.Regions, x => x.Regions, ExistingNames(game == null ? null : game.Regions));
            AddListProvenance(result, game, "categories", result.Categories, null, ExistingNames(game == null ? null : game.Categories));
            AddLinksProvenance(result, game);
            AddTextProvenance(result, game, "releaseDate", result.ReleaseDate, x => x.ReleaseDate, game != null && game.ReleaseDate.HasValue ? game.ReleaseDate.Value.ToString() : string.Empty);
            AddListProvenance(result, game, "series", result.Series, x => x.Series, ExistingNames(game == null ? null : game.Series));
            if (!string.IsNullOrWhiteSpace(result.MinimumSystemRequirements))
            {
                result.Provenance.Add(BuildProvenance(
                    "min_sys_req",
                    FindOfficialSource(x => !string.IsNullOrWhiteSpace(x.MinimumSystemRequirements)),
                    false,
                    false));
            }
            if (!string.IsNullOrWhiteSpace(result.RecommendedSystemRequirements))
            {
                result.Provenance.Add(BuildProvenance(
                    "recommended_sys_req",
                    FindOfficialSource(x => !string.IsNullOrWhiteSpace(x.RecommendedSystemRequirements)),
                    false,
                    false));
            }

            if (settings.GenerateSortingName)
            {
                result.Provenance.Add(new MetadataFieldProvenance
                {
                    Field = "sortingName",
                    Source = "Metadata AI local rule",
                    Method = "deterministic",
                    Confidence = "high",
                    Detail = "Generated locally only when the title contains an explicit ordinal or there is safe local series evidence."
                });
            }
        }

        private void AddTextProvenance(AiMetadataResult result, Game game, string field, string value, Func<OfficialStoreMetadata, string> officialSelector, string existing)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var official = FindOfficialSource(x => !string.IsNullOrWhiteSpace(officialSelector(x)));
            result.Provenance.Add(BuildProvenance(field, official, !string.IsNullOrWhiteSpace(existing), true));
        }

        private void AddListProvenance(AiMetadataResult result, Game game, string field, List<string> values, Func<OfficialStoreMetadata, List<string>> officialSelector, List<string> existing)
        {
            if (values == null || values.Count == 0)
            {
                return;
            }

            var official = officialSelector == null ? null : FindOfficialSource(x =>
            {
                var selected = officialSelector(x);
                return selected != null && selected.Any(y => !string.IsNullOrWhiteSpace(y));
            });
            result.Provenance.Add(BuildProvenance(field, official, existing != null && existing.Count > 0, field != "developers" && field != "publishers" && field != "ageRatings" && field != "regions"));
        }

        private void AddLinksProvenance(AiMetadataResult result, Game game)
        {
            if (result.Links == null || result.Links.Count == 0)
            {
                return;
            }

            var official = FindOfficialSource(x => x.Links != null && x.Links.Count > 0);
            var hasExisting = game != null && game.Links != null && game.Links.Count > 0;
            result.Provenance.Add(BuildProvenance("links", official, hasExisting, false));
        }

        private OfficialStoreMetadata FindOfficialSource(Func<OfficialStoreMetadata, bool> hasField)
        {
            return (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Where(x => x != null && hasField(x))
                .OrderByDescending(x => x.IsExactMatch)
                .FirstOrDefault();
        }

        private List<AiMetadataLink> FirstOfficialLinks()
        {
            return (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Where(x => x != null && x.IsExactMatch && x.Links != null && x.Links.Any(link => link != null && !string.IsNullOrWhiteSpace(link.Url)))
                .Select(x => x.Links.Where(link => link != null && !string.IsNullOrWhiteSpace(link.Url))
                    .Select(link => new AiMetadataLink { Name = link.Name, Url = link.Url }).ToList())
                .FirstOrDefault() ?? new List<AiMetadataLink>();
        }

        private MetadataFieldProvenance BuildProvenance(string field, OfficialStoreMetadata official, bool hasExisting, bool editorial)
        {
            if (official != null)
            {
                return new MetadataFieldProvenance
                {
                    Field = field,
                    Source = official.SourceName,
                    Method = editorial ? "ai-normalized" : "trusted-context",
                    Confidence = official.IsExactMatch ? "high" : "medium",
                    Detail = editorial
                        ? "The source was supplied as factual context and the AI normalized it."
                        : "The value was constrained by trusted source context."
                };
            }

            if (hasExisting && !string.Equals(settings.ExistingMetadataMode, "Ignorar", StringComparison.OrdinalIgnoreCase))
            {
                return new MetadataFieldProvenance
                {
                    Field = field,
                    Source = "Existing Playnite metadata",
                    Method = "ai-normalized",
                    Confidence = "medium",
                    Detail = "Current library metadata was supplied as context and normalized by the AI."
                };
            }

            return new MetadataFieldProvenance
            {
                Field = field,
                Source = "AI provider: " + settings.ProviderPreset,
                Method = "generated-from-identity",
                Confidence = "low",
                Detail = "No field-specific trusted source was available. Review this value before applying it."
            };
        }

        private bool RequiresGeneratedDescription()
        {
            return settings.GenerateDescription && settings.DescriptionApplyMode != MetaDataIASettings.ApplySkip;
        }

        private bool IsOpenRouterFreeModel()
        {
            var model = (settings.Model ?? string.Empty).Trim();
            return settings.ProviderPreset == MetaDataIASettings.ProviderOpenRouterFree ||
                   (settings.ProviderPreset == MetaDataIASettings.ProviderOpenRouter &&
                    (string.Equals(model, "openrouter/free", StringComparison.OrdinalIgnoreCase) ||
                     model.EndsWith(":free", StringComparison.OrdinalIgnoreCase)));
        }

        private bool HasRequestedDescriptionContent(AiMetadataResult result, Game game)
        {
            if (result == null)
            {
                return false;
            }

            var requestedTokens = ExtractTemplateTokens(settings.ResolveTemplate(game));
            if (requestedTokens.Count == 0)
            {
                return !string.IsNullOrWhiteSpace(result.Description);
            }

            var generativeTokens = requestedTokens
                .Where(token => !IsStoreFilledDescriptionToken(token))
                .ToList();
            if (generativeTokens.Count > 0)
            {
                return generativeTokens.Any(token => HasDescriptionTokenContent(result, token));
            }

            return requestedTokens.Any(token => HasDescriptionTokenContent(result, token));
        }

        private static bool IsStoreFilledDescriptionToken(string token)
        {
            switch ((token ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "min_sys_req":
                case "recommended_sys_req":
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasDescriptionTokenContent(AiMetadataResult result, string token)
        {
            switch ((token ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "short": return !string.IsNullOrWhiteSpace(result.Short);
                case "synopsis": return !string.IsNullOrWhiteSpace(result.Synopsis);
                case "premise": return !string.IsNullOrWhiteSpace(result.Premise);
                case "gameplay": return !string.IsNullOrWhiteSpace(result.Gameplay);
                case "tone": return !string.IsNullOrWhiteSpace(result.Tone);
                case "setting": return !string.IsNullOrWhiteSpace(result.Setting);
                case "perspective": return !string.IsNullOrWhiteSpace(result.Perspective);
                case "playmodes": return !string.IsNullOrWhiteSpace(result.PlayModes);
                case "estimatedlength": return !string.IsNullOrWhiteSpace(result.EstimatedLength);
                case "similargames": return !string.IsNullOrWhiteSpace(result.SimilarGames);
                case "notes": return !string.IsNullOrWhiteSpace(result.Notes);
                case "recommendedfor": return !string.IsNullOrWhiteSpace(result.RecommendedFor);
                case "min_sys_req": return !string.IsNullOrWhiteSpace(result.MinimumSystemRequirements);
                case "recommended_sys_req": return !string.IsNullOrWhiteSpace(result.RecommendedSystemRequirements);
                case "features": return result.Features != null && result.Features.Count > 0;
                case "similargameslist":
                    return (result.SimilarGamesList != null && result.SimilarGamesList.Count > 0) ||
                           !string.IsNullOrWhiteSpace(result.SimilarGames);
                case "genres": return result.Genres != null && result.Genres.Count > 0;
                case "tags": return result.Tags != null && result.Tags.Count > 0;
                case "developers": return result.Developers != null && result.Developers.Count > 0;
                case "publishers": return result.Publishers != null && result.Publishers.Count > 0;
                case "ageratings": return result.AgeRatings != null && result.AgeRatings.Count > 0;
                case "regions": return result.Regions != null && result.Regions.Count > 0;
                case "categories": return result.Categories != null && result.Categories.Count > 0;
                default: return false;
            }
        }

        private async Task<AiMetadataResult> GenerateAnthropicAsync(Game game, CancellationToken cancellationToken)
        {
            var userPrompt = await BuildUserPromptAsync(game, cancellationToken).ConfigureAwait(false);
            var request = new
            {
                model = settings.Model,
                max_tokens = ResolveCompletionMaxTokens(),
                temperature = 0.0,
                system = BuildSystemPrompt(),
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = userPrompt
                    }
                }
            };

            using (var client = new HttpClient())
            using (var message = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint))
            {
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    message.Headers.Add("x-api-key", settings.ApiKey);
                }

                message.Headers.Add("anthropic-version", "2023-06-01");
                message.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    throw CreateConnectionException(new TimeoutException("The provider request timed out."));
                }
                catch (HttpRequestException ex)
                {
                    throw CreateConnectionException(ex);
                }

                ProviderUsageService.CaptureResponseHeaders(settings, response);

                var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw CreateProviderException((int)response.StatusCode, responseText);
                }

                var content = ExtractAnthropicContent(responseText);
                var result = ParseResult(content);
                PrepareResult(result, game);
                await LocalizeSystemRequirementsAsync(result, game, cancellationToken).ConfigureAwait(false);
                await ApplyVerifiedSeriesOrderAsync(result, game, cancellationToken).ConfigureAwait(false);
                return result;
            }
        }

        private async Task ApplyVerifiedSeriesOrderAsync(AiMetadataResult result, Game game, CancellationToken cancellationToken)
        {
            if (result == null || game == null || (!settings.GenerateSortingName && !settings.GenerateSeries))
            {
                return;
            }

            var verified = await new SeriesOrderLookupService(settings).ResolveAsync(game, cancellationToken).ConfigureAwait(false);
            if (settings.GenerateSortingName)
            {
                result.SortingName = SortingNameService.Generate(playniteApi, game, verified != null && verified.HasOrder ? verified : null);
            }

            if (verified == null)
            {
                return;
            }

            if (settings.GenerateSeries && verified.HasSeries)
            {
                result.Series = ResolveKnownSeries(new[] { verified.SeriesName }, game, settings.MaxSeries);
                result.Conflicts.RemoveAll(x => string.Equals(x.Field, "series", StringComparison.OrdinalIgnoreCase));
            }

            if (verified.HasOrder && !string.IsNullOrWhiteSpace(result.SortingName))
            {
                result.Provenance.RemoveAll(x => string.Equals(x.Field, "sortingName", StringComparison.OrdinalIgnoreCase));
                result.Provenance.Add(new MetadataFieldProvenance
                {
                    Field = "sortingName",
                    Source = verified.Source,
                    Method = "catalog lookup",
                    Confidence = "high",
                    Detail = verified.Detail
                });
            }

            if (settings.GenerateSeries && verified.HasSeries && result.Series.Count > 0)
            {
                result.Provenance.RemoveAll(x => string.Equals(x.Field, "series", StringComparison.OrdinalIgnoreCase));
                result.Provenance.Add(new MetadataFieldProvenance
                {
                    Field = "series",
                    Source = verified.Source,
                    Method = "catalog lookup",
                    Confidence = "high",
                    Detail = verified.Detail
                });
            }
        }

        private string BuildSystemPrompt()
        {
            return "You are a careful video game metadata editor for Playnite. " +
                   "Return only valid JSON, without markdown. " +
                   "Use the requested output language (targetLanguage / targetLanguageName) for every user-facing value, including descriptions and free-form list labels, unless a field is locked by playniteLibraryVocabulary. " +
                   "When playniteLibraryVocabulary lists values for a field, that list is the only allowed vocabulary for that field: copy those exact spellings, do not translate them, and skip items that do not fit. " +
                   "Never rewrite list labels from one natural language into another after choosing them. " +
                   "Your job is to normalize and structure provided facts, not to invent missing metadata. " +
                   "Prioritize factual accuracy over filling every field. If a fact is uncertain, leave that field empty. " +
                   "Respond with a JSON object that contains only the keys listed in jsonShape. Do not add keys that are absent from jsonShape. " +
                   "Do not invent factual metadata. Normalize, translate and structure only facts that are present in officialStoreContext, existing metadata, the game source, platforms or the provided game identity. similarGamesList is the exception: when those tokens are requested, comparable well-known game names are allowed even if they are not listed in officialStoreContext. " +
                   "If officialStoreContextEnabled is true and officialStoreContext is missing, be conservative: do not guess developers, publishers, age ratings, regions, links, release-specific features, platform capabilities or store-specific claims. Leave uncertain fields empty. " +
                   "Respect tone, length, tokenLengths, blacklist and prefixes. " +
                   "Text tokens must contain content only: no titles, headings, field labels, markdown or HTML. Do not write labels such as 'Description:', 'Premise:', 'Synopsis:' or 'Main features:' inside any value. " +
                   "Do not mention, compare with, or recommend other games, other sagas, or unrelated companies in short, synopsis, premise, gameplay, tone, setting, perspective, playModes, estimatedLength, notes or recommendedFor. Focus only on the current game. " +
                   "If requestedDescriptionTokens contains similarGames or similarGamesList, you MUST populate similarGamesList with 3 to 6 comparable game names as an array of strings (names only, no sentences). This is required for the description template. Do not leave similarGamesList empty when those tokens are requested unless the game is so obscure that no reasonable comparison exists. Other description fields must still not mention other games. " +
                   "If requestedDescriptionTokens contains features, populate the features array with individual short feature labels in order. Never add JSON keys named feature_1, feature_2, similar_game_1, similar_game_2, feature_N or similar_game_N; those are description placeholders filled by the plugin from the features and similarGamesList arrays. " +
                   "If requestedDescriptionTokens contains min_sys_req or recommended_sys_req, do not return minimumSystemRequirements or recommendedSystemRequirements. The plugin copies store facts and localizes those lines in a dedicated step. Do not invent hardware specs. " +
                   "Interpret tokenLengths for short as: Short = 1 brief sentence; Medium = 2 or 3 sentences; Long = 1 paragraph; Extra long = 2 compact paragraphs. " +
                   "Interpret tokenLengths for synopsis as: Short = 1 paragraph of 4 to 6 sentences; Medium = 2 paragraphs of 4 to 6 sentences each; Long = 3 paragraphs of 4 to 6 sentences each; Extra long = 4 or 5 paragraphs of 4 to 6 sentences each. " +
                   "If tokenLengths.synopsis is Medium, Long or Extra long, separate paragraphs inside the JSON string using escaped double newlines (\\n\\n). Do not return synopsis as a single paragraph. " +
                   "Inside JSON strings, never use raw line breaks; always use escaped \\n or \\n\\n. " +
                   "Each paragraph must be substantial, not a single sentence, except fields configured as Short. " +
                   "For other text fields: Short = 1 brief sentence; Medium = 1 paragraph of 3 to 5 sentences; Long = 2 paragraphs of 3 to 5 sentences; Extra long = 3 paragraphs of 3 to 5 sentences. " +
                   "For lists, length controls how many useful items to return within each max value: Short = few essentials; Medium = balanced coverage; Long = broad coverage; Extra long = use the max only when enough reliable information exists. " +
                   "short and synopsis must always be different: short is a compact editorial description of what the game is; synopsis develops premise, context and structure without repeating short literally. " +
                   "Use localVocabulary first, then canonicalTerms, to keep genres, tags, features and categories stable across games when those fields are not locked. If both are empty for a field and playniteLibraryVocabulary does not lock it, create stable terms directly in the requested output language and reuse the same wording consistently. " +
                   "If playniteLibraryVocabulary is present for a field, that field is locked: you MUST pick only values from that exact list (same spelling). Do not invent new wording, do not translate those library names into the output language, and omit the item when nothing in the list fits. Locked fields override localVocabulary and canonicalTerms. " +
                   "If fieldsToGenerate includes features, features must contain between 3 and " + settings.MaxFeatures + " concrete features of the game, not generic phrases. " +
                   "Features must be stable between repeated runs: prefer the most factual and durable features over subjective wording. " +
                   "If fieldsToGenerate includes links, links must contain at most " + settings.MaxLinks + " useful and verifiable links for the game. Include only official or very reliable URLs: official website, source store page, official Discord, official wiki or official support. Do not invent URLs, do not use generic searches, and leave links empty if you do not know concrete links. " +
                   "For features, use source and platforms as context only when reasonably certain: controls, local/online multiplayer, achievements, cloud saves, controller support or platform features. " +
                   "Features must follow a Steam-like style in the requested language: very short, scannable labels, preferably 1 to 5 words, no full sentences, no final punctuation and no explanations. " +
                   "Categories must also be in the requested language. They are Playnite library grouping categories, not store tags. Use short reusable category names in the requested language, such as backlog/completed/co-op/retro/narrative equivalents, only when they fit the current game. Do not return Spanish category names unless the requested language is Spanish. " +
                   "If existingMetadataMode is Normalize, preserve the intent of current metadata but correct language, duplicates, formatting and coherence. " +
                   "If officialStoreContext is present, treat it as the primary factual source material for description, companies, genres, features, ratings and links. The store context may contain values in any language; for unlocked fields, always translate every user-facing value (genres, features, tags, categories, descriptions) from the store context into the requested output language before using them. Do not copy store-language strings verbatim unless the field is locked by playniteLibraryVocabulary. Do not add extra factual claims that are not supported by officialStoreContext or existing metadata. Do not copy store marketing headings verbatim unless they fit the selected template. If officialStoreContext conflicts with existing metadata, prefer the official store context for factual fields and use existing metadata only as secondary context. " +
                   "Developers must contain only the main credited developer studio for the base game. Publishers must contain only the main publisher. If maxDevelopers is 1, return one developer at most and choose the primary developer only. Do not include support studios, porting studios, multiplayer support studios, QA, localization, regional distributors, supervisors or collaborators unless they are one of the primary credited developers. If there is reasonable doubt, leave the field empty. " +
                   "For developers and publishers, prioritize accuracy over quantity. Return at most maxDevelopers and maxPublishers. If maxDevelopers is 1, developers must contain only the primary credited developer studio. Do not include support, porting, multiplayer, QA, localization, remaster, regional distribution, supervision or collaboration studios unless they are primary credited developers and maxDevelopers allows more than one. " +
                   "If strictCompanyAgeRegion is true, leave developers, publishers, ageRatings or regions empty when not reasonably sure. " +
                   "short, synopsis, premise, gameplay, tone, setting, perspective, playModes, estimatedLength, similarGames, notes and recommendedFor must be text strings, not arrays. " +
                   "features, similarGamesList, genres, tags, developers, publishers, ageRatings, regions, categories and series must be arrays of strings. releaseDate must be an ISO date string or empty. links must be an array of objects with name and url. " +
                   "If fieldsToGenerate includes series, reuse the exact spelling from existing.series, knownSeriesCandidates or officialStoreContext whenever one of them matches the game. Do not translate franchise or series proper names and do not create a new spelling variant.";
        }

        private async Task<string> BuildUserPromptAsync(Game game, CancellationToken cancellationToken)
        {
            var context = new Dictionary<string, object>();
            context["targetLanguage"] = settings.Language;
            context["targetLanguageName"] = TargetLanguageName(settings.Language);
            context["gameName"] = game.Name;
            context["gameId"] = game.GameId;
            context["releaseYear"] = game.ReleaseYear;
            context["source"] = game.Source == null ? null : game.Source.Name;
            context["platforms"] = Names(game.Platforms);
            context["tone"] = NormalizeToneForPrompt(settings.Tone);
            context["length"] = NormalizeLengthForPrompt(settings.Length);
            context["tokenLengths"] = BuildTokenLengths();
            context["strictCompanyAgeRegion"] = settings.StrictCompanyAgeRegion;
            var requestedTokens = ExtractTemplateTokens(settings.ResolveTemplate(game));
            var fieldsToGenerate = BuildFieldsToGenerate(requestedTokens);
            context["fieldsToGenerate"] = fieldsToGenerate
                .Where(x => x.Value)
                .ToDictionary(x => x.Key, x => true, StringComparer.OrdinalIgnoreCase);
            context["jsonShape"] = BuildJsonShape(requestedTokens, fieldsToGenerate);
            context["maxDevelopers"] = settings.MaxDevelopers;
            context["maxPublishers"] = settings.MaxPublishers;
            context["canonicalTerms"] = ExcludePreferExistingFields(BuildCanonicalTerms());
            context["knownSeriesCandidates"] = BuildKnownSeriesCandidates(game);
            context["localVocabulary"] = ExcludePreferExistingFields(settings.GetVocabularyTerms(settings.Language));
            context["playniteLibraryVocabulary"] = BuildPlayniteLibraryVocabulary();
            context["blacklist"] = settings.GetBlacklistTerms();
            context["tagPrefix"] = settings.TagPrefix;
            context["categoryPrefix"] = settings.CategoryPrefix;
            context["extraInstructions"] = settings.ExtraInstructions;
            context["requestedDescriptionTokens"] = requestedTokens;
            var trustedContextEnabled = settings.UseOfficialStoreContext ||
                                        settings.UseOriginIntegrationAsAiContext ||
                                        settings.UseOriginIntegrationForFactualMetadata ||
                                        TemplateNeedsSystemRequirements(requestedTokens);
            context["officialStoreContextEnabled"] = trustedContextEnabled;
            officialContextForCurrentRequest = new List<OfficialStoreMetadata>();

            if (!string.Equals(settings.ExistingMetadataMode, "Ignorar", StringComparison.OrdinalIgnoreCase))
            {
                context["existing"] = new
                {
                    description = game.Description,
                    genres = Names(game.Genres),
                    tags = Names(game.Tags),
                    features = Names(game.Features),
                    categories = Names(game.Categories),
                    developers = Names(game.Developers),
                    publishers = Names(game.Publishers),
                    ageRatings = Names(game.AgeRatings),
                    regions = Names(game.Regions),
                    releaseDate = game.ReleaseDate.HasValue ? game.ReleaseDate.Value.ToString() : string.Empty,
                    series = Names(game.Series),
                    links = game.Links == null ? new List<object>() : game.Links.Select(x => new { name = x.Name, url = x.Url }).Cast<object>().ToList()
                };
                context["existingMetadataMode"] = NormalizeExistingMetadataModeForPrompt(settings.ExistingMetadataMode);
            }

            if ((settings.UseOriginIntegrationAsAiContext || settings.UseOriginIntegrationForFactualMetadata) && playniteApi != null)
            {
                var integrationService = new PlayniteIntegrationService(playniteApi, settings);
                var integrationResult = await integrationService.GetOriginMetadataAsync(game, cancellationToken).ConfigureAwait(false);
                var integrationContext = integrationService.ToTrustedContext(integrationResult, game);
                if (integrationContext != null && integrationContext.HasUsefulData())
                {
                    officialContextForCurrentRequest.Add(integrationContext);
                }
            }

            if (settings.UseOfficialStoreContext || NeedsTrustedEnrichment() || TemplateNeedsSystemRequirements(requestedTokens))
            {
                var officialContext = await new OfficialStoreDataService(settings).GetOfficialContextsAsync(game, cancellationToken).ConfigureAwait(false);
                officialContextForCurrentRequest.AddRange(officialContext);
            }

            if (ShouldUseTrustedEnrichment(game) && HasMissingTrustedEvidence())
            {
                var igdbContext = await new IgdbMetadataContextService(settings).GetContextAsync(game, cancellationToken).ConfigureAwait(false);
                if (igdbContext != null && igdbContext.HasUsefulData())
                {
                    officialContextForCurrentRequest.Add(igdbContext);
                }
            }

            if (settings.MediaUseIgn && (settings.UseOfficialStoreContext || NeedsTrustedEnrichment()))
            {
                var ignContext = await new IgnDataService().GetContextAsync(game, cancellationToken).ConfigureAwait(false);
                if (ignContext != null && ignContext.HasUsefulData())
                {
                    officialContextForCurrentRequest.Add(ignContext);
                }
            }

            // These are opt-in, specialist/fallback catalogues. Their data is
            // offered to the same factual-validation path as other sources; it
            // never bypasses the configured field apply rules.
            if (settings.UseVndbMetadata && (settings.UseOfficialStoreContext || NeedsTrustedEnrichment()))
            {
                var vndbContext = await new VndbMetadataService().GetContextAsync(game, cancellationToken).ConfigureAwait(false);
                if (vndbContext != null && vndbContext.HasUsefulData())
                {
                    officialContextForCurrentRequest.Add(vndbContext);
                }
            }

            if (settings.UseWikidataMetadata && (settings.UseOfficialStoreContext || NeedsTrustedEnrichment()))
            {
                var wikidataContext = await new WikidataMetadataService().GetContextAsync(game, cancellationToken).ConfigureAwait(false);
                if (wikidataContext != null && wikidataContext.HasUsefulData())
                {
                    officialContextForCurrentRequest.Add(wikidataContext);
                }
            }

            if (officialContextForCurrentRequest.Count > 0)
            {
                context["officialStoreContext"] = officialContextForCurrentRequest.Select(x => new
                {
                    source = x.SourceName,
                    exactMatch = x.IsExactMatch,
                    url = x.StoreUrl,
                    title = x.Title,
                    description = x.Description,
                    genres = x.Genres,
                    features = x.Features,
                    developers = x.Developers,
                    publishers = x.Publishers,
                    ageRating = x.AgeRating,
                    regions = x.Regions,
                    releaseDate = x.ReleaseDate,
                    series = x.Series,
                    minimumSystemRequirements = x.MinimumSystemRequirements,
                    recommendedSystemRequirements = x.RecommendedSystemRequirements,
                    links = x.Links.Select(link => new { name = link.Name, url = link.Url }).ToList()
                }).ToList();
            }

            return "Generate normalized metadata for this game. The requested output language is " +
                   TargetLanguageName(settings.Language) + " (" + settings.Language + "). " +
                   "Context: " + JsonConvert.SerializeObject(context);
        }

        private Dictionary<string, string> BuildTokenLengths()
        {
            var lengths = new Dictionary<string, string>();
            lengths["short"] = ResolveTokenLength(settings.OverrideShortLength, settings.ShortLength);
            lengths["synopsis"] = ResolveTokenLength(settings.OverrideSynopsisLength, settings.SynopsisLength);
            lengths["premise"] = ResolveTokenLength(settings.OverridePremiseLength, settings.PremiseLength);
            lengths["gameplay"] = ResolveTokenLength(settings.OverrideGameplayLength, settings.GameplayLength);
            lengths["tone"] = ResolveTokenLength(settings.OverrideToneLength, settings.ToneLength);
            lengths["setting"] = ResolveTokenLength(settings.OverrideSettingLength, settings.SettingLength);
            lengths["perspective"] = ResolveTokenLength(settings.OverridePerspectiveLength, settings.PerspectiveLength);
            lengths["playModes"] = ResolveTokenLength(settings.OverridePlayModesLength, settings.PlayModesLength);
            lengths["estimatedLength"] = ResolveTokenLength(settings.OverrideEstimatedLengthLength, settings.EstimatedLengthLength);
            lengths["similarGames"] = ResolveTokenLength(settings.OverrideSimilarGamesLength, settings.SimilarGamesLength);
            lengths["notes"] = ResolveTokenLength(settings.OverrideNotesLength, settings.NotesLength);
            lengths["recommendedFor"] = ResolveTokenLength(settings.OverrideRecommendedForLength, settings.RecommendedForLength);
            return lengths;
        }

        private string ResolveTokenLength(bool useOverride, string overrideValue)
        {
            return NormalizeLengthForPrompt(useOverride ? overrideValue : settings.Length);
        }

        private static string NormalizeLengthForPrompt(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "corta": return "Short";
                case "media": return "Medium";
                case "larga": return "Long";
                case "extra larga": return "Extra long";
                default: return value;
            }
        }

        private static string NormalizeToneForPrompt(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "enciclopedico": return "Encyclopedic";
                case "tienda": return "Store";
                case "critico": return "Critical";
                case "breve": return "Brief";
                case "entusiasta": return "Enthusiastic";
                case "tecnico": return "Technical";
                case "familiar": return "Family-friendly";
                default: return value;
            }
        }

        private static string NormalizeExistingMetadataModeForPrompt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "usar como contexto": return "Use as context";
                case "normalizar": return "Normalize";
                case "ignorar": return "Ignore";
                default: return value;
            }
        }

        private Dictionary<string, bool> BuildFieldsToGenerate(IList<string> requestedTokens)
        {
            var fields = new Dictionary<string, bool>();
            fields["description"] = settings.GenerateDescription;
            fields["genres"] = settings.GenerateGenres;
            fields["tags"] = settings.GenerateTags;
            fields["features"] = settings.GenerateFeatures || ContainsToken(requestedTokens, "features");
            fields["developers"] = settings.GenerateDevelopers;
            fields["publishers"] = settings.GeneratePublishers;
            fields["ageRatings"] = settings.GenerateAgeRatings;
            fields["regions"] = settings.GenerateRegions;
            fields["categories"] = settings.GenerateCategories;
            fields["links"] = settings.GenerateLinks;
            fields["releaseDate"] = settings.GenerateReleaseDate;
            fields["series"] = settings.GenerateSeries;
            fields["minimumSystemRequirements"] = ContainsToken(requestedTokens, "min_sys_req");
            fields["recommendedSystemRequirements"] = ContainsToken(requestedTokens, "recommended_sys_req");
            return fields;
        }

        private static string BuildJsonShape(IList<string> requestedTokens, Dictionary<string, bool> fields)
        {
            var parts = new List<string>();
            AddShapeKey(parts, requestedTokens, "short", "\"\"");
            AddShapeKey(parts, requestedTokens, "synopsis", "\"\"");
            AddShapeKey(parts, requestedTokens, "premise", "\"\"");
            AddShapeKey(parts, requestedTokens, "gameplay", "\"\"");
            AddShapeKey(parts, requestedTokens, "tone", "\"\"");
            AddShapeKey(parts, requestedTokens, "setting", "\"\"");
            AddShapeKey(parts, requestedTokens, "perspective", "\"\"");
            AddShapeKey(parts, requestedTokens, "playModes", "\"\"");
            AddShapeKey(parts, requestedTokens, "estimatedLength", "\"\"");
            AddShapeKey(parts, requestedTokens, "similarGames", "\"\"");
            AddShapeKey(parts, requestedTokens, "notes", "\"\"");
            AddShapeKey(parts, requestedTokens, "recommendedFor", "\"\"");
            if (ContainsToken(requestedTokens, "similarGames") ||
                ContainsToken(requestedTokens, "similarGamesList") ||
                requestedTokens.Any(IsIndexedSimilarGameToken))
            {
                parts.Add("\"similarGamesList\":[]");
            }

            if (FieldEnabled(fields, "features") || ContainsToken(requestedTokens, "features") || requestedTokens.Any(IsIndexedFeatureToken))
            {
                parts.Add("\"features\":[]");
            }

            AddFieldShape(parts, fields, "genres", "[]");
            AddFieldShape(parts, fields, "tags", "[]");
            AddFieldShape(parts, fields, "developers", "[]");
            AddFieldShape(parts, fields, "publishers", "[]");
            AddFieldShape(parts, fields, "ageRatings", "[]");
            AddFieldShape(parts, fields, "regions", "[]");
            AddFieldShape(parts, fields, "categories", "[]");
            AddFieldShape(parts, fields, "links", "[]");
            AddFieldShape(parts, fields, "releaseDate", "\"\"");
            AddFieldShape(parts, fields, "series", "[]");
            if (parts.Count == 0)
            {
                parts.Add("\"short\":\"\"");
            }

            return "{" + string.Join(",", parts) + "}";
        }

        private static void AddShapeKey(List<string> parts, IList<string> requestedTokens, string token, string emptyJson)
        {
            if (ContainsToken(requestedTokens, token))
            {
                parts.Add("\"" + token + "\":" + emptyJson);
            }
        }

        private static void AddFieldShape(List<string> parts, Dictionary<string, bool> fields, string name, string emptyJson)
        {
            if (FieldEnabled(fields, name))
            {
                parts.Add("\"" + name + "\":" + emptyJson);
            }
        }

        private static bool FieldEnabled(Dictionary<string, bool> fields, string name)
        {
            bool enabled;
            return fields != null && fields.TryGetValue(name, out enabled) && enabled;
        }

        private int ResolveCompletionMaxTokens()
        {
            var lengths = new[]
            {
                NormalizeLengthForPrompt(settings.Length),
                ResolveTokenLength(settings.OverrideSynopsisLength, settings.SynopsisLength),
                ResolveTokenLength(settings.OverrideShortLength, settings.ShortLength)
            };
            if (lengths.Any(x => string.Equals(x, "Extra long", StringComparison.OrdinalIgnoreCase)))
            {
                return 4096;
            }

            if (lengths.Any(x => string.Equals(x, "Long", StringComparison.OrdinalIgnoreCase)))
            {
                return 3072;
            }

            return 2560;
        }

        private bool SupportsJsonObjectResponse()
        {
            var preset = settings.ProviderPreset;
            return preset == MetaDataIASettings.ProviderOpenAI ||
                   preset == MetaDataIASettings.ProviderGemini ||
                   preset == MetaDataIASettings.ProviderGroq ||
                   preset == MetaDataIASettings.ProviderMistral ||
                   preset == MetaDataIASettings.ProviderCerebras;
        }

        private static bool ContainsToken(IList<string> tokens, string name)
        {
            return tokens != null && tokens.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TemplateNeedsSystemRequirements(IList<string> requestedTokens)
        {
            return ContainsToken(requestedTokens, "min_sys_req") ||
                   ContainsToken(requestedTokens, "recommended_sys_req");
        }

        private List<string> BuildKnownSeriesCandidates(Game game)
        {
            var result = ExistingNames(game == null ? null : game.Series);
            if (playniteApi == null || game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return result;
            }

            var gameKey = TitleMatchingService.NormalizeTitle(game.Name);
            foreach (var series in playniteApi.Database.Series)
            {
                if (series == null || string.IsNullOrWhiteSpace(series.Name))
                {
                    continue;
                }

                var seriesKey = TitleMatchingService.NormalizeTitle(series.Name);
                if (seriesKey.Length >= 4 &&
                    (string.Equals(gameKey, seriesKey, StringComparison.OrdinalIgnoreCase) ||
                     gameKey.StartsWith(seriesKey + " ", StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(series.Name.Trim());
                }
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        }

        private List<string> ResolveKnownSeries(IEnumerable<string> generated, Game game, int maxItems)
        {
            var requested = ExistingNames(game == null ? null : game.Series)
                .Concat(generated ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requested.Count == 0)
            {
                var inferred = SortingNameService.GenerateSeriesName(playniteApi, game);
                if (!string.IsNullOrWhiteSpace(inferred))
                {
                    requested.Add(inferred);
                }
            }

            var knownCandidates = BuildKnownSeriesCandidates(game);
            if (requested.Count == 0 && knownCandidates.Count == 1)
            {
                requested.Add(knownCandidates[0]);
            }

            if (playniteApi == null)
            {
                return requested.Take(Math.Max(1, maxItems)).ToList();
            }

            var known = playniteApi.Database.Series
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => x.Name.Trim())
                .ToList();
            return requested
                .Select(value => known.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))
                              ?? known.FirstOrDefault(x => string.Equals(TitleMatchingService.NormalizeTitle(x), TitleMatchingService.NormalizeTitle(value), StringComparison.OrdinalIgnoreCase))
                              ?? value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxItems))
                .ToList();
        }

        private static List<string> ExtractTemplateTokens(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return new List<string>();
            }

            var tokens = Regex.Matches(template, @"\{([A-Za-z0-9_]+)\}")
                .Cast<Match>()
                .Select(x => x.Groups[1].Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var hasFeatureIndex = tokens.Any(IsIndexedFeatureToken);
            var hasSimilarIndex = tokens.Any(IsIndexedSimilarGameToken);
            tokens = tokens
                .Where(x => !IsIndexedFeatureToken(x) && !IsIndexedSimilarGameToken(x))
                .ToList();

            if (hasFeatureIndex && !tokens.Contains("features", StringComparer.OrdinalIgnoreCase))
            {
                tokens.Add("features");
            }

            if (hasSimilarIndex)
            {
                if (!tokens.Contains("similarGames", StringComparer.OrdinalIgnoreCase))
                {
                    tokens.Add("similarGames");
                }

                if (!tokens.Contains("similarGamesList", StringComparer.OrdinalIgnoreCase))
                {
                    tokens.Add("similarGamesList");
                }
            }

            return tokens;
        }

        private static bool IsIndexedFeatureToken(string token)
        {
            return Regex.IsMatch(token ?? string.Empty, @"^feature_(\d+|N)$", RegexOptions.IgnoreCase);
        }

        private static bool IsIndexedSimilarGameToken(string token)
        {
            return Regex.IsMatch(token ?? string.Empty, @"^similar_game_(\d+|N)$", RegexOptions.IgnoreCase);
        }

        private static string TargetLanguageName(string code)
        {
            switch ((code ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "es":
                case "es-es":
                case "es-mx":
                case "es-ar": return "Spanish";
                case "en":
                case "en-us":
                case "en-gb": return "English";
                case "pl": return "Polish";
                case "fr": return "French";
                case "de": return "German";
                case "it": return "Italian";
                case "pt": return "Portuguese";
                case "pt-br": return "Brazilian Portuguese";
                case "nl": return "Dutch";
                case "ru": return "Russian";
                case "uk": return "Ukrainian";
                case "ja": return "Japanese";
                case "ko": return "Korean";
                case "zh": return "Chinese";
                case "sv": return "Swedish";
                case "no": return "Norwegian";
                case "da": return "Danish";
                case "fi": return "Finnish";
                case "tr": return "Turkish";
                case "cs": return "Czech";
                case "hu": return "Hungarian";
                case "ro": return "Romanian";
                case "sk": return "Slovak";
                case "sl": return "Slovenian";
                case "hr": return "Croatian";
                case "sr": return "Serbian";
                case "bg": return "Bulgarian";
                case "el": return "Greek";
                case "ca": return "Catalan";
                case "gl": return "Galician";
                case "eu": return "Basque";
                case "et": return "Estonian";
                case "lv": return "Latvian";
                case "lt": return "Lithuanian";
                case "ar": return "Arabic";
                case "he": return "Hebrew";
                case "hi": return "Hindi";
                case "id": return "Indonesian";
                case "ms": return "Malay";
                case "th": return "Thai";
                case "vi": return "Vietnamese";
                case "zh-cn": return "Simplified Chinese";
                case "zh-tw": return "Traditional Chinese";
                default: return string.IsNullOrWhiteSpace(code) ? "Spanish" : code;
            }
        }

        private Dictionary<string, List<string>> ExcludePreferExistingFields(Dictionary<string, List<string>> vocabulary)
        {
            if (vocabulary == null || vocabulary.Count == 0)
            {
                return vocabulary;
            }

            var filtered = new Dictionary<string, List<string>>(vocabulary, StringComparer.OrdinalIgnoreCase);
            if (settings.PreferExistingGenres)
            {
                filtered.Remove("genres");
            }

            if (settings.PreferExistingTags)
            {
                filtered.Remove("tags");
            }

            if (settings.PreferExistingFeatures)
            {
                filtered.Remove("features");
            }

            if (settings.PreferExistingCategories)
            {
                filtered.Remove("categories");
            }

            if (settings.PreferExistingAgeRatings)
            {
                filtered.Remove("ageRatings");
            }

            return filtered.Count == 0 ? null : filtered;
        }

        private Dictionary<string, List<string>> BuildPlayniteLibraryVocabulary()
        {
            if (playniteApi == null || playniteApi.Database == null)
            {
                return null;
            }

            var vocabulary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (settings.PreferExistingGenres)
            {
                vocabulary["genres"] = Names(playniteApi.Database.Genres);
            }

            if (settings.PreferExistingTags)
            {
                vocabulary["tags"] = Names(playniteApi.Database.Tags);
            }

            if (settings.PreferExistingFeatures)
            {
                vocabulary["features"] = Names(playniteApi.Database.Features);
            }

            if (settings.PreferExistingCategories)
            {
                vocabulary["categories"] = Names(playniteApi.Database.Categories);
            }

            if (settings.PreferExistingAgeRatings)
            {
                vocabulary["ageRatings"] = Names(playniteApi.Database.AgeRatings);
            }

            return vocabulary.Count == 0 ? null : vocabulary;
        }

        private Dictionary<string, List<string>> BuildCanonicalTerms()
        {
            var vocabulary = settings.GetVocabularyTerms(settings.Language);
            var canonical = BuildDefaultCanonicalTerms();
            foreach (var pair in vocabulary)
            {
                if (!canonical.ContainsKey(pair.Key))
                {
                    canonical[pair.Key] = new List<string>();
                }

                canonical[pair.Key] = pair.Value
                    .Concat(canonical[pair.Key])
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return canonical;
        }

        private Dictionary<string, List<string>> BuildDefaultCanonicalTerms()
        {
            if (!string.IsNullOrWhiteSpace(settings.Language) &&
                settings.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, List<string>>
                {
                    {
                        "genres",
                        new List<string>
                        {
                            "Action", "Adventure", "RPG", "Strategy", "Simulation", "Sports", "Racing",
                            "Fighting", "Platformer", "Puzzle", "Shooter", "Horror", "Survival",
                            "Stealth", "Roguelike", "Open world", "Metroidvania", "Visual novel", "Rhythm"
                        }
                    },
                    {
                        "tags",
                        new List<string>
                        {
                            "Single-player", "Multiplayer", "Co-op", "Online co-op", "Local co-op",
                            "Competitive", "PvP", "PvE", "Social deduction", "Exploration", "Building",
                            "Management", "Crafting", "Deep story", "Narrative", "Comedy", "Difficult",
                            "Casual", "Retro", "Anime", "Pixel art", "Science fiction", "Fantasy", "Cyberpunk",
                            "Post-apocalyptic", "Sandbox", "Procedural"
                        }
                    },
                    {
                        "features",
                        new List<string>
                        {
                            "Single-player", "Online multiplayer", "Local multiplayer", "Online co-op",
                            "Local co-op", "Split screen", "Controller support", "Achievements",
                            "Cloud saves", "Steam trading cards", "Steam Deck compatibility",
                            "Cross-play", "Level editor", "PvP modes", "PvE modes", "In-app purchases"
                        }
                    },
                    {
                        "categories",
                        new List<string>
                        {
                            "Favorites", "Backlog", "Completed", "Abandoned", "Co-op games",
                            "Quick sessions", "Long sessions", "Relaxing", "Challenges", "Narrative",
                            "Multiplayer", "Indie", "Retro", "Emulation"
                        }
                    }
                };
            }

            if (!string.IsNullOrWhiteSpace(settings.Language) &&
                !settings.Language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, List<string>>();
            }

            return new Dictionary<string, List<string>>
            {
                {
                    "genres",
                    new List<string>
                    {
                        "Accion", "Aventura", "RPG", "Estrategia", "Simulacion", "Deportes", "Carreras",
                        "Lucha", "Plataformas", "Puzzle", "Disparos", "Terror", "Supervivencia",
                        "Sigilo", "Roguelike", "Mundo abierto", "Metroidvania", "Novela visual", "Ritmo"
                    }
                },
                {
                    "tags",
                    new List<string>
                    {
                        "Un jugador", "Multijugador", "Cooperativo", "Cooperativo online", "Cooperativo local",
                        "Competitivo", "PvP", "PvE", "Deduccion social", "Exploracion", "Construccion",
                        "Gestion", "Crafteo", "Historia profunda", "Narrativo", "Humor", "Dificil",
                        "Casual", "Retro", "Anime", "Pixel art", "Ciencia ficcion", "Fantasia", "Cyberpunk",
                        "Postapocaliptico", "Sandbox", "Procedural"
                    }
                },
                {
                    "features",
                    new List<string>
                    {
                        "Un jugador", "Multijugador online", "Multijugador local", "Cooperativo online",
                        "Cooperativo local", "Pantalla dividida", "Soporte mando", "Logros",
                        "Guardado en la nube", "Cromos de Steam", "Compatibilidad Steam Deck",
                        "Juego cruzado", "Editor de niveles", "Modos PvP", "Modos PvE", "Compras integradas"
                    }
                },
                {
                    "categories",
                    new List<string>
                    {
                        "Favoritos", "Pendientes", "Completados", "Abandonados", "Para jugar en cooperativo",
                        "Para jugar rapido", "Para sesiones largas", "Relax", "Retos", "Narrativos",
                        "Multijugador", "Indie", "Retro", "Emulacion"
                    }
                }
            };
        }

        private static string ExtractAssistantContent(string responseText)
        {
            var json = JObject.Parse(responseText);
            var choices = json["choices"] as JArray;
            var content = choices == null || choices.Count == 0 ? null : choices[0]["message"]["content"].ToString();
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException(Loc("MTDA_ErrorAiNoUsefulContent", "The AI provider did not return useful content."));
            }

            return content.Trim();
        }

        private static string ExtractAnthropicContent(string responseText)
        {
            var json = JObject.Parse(responseText);
            var blocks = json["content"] as JArray;
            if (blocks == null || blocks.Count == 0)
            {
                throw new InvalidOperationException(Loc("MTDA_ErrorAiNoUsefulContent", "The AI provider did not return useful content."));
            }

            var texts = blocks
                .Where(x => x["type"] != null && string.Equals(x["type"].ToString(), "text", StringComparison.OrdinalIgnoreCase))
                .Select(x => x["text"] == null ? string.Empty : x["text"].ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (texts.Count == 0)
            {
                throw new InvalidOperationException(Loc("MTDA_ErrorAiNoUsefulText", "The AI provider did not return useful text."));
            }

            return string.Join("\n", texts).Trim();
        }

        private static AiMetadataResult ParseResult(string content)
        {
            var cleaned = content.Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                cleaned = cleaned.Trim('`').Trim();
                if (cleaned.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned.Substring(4).Trim();
                }
            }

            cleaned = ExtractJsonObject(cleaned);
            JObject json = null;
            Exception parseError = null;
            try
            {
                json = ParseJsonObject(cleaned);
            }
            catch (Exception ex)
            {
                parseError = ex;
            }

            if (json == null)
            {
                var loose = ParseLooseResult(cleaned);
                if (HasUsefulData(loose))
                {
                    return loose;
                }

                throw parseError ?? new InvalidOperationException(Loc("MTDA_ErrorAiResponseNotParsed", "The AI response could not be interpreted."));
            }

            if (json == null)
            {
                throw new InvalidOperationException(Loc("MTDA_ErrorAiResponseNotParsed", "The AI response could not be interpreted."));
            }

            var features = List(json, "features");
            if (features.Count == 0)
            {
                features = IndexedList(json, "feature_");
            }

            var similarGamesList = List(json, "similarGamesList");
            if (similarGamesList.Count == 0)
            {
                similarGamesList = IndexedList(json, "similar_game_");
            }

            return new AiMetadataResult
            {
                Short = Text(json, "short"),
                Synopsis = Text(json, "synopsis"),
                Premise = Text(json, "premise"),
                Gameplay = Text(json, "gameplay"),
                Tone = Text(json, "tone"),
                Setting = Text(json, "setting"),
                Perspective = Text(json, "perspective"),
                PlayModes = Text(json, "playModes"),
                EstimatedLength = Text(json, "estimatedLength"),
                SimilarGames = Text(json, "similarGames"),
                SimilarGamesList = similarGamesList,
                Notes = Text(json, "notes"),
                Features = features,
                RecommendedFor = Text(json, "recommendedFor"),
                Genres = List(json, "genres"),
                Tags = List(json, "tags"),
                Developers = List(json, "developers"),
                Publishers = List(json, "publishers"),
                AgeRatings = List(json, "ageRatings", "ageRating"),
                Regions = List(json, "regions", "region"),
                Categories = List(json, "categories"),
                ReleaseDate = Text(json, "releaseDate"),
                Series = List(json, "series", "franchise"),
                Links = Links(json, "links"),
                MinimumSystemRequirements = Text(json, "minimumSystemRequirements", "min_sys_req"),
                RecommendedSystemRequirements = Text(json, "recommendedSystemRequirements", "recommended_sys_req")
            };
        }

        private static bool HasUsefulData(AiMetadataResult result)
        {
            if (result == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(result.Short) ||
                   !string.IsNullOrWhiteSpace(result.Synopsis) ||
                   !string.IsNullOrWhiteSpace(result.Premise) ||
                   !string.IsNullOrWhiteSpace(result.Gameplay) ||
                   result.Features.Count > 0 ||
                   result.Genres.Count > 0 ||
                   result.Tags.Count > 0 ||
                   result.Categories.Count > 0 ||
                   result.Series.Count > 0 ||
                   !string.IsNullOrWhiteSpace(result.ReleaseDate);
        }

        private static AiMetadataResult ParseLooseResult(string content)
        {
            return new AiMetadataResult
            {
                Short = LooseText(content, "short"),
                Synopsis = LooseText(content, "synopsis"),
                Premise = LooseText(content, "premise"),
                Gameplay = LooseText(content, "gameplay"),
                Tone = LooseText(content, "tone"),
                Setting = LooseText(content, "setting"),
                Perspective = LooseText(content, "perspective"),
                PlayModes = LooseText(content, "playModes"),
                EstimatedLength = LooseText(content, "estimatedLength"),
                SimilarGames = LooseText(content, "similarGames"),
                SimilarGamesList = MergeNonEmpty(LooseList(content, "similarGamesList"), LooseIndexedList(content, "similar_game_")),
                Notes = LooseText(content, "notes"),
                Features = MergeNonEmpty(LooseList(content, "features"), LooseIndexedList(content, "feature_")),
                RecommendedFor = LooseText(content, "recommendedFor"),
                Genres = LooseList(content, "genres"),
                Tags = LooseList(content, "tags"),
                Developers = LooseList(content, "developers"),
                Publishers = LooseList(content, "publishers"),
                AgeRatings = LooseList(content, "ageRatings", "ageRating"),
                Regions = LooseList(content, "regions", "region"),
                Categories = LooseList(content, "categories"),
                ReleaseDate = LooseText(content, "releaseDate"),
                Series = LooseList(content, "series", "franchise"),
                Links = new List<AiMetadataLink>(),
                MinimumSystemRequirements = LooseText(content, "minimumSystemRequirements", "min_sys_req"),
                RecommendedSystemRequirements = LooseText(content, "recommendedSystemRequirements", "recommended_sys_req")
            };
        }

        private static readonly string[] KnownJsonFields = new[]
        {
            "short", "synopsis", "premise", "gameplay", "tone", "setting", "perspective", "playModes",
            "estimatedLength", "similarGames", "similarGamesList", "notes", "features", "recommendedFor", "genres", "tags",
            "developers", "publishers", "ageRatings", "ageRating", "regions", "region", "categories",
            "releaseDate", "series", "franchise", "links", "minimumSystemRequirements", "recommendedSystemRequirements", "min_sys_req", "recommended_sys_req"
        };

        private static string LooseText(string content, params string[] names)
        {
            var raw = LooseRawValue(content, names);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            raw = TrimLooseValue(raw);
            if (raw.StartsWith("[", StringComparison.Ordinal))
            {
                return string.Join(", ", LooseListFromRaw(raw));
            }

            return UnescapeLooseText(raw);
        }

        private static List<string> LooseList(string content, params string[] names)
        {
            return LooseListFromRaw(LooseRawValue(content, names));
        }

        private static string LooseRawValue(string content, params string[] names)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            Match keyMatch = null;
            foreach (var name in names)
            {
                var match = Regex.Match(content, "\"" + Regex.Escape(name) + "\"\\s*:", RegexOptions.IgnoreCase);
                if (match.Success && (keyMatch == null || match.Index < keyMatch.Index))
                {
                    keyMatch = match;
                }
            }

            if (keyMatch == null)
            {
                return string.Empty;
            }

            var start = keyMatch.Index + keyMatch.Length;
            var next = content.Length;
            foreach (Match match in Regex.Matches(content.Substring(start), ",\\s*\"(" + string.Join("|", KnownJsonFields.Select(Regex.Escape)) + ")\"\\s*:", RegexOptions.IgnoreCase))
            {
                next = start + match.Index;
                break;
            }

            var endBrace = content.LastIndexOf('}');
            if (endBrace > start && endBrace < next)
            {
                next = endBrace;
            }

            return content.Substring(start, next - start).Trim();
        }

        private static string TrimLooseValue(string raw)
        {
            var value = (raw ?? string.Empty).Trim().TrimEnd(',');
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            if (value.EndsWith("\"", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - 1);
            }

            return value.Trim();
        }

        private static string UnescapeLooseText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\n", "\n")
                .Replace("\\r", string.Empty)
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\/", "/")
                .Trim();
        }

        private static List<string> LooseListFromRaw(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            var value = raw.Trim().TrimEnd(',');
            if (value.StartsWith("[", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            if (value.EndsWith("]", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - 1);
            }

            var quoted = Regex.Matches(value, "\"((?:\\\\.|[^\"])*)\"")
                .Cast<Match>()
                .Select(x => UnescapeLooseText(x.Groups[1].Value))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (quoted.Count > 0)
            {
                return quoted;
            }

            return value
                .Replace("\r", string.Empty)
                .Split(new[] { '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(TrimLooseValue)
                .Select(UnescapeLooseText)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static JObject ParseJsonObject(string content)
        {
            try
            {
                return JObject.Parse(content);
            }
            catch (JsonReaderException firstError)
            {
                var repaired = EscapeRawControlCharactersInJsonStrings(content);
                if (!string.Equals(content, repaired, StringComparison.Ordinal))
                {
                    try
                    {
                        return JObject.Parse(repaired);
                    }
                    catch (JsonReaderException secondError)
                    {
                        throw CreateMalformedJsonException(secondError, firstError);
                    }
                }

                throw CreateMalformedJsonException(firstError, null);
            }
        }

        private static Exception CreateMalformedJsonException(JsonReaderException error, JsonReaderException originalError)
        {
            var detail = originalError == null ? error.Message : originalError.Message;
            return new InvalidOperationException(
                Loc("MTDA_ErrorMalformedAiJson", "The AI returned a response with an invalid format and it could not be interpreted.\n\nThe plugin will continue with the rest of the games. You can retry this game, reduce text length, or switch to a model that follows JSON more reliably.\n\nBrief detail: ") + SanitizeForUser(detail));
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

        private static string ExtractJsonObject(string content)
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return content.Substring(start, end - start + 1);
            }

            return content;
        }

        private static string Text(JObject json, params string[] names)
        {
            return TokenToText(Token(json, names));
        }

        private static List<string> List(JObject json, params string[] names)
        {
            return TokenToList(Token(json, names));
        }

        private static List<string> IndexedList(JObject json, string prefix)
        {
            var items = new List<KeyValuePair<int, string>>();
            if (json == null || string.IsNullOrWhiteSpace(prefix))
            {
                return new List<string>();
            }

            var pattern = "^" + Regex.Escape(prefix) + @"(\d+)$";
            foreach (var property in json.Properties())
            {
                var match = Regex.Match(property.Name ?? string.Empty, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }

                int index;
                if (!int.TryParse(match.Groups[1].Value, out index))
                {
                    continue;
                }

                var text = TokenToText(property.Value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    items.Add(new KeyValuePair<int, string>(index, text));
                }
            }

            return items.OrderBy(x => x.Key).Select(x => x.Value).ToList();
        }

        private static List<string> LooseIndexedList(string content, string prefix)
        {
            var items = new List<KeyValuePair<int, string>>();
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(prefix))
            {
                return new List<string>();
            }

            var pattern = "\"" + Regex.Escape(prefix) + "(\\d+)\"\\s*:\\s*\"([^\"]*)\"";
            foreach (Match match in Regex.Matches(content, pattern, RegexOptions.IgnoreCase))
            {
                int index;
                if (!int.TryParse(match.Groups[1].Value, out index))
                {
                    continue;
                }

                var text = UnescapeLooseText(match.Groups[2].Value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    items.Add(new KeyValuePair<int, string>(index, text));
                }
            }

            return items.OrderBy(x => x.Key).Select(x => x.Value).ToList();
        }

        private static List<string> MergeNonEmpty(List<string> primary, List<string> fallback)
        {
            if (primary != null && primary.Count > 0)
            {
                return primary;
            }

            return fallback ?? new List<string>();
        }

        private static List<AiMetadataLink> Links(JObject json, params string[] names)
        {
            var token = Token(json, names);
            if (token == null || token.Type != JTokenType.Array)
            {
                return new List<AiMetadataLink>();
            }

            return token.Children()
                .OfType<JObject>()
                .Select(x => new AiMetadataLink(Text(x, "name", "title", "label"), Text(x, "url", "href")))
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .ToList();
        }

        private static JToken Token(JObject json, params string[] names)
        {
            if (json == null || names == null)
            {
                return null;
            }

            foreach (var name in names)
            {
                var property = json.Properties().FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (property != null)
                {
                    return property.Value;
                }
            }

            return null;
        }

        private static string TokenToText(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            if (token.Type == JTokenType.Array)
            {
                return string.Join(", ", token.Children().Select(TokenToText).Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            if (token.Type == JTokenType.Object)
            {
                return token.ToString(Formatting.None);
            }

            return token.ToString().Trim();
        }

        private static List<string> TokenToList(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return new List<string>();
            }

            if (token.Type == JTokenType.Array)
            {
                return token.Children()
                    .Select(TokenToText)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
            }

            var text = TokenToText(token);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            return text
                .Replace("\r", string.Empty)
                .Split(new[] { '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static Exception CreateProviderException(int statusCode, string responseText)
        {
            var providerMessage = string.Empty;
            var providerCode = string.Empty;

            try
            {
                var json = JObject.Parse(responseText);
                providerMessage = json["error"] == null || json["error"]["message"] == null ? string.Empty : json["error"]["message"].ToString();
                providerCode = json["error"] == null || json["error"]["code"] == null ? string.Empty : json["error"]["code"].ToString();
            }
            catch
            {
                providerMessage = responseText;
            }

            if (statusCode == 402 ||
                string.Equals(providerCode, "insufficient_quota", StringComparison.OrdinalIgnoreCase) ||
                providerMessage.IndexOf("credits", StringComparison.OrdinalIgnoreCase) >= 0 &&
                providerMessage.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new AiProviderException(
                    Loc("MTDA_ErrorProviderQuota", "Your AI provider rejected the request because the account has no available quota.\n\nWith OpenAI, this usually means there is no active API credit/balance or the monthly limit has been reached.\n\nFree options:\n- Use a local OpenAI-compatible provider such as LM Studio or Ollama and change the endpoint in the plugin settings.\n- Process fewer games and fewer generated fields, although this will not help if the quota is zero.\n- Use a small local model for metadata and keep cloud AI only for occasional cases.\n\nLocal endpoint examples:\nLM Studio: http://localhost:1234/v1/chat/completions\nOllama: http://localhost:11434/v1/chat/completions\n\nFor local providers, the API key can be empty."),
                    true);
            }

            if (statusCode == 404 ||
                string.Equals(providerCode, "model_not_found", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(providerCode, "invalid_model", StringComparison.OrdinalIgnoreCase) ||
                providerMessage.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0 && providerMessage.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                providerMessage.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new AiProviderException(
                    Loc("MTDA_ErrorProviderModelNotFound", "The configured provider or model does not exist, or is not available for your account.\n\nCheck that the provider, endpoint and model name are written correctly. If you typed the model manually, copy the exact name from the provider documentation or console.\n\nExamples:\n- Gemini: gemini-3.5-flash-lite\n- Ollama: the name shown by 'ollama list'\n- LM Studio: the model loaded in the local server"),
                    true,
                    responseText);
            }

            if (statusCode == 429)
            {
                return new AiProviderException(
                    Loc("MTDA_ErrorProviderRateLimit", "The AI provider has temporarily limited requests.\n\nTry waiting a few minutes, processing fewer games at once, or using a model/local endpoint with fewer restrictions."),
                    true,
                    responseText);
            }

            if (statusCode == 503 ||
                providerMessage.IndexOf("high demand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                providerMessage.IndexOf("overloaded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                providerMessage.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new AiProviderException(
                    Loc("MTDA_ErrorProviderUnavailable", "The AI provider is overloaded or the selected model is temporarily unavailable.\n\nIf you are using Gemini, this can happen even if you have Gemini Pro/Google AI Pro in the app: the Gemini API has its own limits and availability, separate from the app subscription.\n\nWhat you can do without paying:\n- Wait a few minutes and try again.\n- Switch to gemini-3.5-flash-lite if you were using another model.\n- Process fewer games at once.\n- Use LM Studio or Ollama locally if you want to avoid external quotas."),
                    true,
                    responseText);
            }

            if (statusCode == 401 || statusCode == 403)
            {
                return new AiProviderException(
                    Loc("MTDA_ErrorProviderAuth", "The AI provider did not accept the authentication.\n\nCheck the API key, endpoint and configured model. If you use LM Studio or Ollama locally, the API key can usually be empty."),
                    false,
                    responseText);
            }

            return new AiProviderException(
                string.Format(Loc("MTDA_ErrorProviderGeneric", "The AI provider returned an error ({0}).\n\nCheck the configured provider, endpoint, model and API key. If the problem continues, try another model or a local provider."), statusCode),
                false,
                responseText);
        }

        private static Exception CreateConnectionException(Exception ex)
        {
            return new AiProviderException(
                Loc("MTDA_ErrorProviderConnection", "Could not connect to the configured provider.\n\nCheck that the endpoint is written correctly and that the provider exists. If you use LM Studio or Ollama, make sure the app is open, the local server is active, and the model is loaded or downloaded.\n\nBrief detail: ") + SanitizeForUser(ex == null ? string.Empty : ex.Message),
                true,
                ex == null ? string.Empty : ex.ToString());
        }

        private bool NeedsTrustedEnrichment()
        {
            return (settings.GenerateGenres && settings.GenresApplyMode != MetaDataIASettings.ApplySkip) ||
                   (settings.GenerateDevelopers && settings.DevelopersApplyMode != MetaDataIASettings.ApplySkip) ||
                   (settings.GeneratePublishers && settings.PublishersApplyMode != MetaDataIASettings.ApplySkip) ||
                   (settings.GenerateAgeRatings && settings.AgeRatingsApplyMode != MetaDataIASettings.ApplySkip) ||
                   (settings.GenerateRegions && settings.RegionsApplyMode != MetaDataIASettings.ApplySkip) ||
                   (settings.GenerateReleaseDate && settings.ReleaseDateApplyMode != MetaDataIASettings.ApplySkip) ||
                   (settings.GenerateSeries && settings.SeriesApplyMode != MetaDataIASettings.ApplySkip) ||
                   (settings.GenerateLinks && settings.LinksApplyMode != MetaDataIASettings.ApplySkip);
        }

        private bool ShouldUseTrustedEnrichment(Game game)
        {
            return game != null &&
                   NeedsTrustedEnrichment() &&
                   !string.IsNullOrWhiteSpace(settings.IgdbClientId) &&
                   (!string.IsNullOrWhiteSpace(settings.IgdbClientSecret) || !string.IsNullOrWhiteSpace(settings.IgdbAccessToken));
        }

        private bool HasMissingTrustedEvidence()
        {
            var sources = (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Where(x => x != null && x.IsExactMatch)
                .ToList();
            if (sources.Count == 0) return true;

            return (settings.GenerateGenres && !sources.Any(x => x.Genres != null && x.Genres.Count > 0)) ||
                   (settings.GenerateDevelopers && !sources.Any(x => x.Developers != null && x.Developers.Count > 0)) ||
                   (settings.GeneratePublishers && !sources.Any(x => x.Publishers != null && x.Publishers.Count > 0)) ||
                   (settings.GenerateAgeRatings && !sources.Any(x => !string.IsNullOrWhiteSpace(x.AgeRating))) ||
                   (settings.GenerateRegions && !sources.Any(x => x.Regions != null && x.Regions.Count > 0)) ||
                   (settings.GenerateReleaseDate && !sources.Any(x => !string.IsNullOrWhiteSpace(x.ReleaseDate))) ||
                   (settings.GenerateSeries && !sources.Any(x => x.Series != null && x.Series.Count > 0)) ||
                   (settings.GenerateLinks && !sources.Any(x => x.Links != null && x.Links.Count > 0));
        }

        private void ApplyStrictFactualGuard(AiMetadataResult result, Game game)
        {
            if (result == null ||
                !(settings.UseOfficialStoreContext || settings.UseOriginIntegrationForFactualMetadata ||
                  (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>()).Any(x => x != null && x.IsExactMatch)) ||
                !settings.StrictCompanyAgeRegion)
            {
                return;
            }

            result.Developers = ResolveStrictField(
                FirstOfficialList(x => x.Developers),
                ExistingNames(game == null ? null : game.Developers),
                settings.ExistingMetadataMode,
                settings.MaxDevelopers);

            result.Publishers = ResolveStrictField(
                FirstOfficialList(x => x.Publishers),
                ExistingNames(game == null ? null : game.Publishers),
                settings.ExistingMetadataMode,
                settings.MaxPublishers);

            result.AgeRatings = ResolveStrictField(
                FirstOfficialList(x => string.IsNullOrWhiteSpace(x.AgeRating) ? new List<string>() : new List<string> { x.AgeRating }),
                ExistingNames(game == null ? null : game.AgeRatings),
                settings.ExistingMetadataMode,
                settings.MaxAgeRatings);

            result.Regions = ResolveStrictField(
                FirstOfficialList(x => x.Regions),
                ExistingNames(game == null ? null : game.Regions),
                settings.ExistingMetadataMode,
                settings.MaxRegions);
        }

        private List<string> FirstOfficialList(Func<OfficialStoreMetadata, List<string>> selector)
        {
            return (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Select(selector)
                .Where(x => x != null && x.Any(y => !string.IsNullOrWhiteSpace(y)))
                .FirstOrDefault() ?? new List<string>();
        }

        private static List<string> ResolveStrictField(List<string> officialValues, List<string> existingValues, string existingMetadataMode, int maxItems)
        {
            var official = CleanStrictValues(officialValues, maxItems);
            if (official.Count > 0)
            {
                return official;
            }

            if (!string.Equals(existingMetadataMode, "Ignorar", StringComparison.OrdinalIgnoreCase))
            {
                return CleanStrictValues(existingValues, maxItems);
            }

            return new List<string>();
        }

        private static List<string> CleanStrictValues(IEnumerable<string> values, int maxItems)
        {
            return (values ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxItems))
                .ToList();
        }

        public static string SanitizeForUser(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return Loc("MTDA_ErrorUnspecified", "Unspecified error.");
            }

            var text = message.Trim();
            if (text.StartsWith("{", StringComparison.Ordinal) || text.StartsWith("[", StringComparison.Ordinal))
            {
                return Loc("MTDA_ErrorProviderTechnical", "The provider returned a technical error. Check the configuration or try another model.");
            }

            var jsonStart = text.IndexOf('{');
            if (jsonStart >= 0)
            {
                text = text.Substring(0, jsonStart).Trim();
            }

            jsonStart = text.IndexOf('[');
            if (jsonStart >= 0 && text.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0)
            {
                text = text.Substring(0, jsonStart).Trim();
            }

            return text.Length > 700 ? text.Substring(0, 700).Trim() + "..." : text;
        }

        private static List<string> Names<T>(IEnumerable<T> items) where T : DatabaseObject
        {
            return items == null
                ? new List<string>()
                : items.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name).ToList();
        }

        private static List<string> ExistingNames<T>(IEnumerable<T> items) where T : DatabaseObject
        {
            return Names(items);
        }

        private static string Loc(string key, string fallback)
        {
            return PluginLocalization.GetString(key, fallback);
        }
    }
}
