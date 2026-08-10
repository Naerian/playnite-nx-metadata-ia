using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Playnite.SDK.Data;
using Playnite.SDK.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace MetaDataIAPlugin
{
    internal sealed class MetadataAiTopPanelControl : PluginUserControl
    {
        public MetadataAiTopPanelControl(string tooltip)
        {
            Focusable = false;
            ToolTip = tooltip;
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;

            var canvas = new Canvas { Width = 24, Height = 24 };
            foreach (var geometry in new[]
            {
                // Complete paths from Icons/settings-ai.svg (the empty SVG canvas path is omitted).
                "M10.325 4.317c.426 -1.756 2.924 -1.756 3.35 0a1.724 1.724 0 0 0 2.573 1.066c1.543 -.94 3.31 .826 2.37 2.37a1.724 1.724 0 0 0 1.065 2.572c1.756 .426 1.756 2.924 0 3.35a1.724 1.724 0 0 0 -1.066 2.573c.94 1.543 -.826 3.31 -2.37 2.37a1.724 1.724 0 0 0 -2.572 1.065c-.426 1.756 -2.924 1.756 -3.35 0a1.724 1.724 0 0 0 -2.573 -1.066c-1.543 .94 -3.31 -.826 -2.37 -2.37a1.724 1.724 0 0 0 -1.065 -2.572c-1.756 -.426 -1.756 -2.924 0 -3.35a1.724 1.724 0 0 0 1.066 -2.573c-.94 -1.543 .826 -3.31 2.37 -2.37c1 .608 2.296 .07 2.572 -1.065",
                "M9 14v-2.5a1.5 1.5 0 0 1 3 0v2.5",
                "M9 13h3",
                "M15 10v4"
            })
            {
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(geometry),
                    StrokeThickness = 2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Fill = Brushes.Transparent
                };
                path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding("Foreground") { Source = this });
                canvas.Children.Add(path);
            }

            Content = new Viewbox
            {
                Width = 22,
                Height = 22,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = canvas
            };
        }
    }

    public class MetaDataIAPlugin : MetadataPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly MetaDataIASettingsViewModel settings;
        private readonly MetadataHistoryService history;
        private readonly MetadataMaintenanceStateService maintenanceState;

        private sealed class BatchFailedGame
        {
            public Game Game { get; set; }
            public string GameName { get { return Game == null || string.IsNullOrWhiteSpace(Game.Name) ? string.Empty : Game.Name; } }
            public string Reason { get; set; }
        }

        private sealed class MediaPickerCandidateFilter
        {
            public bool OfficialOnly { get; set; }
            public bool HideScreenshots { get; set; }
            public string Aspect { get; set; }
            public HashSet<string> Styles { get; private set; }

            public MediaPickerCandidateFilter()
            {
                Aspect = "all";
                Styles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            public int ActiveCount
            {
                get { return (OfficialOnly ? 1 : 0) + (HideScreenshots ? 1 : 0) + (string.Equals(Aspect, "all", StringComparison.OrdinalIgnoreCase) ? 0 : 1) + Styles.Count; }
            }
        }

        // The global Playnite progress dialog cannot be safely nested inside this
        // modal picker: its application-wide backdrop can remain active afterwards.
        // Keep the feedback local to the picker while preserving theme resources.
        private sealed class MediaPickerBusyOverlay : IDisposable
        {
            private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
            private readonly Grid host;
            private readonly Grid overlay;

            public MediaPickerBusyOverlay(MetaDataIAPlugin plugin, Grid host, string message)
            {
                this.host = host;
                overlay = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    // A transparent panel still participates in hit testing, so the
                    // picker behind the busy dialog cannot be used concurrently.
                    Background = Brushes.Transparent
                };
                Grid.SetRowSpan(overlay, 2);
                Panel.SetZIndex(overlay, 20);

                var panel = new Border
                {
                    Width = 410,
                    Padding = new Thickness(20),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ApplyDynamicResource(panel, Border.BackgroundProperty, "StandardWindowBackgroundBrush");
                ApplyDynamicResource(panel, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
                panel.BorderThickness = new Thickness(1);

                var content = new StackPanel();
                var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
                ApplyDynamicResource(text, TextBlock.ForegroundProperty, "TextBrush");
                content.Children.Add(text);
                content.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 0, 0, 14) });
                var cancel = new Button { Content = plugin.Loc("MTDA_Cancel", "Cancel"), MinWidth = 110, HorizontalAlignment = HorizontalAlignment.Right };
                cancel.Click += (sender, args) =>
                {
                    cancellation.Cancel();
                    cancel.IsEnabled = false;
                };
                content.Children.Add(cancel);
                panel.Child = content;
                overlay.Children.Add(panel);
                host.Children.Add(overlay);
            }

            public Task<T> RunAsync<T>(Func<CancellationToken, T> operation)
            {
                return Task.Run(() => operation(cancellation.Token), cancellation.Token);
            }

            public void Dispose()
            {
                host.Children.Remove(overlay);
                cancellation.Dispose();
            }
        }

        public override Guid Id
        {
            get { return Guid.Parse("2f42c46c-9e3f-48cb-99b6-7f41f12d9b83"); }
        }

        public override List<MetadataField> SupportedFields
        {
            get
            {
                var currentSettings = settings == null ? new MetaDataIASettings() : settings.Settings;
                var fields = new List<MetadataField>();

                if (currentSettings.GenerateDescription && currentSettings.DescriptionApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.Description);
                }

                if (currentSettings.GenerateGenres && currentSettings.GenresApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.Genres);
                }

                if (currentSettings.GenerateDevelopers && currentSettings.DevelopersApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.Developers);
                }

                if (currentSettings.GeneratePublishers && currentSettings.PublishersApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.Publishers);
                }

                if (currentSettings.GenerateTags && currentSettings.TagsApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.Tags);
                }

                if (currentSettings.GenerateFeatures && currentSettings.FeaturesApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.Features);
                }

                if (currentSettings.GenerateAgeRatings && currentSettings.AgeRatingsApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.AgeRating);
                }

                if (currentSettings.GenerateRegions && currentSettings.RegionsApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.Region);
                }

                if (currentSettings.GenerateLinks && currentSettings.LinksApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.Links);
                }

                if (currentSettings.GenerateReleaseDate && currentSettings.ReleaseDateApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.ReleaseDate);
                }

                if (currentSettings.GenerateSeries && currentSettings.SeriesApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.Series);
                }

                if (currentSettings.DownloadCoverImage && currentSettings.CoverImageApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.CoverImage);
                }

                if (currentSettings.DownloadIcon && currentSettings.IconApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.Icon);
                }

                if (currentSettings.DownloadBackgroundImage && currentSettings.BackgroundImageApplyMode != MetaDataIASettings.ApplySkip)
                {
                    fields.Add(MetadataField.BackgroundImage);
                }

                return fields;
            }
        }

        public override string Name { get { return "Metadata AI"; } }
        public IPlayniteAPI Api { get { return PlayniteApi; } }

        private string MenuRoot { get { return Loc("MTDA_PluginName", "Metadata AI"); } }
        private string PluginTitle { get { return Loc("MTDA_PluginName", "Metadata AI"); } }

        public MetaDataIAPlugin(IPlayniteAPI api) : base(api)
        {
            PluginLocalization.Initialize(api);
            settings = new MetaDataIASettingsViewModel(this);
            history = new MetadataHistoryService(api, GetPluginUserDataPath());
            maintenanceState = new MetadataMaintenanceStateService(GetPluginUserDataPath());
            Properties = new MetadataPluginProperties
            {
                HasSettings = true
            };
        }

        public string Loc(string key, string fallback = null)
        {
            return PluginLocalization.GetString(key, fallback);
        }

        public override OnDemandMetadataProvider GetMetadataProvider(MetadataRequestOptions options)
        {
            return new MetaDataIAProvider(options, this, settings.Settings);
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            SeedKnownGamesIfNeeded();
            if (settings.IsSetupWizardPending && !IsFullscreenMode)
            {
                PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(() => OpenSetupWizard(true)));
            }
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            ProcessNewGamesAfterLibraryUpdate();
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new MetaDataIASettingsView();
        }

        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            var tooltip = GetLocalizedOrFallback("MTDA_TopPanelSettings", "Open Metadata AI settings");
            yield return new TopPanelItem
            {
                Title = tooltip,
                Icon = new MetadataAiTopPanelControl(tooltip),
                Visible = !IsFullscreenMode,
                Activated = () => OpenSettingsView()
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            if (args == null || args.Games == null || args.Games.Count == 0)
            {
                yield break;
            }
            var multipleGames = args.Games.Count > 1;

            if (!IsFullscreenMode)
            {
                yield return new GameMenuItem
                {
                    Description = multipleGames
                        ? Loc("MTDA_MenuAuditSelectedGames", "Audit selected games")
                        : Loc("MTDA_MenuAuditGame", "Audit this game"),
                    MenuSection = MenuRoot,
                    Action = actionArgs => ShowLibraryAudit(actionArgs.Games)
                };

                yield return new GameMenuItem
                {
                    Description = Loc("MTDA_MenuSimulateChanges", "Preview and choose Metadata AI changes"),
                    MenuSection = MenuRoot,
                    Action = actionArgs => SimulateChanges(actionArgs.Games, settings.Settings)
                };

                if (!multipleGames)
                {
                    yield return new GameMenuItem
                    {
                        Description = Loc("MTDA_MenuGenerateReview", "Generate and review in Playnite editor"),
                        MenuSection = MenuRoot,
                        Action = actionArgs => GenerateAndReview(actionArgs.Games.FirstOrDefault())
                    };
                }
            }

            yield return new GameMenuItem
            {
                Description = Loc("MTDA_MenuGenerateApply", "Generate and apply configured metadata"),
                MenuSection = MenuRoot,
                Action = actionArgs => GenerateAndApply(actionArgs.Games, settings.Settings)
            };
            yield return CreateGameRootSeparator();

            yield return CreateGameMenuItem("Establecer descripciones", activeSettings => CreateFocusedSettings("description"));
            yield return CreateGameMenuItem("Establecer generos", activeSettings => CreateFocusedSettings("genres"));
            yield return CreateGameMenuItem("Establecer categorias", activeSettings => CreateFocusedSettings("categories"));
            yield return CreateGameMenuItem("Establecer etiquetas", activeSettings => CreateFocusedSettings("tags"));
            yield return CreateGameMenuItem("Establecer caracteristicas", activeSettings => CreateFocusedSettings("features"));
            yield return CreateGameMenuItem("Establecer desarrolladores", activeSettings => CreateFocusedSettings("developers"));
            yield return CreateGameMenuItem("Establecer editores", activeSettings => CreateFocusedSettings("publishers"));
            yield return CreateGameMenuItem("Establecer clasificaciones por edad", activeSettings => CreateFocusedSettings("ageRatings"));
            yield return CreateGameMenuItem("Establecer regiones", activeSettings => CreateFocusedSettings("regions"));
            yield return CreateGameMenuItem("Establecer enlaces", activeSettings => CreateFocusedSettings("links"));
            yield return CreateGameMenuItem("Establecer fecha de lanzamiento", activeSettings => CreateFocusedSettings("releaseDate"));
            yield return CreateGameMenuItem("Establecer serie", activeSettings => CreateFocusedSettings("series"));
            yield return CreateGameSortingMenuItem("Establecer orden de nombre");
            yield return CreateGameSubmenuSeparator("MTDA_TabFields", "Campos");
            yield return CreateGameMenuItem("Establecer todos los campos", activeSettings => activeSettings);

            yield return CreateGameMediaMenuItem("Establecer portada", activeSettings => CreateFocusedMediaSettings("cover"));
            yield return CreateGameMediaMenuItem("Establecer icono", activeSettings => CreateFocusedMediaSettings("icon"));
            yield return CreateGameMediaMenuItem("Establecer fondo", activeSettings => CreateFocusedMediaSettings("background"));
            if (!IsFullscreenMode && !multipleGames && settings.Settings.EnableExtraMetadataLoaderLogos)
            {
                yield return new GameMenuItem
                {
                    Description = Loc("MTDA_MenuSetLogo", "Find logo for Extra Metadata Loader"),
                    MenuSection = MenuRoot + "|" + Loc("MTDA_TabMedia", "Media"),
                    Action = actionArgs => FindAndApplyLogo(actionArgs.Games.FirstOrDefault())
                };
            }
            yield return CreateGameSubmenuSeparator("MTDA_TabMedia", "Media");
            yield return CreateGameMediaMenuItem("Establecer media completa", activeSettings => activeSettings, true);
            if (!IsFullscreenMode && !multipleGames)
            {
                yield return CreateGameSubmenuSeparator("MTDA_TabMedia", "Media");
                yield return new GameMenuItem
                {
                    Description = Loc("MTDA_OpenGameMediaFolder", "Open game media folder"),
                    MenuSection = MenuRoot + "|" + Loc("MTDA_TabMedia", "Media"),
                    Action = actionArgs => OpenGameMediaFolder(actionArgs.Games.FirstOrDefault())
                };
                yield return new GameMenuItem
                {
                    Description = Loc("MTDA_ClearGameMedia", "Remove all game media"),
                    MenuSection = MenuRoot + "|" + Loc("MTDA_TabMedia", "Media"),
                    Action = actionArgs => ClearGameMedia(actionArgs.Games.FirstOrDefault())
                };
            }
            yield return CreateGameRootSeparator();

            if (!IsFullscreenMode && !multipleGames)
            {
                var selectedGame = args.Games.First();
                var lockKinds = new List<MediaKind> { MediaKind.Cover, MediaKind.Icon, MediaKind.Background };
                if (settings.Settings.EnableExtraMetadataLoaderLogos) lockKinds.Add(MediaKind.Logo);
                foreach (var kind in lockKinds)
                {
                    var localKind = kind;
                    yield return new GameMenuItem
                    {
                        Description = string.Format(
                            maintenanceState.IsLocked(selectedGame.Id, localKind)
                                ? Loc("MTDA_UnlockMediaKind", "Allow Metadata AI to replace {0}")
                                : Loc("MTDA_LockMediaKind", "Protect {0} from automatic replacement"),
                            MediaKindName(localKind).ToLowerInvariant()),
                        MenuSection = MenuRoot + "|" + Loc("MTDA_MenuMediaLocks", "Media locks"),
                        Action = actionArgs => ToggleMediaLock(actionArgs.Games.FirstOrDefault(), localKind)
                    };
                }

            }

            if (!IsFullscreenMode)
            {
                yield return new GameMenuItem
                {
                    Description = multipleGames
                        ? Loc("MTDA_MenuSelectedHistory", "View history for selected games")
                        : Loc("MTDA_MenuGameHistory", "View history for this game"),
                    MenuSection = MenuRoot + "|" + Loc("MTDA_MenuTools", "History and provenance"),
                    Action = actionArgs => ShowGameHistory(actionArgs.Games)
                };

                yield return new GameMenuItem
                {
                    Description = multipleGames
                        ? Loc("MTDA_MenuSelectedProvenance", "View provenance for selected games")
                        : Loc("MTDA_MenuViewProvenance", "View last Metadata AI provenance"),
                    MenuSection = MenuRoot + "|" + Loc("MTDA_MenuTools", "History and provenance"),
                    Action = actionArgs => ShowLatestProvenance(actionArgs.Games)
                };
            }
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            if (!IsFullscreenMode)
            {
                yield return new MainMenuItem
                {
                    Description = Loc("MTDA_MenuSetupWizard", "Open first-time setup assistant"),
                    MenuSection = MenuRoot,
                    Action = actionArgs => OpenSetupWizard(false)
                };

                yield return new MainMenuItem
                {
                    Description = Loc("MTDA_MenuAuditLibrary", "Audit current library"),
                    MenuSection = MenuRoot,
                    Action = actionArgs => ShowLibraryAudit(GetFilteredGames())
                };

                yield return new MainMenuItem
                {
                    Description = Loc("MTDA_MenuSimulateSelectedOrList", "Simulate selected games or current list"),
                    MenuSection = MenuRoot,
                    Action = actionArgs => SimulateChanges(GetSelectedOrFilteredGames(), settings.Settings)
                };

                yield return new MainMenuItem
                {
                    Description = Loc("MTDA_MenuHistory", "View change history"),
                    MenuSection = MenuRoot,
                    Action = actionArgs => ShowHistory()
                };
            }

            yield return new MainMenuItem
            {
                Description = Loc("MTDA_MenuApplyAllList", "Aplicar a todos los juegos de la lista"),
                MenuSection = MenuRoot,
                Action = actionArgs => GenerateAndApplyWithConfirmation(GetFilteredGames(), settings.Settings, Loc("MTDA_ScopeAllGamesInList", "all games in the list"))
            };

            yield return new MainMenuItem
            {
                Description = Loc("MTDA_MenuApplySelected", "Aplicar a seleccionados"),
                MenuSection = MenuRoot,
                Action = actionArgs => GenerateAndApplySelected(settings.Settings)
            };
            yield return CreateMainRootSeparator();

            yield return CreateMainMenuItem("Establecer descripciones", activeSettings => CreateFocusedSettings("description"));
            yield return CreateMainMenuItem("Establecer generos", activeSettings => CreateFocusedSettings("genres"));
            yield return CreateMainMenuItem("Establecer categorias", activeSettings => CreateFocusedSettings("categories"));
            yield return CreateMainMenuItem("Establecer etiquetas", activeSettings => CreateFocusedSettings("tags"));
            yield return CreateMainMenuItem("Establecer caracteristicas", activeSettings => CreateFocusedSettings("features"));
            yield return CreateMainMenuItem("Establecer desarrolladores", activeSettings => CreateFocusedSettings("developers"));
            yield return CreateMainMenuItem("Establecer editores", activeSettings => CreateFocusedSettings("publishers"));
            yield return CreateMainMenuItem("Establecer clasificaciones por edad", activeSettings => CreateFocusedSettings("ageRatings"));
            yield return CreateMainMenuItem("Establecer regiones", activeSettings => CreateFocusedSettings("regions"));
            yield return CreateMainMenuItem("Establecer enlaces", activeSettings => CreateFocusedSettings("links"));
            yield return CreateMainMenuItem("Establecer fecha de lanzamiento", activeSettings => CreateFocusedSettings("releaseDate"));
            yield return CreateMainMenuItem("Establecer serie", activeSettings => CreateFocusedSettings("series"));
            yield return CreateMainSortingMenuItem("Establecer orden de nombre");
            yield return CreateMainSubmenuSeparator("MTDA_TabFields", "Campos");
            yield return CreateMainMenuItem("Establecer todos los campos", activeSettings => activeSettings);

            yield return CreateMainMediaMenuItem("Establecer portadas", activeSettings => CreateFocusedMediaSettings("cover"));
            yield return CreateMainMediaMenuItem("Establecer iconos", activeSettings => CreateFocusedMediaSettings("icon"));
            yield return CreateMainMediaMenuItem("Establecer fondos", activeSettings => CreateFocusedMediaSettings("background"));
            yield return CreateMainSubmenuSeparator("MTDA_TabMedia", "Media");
            yield return CreateMainMediaMenuItem("Establecer media completa", activeSettings => activeSettings, true);

            if (!IsFullscreenMode)
            {
                yield return new MainMenuItem
                {
                    Description = "-",
                    MenuSection = MenuRoot
                };

                yield return new MainMenuItem
                {
                    Description = Loc("MTDA_MenuSettings", "Settings"),
                    MenuSection = MenuRoot,
                    Action = actionArgs => OpenSettingsView()
                };
            }
        }

        private void GenerateAndReview(Game game)
        {
            if (game == null)
            {
                return;
            }

            if (!EnsureConfigured())
            {
                return;
            }

            try
            {
                AiMetadataResult result = null;
                Exception generationError = null;
                PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                {
                    try
                    {
                        progress.Text = Loc("MTDA_ProgressGeneratingMetadata", "Generating AI metadata...");
                        result = new MetadataGenerationService(settings.Settings, PlayniteApi).GenerateAsync(game, progress.CancelToken).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        generationError = ex;
                    }
                }, new GlobalProgressOptions(PluginTitle, false) { IsIndeterminate = true });

                if (generationError != null)
                {
                    throw generationError;
                }

                if (result == null)
                {
                    return;
                }

                RunOnUiThread(() => OpenNativeEditReview(game, result, settings.Settings));
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to generate AI metadata.");
                PlayniteApi.Dialogs.ShowErrorMessage(UserError(ex), PluginTitle);
            }
        }

        private void GenerateAndApply(List<Game> games, MetaDataIASettings activeSettings, bool silent = false)
        {
            if (games == null || games.Count == 0 || !EnsureConfigured())
            {
                if (!silent && (games == null || games.Count == 0))
                {
                    PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MessageNoGamesMetadata", "There are no games to apply Metadata AI to."), PluginTitle);
                }

                return;
            }

            try
            {
                var processed = 0;
                var cancelled = false;
                var errors = new List<string>();
                var updatedGameIds = new HashSet<Guid>();
                var failureReasons = new Dictionary<Guid, string>();
                var historyOperation = history.BeginOperation(silent
                    ? Loc("MTDA_HistoryAutoImportMetadata", "Automatic metadata import")
                    : Loc("MTDA_HistoryApplyMetadata", "Apply AI metadata"));
                PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                {
                    progress.ProgressMaxValue = games.Count;
                    foreach (var game in games)
                    {
                        if (progress.CancelToken.IsCancellationRequested)
                        {
                            cancelled = true;
                            break;
                        }

                        progress.Text = Loc("MTDA_ProgressGeneratingMetadataGame", "Generating AI metadata: ") + game.Name;
                        try
                        {
                            var result = new MetadataGenerationService(activeSettings, PlayniteApi).GenerateAsync(game, progress.CancelToken).GetAwaiter().GetResult();
                            progress.MainDispatcher.Invoke(new Action(() =>
                            {
                                var resultToApply = PrepareResultForDirectBatchApply(result, activeSettings, games.Count > 1 || silent);
                                var before = history.Capture(game, historyOperation, false);
                                MetadataApplyService.Apply(PlayniteApi, game, resultToApply, activeSettings);
                                var after = history.Capture(game, historyOperation, false);
                                history.AddGame(historyOperation, game, before, after, resultToApply.Provenance);
                                LearnVocabulary(activeSettings, resultToApply);
                            }));
                            processed++;
                            updatedGameIds.Add(game.Id);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Failed to process AI metadata for " + game.Name);
                            var reason = UserError(ex);
                            errors.Add(game.Name + ": " + reason);
                            failureReasons[game.Id] = reason;

                            var providerException = ex as AiProviderException;
                            if (providerException != null && providerException.StopBatch)
                            {
                                break;
                            }
                        }

                        progress.CurrentProgressValue = processed + errors.Count;
                    }
                }, new GlobalProgressOptions(PluginTitle, true));

                history.SaveOperation(historyOperation);

                if (errors.Count > 0)
                {
                    if (silent)
                    {
                        logger.Warn("Metadata AI auto-import metadata completed with errors: " + string.Join(" | ", errors));
                    }
                    else
                    {
                        var failedGames = BuildBatchFailures(games, updatedGameIds, failureReasons);
                        ShowBatchErrors(
                            processed,
                            errors,
                            0,
                            failedGames,
                            () => GenerateAndApply(failedGames.Select(x => x.Game).Where(x => x != null).ToList(), activeSettings));
                    }
                }
                else if (cancelled && !silent)
                {
                    PlayniteApi.Dialogs.ShowMessage(string.Format(Loc("MTDA_MessageBatchCancelled", "Metadata AI cancelled the operation. Processed games: {0}."), processed), PluginTitle);
                }
                else if (!silent)
                {
                    PlayniteApi.Dialogs.ShowMessage(string.Format(Loc("MTDA_MessageMetadataUpdated", "Metadata AI updated {0} game(s)."), processed), PluginTitle);
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

        private static AiMetadataResult PrepareResultForDirectBatchApply(AiMetadataResult result, MetaDataIASettings activeSettings, bool batchMode)
        {
            if (result == null || activeSettings == null || !batchMode || !activeSettings.StrictCompanyAgeRegion)
            {
                return result;
            }

            var clone = Serialization.GetClone(result);
            RemoveLowConfidenceFactualField(clone, "developers", () => clone.Developers = new List<string>());
            RemoveLowConfidenceFactualField(clone, "publishers", () => clone.Publishers = new List<string>());
            RemoveLowConfidenceFactualField(clone, "ageRatings", () => clone.AgeRatings = new List<string>());
            RemoveLowConfidenceFactualField(clone, "regions", () => clone.Regions = new List<string>());
            RemoveLowConfidenceFactualField(clone, "releaseDate", () => clone.ReleaseDate = string.Empty);
            RemoveLowConfidenceFactualField(clone, "series", () => clone.Series = new List<string>());
            return clone;
        }

        private static void RemoveLowConfidenceFactualField(AiMetadataResult result, string field, Action clear)
        {
            if (result == null || clear == null || string.IsNullOrWhiteSpace(field))
            {
                return;
            }

            var provenance = (result.Provenance ?? new List<MetadataFieldProvenance>())
                .FirstOrDefault(x => string.Equals(x.Field, field, StringComparison.OrdinalIgnoreCase));
            var confidence = provenance == null ? string.Empty : provenance.Confidence ?? string.Empty;
            var method = provenance == null ? string.Empty : provenance.Method ?? string.Empty;
            var trusted = string.Equals(confidence, "high", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(confidence, "medium", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(method, "generated-from-identity", StringComparison.OrdinalIgnoreCase);

            if (trusted)
            {
                return;
            }

            clear();
            result.Provenance = (result.Provenance ?? new List<MetadataFieldProvenance>())
                .Where(x => !string.Equals(x.Field, field, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private void ApplyMedia(List<Game> games, MetaDataIASettings activeSettings, bool silent = false)
        {
            if (games == null || games.Count == 0)
            {
                if (!silent)
                {
                    PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MessageNoGamesMedia", "There are no games to apply media to."), PluginTitle);
                }
                return;
            }

            if (!activeSettings.IsMediaConfigured)
            {
                if (!silent)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(Loc("MTDA_ErrorNoMediaSources", "There are no configured or available media sources."), PluginTitle);
                    OpenSettingsView();
                }
                return;
            }

            try
            {
                var processed = 0;
                var appliedMedia = 0;
                var qualitySkipped = 0;
                var cancelled = false;
                var errors = new List<string>();
                var updatedGameIds = new HashSet<Guid>();
                var failureReasons = new Dictionary<Guid, string>();
                var historyOperation = history.BeginOperation(silent
                    ? Loc("MTDA_HistoryAutoImportMedia", "Automatic media import")
                    : Loc("MTDA_HistoryApplyMedia", "Apply media"));
                PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                {
                    progress.ProgressMaxValue = games.Count;
                    var service = new MediaGenerationService(activeSettings, PlayniteApi);
                    foreach (var game in games)
                    {
                        if (progress.CancelToken.IsCancellationRequested)
                        {
                            cancelled = true;
                            break;
                        }

                        progress.Text = Loc("MTDA_ProgressDownloadingMedia", "Downloading media: ") + game.Name;
                        try
                        {
                            GameMetadataSnapshot before = null;
                            progress.MainDispatcher.Invoke(new Action(() => before = history.Capture(game, historyOperation, true)));
                            var mediaProvenance = new List<MetadataFieldProvenance>();
                            var appliedForGame = ApplyEnabledMedia(service, game, progress, mediaProvenance);
                            progress.MainDispatcher.Invoke(new Action(() =>
                            {
                                var after = history.Capture(game, historyOperation, false);
                                history.AddGame(historyOperation, game, before, after, mediaProvenance);
                            }));
                            appliedMedia += appliedForGame;
                            processed++;
                            updatedGameIds.Add(game.Id);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Failed to process media for " + game.Name);
                            var reason = UserError(ex);
                            errors.Add(game.Name + ": " + reason);
                            failureReasons[game.Id] = reason;
                        }

                        progress.CurrentProgressValue = processed + errors.Count;
                    }

                    qualitySkipped = service.StrictQualitySkipCount;
                }, new GlobalProgressOptions(PluginTitle + " - " + Loc("MTDA_TabMedia", "Media"), true));

                history.SaveOperation(historyOperation);

                if (errors.Count > 0)
                {
                    if (silent)
                    {
                        logger.Warn("Metadata AI auto-import media completed with errors: " + string.Join(" | ", errors));
                    }
                    else
                    {
                        var failedGames = BuildBatchFailures(games, updatedGameIds, failureReasons);
                        ShowBatchErrors(
                            processed,
                            errors,
                            qualitySkipped,
                            failedGames,
                            () => ApplyMedia(failedGames.Select(x => x.Game).Where(x => x != null).ToList(), activeSettings));
                    }
                }
                else if (cancelled && !silent)
                {
                    PlayniteApi.Dialogs.ShowMessage(AppendQualitySkipSummary(string.Format(Loc("MTDA_MessageMediaCancelled", "Metadata AI cancelled the media operation. Processed games: {0}. Applied files: {1}."), processed, appliedMedia), qualitySkipped), PluginTitle);
                }
                else if (!silent)
                {
                    PlayniteApi.Dialogs.ShowMessage(AppendQualitySkipSummary(string.Format(Loc("MTDA_MessageMediaUpdated", "Metadata AI updated media for {0} game(s). Applied files: {1}."), processed, appliedMedia), qualitySkipped), PluginTitle);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to apply media.");
                if (!silent)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(UserError(ex), PluginTitle);
                }
            }
        }

        public void OpenSetupWizard(bool firstRun = false)
        {
            if (IsFullscreenMode)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_SetupWizardDesktopOnly", "The setup assistant is available in Desktop mode. Your current settings remain unchanged."), PluginTitle);
                return;
            }

            var window = new SetupWizardWindow(this, settings.Settings, firstRun);
            var accepted = window.ShowDialog();
            if (accepted == true && window.ResultSettings != null)
            {
                settings.ReplaceSettingsFromWizard(window.ResultSettings);
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_SetupWizardSaved", "The Metadata AI configuration was saved. No games were modified."), PluginTitle);
            }
            else if (firstRun && window.Skipped)
            {
                settings.Settings.SetupWizardCompleted = true;
                settings.Settings.SetupWizardMigrationApplied = true;
                SaveSettingsSecurely(settings.Settings);
            }
        }

        public void SimulateChanges(List<Game> games, MetaDataIASettings activeSettings)
        {
            if (games == null || games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MessageNoGamesMetadata", "There are no games to apply Metadata AI to."), PluginTitle);
                return;
            }

            if (!EnsureConfigured())
            {
                return;
            }

            if (games.Count > 1)
            {
                var confirmation = PlayniteApi.Dialogs.ShowMessage(
                    string.Format(Loc("MTDA_SimulationQuotaWarning", "The simulation will query the configured AI provider for {0} games and may consume API quota. It will not modify the library. Continue?"), games.Count),
                    PluginTitle,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var simulations = new List<MetadataSimulationResult>();
            var cancelled = false;
            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                progress.ProgressMaxValue = games.Count;
                var index = 0;
                foreach (var game in games)
                {
                    if (progress.CancelToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    progress.Text = Loc("MTDA_ProgressSimulatingGame", "Simulating changes: ") + game.Name;
                    var simulation = new MetadataSimulationResult { Game = game };
                    try
                    {
                        simulation.Result = new MetadataGenerationService(activeSettings, PlayniteApi).GenerateAsync(game, progress.CancelToken).GetAwaiter().GetResult();
                        progress.MainDispatcher.Invoke(new Action(() =>
                        {
                            simulation.Changes = MetadataChangePreviewService.Build(PlayniteApi, game, simulation.Result, activeSettings);
                        }));
                        if (games.Count == 1 && activeSettings.IsMediaConfigured)
                        {
                            simulation.MediaChanges = BuildSimulationMediaProposals(game, activeSettings, progress.CancelToken, text => progress.Text = text);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Failed to simulate metadata for " + game.Name);
                        simulation.Error = UserError(ex);
                    }

                    simulations.Add(simulation);
                    index++;
                    progress.CurrentProgressValue = index;
                }
            }, new GlobalProgressOptions(PluginTitle + " - " + Loc("MTDA_SimulationTitle", "Simulation"), true));

            if (cancelled || simulations.Count == 0)
            {
                return;
            }

            var window = new SimulationWindow(this, simulations, activeSettings);
            var owner = window.Owner ?? PlayniteApi.Dialogs.GetCurrentAppWindow();
            bool? accepted = null;
            try
            {
                accepted = window.ShowDialog();
            }
            finally
            {
                RestoreWindowActivation(owner);
            }

            if (accepted == true && window.ApplyRequested)
            {
                ApplySimulationResults(simulations, activeSettings);
            }
        }

        private List<MediaSimulationChange> BuildSimulationMediaProposals(
            Game game,
            MetaDataIASettings activeSettings,
            System.Threading.CancellationToken cancelToken,
            Action<string> progressText)
        {
            var proposals = new List<MediaSimulationChange>();
            foreach (var kind in new[] { MediaKind.Cover, MediaKind.Icon, MediaKind.Background })
            {
                cancelToken.ThrowIfCancellationRequested();
                if (maintenanceState.IsLocked(game.Id, kind))
                {
                    continue;
                }
                try
                {
                    if (progressText != null)
                    {
                        progressText(string.Format(
                            Loc("MTDA_ProgressSearchingMediaKind", "Searching for {0} in media sources..."),
                            MediaKindName(kind).ToLowerInvariant()));
                    }

                    var focus = kind == MediaKind.Cover ? "cover" : kind == MediaKind.Icon ? "icon" : "background";
                    var focusedSettings = CreateFocusedMediaSettings(activeSettings, focus);
                    var option = new MediaGenerationService(focusedSettings, PlayniteApi)
                        .GetRecommendedPreviewOptionAsync(game, kind, cancelToken)
                        .GetAwaiter()
                        .GetResult();
                    if (option != null)
                    {
                        proposals.Add(new MediaSimulationChange
                        {
                            Kind = kind,
                            Option = option,
                            Settings = focusedSettings,
                            IsSelected = false
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Failed to prepare simulated media proposal for " + game.Name + " (" + kind + ").");
                }
            }

            return proposals;
        }

        private void ApplySimulationResults(IEnumerable<MetadataSimulationResult> simulations, MetaDataIASettings activeSettings)
        {
            var operation = history.BeginOperation(Loc("MTDA_HistoryApplySimulation", "Apply simulated metadata"));
            var applied = 0;
            var mediaErrors = new List<string>();
            foreach (var simulation in simulations ?? Enumerable.Empty<MetadataSimulationResult>())
            {
                var selectedFields = new HashSet<string>(
                    simulation.Changes == null
                        ? Enumerable.Empty<string>()
                        : simulation.Changes.Where(x => x.IsSelected).Select(x => x.Field),
                    StringComparer.OrdinalIgnoreCase);
                var selectedMedia = (simulation.MediaChanges ?? new List<MediaSimulationChange>())
                    .Where(x => x != null && x.IsSelected && x.Option != null && x.Settings != null)
                    .ToList();
                if (simulation.Game == null || (selectedFields.Count == 0 && selectedMedia.Count == 0))
                {
                    continue;
                }

                var selectedSettings = simulation.Result == null ? null : CreateSimulationApplySettings(activeSettings, selectedFields);
                var selectedResult = simulation.Result == null ? new AiMetadataResult() : FilterSimulationResult(simulation.Result, selectedFields);
                var before = history.Capture(simulation.Game, operation, selectedMedia.Count > 0);
                if (selectedFields.Count > 0)
                {
                    MetadataApplyService.Apply(PlayniteApi, simulation.Game, selectedResult, selectedSettings);
                    LearnVocabulary(selectedSettings, selectedResult);
                }

                var provenance = new List<MetadataFieldProvenance>(selectedResult.Provenance ?? new List<MetadataFieldProvenance>());
                var appliedMediaProvenance = ApplySimulationMediaChanges(simulation.Game, selectedMedia, mediaErrors);
                provenance.AddRange(appliedMediaProvenance);
                var after = history.Capture(simulation.Game, operation, false);
                if (selectedFields.Count > 0 || appliedMediaProvenance.Count > 0)
                {
                    history.AddGame(operation, simulation.Game, before, after, provenance);
                    applied++;
                }
            }

            history.SaveOperation(operation);
            PlayniteApi.Dialogs.ShowMessage(string.Format(Loc("MTDA_SimulationApplied", "Metadata AI applied the simulated changes to {0} game(s)."), applied), PluginTitle);
            if (mediaErrors.Count > 0)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(string.Join(Environment.NewLine + Environment.NewLine, mediaErrors), PluginTitle);
            }
        }

        private List<MetadataFieldProvenance> ApplySimulationMediaChanges(Game game, List<MediaSimulationChange> changes, List<string> errors)
        {
            var provenance = new List<MetadataFieldProvenance>();
            if (game == null || changes == null || changes.Count == 0)
            {
                return provenance;
            }

            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                foreach (var change in changes)
                {
                    if (progress.CancelToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        if (maintenanceState.IsLocked(game.Id, change.Kind))
                        {
                            continue;
                        }
                        progress.Text = string.Format(Loc("MTDA_ProgressApplyingMediaKind", "Applying {0}..."), MediaKindName(change.Kind).ToLowerInvariant());
                        var service = new MediaGenerationService(change.Settings, PlayniteApi);
                        var media = service.GenerateFromOptionAsync(game, change.Option, progress.CancelToken).GetAwaiter().GetResult();
                        progress.MainDispatcher.Invoke(new Action(() =>
                        {
                            MediaGenerationService.ApplyMediaFile(PlayniteApi, game, media);
                            PlayniteApi.Database.Games.Update(game);
                        }));
                        provenance.Add(new MetadataFieldProvenance
                        {
                            Field = change.Kind == MediaKind.Cover ? "cover" : change.Kind == MediaKind.Icon ? "icon" : "background",
                            Source = string.IsNullOrWhiteSpace(change.Option.SourceName) ? Loc("MTDA_UnknownSource", "Unknown source") : change.Option.SourceName,
                            Method = "downloaded-media",
                            Confidence = change.Option.IsOfficial ? "high" : "medium",
                            Detail = change.Option.Url
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Failed to apply simulated media for " + game.Name);
                        errors.Add(game.Name + " - " + MediaKindName(change.Kind) + ": " + UserError(ex));
                    }
                }
            }, new GlobalProgressOptions(PluginTitle + " - " + Loc("MTDA_ApplyMedia", "Apply media"), true) { IsIndeterminate = true });
            return provenance;
        }

        private static MetaDataIASettings CreateSimulationApplySettings(MetaDataIASettings source, HashSet<string> selectedFields)
        {
            var clone = Serialization.GetClone(source);
            if (!selectedFields.Contains("description")) { clone.GenerateDescription = false; clone.DescriptionApplyMode = MetaDataIASettings.ApplySkip; }
            if (!selectedFields.Contains("genres")) { clone.GenerateGenres = false; clone.GenresApplyMode = MetaDataIASettings.ApplySkip; }
            if (!selectedFields.Contains("tags")) { clone.GenerateTags = false; clone.TagsApplyMode = MetaDataIASettings.ApplySkip; }
            if (!selectedFields.Contains("features")) { clone.GenerateFeatures = false; clone.FeaturesApplyMode = MetaDataIASettings.ApplySkip; }
            if (!selectedFields.Contains("developers")) { clone.GenerateDevelopers = false; clone.DevelopersApplyMode = MetaDataIASettings.ApplySkip; }
            if (!selectedFields.Contains("publishers")) { clone.GeneratePublishers = false; clone.PublishersApplyMode = MetaDataIASettings.ApplySkip; }
            if (!selectedFields.Contains("ageRatings")) { clone.GenerateAgeRatings = false; clone.AgeRatingsApplyMode = MetaDataIASettings.ApplySkip; }
            if (!selectedFields.Contains("regions")) { clone.GenerateRegions = false; clone.RegionsApplyMode = MetaDataIASettings.ApplySkip; }
            if (!selectedFields.Contains("categories")) { clone.GenerateCategories = false; clone.CategoriesApplyMode = MetaDataIASettings.ApplySkip; }
            if (!selectedFields.Contains("sortingName")) { clone.GenerateSortingName = false; clone.SortingNameApplyMode = MetaDataIASettings.ApplySkip; }
            if (!selectedFields.Contains("links")) { clone.GenerateLinks = false; clone.LinksApplyMode = MetaDataIASettings.ApplySkip; }
            if (!selectedFields.Contains("releaseDate")) { clone.GenerateReleaseDate = false; clone.ReleaseDateApplyMode = MetaDataIASettings.ApplySkip; }
            if (!selectedFields.Contains("series")) { clone.GenerateSeries = false; clone.SeriesApplyMode = MetaDataIASettings.ApplySkip; }
            return clone;
        }

        private static AiMetadataResult FilterSimulationResult(AiMetadataResult source, HashSet<string> selectedFields)
        {
            var clone = Serialization.GetClone(source);
            if (!selectedFields.Contains("description")) clone.Description = string.Empty;
            if (!selectedFields.Contains("genres")) clone.Genres = new List<string>();
            if (!selectedFields.Contains("tags")) clone.Tags = new List<string>();
            if (!selectedFields.Contains("features")) clone.Features = new List<string>();
            if (!selectedFields.Contains("developers")) clone.Developers = new List<string>();
            if (!selectedFields.Contains("publishers")) clone.Publishers = new List<string>();
            if (!selectedFields.Contains("ageRatings")) clone.AgeRatings = new List<string>();
            if (!selectedFields.Contains("regions")) clone.Regions = new List<string>();
            if (!selectedFields.Contains("categories")) clone.Categories = new List<string>();
            if (!selectedFields.Contains("links")) clone.Links = new List<AiMetadataLink>();
            if (!selectedFields.Contains("releaseDate")) clone.ReleaseDate = string.Empty;
            if (!selectedFields.Contains("series")) clone.Series = new List<string>();
            clone.Provenance = (clone.Provenance ?? new List<MetadataFieldProvenance>())
                .Where(x => x != null && selectedFields.Contains(x.Field))
                .ToList();
            return clone;
        }

        public void ShowHistory()
        {
            if (IsFullscreenMode)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_HistoryDesktopOnly", "The detailed change history is available in Desktop mode."), PluginTitle);
                return;
            }

            new HistoryWindow(this, history).ShowDialog();
        }

        private void ShowGameHistory(Game game)
        {
            ShowGameHistory(game == null ? null : new[] { game });
        }

        private void ShowGameHistory(IEnumerable<Game> games)
        {
            var selectedGames = (games ?? Enumerable.Empty<Game>()).Where(x => x != null).GroupBy(x => x.Id).Select(x => x.First()).ToList();
            if (selectedGames.Count == 0)
            {
                return;
            }

            if (IsFullscreenMode)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_HistoryDesktopOnly", "The detailed change history is available in Desktop mode."), PluginTitle);
                return;
            }

            var label = selectedGames.Count == 1 ? selectedGames[0].Name : string.Join(", ", selectedGames.Select(x => x.Name));
            new HistoryWindow(this, history, selectedGames.Select(x => x.Id), label).ShowDialog();
        }

        private void ShowLatestProvenance(Game game)
        {
            ShowLatestProvenance(game == null ? null : new[] { game });
        }

        private void ShowLatestProvenance(IEnumerable<Game> games)
        {
            var selectedGames = (games ?? Enumerable.Empty<Game>()).Where(x => x != null).GroupBy(x => x.Id).Select(x => x.First()).ToList();
            if (selectedGames.Count == 0)
            {
                return;
            }

            var groups = selectedGames.Select(game => new { Game = game, Entry = history.GetLatestForGame(game.Id) })
                .Where(x => x.Entry != null && x.Entry.Provenance != null && x.Entry.Provenance.Count > 0)
                .Select(x => new ProvenanceGameGroup { GameName = x.Game.Name, Provenance = x.Entry.Provenance })
                .ToList();
            if (groups.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_ProvenanceEmpty", "There is no recorded Metadata AI provenance for this game yet. Run a simulation or apply generated metadata first."), PluginTitle);
                return;
            }

            new ProvenanceWindow(this, groups).ShowDialog();
        }

        private void LearnVocabulary(MetaDataIASettings activeSettings, AiMetadataResult result)
        {
            if (settings == null || settings.Settings == null || result == null)
            {
                return;
            }

            var language = activeSettings == null ? settings.Settings.Language : activeSettings.Language;
            settings.Settings.LearnVocabulary(language, result);
            SaveSettingsSecurely(settings.Settings);
        }

        private void ApplyMediaInteractive(List<Game> games, MetaDataIASettings activeSettings, bool includeLogo = false)
        {
            if (games == null || games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MessageNoGamesMedia", "There are no games to apply media to."), PluginTitle);
                return;
            }

            if (games.Count == 1)
            {
                ShowMediaChooser(games[0], activeSettings, includeLogo: includeLogo);
                return;
            }

            ApplyMedia(games, activeSettings);
        }

        private void ShowMediaChooser(
            Game game,
            MetaDataIASettings activeSettings,
            Action<MetaDataIASettings, List<MediaPreviewOption>> selectionHandler = null,
            Window dialogOwner = null,
            bool includeLogo = false)
        {
            if (game == null)
            {
                return;
            }

            if (!activeSettings.IsMediaConfigured)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(Loc("MTDA_ErrorNoMediaSources", "There are no configured or available media sources."), PluginTitle);
                OpenSettingsView();
                return;
            }

            var kinds = GetEnabledMediaKinds(activeSettings, includeLogo).Where(x => !maintenanceState.IsLocked(game.Id, x)).ToList();
            if (kinds.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MessageNoMediaTypesEnabledOrUnlocked", "No media type is enabled and unlocked for this game."), PluginTitle);
                return;
            }

            var optionsByKind = new Dictionary<MediaKind, List<MediaPreviewOption>>();
            var previewResultsByKind = new Dictionary<MediaKind, MediaPreviewSearchResult>();
            var diagnosticsByKind = new Dictionary<MediaKind, string>();
            var service = new MediaGenerationService(activeSettings, PlayniteApi);
            var searchTextByKind = kinds.ToDictionary(kind => kind, kind => service.GetDefaultSearchText(game, kind));
            Exception loadError = null;
            var cancelled = false;
            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                using (progress.CancelToken.Register(() => cancelled = true))
                {
                    foreach (var kind in kinds)
                    {
                        if (progress.CancelToken.IsCancellationRequested)
                        {
                            cancelled = true;
                            break;
                        }

                        progress.Text = string.Format(Loc("MTDA_ProgressSearchingMediaKind", "Searching for {0} in media sources..."), MediaKindName(kind).ToLowerInvariant());
                        try
                        {
                            var previewResult = service.GetPreviewOptionsWithResolutionFallbackAsync(game, kind, null, progress.CancelToken).GetAwaiter().GetResult();
                            if (activeSettings.MediaUseWebSearch && kind != MediaKind.Logo)
                            {
                                var webCandidates = SearchWebImageCandidates(searchTextByKind[kind], kind, activeSettings);
                                if (previewResult.Options == null)
                                {
                                    previewResult.Options = new List<MediaPreviewOption>();
                                }
                                previewResult.Options.AddRange(webCandidates);
                                var webDiagnostic = string.Format(
                                    Loc("MTDA_WebSearchDiagnostics", "- Web search: {0} candidate(s) found"),
                                    webCandidates.Count);
                                var sourceDiagnostics = service.GetLastDiagnostics(game, kind);
                                diagnosticsByKind[kind] = string.IsNullOrWhiteSpace(sourceDiagnostics)
                                    ? webDiagnostic
                                    : sourceDiagnostics + Environment.NewLine + webDiagnostic;
                            }
                            previewResultsByKind[kind] = previewResult;
                            optionsByKind[kind] = previewResult.Options;
                            if (!diagnosticsByKind.ContainsKey(kind))
                            {
                                diagnosticsByKind[kind] = service.GetLastDiagnostics(game, kind);
                            }
                            if (progress.CancelToken.IsCancellationRequested)
                            {
                                cancelled = true;
                                break;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Failed to load media options.");
                            optionsByKind[kind] = new List<MediaPreviewOption>();
                            diagnosticsByKind[kind] = service.GetLastDiagnostics(game, kind);
                            loadError = ex;
                        }
                    }
                }
            }, new GlobalProgressOptions(PluginTitle + " - " + Loc("MTDA_TabMedia", "Media"), true) { IsIndeterminate = true });

            if (cancelled)
            {
                return;
            }

            if (optionsByKind.Values.All(x => x.Count == 0))
            {
                var diagnosticText = BuildMediaDiagnosticsMessage(kinds, diagnosticsByKind);
                var message = loadError == null
                    ? Loc("MTDA_ErrorNoMediaCandidatesForGame", "No media candidates were found for this game in the configured sources.")
                    : UserError(loadError);
                if (!string.IsNullOrWhiteSpace(diagnosticText))
                {
                    message += Environment.NewLine + Environment.NewLine + diagnosticText;
                }
                if (previewResultsByKind.Values.Any(x => x != null && x.HadCandidatesOutsideRequestedResolution))
                {
                    message += Environment.NewLine + Environment.NewLine +
                        Loc("MTDA_NoCandidatesAtRequestedResolution", "The sources returned candidates, but none matched the requested resolution after validation.");
                }

                PlayniteApi.Dialogs.ShowErrorMessage(message, PluginTitle);
                return;
            }

            var selectedOptions = new Dictionary<MediaKind, MediaPreviewOption>();
            var pickerSettings = Serialization.GetClone(activeSettings);
            pickerSettings.EnsureDefaults();
            var initialResolutionFallbackMessages = new List<string>();
            foreach (var kind in kinds)
            {
                MediaPreviewSearchResult previewResult;
                if (!previewResultsByKind.TryGetValue(kind, out previewResult) || previewResult == null || !previewResult.UsedResolutionFallback)
                {
                    continue;
                }

                string fallbackValue;
                if (!TryGetMediaPickerFormatValueForResolution(kind, previewResult.ResolvedWidth, previewResult.ResolvedHeight, out fallbackValue))
                {
                    fallbackValue = GetAllResolutionsPickerValue(kind);
                }

                var requestedValue = GetMediaPickerFormatValue(pickerSettings, kind);
                if (string.Equals(requestedValue, fallbackValue, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SetMediaPickerFormatValue(pickerSettings, kind, fallbackValue);
                initialResolutionFallbackMessages.Add(BuildMediaResolutionFallbackMessage(kind, requestedValue, fallbackValue, pickerSettings));
            }
            var window = new Window
            {
                Title = PluginTitle + " - " + Loc("MTDA_MediaPickerTitle", "Choose media") + " - " + game.Name,
                Width = 1060,
                Height = 760,
                MinWidth = 820,
                MinHeight = 560,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false
            };
            ApplyPlayniteWindowStyle(window);

            var owner = dialogOwner ?? PlayniteApi.Dialogs.GetCurrentAppWindow();
            if (owner != null)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            var root = new Grid { Margin = new Thickness(14) };
            ApplyDynamicResource(root, Panel.BackgroundProperty, "StandardWindowBackgroundBrush");
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Button applyChangesButton = null;
            var tabs = new TabControl();
            var manualUrlPanels = new Dictionary<MediaKind, UIElement>();
            var manualUrlHost = new ContentControl();
            foreach (var kind in kinds)
            {
                var localKind = kind;
                UIElement manualUrlPanel;
                tabs.Items.Add(CreateTab(MediaKindName(localKind), CreateMediaSearchTabContent(
                    game,
                    localKind,
                    optionsByKind[localKind],
                    searchTextByKind[localKind],
                    pickerSettings,
                    diagnosticsByKind,
                    () =>
                    {
                        selectedOptions.Remove(localKind);
                        if (applyChangesButton != null)
                        {
                            applyChangesButton.IsEnabled = selectedOptions.Count > 0;
                        }
                    },
                    option =>
                    {
                        selectedOptions[option.Kind] = option;
                        if (applyChangesButton != null)
                    {
                            applyChangesButton.IsEnabled = true;
                        }
                    }, out manualUrlPanel)));
                manualUrlPanels[localKind] = manualUrlPanel;
            }

            Grid.SetRow(tabs, 0);
            root.Children.Add(tabs);

            var pickerTools = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 10, 0, 0),
                Margin = new Thickness(0, 10, 0, 0)
            };
            ApplyDynamicResource(pickerTools, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
            var toolsGrid = new Grid();
            toolsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            toolsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pickerTools.Child = toolsGrid;

            var cropContainer = new Border
            {
                Margin = new Thickness(0, 0, 10, 0),
                Visibility = kinds.Contains(MediaKind.Cover) || kinds.Contains(MediaKind.Background)
                    ? Visibility.Visible
                    : Visibility.Collapsed
            };
            ApplyDynamicResource(cropContainer, Border.BackgroundProperty, "ControlBackgroundBrush");

            var cropPanel = new StackPanel();
            cropContainer.Child = cropPanel;
            var cropControls = new StackPanel();
            if (kinds.Contains(MediaKind.Cover))
            {
                cropControls.Children.Add(CreateCropPickerControl(
                    Loc("MTDA_CoverCropAnchor", "Cover crop origin"),
                    Loc("MTDA_CropAnchorPickerHelp", "Choose which area of the image should be preserved when it is cropped to the final aspect ratio."),
                    pickerSettings.CropAnchorOptions,
                    pickerSettings.CoverCropAnchor,
                    value => pickerSettings.CoverCropAnchor = value));
            }

            if (kinds.Contains(MediaKind.Background))
            {
                var globalBackgroundOutput = activeSettings.BackgroundImagePresetOptions
                    .FirstOrDefault(x => string.Equals(x.Value, activeSettings.BackgroundImagePreset, StringComparison.OrdinalIgnoreCase));
                cropControls.Children.Add(CreateCropPickerControl(
                    Loc("MTDA_BackgroundCropAnchor", "Background crop area"),
                    string.Format(
                        Loc("MTDA_BackgroundCropAnchorPickerHelp", "This crop is used when the selected image is larger than, or has a different aspect ratio from, the global output. Final background output: {0}."),
                        globalBackgroundOutput == null ? activeSettings.BackgroundImagePreset : globalBackgroundOutput.DisplayName),
                    pickerSettings.CropAnchorOptions,
                    pickerSettings.BackgroundCropAnchor,
                    value => pickerSettings.BackgroundCropAnchor = value));
            }

            cropPanel.Children.Add(cropControls);
            cropPanel.Children.Add(new TextBlock
            {
                Text = Loc("MTDA_CropPickerHelp", "This choice applies only to media selected in this window and does not change the default setting."),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                Margin = new Thickness(2, 8, 0, 0)
            });
            toolsGrid.Children.Add(cropContainer);

            var toolsDivider = new Border { Width = 1, Margin = new Thickness(0, 4, 0, 4) };
            ApplyDynamicResource(toolsDivider, Border.BackgroundProperty, "DetailsViewBannerPanelBorderBrush");
            Grid.SetColumn(toolsDivider, 1);
            toolsGrid.Children.Add(toolsDivider);

            var manualUrlContainer = new Border
            {
                Margin = new Thickness(10, 0, 0, 0),
                Child = manualUrlHost
            };
            ApplyDynamicResource(manualUrlContainer, Border.BackgroundProperty, "ControlBackgroundBrush");
            Grid.SetColumn(manualUrlContainer, 2);
            toolsGrid.Children.Add(manualUrlContainer);

            if (kinds.Count > 0)
            {
                manualUrlHost.Content = manualUrlPanels[kinds[0]];
            }
            tabs.SelectionChanged += (sender, args) =>
            {
                if (!ReferenceEquals(args.Source, tabs) || tabs.SelectedIndex < 0 || tabs.SelectedIndex >= kinds.Count)
                {
                    return;
                }

                manualUrlHost.Content = manualUrlPanels[kinds[tabs.SelectedIndex]];
            };
            Grid.SetRow(pickerTools, 1);
            root.Children.Add(pickerTools);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            applyChangesButton = new Button
            {
                Content = selectionHandler == null
                    ? Loc("MTDA_ApplyChanges", "Apply changes")
                    : Loc("MTDA_UseSelection", "Use selection"),
                MinWidth = 130,
                IsEnabled = false,
                Margin = new Thickness(0, 0, 8, 0)
            };
            applyChangesButton.Click += (sender, args) =>
            {
                if (selectedOptions.Count == 0)
                {
                    PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MessageSelectMediaBeforeApply", "Select at least one media item before applying changes."), PluginTitle);
                    return;
                }

                window.DialogResult = true;
            };
            buttonsPanel.Children.Add(applyChangesButton);

            var closeButton = new Button
            {
                Content = Loc("MTDA_Close", "Close"),
                MinWidth = 100
            };
            closeButton.Click += (sender, args) =>
            {
                window.DialogResult = false;
            };
            buttonsPanel.Children.Add(closeButton);

            Grid.SetRow(buttonsPanel, 2);
            root.Children.Add(buttonsPanel);

            window.Content = root;
            if (initialResolutionFallbackMessages.Count > 0)
            {
                PlayniteApi.Dialogs.ShowMessage(string.Join(Environment.NewLine + Environment.NewLine, initialResolutionFallbackMessages), PluginTitle);
            }
            bool? accepted = null;
            try
            {
                accepted = window.ShowDialog();
            }
            finally
            {
                RestoreWindowActivation(owner);
            }

            if (accepted == true && selectedOptions.Count > 0)
            {
                var orderedOptions = kinds.Where(selectedOptions.ContainsKey).Select(kind => selectedOptions[kind]).ToList();
                if (selectionHandler != null)
                {
                    selectionHandler(activeSettings, orderedOptions);
                }
                else
                {
                    // Picker resolution is a candidate filter only. Processing must
                    // always follow the user's saved global media configuration.
                    ApplySelectedMediaOptions(game, activeSettings, orderedOptions);
                }
            }
        }

        private static void RestoreWindowActivation(Window owner)
        {
            if (owner == null)
            {
                return;
            }

            try
            {
                owner.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!owner.IsVisible)
                    {
                        return;
                    }

                    owner.IsEnabled = true;
                    owner.Activate();
                    owner.Focus();
                }));
            }
            catch
            {
            }
        }

        internal MediaSimulationChange SelectMediaForSimulation(Game game, MediaKind kind, MetaDataIASettings activeSettings, Window owner)
        {
            if (game == null || activeSettings == null)
            {
                return null;
            }

            var focus = kind == MediaKind.Cover ? "cover" : kind == MediaKind.Icon ? "icon" : "background";
            var focusedSettings = CreateFocusedMediaSettings(activeSettings, focus);
            MediaSimulationChange selected = null;
            ShowMediaChooser(game, focusedSettings, (pickerSettings, options) =>
            {
                var option = options == null ? null : options.FirstOrDefault(x => x.Kind == kind);
                if (option != null)
                {
                    selected = new MediaSimulationChange
                    {
                        Kind = kind,
                        Option = option,
                        Settings = pickerSettings,
                        IsSelected = true
                    };
                }
            }, owner);
            return selected;
        }

        private static UIElement CreateCropPickerControl(string label, string hint, IEnumerable<LocalizedOption> options, string selectedValue, Action<string> changed)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Stretch };
            var labelText = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 0, 0, 4)
            };
            ApplyDynamicResource(labelText, TextBlock.ForegroundProperty, "TextBrush");
            panel.Children.Add(labelText);

            var combo = new ComboBox
            {
                ItemsSource = options,
                DisplayMemberPath = "DisplayName",
                SelectedValuePath = "Value",
                SelectedValue = selectedValue,
                MinWidth = 230,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            combo.SelectionChanged += (sender, args) =>
            {
                var value = combo.SelectedValue as string;
                if (!string.IsNullOrWhiteSpace(value) && changed != null)
                {
                    changed(value);
                }
            };
            panel.Children.Add(combo);
            var hintText = new TextBlock
            {
                Text = hint,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                Margin = new Thickness(2, 5, 0, 0)
            };
            ApplyDynamicResource(hintText, TextBlock.ForegroundProperty, "TextBrush");
            panel.Children.Add(hintText);
            return panel;
        }

        private IEnumerable<LocalizedOption> GetMediaPickerFormatOptions(MetaDataIASettings settings, MediaKind kind)
        {
            if (settings == null || kind == MediaKind.Logo)
            {
                return Enumerable.Empty<LocalizedOption>();
            }

            if (kind == MediaKind.Cover)
            {
                return RenamePickerOriginalOption(settings.CoverImagePresetOptions, MetaDataIASettings.CoverPresetOriginal);
            }

            if (kind == MediaKind.Icon)
            {
                return RenamePickerOriginalOption(settings.IconPresetOptions, MetaDataIASettings.IconPresetOriginal);
            }

            return RenamePickerOriginalOption(settings.BackgroundImagePresetOptions, MetaDataIASettings.BackgroundPresetOriginal);
        }

        private IEnumerable<LocalizedOption> RenamePickerOriginalOption(IEnumerable<LocalizedOption> options, string originalValue)
        {
            return (options ?? Enumerable.Empty<LocalizedOption>())
                .Select(x => new LocalizedOption(
                    x.Value,
                    string.Equals(x.Value, originalValue, StringComparison.OrdinalIgnoreCase)
                        ? Loc("MTDA_MediaSearchAllResolutions", "All resolutions")
                        : x.DisplayName))
                .ToList();
        }

        private static string GetMediaPickerFormatValue(MetaDataIASettings settings, MediaKind kind)
        {
            if (kind == MediaKind.Cover)
            {
                return settings.CoverImagePreset;
            }

            if (kind == MediaKind.Icon)
            {
                return settings.IconPreset;
            }

            return settings.BackgroundImagePreset;
        }

        private static void SetMediaPickerFormatValue(MetaDataIASettings settings, MediaKind kind, string value)
        {
            if (kind == MediaKind.Cover)
            {
                settings.CoverImagePreset = value;
            }
            else if (kind == MediaKind.Icon)
            {
                settings.IconPreset = value;
            }
            else if (kind == MediaKind.Background)
            {
                settings.BackgroundImagePreset = value;
            }
        }

        private static bool TryGetMediaPickerFormatValueForResolution(MediaKind kind, int width, int height, out string value)
        {
            value = null;
            if (kind == MediaKind.Background)
            {
                if (width == 3840 && height == 1240) { value = MetaDataIASettings.BackgroundPresetSteamHero; return true; }
                if (width == 1920 && height == 620) { value = MetaDataIASettings.BackgroundPresetSteamHeroSmall; return true; }
                if (width == 1280 && height == 720) { value = MetaDataIASettings.BackgroundPresetHd; return true; }
                if (width == 1920 && height == 1080) { value = MetaDataIASettings.BackgroundPresetFullHd; return true; }
                if (width == 2560 && height == 1440) { value = MetaDataIASettings.BackgroundPresetQhd; return true; }
                if (width == 3840 && height == 2160) { value = MetaDataIASettings.BackgroundPreset4K; return true; }
            }

            if (kind == MediaKind.Cover)
            {
                if (width == 600 && height == 600) { value = MetaDataIASettings.CoverPresetSquare; return true; }
                if (width == 920 && height == 430) { value = MetaDataIASettings.CoverPresetHorizontal; return true; }
                if (width == 600 && height == 900) { value = MetaDataIASettings.CoverPresetPlayniteVertical; return true; }
            }

            return false;
        }

        private static string GetAllResolutionsPickerValue(MediaKind kind)
        {
            if (kind == MediaKind.Cover) return MetaDataIASettings.CoverPresetOriginal;
            if (kind == MediaKind.Icon) return MetaDataIASettings.IconPresetOriginal;
            return MetaDataIASettings.BackgroundPresetOriginal;
        }

        private static IEnumerable<string> GetPickerSourceNames(MetaDataIASettings source, MediaKind kind)
        {
            if (source == null) return Enumerable.Empty<string>();
            var names = new List<string>();
            if (source.UseOriginIntegrationForMedia && kind != MediaKind.Logo) names.Add(MetaDataIASettings.SourceOriginIntegration);
            if (source.MediaUseSteamOfficial && kind != MediaKind.Logo) names.Add("Steam official");
            if (source.MediaUseSteamScreenshots && kind == MediaKind.Background) names.Add("Steam screenshots");
            if (source.MediaUsePsnStore && kind != MediaKind.Logo) names.Add(OfficialStoreDataService.SourcePsnStore);
            if (source.MediaUseXboxStore && kind != MediaKind.Logo) names.Add(OfficialStoreDataService.SourceXboxStore);
            if (source.MediaUseEpicStore && kind != MediaKind.Logo) names.Add(OfficialStoreDataService.SourceEpicStore);
            if (source.MediaUseSteamGridDb) names.Add("SteamGridDB");
            if (source.MediaUseRawg && kind != MediaKind.Logo) names.Add("RAWG");
            if (source.MediaUseWallhaven && kind == MediaKind.Background) names.Add("Wallhaven");
            if (source.MediaUseScreenScraper) names.Add("ScreenScraper");
            if (source.MediaUseGiantBomb && kind != MediaKind.Logo) names.Add("Giant Bomb");
            if (source.MediaUseMobyGames && kind != MediaKind.Logo) names.Add("MobyGames");
            if (source.MediaUseIgdb && kind != MediaKind.Logo) names.Add("IGDB");
            if (source.MediaUseWebSearch && kind != MediaKind.Logo) names.Add("Web search");
            return names;
        }

        private static MetaDataIASettings CreatePickerSourceFilteredSettings(MetaDataIASettings source, ISet<string> enabledSources)
        {
            var value = Serialization.GetClone(source);
            var enabled = enabledSources ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            value.UseOriginIntegrationForMedia &= enabled.Contains(MetaDataIASettings.SourceOriginIntegration);
            value.MediaUseSteamOfficial &= enabled.Contains("Steam official");
            value.MediaUseSteamScreenshots &= enabled.Contains("Steam screenshots");
            value.MediaUsePsnStore &= enabled.Contains(OfficialStoreDataService.SourcePsnStore);
            value.MediaUseXboxStore &= enabled.Contains(OfficialStoreDataService.SourceXboxStore);
            value.MediaUseEpicStore &= enabled.Contains(OfficialStoreDataService.SourceEpicStore);
            value.MediaUseSteamGridDb &= enabled.Contains("SteamGridDB");
            value.MediaUseRawg &= enabled.Contains("RAWG");
            value.MediaUseWallhaven &= enabled.Contains("Wallhaven");
            value.MediaUseScreenScraper &= enabled.Contains("ScreenScraper");
            value.MediaUseGiantBomb &= enabled.Contains("Giant Bomb");
            value.MediaUseMobyGames &= enabled.Contains("MobyGames");
            value.MediaUseIgdb &= enabled.Contains("IGDB");
            return value;
        }

        private string BuildPickerSourceFilterLabel(int selected, int total)
        {
            return string.Format(Loc("MTDA_MediaSourceFilter", "Sources ({0}/{1})"), selected, total);
        }

        private string BuildPickerFilterLabel(int selectedSources, int totalSources, MediaPickerCandidateFilter filter)
        {
            var activeFilters = filter == null ? 0 : filter.ActiveCount;
            var sourceSummary = string.Format(
                GetLocalizedOrFallback("MTDA_MediaFiltersSourceSummary", "Sources: {0} of {1}"),
                selectedSources,
                totalSources);
            var filterSummary = activeFilters == 0
                ? GetLocalizedOrFallback("MTDA_MediaFiltersNoActive", "no candidate filters active")
                : string.Format(
                    GetLocalizedOrFallback("MTDA_MediaFiltersActiveSummary", "{0} candidate filters active"),
                    activeFilters);
            return sourceSummary + " \u00b7 " + filterSummary;
        }

        private string GetPickerSourceDisplayName(string sourceName)
        {
            return string.Equals(sourceName, "Web search", StringComparison.OrdinalIgnoreCase)
                ? GetLocalizedOrFallback("MTDA_SourceWebSearch", "Web search")
                : sourceName;
        }

        private static List<MediaPreviewOption> ApplyPickerCandidateFilters(
            IEnumerable<MediaPreviewOption> options,
            MediaPickerCandidateFilter filter)
        {
            var result = options == null ? Enumerable.Empty<MediaPreviewOption>() : options.Where(x => x != null);
            if (filter == null)
            {
                return result.ToList();
            }

            if (filter.OfficialOnly)
            {
                result = result.Where(x => x.IsOfficial);
            }

            if (filter.HideScreenshots)
            {
                result = result.Where(x => string.IsNullOrWhiteSpace(x.Style) ||
                    x.Style.IndexOf("screenshot", StringComparison.OrdinalIgnoreCase) < 0);
            }

            if (string.Equals(filter.Aspect, "landscape", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Where(x => x.Width > x.Height);
            }
            else if (string.Equals(filter.Aspect, "portrait", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Where(x => x.Height > x.Width);
            }
            else if (string.Equals(filter.Aspect, "square", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Where(x => x.Width > 0 && x.Height > 0 && Math.Abs(x.Width - x.Height) <= Math.Max(x.Width, x.Height) * 0.08);
            }

            if (filter.Styles != null && filter.Styles.Count > 0)
            {
                result = result.Where(x => !string.IsNullOrWhiteSpace(x.Style) && filter.Styles.Contains(x.Style));
            }

            return result.ToList();
        }

        private string GetMediaStyleLabel(string style)
        {
            var value = (style ?? string.Empty).Trim().ToLowerInvariant();
            if (value == "custom") return GetLocalizedOrFallback("MTDA_MediaStyleCustom", "Custom");
            if (value == "official") return GetLocalizedOrFallback("MTDA_MediaStyleOfficial", "Official");
            if (value.IndexOf("official portrait banner", StringComparison.OrdinalIgnoreCase) >= 0)
                return GetLocalizedOrFallback("MTDA_MediaStyleOfficialPortraitBanner", "Official portrait banner");
            if (value.IndexOf("official banner", StringComparison.OrdinalIgnoreCase) >= 0)
                return GetLocalizedOrFallback("MTDA_MediaStyleOfficialBanner", "Official banner");
            if (value.IndexOf("official cover", StringComparison.OrdinalIgnoreCase) >= 0)
                return GetLocalizedOrFallback("MTDA_MediaStyleOfficialCover", "Official cover");
            if ((value.IndexOf("official", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 value.IndexOf("oficial", StringComparison.OrdinalIgnoreCase) >= 0) &&
                value.IndexOf("logo", StringComparison.OrdinalIgnoreCase) >= 0)
                return GetLocalizedOrFallback("MTDA_MediaStyleOfficialLogo", "Official logo");
            if (value.IndexOf("screenshot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return value.IndexOf("no_logo", StringComparison.OrdinalIgnoreCase) >= 0
                    ? GetLocalizedOrFallback("MTDA_MediaStyleOfficialScreenshotNoLogo", "Official screenshot (no logo)")
                    : GetLocalizedOrFallback("MTDA_MediaStyleOfficialScreenshot", "Official screenshot");
            }
            if (value.IndexOf("game hub", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return value.IndexOf("no_logo", StringComparison.OrdinalIgnoreCase) >= 0
                    ? GetLocalizedOrFallback("MTDA_MediaStyleOfficialBackgroundNoLogo", "Official background (no logo)")
                    : GetLocalizedOrFallback("MTDA_MediaStyleOfficialBackground", "Official background");
            }
            if (value == "alternate") return GetLocalizedOrFallback("MTDA_MediaStyleAlternate", "Alternate artwork");
            if (value == "material") return GetLocalizedOrFallback("MTDA_MediaStyleMaterial", "Promotional artwork");
            if (value == "no_logo") return GetLocalizedOrFallback("MTDA_MediaStyleNoLogo", "Without logo");
            if (value == "white_logo") return GetLocalizedOrFallback("MTDA_MediaStyleWhiteLogo", "White logo");
            if (value == "blurred") return GetLocalizedOrFallback("MTDA_MediaStyleBlurred", "Blurred");
            return string.IsNullOrWhiteSpace(style) ? GetLocalizedOrFallback("MTDA_MediaStyleUnknown", "Unspecified") : style;
        }

        private string GetLocalizedOrFallback(string key, string fallback)
        {
            var value = Loc(key, fallback);
            return string.IsNullOrWhiteSpace(value) || value.StartsWith("<!", StringComparison.Ordinal) ? fallback : value;
        }

        private List<MediaPreviewOption> SearchWebImageCandidates(
            string query,
            MediaKind kind,
            MetaDataIASettings pickerSettings = null,
            MediaPickerCandidateFilter candidateFilter = null)
        {
            var bingResults = SearchBingImageCandidates(query, kind, pickerSettings, candidateFilter);
            if (bingResults.Count > 0)
            {
                return bingResults;
            }

            var results = new List<MediaPreviewOption>();
            if (PlayniteApi == null || PlayniteApi.WebViews == null || string.IsNullOrWhiteSpace(query)) return results;
            try
            {
                var searchQuery = BuildWebImageSearchQuery(query, kind, pickerSettings, candidateFilter);
                using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                {
                    view.NavigateAndWait("https://www.google.com/search?tbm=isch&safe=on&q=" + Uri.EscapeDataString(searchQuery));
                    if ((view.GetCurrentAddress() ?? string.Empty).StartsWith("https://consent.google.com", StringComparison.OrdinalIgnoreCase))
                    {
                        view.EvaluateScriptAsync("document.getElementsByTagName('form')[0].submit();").Wait();
                        Thread.Sleep(3000);
                        view.NavigateAndWait("https://www.google.com/search?tbm=isch&safe=on&q=" + Uri.EscapeDataString(searchQuery));
                    }
                    var source = Regex.Replace(view.GetPageSource() ?? string.Empty, @"\r\n?|\n", string.Empty);
                    var pattern = new Regex(@"\[\""(https:\/\/encrypted-[^,]+?)\"",\d+,\d+\],\[\""(http.+?)\"",(\d+),(\d+)\]", RegexOptions.Compiled);
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Match match in pattern.Matches(source))
                    {
                        try
                        {
                            var data = JsonConvert.DeserializeObject<List<List<object>>>("[" + match.Value + "]");
                            if (data == null || data.Count < 2 || data[1].Count < 3) continue;
                            var url = Convert.ToString(data[1][0]);
                            var height = Convert.ToInt32(data[1][1]);
                            var width = Convert.ToInt32(data[1][2]);
                            if (string.IsNullOrWhiteSpace(url) || width <= 0 || height <= 0 || !seen.Add(url)) continue;
                            results.Add(new MediaPreviewOption { Kind = kind, Url = url, Width = width, Height = height, Extension = System.IO.Path.GetExtension(new Uri(url).AbsolutePath), SourceName = GetLocalizedOrFallback("MTDA_SourceWebSearch", "Web search"), Style = GetLocalizedOrFallback("MTDA_MediaStyleWeb", "Web result") });
                        }
                        catch
                        {
                            // A malformed result must not discard the rest of the page.
                        }
                        if (results.Count >= 40) break;
                    }
                }
            }
            catch (Exception ex) { logger.Warn(ex, "Web image search failed."); }
            return ValidateWebImageCandidates(results);
        }

        private static List<MediaPreviewOption> ValidateWebImageCandidates(IEnumerable<MediaPreviewOption> candidates)
        {
            var valid = new List<MediaPreviewOption>();
            var pending = (candidates ?? Enumerable.Empty<MediaPreviewOption>()).Where(x => x != null).Take(30).ToList();
            const int batchSize = 5;
            for (var offset = 0; offset < pending.Count; offset += batchSize)
            {
                var batch = pending.Skip(offset).Take(batchSize).ToList();
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                {
                    var probes = batch.Select(option => MediaGenerationService.ValidatePreviewOptionAsync(option, cancellation.Token)).ToArray();
                    bool[] results;
                    try
                    {
                        results = Task.WhenAll(probes).GetAwaiter().GetResult();
                    }
                    catch
                    {
                        continue;
                    }

                    for (var index = 0; index < batch.Count; index++)
                    {
                        if (results[index])
                        {
                            valid.Add(batch[index]);
                        }
                    }
                }
            }

            return valid;
        }

        private List<MediaPreviewOption> SearchBingImageCandidates(
            string query,
            MediaKind kind,
            MetaDataIASettings pickerSettings,
            MediaPickerCandidateFilter candidateFilter)
        {
            var results = new List<MediaPreviewOption>();
            if (string.IsNullOrWhiteSpace(query))
            {
                return results;
            }

            try
            {
                var searchQuery = BuildWebImageSearchQuery(query, kind, pickerSettings, candidateFilter);
                var relevanceTerms = GetWebSearchTerms(query);
                var address = "https://www.bing.com/images/search?form=HDRSC3&adlt=strict&setlang=en&q=" + Uri.EscapeDataString(searchQuery);
                string source;
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131 Safari/537.36";
                    client.Headers[HttpRequestHeader.AcceptLanguage] = "en-US,en;q=0.9";
                    source = client.DownloadString(address);
                }

                var pattern = new Regex(
                    @"<a\b(?=[^>]*\bclass=\""[^\""\r\n]*\biusc\b[^\""\r\n]*\"")(?=[^>]*\bm=\""(?<data>[^\""\r\n]+)\"")[^>]*>",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in pattern.Matches(source ?? string.Empty))
                {
                    try
                    {
                        var payload = JsonConvert.DeserializeObject<Dictionary<string, object>>(WebUtility.HtmlDecode(match.Groups["data"].Value));
                        object rawUrl;
                        if (payload == null || !payload.TryGetValue("murl", out rawUrl))
                        {
                            continue;
                        }

                        var url = Convert.ToString(rawUrl);
                        object rawTitle;
                        object rawPageUrl;
                        payload.TryGetValue("t", out rawTitle);
                        payload.TryGetValue("purl", out rawPageUrl);
                        var searchableText = string.Join(" ", new[]
                        {
                            Convert.ToString(rawTitle),
                            Convert.ToString(rawPageUrl),
                            url
                        });
                        if (!IsWebSearchResultRelevant(searchableText, relevanceTerms))
                        {
                            continue;
                        }

                        Uri uri;
                        if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                            !seen.Add(url))
                        {
                            continue;
                        }

                        results.Add(new MediaPreviewOption
                        {
                            Kind = kind,
                            Url = url,
                            // Bing's public search markup does not consistently expose
                            // the original size. The normal download pipeline validates it
                            // before applying the selected candidate.
                            Width = 0,
                            Height = 0,
                            Extension = System.IO.Path.GetExtension(uri.AbsolutePath),
                            SourceName = GetLocalizedOrFallback("MTDA_SourceWebSearch", "Web search"),
                            Style = GetLocalizedOrFallback("MTDA_MediaStyleWeb", "Web result")
                        });
                    }
                    catch
                    {
                        // Skip malformed results without discarding the remaining page.
                    }

                    if (results.Count >= 40)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Bing image search failed.");
            }

            return ValidateWebImageCandidates(results);
        }

        private static string BuildWebImageSearchQuery(
            string query,
            MediaKind kind,
            MetaDataIASettings pickerSettings,
            MediaPickerCandidateFilter candidateFilter)
        {
            var terms = new List<string> { "game" };
            if (kind == MediaKind.Cover)
            {
                terms.Add("cover art");
            }
            else if (kind == MediaKind.Icon)
            {
                terms.Add("icon");
            }
            else
            {
                terms.Add("wallpaper");
            }

            var filterAspect = candidateFilter == null ? null : candidateFilter.Aspect;
            if (string.Equals(filterAspect, "portrait", StringComparison.OrdinalIgnoreCase))
            {
                terms.Add("portrait vertical");
            }
            else if (string.Equals(filterAspect, "landscape", StringComparison.OrdinalIgnoreCase))
            {
                terms.Add("landscape");
            }
            else if (string.Equals(filterAspect, "square", StringComparison.OrdinalIgnoreCase))
            {
                terms.Add("square");
            }
            else
            {
                AddWebSearchTermsForResolution(terms, pickerSettings, kind);
            }

            return (query ?? string.Empty).Trim() + " " + string.Join(" ", terms);
        }

        private static void AddWebSearchTermsForResolution(List<string> terms, MetaDataIASettings pickerSettings, MediaKind kind)
        {
            if (terms == null || pickerSettings == null)
            {
                return;
            }

            var preset = GetMediaPickerFormatValue(pickerSettings, kind);
            if (kind == MediaKind.Cover)
            {
                if (string.Equals(preset, MetaDataIASettings.CoverPresetPlayniteVertical, StringComparison.OrdinalIgnoreCase))
                {
                    terms.Add("portrait vertical 600x900");
                }
                else if (string.Equals(preset, MetaDataIASettings.CoverPresetSquare, StringComparison.OrdinalIgnoreCase))
                {
                    terms.Add("square 600x600");
                }
                else if (string.Equals(preset, MetaDataIASettings.CoverPresetHorizontal, StringComparison.OrdinalIgnoreCase))
                {
                    terms.Add("horizontal 920x430");
                }
                return;
            }

            if (kind == MediaKind.Icon)
            {
                if (!string.Equals(preset, MetaDataIASettings.IconPresetOriginal, StringComparison.OrdinalIgnoreCase))
                {
                    terms.Add("square 256 transparent png");
                }
                return;
            }

            if (kind != MediaKind.Background)
            {
                return;
            }

            if (string.Equals(preset, MetaDataIASettings.BackgroundPresetSteamHero, StringComparison.OrdinalIgnoreCase)) terms.Add("3840x1240 ultrawide");
            else if (string.Equals(preset, MetaDataIASettings.BackgroundPresetSteamHeroSmall, StringComparison.OrdinalIgnoreCase)) terms.Add("1920x620 ultrawide");
            else if (string.Equals(preset, MetaDataIASettings.BackgroundPresetHd, StringComparison.OrdinalIgnoreCase)) terms.Add("1280x720 16:9");
            else if (string.Equals(preset, MetaDataIASettings.BackgroundPresetFullHd, StringComparison.OrdinalIgnoreCase)) terms.Add("1920x1080 16:9");
            else if (string.Equals(preset, MetaDataIASettings.BackgroundPresetQhd, StringComparison.OrdinalIgnoreCase)) terms.Add("2560x1440 16:9");
            else if (string.Equals(preset, MetaDataIASettings.BackgroundPreset4K, StringComparison.OrdinalIgnoreCase)) terms.Add("3840x2160 16:9");
        }

        private static List<string> GetWebSearchTerms(string query)
        {
            var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "game", "videojuego", "pc", "steam", "cover", "art", "wallpaper", "background", "fondo",
                "icon", "logo", "goty", "edition", "edicion", "complete", "definitive", "deluxe", "ultimate",
                "enhanced", "remastered", "remake", "director", "cut", "the", "and", "of", "for", "de", "la", "el"
            };
            return Regex.Matches(query ?? string.Empty, @"[\p{L}\p{N}]{3,}")
                .Cast<Match>()
                .Select(x => x.Value.ToLowerInvariant())
                .Where(x => !ignored.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsWebSearchResultRelevant(string searchableText, List<string> terms)
        {
            if (terms == null || terms.Count == 0)
            {
                return true;
            }

            var text = (searchableText ?? string.Empty).ToLowerInvariant();
            var matches = terms.Count(term => text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            // A one-word title such as Palworld must match exactly. For longer
            // titles, two matching significant words reject unrelated pages while
            // still allowing editions and subtitles to differ between sources.
            return matches >= (terms.Count == 1 ? 1 : 2);
        }

        private string BuildMediaResolutionFallbackMessage(MediaKind kind, string requestedValue, string fallbackValue, MetaDataIASettings pickerSettings)
        {
            var requested = GetMediaPickerFormatOptions(pickerSettings, kind).FirstOrDefault(x => string.Equals(x.Value, requestedValue, StringComparison.OrdinalIgnoreCase));
            var fallback = GetMediaPickerFormatOptions(pickerSettings, kind).FirstOrDefault(x => string.Equals(x.Value, fallbackValue, StringComparison.OrdinalIgnoreCase));
            return string.Format(
                Loc("MTDA_MediaResolutionFallback", "No {0} candidates were available at {1}. The filter was changed to the closest available resolution: {2}."),
                MediaKindName(kind).ToLowerInvariant(),
                requested == null ? requestedValue : requested.DisplayName,
                fallback == null ? fallbackValue : fallback.DisplayName);
        }

        private UIElement CreateMediaSearchTabContent(
            Game game,
            MediaKind kind,
            List<MediaPreviewOption> initialOptions,
            string initialSearchText,
            MetaDataIASettings pickerSettings,
            Dictionary<MediaKind, string> diagnosticsByKind,
            Action clearSelection,
            Action<MediaPreviewOption> selectAction,
            out UIElement manualUrlControl)
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var searchPanel = new Grid { Margin = new Thickness(0, 8, 0, 10) };
            searchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var queryPanel = new StackPanel();
            var searchLabel = new TextBlock
            {
                Text = Loc("MTDA_MediaSearchTerms", "Search terms"),
                Margin = new Thickness(0, 0, 0, 4)
            };
            ApplyDynamicResource(searchLabel, TextBlock.ForegroundProperty, "TextBrush");
            queryPanel.Children.Add(searchLabel);

            var searchBox = new TextBox
            {
                Text = initialSearchText ?? string.Empty,
                VerticalContentAlignment = VerticalAlignment.Center,
                MinWidth = 240
            };
            queryPanel.Children.Add(searchBox);
            searchPanel.Children.Add(queryPanel);

            var searchButton = new Button
            {
                Content = Loc("MTDA_Search", "Search"),
                MinWidth = 100,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8, 0, 0, 0)
            };
            searchBox.SetBinding(FrameworkElement.HeightProperty, new Binding("ActualHeight") { Source = searchButton });
            Grid.SetColumn(searchButton, 1);
            searchPanel.Children.Add(searchButton);

            Action runSearch = null;
            ComboBox formatCombo = null;
            var formatOptions = GetMediaPickerFormatOptions(pickerSettings, kind).ToList();

            var manualUrlPanel = new StackPanel();
            var manualUrlLabel = new TextBlock
            {
                Text = Loc("MTDA_ManualMediaUrl", "External image URL"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 0, 0, 4)
            };
            ApplyDynamicResource(manualUrlLabel, TextBlock.ForegroundProperty, "TextBrush");
            manualUrlPanel.Children.Add(manualUrlLabel);
            var manualUrlControls = new Grid();
            manualUrlControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            manualUrlControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var manualUrlBox = new TextBox
            {
                MinWidth = 230,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = Loc("MTDA_ManualMediaUrlHelp", "Paste a direct HTTP or HTTPS image URL to use the same crop and processing rules.")
            };
            manualUrlControls.Children.Add(manualUrlBox);
            var previewUrlButton = new Button
            {
                Content = Loc("MTDA_PreviewManualMediaUrl", "Preview URL"),
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 108,
                IsEnabled = false
            };
            Grid.SetColumn(previewUrlButton, 1);
            manualUrlControls.Children.Add(previewUrlButton);
            var clearUrlButton = new Button
            {
                Content = Loc("MTDA_RestoreMediaCandidates", "Restore candidates"),
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 142,
                IsEnabled = false
            };
            manualUrlPanel.Children.Add(manualUrlControls);
            var manualUrlHint = new TextBlock
            {
                Text = Loc("MTDA_ManualMediaUrlHelp", "Paste a direct HTTP or HTTPS image URL to use the same crop and processing rules."),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                Margin = new Thickness(2, 5, 0, 0)
            };
            ApplyDynamicResource(manualUrlHint, TextBlock.ForegroundProperty, "TextBrush");
            manualUrlPanel.Children.Add(manualUrlHint);
            var manualSourceDivider = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 9) };
            ApplyDynamicResource(manualSourceDivider, Border.BackgroundProperty, "DetailsViewBannerPanelBorderBrush");
            manualUrlPanel.Children.Add(manualSourceDivider);
            var localFileLabel = new TextBlock
            {
                Text = Loc("MTDA_ManualMediaLocalFile", "Or choose an image from this device"),
                Margin = new Thickness(2, 0, 0, 5)
            };
            ApplyDynamicResource(localFileLabel, TextBlock.ForegroundProperty, "TextBrush");
            manualUrlPanel.Children.Add(localFileLabel);
            var manualFileActions = new StackPanel { Orientation = Orientation.Horizontal };
            var chooseLocalFileButton = new Button
            {
                Content = Loc("MTDA_ChooseManualMediaFile", "Choose image from device"),
                MinWidth = 180,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = Loc("MTDA_ChooseManualMediaFileHelp", "Choose a local image to use the same crop and processing rules.")
            };
            manualFileActions.Children.Add(chooseLocalFileButton);
            manualFileActions.Children.Add(clearUrlButton);
            manualUrlPanel.Children.Add(manualFileActions);
            root.Children.Add(searchPanel);

            var candidatesArea = new Grid();
            candidatesArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            candidatesArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(candidatesArea, 1);
            root.Children.Add(candidatesArea);
            var filterSidebar = new Border
            {
                Width = 270,
                Margin = new Thickness(0, 0, 12, 0),
                Padding = new Thickness(12),
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            ApplyDynamicResource(filterSidebar, Border.BackgroundProperty, "ControlBackgroundBrush");
            ApplyDynamicResource(filterSidebar, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
            filterSidebar.BorderThickness = new Thickness(0, 0, 1, 0);
            candidatesArea.Children.Add(filterSidebar);
            var resultsHost = new ContentControl { Content = null };
            Grid.SetColumn(resultsHost, 1);
            candidatesArea.Children.Add(resultsHost);

            var availableOptions = initialOptions ?? new List<MediaPreviewOption>();
            var hasManualLocalFile = false;
            var pickerSourceNames = GetPickerSourceNames(pickerSettings, kind).ToList();
            var selectedPickerSources = new HashSet<string>(pickerSourceNames, StringComparer.OrdinalIgnoreCase);
            var pickerCandidateFilter = new MediaPickerCandidateFilter();
            MediaPreviewOption displayedManualOption = null;
            Action rebuildOptions = null;
            Action restoreAvailableOptions = null;
            Action<FrameworkElement> showPickerFilters = null;
            rebuildOptions = () =>
            {
                var shown = displayedManualOption == null
                    ? ApplyPickerCandidateFilters(availableOptions, pickerCandidateFilter)
                    : new List<MediaPreviewOption> { displayedManualOption };
                resultsHost.Content = CreateMediaOptionsPanel(
                    shown,
                    selectAction,
                    displayedManualOption,
                    pickerSettings,
                    rebuildOptions,
                    showPickerFilters,
                    BuildPickerFilterLabel(selectedPickerSources.Count, pickerSourceNames.Count, pickerCandidateFilter));
            };
            restoreAvailableOptions = () =>
            {
                displayedManualOption = null;
                rebuildOptions();
            };
            rebuildOptions();
            showPickerFilters = placementTarget =>
            {
                var menu = new ContextMenu { StaysOpen = true };
                var sourcesMenu = new MenuItem { Header = Loc("MTDA_MediaFilterSources", "Sources") };
                foreach (var sourceName in pickerSourceNames)
                {
                    var localSourceName = sourceName;
                    var item = new MenuItem
                    {
                        Header = localSourceName,
                        IsCheckable = true,
                        IsChecked = selectedPickerSources.Contains(localSourceName),
                        StaysOpenOnClick = true
                    };
                    item.Click += (sender, args) =>
                    {
                        if (item.IsChecked) selectedPickerSources.Add(localSourceName);
                        else selectedPickerSources.Remove(localSourceName);
                    };
                    sourcesMenu.Items.Add(item);
                }
                if (pickerSourceNames.Count > 0)
                {
                    sourcesMenu.Items.Add(new Separator());
                }
                var updateItem = new MenuItem { Header = Loc("MTDA_UpdateMediaSourceFilter", "Update candidates") };
                updateItem.Click += (sender, args) =>
                {
                    menu.IsOpen = false;
                    if (runSearch != null) runSearch();
                };
                sourcesMenu.Items.Add(updateItem);
                menu.Items.Add(sourcesMenu);
                menu.Items.Add(new Separator());

                var officialItem = new MenuItem
                {
                    Header = Loc("MTDA_MediaFilterOfficialOnly", "Official candidates only"),
                    IsCheckable = true,
                    IsChecked = pickerCandidateFilter.OfficialOnly,
                    StaysOpenOnClick = true
                };
                officialItem.Click += (sender, args) =>
                {
                    pickerCandidateFilter.OfficialOnly = officialItem.IsChecked;
                    rebuildOptions();
                };
                menu.Items.Add(officialItem);
                var screenshotsItem = new MenuItem
                {
                    Header = Loc("MTDA_MediaFilterHideScreenshots", "Hide screenshots"),
                    IsCheckable = true,
                    IsChecked = pickerCandidateFilter.HideScreenshots,
                    StaysOpenOnClick = true
                };
                screenshotsItem.Click += (sender, args) =>
                {
                    pickerCandidateFilter.HideScreenshots = screenshotsItem.IsChecked;
                    rebuildOptions();
                };
                menu.Items.Add(screenshotsItem);

                var aspectMenu = new MenuItem { Header = Loc("MTDA_MediaFilterAspect", "Shape") };
                foreach (var aspect in new[]
                {
                    new { Value = "all", Label = Loc("MTDA_MediaFilterAllShapes", "Any shape") },
                    new { Value = "landscape", Label = Loc("MTDA_MediaFilterLandscape", "Landscape") },
                    new { Value = "portrait", Label = Loc("MTDA_MediaFilterPortrait", "Portrait") },
                    new { Value = "square", Label = Loc("MTDA_MediaFilterSquare", "Square") }
                })
                {
                    var localAspect = aspect;
                    var aspectItem = new MenuItem
                    {
                        Header = localAspect.Label,
                        IsCheckable = true,
                        IsChecked = string.Equals(pickerCandidateFilter.Aspect, localAspect.Value, StringComparison.OrdinalIgnoreCase),
                        StaysOpenOnClick = true
                    };
                    aspectItem.Click += (sender, args) =>
                    {
                        pickerCandidateFilter.Aspect = localAspect.Value;
                        menu.IsOpen = false;
                        rebuildOptions();
                    };
                    aspectMenu.Items.Add(aspectItem);
                }
                menu.Items.Add(aspectMenu);

                var styles = availableOptions
                    .Where(x => !string.IsNullOrWhiteSpace(x.Style))
                    .Select(x => x.Style)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (styles.Count > 0)
                {
                    var stylesMenu = new MenuItem { Header = Loc("MTDA_MediaFilterStyles", "Style") };
                    foreach (var style in styles)
                    {
                        var localStyle = style;
                        var styleItem = new MenuItem
                        {
                            Header = GetMediaStyleLabel(localStyle),
                            IsCheckable = true,
                            IsChecked = pickerCandidateFilter.Styles.Contains(localStyle),
                            StaysOpenOnClick = true
                        };
                        styleItem.Click += (sender, args) =>
                        {
                            if (styleItem.IsChecked) pickerCandidateFilter.Styles.Add(localStyle);
                            else pickerCandidateFilter.Styles.Remove(localStyle);
                            rebuildOptions();
                        };
                        stylesMenu.Items.Add(styleItem);
                    }
                    menu.Items.Add(stylesMenu);
                }

                if (pickerCandidateFilter.ActiveCount > 0 || selectedPickerSources.Count != pickerSourceNames.Count)
                {
                    menu.Items.Add(new Separator());
                    var resetItem = new MenuItem { Header = Loc("MTDA_ResetMediaFilters", "Reset filters") };
                    resetItem.Click += (sender, args) =>
                    {
                        pickerCandidateFilter.OfficialOnly = false;
                        pickerCandidateFilter.HideScreenshots = false;
                        pickerCandidateFilter.Aspect = "all";
                        pickerCandidateFilter.Styles.Clear();
                        selectedPickerSources.Clear();
                        foreach (var source in pickerSourceNames) selectedPickerSources.Add(source);
                        menu.IsOpen = false;
                        rebuildOptions();
                    };
                    menu.Items.Add(resetItem);
                }
                menu.PlacementTarget = placementTarget ?? resultsHost;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
            };
            showPickerFilters = placementTarget =>
            {
                filterSidebar.Visibility = filterSidebar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                if (filterSidebar.Visibility != Visibility.Visible) return;
                var panel = new StackPanel();
                panel.Children.Add(new TextBlock { Text = Loc("MTDA_MediaFilterSources", "Sources"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
                foreach (var sourceName in pickerSourceNames)
                {
                    var name = sourceName;
                    var check = new CheckBox { Content = GetPickerSourceDisplayName(name), IsChecked = selectedPickerSources.Contains(name), Margin = new Thickness(0, 2, 0, 2) };
                    check.Checked += (s, e) => selectedPickerSources.Add(name);
                    check.Unchecked += (s, e) => selectedPickerSources.Remove(name);
                    panel.Children.Add(check);
                }
                panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
                var official = new CheckBox { Content = Loc("MTDA_MediaFilterOfficialOnly", "Official candidates only"), IsChecked = pickerCandidateFilter.OfficialOnly, Margin = new Thickness(0, 2, 0, 2) };
                var screenshots = new CheckBox { Content = Loc("MTDA_MediaFilterHideScreenshots", "Hide screenshots"), IsChecked = pickerCandidateFilter.HideScreenshots, Margin = new Thickness(0, 2, 0, 8) };
                panel.Children.Add(official); panel.Children.Add(screenshots);
                panel.Children.Add(new TextBlock { Text = Loc("MTDA_MediaFilterAspect", "Shape"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
                var aspectOptions = new[]
                {
                    new LocalizedOption("all", GetLocalizedOrFallback("MTDA_MediaFilterAllShapes", "Any shape")),
                    new LocalizedOption("landscape", GetLocalizedOrFallback("MTDA_MediaFilterLandscape", "Landscape")),
                    new LocalizedOption("portrait", GetLocalizedOrFallback("MTDA_MediaFilterPortrait", "Portrait")),
                    new LocalizedOption("square", GetLocalizedOrFallback("MTDA_MediaFilterSquare", "Square"))
                };
                var aspect = new ComboBox { ItemsSource = aspectOptions, DisplayMemberPath = "DisplayName", SelectedValuePath = "Value", SelectedValue = pickerCandidateFilter.Aspect };
                panel.Children.Add(aspect);

                if (formatOptions.Count > 0)
                {
                    panel.Children.Add(new TextBlock { Text = GetLocalizedOrFallback("MTDA_MediaSearchFormat", "Candidate resolution"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
                    formatCombo = new ComboBox
                    {
                        ItemsSource = formatOptions,
                        DisplayMemberPath = "DisplayName",
                        SelectedValuePath = "Value",
                        SelectedValue = GetMediaPickerFormatValue(pickerSettings, kind),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        ToolTip = GetLocalizedOrFallback("MTDA_MediaSearchFormatHelp", "Choose which candidate resolution to show. This does not change the saved global output format.")
                    };
                    panel.Children.Add(formatCombo);
                }

                var styles = availableOptions
                    .Where(x => !string.IsNullOrWhiteSpace(x.Style))
                    .Select(x => x.Style)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (styles.Count > 0)
                {
                    panel.Children.Add(new TextBlock { Text = GetLocalizedOrFallback("MTDA_MediaFilterStyles", "Style"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
                    foreach (var style in styles)
                    {
                        var localStyle = style;
                        var styleCheck = new CheckBox { Content = GetMediaStyleLabel(localStyle), IsChecked = pickerCandidateFilter.Styles.Contains(localStyle), Margin = new Thickness(0, 2, 0, 2) };
                        styleCheck.Checked += (s, e) => pickerCandidateFilter.Styles.Add(localStyle);
                        styleCheck.Unchecked += (s, e) => pickerCandidateFilter.Styles.Remove(localStyle);
                        panel.Children.Add(styleCheck);
                    }
                }
                var apply = new Button { Content = Loc("MTDA_ApplyMediaFilters", "Apply filters"), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
                apply.Click += (s, e) =>
                {
                    pickerCandidateFilter.OfficialOnly = official.IsChecked == true;
                    pickerCandidateFilter.HideScreenshots = screenshots.IsChecked == true;
                    pickerCandidateFilter.Aspect = aspect.SelectedValue as string ?? "all";
                    var resolutionValue = formatCombo == null ? null : formatCombo.SelectedValue as string;
                    if (!string.IsNullOrWhiteSpace(resolutionValue))
                    {
                        SetMediaPickerFormatValue(pickerSettings, kind, resolutionValue);
                    }
                    filterSidebar.Visibility = Visibility.Collapsed;
                    rebuildOptions();
                    if (runSearch != null) runSearch();
                };
                panel.Children.Add(apply);
                filterSidebar.Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            };
            rebuildOptions();
            Action<MediaPreviewSearchResult> applyResolutionFallback = result =>
            {
                if (result == null || !result.UsedResolutionFallback)
                {
                    return;
                }

                string fallbackValue;
                if (!TryGetMediaPickerFormatValueForResolution(kind, result.ResolvedWidth, result.ResolvedHeight, out fallbackValue))
                {
                    fallbackValue = GetAllResolutionsPickerValue(kind);
                }

                var requestedValue = GetMediaPickerFormatValue(pickerSettings, kind);
                if (string.Equals(requestedValue, fallbackValue, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                SetMediaPickerFormatValue(pickerSettings, kind, fallbackValue);
                if (formatCombo != null)
                {
                    formatCombo.SelectedValue = fallbackValue;
                }

                PlayniteApi.Dialogs.ShowMessage(BuildMediaResolutionFallbackMessage(kind, requestedValue, fallbackValue, pickerSettings), PluginTitle);
            };
            Action showManualUrlHelp = () => resultsHost.Content = CreateMediaMessagePanel(
                Loc("MTDA_ManualMediaUrlPrompt", "Preview the image URL to use it instead of the candidates from the configured sources."));
            Action<MediaPreviewOption> showManualOption = manualOption =>
            {
                if (clearSelection != null)
                {
                    clearSelection();
                }

                displayedManualOption = manualOption;
                rebuildOptions();
                if (selectAction != null)
                {
                    selectAction(manualOption);
                }
            };
            Action previewManualUrl = async () =>
            {
                var url = (manualUrlBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(url))
                {
                    restoreAvailableOptions();
                    return;
                }

                MediaPreviewOption manualOption = null;
                Exception manualError = null;
                previewUrlButton.IsEnabled = false;
                try
                {
                    using (var busy = new MediaPickerBusyOverlay(
                        this,
                        root,
                        Loc("MTDA_ProgressPreviewingManualMedia", "Validating image URL...")))
                    {
                        try
                        {
                            manualOption = await busy.RunAsync(token => new MediaGenerationService(pickerSettings, PlayniteApi)
                                .GetManualPreviewOptionAsync(kind, url, token)
                                .GetAwaiter()
                                .GetResult());
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (Exception ex)
                        {
                            manualError = ex;
                        }
                    }
                }
                finally
                {
                    previewUrlButton.IsEnabled = !string.IsNullOrWhiteSpace(manualUrlBox.Text);
                }

                if (manualError != null)
                {
                    resultsHost.Content = CreateMediaMessagePanel(UserError(manualError));
                    return;
                }

                if (manualOption == null)
                {
                    resultsHost.Content = CreateMediaMessagePanel(Loc("MTDA_ErrorManualMediaNotImage", "The URL could not be loaded as a valid image."));
                    return;
                }

                hasManualLocalFile = false;
                showManualOption(manualOption);
            };
            manualUrlBox.TextChanged += (sender, args) =>
            {
                var hasManualUrl = !string.IsNullOrWhiteSpace(manualUrlBox.Text);
                previewUrlButton.IsEnabled = hasManualUrl;
                clearUrlButton.IsEnabled = hasManualUrl || hasManualLocalFile;
                if (!hasManualUrl)
                {
                    if (clearSelection != null)
                    {
                        clearSelection();
                    }

                    restoreAvailableOptions();
                    return;
                }

                hasManualLocalFile = false;
                if (clearSelection != null)
                {
                    clearSelection();
                }

                showManualUrlHelp();
            };
            previewUrlButton.Click += (sender, args) => previewManualUrl();
            clearUrlButton.Click += (sender, args) =>
            {
                hasManualLocalFile = false;
                manualUrlBox.Clear();
                clearUrlButton.IsEnabled = false;
                if (clearSelection != null) clearSelection();
                restoreAvailableOptions();
            };
            chooseLocalFileButton.Click += async (sender, args) =>
            {
                var dialog = new OpenFileDialog
                {
                    Title = Loc("MTDA_ChooseManualMediaFile", "Choose image from device"),
                    Filter = "Image files|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif|All files|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };
                var owner = Window.GetWindow(chooseLocalFileButton);
                if (dialog.ShowDialog(owner) != true)
                {
                    return;
                }

                manualUrlBox.Clear();
                MediaPreviewOption localOption = null;
                Exception localError = null;
                chooseLocalFileButton.IsEnabled = false;
                try
                {
                    using (var busy = new MediaPickerBusyOverlay(
                        this,
                        root,
                        Loc("MTDA_ProgressPreviewingManualMedia", "Validating image URL...")))
                    {
                        try
                        {
                            localOption = await busy.RunAsync(token => new MediaGenerationService(pickerSettings, PlayniteApi)
                                .GetManualLocalFilePreviewOptionAsync(kind, dialog.FileName, token)
                                .GetAwaiter()
                                .GetResult());
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (Exception ex)
                        {
                            localError = ex;
                        }
                    }
                }
                finally
                {
                    chooseLocalFileButton.IsEnabled = true;
                }

                if (localError != null)
                {
                    resultsHost.Content = CreateMediaMessagePanel(UserError(localError));
                    return;
                }

                if (localOption == null)
                {
                    resultsHost.Content = CreateMediaMessagePanel(Loc("MTDA_ErrorManualMediaNotImage", "The URL could not be loaded as a valid image."));
                    return;
                }

                hasManualLocalFile = true;
                clearUrlButton.IsEnabled = true;
                showManualOption(localOption);
            };
            manualUrlBox.KeyDown += (sender, args) =>
            {
                if (args.Key == System.Windows.Input.Key.Enter)
                {
                    args.Handled = true;
                    previewManualUrl();
                }
            };

            runSearch = async () =>
            {
                var query = (searchBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(query))
                {
                    PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MediaSearchTermsRequired", "Enter at least one search term."), PluginTitle);
                    return;
                }

                MediaPreviewSearchResult refreshed = null;
                Exception searchError = null;
                var cancelled = false;
                searchButton.IsEnabled = false;
                try
                {
                    using (var busy = new MediaPickerBusyOverlay(
                        this,
                        root,
                        string.Format(
                            Loc("MTDA_ProgressSearchingMediaQuery", "Searching for {0} using ‘{1}’..."),
                            MediaKindName(kind).ToLowerInvariant(),
                            query)))
                    {
                        try
                        {
                            var sourceFilteredSettings = CreatePickerSourceFilteredSettings(pickerSettings, selectedPickerSources);
                            var searchService = new MediaGenerationService(sourceFilteredSettings, PlayniteApi);
                            refreshed = await busy.RunAsync(token =>
                                searchService.GetPreviewOptionsWithResolutionFallbackAsync(game, kind, query, token).GetAwaiter().GetResult());
                            if (selectedPickerSources.Contains("Web search"))
                            {
                                if (refreshed == null) refreshed = new MediaPreviewSearchResult();
                                var webCandidates = await busy.RunAsync(token => SearchWebImageCandidates(
                                    query,
                                    kind,
                                    pickerSettings,
                                    pickerCandidateFilter));
                                refreshed.Options.AddRange(webCandidates);
                                var webDiagnostic = string.Format(
                                    Loc("MTDA_WebSearchDiagnostics", "- Web search: {0} candidate(s) found"),
                                    webCandidates.Count);
                                var sourceDiagnostics = searchService.GetLastDiagnostics(game, kind, query);
                                diagnosticsByKind[kind] = string.IsNullOrWhiteSpace(sourceDiagnostics)
                                    ? webDiagnostic
                                    : sourceDiagnostics + Environment.NewLine + webDiagnostic;
                            }
                            else
                            {
                                diagnosticsByKind[kind] = searchService.GetLastDiagnostics(game, kind, query);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                        }
                        catch (Exception ex)
                        {
                            searchError = ex;
                            logger.Error(ex, "Failed to refresh media options.");
                        }
                    }
                }
                finally
                {
                    searchButton.IsEnabled = true;
                }

                if (cancelled)
                {
                    return;
                }

                if (searchError != null)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(UserError(searchError), PluginTitle);
                    return;
                }

                if (clearSelection != null)
                {
                    clearSelection();
                }

                availableOptions = refreshed == null ? new List<MediaPreviewOption>() : refreshed.Options ?? new List<MediaPreviewOption>();
                applyResolutionFallback(refreshed);
                if (string.IsNullOrWhiteSpace(manualUrlBox.Text))
                {
                    restoreAvailableOptions();
                }
                if (availableOptions.Count == 0)
                {
                    var diagnostics = diagnosticsByKind.ContainsKey(kind) ? diagnosticsByKind[kind] : string.Empty;
                    if (!string.IsNullOrWhiteSpace(diagnostics))
                    {
                        PlayniteApi.Dialogs.ShowMessage(
                            Loc("MTDA_MessageNoCandidatesForMediaType", "There are no candidates for this media type.") +
                            Environment.NewLine + Environment.NewLine + diagnostics,
                            PluginTitle);
                    }
                }
            };

            searchButton.Click += (sender, args) => runSearch();
            searchBox.KeyDown += (sender, args) =>
            {
                if (args.Key == System.Windows.Input.Key.Enter)
                {
                    args.Handled = true;
                    runSearch();
                }
            };

            manualUrlControl = manualUrlPanel;
            return root;
        }

        private UIElement CreateMediaOptionsPanel(
            List<MediaPreviewOption> options,
            Action<MediaPreviewOption> selectAction,
            MediaPreviewOption initiallySelected = null,
            MetaDataIASettings pickerSettings = null,
            Action refreshView = null,
            Action<FrameworkElement> showPickerFilters = null,
            string pickerFiltersLabel = null)
        {
            if (options == null || options.Count == 0)
            {
                return new TextBlock
                {
                    Text = Loc("MTDA_MessageNoCandidatesForMediaType", "There are no candidates for this media type."),
                    Margin = new Thickness(12),
                    TextWrapping = TextWrapping.Wrap
                };
            }

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalContentAlignment = VerticalAlignment.Top
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var listMode = pickerSettings != null && string.Equals(pickerSettings.MediaPickerViewMode, MetaDataIASettings.MediaPickerViewList, StringComparison.OrdinalIgnoreCase);
            if (pickerSettings != null)
            {
                var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var count = new TextBlock
                {
                    Text = string.Format(Loc("MTDA_MediaCandidateCount", "{0} candidates"), options.Count),
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.75
                };
                ApplyDynamicResource(count, TextBlock.ForegroundProperty, "TextBrush");
                count.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(count, 1);
                toolbar.Children.Add(count);
                var viewButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(viewButtons, 2);
                if (showPickerFilters != null)
                {
                    var filtersButton = new Button
                    {
                        Content = CreateMediaPickerIcon("filter"),
                        ToolTip = pickerFiltersLabel ?? Loc("MTDA_MediaFilters", "Filters"),
                        Width = 36,
                        Height = 36
                    };
                    filtersButton.Click += (sender, args) => showPickerFilters(filtersButton);
                    Grid.SetColumn(filtersButton, 0);
                    toolbar.Children.Add(filtersButton);
                }
                var gridButton = new Button { Content = CreateMediaPickerIcon("grid"), ToolTip = Loc("MTDA_MediaViewGrid", "Grid"), Width = 36, Height = 36, IsEnabled = listMode };
                var listButton = new Button { Content = CreateMediaPickerIcon("list"), ToolTip = Loc("MTDA_MediaViewList", "List"), Width = 36, Height = 36, Margin = new Thickness(6, 0, 0, 0), IsEnabled = !listMode };
                gridButton.Click += (sender, args) =>
                {
                    pickerSettings.MediaPickerViewMode = MetaDataIASettings.MediaPickerViewGrid;
                    if (settings != null && settings.Settings != null) settings.Settings.MediaPickerViewMode = MetaDataIASettings.MediaPickerViewGrid;
                    if (refreshView != null) refreshView();
                };
                listButton.Click += (sender, args) =>
                {
                    pickerSettings.MediaPickerViewMode = MetaDataIASettings.MediaPickerViewList;
                    if (settings != null && settings.Settings != null) settings.Settings.MediaPickerViewMode = MetaDataIASettings.MediaPickerViewList;
                    if (refreshView != null) refreshView();
                };
                viewButtons.Children.Add(gridButton);
                viewButtons.Children.Add(listButton);
                toolbar.Children.Add(viewButtons);
                root.Children.Add(toolbar);
            }
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            Panel panel;
            if (listMode)
            {
                panel = new StackPanel { Margin = new Thickness(0, 2, 0, 4), VerticalAlignment = VerticalAlignment.Top };
            }
            else
            {
                panel = new UniformGrid { Margin = new Thickness(0, 4, 0, 4), Columns = 1 };
            }
            scroll.Content = panel;
            scroll.SizeChanged += (sender, args) =>
            {
                if (listMode)
                {
                    return;
                }

                var kind = options.Select(x => x.Kind).FirstOrDefault();
                var minimumTileWidth = kind == MediaKind.Background ? 340 : 240;
                var availableWidth = Math.Max(1, args.NewSize.Width - 16);
                ((UniformGrid)panel).Columns = Math.Max(1, (int)Math.Floor(availableWidth / minimumTileWidth));
            };

            var optionBorders = new List<Border>();
            var selectionMarkers = new Dictionary<Border, TextBlock>();
            var visibleCount = Math.Min(24, options.Count);
            Action<MediaPreviewOption> addOption = option =>
            {
                Border optionBorder;
                TextBlock selectionMarker;
                panel.Children.Add(CreateMediaOptionTile(option, (selectedOption, selectedBorder) =>
                {
                    foreach (var border in optionBorders)
                    {
                        border.BorderThickness = new Thickness(1);
                        ApplyDynamicResource(border, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
                        TextBlock marker;
                        if (selectionMarkers.TryGetValue(border, out marker)) marker.Visibility = Visibility.Collapsed;
                    }

                    selectedBorder.BorderThickness = new Thickness(3);
                    ApplyDynamicResource(selectedBorder, Border.BorderBrushProperty, "HighlightGlyphBrush");
                    TextBlock selectedMarker;
                    if (selectionMarkers.TryGetValue(selectedBorder, out selectedMarker)) selectedMarker.Visibility = Visibility.Visible;

                    if (selectAction != null)
                    {
                        selectAction(selectedOption);
                    }
                }, out optionBorder, out selectionMarker, listMode));
                optionBorders.Add(optionBorder);
                selectionMarkers[optionBorder] = selectionMarker;

                if (initiallySelected != null &&
                    (ReferenceEquals(option, initiallySelected) ||
                     string.Equals(option.Url, initiallySelected.Url, StringComparison.OrdinalIgnoreCase)))
                {
                    optionBorder.BorderThickness = new Thickness(3);
                    ApplyDynamicResource(optionBorder, Border.BorderBrushProperty, "HighlightGlyphBrush");
                    selectionMarker.Visibility = Visibility.Visible;
                }
            };

            for (var index = 0; index < visibleCount; index++)
            {
                addOption(options[index]);
            }

            if (visibleCount < options.Count)
            {
                var loadMoreButton = new Button
                {
                    Content = string.Format(Loc("MTDA_LoadMoreMedia", "Load more ({0} remaining)"), options.Count - visibleCount),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(14, 9, 14, 9),
                    Margin = new Thickness(10, 8, 10, 2)
                };
                loadMoreButton.Click += (sender, args) =>
                {
                    var oldIndex = visibleCount;
                    visibleCount = Math.Min(visibleCount + 24, options.Count);
                    for (var index = oldIndex; index < visibleCount; index++)
                    {
                        addOption(options[index]);
                    }

                    if (visibleCount < options.Count)
                    {
                        loadMoreButton.Content = string.Format(Loc("MTDA_LoadMoreMedia", "Load more ({0} remaining)"), options.Count - visibleCount);
                    }
                    else
                    {
                        loadMoreButton.Visibility = Visibility.Collapsed;
                    }
                };
                Grid.SetRow(loadMoreButton, 2);
                root.Children.Add(loadMoreButton);
            }

            return root;
        }

        private UIElement CreateMediaMessagePanel(string message)
        {
            return new TextBlock
            {
                Text = message ?? string.Empty,
                Margin = new Thickness(12),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static UIElement CreateMediaPickerIcon(string kind)
        {
            var canvas = new Canvas { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false };
            if (kind == "grid" || kind == "list")
            {
                var points = kind == "grid"
                    ? new[] { new Point(3, 3), new Point(8, 3), new Point(13, 3), new Point(3, 8), new Point(8, 8), new Point(13, 8), new Point(3, 13), new Point(8, 13), new Point(13, 13) }
                    : new[] { new Point(3, 3), new Point(3, 8), new Point(3, 13) };
                foreach (var point in points)
                {
                    var dot = new Ellipse { Width = 2.6, Height = 2.6 };
                    ApplyDynamicResource(dot, Shape.FillProperty, "TextBrush");
                    Canvas.SetLeft(dot, point.X - 1.3); Canvas.SetTop(dot, point.Y - 1.3); canvas.Children.Add(dot);
                }
                if (kind == "list")
                {
                    foreach (var y in new[] { 3d, 8d, 13d })
                    {
                        var line = new Rectangle { Width = 9, Height = 1.7, RadiusX = 0.8, RadiusY = 0.8 };
                        ApplyDynamicResource(line, Shape.FillProperty, "TextBrush");
                        Canvas.SetLeft(line, 6); Canvas.SetTop(line, y - 0.85); canvas.Children.Add(line);
                    }
                }
            }
            else
            {
                foreach (var item in new[] { new { X = 1d, Y = 3d, W = 14d }, new { X = 3d, Y = 8d, W = 9d }, new { X = 6d, Y = 13d, W = 4d } })
                {
                    var line = new Rectangle { Width = item.W, Height = 1.7, RadiusX = 0.8, RadiusY = 0.8 };
                    ApplyDynamicResource(line, Shape.FillProperty, "TextBrush");
                    Canvas.SetLeft(line, item.X); Canvas.SetTop(line, item.Y - 0.85); canvas.Children.Add(line);
                }
            }
            return canvas;
        }

        private UIElement CreateMediaOptionTile(MediaPreviewOption option, Action<MediaPreviewOption, Border> selectAction, out Border optionBorder, out TextBlock selectionMarker, bool listMode)
        {
            var tileRoot = new Grid { MinWidth = listMode ? 0 : option.Kind == MediaKind.Background ? 300 : 210, Margin = new Thickness(0, 6, 6, 6), Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Top };
            var dottedBorder = new Rectangle { Stroke = new SolidColorBrush(Color.FromArgb(70, 150, 150, 150)), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 1, 3 }, RadiusX = 4, RadiusY = 4, IsHitTestVisible = false };
            tileRoot.Children.Add(dottedBorder);
            var border = new Border { Margin = new Thickness(1), Padding = new Thickness(8), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top };
            ApplyDynamicResource(border, Border.BackgroundProperty, "ControlBackgroundBrush");
            ApplyDynamicResource(border, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
            optionBorder = border;
            tileRoot.Children.Add(border);

            var content = new StackPanel();
            Grid listContent = null;
            if (listMode)
            {
                listContent = new Grid();
                listContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(option.Kind == MediaKind.Background ? 180 : 120) });
                listContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                border.Child = listContent;
            }
            else border.Child = content;

            var image = new Image { Height = listMode ? 92 : option.Kind == MediaKind.Cover ? 190 : option.Kind == MediaKind.Background ? 96 : 190, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch };
            try
            {
                if (option.IsAnimated && string.Equals(option.Extension, ".gif", StringComparison.OrdinalIgnoreCase))
                {
                    XamlAnimatedGif.AnimationBehavior.SetSourceUri(image, new Uri(option.Url, UriKind.Absolute));
                }
                else
                {
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = new Uri(option.Url, UriKind.Absolute); bitmap.DecodePixelWidth = option.Kind == MediaKind.Background ? 360 : 240; bitmap.CacheOption = BitmapCacheOption.OnDemand; bitmap.EndInit(); image.Source = bitmap;
                }
            }
            catch { }
            if (listMode) { Grid.SetColumn(image, 0); listContent.Children.Add(image); content.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(content, 1); listContent.Children.Add(content); }
            else content.Children.Add(image);

            var infoPanel = new StackPanel { Margin = new Thickness(0, listMode ? 0 : 8, 0, 8) };
            var sourceBadge = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(7, 2, 7, 2), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 5) };
            if (listMode) sourceBadge.HorizontalAlignment = HorizontalAlignment.Right;
            ApplyDynamicResource(sourceBadge, Border.BackgroundProperty, "ControlBackgroundBrush");
            ApplyDynamicResource(sourceBadge, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
            var sourceText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(option.SourceName) ? Loc("MTDA_UnknownSource", "Unknown source") : string.Equals(option.SourceName, MetaDataIASettings.SourceOriginIntegration, StringComparison.OrdinalIgnoreCase) ? Loc("MTDA_SourceOriginIntegration", "Origin library integration") : option.SourceName,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            ApplyDynamicResource(sourceText, TextBlock.ForegroundProperty, "TextBrush");
            sourceBadge.Child = sourceText;
            infoPanel.Children.Add(sourceBadge);
            infoPanel.Children.Add(CreateMediaInfoLine(Loc("MTDA_MediaInfoSize", "Size"), option.Width > 0 && option.Height > 0 ? option.Width + " x " + option.Height : Loc("MTDA_Unknown", "Unknown")));
            infoPanel.Children.Add(CreateMediaInfoLine(GetLocalizedOrFallback("MTDA_MediaInfoFormat", "Format"), string.IsNullOrWhiteSpace(option.Extension) ? Loc("MTDA_Unknown", "Unknown") : option.Extension.TrimStart('.').ToUpperInvariant()));
            infoPanel.Children.Add(CreateMediaInfoLine(Loc("MTDA_MediaInfoStyle", "Style"), GetMediaStyleLabel(option.Style)));
            if (option.IsAnimated)
            {
                infoPanel.Children.Add(CreateMediaInfoLine(Loc("MTDA_MediaInfoAnimation", "Animation"), Loc("MTDA_MediaAnimated", "Animated — kept in original format")));
            }
            infoPanel.Children.Add(CreateMediaInfoLine(Loc("MTDA_MediaInfoOfficial", "Official"), option.IsOfficial ? Loc("MTDA_Yes", "Yes") : Loc("MTDA_NoCommunity", "No / community")));
            content.Children.Add(infoPanel);
            var openButton = new Button { Content = Loc("MTDA_OpenInBrowser", "Open"), HorizontalAlignment = listMode ? HorizontalAlignment.Right : HorizontalAlignment.Stretch, MinWidth = listMode ? 150 : 0, Margin = new Thickness(0) };
            openButton.Click += (sender, args) => { args.Handled = true; OpenUrl(option.Url); };
            content.Children.Add(openButton);

            selectionMarker = new TextBlock { Text = "✓", FontSize = 16, FontWeight = FontWeights.Bold, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 7, 0), IsHitTestVisible = false };
            ApplyDynamicResource(selectionMarker, TextBlock.ForegroundProperty, "HighlightGlyphBrush");
            tileRoot.Children.Add(selectionMarker);
            tileRoot.MouseLeftButtonUp += (sender, args) => { if (!IsInsideButton(args.OriginalSource as DependencyObject) && selectAction != null) selectAction(option, border); };
            return tileRoot;
        }

        private static bool IsInsideButton(DependencyObject source)
        {
            var current = source;
            while (current != null)
            {
                if (current is Button)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        private static TextBlock CreateMediaInfoLine(string label, string value)
        {
            return new TextBlock
            {
                Text = label + ": " + value,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
        }

        private void ApplySelectedMediaOption(Game game, MetaDataIASettings activeSettings, MediaPreviewOption option)
        {
            ApplySelectedMediaOptions(game, activeSettings, new List<MediaPreviewOption> { option });
        }

        private void ApplySelectedMediaOptions(Game game, MetaDataIASettings activeSettings, List<MediaPreviewOption> options)
        {
            try
            {
                if (options == null || options.Count == 0)
                {
                    return;
                }

                if (options.Any(x => x != null && x.Kind == MediaKind.Logo) && !ExtraMetadataLogoService.IsInstalled(PlayniteApi))
                {
                    PlayniteApi.Dialogs.ShowMessage(
                        Loc("MTDA_LogoNeedsEml", "Install Extra Metadata Loader before using logo integration."),
                        PluginTitle);
                    return;
                }

                if (options.Any(x => x != null && x.IsAnimated))
                {
                    var confirmAnimated = PlayniteApi.Dialogs.ShowMessage(
                        Loc("MTDA_AnimatedMediaConfirm", "This animated GIF/WebP will be saved in its original form. Crop, resize and image-quality settings will not be applied so the animation is preserved."),
                        PluginTitle,
                        MessageBoxButton.YesNo);
                    if (confirmAnimated != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                Exception applyError = null;
                var appliedCount = 0;
                var appliedOptions = new List<MediaPreviewOption>();
                var historyOperation = history.BeginOperation(Loc("MTDA_HistoryCuratedMedia", "Apply selected media"));
                GameMetadataSnapshot before = null;
                PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                {
                    try
                    {
                        progress.MainDispatcher.Invoke(new Action(() => before = history.Capture(game, historyOperation, true)));
                        var service = new MediaGenerationService(activeSettings, PlayniteApi);
                        foreach (var option in options)
                        {
                            if (progress.CancelToken.IsCancellationRequested)
                            {
                                break;
                            }

                            if (maintenanceState.IsLocked(game.Id, option.Kind))
                            {
                                continue;
                            }

                            progress.Text = string.Format(Loc("MTDA_ProgressApplyingMediaKind", "Applying {0}..."), MediaKindName(option.Kind).ToLowerInvariant());
                            var media = service.GenerateFromOptionAsync(game, option, progress.CancelToken).GetAwaiter().GetResult();
                            progress.MainDispatcher.Invoke(new Action(() =>
                            {
                                if (option.Kind == MediaKind.Logo)
                                {
                                    ExtraMetadataLogoService.Apply(PlayniteApi, game, media);
                                }
                                else
                                {
                                    MediaGenerationService.ApplyMediaFile(PlayniteApi, game, media);
                                }
                            }));
                            appliedOptions.Add(option);
                            appliedCount++;
                        }

                        if (appliedCount > 0)
                        {
                            progress.MainDispatcher.Invoke(new Action(() =>
                            {
                                PlayniteApi.Database.Games.Update(game);
                            }));
                        }
                    }
                    catch (Exception ex)
                    {
                        applyError = ex;
                    }
                }, new GlobalProgressOptions(PluginTitle + " - " + Loc("MTDA_ApplyMedia", "Apply media"), true) { IsIndeterminate = true });

                if (applyError != null)
                {
                    throw applyError;
                }

                var after = history.Capture(game, historyOperation, false);
                var mediaProvenance = appliedOptions.Select(x => new MetadataFieldProvenance
                {
                    Field = x.Kind == MediaKind.Cover ? "cover" : x.Kind == MediaKind.Icon ? "icon" : x.Kind == MediaKind.Logo ? "logo" : "background",
                    Source = string.IsNullOrWhiteSpace(x.SourceName) ? Loc("MTDA_UnknownSource", "Unknown source") : x.SourceName,
                    Method = "downloaded-media",
                    Confidence = x.IsOfficial ? "high" : "medium",
                    Detail = x.Url
                }).ToList();
                history.AddGame(historyOperation, game, before, after, mediaProvenance);
                history.SaveOperation(historyOperation);

                PlayniteApi.Dialogs.ShowMessage(string.Format(Loc("MTDA_MessageAppliedMediaFiles", "Metadata AI applied {0} media file(s)."), appliedCount), PluginTitle);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to apply selected media.");
                PlayniteApi.Dialogs.ShowErrorMessage(UserError(ex), PluginTitle);
            }
        }

        private IEnumerable<MediaKind> GetEnabledMediaKinds(MetaDataIASettings activeSettings, bool includeLogo)
        {
            var service = new MediaGenerationService(activeSettings, PlayniteApi);
            foreach (var kind in new[] { MediaKind.Cover, MediaKind.Icon, MediaKind.Background })
            {
                if (service.ShouldGenerate(kind))
                {
                    yield return kind;
                }
            }

            // Logos are an opt-in Extra Metadata Loader integration.  Including
            // them here makes the bulk manual picker a single place to curate
            // every enabled media type, while keeping automatic media runs unchanged.
            if (includeLogo && activeSettings.EnableExtraMetadataLoaderLogos)
            {
                yield return MediaKind.Logo;
            }
        }

        private string MediaKindName(MediaKind kind)
        {
            if (kind == MediaKind.Cover)
            {
                return Loc("MTDA_Cover", "Cover");
            }

            if (kind == MediaKind.Icon)
            {
                return Loc("MTDA_Icon", "Icon");
            }

            if (kind == MediaKind.Logo)
            {
                return Loc("MTDA_Logo", "Logo");
            }

            return Loc("MTDA_Background", "Background");
        }

        private string BuildMediaDiagnosticsMessage(IEnumerable<MediaKind> kinds, Dictionary<MediaKind, string> diagnosticsByKind)
        {
            if (kinds == null || diagnosticsByKind == null || diagnosticsByKind.Count == 0)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var kind in kinds)
            {
                string diagnostics;
                if (!diagnosticsByKind.TryGetValue(kind, out diagnostics) || string.IsNullOrWhiteSpace(diagnostics))
                {
                    continue;
                }

                parts.Add(MediaKindName(kind) + ":" + Environment.NewLine + diagnostics);
            }

            if (parts.Count == 0)
            {
                return string.Empty;
            }

            return Loc("MTDA_MediaDiagnosticsTitle", "Source diagnostics:") + Environment.NewLine +
                   string.Join(Environment.NewLine + Environment.NewLine, parts);
        }

        private static void ApplyPlayniteWindowStyle(Window window)
        {
            ApplyDynamicResource(window, FrameworkElement.StyleProperty, "StandardWindowStyle");
            ApplyDynamicResource(window, Control.BackgroundProperty, "StandardWindowBackgroundBrush");
            ApplyDynamicResource(window, Control.ForegroundProperty, "TextBrush");
        }

        private static void ApplyDynamicResource(DependencyObject target, DependencyProperty property, string resourceKey)
        {
            var element = target as FrameworkElement;
            if (element == null || string.IsNullOrWhiteSpace(resourceKey))
            {
                return;
            }

            try
            {
                element.SetResourceReference(property, resourceKey);
            }
            catch
            {
            }
        }

        private void ApplySortingNames(List<Game> games)
        {
            if (games == null || games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MessageNoGamesSortingName", "There are no games to apply sorting names to."), PluginTitle);
                return;
            }

            var processed = 0;
            var updatedGameIds = new HashSet<Guid>();
            var failureReasons = new Dictionary<Guid, string>();
            var historyOperation = history.BeginOperation(Loc("MTDA_HistorySortingNames", "Apply sorting names"));
            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                var lookup = new SeriesOrderLookupService(settings.Settings);
                progress.ProgressMaxValue = games.Count;
                foreach (var game in games)
                {
                    if (progress.CancelToken.IsCancellationRequested)
                    {
                        failureReasons[game.Id] = Loc("MTDA_BatchCancelledBeforeGame", "Not processed because the operation was cancelled.");
                        continue;
                    }

                    progress.Text = Loc("MTDA_MenuSetSortingName", "Set sorting name") + ": " + game.Name;
                    var verified = lookup.ResolveAsync(game, progress.CancelToken).GetAwaiter().GetResult();
                    var sortingName = SortingNameService.Generate(PlayniteApi, game, verified != null && verified.HasOrder ? verified : null);
                    if (string.IsNullOrWhiteSpace(sortingName))
                    {
                        failureReasons[game.Id] = verified == null || string.IsNullOrWhiteSpace(verified.FailureReason)
                            ? Loc("MTDA_MessageSortingNameNotDetermined", "No reliable series order could be determined.")
                            : verified.FailureReason;
                        progress.CurrentProgressValue++;
                        continue;
                    }

                    var before = history.Capture(game, historyOperation, false);
                    game.SortingName = sortingName;
                    PlayniteApi.Database.Games.Update(game);
                    var after = history.Capture(game, historyOperation, false);
                    history.AddGame(historyOperation, game, before, after, new[]
                    {
                        new MetadataFieldProvenance
                        {
                            Field = "sortingName",
                            Source = verified != null && verified.HasOrder ? verified.Source : "Metadata AI local rule",
                            Method = verified != null && verified.HasOrder ? "catalog lookup" : "deterministic",
                            Confidence = "high",
                            Detail = verified != null && verified.HasOrder ? verified.Detail : "Generated locally from an explicit ordinal in the game title."
                        }
                    });
                    updatedGameIds.Add(game.Id);
                    processed++;
                    progress.CurrentProgressValue++;
                }
            }, new GlobalProgressOptions(PluginTitle, true) { IsIndeterminate = false });

            history.SaveOperation(historyOperation);

            var failedGames = BuildBatchFailures(games, updatedGameIds, failureReasons);
            if (failedGames.Count > 0)
            {
                ShowBatchErrors(
                    processed,
                    failedGames.Select(x => x.Reason).ToList(),
                    0,
                    failedGames,
                    () => ApplySortingNames(failedGames.Select(x => x.Game).Where(x => x != null).ToList()));
            }
            else
            {
                PlayniteApi.Dialogs.ShowMessage(string.Format(Loc("MTDA_MessageSortingNameUpdated", "Metadata AI updated sorting names for {0} game(s)."), processed), PluginTitle);
            }
        }

        private int ApplyEnabledMedia(MediaGenerationService service, Game game, GlobalProgressActionArgs progress, List<MetadataFieldProvenance> provenance)
        {
            var applied = 0;
            foreach (var kind in new[] { MediaKind.Cover, MediaKind.Icon, MediaKind.Background })
            {
                if (progress.CancelToken.IsCancellationRequested || maintenanceState.IsLocked(game.Id, kind) || !service.ShouldGenerate(kind) || !service.ShouldApply(game, kind))
                {
                    continue;
                }

                GeneratedMediaFile media;
                var applyMode = kind == MediaKind.Cover
                    ? settings.Settings.CoverImageApplyMode
                    : kind == MediaKind.Icon
                        ? settings.Settings.IconApplyMode
                        : settings.Settings.BackgroundImageApplyMode;
                if (settings.Settings.MediaRepairOnlyWhenBetter &&
                    !string.Equals(applyMode, MetaDataIASettings.ApplyOverwrite, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(kind == MediaKind.Cover ? game.CoverImage : kind == MediaKind.Icon ? game.Icon : game.BackgroundImage))
                {
                    var currentQuality = MediaQualityInspector.Inspect(PlayniteApi, game, kind, settings.Settings);
                    var proposed = service.GetRecommendedPreviewOptionAsync(game, kind, progress.CancelToken).GetAwaiter().GetResult();
                    if (!MediaQualityInspector.IsMateriallyBetter(currentQuality, proposed))
                    {
                        continue;
                    }
                    media = service.GenerateFromOptionAsync(game, proposed, progress.CancelToken).GetAwaiter().GetResult();
                }
                else
                {
                    media = service.GenerateAsync(game, kind, progress.CancelToken).GetAwaiter().GetResult();
                }
                if (media == null)
                {
                    continue;
                }

                if (provenance != null)
                {
                    provenance.Add(new MetadataFieldProvenance
                    {
                        Field = kind == MediaKind.Cover ? "cover" : kind == MediaKind.Icon ? "icon" : "background",
                        Source = string.IsNullOrWhiteSpace(media.SourceName) ? Loc("MTDA_UnknownSource", "Unknown source") : media.SourceName,
                        Method = "downloaded-media",
                        Confidence = "medium",
                        Detail = media.SourceUrl
                    });
                }

                progress.MainDispatcher.Invoke(new Action(() =>
                {
                    MediaGenerationService.ApplyMediaFile(PlayniteApi, game, media);
                }));
                applied++;
            }

            if (applied > 0)
            {
                progress.MainDispatcher.Invoke(new Action(() =>
                {
                    PlayniteApi.Database.Games.Update(game);
                }));
            }

            return applied;
        }

        private void ProcessNewGamesAfterLibraryUpdate()
        {
            var activeSettings = settings.Settings;
            activeSettings.EnsureDefaults();
            if (!activeSettings.AutoImportNewGames)
            {
                SeedKnownGamesIfNeeded();
                return;
            }

            var allGames = PlayniteApi.Database.Games.GetClone().ToList();
            if (activeSettings.AutoImportKnownGameIds == null || activeSettings.AutoImportKnownGameIds.Count == 0)
            {
                activeSettings.AutoImportKnownGameIds = allGames.Select(x => x.Id).ToList();
                SaveCurrentSettings();
                return;
            }

            var known = new HashSet<Guid>(activeSettings.AutoImportKnownGameIds);
            var newGames = allGames.Where(x => !known.Contains(x.Id)).ToList();
            activeSettings.AutoImportKnownGameIds = allGames.Select(x => x.Id).ToList();
            SaveCurrentSettings();

            if (newGames.Count == 0)
            {
                return;
            }

            if (activeSettings.AutoImportGenerateMetadata && activeSettings.IsConfigured)
            {
                GenerateAndApply(newGames, activeSettings, true);
            }

            if (activeSettings.AutoImportGenerateMedia && activeSettings.IsMediaConfigured)
            {
                ApplyMedia(newGames, activeSettings, true);
            }
        }

        private void SeedKnownGamesIfNeeded()
        {
            var activeSettings = settings.Settings;
            activeSettings.EnsureDefaults();
            if (activeSettings.AutoImportKnownGameIds != null && activeSettings.AutoImportKnownGameIds.Count > 0)
            {
                return;
            }

            activeSettings.AutoImportKnownGameIds = PlayniteApi.Database.Games.GetClone().Select(x => x.Id).ToList();
            SaveCurrentSettings();
        }

        private void SaveCurrentSettings()
        {
            settings.SyncSelectedTemplate();
            SaveSettingsSecurely(settings.Settings);
        }

        public void SaveSettingsSecurely(MetaDataIASettings currentSettings)
        {
            if (currentSettings == null)
            {
                return;
            }

            var storedSettings = Serialization.GetClone(currentSettings);
            storedSettings.ProtectSecretsForStorage();
            SavePluginSettings(storedSettings);
        }

        private string UserError(Exception ex)
        {
            if (ex == null)
            {
                return Loc("MTDA_ErrorUnspecified", "An unspecified error occurred.");
            }

            return MetadataGenerationService.SanitizeForUser(ex.Message);
        }

        private List<BatchFailedGame> BuildBatchFailures(
            IEnumerable<Game> games,
            ISet<Guid> updatedGameIds,
            IDictionary<Guid, string> failureReasons)
        {
            return (games ?? Enumerable.Empty<Game>())
                .Where(x => x != null && (updatedGameIds == null || !updatedGameIds.Contains(x.Id)))
                .Select(x => new BatchFailedGame
                {
                    Game = x,
                    Reason = failureReasons != null && failureReasons.ContainsKey(x.Id)
                        ? failureReasons[x.Id]
                        : Loc("MTDA_BatchNotProcessedAfterStop", "Not processed because the batch stopped after the previous error.")
                })
                .ToList();
        }

        private void ShowBatchErrors(
            int processed,
            List<string> errors,
            int qualitySkipped = 0,
            IEnumerable<BatchFailedGame> notUpdatedGames = null,
            Action retryAction = null)
        {
            var failedGames = (notUpdatedGames ?? Enumerable.Empty<BatchFailedGame>()).ToList();
            var message = string.Format(
                Loc("MTDA_MessageBatchErrorsHeader", "Metadata AI updated {0} game(s). Errors: {1}"),
                processed,
                errors == null ? 0 : errors.Count);
            message = AppendQualitySkipSummary(message, qualitySkipped);

            var exportText = new StringBuilder();
            exportText.AppendLine(message);
            exportText.AppendLine();
            exportText.AppendLine(string.Format(Loc("MTDA_MessageGamesNotUpdated", "Games not updated ({0}):"), failedGames.Count));
            foreach (var failedGame in failedGames)
            {
                exportText.AppendLine(failedGame.GameName + "\t" + failedGame.Reason);
            }

            if (IsFullscreenMode)
            {
                PlayniteApi.Dialogs.ShowMessage(exportText.ToString().Trim(), PluginTitle);
                return;
            }

            var window = new Window
            {
                Title = PluginTitle,
                Width = 980,
                Height = 700,
                MinWidth = 720,
                MinHeight = 460,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false
            };
            ApplyPlayniteWindowStyle(window);

            var owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
            if (owner != null)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            var root = new Grid { Margin = new Thickness(16) };
            ApplyDynamicResource(root, Panel.BackgroundProperty, "StandardWindowBackgroundBrush");
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var summaryText = new TextBlock
            {
                Text = message + Environment.NewLine + string.Format(
                    Loc("MTDA_MessageGamesNotUpdated", "Games not updated ({0}):"),
                    failedGames.Count),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 14
            };
            ApplyDynamicResource(summaryText, TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetRow(summaryText, 0);
            root.Children.Add(summaryText);

            var list = new ListView
            {
                ItemsSource = failedGames,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            var reasonText = new FrameworkElementFactory(typeof(TextBlock));
            reasonText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Reason"));
            reasonText.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("Reason"));
            reasonText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            reasonText.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 4, 6, 4));
            reasonText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var reasonTemplate = new DataTemplate { VisualTree = reasonText };

            list.View = new GridView
            {
                Columns =
                {
                    new GridViewColumn
                    {
                        Header = Loc("MTDA_Game", "Game"),
                        DisplayMemberBinding = new System.Windows.Data.Binding("GameName"),
                        Width = 260
                    },
                    new GridViewColumn
                    {
                        Header = Loc("MTDA_BatchFailureReason", "Reason"),
                        CellTemplate = reasonTemplate,
                        Width = 620
                    }
                }
            };
            Grid.SetRow(list, 1);
            root.Children.Add(list);

            var retryRequested = false;
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            if (retryAction != null && failedGames.Count > 0)
            {
                var retryButton = new Button { Content = Loc("MTDA_RetryPending", "Retry pending"), MinWidth = 130, Margin = new Thickness(0, 0, 8, 0) };
                retryButton.Click += (sender, args) => { retryRequested = true; window.Close(); };
                buttons.Children.Add(retryButton);
            }

            var copyButton = new Button { Content = Loc("MTDA_CopyList", "Copy list"), MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
            copyButton.Click += (sender, args) => Clipboard.SetText(exportText.ToString().Trim());
            buttons.Children.Add(copyButton);

            var exportButton = new Button { Content = Loc("MTDA_ExportList", "Export list"), MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
            exportButton.Click += (sender, args) =>
            {
                var dialog = new SaveFileDialog
                {
                    Title = Loc("MTDA_ExportPendingGames", "Export games not updated"),
                    Filter = "Text (*.txt)|*.txt",
                    FileName = "metadata-ai-pending-games.txt",
                    AddExtension = true
                };
                if (dialog.ShowDialog(window) == true)
                {
                    File.WriteAllText(dialog.FileName, exportText.ToString(), Encoding.UTF8);
                }
            };
            buttons.Children.Add(exportButton);

            var okButton = new Button
            {
                Content = Loc("MTDA_Close", "Close"),
                MinWidth = 100,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            okButton.Click += (sender, args) => window.Close();
            buttons.Children.Add(okButton);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            window.Content = root;
            window.ShowDialog();
            if (retryRequested)
            {
                retryAction();
            }
        }

        private string AppendQualitySkipSummary(string message, int qualitySkipped)
        {
            if (qualitySkipped <= 0)
            {
                return message;
            }

            return message + Environment.NewLine + Environment.NewLine +
                   string.Format(Loc("MTDA_MessageMediaStrictQualitySkipped", "Skipped because the source image was below the configured output resolution: {0}."), qualitySkipped);
        }

        private void ShowReviewWindow(Game game, AiMetadataResult result, MetaDataIASettings activeSettings)
        {
            logger.Info("Metadata AI review window opening for " + game.Name);

            var window = new Window();
            window.Title = "Metadata AI - " + game.Name;
            window.Content = CreateReviewContent(window, result);

            var owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
            if (owner != null)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            window.Width = 920;
            window.Height = 720;
            window.MinWidth = 760;
            window.MinHeight = 560;
            window.ResizeMode = ResizeMode.CanResize;
            window.ShowInTaskbar = false;

            if (window.ShowDialog() == true)
            {
                logger.Info("Metadata AI review accepted for " + game.Name);
                MetadataApplyService.Apply(PlayniteApi, game, result, activeSettings);
                LearnVocabulary(activeSettings, result);
            }
        }

        private void OpenNativeEditReview(Game game, AiMetadataResult result, MetaDataIASettings activeSettings)
        {
            logger.Info("Metadata AI native edit review opening for " + game.Name);

            var original = Serialization.GetClone(game);
            var reviewSettings = CreateNativeReviewSettings(activeSettings);
            var historyOperation = history.BeginOperation(Loc("MTDA_HistoryReviewedMetadata", "Reviewed AI metadata"));
            var before = history.Capture(game, historyOperation, false);

            try
            {
                MetadataApplyService.Apply(PlayniteApi, game, result, reviewSettings);
                var accepted = PlayniteApi.MainView.OpenEditDialog(game.Id);
                if (accepted != true)
                {
                    logger.Info("Metadata AI native edit review cancelled for " + game.Name);
                    PlayniteApi.Database.Games.Update(original);
                }
                else
                {
                    logger.Info("Metadata AI native edit review accepted for " + game.Name);
                    var after = history.Capture(game, historyOperation, false);
                    history.AddGame(historyOperation, game, before, after, result.Provenance);
                    history.SaveOperation(historyOperation);
                    LearnVocabulary(reviewSettings, result);
                }
            }
            catch
            {
                PlayniteApi.Database.Games.Update(original);
                throw;
            }
        }

        private static MetaDataIASettings CreateNativeReviewSettings(MetaDataIASettings activeSettings)
        {
            var reviewSettings = Serialization.GetClone(activeSettings);
            return reviewSettings;
        }

        private UIElement CreateReviewContent(Window window, AiMetadataResult result)
        {
            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tabs = new TabControl();
            Grid.SetRow(tabs, 0);
            root.Children.Add(tabs);

            var descriptionBox = CreateMultilineBox(result.Description, true);
            tabs.Items.Add(CreateTab("Descripcion", descriptionBox));

            var listsPanel = new Grid { Margin = new Thickness(8) };
            listsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            listsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            listsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            listsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            listsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            listsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var genresBox = AddField(listsPanel, "Generos", result.Genres, 0, 0);
            var tagsBox = AddField(listsPanel, "Etiquetas", result.Tags, 0, 1);
            var featuresBox = AddField(listsPanel, "Caracteristicas", result.Features, 1, 0);
            var categoriesBox = AddField(listsPanel, "Categorias", result.Categories, 1, 1);
            var developersBox = AddField(listsPanel, "Desarrolladores", result.Developers, 2, 0);
            var publishersBox = AddField(listsPanel, "Editores", result.Publishers, 2, 1);
            var ageRatingsBox = AddField(listsPanel, "Calificacion por edad", result.AgeRatings, 3, 0);
            var regionsBox = AddField(listsPanel, "Region", result.Regions, 3, 1);
            tabs.Items.Add(CreateTab("Listados", listsPanel));

            var linksPanel = new Grid { Margin = new Thickness(8) };
            linksPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            linksPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var linksBox = AddField(linksPanel, "Enlaces (Nombre | URL)", result.Links.Select(x => x.Name + " | " + x.Url), 0, 0);
            tabs.Items.Add(CreateTab("Enlaces", linksPanel));

            var detailPanel = new Grid { Margin = new Thickness(8) };
            detailPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            detailPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            detailPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            detailPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            detailPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            detailPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            detailPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var shortBox = AddField(detailPanel, "Descripcion breve", result.Short, 0, 0);
            var synopsisBox = AddField(detailPanel, "Sinopsis", result.Synopsis, 0, 1);
            var premiseBox = AddField(detailPanel, "Premisa", result.Premise, 1, 0);
            var gameplayBox = AddField(detailPanel, "Jugabilidad", result.Gameplay, 1, 1);
            var toneBox = AddField(detailPanel, "Tono", result.Tone, 2, 0);
            var settingBox = AddField(detailPanel, "Ambientacion", result.Setting, 2, 1);
            var perspectiveBox = AddField(detailPanel, "Perspectiva", result.Perspective, 3, 0);
            var playModesBox = AddField(detailPanel, "Modos de juego", result.PlayModes, 3, 1);
            var estimatedLengthBox = AddField(detailPanel, "Duracion estimada", result.EstimatedLength, 4, 0);
            var recommendedForBox = AddField(detailPanel, "Recomendado para", result.RecommendedFor, 4, 1);
            tabs.Items.Add(CreateTab("Detalle IA", detailPanel));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);

            var cancelButton = new Button { Content = "Cancelar", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
            cancelButton.Click += (sender, args) =>
            {
                window.DialogResult = false;
                window.Close();
            };
            buttons.Children.Add(cancelButton);

            var applyButton = new Button { Content = "Aplicar", MinWidth = 90 };
            applyButton.Click += (sender, args) =>
            {
                result.Description = descriptionBox.Text;
                result.Genres = SplitList(genresBox.Text);
                result.Tags = SplitList(tagsBox.Text);
                result.Features = SplitList(featuresBox.Text);
                result.Categories = SplitList(categoriesBox.Text);
                result.Developers = SplitList(developersBox.Text);
                result.Publishers = SplitList(publishersBox.Text);
                result.AgeRatings = SplitList(ageRatingsBox.Text);
                result.Regions = SplitList(regionsBox.Text);
                result.Links = SplitLinks(linksBox.Text);
                result.Short = shortBox.Text;
                result.Synopsis = synopsisBox.Text;
                result.Premise = premiseBox.Text;
                result.Gameplay = gameplayBox.Text;
                result.Tone = toneBox.Text;
                result.Setting = settingBox.Text;
                result.Perspective = perspectiveBox.Text;
                result.PlayModes = playModesBox.Text;
                result.EstimatedLength = estimatedLengthBox.Text;
                result.RecommendedFor = recommendedForBox.Text;
                window.DialogResult = true;
                window.Close();
            };
            buttons.Children.Add(applyButton);

            return root;
        }

        private static TabItem CreateTab(string header, UIElement content)
        {
            return new TabItem
            {
                Header = header,
                Content = content
            };
        }

        private static TextBox AddField(Grid grid, string label, IEnumerable<string> values, int row, int column)
        {
            return AddField(grid, label, JoinLines(values), row, column);
        }

        private static TextBox AddField(Grid grid, string label, string value, int row, int column)
        {
            var panel = new DockPanel { Margin = new Thickness(0, 0, 8, 8) };
            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);

            var header = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);

            var box = CreateMultilineBox(value, false);
            panel.Children.Add(box);
            return box;
        }

        private static TextBox CreateMultilineBox(string value, bool large)
        {
            return new TextBox
            {
                Text = value ?? string.Empty,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MinHeight = large ? 420 : 80,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                FontFamily = new FontFamily("Segoe UI")
            };
        }

        private static string JoinLines(IEnumerable<string> values)
        {
            return values == null ? string.Empty : string.Join("\n", values.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static List<string> SplitList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<string>();
            }

            return value
                .Replace("\r", string.Empty)
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<AiMetadataLink> SplitLinks(string value)
        {
            return SplitList(value)
                .Select(line =>
                {
                    var parts = line.Split(new[] { '|' }, 2);
                    return parts.Length == 2
                        ? new AiMetadataLink(parts[0].Trim(), parts[1].Trim())
                        : new AiMetadataLink(string.Empty, line.Trim());
                })
                .ToList();
        }

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = Application.Current == null ? null : Application.Current.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.Invoke(action);
        }

        private bool EnsureConfigured()
        {
            if (settings.Settings.IsConfigured)
            {
                return true;
            }

            PlayniteApi.Dialogs.ShowErrorMessage(Loc("MTDA_ErrorConfigureBeforeGenerate", "Configure the endpoint, model and API key for Metadata AI before generating metadata."), PluginTitle);
            OpenSettingsView();
            return false;
        }

        private MainMenuItem CreateMainMenuItem(string description, Func<MetaDataIASettings, MetaDataIASettings> settingsFactory)
        {
            return new MainMenuItem
            {
                Description = Loc(MenuKey(description), description),
                MenuSection = MenuRoot + "|" + Loc("MTDA_TabFields", "Campos"),
                Action = actionArgs => GenerateAndApply(GetSelectedOrFilteredGames(), settingsFactory(settings.Settings))
            };
        }

        private MainMenuItem CreateMainMediaMenuItem(
            string description,
            Func<MetaDataIASettings, MetaDataIASettings> settingsFactory,
            bool includeLogo = false)
        {
            return new MainMenuItem
            {
                Description = Loc(MenuKey(description), description),
                MenuSection = MenuRoot + "|" + Loc("MTDA_TabMedia", "Media"),
                Action = actionArgs => ApplyMediaForCurrentMode(
                    GetSelectedOrFilteredGames(),
                    settingsFactory(settings.Settings),
                    includeLogo)
            };
        }

        private MainMenuItem CreateMainSortingMenuItem(string description)
        {
            return new MainMenuItem
            {
                Description = Loc(MenuKey(description), description),
                MenuSection = MenuRoot + "|" + Loc("MTDA_TabFields", "Campos"),
                Action = actionArgs => ApplySortingNames(GetSelectedOrFilteredGames())
            };
        }

        private MainMenuItem CreateMainSubmenuSeparator(string sectionKey, string sectionFallback)
        {
            return new MainMenuItem
            {
                Description = "-",
                MenuSection = MenuRoot + "|" + Loc(sectionKey, sectionFallback)
            };
        }

        private MainMenuItem CreateMainRootSeparator()
        {
            return new MainMenuItem { Description = "-", MenuSection = MenuRoot };
        }

        private GameMenuItem CreateGameMenuItem(string description, Func<MetaDataIASettings, MetaDataIASettings> settingsFactory)
        {
            return new GameMenuItem
            {
                Description = Loc(MenuKey(description), description),
                MenuSection = MenuRoot + "|" + Loc("MTDA_TabFields", "Campos"),
                Action = actionArgs => GenerateAndApply(actionArgs.Games, settingsFactory(settings.Settings))
            };
        }

        private GameMenuItem CreateGameSubmenuSeparator(string sectionKey, string sectionFallback)
        {
            return new GameMenuItem
            {
                Description = "-",
                MenuSection = MenuRoot + "|" + Loc(sectionKey, sectionFallback)
            };
        }

        private GameMenuItem CreateGameRootSeparator()
        {
            return new GameMenuItem { Description = "-", MenuSection = MenuRoot };
        }

        private GameMenuItem CreateGameMediaMenuItem(
            string description,
            Func<MetaDataIASettings, MetaDataIASettings> settingsFactory,
            bool includeLogo = false)
        {
            return new GameMenuItem
            {
                Description = Loc(MenuKey(description), description),
                MenuSection = MenuRoot + "|" + Loc("MTDA_TabMedia", "Media"),
                Action = actionArgs => ApplyMediaForCurrentMode(
                    actionArgs.Games,
                    settingsFactory(settings.Settings),
                    includeLogo)
            };
        }

        private void OpenGameMediaFolder(Game game)
        {
            if (game == null || PlayniteApi == null || PlayniteApi.Database == null)
            {
                return;
            }

            try
            {
                var references = new[] { game.CoverImage, game.Icon, game.BackgroundImage }
                    .Where(x => !string.IsNullOrWhiteSpace(x));
                string folder = null;
                foreach (var reference in references)
                {
                    try
                    {
                        var fullPath = PlayniteApi.Database.GetFullFilePath(reference);
                        if (!string.IsNullOrWhiteSpace(fullPath))
                        {
                            folder = System.IO.Path.GetDirectoryName(fullPath);
                            if (!string.IsNullOrWhiteSpace(folder)) break;
                        }
                    }
                    catch
                    {
                    }
                }

                if (string.IsNullOrWhiteSpace(folder))
                {
                    folder = System.IO.Path.Combine(PlayniteApi.Database.DatabasePath, "files", game.Id.ToString());
                }

                Directory.CreateDirectory(folder);
                Process.Start("explorer.exe", folder);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Could not open the game media folder.");
                PlayniteApi.Dialogs.ShowErrorMessage(Loc("MTDA_OpenGameMediaFolderFailed", "The game media folder could not be opened."), PluginTitle);
            }
        }

        private void ClearGameMedia(Game game)
        {
            if (game == null || PlayniteApi == null || PlayniteApi.Database == null)
            {
                return;
            }

            var media = new[] { game.CoverImage, game.Icon, game.BackgroundImage }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (media.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_ClearGameMediaEmpty", "This game has no cover, icon or background to remove."), PluginTitle);
                return;
            }

            var confirm = PlayniteApi.Dialogs.ShowMessage(
                string.Format(Loc("MTDA_ClearGameMediaConfirm", "Remove the cover, icon and background assigned to ‘{0}’? Files managed by Playnite will also be removed when they are no longer used by another game."), game.Name),
                PluginTitle,
                MessageBoxButton.YesNo);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var oldMedia = media.ToList();
            game.CoverImage = null;
            game.Icon = null;
            game.BackgroundImage = null;
            PlayniteApi.Database.Games.Update(game);
            foreach (var reference in oldMedia)
            {
                MediaStorageCleanupService.TryRemoveUnreferencedMedia(PlayniteApi, reference);
            }

            PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_ClearGameMediaDone", "The game media was removed."), PluginTitle);
        }

        private bool IsFullscreenMode
        {
            get
            {
                return PlayniteApi != null &&
                       PlayniteApi.ApplicationInfo != null &&
                       PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen;
            }
        }

        public void ShowLibraryAudit(IEnumerable<Game> games = null)
        {
            if (IsFullscreenMode)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_AuditDesktopOnly", "The library audit is available in Desktop mode."), PluginTitle);
                return;
            }

            List<LibraryAuditIssue> issues = null;
            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                progress.Text = Loc("MTDA_AuditScanning", "Scanning metadata and media files...");
                var target = (games ?? PlayniteApi.Database.Games).ToList();
                issues = new LibraryAuditService(PlayniteApi, settings.Settings, maintenanceState).Scan(target);
            }, new GlobalProgressOptions(PluginTitle + " - " + Loc("MTDA_AuditTitle", "Library audit"), false) { IsIndeterminate = true });

            var window = new LibraryAuditWindow(
                this,
                issues ?? new List<LibraryAuditIssue>(),
                RepairAuditIssue,
                game => new LibraryAuditService(PlayniteApi, settings.Settings, maintenanceState).Scan(new[] { game }));
            var owner = window.Owner ?? PlayniteApi.Dialogs.GetCurrentAppWindow();
            try { window.ShowDialog(); } finally { RestoreWindowActivation(owner); }
        }

        private bool RepairAuditIssue(LibraryAuditIssue issue)
        {
            if (issue == null || !issue.IsRepairable || issue.Game == null)
            {
                ShowAuditNotice(Loc("MTDA_AuditNotRepairable", "This issue is informational and requires review."));
                return false;
            }

            if (issue.MediaKind.HasValue)
            {
                if (maintenanceState.IsLocked(issue.Game.Id, issue.MediaKind.Value))
                {
                    ShowAuditNotice(Loc("MTDA_AuditLocked", "This media is protected. Allow Metadata AI to replace it from the game's context menu before repairing it."));
                    return false;
                }

                var mediaFocus = issue.MediaKind.Value == MediaKind.Cover ? "cover" : issue.MediaKind.Value == MediaKind.Icon ? "icon" : "background";
                var before = issue.MediaKind.Value == MediaKind.Cover ? issue.Game.CoverImage : issue.MediaKind.Value == MediaKind.Icon ? issue.Game.Icon : issue.Game.BackgroundImage;
                ApplyMediaForCurrentMode(new List<Game> { issue.Game }, CreateFocusedMediaSettings(settings.Settings, mediaFocus));
                var after = issue.MediaKind.Value == MediaKind.Cover ? issue.Game.CoverImage : issue.MediaKind.Value == MediaKind.Icon ? issue.Game.Icon : issue.Game.BackgroundImage;
                return !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
            }

            var focus = AuditFieldFocus(issue.Field);
            if (string.IsNullOrWhiteSpace(focus))
            {
                ShowAuditNotice(Loc("MTDA_AuditNotRepairable", "This issue requires manual review."));
                return false;
            }

            if (focus == "sortingName")
            {
                if (!ApplyAuditSortingName(issue.Game))
                {
                    ShowAuditNotice(Loc("MTDA_AuditNoReliableValue", "No reliable value could be determined for this field. The issue will remain in the audit."));
                    return false;
                }
                return true;
            }

            if (focus == "series")
            {
                var inferredSeries = SortingNameService.GenerateSeriesName(PlayniteApi, issue.Game);
                if (!string.IsNullOrWhiteSpace(inferredSeries) && ApplyAuditSeries(issue.Game, inferredSeries))
                {
                    return true;
                }
            }

            var focused = CreateFocusedSettings(focus);
            if (issue.Problem == "duplicate") SetFocusedApplyMode(focused, focus, MetaDataIASettings.ApplyOverwrite);
            return RepairGeneratedAuditField(issue, focused);
        }

        private bool RepairGeneratedAuditField(LibraryAuditIssue issue, MetaDataIASettings focusedSettings)
        {
            if (issue == null || issue.Game == null || focusedSettings == null) return false;
            if (!settings.Settings.IsConfigured)
            {
                ShowAuditNotice(Loc("MTDA_ErrorConfigureBeforeGenerate", "Configure the endpoint, model and API key for Metadata AI before generating metadata."));
                return false;
            }

            var operation = history.BeginOperation(Loc("MTDA_HistoryApplyMetadata", "Apply AI metadata"));
            var dispatcher = Application.Current == null ? null : Application.Current.Dispatcher;
            Action<System.Threading.CancellationToken> generateAndApply = cancelToken =>
            {
                var result = new MetadataGenerationService(focusedSettings, PlayniteApi)
                    .GenerateAsync(issue.Game, cancelToken)
                    .GetAwaiter()
                    .GetResult();

                Action apply = () =>
                {
                    var before = history.Capture(issue.Game, operation, false);
                    MetadataApplyService.Apply(PlayniteApi, issue.Game, result, focusedSettings);
                    var after = history.Capture(issue.Game, operation, false);
                    history.AddGame(operation, issue.Game, before, after, result.Provenance);
                    LearnVocabulary(focusedSettings, result);
                };
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    apply();
                }
                else
                {
                    dispatcher.Invoke(apply);
                }
            };

            Exception generationError = null;
            var auditOwner = FindAuditWindow();
            if (auditOwner != null)
            {
                var progressWindow = new MetadataAuditProgressWindow(
                    this,
                    auditOwner,
                    Loc("MTDA_ProgressGeneratingMetadataGame", "Generating AI metadata: ") + issue.Game.Name,
                    generateAndApply);
                try { progressWindow.ShowDialog(); } finally { RestoreWindowActivation(auditOwner); }
                if (progressWindow.Cancelled) return false;
                generationError = progressWindow.Error;
            }
            else
            {
                PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                {
                    progress.Text = Loc("MTDA_ProgressGeneratingMetadataGame", "Generating AI metadata: ") + issue.Game.Name;
                    try { generateAndApply(progress.CancelToken); }
                    catch (Exception ex) { generationError = ex; }
                }, new GlobalProgressOptions(PluginTitle, true) { IsIndeterminate = true });
            }

            if (generationError != null)
            {
                logger.Error(generationError, "Failed to repair audited field " + issue.Field + " for " + issue.Game.Name);
                ShowAuditNotice(UserError(generationError));
                return false;
            }

            history.SaveOperation(operation);
            var unresolved = new LibraryAuditService(PlayniteApi, settings.Settings, maintenanceState)
                .Scan(new[] { issue.Game })
                .Any(x => string.Equals(x.Area, issue.Area, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(x.Field, issue.Field, StringComparison.OrdinalIgnoreCase));
            if (unresolved)
            {
                ShowAuditNotice(Loc("MTDA_AuditNoReliableValue", "No reliable value could be determined for this field. The issue will remain in the audit."));
                return false;
            }
            return true;
        }

        private void ShowAuditNotice(string message)
        {
            var auditOwner = FindAuditWindow();
            if (auditOwner == null)
            {
                PlayniteApi.Dialogs.ShowMessage(message, PluginTitle);
                return;
            }

            var notice = new MetadataNoticeWindow(this, auditOwner, message);
            try { notice.ShowDialog(); } finally { RestoreWindowActivation(auditOwner); }
        }

        private static LibraryAuditWindow FindAuditWindow()
        {
            return Application.Current == null
                ? null
                : Application.Current.Windows.OfType<LibraryAuditWindow>().FirstOrDefault(x => x.IsActive)
                  ?? Application.Current.Windows.OfType<LibraryAuditWindow>().FirstOrDefault(x => x.IsVisible);
        }

        private bool ApplyAuditSortingName(Game game)
        {
            if (game == null) return false;
            var verified = new SeriesOrderLookupService(settings.Settings)
                .ResolveAsync(game, System.Threading.CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var sortingName = SortingNameService.Generate(PlayniteApi, game, verified != null && verified.HasOrder ? verified : null);
            if (string.IsNullOrWhiteSpace(sortingName)) return false;

            var operation = history.BeginOperation(Loc("MTDA_HistorySortingNames", "Apply sorting names"));
            var before = history.Capture(game, operation, false);
            game.SortingName = sortingName;
            PlayniteApi.Database.Games.Update(game);
            var after = history.Capture(game, operation, false);
            var provenance = new[]
            {
                new MetadataFieldProvenance
                {
                    Field = "sortingName",
                    Source = verified != null && verified.HasOrder ? verified.Source : "Metadata AI local rule",
                    Method = verified != null && verified.HasOrder ? "catalog lookup" : "deterministic",
                    Confidence = "high",
                    Detail = verified != null && verified.HasOrder ? verified.Detail : "Generated locally from an explicit ordinal in the game title."
                }
            };
            history.AddGame(operation, game, before, after, provenance);
            history.SaveOperation(operation);
            return true;
        }

        private bool ApplyAuditSeries(Game game, string seriesName)
        {
            if (game == null || string.IsNullOrWhiteSpace(seriesName)) return false;

            var operation = history.BeginOperation(Loc("MTDA_HistoryApplyMetadata", "Apply AI metadata"));
            var before = history.Capture(game, operation, false);
            var result = new AiMetadataResult
            {
                Series = new List<string> { seriesName },
                Provenance = new List<MetadataFieldProvenance>
                {
                    new MetadataFieldProvenance
                    {
                        Field = "series",
                        Source = "Metadata AI local rule",
                        Method = "deterministic",
                        Confidence = "high",
                        Detail = "Derived from the numbered game title without inventing a series name."
                    }
                }
            };
            var focused = CreateFocusedSettings("series");
            focused.SeriesApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            MetadataApplyService.Apply(PlayniteApi, game, result, focused);

            var applied = game.SeriesIds != null && game.SeriesIds.Count > 0;
            if (applied)
            {
                var after = history.Capture(game, operation, false);
                history.AddGame(operation, game, before, after, result.Provenance);
                history.SaveOperation(operation);
            }

            return applied;
        }

        private static string AuditFieldFocus(string field)
        {
            switch (field)
            {
                case "Description": return "description";
                case "Genres": return "genres";
                case "Tags": return "tags";
                case "Features": return "features";
                case "Developer":
                case "Developers": return "developers";
                case "Publisher":
                case "Publishers": return "publishers";
                case "Age ratings": return "ageRatings";
                case "Regions": return "regions";
                case "Categories": return "categories";
                case "Links": return "links";
                case "Release date": return "releaseDate";
                case "Series": return "series";
                case "Sorting name": return "sortingName";
                default: return null;
            }
        }

        private static void SetFocusedApplyMode(MetaDataIASettings value, string focus, string mode)
        {
            if (focus == "genres") value.GenresApplyMode = mode;
            else if (focus == "tags") value.TagsApplyMode = mode;
            else if (focus == "features") value.FeaturesApplyMode = mode;
            else if (focus == "developers") value.DevelopersApplyMode = mode;
            else if (focus == "publishers") value.PublishersApplyMode = mode;
            else if (focus == "ageRatings") value.AgeRatingsApplyMode = mode;
            else if (focus == "regions") value.RegionsApplyMode = mode;
            else if (focus == "categories") value.CategoriesApplyMode = mode;
            else if (focus == "links") value.LinksApplyMode = mode;
            else if (focus == "series") value.SeriesApplyMode = mode;
        }

        private void ToggleMediaLock(Game game, MediaKind kind)
        {
            if (game == null) return;
            var locked = maintenanceState.Toggle(game.Id, kind);
            PlayniteApi.Dialogs.ShowMessage(string.Format(
                locked
                    ? Loc("MTDA_MediaLocked", "The {0} for {1} is now protected. Metadata AI will keep the current file during automatic, batch, simulation, and repair operations.")
                    : Loc("MTDA_MediaUnlocked", "The {0} for {1} is no longer protected. Metadata AI may replace it when you run a compatible media operation."),
                MediaKindName(kind), game.Name), PluginTitle);
        }

        private void FindAndApplyLogo(Game game)
        {
            if (game == null) return;
            if (!ExtraMetadataLogoService.IsInstalled(PlayniteApi))
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_LogoNeedsEml", "Install Extra Metadata Loader before using logo integration."), PluginTitle);
                return;
            }
            if (maintenanceState.IsLocked(game.Id, MediaKind.Logo))
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_AuditLocked", "This media field is locked. Unlock it before replacing it."), PluginTitle);
                return;
            }

            List<MediaPreviewOption> options = null;
            Exception error = null;
            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                try { options = new MediaGenerationService(settings.Settings, PlayniteApi).GetPreviewOptionsAsync(game, MediaKind.Logo, progress.CancelToken).GetAwaiter().GetResult(); }
                catch (Exception ex) { error = ex; }
            }, new GlobalProgressOptions(PluginTitle + " - " + Loc("MTDA_SearchingLogos", "Searching logos"), true) { IsIndeterminate = true });
            if (error != null) { PlayniteApi.Dialogs.ShowErrorMessage(UserError(error), PluginTitle); return; }
            if (options == null || options.Count == 0) { PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_NoLogosFound", "No reliable logos were found for this game."), PluginTitle); return; }

            MediaPreviewOption selected = null;
            var window = new Window { Title = PluginTitle + " - " + Loc("MTDA_Logo", "Logo"), Width = 980, Height = 720, MinWidth = 720, MinHeight = 500, ShowInTaskbar = false };
            ApplyPlayniteWindowStyle(window);
            var owner = PlayniteApi.Dialogs.GetCurrentAppWindow(); if (owner != null) { window.Owner = owner; window.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            var root = new DockPanel { Margin = new Thickness(14) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var apply = new Button { Content = Loc("MTDA_ApplyChanges", "Apply changes"), MinWidth = 150, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = Loc("MTDA_Cancel", "Cancel"), MinWidth = 110 };
            apply.Click += (s, e) => { if (selected != null) window.DialogResult = true; };
            cancel.Click += (s, e) => window.DialogResult = false;
            buttons.Children.Add(apply); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            root.Children.Add(CreateMediaOptionsPanel(options, option => selected = option)); window.Content = root;
            bool? accepted = null; try { accepted = window.ShowDialog(); } finally { RestoreWindowActivation(owner); }
            if (accepted != true || selected == null) return;

            GeneratedMediaFile logo = null;
            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                logo = new MediaGenerationService(settings.Settings, PlayniteApi).GenerateFromOptionAsync(game, selected, progress.CancelToken).GetAwaiter().GetResult();
            }, new GlobalProgressOptions(PluginTitle + " - " + Loc("MTDA_ApplyLogo", "Apply logo"), true) { IsIndeterminate = true });
            ExtraMetadataLogoService.Apply(PlayniteApi, game, logo);
            PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_LogoApplied", "The logo was saved for Extra Metadata Loader."), PluginTitle);
        }

        private void ApplyMediaForCurrentMode(
            List<Game> games,
            MetaDataIASettings activeSettings,
            bool includeLogo = false)
        {
            if (IsFullscreenMode)
            {
                ApplyMedia(games, activeSettings);
                return;
            }

            ApplyMediaInteractive(games, activeSettings, includeLogo);
        }

        private static string MenuKey(string description)
        {
            switch (description)
            {
                case "Establecer descripciones": return "MTDA_MenuSetDescriptions";
                case "Establecer etiquetas": return "MTDA_MenuSetTags";
                case "Establecer categorias": return "MTDA_MenuSetCategories";
                case "Establecer generos": return "MTDA_MenuSetGenres";
                case "Establecer caracteristicas": return "MTDA_MenuSetFeatures";
                case "Establecer compañías": return "MTDA_MenuSetCompanies";
                case "Establecer edad y region": return "MTDA_MenuSetAgeRegion";
                case "Establecer desarrolladores": return "MTDA_MenuSetDevelopers";
                case "Establecer editores": return "MTDA_MenuSetPublishers";
                case "Establecer clasificaciones por edad": return "MTDA_MenuSetAgeRatings";
                case "Establecer regiones": return "MTDA_MenuSetRegions";
                case "Establecer enlaces": return "MTDA_MenuSetLinks";
                case "Establecer fecha y serie": return "MTDA_MenuSetReleaseSeries";
                case "Establecer fecha de lanzamiento": return "MTDA_MenuSetReleaseDate";
                case "Establecer serie": return "MTDA_MenuSetSeries";
                case "Establecer orden de nombre": return "MTDA_MenuSetSortingName";
                case "Establecer todos los campos": return "MTDA_MenuSetAllFields";
                case "Establecer portada": return "MTDA_MenuSetCover";
                case "Establecer icono": return "MTDA_MenuSetIcon";
                case "Establecer fondo": return "MTDA_MenuSetBackground";
                case "Establecer media completa": return "MTDA_MenuSetFullMedia";
                case "Establecer portadas": return "MTDA_MenuSetCovers";
                case "Establecer iconos": return "MTDA_MenuSetIcons";
                case "Establecer fondos": return "MTDA_MenuSetBackgrounds";
                default: return description;
            }
        }

        private GameMenuItem CreateGameSortingMenuItem(string description)
        {
            return new GameMenuItem
            {
                Description = Loc(MenuKey(description), description),
                MenuSection = MenuRoot + "|" + Loc("MTDA_TabFields", "Campos"),
                Action = actionArgs => ApplySortingNames(actionArgs.Games)
            };
        }

        private void GenerateAndApplyWithConfirmation(List<Game> games, MetaDataIASettings activeSettings, string scopeName)
        {
            if (games == null || games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MessageNoGamesCurrentList", "There are no games in the current list."), PluginTitle);
                return;
            }

            var result = PlayniteApi.Dialogs.ShowMessage(
                string.Format(Loc("MTDA_ConfirmApplyScope", "Metadata AI will be applied to {0} game(s) from {1}.\n\nThis may take a while and consume usage from the configured API. Do you want to continue?"), games.Count, scopeName),
                PluginTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            GenerateAndApply(games, activeSettings);
        }

        private void GenerateAndApplySelected(MetaDataIASettings activeSettings)
        {
            var selected = GetSelectedGames();
            if (selected.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MessageNoSelectedGames", "No games are selected."), PluginTitle);
                return;
            }

            GenerateAndApply(selected, activeSettings);
        }

        private List<Game> GetSelectedOrFilteredGames()
        {
            var selected = GetSelectedGames();
            return selected.Count > 0 ? selected : GetFilteredGames();
        }

        private List<Game> GetSelectedGames()
        {
            return PlayniteApi.MainView.SelectedGames == null
                ? new List<Game>()
                : PlayniteApi.MainView.SelectedGames.ToList();
        }

        private List<Game> GetFilteredGames()
        {
            var filtered = PlayniteApi.MainView.FilteredGames == null
                ? new List<Game>()
                : PlayniteApi.MainView.FilteredGames.ToList();

            if (filtered.Count > 0)
            {
                return filtered;
            }

            return PlayniteApi.Database.Games.GetClone().ToList();
        }

        private MetaDataIASettings CreateFocusedSettings(string focus)
        {
            var clone = Serialization.GetClone(settings.Settings);
            DisableAllFields(clone);

            if (focus == "description")
            {
                clone.GenerateDescription = true;
                clone.DescriptionApplyMode = MetaDataIASettings.ApplyOverwrite;
            }
            else if (focus == "tags")
            {
                clone.GenerateTags = true;
                clone.TagsApplyMode = MetaDataIASettings.ApplyAppend;
            }
            else if (focus == "categories")
            {
                clone.GenerateCategories = true;
                clone.CategoriesApplyMode = MetaDataIASettings.ApplyAppend;
            }
            else if (focus == "genres")
            {
                clone.GenerateGenres = true;
                clone.GenresApplyMode = MetaDataIASettings.ApplyAppend;
            }
            else if (focus == "features")
            {
                clone.GenerateFeatures = true;
                clone.FeaturesApplyMode = MetaDataIASettings.ApplyAppend;
            }
            else if (focus == "developers")
            {
                clone.GenerateDevelopers = true;
                clone.DevelopersApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            }
            else if (focus == "publishers")
            {
                clone.GeneratePublishers = true;
                clone.PublishersApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            }
            else if (focus == "ageRatings")
            {
                clone.GenerateAgeRatings = true;
                clone.AgeRatingsApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            }
            else if (focus == "regions")
            {
                clone.GenerateRegions = true;
                clone.RegionsApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            }
            else if (focus == "links")
            {
                clone.GenerateLinks = true;
                clone.LinksApplyMode = MetaDataIASettings.ApplyAppend;
            }
            else if (focus == "releaseDate")
            {
                clone.GenerateReleaseDate = true;
                clone.ReleaseDateApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            }
            else if (focus == "series")
            {
                clone.GenerateSeries = true;
                clone.SeriesApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            }

            return clone;
        }

        private MetaDataIASettings CreateFocusedMediaSettings(string focus)
        {
            return CreateFocusedMediaSettings(settings.Settings, focus);
        }

        private static MetaDataIASettings CreateFocusedMediaSettings(MetaDataIASettings source, string focus)
        {
            var clone = Serialization.GetClone(source);
            clone.DownloadCoverImage = false;
            clone.DownloadIcon = false;
            clone.DownloadBackgroundImage = false;
            clone.CoverImageApplyMode = MetaDataIASettings.ApplySkip;
            clone.IconApplyMode = MetaDataIASettings.ApplySkip;
            clone.BackgroundImageApplyMode = MetaDataIASettings.ApplySkip;

            if (focus == "cover")
            {
                clone.DownloadCoverImage = true;
                clone.CoverImageApplyMode = MetaDataIASettings.ApplyOverwrite;
            }
            else if (focus == "icon")
            {
                clone.DownloadIcon = true;
                clone.IconApplyMode = MetaDataIASettings.ApplyOverwrite;
            }
            else if (focus == "background")
            {
                clone.DownloadBackgroundImage = true;
                clone.BackgroundImageApplyMode = MetaDataIASettings.ApplyOverwrite;
            }

            return clone;
        }

        private static void DisableAllFields(MetaDataIASettings activeSettings)
        {
            activeSettings.GenerateDescription = false;
            activeSettings.GenerateGenres = false;
            activeSettings.GenerateTags = false;
            activeSettings.GenerateFeatures = false;
            activeSettings.GenerateDevelopers = false;
            activeSettings.GeneratePublishers = false;
            activeSettings.GenerateAgeRatings = false;
            activeSettings.GenerateRegions = false;
            activeSettings.GenerateCategories = false;
            activeSettings.GenerateSortingName = false;
            activeSettings.GenerateLinks = false;
            activeSettings.GenerateReleaseDate = false;
            activeSettings.GenerateSeries = false;
            activeSettings.DescriptionApplyMode = MetaDataIASettings.ApplySkip;
            activeSettings.GenresApplyMode = MetaDataIASettings.ApplySkip;
            activeSettings.TagsApplyMode = MetaDataIASettings.ApplySkip;
            activeSettings.FeaturesApplyMode = MetaDataIASettings.ApplySkip;
            activeSettings.DevelopersApplyMode = MetaDataIASettings.ApplySkip;
            activeSettings.PublishersApplyMode = MetaDataIASettings.ApplySkip;
            activeSettings.AgeRatingsApplyMode = MetaDataIASettings.ApplySkip;
            activeSettings.RegionsApplyMode = MetaDataIASettings.ApplySkip;
            activeSettings.CategoriesApplyMode = MetaDataIASettings.ApplySkip;
            activeSettings.SortingNameApplyMode = MetaDataIASettings.ApplySkip;
            activeSettings.LinksApplyMode = MetaDataIASettings.ApplySkip;
        }
    }
}
