using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MetaDataIAPlugin
{
    public class MetaDataIAPlugin : MetadataPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly MetaDataIASettingsViewModel settings;

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
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            ProcessNewGamesAfterLibraryUpdate();
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new MetaDataIASettingsView();
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            if (args == null || args.Games == null || args.Games.Count == 0)
            {
                yield break;
            }

            if (!IsFullscreenMode)
            {
                yield return new GameMenuItem
                {
                    Description = Loc("MTDA_MenuGenerateReview", "Generar y revisar metadatos IA"),
                    MenuSection = MenuRoot,
                    Action = actionArgs => GenerateAndReview(actionArgs.Games.FirstOrDefault())
                };
            }

            yield return new GameMenuItem
            {
                Description = Loc("MTDA_MenuGenerateApply", "Generar y aplicar metadatos IA"),
                MenuSection = MenuRoot,
                Action = actionArgs => GenerateAndApply(actionArgs.Games, settings.Settings)
            };

            yield return CreateGameMenuItem("Establecer descripciones", activeSettings => CreateFocusedSettings("description"));
            yield return CreateGameMenuItem("Establecer etiquetas", activeSettings => CreateFocusedSettings("tags"));
            yield return CreateGameMenuItem("Establecer categorias", activeSettings => CreateFocusedSettings("categories"));
            yield return CreateGameMenuItem("Establecer generos", activeSettings => CreateFocusedSettings("genres"));
            yield return CreateGameMenuItem("Establecer caracteristicas", activeSettings => CreateFocusedSettings("features"));
            yield return CreateGameMenuItem("Establecer compañías", activeSettings => CreateFocusedSettings("companies"));
            yield return CreateGameMenuItem("Establecer edad y region", activeSettings => CreateFocusedSettings("age-region"));
            yield return CreateGameMenuItem("Establecer enlaces", activeSettings => CreateFocusedSettings("links"));
            yield return CreateGameSortingMenuItem("Establecer orden de nombre");

            yield return CreateGameMediaMenuItem("Establecer portada", activeSettings => CreateFocusedMediaSettings("cover"));
            yield return CreateGameMediaMenuItem("Establecer icono", activeSettings => CreateFocusedMediaSettings("icon"));
            yield return CreateGameMediaMenuItem("Establecer fondo", activeSettings => CreateFocusedMediaSettings("background"));
            yield return CreateGameMediaMenuItem("Establecer media completa", activeSettings => activeSettings);
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
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

            yield return CreateMainMenuItem("Establecer descripciones", activeSettings => CreateFocusedSettings("description"));
            yield return CreateMainMenuItem("Establecer etiquetas", activeSettings => CreateFocusedSettings("tags"));
            yield return CreateMainMenuItem("Establecer categorias", activeSettings => CreateFocusedSettings("categories"));
            yield return CreateMainMenuItem("Establecer generos", activeSettings => CreateFocusedSettings("genres"));
            yield return CreateMainMenuItem("Establecer caracteristicas", activeSettings => CreateFocusedSettings("features"));
            yield return CreateMainMenuItem("Establecer compañías", activeSettings => CreateFocusedSettings("companies"));
            yield return CreateMainMenuItem("Establecer edad y region", activeSettings => CreateFocusedSettings("age-region"));
            yield return CreateMainMenuItem("Establecer enlaces", activeSettings => CreateFocusedSettings("links"));
            yield return CreateMainSortingMenuItem("Establecer orden de nombre");

            yield return CreateMainMediaMenuItem("Establecer portadas", activeSettings => CreateFocusedMediaSettings("cover"));
            yield return CreateMainMediaMenuItem("Establecer iconos", activeSettings => CreateFocusedMediaSettings("icon"));
            yield return CreateMainMediaMenuItem("Establecer fondos", activeSettings => CreateFocusedMediaSettings("background"));
            yield return CreateMainMediaMenuItem("Establecer media completa", activeSettings => activeSettings);
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
                        result = new MetadataGenerationService(settings.Settings).GenerateAsync(game, progress.CancelToken).GetAwaiter().GetResult();
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
                            var result = new MetadataGenerationService(activeSettings).GenerateAsync(game, progress.CancelToken).GetAwaiter().GetResult();
                            progress.MainDispatcher.Invoke(new Action(() =>
                            {
                                MetadataApplyService.Apply(PlayniteApi, game, result, activeSettings);
                                LearnVocabulary(activeSettings, result);
                            }));
                            processed++;
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Failed to process AI metadata for " + game.Name);
                            errors.Add(game.Name + ": " + UserError(ex));

                            var providerException = ex as AiProviderException;
                            if (providerException != null && providerException.StopBatch)
                            {
                                break;
                            }
                        }

                        progress.CurrentProgressValue = processed + errors.Count;
                    }
                }, new GlobalProgressOptions(PluginTitle, true));

                if (errors.Count > 0)
                {
                    if (silent)
                    {
                        logger.Warn("Metadata AI auto-import metadata completed with errors: " + string.Join(" | ", errors));
                    }
                    else
                    {
                        ShowBatchErrors(processed, errors);
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
                            var appliedForGame = ApplyEnabledMedia(service, game, progress);
                            appliedMedia += appliedForGame;
                            processed++;
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Failed to process media for " + game.Name);
                            errors.Add(game.Name + ": " + UserError(ex));
                        }

                        progress.CurrentProgressValue = processed + errors.Count;
                    }

                    qualitySkipped = service.StrictQualitySkipCount;
                }, new GlobalProgressOptions(PluginTitle + " - " + Loc("MTDA_TabMedia", "Media"), true));

                if (errors.Count > 0)
                {
                    if (silent)
                    {
                        logger.Warn("Metadata AI auto-import media completed with errors: " + string.Join(" | ", errors));
                    }
                    else
                    {
                        ShowBatchErrors(processed, errors, qualitySkipped);
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

        private void ApplyMediaInteractive(List<Game> games, MetaDataIASettings activeSettings)
        {
            if (games == null || games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MessageNoGamesMedia", "There are no games to apply media to."), PluginTitle);
                return;
            }

            if (games.Count == 1)
            {
                ShowMediaChooser(games[0], activeSettings);
                return;
            }

            ApplyMedia(games, activeSettings);
        }

        private void ShowMediaChooser(Game game, MetaDataIASettings activeSettings)
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

            var kinds = GetEnabledMediaKinds(activeSettings).ToList();
            if (kinds.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(Loc("MTDA_MessageNoMediaTypesEnabled", "No media type is enabled in the selected configuration."), PluginTitle);
                return;
            }

            var optionsByKind = new Dictionary<MediaKind, List<MediaPreviewOption>>();
            var diagnosticsByKind = new Dictionary<MediaKind, string>();
            var service = new MediaGenerationService(activeSettings, PlayniteApi);
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
                            optionsByKind[kind] = service.GetPreviewOptionsAsync(game, kind, progress.CancelToken).GetAwaiter().GetResult();
                            diagnosticsByKind[kind] = service.GetLastDiagnostics(game, kind);
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

                PlayniteApi.Dialogs.ShowErrorMessage(message, PluginTitle);
                return;
            }

            var selectedOptions = new Dictionary<MediaKind, MediaPreviewOption>();
            var pickerSettings = Serialization.GetClone(activeSettings);
            pickerSettings.EnsureDefaults();
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

            var root = new Grid { Margin = new Thickness(14) };
            ApplyDynamicResource(root, Panel.BackgroundProperty, "StandardWindowBackgroundBrush");
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Button applyChangesButton = null;
            var tabs = new TabControl();
            foreach (var kind in kinds)
            {
                tabs.Items.Add(CreateTab(MediaKindName(kind), CreateMediaOptionsPanel(optionsByKind[kind], option =>
                {
                    selectedOptions[option.Kind] = option;
                    if (applyChangesButton != null)
                    {
                        applyChangesButton.IsEnabled = true;
                    }
                })));
            }

            Grid.SetRow(tabs, 0);
            root.Children.Add(tabs);

            var cropContainer = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = kinds.Contains(MediaKind.Cover) || kinds.Contains(MediaKind.Background)
                    ? Visibility.Visible
                    : Visibility.Collapsed
            };
            ApplyDynamicResource(cropContainer, Border.BackgroundProperty, "ControlBackgroundBrush");
            ApplyDynamicResource(cropContainer, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");

            var cropPanel = new StackPanel();
            cropContainer.Child = cropPanel;
            var cropTitle = new TextBlock
            {
                Text = Loc("MTDA_CropPickerTitle", "Crop positioning"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 0, 0, 8)
            };
            ApplyDynamicResource(cropTitle, TextBlock.ForegroundProperty, "TextBrush");
            cropPanel.Children.Add(cropTitle);

            var cropControls = new WrapPanel { Orientation = Orientation.Horizontal };
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
                cropControls.Children.Add(CreateCropPickerControl(
                    Loc("MTDA_BackgroundCropAnchor", "Background crop origin"),
                    Loc("MTDA_CropAnchorPickerHelp", "Choose which area of the image should be preserved when it is cropped to the final aspect ratio."),
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
            Grid.SetRow(cropContainer, 1);
            root.Children.Add(cropContainer);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            applyChangesButton = new Button
            {
                Content = Loc("MTDA_ApplyChanges", "Apply changes"),
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
            var accepted = window.ShowDialog();

            if (accepted == true && selectedOptions.Count > 0)
            {
                var orderedOptions = kinds.Where(selectedOptions.ContainsKey).Select(kind => selectedOptions[kind]).ToList();
                ApplySelectedMediaOptions(game, pickerSettings, orderedOptions);
            }
        }

        private static UIElement CreateCropPickerControl(string label, string hint, IEnumerable<LocalizedOption> options, string selectedValue, Action<string> changed)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 18, 0), MinWidth = 300, MaxWidth = 460 };
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
                MinWidth = 230
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

        private UIElement CreateMediaOptionsPanel(List<MediaPreviewOption> options, Action<MediaPreviewOption> selectAction)
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
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(scroll, 0);
            root.Children.Add(scroll);

            var panel = new UniformGrid
            {
                Margin = new Thickness(4),
                Columns = 1
            };
            scroll.Content = panel;
            scroll.SizeChanged += (sender, args) =>
            {
                var kind = options.Select(x => x.Kind).FirstOrDefault();
                var minimumTileWidth = kind == MediaKind.Background ? 340 : 240;
                var availableWidth = Math.Max(1, args.NewSize.Width - 16);
                panel.Columns = Math.Max(1, (int)Math.Floor(availableWidth / minimumTileWidth));
            };

            var optionBorders = new List<Border>();
            var visibleCount = Math.Min(24, options.Count);
            Action<MediaPreviewOption> addOption = option =>
            {
                Border optionBorder;
                panel.Children.Add(CreateMediaOptionTile(option, (selectedOption, selectedBorder) =>
                {
                    foreach (var border in optionBorders)
                    {
                        border.BorderThickness = new Thickness(1);
                        ApplyDynamicResource(border, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
                    }

                    selectedBorder.BorderThickness = new Thickness(3);
                    selectedBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(105, 176, 255));

                    if (selectAction != null)
                    {
                        selectAction(selectedOption);
                    }
                }, out optionBorder));
                optionBorders.Add(optionBorder);
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
                Grid.SetRow(loadMoreButton, 1);
                root.Children.Add(loadMoreButton);
            }

            return root;
        }

        private UIElement CreateMediaOptionTile(MediaPreviewOption option, Action<MediaPreviewOption, Border> selectAction, out Border optionBorder)
        {
            var tileRoot = new Grid
            {
                MinWidth = option.Kind == MediaKind.Background ? 300 : 210,
                Margin = new Thickness(6),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var dottedBorder = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromArgb(70, 150, 150, 150)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 1, 3 },
                RadiusX = 4,
                RadiusY = 4,
                IsHitTestVisible = false
            };
            tileRoot.Children.Add(dottedBorder);

            var border = new Border
            {
                Margin = new Thickness(1),
                Padding = new Thickness(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(45, 90, 90, 90))
            };
            ApplyDynamicResource(border, Border.BackgroundProperty, "ControlBackgroundBrush");
            ApplyDynamicResource(border, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
            optionBorder = border;
            tileRoot.Children.Add(border);

            var stack = new StackPanel();
            border.Child = stack;

            var image = new Image
            {
                Height = option.Kind == MediaKind.Cover ? 190 : option.Kind == MediaKind.Background ? 96 : 190,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(option.Url, UriKind.Absolute);
                bitmap.DecodePixelWidth = option.Kind == MediaKind.Background ? 360 : 240;
                bitmap.CacheOption = BitmapCacheOption.OnDemand;
                bitmap.EndInit();
                image.Source = bitmap;
            }
            catch
            {
            }

            stack.Children.Add(image);
            var infoPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            infoPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(option.SourceName) ? "Fuente desconocida" : option.SourceName,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            infoPanel.Children.Add(CreateMediaInfoLine(Loc("MTDA_MediaInfoSize", "Size"), option.Width > 0 && option.Height > 0 ? option.Width + " x " + option.Height : Loc("MTDA_Unknown", "Unknown")));
            infoPanel.Children.Add(CreateMediaInfoLine(Loc("MTDA_MediaInfoStyle", "Style"), string.IsNullOrWhiteSpace(option.Style) ? Loc("MTDA_NotSpecified", "Not specified") : option.Style));
            infoPanel.Children.Add(CreateMediaInfoLine(Loc("MTDA_MediaInfoScore", "Score"), option.Score.ToString()));
            infoPanel.Children.Add(CreateMediaInfoLine(Loc("MTDA_MediaInfoOfficial", "Official"), option.IsOfficial ? Loc("MTDA_Yes", "Yes") : Loc("MTDA_NoCommunity", "No / community")));
            stack.Children.Add(infoPanel);

            var openButton = new Button
            {
                Content = Loc("MTDA_OpenInBrowser", "Open"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0)
            };
            openButton.Click += (sender, args) =>
            {
                args.Handled = true;
                OpenUrl(option.Url);
            };
            stack.Children.Add(openButton);

            tileRoot.MouseLeftButtonUp += (sender, args) =>
            {
                if (IsInsideButton(args.OriginalSource as DependencyObject))
                {
                    return;
                }

                if (selectAction != null)
                {
                    selectAction(option, border);
                }
            };

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

                Exception applyError = null;
                var appliedCount = 0;
                PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                {
                    try
                    {
                        var service = new MediaGenerationService(activeSettings, PlayniteApi);
                        foreach (var option in options)
                        {
                            if (progress.CancelToken.IsCancellationRequested)
                            {
                                break;
                            }

                            progress.Text = string.Format(Loc("MTDA_ProgressApplyingMediaKind", "Applying {0}..."), MediaKindName(option.Kind).ToLowerInvariant());
                            var media = service.GenerateFromOptionAsync(game, option, progress.CancelToken).GetAwaiter().GetResult();
                            progress.MainDispatcher.Invoke(new Action(() =>
                            {
                                MediaGenerationService.ApplyMediaFile(PlayniteApi, game, media);
                            }));
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

                PlayniteApi.Dialogs.ShowMessage(string.Format(Loc("MTDA_MessageAppliedMediaFiles", "Metadata AI applied {0} media file(s)."), appliedCount), PluginTitle);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to apply selected media.");
                PlayniteApi.Dialogs.ShowErrorMessage(UserError(ex), PluginTitle);
            }
        }

        private IEnumerable<MediaKind> GetEnabledMediaKinds(MetaDataIASettings activeSettings)
        {
            var service = new MediaGenerationService(activeSettings, PlayniteApi);
            foreach (var kind in new[] { MediaKind.Cover, MediaKind.Icon, MediaKind.Background })
            {
                if (service.ShouldGenerate(kind))
                {
                    yield return kind;
                }
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
            foreach (var game in games)
            {
                var sortingName = SortingNameService.Generate(PlayniteApi, game);
                if (string.IsNullOrWhiteSpace(sortingName))
                {
                    continue;
                }

                game.SortingName = sortingName;
                PlayniteApi.Database.Games.Update(game);
                processed++;
            }

            PlayniteApi.Dialogs.ShowMessage(string.Format(Loc("MTDA_MessageSortingNameUpdated", "Metadata AI updated sorting names for {0} game(s)."), processed), PluginTitle);
        }

        private int ApplyEnabledMedia(MediaGenerationService service, Game game, GlobalProgressActionArgs progress)
        {
            var applied = 0;
            foreach (var kind in new[] { MediaKind.Cover, MediaKind.Icon, MediaKind.Background })
            {
                if (progress.CancelToken.IsCancellationRequested || !service.ShouldGenerate(kind) || !service.ShouldApply(game, kind))
                {
                    continue;
                }

                var media = service.GenerateAsync(game, kind, progress.CancelToken).GetAwaiter().GetResult();
                if (media == null)
                {
                    continue;
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

        private void ShowBatchErrors(int processed, List<string> errors, int qualitySkipped = 0)
        {
            var separator = "\n\n" + new string('-', 90) + "\n\n";
            var message = string.Format(Loc("MTDA_MessageBatchErrorsHeader", "Metadata AI updated {0} game(s). Errors: {1}"), processed, errors.Count) + "\n\n" +
                          string.Join(separator, errors);
            message = AppendQualitySkipSummary(message, qualitySkipped);

            if (IsFullscreenMode)
            {
                PlayniteApi.Dialogs.ShowMessage(message, PluginTitle);
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
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10),
                FontSize = 14
            };
            ApplyDynamicResource(text, TextBlock.ForegroundProperty, "TextBrush");

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = text
            };

            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = scroll
            };
            ApplyDynamicResource(border, Border.BackgroundProperty, "ControlIdleBackgroundBrush");
            ApplyDynamicResource(border, Border.BorderBrushProperty, "GlyphBrush");
            Grid.SetRow(border, 0);
            root.Children.Add(border);

            var okButton = new Button
            {
                Content = "OK",
                MinWidth = 100,
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            okButton.Click += (sender, args) => window.Close();
            Grid.SetRow(okButton, 1);
            root.Children.Add(okButton);

            window.Content = root;
            window.ShowDialog();
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

        private MainMenuItem CreateMainMediaMenuItem(string description, Func<MetaDataIASettings, MetaDataIASettings> settingsFactory)
        {
            return new MainMenuItem
            {
                Description = Loc(MenuKey(description), description),
                MenuSection = MenuRoot + "|" + Loc("MTDA_TabMedia", "Media"),
                Action = actionArgs => ApplyMediaForCurrentMode(GetSelectedOrFilteredGames(), settingsFactory(settings.Settings))
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

        private GameMenuItem CreateGameMenuItem(string description, Func<MetaDataIASettings, MetaDataIASettings> settingsFactory)
        {
            return new GameMenuItem
            {
                Description = Loc(MenuKey(description), description),
                MenuSection = MenuRoot + "|" + Loc("MTDA_TabFields", "Campos"),
                Action = actionArgs => GenerateAndApply(actionArgs.Games, settingsFactory(settings.Settings))
            };
        }

        private GameMenuItem CreateGameMediaMenuItem(string description, Func<MetaDataIASettings, MetaDataIASettings> settingsFactory)
        {
            return new GameMenuItem
            {
                Description = Loc(MenuKey(description), description),
                MenuSection = MenuRoot + "|" + Loc("MTDA_TabMedia", "Media"),
                Action = actionArgs => ApplyMediaForCurrentMode(actionArgs.Games, settingsFactory(settings.Settings))
            };
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

        private void ApplyMediaForCurrentMode(List<Game> games, MetaDataIASettings activeSettings)
        {
            if (IsFullscreenMode)
            {
                ApplyMedia(games, activeSettings);
                return;
            }

            ApplyMediaInteractive(games, activeSettings);
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
                case "Establecer enlaces": return "MTDA_MenuSetLinks";
                case "Establecer orden de nombre": return "MTDA_MenuSetSortingName";
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
            else if (focus == "companies")
            {
                clone.GenerateDevelopers = true;
                clone.GeneratePublishers = true;
                clone.DevelopersApplyMode = MetaDataIASettings.ApplyEmptyOnly;
                clone.PublishersApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            }
            else if (focus == "age-region")
            {
                clone.GenerateAgeRatings = true;
                clone.GenerateRegions = true;
                clone.AgeRatingsApplyMode = MetaDataIASettings.ApplyEmptyOnly;
                clone.RegionsApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            }
            else if (focus == "links")
            {
                clone.GenerateLinks = true;
                clone.LinksApplyMode = MetaDataIASettings.ApplyAppend;
            }

            return clone;
        }

        private MetaDataIASettings CreateFocusedMediaSettings(string focus)
        {
            var clone = Serialization.GetClone(settings.Settings);
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
