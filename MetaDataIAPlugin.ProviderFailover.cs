using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    public partial class MetaDataIAPlugin
    {
        private sealed class BatchProviderRuntime
        {
            public MetaDataIASettings TemplateSettings { get; set; }
            public MetaDataIASettings ActiveSettings { get; set; }
            public IList<ProviderProfile> Chain { get; set; }
            public int ChainIndex { get; set; }
            public ProviderProfile ActiveProfile { get; set; }
            /// <summary>
            /// Provider endpoints that already failed probe or hard StopBatch in this run.
            /// Skipped on later failover so a hung/exhausted free tier is not probed again.
            /// </summary>
            public HashSet<string> FailedProviderKeys { get; set; }
        }

        private void GenerateAndApplyWithProviderFailover(List<Game> games, MetaDataIASettings activeSettings, bool silent = false)
        {
            games = NormalizeGamesForBatch(games);
            if (games == null || games.Count == 0 || !EnsureConfigured())
            {
                if (!silent && (games == null || games.Count == 0))
                {
                    PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MessageNoGamesMetadata", "There are no games to apply Metadata AI to."), PluginTitle);
                }

                return;
            }

            var rootSettings = settings == null ? null : settings.Settings;
            if (rootSettings == null)
            {
                return;
            }

            rootSettings.EnsureProviderProfiles();
            var template = activeSettings ?? rootSettings;
            var chain = rootSettings.GetEnabledProviderProfiles();
            if (chain == null || chain.Count == 0)
            {
                chain = new List<ProviderProfile> { rootSettings.CreateProfileFromPrimary(Loc("MTDA_ProviderProfilePrimary", "Primary")) };
            }

            var runtime = new BatchProviderRuntime
            {
                TemplateSettings = template,
                Chain = chain,
                ChainIndex = FindStartingProviderIndex(chain, template),
                FailedProviderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            };
            runtime.ActiveProfile = runtime.Chain[runtime.ChainIndex];
            runtime.ActiveSettings = BindSettingsToProfile(template, runtime.ActiveProfile);

            try
            {
                var processed = 0;
                var cancelled = false;
                var batchStopped = false;
                var probeAborted = false;
                var errors = new List<string>();
                var updatedGameIds = new HashSet<Guid>();
                var failureReasons = new Dictionary<Guid, string>();
                var historyOperation = history.BeginOperation(silent
                    ? Loc("MTDA_HistoryAutoImportMetadata", "Automatic metadata import")
                    : Loc("MTDA_HistoryApplyMetadata", "Apply AI metadata"));

                var progressResult = RunPluginProgress(PluginTitle, progress =>
                {
                    progress.ProgressMaxValue = games.Count;
                    const int maxParallel = 1;
                    var pending = new Queue<Game>(games);
                    var inFlight = new List<Task<MetadataBatchItemResult>>();
                    var batchSessionCache = new BatchTermSessionCache();
                    var actionLabel = DescribeMetadataProgressAction(runtime.ActiveSettings);
                    string focusGameName = null;

                    Action refreshFooter = () =>
                    {
                        progress.SetProviderFooter(
                            runtime.ActiveSettings == null ? null : runtime.ActiveSettings.ProviderPreset,
                            runtime.ActiveSettings == null ? null : runtime.ActiveSettings.Model);
                    };

                    Action refreshProgress = () =>
                    {
                        // BeginInvoke: never block the batch STA on the UI frame (sync Invoke
                        // can deadlock when the last game finishes and Close() is pending).
                        // Skip once every game is accounted for so a late paint cannot keep
                        // the spinner looking alive after FinishOperation.
                        var doneCount = processed + CountDistinctFailureGames(failureReasons, updatedGameIds);
                        if (cancelled || batchStopped || doneCount >= games.Count)
                        {
                            return;
                        }

                        progress.MainDispatcher.BeginInvoke(new Action(() =>
                        {
                            progress.Text = BuildParallelBatchProgressText(
                                actionLabel,
                                processed + CountDistinctFailureGames(failureReasons, updatedGameIds),
                                games.Count,
                                focusGameName);
                            progress.CurrentProgressValue = processed + CountDistinctFailureGames(failureReasons, updatedGameIds);
                        }));
                    };

                    Action startNext = () =>
                    {
                        while (inFlight.Count < maxParallel &&
                               pending.Count > 0 &&
                               !batchStopped &&
                               !progress.CancelToken.IsCancellationRequested)
                        {
                            var game = pending.Dequeue();
                            focusGameName = game.Name;
                            var settingsForTask = runtime.ActiveSettings;
                            var sessionCache = batchSessionCache;
                            var task = Task.Run(() =>
                            {
                                try
                                {
                                    var result = new MetadataGenerationService(settingsForTask, PlayniteApi, sessionCache)
                                        .GenerateAsync(game, progress.CancelToken)
                                        .GetAwaiter()
                                        .GetResult();
                                    return new MetadataBatchItemResult
                                    {
                                        Game = game,
                                        Result = result,
                                        UsedSettings = settingsForTask
                                    };
                                }
                                catch (Exception ex)
                                {
                                    return new MetadataBatchItemResult
                                    {
                                        Game = game,
                                        Error = ex,
                                        UsedSettings = settingsForTask
                                    };
                                }
                            }, progress.CancelToken);
                            inFlight.Add(task);
                            refreshProgress();
                        }
                    };

                    refreshFooter();
                    if (!EnsureActiveProviderReady(runtime, progress, refreshFooter, silent))
                    {
                        probeAborted = true;
                        batchStopped = true;
                        refreshProgress();
                        return;
                    }

                    startNext();
                    while (inFlight.Count > 0)
                    {
                        if (progress.CancelToken.IsCancellationRequested)
                        {
                            cancelled = true;
                            MarkBatchGamesCancelled(pending, failureReasons, errors, Loc(
                                "MTDA_BatchCancelledRemaining",
                                "Not processed because the operation was cancelled."));
                            MarkInFlightCancelled(inFlight, failureReasons, errors);
                            break;
                        }

                        Task<MetadataBatchItemResult> finished;
                        try
                        {
                            finished = WaitForNextBatchItem(inFlight, progress.CancelToken);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                            MarkBatchGamesCancelled(pending, failureReasons, errors, Loc(
                                "MTDA_BatchCancelledRemaining",
                                "Not processed because the operation was cancelled."));
                            MarkInFlightCancelled(inFlight, failureReasons, errors);
                            break;
                        }

                        if (finished == null)
                        {
                            cancelled = true;
                            MarkBatchGamesCancelled(pending, failureReasons, errors, Loc(
                                "MTDA_BatchCancelledRemaining",
                                "Not processed because the operation was cancelled."));
                            MarkInFlightCancelled(inFlight, failureReasons, errors);
                            break;
                        }

                        inFlight.Remove(finished);
                        MetadataBatchItemResult item;
                        try
                        {
                            item = finished.GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                            MarkBatchGamesCancelled(pending, failureReasons, errors, Loc(
                                "MTDA_BatchCancelledRemaining",
                                "Not processed because the operation was cancelled."));
                            MarkInFlightCancelled(inFlight, failureReasons, errors);
                            break;
                        }
                        catch (AggregateException aggregate)
                        {
                            if (aggregate.InnerExceptions.Any(x => x is OperationCanceledException))
                            {
                                cancelled = true;
                                MarkBatchGamesCancelled(pending, failureReasons, errors, Loc(
                                    "MTDA_BatchCancelledRemaining",
                                    "Not processed because the operation was cancelled."));
                                MarkInFlightCancelled(inFlight, failureReasons, errors);
                                break;
                            }

                            throw;
                        }

                        var game = item.Game;
                        focusGameName = game == null ? focusGameName : game.Name;
                        if (item.Error != null)
                        {
                            var ex = item.Error;
                            if (ex is OperationCanceledException)
                            {
                                if (progress.CancelToken.IsCancellationRequested)
                                {
                                    cancelled = true;
                                    MarkBatchGamesCancelled(pending, failureReasons, errors, Loc(
                                        "MTDA_BatchCancelledRemaining",
                                        "Not processed because the operation was cancelled."));
                                    MarkInFlightCancelled(inFlight, failureReasons, errors);
                                    break;
                                }

                                logger.Error("AI metadata request timed out for " + game.Name);
                                var reason = Loc(
                                    "MTDA_ErrorProviderTimeout",
                                    "The request timed out or was interrupted before completion. Check your network connection and AI provider, then try again.");
                                errors.Add(game.Name + ": " + reason);
                                failureReasons[game.Id] = reason;
                            }
                            else
                            {
                                logger.Error(ex, "Failed to process AI metadata for " + game.Name);
                                var reason = UserError(ex);
                                errors.Add(game.Name + ": " + reason);
                                failureReasons[game.Id] = reason;

                                var providerException = ex as AiProviderException;
                                if (providerException != null && providerException.StopBatch)
                                {
                                    MarkProviderFailed(runtime, item.UsedSettings);

                                    // A parallel call may still finish on the previous provider after
                                    // failover already moved the chain; requeue that game instead of
                                    // advancing again or stopping the batch.
                                    if (!IsSameProviderEndpoint(item.UsedSettings, runtime.ActiveSettings))
                                    {
                                        RemoveGameErrorEntries(errors, game);
                                        failureReasons.Remove(game.Id);
                                        RequeueGameFront(pending, game);
                                        startNext();
                                        refreshProgress();
                                        continue;
                                    }

                                    if (TryAdvanceProviderChain(runtime, progress, refreshFooter, game, reason, errors, failureReasons, pending))
                                    {
                                        startNext();
                                        refreshProgress();
                                        continue;
                                    }

                                    batchStopped = true;
                                }
                            }
                        }
                        else
                        {
                            var applySettings = runtime.ActiveSettings;
                            MetadataDebugLog.Write(
                                "batch-apply | " + (game == null ? string.Empty : game.Name ?? string.Empty),
                                "start");
                            RunOnProgressDispatcher(
                                progress,
                                () =>
                                {
                                    var resultToApply = PrepareResultForDirectBatchApply(item.Result, applySettings, games.Count > 1 || silent);
                                    var before = history.Capture(game, historyOperation, false);
                                    MetadataApplyService.Apply(PlayniteApi, game, resultToApply, applySettings);
                                    var after = history.Capture(game, historyOperation, false);
                                    history.AddGame(historyOperation, game, before, after, resultToApply.Provenance);
                                    LearnVocabulary(applySettings, resultToApply);
                                });
                            MetadataDebugLog.Write(
                                "batch-apply | " + (game == null ? string.Empty : game.Name ?? string.Empty),
                                "done");
                            processed++;
                            updatedGameIds.Add(game.Id);
                            failureReasons.Remove(game.Id);
                        }

                        if (cancelled || batchStopped)
                        {
                            if (batchStopped && pending.Count > 0)
                            {
                                MarkBatchGamesCancelled(pending, failureReasons, errors, Loc(
                                    "MTDA_BatchNotProcessedAfterStop",
                                    "Not processed because the batch stopped after the previous error."));
                            }

                            refreshProgress();
                            break;
                        }

                        startNext();
                        refreshProgress();
                    }

                    MetadataDebugLog.Write(
                        "batch-complete",
                        "processed=" + processed +
                        " failed=" + CountDistinctFailureGames(failureReasons, updatedGameIds) +
                        " cancelled=" + cancelled +
                        " stopped=" + batchStopped);
                }, null, runtime.ActiveSettings);

                if (progressResult != null && progressResult.Error != null)
                {
                    logger.Error(progressResult.Error, "Metadata AI batch progress aborted unexpectedly.");
                    batchStopped = true;
                    if (errors.Count == 0)
                    {
                        errors.Add(UserError(progressResult.Error));
                    }
                }

                if (progressResult != null && progressResult.Cancelled)
                {
                    cancelled = true;
                }

                if (cancelled)
                {
                    var cancelReason = Loc(
                        "MTDA_BatchCancelledRemaining",
                        "Not processed because the operation was cancelled.");
                    foreach (var game in games)
                    {
                        if (game == null || updatedGameIds.Contains(game.Id) || failureReasons.ContainsKey(game.Id))
                        {
                            continue;
                        }

                        failureReasons[game.Id] = cancelReason;
                    }
                }

                history.SaveOperation(historyOperation);

                if (probeAborted)
                {
                    return;
                }

                if (errors.Count > 0)
                {
                    if (silent)
                    {
                        logger.Warn("Metadata AI auto-import metadata completed with errors: " + string.Join(" | ", errors));
                    }
                    else
                    {
                        var failedGames = BuildBatchFailures(games, updatedGameIds, failureReasons, batchStopped || cancelled);
                        var retrySettings = runtime.ActiveSettings;
                        ShowBatchErrors(
                            processed,
                            errors,
                            0,
                            failedGames,
                            () => GenerateAndApply(failedGames.Select(x => x.Game).Where(x => x != null).ToList(), retrySettings),
                            FormatLocalizedFieldList(CollectChangedFields(historyOperation)),
                            CountUpdatedGames(historyOperation),
                            retrySettings);
                    }
                }
                else if (cancelled && !silent)
                {
                    PlayniteApi.Dialogs.ShowMessage(BuildMetadataBatchCancelledMessage(processed, historyOperation), PluginTitle);
                }
                else if (!silent)
                {
                    PlayniteApi.Dialogs.ShowMessage(BuildMetadataUpdatedMessage(processed, historyOperation), PluginTitle);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to generate and apply AI metadata.");
                if (!silent)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(UserError(ex), PluginTitle);
                }
            }
        }

        private static MetaDataIASettings BindSettingsToProfile(MetaDataIASettings template, ProviderProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }

            var clone = template == null
                ? new MetaDataIASettings()
                : Serialization.GetClone(template);
            clone.ProviderPreset = profile.ProviderPreset;
            clone.Endpoint = profile.Endpoint ?? string.Empty;
            clone.ApiKey = profile.ApiKey ?? string.Empty;
            clone.Model = profile.Model ?? string.Empty;
            clone.EnableLocalFallback = false;
            clone.TryLmStudioFallback = false;
            clone.TryOllamaFallback = false;
            return clone;
        }

        private static int FindStartingProviderIndex(IList<ProviderProfile> chain, MetaDataIASettings activeSettings)
        {
            if (chain == null || chain.Count == 0)
            {
                return 0;
            }

            if (activeSettings == null)
            {
                return 0;
            }

            for (var i = 0; i < chain.Count; i++)
            {
                var profile = chain[i];
                if (profile == null)
                {
                    continue;
                }

                if (string.Equals(profile.ProviderPreset, activeSettings.ProviderPreset, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((profile.Model ?? string.Empty).Trim(), (activeSettings.Model ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((profile.Endpoint ?? string.Empty).Trim(), (activeSettings.Endpoint ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            for (var i = 0; i < chain.Count; i++)
            {
                var profile = chain[i];
                if (profile == null)
                {
                    continue;
                }

                if (string.Equals(profile.ProviderPreset, activeSettings.ProviderPreset, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((profile.Model ?? string.Empty).Trim(), (activeSettings.Model ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        private bool EnsureActiveProviderReady(
            BatchProviderRuntime runtime,
            PluginProgressHandle progress,
            Action refreshFooter,
            bool silent)
        {
            Exception lastError = null;
            while (runtime.ChainIndex < runtime.Chain.Count)
            {
                if (progress.CancelToken.IsCancellationRequested)
                {
                    return false;
                }

                if (IsProviderMarkedFailed(runtime, runtime.ActiveSettings))
                {
                    var skippedName = runtime.ActiveProfile == null
                        ? (runtime.ActiveSettings.ProviderPreset ?? string.Empty)
                        : runtime.ActiveProfile.ListLabel;
                    MetadataDebugLog.Write(
                        "provider-skip-failed",
                        "profile=" + skippedName + " reason=already-failed-this-batch");
                    if (!TryMoveToNextProvider(runtime, refreshFooter))
                    {
                        break;
                    }

                    continue;
                }

                try
                {
                    RunOnProgressDispatcher(progress, () =>
                    {
                        progress.Text = Loc("MTDA_ProgressProbingProvider", "Checking AI provider…");
                    });
                    refreshFooter();
                    new MetadataGenerationService(runtime.ActiveSettings, PlayniteApi)
                        .ProbeProviderAsync(progress.CancelToken)
                        .GetAwaiter()
                        .GetResult();
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    MarkProviderFailed(runtime, runtime.ActiveSettings);
                    logger.Error(ex, "AI provider probe failed for " + (runtime.ActiveSettings.ProviderPreset ?? string.Empty));
                    var failedName = runtime.ActiveProfile == null
                        ? (runtime.ActiveSettings.ProviderPreset ?? string.Empty)
                        : runtime.ActiveProfile.ListLabel;
                    if (!TryMoveToNextProvider(runtime, refreshFooter))
                    {
                        break;
                    }

                    logger.Warn("Metadata AI switching provider after probe failure from " + failedName + " to " + runtime.ActiveProfile.ListLabel);
                }
            }

            if (lastError != null && !silent)
            {
                var message = UserError(lastError);
                RunOnProgressDispatcher(progress, () =>
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(message, PluginTitle);
                });
            }

            return false;
        }

        private bool TryAdvanceProviderChain(
            BatchProviderRuntime runtime,
            PluginProgressHandle progress,
            Action refreshFooter,
            Game failedGame,
            string reason,
            List<string> errors,
            Dictionary<Guid, string> failureReasons,
            Queue<Game> pending)
        {
            var fromLabel = runtime.ActiveProfile == null
                ? (runtime.ActiveSettings.ProviderPreset ?? string.Empty)
                : runtime.ActiveProfile.ListLabel;
            var requeued = false;
            while (TryMoveToNextProvider(runtime, refreshFooter))
            {
                var toLabel = runtime.ActiveProfile.ListLabel;
                logger.Warn("Metadata AI provider failover: " + fromLabel + " → " + toLabel + " (" + reason + ")");
                MetadataDebugLog.Write(
                    "provider-failover",
                    "from=" + fromLabel + " to=" + toLabel + " game=" + (failedGame == null ? string.Empty : failedGame.Name));

                if (!requeued && failedGame != null)
                {
                    RemoveGameErrorEntries(errors, failedGame);
                    failureReasons.Remove(failedGame.Id);
                    RequeueGameFront(pending, failedGame);
                    requeued = true;
                }

                try
                {
                    RunOnProgressDispatcher(progress, () =>
                    {
                        progress.Text = Loc("MTDA_ProgressProbingProvider", "Checking AI provider…");
                    });
                    new MetadataGenerationService(runtime.ActiveSettings, PlayniteApi)
                        .ProbeProviderAsync(progress.CancelToken)
                        .GetAwaiter()
                        .GetResult();
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (Exception probeEx)
                {
                    MarkProviderFailed(runtime, runtime.ActiveSettings);
                    logger.Error(probeEx, "Failover probe failed for " + toLabel);
                    reason = UserError(probeEx);
                    fromLabel = toLabel;
                }
            }

            return false;
        }

        /// <summary>
        /// Runs work on the progress UI dispatcher without a synchronous Invoke.
        /// Sync Invoke from the batch STA can deadlock with the progress PushFrame
        /// when the batch is finishing and Close() is about to run.
        /// </summary>
        private static void RunOnProgressDispatcher(PluginProgressHandle progress, Action action)
        {
            if (action == null)
            {
                return;
            }

            if (progress == null || progress.MainDispatcher == null)
            {
                action();
                return;
            }

            if (progress.MainDispatcher.CheckAccess())
            {
                action();
                return;
            }

            var done = new ManualResetEventSlim(false);
            Exception error = null;
            progress.MainDispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            }));

            while (!done.Wait(200))
            {
                if (progress.CancelToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(progress.CancelToken);
                }
            }

            if (error != null)
            {
                throw new AggregateException(error);
            }
        }

        private static void MarkProviderFailed(BatchProviderRuntime runtime, MetaDataIASettings providerSettings)
        {
            if (runtime == null || runtime.FailedProviderKeys == null || providerSettings == null)
            {
                return;
            }

            var key = ProviderEndpointKey(providerSettings);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (runtime.FailedProviderKeys.Add(key))
            {
                MetadataDebugLog.Write(
                    "provider-mark-failed",
                    "key=" + key);
            }
        }

        private static bool IsProviderMarkedFailed(BatchProviderRuntime runtime, MetaDataIASettings providerSettings)
        {
            if (runtime == null || runtime.FailedProviderKeys == null || providerSettings == null)
            {
                return false;
            }

            return runtime.FailedProviderKeys.Contains(ProviderEndpointKey(providerSettings));
        }

        private static string ProviderEndpointKey(MetaDataIASettings providerSettings)
        {
            if (providerSettings == null)
            {
                return string.Empty;
            }

            return (providerSettings.ProviderPreset ?? string.Empty).Trim() + "|" +
                   (providerSettings.Model ?? string.Empty).Trim() + "|" +
                   (providerSettings.Endpoint ?? string.Empty).Trim();
        }

        private static int CountDistinctFailureGames(
            Dictionary<Guid, string> failureReasons,
            ISet<Guid> updatedGameIds)
        {
            if (failureReasons == null || failureReasons.Count == 0)
            {
                return 0;
            }

            return failureReasons.Keys.Count(id => updatedGameIds == null || !updatedGameIds.Contains(id));
        }

        private static bool TryMoveToNextProvider(BatchProviderRuntime runtime, Action refreshFooter)
        {
            var next = runtime.ChainIndex + 1;
            while (next < runtime.Chain.Count)
            {
                var profile = runtime.Chain[next];
                if (profile != null && profile.Enabled && !string.IsNullOrWhiteSpace(profile.ProviderPreset))
                {
                    var candidateSettings = BindSettingsToProfile(runtime.TemplateSettings, profile);
                    if (IsProviderMarkedFailed(runtime, candidateSettings))
                    {
                        MetadataDebugLog.Write(
                            "provider-skip-failed",
                            "profile=" + (profile.ListLabel ?? profile.ProviderPreset) + " reason=already-failed-this-batch");
                        next++;
                        continue;
                    }

                    runtime.ChainIndex = next;
                    runtime.ActiveProfile = profile;
                    runtime.ActiveSettings = candidateSettings;
                    if (refreshFooter != null)
                    {
                        refreshFooter();
                    }

                    return true;
                }

                next++;
            }

            return false;
        }

        private static bool IsSameProviderEndpoint(MetaDataIASettings left, MetaDataIASettings right)
        {
            if (left == null || right == null)
            {
                return ReferenceEquals(left, right);
            }

            return string.Equals(ProviderEndpointKey(left), ProviderEndpointKey(right), StringComparison.OrdinalIgnoreCase);
        }

        private static void RequeueGameFront(Queue<Game> pending, Game game)
        {
            if (pending == null || game == null)
            {
                return;
            }

            var rest = pending.Where(x => x != null && x.Id != game.Id).ToList();
            pending.Clear();
            pending.Enqueue(game);
            foreach (var item in rest)
            {
                pending.Enqueue(item);
            }
        }

        private static void RemoveGameErrorEntries(List<string> errors, Game game)
        {
            if (errors == null || game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return;
            }

            var prefix = game.Name + ":";
            errors.RemoveAll(x => !string.IsNullOrWhiteSpace(x) && x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static void MarkBatchGamesCancelled(
            Queue<Game> pending,
            Dictionary<Guid, string> failureReasons,
            List<string> errors,
            string reason)
        {
            if (pending == null)
            {
                return;
            }

            while (pending.Count > 0)
            {
                var game = pending.Dequeue();
                if (game == null)
                {
                    continue;
                }

                failureReasons[game.Id] = reason;
                errors.Add(game.Name + ": " + reason);
            }
        }

        private void MarkInFlightCancelled(
            List<Task<MetadataBatchItemResult>> inFlight,
            Dictionary<Guid, string> failureReasons,
            List<string> errors)
        {
            if (inFlight == null)
            {
                return;
            }

            var reason = Loc(
                "MTDA_BatchCancelledRemaining",
                "Not processed because the operation was cancelled.");
            foreach (var task in inFlight.ToList())
            {
                if (task == null)
                {
                    continue;
                }

                if (!task.IsCompleted)
                {
                    continue;
                }

                try
                {
                    var item = task.GetAwaiter().GetResult();
                    if (item != null && item.Game != null && !failureReasons.ContainsKey(item.Game.Id))
                    {
                        failureReasons[item.Game.Id] = reason;
                        errors.Add(item.Game.Name + ": " + reason);
                    }
                }
                catch
                {
                }
            }

            inFlight.Clear();
        }

        private static Task<MetadataBatchItemResult> WaitForNextBatchItem(
            List<Task<MetadataBatchItemResult>> inFlight,
            CancellationToken cancelToken)
        {
            if (inFlight == null || inFlight.Count == 0)
            {
                return null;
            }

            if (cancelToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancelToken);
            }

            var cancelTcs = new TaskCompletionSource<bool>();
            using (cancelToken.Register(() => cancelTcs.TrySetResult(true)))
            {
                var anyItem = Task.WhenAny(inFlight);
                var completed = Task.WhenAny(anyItem, cancelTcs.Task).GetAwaiter().GetResult();
                if (completed == cancelTcs.Task || cancelToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancelToken);
                }

                return anyItem.GetAwaiter().GetResult();
            }
        }
    }
}
