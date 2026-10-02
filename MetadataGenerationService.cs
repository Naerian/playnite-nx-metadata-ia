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
        private const int ProviderRateLimitRetries = 3;
        private const int OrganizeProviderRetries = 1;
        private const int OrganizeProviderRetryDelaySeconds = 8;
        // Failover probes must not sit on a hung free-tier endpoint for the full SharedHttpClient timeout.
        private const int ProviderProbeTimeoutSeconds = 20;
        private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

        private readonly MetaDataIASettings settings;
        private readonly IPlayniteAPI playniteApi;
        private List<OfficialStoreMetadata> officialContextForCurrentRequest = new List<OfficialStoreMetadata>();
        private bool termsOrganizedByMainCall;

        /// <summary>
        /// Optional intra-batch cache of localized official-store spellings. Set by the
        /// batch runner; null for single-game / probe / simulation calls.
        /// </summary>
        public BatchTermSessionCache SessionCache { get; set; }

        public MetadataGenerationService(
            MetaDataIASettings settings,
            IPlayniteAPI playniteApi = null,
            BatchTermSessionCache sessionCache = null)
        {
            this.settings = settings;
            this.playniteApi = playniteApi;
            SessionCache = sessionCache;
        }

        private static HttpClient CreateSharedHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(3);
            return client;
        }

        public async Task ProbeProviderAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var provider = settings.ProviderPreset ?? string.Empty;
            var model = settings.Model ?? string.Empty;
            MetadataDebugLog.Info(
                "provider.probe",
                "provider=" + provider + " model=" + model);

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(ProviderProbeTimeoutSeconds));
                var probeToken = timeoutCts.Token;

                try
                {
                    string content;
                    if (settings.ProviderPreset == MetaDataIASettings.ProviderClaude)
                    {
                        content = await SendAnthropicTextAsync(
                            "Reply with the single word OK.",
                            "ping",
                            8,
                            probeToken).ConfigureAwait(false);
                    }
                    else
                    {
                        content = await SendOpenAICompatibleTextAsync(
                            "Reply with the single word OK.",
                            "ping",
                            8,
                            false,
                            false,
                            probeToken).ConfigureAwait(false);
                    }

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        throw new InvalidOperationException(
                            Loc("MTDA_ErrorAiNoUsefulText", "The AI provider did not return useful text."));
                    }

                    MetadataDebugLog.Info("provider.probe", "ok reply=" + (content ?? string.Empty).Trim());
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    MetadataDebugLog.Error(
                        "provider.probe",
                        "timeout provider=" + provider +
                        " model=" + model +
                        " timeoutSeconds=" + ProviderProbeTimeoutSeconds);
                    throw CreateConnectionException(new TimeoutException(
                        "The AI provider probe timed out after " + ProviderProbeTimeoutSeconds + " seconds."));
                }
            }
        }

        public async Task<AiMetadataResult> GenerateAsync(Game game, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Provider failover is owned by the batch runner (ordered ProviderProfiles).
            // Per-game local LM Studio/Ollama fallback is no longer used.
            MetadataDebugLog.GameBegin(game);
            MetadataDebugLog.Verbose = settings != null && settings.VerboseOrganizeLog;
            var ok = false;
            string failReason = null;
            try
            {
                var result = await GenerateCurrentAsync(game, cancellationToken).ConfigureAwait(false);
                ok = true;
                return result;
            }
            catch (OperationCanceledException)
            {
                failReason = "cancelled";
                throw;
            }
            catch (Exception ex)
            {
                failReason = ex.Message;
                throw;
            }
            finally
            {
                // In a metadata batch, keep the game scope open until after Apply so
                // apply lines stay inside GAME begin/end. Solo generate closes here.
                if (!ok || string.IsNullOrEmpty(MetadataDebugLog.CurrentBatchId))
                {
                    MetadataDebugLog.GameEnd(game, ok, failReason);
                }
            }
        }

        private async Task<AiMetadataResult> GenerateCurrentAsync(Game game, CancellationToken cancellationToken)
        {
            termsOrganizedByMainCall = false;
            if (IsTermFieldsOnlyGeneration())
            {
                return await GenerateTermFieldsOnlyAsync(game, cancellationToken).ConfigureAwait(false);
            }

            if (settings.ProviderPreset == MetaDataIASettings.ProviderClaude)
            {
                return await GenerateAnthropicAsync(game, cancellationToken).ConfigureAwait(false);
            }

            return await GenerateOpenAICompatibleAsync(game, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Set genres/tags/features/categories: store (+ IGDB) evidence plus the small
        /// organize/knowledge AI call. Skips the large metadata-generation request.
        /// </summary>
        private bool IsTermFieldsOnlyGeneration()
        {
            if (settings == null)
            {
                return false;
            }

            var wantsTerms = settings.GenerateGenres || settings.GenerateTags ||
                             settings.GenerateFeatures || settings.GenerateCategories;
            if (!wantsTerms)
            {
                return false;
            }

            return !settings.GenerateDescription &&
                   !settings.GenerateDevelopers &&
                   !settings.GeneratePublishers &&
                   !settings.GenerateAgeRatings &&
                   !settings.GenerateRegions &&
                   !settings.GenerateLinks &&
                   !settings.GenerateReleaseDate &&
                   !settings.GenerateSeries;
        }

        private bool NeedsTermOrganizeInMainCall()
        {
            return settings != null &&
                   !IsTermFieldsOnlyGeneration() &&
                   (settings.GenerateGenres || settings.GenerateTags || settings.GenerateFeatures);
        }

        private async Task<AiMetadataResult> GenerateTermFieldsOnlyAsync(Game game, CancellationToken cancellationToken)
        {
            await LoadOfficialContextAsync(game, cancellationToken).ConfigureAwait(false);
            LogOfficialSources(game);
            var result = new AiMetadataResult();
            ApplyTrustedFactualFields(result, game);
            result.Normalize(settings, game);
            ApplyVocabularyNormalization(result);
            ApplyStrictFactualGuard(result, game);
            AttachProvenance(result, game);
            await ResolveTermFieldsAsync(result, game, cancellationToken).ConfigureAwait(false);
            result.ApplyConfiguredPrefixes(settings, Names(game == null ? null : game.Tags), Names(game == null ? null : game.Categories));
            return result;
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

            // Content/parse failures from the primary provider must not stop a multi-game batch
            // when local fallback is also unavailable.
            var stopBatch = !(primaryError is InvalidOperationException) &&
                            !(primaryError is AiProviderException && !((AiProviderException)primaryError).StopBatch);
            throw new AiProviderException(
                primaryError.Message +
                "\n\n" +
                string.Format(
                    Loc("MTDA_ErrorLocalFallbackUnavailable", "Metadata AI tried to use the free local fallback, but no local provider was available.\n\nCheck that LM Studio has the local server active at http://localhost:1234 or that Ollama is running at http://localhost:11434.\n\nFallback errors:\n{0}"),
                    string.Join("\n", errors.Select(SanitizeForUser))),
                stopBatch,
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
            LogOfficialSources(game);
            LogMetadataRequest(game, userPrompt);
            var result = await SendOpenAICompatibleRequestAsync(userPrompt, cancellationToken).ConfigureAwait(false);
            PrepareResult(result, game);

            if (RequiresGeneratedDescription() && !HasRequestedDescriptionContent(result, game))
            {
                MetadataDebugLog.Warn(
                    game,
                    "metadata.response",
                    "description empty for active template → retry");
                var requestedTokens = ExtractTemplateTokens(settings.ResolveTemplate(game));
                var retryPrompt = userPrompt +
                    "\n\nRETRY REQUIREMENT: The previous response left every token used by the active description template empty. " +
                    "Return useful text for at least one of these requested description tokens when the supplied context supports it: " +
                    string.Join(", ", requestedTokens) + ". " +
                    "Keep the exact JSON shape, do not add headings, and do not invent unsupported facts. If reliable context is genuinely insufficient, keep the values empty.";

                LogMetadataRequest(game, retryPrompt, "metadata.request-retry");
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
            LogMetadataResultSummary(game, result);
            await ResolveTermFieldsAsync(result, game, cancellationToken).ConfigureAwait(false);
            result.ApplyConfiguredPrefixes(settings, Names(game == null ? null : game.Tags), Names(game == null ? null : game.Categories));
            await ApplyVerifiedSeriesOrderAsync(result, game, cancellationToken).ConfigureAwait(false);
            return result;
        }

        private async Task<AiMetadataResult> SendOpenAICompatibleRequestAsync(string userPrompt, CancellationToken cancellationToken)
        {
            return await SendOpenAICompatibleRequestAsync(userPrompt, SupportsJsonObjectResponse(), true, cancellationToken).ConfigureAwait(false);
        }

        private async Task<AiMetadataResult> SendOpenAICompatibleRequestAsync(string userPrompt, bool jsonObject, bool allowReasoning, CancellationToken cancellationToken)
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
            ApplyOpenAiSampling(request, allowReasoning);

            if (jsonObject)
            {
                request["response_format"] = JObject.FromObject(new { type = "json_object" });
            }

            Func<HttpRequestMessage> createMessage = () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, ProviderEndpointHelper.ResolveChatCompletionsUri(settings.Endpoint));
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                }

                message.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                return message;
            };

            HttpResponseMessage response;
            try
            {
                response = await SendProviderRequestWithRetriesAsync(createMessage, cancellationToken).ConfigureAwait(false);
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

            using (response)
            {
                ProviderUsageService.CaptureResponseHeaders(settings, response);

                var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode == 400 && jsonObject)
                    {
                        return await SendOpenAICompatibleRequestAsync(userPrompt, false, allowReasoning && !ResponseRejectsReasoningEffort(responseText), cancellationToken).ConfigureAwait(false);
                    }

                    if ((int)response.StatusCode == 400 && allowReasoning && ResponseRejectsReasoningEffort(responseText))
                    {
                        return await SendOpenAICompatibleRequestAsync(userPrompt, false, false, cancellationToken).ConfigureAwait(false);
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
            ApplyVocabularyNormalization(result);
            ApplyStrictFactualGuard(result, game);
            AttachProvenance(result, game);
        }

        private void ApplyVocabularyNormalization(AiMetadataResult result)
        {
            if (result == null)
            {
                return;
            }

            var genresLibrary = MergePreferredNames(
                playniteApi != null && playniteApi.Database != null
                    ? playniteApi.Database.Genres.Select(x => x.Name)
                    : Enumerable.Empty<string>(),
                SessionCache == null ? null : SessionCache.GetPreferred("genres"));
            var tagsLibrary = MergePreferredNames(
                playniteApi != null && playniteApi.Database != null
                    ? playniteApi.Database.Tags.Select(x => x.Name)
                    : Enumerable.Empty<string>(),
                SessionCache == null ? null : SessionCache.GetPreferred("tags"));
            var featuresLibrary = MergePreferredNames(
                playniteApi != null && playniteApi.Database != null
                    ? playniteApi.Database.Features.Select(x => x.Name)
                    : Enumerable.Empty<string>(),
                SessionCache == null ? null : SessionCache.GetPreferred("features"));
            var categoriesLibrary = MergePreferredNames(
                playniteApi != null && playniteApi.Database != null
                    ? playniteApi.Database.Categories.Select(x => x.Name)
                    : Enumerable.Empty<string>(),
                SessionCache == null ? null : SessionCache.GetPreferred("categories"));

            result.Genres = VocabularyTermNormalizer.NormalizeField(
                StoreTermSanitizer.Sanitize(result.Genres), "genres", settings.Language, genresLibrary, null,
                settings.MaxGenres, false);

            result.Tags = VocabularyTermNormalizer.NormalizeField(
                StoreTermSanitizer.Sanitize(result.Tags), "tags", settings.Language, tagsLibrary, null,
                settings.MaxTags, false);

            result.Features = VocabularyTermNormalizer.NormalizeField(
                StoreTermSanitizer.Sanitize(result.Features), "features", settings.Language, featuresLibrary, null,
                settings.MaxFeatures, false);

            result.Categories = VocabularyTermNormalizer.NormalizeField(
                StoreTermSanitizer.Sanitize(result.Categories), "categories", settings.Language, categoriesLibrary, null,
                settings.MaxCategories, settings.PreferExistingCategories);
        }

        private static IEnumerable<string> MergePreferredNames(
            IEnumerable<string> libraryNames,
            IEnumerable<string> sessionNames)
        {
            // Library first so FindExisting prefers Playnite spellings over session.
            var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in (libraryNames ?? Enumerable.Empty<string>())
                .Concat(sessionNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()))
            {
                var key = LibraryNameMatching.NormalizeKey(name);
                if (key.Length == 0 || byKey.ContainsKey(key))
                {
                    continue;
                }

                byKey[key] = name;
            }

            return byKey.Values;
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

            // A store that already returned this field wins order of preference, but lists
            // from every exact match (Steam + IGDB, etc.) are merged before the organise step.
            // Epic, GOG and other PC libraries usually have no structured genres/features, so the
            // first allowed store that does (often Steam, in the plugin language) leads the list.
            // Collect a pool larger than Max* so a second source (e.g. IGDB Shooter) is not
            // truncated away when Steam already filled Action/Adventure.
            // When the main metadata call already organized terms, keep the model lists.
            if (settings.GenerateGenres)
            {
                if (!(termsOrganizedByMainCall && HasAnyTerms(result.Genres)))
                {
                    result.Genres = CollectStoreTerms(x => x.Genres, TermPoolSize(settings.MaxGenres), false, game);
                }
            }

            if (settings.GenerateFeatures)
            {
                if (!(termsOrganizedByMainCall && HasAnyTerms(result.Features)))
                {
                    result.Features = CollectStoreTerms(x => x.Features, TermPoolSize(settings.MaxFeatures), true, game);
                }
            }

            if (settings.GenerateTags)
            {
                if (!(termsOrganizedByMainCall && HasAnyTerms(result.Tags)))
                {
                    result.Tags = CollectStoreTerms(x => x.Tags, TermPoolSize(20), false, game);
                }
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
            return "You localize PC game system requirements. " +
                   "Output language: " + languageName + " (" + settings.Language + "). " +
                   "Return ONLY a valid JSON object matching the requested schema, without markdown formatting blocks. " +
                   "Output Schema: {\"minimumSystemRequirements\": \"string\", \"recommendedSystemRequirements\": \"string\"}. " +
                   "Rules: " +
                   "1. Formatting and Line Structure: Each requirement must be on its own line using the format \"Label: value\". " +
                   "Separate lines inside the JSON string exclusively with escaped newlines (\\n). Do NOT use raw unescaped control characters. " +
                   "Clean Plain Text: Strip all HTML tags (e.g., <br>, <strong>) and markdown from the source text. Output pure plain text only. " +
                   "Line Integrity: Preserve the original line count and order. Do not merge, add, or omit lines. " +
                   "2. Translation Scope: Translate requirement labels into " + languageName +
                   " (e.g., OS, Processor, Memory, Graphics, Storage, Additional Notes, or target language equivalents). " +
                   "Translate connecting phrases (e.g., \"or equivalent\", \"required\", \"broadband internet connection\"). " +
                   "NEVER translate hardware models, brand names, architecture, SKUs, or specs " +
                   "(keep Intel, AMD, NVIDIA, GeForce, Radeon, DirectX, GHz, GB, MB, 64-bit strictly as-is). " +
                   "3. Grounding: Do NOT invent or alter any technical specifications. If an input field is empty, return an empty string.";
        }

        private string BuildSystemRequirementsLocalizationUserPrompt(string minimum, string recommended, string language)
        {
            var languageName = TargetLanguageName(language);
            return "Localize these store system requirements into " + languageName + " (" + (language ?? string.Empty) + ").\n" +
                   "Preserve exact line count, facts, hardware names, and numbers. Strip any HTML formatting.\n\n" +
                   "minimumSystemRequirements:\n\"\"\"\n" + (minimum ?? string.Empty) + "\n\"\"\"\n\n" +
                   "recommendedSystemRequirements:\n\"\"\"\n" + (recommended ?? string.Empty) + "\n\"\"\"";
        }

        private async Task<string> SendConstrainedPromptAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken cancellationToken)
        {
            if (settings.ProviderPreset == MetaDataIASettings.ProviderClaude)
            {
                return await SendAnthropicTextAsync(systemPrompt, userPrompt, maxTokens, cancellationToken).ConfigureAwait(false);
            }

            return await SendOpenAICompatibleTextAsync(systemPrompt, userPrompt, maxTokens, SupportsJsonObjectResponse(), true, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> SendOpenAICompatibleTextAsync(string systemPrompt, string userPrompt, int maxTokens, bool jsonObject, bool allowReasoning, CancellationToken cancellationToken)
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
            ApplyOpenAiSampling(request, allowReasoning);

            if (jsonObject)
            {
                request["response_format"] = JObject.FromObject(new { type = "json_object" });
            }

            Func<HttpRequestMessage> createMessage = () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, ProviderEndpointHelper.ResolveChatCompletionsUri(settings.Endpoint));
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                }

                message.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                return message;
            };

            HttpResponseMessage response;
            try
            {
                response = await SendProviderRequestWithRetriesAsync(createMessage, cancellationToken).ConfigureAwait(false);
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

            using (response)
            {
                ProviderUsageService.CaptureResponseHeaders(settings, response);
                var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode == 400 && jsonObject)
                    {
                        return await SendOpenAICompatibleTextAsync(systemPrompt, userPrompt, maxTokens, false, allowReasoning && !ResponseRejectsReasoningEffort(responseText), cancellationToken).ConfigureAwait(false);
                    }

                    if ((int)response.StatusCode == 400 && allowReasoning && ResponseRejectsReasoningEffort(responseText))
                    {
                        return await SendOpenAICompatibleTextAsync(systemPrompt, userPrompt, maxTokens, false, false, cancellationToken).ConfigureAwait(false);
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

            Func<HttpRequestMessage> createMessage = () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    message.Headers.Add("x-api-key", settings.ApiKey);
                }

                message.Headers.Add("anthropic-version", "2023-06-01");
                message.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                return message;
            };

            HttpResponseMessage response;
            try
            {
                response = await SendProviderRequestWithRetriesAsync(createMessage, cancellationToken).ConfigureAwait(false);
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

            using (response)
            {
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
                var fromOfficial = new MetadataFieldProvenance
                {
                    Field = field,
                    Source = official.SourceName,
                    Method = editorial ? "ai-normalized" : "trusted-context",
                    Confidence = official.IsExactMatch ? "high" : "medium",
                    Detail = editorial
                        ? "The source was supplied as factual context and the AI normalized it."
                        : "The value was constrained by trusted source context."
                };
                return editorial ? AttachAiEndpoint(fromOfficial) : fromOfficial;
            }

            if (hasExisting && !MetaDataIASettings.IsExistingMetadataIgnored(settings.ExistingMetadataMode))
            {
                return AttachAiEndpoint(new MetadataFieldProvenance
                {
                    Field = field,
                    Source = "Existing Playnite metadata",
                    Method = "ai-normalized",
                    Confidence = "medium",
                    Detail = "Current library metadata was supplied as context and normalized by the AI."
                });
            }

            return AttachAiEndpoint(new MetadataFieldProvenance
            {
                Field = field,
                Source = "AI provider: " + settings.ProviderPreset,
                Method = "generated-from-identity",
                Confidence = "low",
                Detail = "No field-specific trusted source was available. Review this value before applying it."
            });
        }

        private MetadataFieldProvenance AttachAiEndpoint(MetadataFieldProvenance item)
        {
            if (item == null)
            {
                return null;
            }

            item.Provider = settings == null ? string.Empty : (settings.ProviderPreset ?? string.Empty).Trim();
            item.Model = settings == null ? string.Empty : (settings.Model ?? string.Empty).Trim();
            return item;
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
            LogOfficialSources(game);
            LogMetadataRequest(game, userPrompt);
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

            Func<HttpRequestMessage> createMessage = () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    message.Headers.Add("x-api-key", settings.ApiKey);
                }

                message.Headers.Add("anthropic-version", "2023-06-01");
                message.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                return message;
            };

            HttpResponseMessage response;
            try
            {
                response = await SendProviderRequestWithRetriesAsync(createMessage, cancellationToken).ConfigureAwait(false);
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

            using (response)
            {
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
                LogMetadataResultSummary(game, result);
                await ResolveTermFieldsAsync(result, game, cancellationToken).ConfigureAwait(false);
                result.ApplyConfiguredPrefixes(settings, Names(game == null ? null : game.Tags), Names(game == null ? null : game.Categories));
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
            var parts = new List<string>();
            parts.Add(
                "You are a video game metadata editor for Playnite. Return ONLY valid JSON matching jsonShape, without markdown blocks. " +
                "Core Rules: " +
                "1. Target Language: Use targetLanguage / targetLanguageName for all user-facing text, descriptions, and list labels. " +
                "If playniteLibraryVocabulary is provided for a field: choose values ONLY from that exact list. " +
                "If both a translated term and an English term exist in that list for the same concept (e.g., \"Aventura\" vs \"Adventure\"), ALWAYS pick the one matching targetLanguage. " +
                "If no value in playniteLibraryVocabulary fits the concept, omit the item. " +
                "If preferredSpellings lists a spelling for a concept already grounded in officialStoreContext or termCandidates, reuse that exact spelling. " +
                "Never invent labels from preferredSpellings alone. " +
                "2. Factual Grounding (No Hallucinations): Normalize, translate, and structure only facts present in officialStoreContext, existing metadata, game source, platforms, termCandidates, or game identity. " +
                "Do NOT invent companies, dates, or tags. If uncertain, leave the field empty. " +
                "Exception: similarGamesList may suggest well-known titles if requested. " +
                "If officialStoreContextEnabled is true and officialStoreContext is missing, be conservative and leave uncertain factual fields empty. " +
                "3. Source Priority: If officialStoreContext is present, treat it as the primary factual source for descriptions, companies, ratings, and links. " +
                "If existing metadata is included, treat it as secondary context only — do not copy it blindly over trusted store data. " +
                "4. Constraints and Schema Types: Obey tone, length, tokenLengths, blacklist, keepLoanwords, and prefixes. " +
                "blacklist: do not use those exact words or phrases (case-insensitive) as labels or as leftover English spelling in prose. " +
                "Blacklist blocks the listed spelling, not the idea: if Action is blacklisted and the target language is Spanish, write acción — never leave the English word Action. " +
                "keepLoanwords (list fields: genres, tags, features, categories): always use the exact keepLoanwords spelling for matching labels " +
                "(e.g. Action stays Action; PvP stays PvP — never JcJ). " +
                "keepLoanwords (description prose: short, synopsis, and other text fields): only keep the listed spelling when it reads naturally as an industry loanword or acronym in the target language " +
                "(e.g. PvP, Roguelike, Indie). Do NOT force ordinary English nouns from keepLoanwords into translated sentences " +
                "(e.g. if Action is kept for lists, Spanish prose must still say \"juego de acción…\", never \"juego de Action…\"). " +
                "If the same term appears in both lists, blacklist wins. " +
                "Respond with a JSON object that contains only the keys listed in jsonShape. " +
                "Strings: short, synopsis, premise, gameplay, tone, setting, perspective, playModes, estimatedLength, similarGames, notes, recommendedFor. " +
                "ISO date string or empty: releaseDate. " +
                "Array of objects {name, url}: links. " +
                "Array of strings: features, similarGamesList, genres, tags, developers, publishers, ageRatings, regions, categories, series.");

            if (settings.GenerateDescription)
            {
                parts.Add(
                    "Description Generation Rules: " +
                    "1. Text Formatting and Cleanliness: Output pure text content: NO headings, NO titles, NO markdown/HTML, and NO field labels (e.g., never write \"Description:\" or \"Synopsis:\"). " +
                    "Ensure the JSON output is valid: format multi-paragraph text fields using standard escaped newlines (\\n\\n). Never output invalid unescaped control characters. " +
                    "2. Editorial Scope and Game Focus: Focus exclusively on the current game. Never reference, compare to, or recommend other games, franchises, or unrelated studios inside text fields (short, synopsis, premise, gameplay, tone, setting, perspective, playModes, estimatedLength, notes, recommendedFor). " +
                    "\"short\" vs \"synopsis\": \"short\" is a concise editorial hook defining what the game is. \"synopsis\" expands on premise, setting, and narrative without literally duplicating \"short\". " +
                    "3. Terminology from settings: Obey blacklist and keepLoanwords. " +
                    "Blacklist: never output those exact spellings; still describe the idea in natural target-language wording when needed. " +
                    "keepLoanwords in features arrays: use the keep-list spelling (PvP not JcJ; Action not Acción when Action is kept). " +
                    "keepLoanwords in narrative text (short, synopsis, etc.): keep only when the industry form is natural in that language (PvP, Roguelike, Indie). " +
                    "Never write awkward mixed phrases like \"juego de Action\" — use \"juego de acción\" even if Action is on the keep-list for tags/features. " +
                    "4. Array and Placeholder Handling: similarGamesList: If requested, provide 3 to 6 comparable game titles as an array of strings (names only, no sentences). Do not mention them in the narrative text fields. " +
                    "features: Populate as an array of short feature strings. NEVER create dynamic keys like \"feature_1\" or \"similar_game_1\". " +
                    "System Requirements: Do NOT return minimumSystemRequirements or recommendedSystemRequirements (handled externally by the plugin). " +
                    "5. Length Mapping (tokenLengths): " +
                    "short: Short = 1 sentence | Medium = 2-3 sentences | Long = 1 paragraph | Extra long = 2 paragraphs. " +
                    "synopsis: Short = 1 paragraph (4-6 sentences) | Medium = 2 paragraphs | Long = 3 paragraphs | Extra long = 4-5 paragraphs. Paragraphs must be substantial and separated by \\n\\n. " +
                    "other text fields: Short = 1 sentence | Medium = 1 paragraph (3-5 sentences) | Long = 2 paragraphs | Extra long = 3 paragraphs. " +
                    "lists: Short = minimal essentials | Medium = balanced coverage | Long = broad coverage | Extra long = comprehensive (up to max items).");
            }

            if (NeedsTermOrganizeInMainCall())
            {
                // keepLoanwords is supplied in the request JSON from GamingLoanwordVocabulary / settings.
                parts.Add(TermFieldPrompts.MainCallFieldCategorizationRules(settings.MaxFeatures));
            }
            else if (settings.GenerateFeatures && settings.GenerateDescription)
            {
                parts.Add(
                    "Features Generation Rules (standalone): " +
                    "1. Formatting and Scope: Follow a Steam-like feature style in the requested target language. " +
                    "Very short, scannable labels (1 to 5 words max). No full sentences, no explanations, no final periods. " +
                    "Never output genres, story synopsis, or marketing adjectives as features. Confine strictly to functional, technical, and gameplay mode traits: " +
                    "e.g., controls, local/online multiplayer, co-op, achievements, cloud saves, and controller support. " +
                    "2. Mandatory Localization: If target language is NOT English: translate common nouns and descriptive feature labels into the requested language " +
                    "(e.g., Single-player -> Un jugador, Full controller support -> Compatibilidad total con mando, or the equivalent). " +
                    "Keep labels listed in keepLoanwords unchanged. " +
                    "Never leave raw English descriptive feature labels when target language is not English. " +
                    "3. Grounding and Count: Output between 3 and " + settings.MaxFeatures + " concrete, factually verified features based on source and platforms context. " +
                    "If there is not enough reliable evidence to confirm at least 3 features, return ONLY the verified ones or leave the array empty. Do NOT invent unsupported platform capabilities or features.");
            }

            if (settings.GenerateCategories)
            {
                parts.Add(
                    "Categories Generation Rules: " +
                    "1. Nature and Scope: Categories are high-level Playnite library grouping buckets, NOT granular store tags. " +
                    "Assign broad, objective organizational themes only when they clearly apply (equivalents in the target language of themes such as Retro, Multiplayer, Narrative, Local Co-op). " +
                    "Never infer or assign personal play-status categories (do NOT output Backlog, Completed, Playing, or similar user-state labels). " +
                    "2. Mandatory Localization: Output all category names strictly in the requested target language (targetLanguage / targetLanguageName). " +
                    "If the target language is NOT English, never output raw English category terms. " +
                    "If playniteLibraryVocabulary is locked for categories, use only matching spellings from that list. " +
                    "3. Formatting and Restraint: Short, reusable category names (1 to 3 words max). " +
                    "Select only 1 to 3 high-confidence categories. If none clearly apply, return an empty array. Do not invent niche categories.");
            }

            if (settings.GenerateLinks)
            {
                parts.Add(
                    "Links Generation Rules: " +
                    "1. Schema and Object Shape: links must be an array of objects matching {\"name\": \"string\", \"url\": \"string\"}. " +
                    "Output at most " + settings.MaxLinks + " links. " +
                    "2. Source Reliability and Grounding: Extract URLs strictly from officialStoreContext or verified official sources: " +
                    "official game website, official store page, official Discord, official wiki, or publisher support. " +
                    "NEVER guess or fabricate URLs. Never use search engine query links. If verifiable URLs are missing from context, return an empty array. " +
                    "3. Localization and Naming: Localize the name property according to the requested target language " +
                    "(e.g., official site / store page / Discord / wiki equivalents in that language).");
            }

            if (settings.GenerateDevelopers || settings.GeneratePublishers || settings.GenerateAgeRatings || settings.GenerateRegions)
            {
                parts.Add(
                    "Companies, Ratings and Regions Rules: " +
                    "1. Developers and Publishers: Return strictly the primary lead creator studio and main publisher. " +
                    "Exclude support, porting, remaster, QA, localization, outsourced multiplayer, or regional distribution studios " +
                    "unless explicitly credited as a primary co-developer and limits allow. " +
                    "Obey maxDevelopers and maxPublishers. If maxDevelopers is 1, return ONLY the single primary lead studio. " +
                    "Keep original company names unchanged. Never translate studio or publisher names. " +
                    "2. Age Ratings and Regions: ageRatings: Output standard official rating board acronyms/categories (e.g., ESRB, PEGI, CERO) only when verified. " +
                    "regions: Output standard region or market names in the target language when applicable; keep well-known English region labels only when that is the local convention. " +
                    "3. Strict Verification: Prioritize accuracy over completeness. " +
                    "If strictCompanyAgeRegion is true or if context lacks verified data, leave uncertain fields as empty arrays. Never guess studios, ratings, or regions.");
            }

            if (settings.GenerateSeries)
            {
                parts.Add(
                    "Series / Franchises Rules: " +
                    "1. Matching and Source Priority: Reuse the EXACT spelling from existing.series, knownSeriesCandidates, or officialStoreContext whenever an explicit match exists. " +
                    "Do NOT create new spelling variants or punctuation tweaks. " +
                    "Never translate franchise, saga, or series names into the target language. " +
                    "2. Standalone Games and Restraint: If the game is a standalone title and has no verified franchise continuity, return an empty array. " +
                    "Do NOT invent series names based solely on the game title, theme, or developer. " +
                    "3. Granularity and Count: Return at most 1 canonical series name (up to 2 only for official crossover titles).");
            }

            return string.Join(" ", parts);
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
            context["tone"] = settings.GenerateDescription ? NormalizeToneForPrompt(settings.Tone) : null;
            context["length"] = settings.GenerateDescription ? NormalizeLengthForPrompt(settings.Length) : null;
            if (settings.GenerateDescription)
            {
                context["tokenLengths"] = BuildTokenLengths();
            }
            context["strictCompanyAgeRegion"] = true;
            var requestedTokens = settings.GenerateDescription
                ? ExtractTemplateTokens(settings.ResolveTemplate(game))
                : new List<string>();
            var fieldsToGenerate = BuildFieldsToGenerate(requestedTokens);
            context["maxDevelopers"] = settings.MaxDevelopers;
            context["maxPublishers"] = settings.MaxPublishers;
            context["knownSeriesCandidates"] = BuildKnownSeriesCandidates(game);
            context["playniteLibraryVocabulary"] = BuildPlayniteLibraryVocabulary();
            // Soft scoped preferredSpellings only — do not dump the full sessionVocabulary
            // into the model (snowball / conformity trap). Session still feeds preferredSpellings.
            context["blacklist"] = settings.GetBlacklistTerms();
            context["keepLoanwords"] = settings.GetKeptLoanwordTerms();
            context["tagPrefix"] = settings.TagPrefix;
            context["categoryPrefix"] = settings.CategoryPrefix;
            context["extraInstructions"] = settings.ExtraInstructions;
            context["requestedDescriptionTokens"] = requestedTokens;
            context["officialStoreContextEnabled"] = true;
            await LoadOfficialContextAsync(game, cancellationToken).ConfigureAwait(false);
            context["preferredSpellings"] = BuildPreferredSpellingsForMainCall(game);

            if (!MetaDataIASettings.IsExistingMetadataIgnored(settings.ExistingMetadataMode))
            {
                context["existing"] = BuildExistingMetadataForPrompt(game);
                context["existingMetadataMode"] = NormalizeExistingMetadataModeForPrompt(settings.ExistingMetadataMode);
            }

            if (officialContextForCurrentRequest.Count > 0)
            {
                context["officialStoreContext"] = officialContextForCurrentRequest.Select(x => new
                {
                    source = x.SourceName,
                    exactMatch = x.IsExactMatch,
                    url = x.StoreUrl,
                    title = x.Title,
                    description = settings.GenerateDescription ? x.Description : null,
                    genres = settings.GenerateGenres || NeedsTermOrganizeInMainCall() ? x.Genres : null,
                    features = settings.GenerateFeatures || NeedsTermOrganizeInMainCall() || ContainsToken(requestedTokens, "features") ? x.Features : null,
                    tags = settings.GenerateTags || NeedsTermOrganizeInMainCall() ? x.Tags : null,
                    developers = settings.GenerateDevelopers ? x.Developers : null,
                    publishers = settings.GeneratePublishers ? x.Publishers : null,
                    ageRating = settings.GenerateAgeRatings ? x.AgeRating : null,
                    regions = settings.GenerateRegions ? x.Regions : null,
                    releaseDate = settings.GenerateReleaseDate ? x.ReleaseDate : null,
                    series = settings.GenerateSeries ? x.Series : null,
                    minimumSystemRequirements = ContainsToken(requestedTokens, "min_sys_req") ? x.MinimumSystemRequirements : null,
                    recommendedSystemRequirements = ContainsToken(requestedTokens, "recommended_sys_req") ? x.RecommendedSystemRequirements : null,
                    links = settings.GenerateLinks ? x.Links.Select(link => new { name = link.Name, url = link.Url }).ToList() : null
                }).ToList();
            }

            // Organize genres/tags/features in the same main call when description or other
            // factual fields are requested (term-only menus use a dedicated small call instead).
            if (NeedsTermOrganizeInMainCall())
            {
                termsOrganizedByMainCall = true;
                fieldsToGenerate["genres"] = settings.GenerateGenres;
                fieldsToGenerate["tags"] = settings.GenerateTags;
                fieldsToGenerate["features"] = settings.GenerateFeatures || ContainsToken(requestedTokens, "features");
                context["termCandidates"] = BuildTermCandidatesForPrompt(game);
            }
            else
            {
                // Description-only (or other non-list) runs must not invent store lists.
                fieldsToGenerate["genres"] = false;
                fieldsToGenerate["tags"] = false;
                if (!ContainsToken(requestedTokens, "features"))
                {
                    fieldsToGenerate["features"] = false;
                }
            }

            context["fieldsToGenerate"] = fieldsToGenerate
                .Where(x => x.Value)
                .ToDictionary(x => x.Key, x => true, StringComparer.OrdinalIgnoreCase);
            context["jsonShape"] = BuildJsonShape(requestedTokens, fieldsToGenerate);

            return "Generate normalized metadata for this game. The requested output language is " +
                   TargetLanguageName(settings.Language) + " (" + settings.Language + "). " +
                   "Context: " + JsonConvert.SerializeObject(context);
        }

        private object BuildExistingMetadataForPrompt(Game game)
        {
            return new
            {
                description = settings.GenerateDescription ? game.Description : null,
                genres = settings.GenerateGenres || NeedsTermOrganizeInMainCall() ? Names(game.Genres) : null,
                tags = settings.GenerateTags || NeedsTermOrganizeInMainCall() ? Names(game.Tags) : null,
                features = settings.GenerateFeatures || NeedsTermOrganizeInMainCall() ? Names(game.Features) : null,
                categories = settings.GenerateCategories ? Names(game.Categories) : null,
                developers = settings.GenerateDevelopers ? Names(game.Developers) : null,
                publishers = settings.GeneratePublishers ? Names(game.Publishers) : null,
                ageRatings = settings.GenerateAgeRatings ? Names(game.AgeRatings) : null,
                regions = settings.GenerateRegions ? Names(game.Regions) : null,
                releaseDate = settings.GenerateReleaseDate && game.ReleaseDate.HasValue ? game.ReleaseDate.Value.ToString() : null,
                series = settings.GenerateSeries ? Names(game.Series) : null,
                links = settings.GenerateLinks && game.Links != null
                    ? game.Links.Select(x => new { name = x.Name, url = x.Url }).Cast<object>().ToList()
                    : null
            };
        }

        private Dictionary<string, object> BuildTermCandidatesForPrompt(Game game)
        {
            var candidates = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            AddTermCandidate(candidates, game, "genres", settings.GenerateGenres, settings.GenresApplyMode, Names(game.Genres), x => x.Genres, settings.MaxGenres, false);
            AddTermCandidate(candidates, game, "tags", settings.GenerateTags, settings.TagsApplyMode, Names(game.Tags), x => x.Tags, settings.MaxTags, false);
            AddTermCandidate(candidates, game, "features", settings.GenerateFeatures, settings.FeaturesApplyMode, Names(game.Features), x => x.Features, settings.MaxFeatures, true);
            return candidates;
        }

        private void AddTermCandidate(
            Dictionary<string, object> candidates,
            Game game,
            string field,
            bool generate,
            string applyMode,
            List<string> existing,
            Func<OfficialStoreMetadata, List<string>> storeSelector,
            int maxItems,
            bool filterFeatures)
        {
            if (!generate || candidates == null)
            {
                return;
            }

            var incoming = CollectStoreTerms(storeSelector, TermPoolSize(maxItems), filterFeatures, game);
            var request = BuildTermRequest(game, field, generate, applyMode, existing, incoming, maxItems, storeSelector);
            if (string.Equals(request.Mode, "skip", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            candidates[field] = new
            {
                mode = request.Mode,
                existing = request.Existing,
                incoming = request.Incoming,
                max = request.MaxItems,
                alreadyInLanguage = request.AlreadyInLanguage,
                organize = request.Organize
            };
        }

        private async Task LoadOfficialContextAsync(Game game, CancellationToken cancellationToken)
        {
            officialContextForCurrentRequest = new List<OfficialStoreMetadata>();
            if (game == null)
            {
                return;
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

            {
                var officialContext = await new OfficialStoreDataService(settings).GetOfficialContextsAsync(game, cancellationToken).ConfigureAwait(false);
                officialContextForCurrentRequest.AddRange(officialContext);
            }

            // When IGDB metadata is enabled, always try it as an enrichment source even if
            // Steam/PSN/Xbox already returned genres or tags. Store lists stay primary;
            // IGDB genres/themes/keywords are merged and organised with them later.
            if (CanQueryIgdb() && settings.UseIgdbMetadata)
            {
                var igdbContext = await new IgdbMetadataContextService(settings).GetContextAsync(game, cancellationToken).ConfigureAwait(false);
                if (igdbContext != null && igdbContext.HasUsefulData())
                {
                    officialContextForCurrentRequest.Add(igdbContext);
                }
            }

            if (settings.UseIgnMetadata)
            {
                await TryAddOptionalContextAsync(() => new IgnDataService().GetContextAsync(game, cancellationToken), cancellationToken).ConfigureAwait(false);
            }

            if (settings.UseVndbMetadata)
            {
                await TryAddOptionalContextAsync(() => new VndbMetadataService().GetContextAsync(game, cancellationToken), cancellationToken).ConfigureAwait(false);
            }

            if (settings.UseWikidataMetadata &&
                !HasContextSource(MetaDataIASettings.SourceWikidata))
            {
                await TryAddOptionalContextAsync(() => new WikidataMetadataService().GetContextAsync(game, cancellationToken), cancellationToken).ConfigureAwait(false);
            }

            if (settings.UsePcGamingWikiMetadata &&
                !HasContextSource(MetaDataIASettings.SourcePcGamingWiki))
            {
                await TryAddOptionalContextAsync(() => new PcGamingWikiMetadataService().GetContextAsync(game, cancellationToken), cancellationToken).ConfigureAwait(false);
            }

            if (settings.UseScreenScraperMetadata &&
                ScreenScraperMetadataContextService.IsConfigured(settings) &&
                !HasContextSource(MetaDataIASettings.SourceScreenScraper))
            {
                await TryAddOptionalContextAsync(
                    () => new ScreenScraperMetadataContextService(settings).GetContextAsync(game, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }

            if (settings.UseTheGamesDbMetadata &&
                TheGamesDbMetadataContextService.IsConfigured(settings) &&
                !HasContextSource(MetaDataIASettings.SourceTheGamesDb))
            {
                await TryAddOptionalContextAsync(
                    () => new TheGamesDbMetadataContextService(settings).GetContextAsync(game, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }

            if (settings.UseMobyGamesMetadata &&
                MobyGamesMetadataContextService.IsConfigured(settings) &&
                !HasContextSource(MetaDataIASettings.SourceMobyGames))
            {
                await TryAddOptionalContextAsync(
                    () => new MobyGamesMetadataContextService(settings).GetContextAsync(game, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
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
            return MetaDataIASettings.IsExistingMetadataIgnored(value) ? "Ignore" : "Use as context";
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

            if (FieldEnabled(fields, "features"))
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

        private void ApplyOpenAiSampling(JObject request, bool allowReasoning)
        {
            if (settings.ProviderPreset != MetaDataIASettings.ProviderGemini)
            {
                request["temperature"] = 0.0;
                return;
            }

            if (!allowReasoning)
            {
                return;
            }

            var effort = ResolveGeminiReasoningEffort(settings.Model);
            if (!string.IsNullOrWhiteSpace(effort))
            {
                request["reasoning_effort"] = effort;
            }
        }

        private static string ResolveGeminiReasoningEffort(string model)
        {
            var id = (model ?? string.Empty).Trim().ToLowerInvariant();
            if (id.StartsWith("gemini-3", StringComparison.Ordinal) && id.IndexOf("flash-lite", StringComparison.Ordinal) >= 0)
            {
                return "minimal";
            }

            if (id.StartsWith("gemini-2.5", StringComparison.Ordinal) || id.StartsWith("gemini-3", StringComparison.Ordinal))
            {
                return "low";
            }

            return null;
        }

        private static bool ResponseRejectsReasoningEffort(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return false;
            }

            return responseText.IndexOf("reasoning_effort", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   responseText.IndexOf("thinking level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   responseText.IndexOf("thinking_level", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private Dictionary<string, List<string>> BuildPreferredSpellingsForFields(IList<TermFieldRequest> fields)
        {
            var built = ScopedTermVocabulary.BuildForFields(fields, GetPlayniteNamesForField, SessionCache);
            return built == null || built.Count == 0 ? null : built;
        }

        private Dictionary<string, List<string>> BuildPreferredSpellingsForMainCall(Game game)
        {
            if (!NeedsTermOrganizeInMainCall())
            {
                return null;
            }

            var fields = new List<TermFieldRequest>
            {
                BuildTermRequest(game, "genres", settings.GenerateGenres, settings.GenresApplyMode, Names(game.Genres), null, settings.MaxGenres, x => x.Genres),
                BuildTermRequest(game, "features", settings.GenerateFeatures, settings.FeaturesApplyMode, Names(game.Features), null, settings.MaxFeatures, x => x.Features),
                BuildTermRequest(game, "tags", settings.GenerateTags, settings.TagsApplyMode, Names(game.Tags), null, settings.MaxTags, x => x.Tags)
            };
            return BuildPreferredSpellingsForFields(fields.Where(x => !string.Equals(x.Mode, "skip", StringComparison.OrdinalIgnoreCase)).ToList());
        }

        private IEnumerable<string> GetPlayniteNamesForField(string field)
        {
            if (playniteApi == null || playniteApi.Database == null || string.IsNullOrWhiteSpace(field))
            {
                return Enumerable.Empty<string>();
            }

            if (string.Equals(field, "genres", StringComparison.OrdinalIgnoreCase))
            {
                return Names(playniteApi.Database.Genres);
            }

            if (string.Equals(field, "tags", StringComparison.OrdinalIgnoreCase))
            {
                return Names(playniteApi.Database.Tags);
            }

            if (string.Equals(field, "features", StringComparison.OrdinalIgnoreCase))
            {
                return Names(playniteApi.Database.Features);
            }

            if (string.Equals(field, "categories", StringComparison.OrdinalIgnoreCase))
            {
                return Names(playniteApi.Database.Categories);
            }

            return Enumerable.Empty<string>();
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
            return new Dictionary<string, List<string>>();
        }

        private static List<string> MergeTermCandidates(IEnumerable<string> primary, IEnumerable<string> secondary, int maxItems)
        {
            return (primary ?? Enumerable.Empty<string>())
                .Concat(secondary ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxItems))
                .ToList();
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
            var cleaned = content == null ? string.Empty : content.Trim();
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
                var loose = ParseLooseResult(AiResponseJson.PrepareForLooseParse(cleaned));
                if (HasUsefulData(loose))
                {
                    return loose;
                }

                throw parseError ?? new InvalidOperationException(Loc("MTDA_ErrorAiResponseNotParsed", "The AI response could not be interpreted."));
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
            JObject json;
            JsonReaderException parseError;
            if (AiResponseJson.TryParseObject(content, out json, out parseError))
            {
                return json;
            }

            throw CreateMalformedJsonException(parseError);
        }

        private static Exception CreateMalformedJsonException(JsonReaderException error)
        {
            var detail = error == null
                ? Loc("MTDA_ErrorAiResponseNotParsed", "The AI response could not be interpreted.")
                : error.Message;
            // Soft failure: one bad model response must not abort a multi-game batch.
            return new AiProviderException(
                Loc("MTDA_ErrorMalformedAiJson", "The AI returned a response with an invalid format and it could not be interpreted.\n\nThis game was skipped; other games in the batch should still be processed. You can retry this game, reduce text length, or switch to a model that follows JSON more reliably.\n\nBrief detail: ") + SanitizeForUser(detail),
                false,
                detail);
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

        private static async Task<HttpResponseMessage> SendProviderRequestWithRetriesAsync(
            Func<HttpRequestMessage> createMessage,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = null;
            for (var attempt = 0; attempt <= ProviderRateLimitRetries; attempt++)
            {
                if (response != null)
                {
                    response.Dispose();
                    response = null;
                }

                using (var message = createMessage())
                {
                    response = await SharedHttpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
                }

                var statusCode = (int)response.StatusCode;
                // Only 429 is retried here. 503 is soft-failed to the caller so organize/knowledge
                // can do a single delayed retry without stacking multi-minute backoff in a batch.
                if (statusCode != 429 || attempt >= ProviderRateLimitRetries)
                {
                    return response;
                }

                var delaySeconds = ResolveRetryAfterSeconds(response) ?? (int)Math.Pow(2, attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, delaySeconds)), cancellationToken).ConfigureAwait(false);
            }

            return response;
        }

        private static int? ResolveRetryAfterSeconds(HttpResponseMessage response)
        {
            if (response == null || response.Headers == null || response.Headers.RetryAfter == null)
            {
                return null;
            }

            if (response.Headers.RetryAfter.Delta.HasValue)
            {
                return (int)Math.Ceiling(response.Headers.RetryAfter.Delta.Value.TotalSeconds);
            }

            return null;
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

            var isModelNotFound =
                string.Equals(providerCode, "model_not_found", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(providerCode, "invalid_model", StringComparison.OrdinalIgnoreCase) ||
                providerMessage.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (providerMessage.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 providerMessage.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isModelNotFound)
            {
                return new AiProviderException(
                    AppendProviderDetail(
                        Loc("MTDA_ErrorProviderModelNotFound", "The configured provider or model does not exist, or is not available for your account.\n\nCheck that the provider, endpoint and model name are written correctly. If you typed the model manually, copy the exact name from the provider documentation or console.\n\nExamples:\n- Gemini: gemini-3.5-flash-lite\n- Ollama: the name shown by 'ollama list'\n- LM Studio: the model loaded in the local server"),
                        providerMessage),
                    true,
                    responseText);
            }

            if (statusCode == 404)
            {
                return new AiProviderException(
                    AppendProviderDetail(
                        Loc("MTDA_ErrorProviderEndpointNotFound", "The configured endpoint returned HTTP 404 (not found).\n\nCheck that the endpoint is written correctly. For Custom OpenAI-compatible providers you can enter either the base URL (for example https://api.deepseek.com or https://api.openai.com/v1) or the full chat completions URL ending in /chat/completions."),
                        providerMessage),
                    true,
                    responseText);
            }

            if (statusCode == 429)
            {
                // Retries already happened in SendProviderRequestWithRetriesAsync.
                // Stop the batch slot so the runner can fail over to the next provider profile.
                return new AiProviderException(
                    AppendProviderDetail(
                        Loc("MTDA_ErrorProviderRateLimit", "The AI provider has temporarily limited requests.\n\nTry waiting a few minutes, processing fewer games at once, or using a model/local endpoint with fewer restrictions."),
                        providerMessage),
                    true,
                    responseText);
            }

            if (statusCode == 503 ||
                providerMessage.IndexOf("high demand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                providerMessage.IndexOf("overloaded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                providerMessage.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Stop the multi-game batch after retries: remaining games stay untouched and
                // appear in the batch results dialog so the user can retry when it recovers.
                return new AiProviderException(
                    AppendProviderDetail(
                        Loc("MTDA_ErrorProviderUnavailable", "The AI provider is overloaded or the selected model is temporarily unavailable.\n\nIf you are using Gemini, this can happen even if you have Gemini Pro/Google AI Pro in the app: the Gemini API has its own limits and availability, separate from the app subscription.\n\nWhat you can do without paying:\n- Wait a few minutes and try again.\n- Switch to gemini-3.5-flash-lite if you were using another model.\n- Process fewer games at once.\n- Use LM Studio or Ollama locally if you want to avoid external quotas."),
                        providerMessage),
                    true,
                    responseText);
            }

            var invalidApiKey = providerMessage.IndexOf("api key", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (providerMessage.IndexOf("not valid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 providerMessage.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0);
            if (statusCode == 401 || statusCode == 403 || (statusCode == 400 && invalidApiKey))
            {
                return new AiProviderException(
                    AppendProviderDetail(
                        Loc("MTDA_ErrorProviderAuth", "The AI provider did not accept the authentication.\n\nCheck the API key, endpoint and configured model. If you use LM Studio or Ollama locally, the API key can usually be empty."),
                        providerMessage),
                    true,
                    responseText);
            }

            return new AiProviderException(
                AppendProviderDetail(
                    string.Format(Loc("MTDA_ErrorProviderGeneric", "The AI provider returned an error ({0}).\n\nCheck the configured provider, endpoint, model and API key. If the problem continues, try another model or a local provider."), statusCode),
                    providerMessage),
                false,
                responseText);
        }

        private static string AppendProviderDetail(string message, string providerMessage)
        {
            var detail = TruncateProviderDetail(providerMessage);
            if (string.IsNullOrWhiteSpace(detail))
            {
                return message;
            }

            return message + "\n\n" + Loc("MTDA_ErrorProviderDetail", "Provider detail:") + " " + detail;
        }

        private static string TruncateProviderDetail(string providerMessage)
        {
            if (string.IsNullOrWhiteSpace(providerMessage))
            {
                return string.Empty;
            }

            var text = providerMessage.Trim().Replace("\r", " ").Replace("\n", " ");
            while (text.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                text = text.Replace("  ", " ");
            }

            if (text.Length > 300)
            {
                text = text.Substring(0, 300).Trim() + "...";
            }

            return text;
        }

        private static Exception CreateConnectionException(Exception ex)
        {
            return new AiProviderException(
                Loc("MTDA_ErrorProviderConnection", "Could not connect to the configured provider.\n\nCheck that the endpoint is written correctly and that the provider exists. If you use LM Studio or Ollama, make sure the app is open, the local server is active, and the model is loaded or downloaded.\n\nBrief detail: ") + SanitizeForUser(ex == null ? string.Empty : ex.Message),
                true,
                ex == null ? string.Empty : ex.ToString());
        }

        private async Task TryAddOptionalContextAsync(Func<Task<OfficialStoreMetadata>> fetch, CancellationToken cancellationToken)
        {
            try
            {
                var context = await fetch().ConfigureAwait(false);
                if (context != null && context.HasUsefulData())
                {
                    officialContextForCurrentRequest.Add(context);
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
            }
            catch
            {
            }
        }

        private void ApplyStrictFactualGuard(AiMetadataResult result, Game game)
        {
            if (result == null)
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

        private async Task ResolveTermFieldsAsync(AiMetadataResult result, Game game, CancellationToken cancellationToken)
        {
            if (result == null || game == null || settings == null)
            {
                return;
            }

            var requests = new List<TermFieldRequest>
            {
                BuildTermRequest(game, "genres", settings.GenerateGenres, settings.GenresApplyMode, Names(game.Genres), result.Genres, settings.MaxGenres, x => x.Genres),
                BuildTermRequest(game, "features", settings.GenerateFeatures, settings.FeaturesApplyMode, Names(game.Features), result.Features, settings.MaxFeatures, x => x.Features),
                BuildTermRequest(game, "tags", settings.GenerateTags, settings.TagsApplyMode, Names(game.Tags), result.Tags, settings.MaxTags, x => x.Tags),
                BuildTermRequest(game, "categories", settings.GenerateCategories, settings.CategoriesApplyMode, Names(game.Categories), result.Categories, settings.MaxCategories, null)
            };

            var active = requests.Where(x => !string.Equals(x.Mode, "skip", StringComparison.OrdinalIgnoreCase)).ToList();
            var direct = active.Where(x => !x.NeedsModel).ToList();
            var modelFields = active.Where(x => x.NeedsModel).ToList();
            foreach (var field in direct)
            {
                // Localized overwrite/append-empty and filled empty-only apply store lists
                // without a model call; keep those values on the result.
                List<string> applied;
                if (string.Equals(field.Mode, "empty", StringComparison.OrdinalIgnoreCase) && field.Existing.Count > 0)
                {
                    applied = field.DirectTerms();
                    AssignTermField(result, field.Field, applied);
                }
                else if (string.Equals(field.Mode, "overwrite", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(field.Mode, "append", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(field.Mode, "empty", StringComparison.OrdinalIgnoreCase))
                {
                    applied = field.DirectTerms();
                    AssignTermField(result, field.Field, applied);
                }
                else
                {
                    continue;
                }

                MetadataDebugLog.Info(
                    game,
                    "terms.direct",
                    field.Field + "=" + MetadataDebugLog.FormatTermList(applied) + " (no model)");
            }

            if (modelFields.Count == 0)
            {
                FinishTermFieldLists(result, game);
                return;
            }

            var organizeFields = modelFields.Where(x => !x.FromKnowledge).ToList();
            // Always keep the dedicated organize pass for genres/tags/features, even after the
            // main metadata call: that call's termCandidates wording is weaker and often leaves
            // English store/IGDB labels that would otherwise skip this stricter prompt.

            var knowledgeFields = modelFields.Where(x => x.FromKnowledge).ToList();
            var insufficientLocalFields = active.Where(x => x.SkippedInsufficientLocalText).ToList();
            LogTermOrganizeContext(game, organizeFields, knowledgeFields);

            if (insufficientLocalFields.Count > 0 &&
                organizeFields.Count == 0 &&
                knowledgeFields.Count == 0)
            {
                throw new InvalidOperationException(
                    Loc(
                        "MTDA_ErrorLocalTermTextInsufficient",
                        "Could not derive genres, tags or features: no store/IGDB list was available and the local description/game text is too thin. Add or generate a richer description, then retry."));
            }

            var resolved = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (organizeFields.Count > 0)
            {
                var userJson = TermFieldResolver.BuildUserJson(
                    settings.Language,
                    PlatformLabels(game),
                    organizeFields,
                    settings.GetKeptLoanwordTerms(),
                    null,
                    BuildPreferredSpellingsForFields(organizeFields));
                foreach (var pair in await AskTermModelAsync(
                    TermFieldResolver.SystemPrompt,
                    userJson,
                    organizeFields,
                    game,
                    "organize",
                    cancellationToken).ConfigureAwait(false))
                {
                    resolved[pair.Key] = pair.Value;
                }
            }

            if (knowledgeFields.Count > 0)
            {
                var knowledgeJson = TermFieldResolver.BuildKnowledgeJson(
                    settings.Language,
                    BuildKnownGameFacts(game),
                    knowledgeFields,
                    settings.GetKeptLoanwordTerms());
                foreach (var pair in await AskTermModelAsync(
                    TermFieldResolver.KnowledgePrompt,
                    knowledgeJson,
                    knowledgeFields,
                    game,
                    "knowledge",
                    cancellationToken).ConfigureAwait(false))
                {
                    resolved[pair.Key] = pair.Value;
                }
            }

            if (result.ResolvedTermFields == null)
            {
                result.ResolvedTermFields = new List<string>();
            }

            foreach (var field in modelFields)
            {
                List<string> terms;
                if (resolved == null || !resolved.TryGetValue(field.Field, out terms))
                {
                    terms = field.FailedOrganizeTerms();
                }

                AssignTermField(result, field.Field, terms);
                if (!result.ResolvedTermFields.Any(x => string.Equals(x, field.Field, StringComparison.OrdinalIgnoreCase)))
                {
                    result.ResolvedTermFields.Add(field.Field);
                }
            }

            // Organize / DirectTerms / English FailedOrganizeTerms assign raw store+IGDB
            // lists after Normalize, so blacklist and keep-list must run again here.
            FinishTermFieldLists(result, game);
        }

        private void FinishTermFieldLists(AiMetadataResult result, Game game)
        {
            if (result == null || settings == null)
            {
                return;
            }

            // Drop pollution from Normalize()'s description fallback when this run
            // did not resolve that field.
            if (!settings.GenerateFeatures &&
                (result.ResolvedTermFields == null ||
                 !result.ResolvedTermFields.Any(x => string.Equals(x, "features", StringComparison.OrdinalIgnoreCase))))
            {
                result.Features = new List<string>();
            }

            if (!settings.GenerateTags &&
                (result.ResolvedTermFields == null ||
                 !result.ResolvedTermFields.Any(x => string.Equals(x, "tags", StringComparison.OrdinalIgnoreCase))))
            {
                result.Tags = new List<string>();
            }

            if (!settings.GenerateCategories &&
                (result.ResolvedTermFields == null ||
                 !result.ResolvedTermFields.Any(x => string.Equals(x, "categories", StringComparison.OrdinalIgnoreCase))))
            {
                result.Categories = new List<string>();
            }

            result.ApplyTermBlacklist(settings);
            result.ApplyKeptLoanwords(settings);

            // Harvest only final labels for this game (not raw Steam dumps) so the
            // next sequential game can prefer spellings already chosen/applied.
            if (SessionCache != null)
            {
                SessionCache.RememberLocalizedStoreTerms("genres", result.Genres);
                SessionCache.RememberLocalizedStoreTerms("tags", result.Tags);
                SessionCache.RememberLocalizedStoreTerms("features", result.Features);
                SessionCache.RememberLocalizedStoreTerms("categories", result.Categories);
            }

            if (settings.GenerateGenres || settings.GenerateTags ||
                settings.GenerateFeatures || settings.GenerateCategories)
            {
                MetadataDebugLog.Info(
                    game,
                    "terms.final",
                    "genres=" + MetadataDebugLog.FormatTermList(result.Genres) + "\n" +
                    "tags=" + MetadataDebugLog.FormatTermList(result.Tags) + "\n" +
                    "features=" + MetadataDebugLog.FormatTermList(result.Features) + "\n" +
                    "categories=" + MetadataDebugLog.FormatTermList(result.Categories));
            }

            result.RefreshDescription(settings, game);
        }

        private void LogOfficialSources(Game game)
        {
            if (game == null)
            {
                return;
            }

            var sources = (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.SourceName))
                .ToList();
            if (sources.Count == 0)
            {
                MetadataDebugLog.Info(game, "metadata.sources", "(none returned for this game)");
                return;
            }

            if (!MetadataDebugLog.Verbose)
            {
                MetadataDebugLog.Info(
                    game,
                    "metadata.sources",
                    string.Join(", ", sources.Select(x => x.SourceName).Distinct(StringComparer.OrdinalIgnoreCase)));
                return;
            }

            var details = new StringBuilder();
            foreach (var source in sources)
            {
                details.Append("  - ")
                    .Append(source.SourceName ?? "?")
                    .Append(" title=")
                    .Append(source.Title ?? string.Empty)
                    .Append(" exactMatch=")
                    .Append(source.IsExactMatch)
                    .Append(" matchLanguage=")
                    .Append(source.ListsMatchPluginLanguage)
                    .Append(" descriptionChars=")
                    .Append(string.IsNullOrEmpty(source.Description) ? 0 : source.Description.Length)
                    .Append(" genres=")
                    .Append(MetadataDebugLog.FormatTermList(source.Genres))
                    .Append(" tags=")
                    .Append(MetadataDebugLog.FormatTermList(source.Tags))
                    .Append(" features=")
                    .Append(MetadataDebugLog.FormatTermList(source.Features))
                    .AppendLine();
            }

            MetadataDebugLog.Info(game, "metadata.sources", details.ToString().TrimEnd());
        }

        private void LogMetadataRequest(Game game, string userPrompt, string topic = "metadata.request")
        {
            if (game == null || settings == null)
            {
                return;
            }

            var fields = new List<string>();
            if (settings.GenerateDescription) fields.Add("description");
            if (settings.GenerateGenres) fields.Add("genres");
            if (settings.GenerateTags) fields.Add("tags");
            if (settings.GenerateFeatures) fields.Add("features");
            if (settings.GenerateCategories) fields.Add("categories");
            if (settings.GenerateDevelopers) fields.Add("developers");
            if (settings.GeneratePublishers) fields.Add("publishers");
            if (settings.GenerateAgeRatings) fields.Add("ageRatings");
            if (settings.GenerateRegions) fields.Add("regions");
            if (settings.GenerateLinks) fields.Add("links");
            if (settings.GenerateReleaseDate) fields.Add("releaseDate");
            if (settings.GenerateSeries) fields.Add("series");
            if (settings.GenerateSortingName) fields.Add("sortingName");

            var summary =
                "model=" + (settings.Model ?? string.Empty) +
                " language=" + (settings.Language ?? string.Empty) +
                " provider=" + (settings.ProviderPreset ?? string.Empty) +
                " fields=" + (fields.Count == 0 ? "(none)" : string.Join(",", fields)) +
                " promptChars=" + (userPrompt == null ? 0 : userPrompt.Length) +
                " verbose=" + MetadataDebugLog.Verbose;

            MetadataDebugLog.Info(
                game,
                topic,
                summary,
                userPrompt ?? string.Empty);
        }

        private void LogMetadataResultSummary(Game game, AiMetadataResult result)
        {
            if (game == null || settings == null)
            {
                return;
            }

            var summary = new StringBuilder();
            string descriptionDump = null;

            if (settings.GenerateDescription)
            {
                var description = result == null ? null : result.Description;
                summary.Append("descriptionChars=")
                    .Append(string.IsNullOrEmpty(description) ? 0 : description.Length);
                summary.AppendLine();
                summary.Append("description=");
                summary.Append(MetadataDebugLog.DescriptionForLog(description));
                if (MetadataDebugLog.Verbose && !string.IsNullOrEmpty(description))
                {
                    descriptionDump = description;
                }
            }

            if (settings.GenerateDevelopers)
            {
                if (summary.Length > 0) summary.AppendLine();
                summary.Append("developers=")
                    .Append(MetadataDebugLog.FormatTermList(result == null ? null : result.Developers));
            }

            if (settings.GeneratePublishers)
            {
                if (summary.Length > 0) summary.AppendLine();
                summary.Append("publishers=")
                    .Append(MetadataDebugLog.FormatTermList(result == null ? null : result.Publishers));
            }

            if (settings.GenerateAgeRatings)
            {
                if (summary.Length > 0) summary.AppendLine();
                summary.Append("ageRatings=")
                    .Append(MetadataDebugLog.FormatTermList(result == null ? null : result.AgeRatings));
            }

            if (settings.GenerateSeries)
            {
                if (summary.Length > 0) summary.AppendLine();
                summary.Append("series=")
                    .Append(MetadataDebugLog.FormatTermList(result == null ? null : result.Series));
            }

            if (settings.GenerateReleaseDate && result != null && !string.IsNullOrWhiteSpace(result.ReleaseDate))
            {
                if (summary.Length > 0) summary.AppendLine();
                summary.Append("releaseDate=").Append(result.ReleaseDate);
            }

            if (settings.GenerateSortingName && result != null && !string.IsNullOrWhiteSpace(result.SortingName))
            {
                if (summary.Length > 0) summary.AppendLine();
                summary.Append("sortingName=").Append(result.SortingName);
            }

            var sources = (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.SourceName))
                .Select(x => x.SourceName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (summary.Length > 0) summary.AppendLine();
            summary.Append("sources=")
                .Append(sources.Count == 0 ? "(none)" : string.Join(",", sources));

            MetadataDebugLog.Info(
                game,
                "metadata.result",
                summary.ToString().TrimEnd(),
                descriptionDump);
        }

        private static List<string> PlatformLabels(Game game)
        {
            if (game == null || game.Platforms == null)
            {
                return new List<string>();
            }

            return game.Platforms
                .Where(platform => platform != null)
                .Select(platform => ((platform.Name ?? string.Empty) + " " + (platform.SpecificationId ?? string.Empty)).Trim())
                .Where(label => label.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private TermFieldRequest BuildTermRequest(
            Game game,
            string field,
            bool generate,
            string applyMode,
            List<string> existing,
            List<string> current,
            int maxItems,
            Func<OfficialStoreMetadata, List<string>> storeSelector)
        {
            var mode = "skip";
            if (generate && applyMode == MetaDataIASettings.ApplyOverwrite)
            {
                mode = "overwrite";
            }
            else if (generate && applyMode == MetaDataIASettings.ApplyEmptyOnly)
            {
                mode = "empty";
            }
            else if (generate && applyMode == MetaDataIASettings.ApplyAppend)
            {
                mode = "append";
            }

            // Pool must be larger than MaxItems: Spanish Steam is preferred first and would
            // otherwise fill the entire incoming list before English IGDB concepts are merged.
            var target = string.Equals(field, "tags", StringComparison.OrdinalIgnoreCase)
                ? 20
                : TermPoolSize(maxItems);
            var filterFeatures = string.Equals(field, "features", StringComparison.OrdinalIgnoreCase);
            var incoming = storeSelector == null
                ? new List<string>()
                : CollectStoreTerms(storeSelector, target, filterFeatures, game, false);
            if (incoming.Count == 0)
            {
                incoming = TermFieldResolver.DistinctTerms(current).Take(Math.Min(20, Math.Max(maxItems, 1) * 2)).ToList();
            }

            var localizedIncoming = storeSelector == null
                ? new List<string>()
                : CollectStoreTerms(storeSelector, target, filterFeatures, game, true);
            var nonLocalizedIncoming = storeSelector == null
                ? new List<string>()
                : CollectNonLocalizedStoreTerms(storeSelector, target, filterFeatures, game);

            var fromStore = incoming.Count > 0;
            var isTermListField = string.Equals(field, "genres", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(field, "tags", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(field, "features", StringComparison.OrdinalIgnoreCase);
            var allowLocalFallback = MetaDataIASettings.AllowsLocalTermFallback(settings.LocalTermFallbackMode);
            var wantsLocalFallback = isTermListField && !fromStore && mode != "skip" && allowLocalFallback;
            var hasLocalEvidence = wantsLocalFallback && HasSufficientLocalTermEvidence(game, officialContextForCurrentRequest);
            return new TermFieldRequest
            {
                Field = field,
                Mode = mode,
                Language = settings.Language,
                Existing = TermFieldResolver.DistinctTerms(existing).Take(20).ToList(),
                Incoming = incoming,
                LocalizedIncoming = localizedIncoming,
                NonLocalizedIncoming = nonLocalizedIncoming,
                MaxItems = Math.Max(1, maxItems),
                AlreadyInLanguage = !fromStore || StoreTermsAreInPluginLanguage(storeSelector, target, filterFeatures, game),
                Organize = isTermListField,
                FromKnowledge = wantsLocalFallback && hasLocalEvidence,
                SkippedInsufficientLocalText = wantsLocalFallback && !hasLocalEvidence
            };
        }

        /// <summary>
        /// Cheap gate before the local-text knowledge call: need a usable description and/or
        /// enough local facts so the model is not inventing from the title alone.
        /// </summary>
        public static bool HasSufficientLocalTermEvidence(Game game, IList<OfficialStoreMetadata> stores = null)
        {
            if (game == null)
            {
                return false;
            }

            var descriptionChars = LongestPlainDescriptionLength(game, stores);
            var factScore = 0;
            if (PlatformLabels(game).Count > 0) factScore++;
            if (Names(game.Developers).Count > 0) factScore++;
            if (Names(game.Publishers).Count > 0) factScore++;
            if (game.ReleaseDate.HasValue) factScore++;
            if (Names(game.Series).Count > 0) factScore++;
            if (Names(game.Genres).Count > 0) factScore++;
            if (Names(game.Tags).Count > 0) factScore++;
            if (Names(game.Features).Count > 0) factScore++;
            if (game.Source != null && !string.IsNullOrWhiteSpace(game.Source.Name)) factScore++;

            if (descriptionChars >= 280)
            {
                return true;
            }

            if (descriptionChars >= 120 && factScore >= 3)
            {
                return true;
            }

            if (descriptionChars >= 40 && factScore >= 5)
            {
                return true;
            }

            return false;
        }

        private static int LongestPlainDescriptionLength(Game game, IList<OfficialStoreMetadata> stores)
        {
            var best = PlainTextLength(game == null ? null : game.Description);
            foreach (var source in stores ?? Enumerable.Empty<OfficialStoreMetadata>())
            {
                if (source == null)
                {
                    continue;
                }

                best = Math.Max(best, PlainTextLength(source.Description));
            }

            return best;
        }

        private static int PlainTextLength(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            var text = Regex.Replace(value, "<[^>]+>", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text.Length;
        }

        private List<string> CollectNonLocalizedStoreTerms(
            Func<OfficialStoreMetadata, List<string>> selector,
            int targetCount,
            bool filterFeatures,
            Game game)
        {
            var sources = (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Where(x => x != null && !IsLibraryIntegrationSource(x) && !x.ListsMatchPluginLanguage)
                .ToList();
            return CollectFromSources(sources, selector, filterFeatures, game, Math.Max(1, targetCount));
        }

        private async Task<Dictionary<string, List<string>>> AskTermModelAsync(
            string systemPrompt,
            string userJson,
            List<TermFieldRequest> fields,
            Game game,
            string callKind,
            CancellationToken cancellationToken)
        {
            Exception lastError = null;
            var keepLoanwords = settings.GetKeptLoanwordTerms();
            var topic = (callKind ?? "organize") + ".response";
            var requestTopic = (callKind ?? "organize") + ".request";
            var prettyRequest = MetadataDebugLog.PrettyJsonOrRaw(userJson);
            var requestSummary =
                "model=" + (settings.Model ?? string.Empty) +
                " language=" + (settings.Language ?? string.Empty) +
                " systemPromptChars=" + (systemPrompt == null ? 0 : systemPrompt.Length);

            for (var attempt = 0; attempt <= OrganizeProviderRetries; attempt++)
            {
                var responseTrail = new StringBuilder();
                try
                {
                    if (attempt > 0)
                    {
                        MetadataDebugLog.Warn(
                            game,
                            (callKind ?? "organize") + ".provider-retry",
                            "attempt=" + (attempt + 1) +
                            " delaySeconds=" + OrganizeProviderRetryDelaySeconds);
                        await Task.Delay(
                            TimeSpan.FromSeconds(OrganizeProviderRetryDelaySeconds),
                            cancellationToken).ConfigureAwait(false);
                    }

                    MetadataDebugLog.Info(
                        game,
                        requestTopic,
                        requestSummary,
                        "userJson:\r\n" + prettyRequest);

                    var content = await SendConstrainedPromptAsync(systemPrompt, userJson, 700, cancellationToken).ConfigureAwait(false);
                    responseTrail.AppendLine("response#1:");
                    responseTrail.AppendLine(MetadataDebugLog.PrettyJsonOrRaw(content));

                    var jsonRetried = false;
                    if (!TermFieldResolver.ResponseIsParseableJson(content))
                    {
                        jsonRetried = true;
                        MetadataDebugLog.Warn(game, topic, "not-json → json-retry");
                        var jsonRetry = userJson +
                            "\n\nRETRY: Your previous answer was not valid JSON. " +
                            "Return ONLY one JSON object that starts with { and ends with }. " +
                            "No markdown fences, no commentary, no JSON wrapped inside a string. " +
                            "Use shape {\"fields\":[{\"field\":\"genres\",\"terms\":[\"...\"]}]} with flat string arrays in \"terms\".";
                        content = await SendConstrainedPromptAsync(systemPrompt, jsonRetry, 700, cancellationToken).ConfigureAwait(false);
                        responseTrail.AppendLine("response#json-retry:");
                        responseTrail.AppendLine(MetadataDebugLog.PrettyJsonOrRaw(content));
                    }

                    var translationRetried = false;
                    if (TermFieldResolver.ResponseNeedsTranslationRetry(content, fields, keepLoanwords))
                    {
                        translationRetried = true;
                        MetadataDebugLog.Warn(game, topic, "needs-translation → translation-retry");
                        var knowledgeHint = fields.Any(f => f != null && f.FromKnowledge)
                            ? "This was a knowledge guess with no store list: every common noun MUST be in the target language. "
                            : string.Empty;
                        var retryJson = userJson + TermFieldPrompts.TranslationRetrySuffix(knowledgeHint);
                        content = await SendConstrainedPromptAsync(systemPrompt, retryJson, 700, cancellationToken).ConfigureAwait(false);
                        responseTrail.AppendLine("response#translation-retry:");
                        responseTrail.AppendLine(MetadataDebugLog.PrettyJsonOrRaw(content));
                    }

                    Dictionary<string, List<string>> resolved;
                    var accepted = TermFieldResolver.TryApplyResponse(content, fields, keepLoanwords, out resolved);
                    var shapeRetried = false;
                    // Parseable-but-unusable answers (collapsed duplicate field keys, one
                    // comma-joined terms string, missing fields) used to skip the JSON retry
                    // and fail the game. Give the model one shape correction pass.
                    if (!accepted)
                    {
                        shapeRetried = true;
                        MetadataDebugLog.Warn(game, topic, "unusable-shape → shape-retry");
                        var fieldShape = string.Join(
                            ",",
                            (fields ?? new List<TermFieldRequest>()).Select(f =>
                                "{\"field\":\"" + (f.Field ?? string.Empty) + "\",\"terms\":[\"...\"]}"));
                        var shapeRetry = userJson +
                            "\n\nRETRY: Your previous answer was unusable for this extension. " +
                            "Return ONLY one JSON object that starts with { and ends with }. " +
                            "Shape: {\"fields\":[" + fieldShape + "]} " +
                            "Echo ONLY the requested field names. Each field must be its own object — never put two \"field\" keys in the same object. " +
                            "\"terms\" must be a flat array of separate short strings (never one comma-joined string). " +
                            "For append mode, you may return only new incoming labels; existing labels are preserved by the plugin. " +
                            "Organize strictly from each field's existing and incoming lists. Do not invent labels.";
                        content = await SendConstrainedPromptAsync(systemPrompt, shapeRetry, 700, cancellationToken).ConfigureAwait(false);
                        responseTrail.AppendLine("response#shape-retry:");
                        responseTrail.AppendLine(MetadataDebugLog.PrettyJsonOrRaw(content));
                        accepted = TermFieldResolver.TryApplyResponse(content, fields, keepLoanwords, out resolved);
                    }

                    resolved = resolved ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    var resolvedSummary =
                        "accepted=" + accepted +
                        " retries=json:" + (jsonRetried ? "1" : "0") +
                        " translate:" + (translationRetried ? "1" : "0") +
                        " shape:" + (shapeRetried ? "1" : "0") +
                        "\n" + FormatResolvedTerms(resolved, fields);

                    if (!accepted)
                    {
                        var failDump =
                            "request.userJson:\r\n" + prettyRequest +
                            "\r\n\r\n" + responseTrail +
                            "\r\nresolved:\r\n" + resolvedSummary;
                        MetadataDebugLog.Error(
                            game,
                            (callKind ?? "organize") + ".failed",
                            "accepted=false",
                            null,
                            failDump);
                        throw new InvalidOperationException(
                            Loc(
                                "MTDA_ErrorOrganizeFailed",
                                "The AI could not organize genres, tags or features for this game. No incomplete store-only list was applied. You can retry this game from the batch results."));
                    }

                    var payload =
                        "request.userJson:\r\n" + prettyRequest +
                        "\r\n\r\n" + responseTrail.ToString().TrimEnd();
                    if (jsonRetried || translationRetried || shapeRetried)
                    {
                        MetadataDebugLog.Warn(
                            game,
                            topic,
                            resolvedSummary.TrimEnd(),
                            payload);
                    }
                    else
                    {
                        MetadataDebugLog.Info(
                            game,
                            topic,
                            resolvedSummary.TrimEnd(),
                            payload);
                    }

                    return resolved;
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    lastError = new InvalidOperationException(
                        Loc(
                            "MTDA_ErrorProviderTimeout",
                            "The request timed out or was interrupted before completion. Check your network connection and AI provider, then try again."));
                    var timeoutDump = "request.userJson:\r\n" + prettyRequest + "\r\n\r\n" + responseTrail;
                    if (attempt >= OrganizeProviderRetries)
                    {
                        MetadataDebugLog.Error(
                            game,
                            (callKind ?? "organize") + ".error",
                            lastError.Message,
                            lastError,
                            timeoutDump);
                        throw lastError;
                    }

                    MetadataDebugLog.Warn(
                        game,
                        (callKind ?? "organize") + ".error",
                        "timeout → provider-retry | " + lastError.Message,
                        timeoutDump);
                }
                catch (AiProviderException ex)
                {
                    lastError = ex;
                    var providerDump = "request.userJson:\r\n" + prettyRequest + "\r\n\r\n" + responseTrail;
                    var canRetry = IsTransientOrganizeProviderFailure(ex) && attempt < OrganizeProviderRetries;
                    if (!canRetry)
                    {
                        MetadataDebugLog.Error(
                            game,
                            (callKind ?? "organize") + ".error",
                            ex.Message,
                            ex,
                            providerDump);
                        throw;
                    }

                    MetadataDebugLog.Warn(
                        game,
                        (callKind ?? "organize") + ".error",
                        "transient → provider-retry | " + ex.Message,
                        providerDump);
                }
                catch (HttpRequestException ex)
                {
                    lastError = ex;
                    var httpDump = "request.userJson:\r\n" + prettyRequest + "\r\n\r\n" + responseTrail;
                    if (attempt >= OrganizeProviderRetries)
                    {
                        MetadataDebugLog.Error(
                            game,
                            (callKind ?? "organize") + ".error",
                            ex.Message,
                            ex,
                            httpDump);
                        throw;
                    }

                    MetadataDebugLog.Warn(
                        game,
                        (callKind ?? "organize") + ".error",
                        "http → provider-retry | " + ex.Message,
                        httpDump);
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
            }

            throw lastError ?? new InvalidOperationException(
                Loc(
                    "MTDA_ErrorOrganizeFailed",
                    "The AI could not organize genres, tags or features for this game. No incomplete store-only list was applied. You can retry this game from the batch results."));
        }

        private static bool IsTransientOrganizeProviderFailure(AiProviderException ex)
        {
            if (ex == null)
            {
                return false;
            }

            var text = ((ex.Message ?? string.Empty) + "\n" + (ex.TechnicalDetails ?? string.Empty)).ToLowerInvariant();
            return text.IndexOf("high demand", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("overloaded", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("unavailable", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("temporarily limited", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("503", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("429", StringComparison.Ordinal) >= 0;
        }

        private void LogTermOrganizeContext(Game game, List<TermFieldRequest> organizeFields, List<TermFieldRequest> knowledgeFields)
        {
            var details = new StringBuilder();
            details.AppendLine("language=" + (settings.Language ?? string.Empty));
            details.AppendLine("model=" + (settings.Model ?? string.Empty));
            details.AppendLine("provider=" + (settings.ProviderPreset ?? string.Empty));
            details.AppendLine("sources:");
            var anySource = false;
            foreach (var source in officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
            {
                if (source == null)
                {
                    continue;
                }

                anySource = true;
                details.Append("  - ")
                    .Append(source.SourceName ?? "?")
                    .Append(" matchLanguage=")
                    .Append(source.ListsMatchPluginLanguage)
                    .Append(" genres=")
                    .Append(MetadataDebugLog.FormatTermList(source.Genres))
                    .Append(" features=")
                    .Append(MetadataDebugLog.FormatTermList(source.Features))
                    .Append(" tags=")
                    .Append(MetadataDebugLog.FormatTermList(source.Tags));
                if (string.Equals(source.SourceName, "IGDB", StringComparison.OrdinalIgnoreCase))
                {
                    details.Append(" (themes+keywords)");
                }

                details.AppendLine();
            }

            if (!anySource)
            {
                details.AppendLine("  (none returned for this game)");
            }

            foreach (var field in (organizeFields ?? new List<TermFieldRequest>())
                .Concat(knowledgeFields ?? new List<TermFieldRequest>()))
            {
                details.AppendLine(
                    "field=" + field.Field +
                    " mode=" + field.Mode +
                    " needsModel=" + field.NeedsModel +
                    " fromKnowledge=" + field.FromKnowledge +
                    " skippedInsufficientLocal=" + field.SkippedInsufficientLocalText);
                details.AppendLine("  existing=" + MetadataDebugLog.FormatTermList(field.Existing));
                details.AppendLine("  incoming=" + MetadataDebugLog.FormatTermList(field.Incoming));
                details.AppendLine("  localizedIncoming=" + MetadataDebugLog.FormatTermList(field.LocalizedIncoming));
                details.AppendLine("  nonLocalizedIncoming=" + MetadataDebugLog.FormatTermList(field.NonLocalizedIncoming));
                details.AppendLine("  rawForeign=" + MetadataDebugLog.FormatTermList(TermFieldResolver.RawForeignIncomingKeys(field)));
                details.AppendLine("  preferredFallback=" + MetadataDebugLog.FormatTermList(field.FallbackTerms()));
            }

            MetadataDebugLog.Info(game, "terms.context", details.ToString().TrimEnd());
        }

        private static string FormatResolvedTerms(Dictionary<string, List<string>> resolved, List<TermFieldRequest> fields)
        {
            var details = new StringBuilder();
            foreach (var field in fields ?? new List<TermFieldRequest>())
            {
                List<string> terms;
                if (resolved == null || !resolved.TryGetValue(field.Field, out terms))
                {
                    terms = new List<string>();
                }

                details.AppendLine(field.Field + "=" + MetadataDebugLog.FormatTermList(terms));
            }

            return details.ToString();
        }

        private JObject BuildKnownGameFacts(Game game)
        {
            var facts = new JObject();
            facts["title"] = game == null ? string.Empty : game.Name ?? string.Empty;
            facts["platform"] = new JArray(PlatformLabels(game));
            facts["librarySource"] = game == null || game.Source == null ? string.Empty : game.Source.Name ?? string.Empty;
            facts["releaseDate"] = game != null && game.ReleaseDate.HasValue ? game.ReleaseDate.Value.ToString() : string.Empty;
            facts["developers"] = new JArray(game == null ? new List<string>() : Names(game.Developers));
            facts["publishers"] = new JArray(game == null ? new List<string>() : Names(game.Publishers));
            facts["series"] = new JArray(game == null ? new List<string>() : Names(game.Series));
            facts["ageRatings"] = new JArray(game == null ? new List<string>() : Names(game.AgeRatings));
            facts["existingGenres"] = new JArray(game == null ? new List<string>() : Names(game.Genres));
            facts["existingTags"] = new JArray(game == null ? new List<string>() : Names(game.Tags));
            facts["existingFeatures"] = new JArray(game == null ? new List<string>() : Names(game.Features));
            var links = game == null || game.Links == null
                ? new List<string>()
                : game.Links
                    .Where(link => link != null && !string.IsNullOrWhiteSpace(link.Url))
                    .Select(link => ((link.Name ?? string.Empty) + " " + link.Url).Trim())
                    .Take(6)
                    .ToList();
            facts["links"] = new JArray(links);

            var editions = new JArray();
            var bestDescription = PlainFact(game == null ? null : game.Description, 900);
            foreach (var source in officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
            {
                if (source == null || editions.Count >= 4)
                {
                    continue;
                }

                var edition = new JObject();
                edition["source"] = source.SourceName ?? string.Empty;
                edition["title"] = source.Title ?? string.Empty;
                edition["url"] = source.StoreUrl ?? string.Empty;
                edition["releaseDate"] = source.ReleaseDate ?? string.Empty;
                edition["developers"] = new JArray(source.Developers ?? new List<string>());
                edition["publishers"] = new JArray(source.Publishers ?? new List<string>());
                edition["series"] = new JArray(source.Series ?? new List<string>());
                edition["ageRating"] = source.AgeRating ?? string.Empty;
                var description = PlainFact(source.Description, 900);
                if (description.Length > 0)
                {
                    edition["description"] = description;
                    if (description.Length > bestDescription.Length)
                    {
                        bestDescription = description;
                    }
                }

                editions.Add(edition);
            }

            facts["editions"] = editions;
            if (bestDescription.Length > 0)
            {
                facts["description"] = bestDescription;
            }

            return facts;
        }

        private static string PlainFact(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = Regex.Replace(value, "<[^>]+>", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (maxChars < 1 || text.Length <= maxChars)
            {
                return text;
            }

            return text.Substring(0, maxChars).Trim();
        }

        private static string ShortFact(string value)
        {
            return PlainFact(value, 480);
        }

        private static bool HasAnyTerms(IEnumerable<string> values)
        {
            return values != null && values.Any(x => !string.IsNullOrWhiteSpace(x));
        }

        private static List<string> GetResultTermList(AiMetadataResult result, string field)
        {
            if (result == null || string.IsNullOrWhiteSpace(field))
            {
                return new List<string>();
            }

            if (string.Equals(field, "genres", StringComparison.OrdinalIgnoreCase)) return result.Genres ?? new List<string>();
            if (string.Equals(field, "tags", StringComparison.OrdinalIgnoreCase)) return result.Tags ?? new List<string>();
            if (string.Equals(field, "features", StringComparison.OrdinalIgnoreCase)) return result.Features ?? new List<string>();
            if (string.Equals(field, "categories", StringComparison.OrdinalIgnoreCase)) return result.Categories ?? new List<string>();
            return new List<string>();
        }

        private void AssignTermField(AiMetadataResult result, string field, List<string> terms)
        {
            var values = terms ?? new List<string>();
            if (string.Equals(field, "genres", StringComparison.OrdinalIgnoreCase))
            {
                result.Genres = values;
            }
            else if (string.Equals(field, "features", StringComparison.OrdinalIgnoreCase))
            {
                result.Features = values;
            }
            else if (string.Equals(field, "tags", StringComparison.OrdinalIgnoreCase))
            {
                result.Tags = values;
            }
            else if (string.Equals(field, "categories", StringComparison.OrdinalIgnoreCase))
            {
                result.Categories = values;
            }
        }

        private static int TermPoolSize(int maxItems)
        {
            return Math.Min(20, Math.Max(Math.Max(1, maxItems) * 3, 8));
        }

        private bool CanQueryIgdb()
        {
            return settings != null &&
                   !string.IsNullOrWhiteSpace(settings.IgdbClientId) &&
                   (!string.IsNullOrWhiteSpace(settings.IgdbClientSecret) || !string.IsNullOrWhiteSpace(settings.IgdbAccessToken));
        }

        private bool HasContextSource(string sourceName)
        {
            return (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Any(x => x != null && string.Equals(x.SourceName, sourceName, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> CollectStoreTerms(Func<OfficialStoreMetadata, List<string>> selector, int targetCount, bool filterFeatures, Game game)
        {
            return CollectStoreTerms(selector, targetCount, filterFeatures, game, false);
        }

        private List<string> CollectStoreTerms(
            Func<OfficialStoreMetadata, List<string>> selector,
            int targetCount,
            bool filterFeatures,
            Game game,
            bool pluginLanguageOnly)
        {
            var target = Math.Max(1, targetCount);
            var sources = (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Where(x => x != null && !IsLibraryIntegrationSource(x))
                .ToList();
            var localizedSources = sources.Where(x => x.ListsMatchPluginLanguage).ToList();
            var otherSources = sources.Where(x => !x.ListsMatchPluginLanguage).ToList();

            if (pluginLanguageOnly)
            {
                return CollectFromSources(localizedSources, selector, filterFeatures, game, target);
            }

            // Always leave room for non-localized enrichment (IGDB English genres/themes).
            // Otherwise Steam/PSN alone can fill the pool and Shooter/Puzzle never reach the model.
            var otherTerms = CollectFromSources(otherSources, selector, filterFeatures, game, target);
            var reserved = Math.Min(otherTerms.Count, Math.Max(target / 2, Math.Min(4, target)));
            var localizedCap = Math.Max(0, target - reserved);
            var localizedTerms = CollectFromSources(localizedSources, selector, filterFeatures, game, localizedCap);

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in localizedTerms.Concat(otherTerms))
            {
                var key = LibraryNameMatching.NormalizeKey(value);
                if (key.Length == 0 || !seen.Add(key))
                {
                    continue;
                }

                result.Add(value);
                if (result.Count >= target)
                {
                    break;
                }
            }

            return result;
        }

        private static List<string> CollectFromSources(
            IList<OfficialStoreMetadata> sources,
            Func<OfficialStoreMetadata, List<string>> selector,
            bool filterFeatures,
            Game game,
            int targetCount)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var target = Math.Max(0, targetCount);
            if (target == 0 || sources == null || selector == null)
            {
                return result;
            }

            foreach (var source in sources)
            {
                var values = selector(source);
                if (filterFeatures)
                {
                    values = FilterFeaturesForGamePlatform(game, values);
                }

                foreach (var value in values ?? new List<string>())
                {
                    var cleaned = StoreTermSanitizer.SanitizeOne(value);
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
                    if (result.Count >= target)
                    {
                        return result;
                    }
                }
            }

            return result;
        }

        private bool StoreTermsAreInPluginLanguage(Func<OfficialStoreMetadata, List<string>> selector, int targetCount, bool filterFeatures, Game game)
        {
            var saw = false;
            foreach (var source in officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
            {
                if (source == null || IsLibraryIntegrationSource(source))
                {
                    continue;
                }

                var values = selector(source);
                if (filterFeatures)
                {
                    values = FilterFeaturesForGamePlatform(game, values);
                }

                if (!HasStoreValues(values))
                {
                    continue;
                }

                saw = true;
                // Any English/IGDB (or other non-localised) list in the merge means the
                // combined terms still need the model to translate into the plugin language.
                if (!source.ListsMatchPluginLanguage)
                {
                    return false;
                }
            }

            return saw;
        }

        private List<string> FirstStoreList(Func<OfficialStoreMetadata, List<string>> selector)
        {
            return (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Where(x => x != null && !IsLibraryIntegrationSource(x))
                .Select(selector)
                .FirstOrDefault(HasStoreValues) ?? new List<string>();
        }

        private static bool IsLibraryIntegrationSource(OfficialStoreMetadata source)
        {
            var name = source == null ? string.Empty : source.SourceName ?? string.Empty;
            return name.IndexOf(MetaDataIASettings.SourceOriginIntegration, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasStoreValues(List<string> values)
        {
            return values != null && values.Any(x => !string.IsNullOrWhiteSpace(x));
        }

        private List<string> FirstOfficialList(Func<OfficialStoreMetadata, List<string>> selector)
        {
            return (officialContextForCurrentRequest ?? new List<OfficialStoreMetadata>())
                .Select(selector)
                .Where(x => x != null && x.Any(y => !string.IsNullOrWhiteSpace(y)))
                .FirstOrDefault() ?? new List<string>();
        }

        private static List<string> FilterFeaturesForGamePlatform(Game game, IEnumerable<string> features)
        {
            var list = (features ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            if (!OfficialStoreDataService.IsPcOrientedGame(game))
            {
                return list;
            }

            return list.Where(x => !OfficialStoreDataService.IsConsoleOnlyXboxFeature(x)).ToList();
        }

        private static List<string> ResolveStrictField(List<string> officialValues, List<string> existingValues, string existingMetadataMode, int maxItems)
        {
            var official = CleanStrictValues(officialValues, maxItems);
            if (official.Count > 0)
            {
                return official;
            }

            if (!MetaDataIASettings.IsExistingMetadataIgnored(existingMetadataMode))
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
