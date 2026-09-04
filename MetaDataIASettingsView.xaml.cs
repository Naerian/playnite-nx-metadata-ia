using System.Windows;
using System.Windows.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using Playnite.SDK.Data;
using Playnite.SDK.Models;

namespace MetaDataIAPlugin
{
    public partial class MetaDataIASettingsView : UserControl
    {
        private Popup sourcePriorityPopup;
        private FrameworkElement sourcePriorityPopupTarget;
        private MediaKind? sourcePriorityPopupKind;
        private FrameworkElement recentlyClosedSourcePriorityPopupTarget;
        private MediaKind? recentlyClosedSourcePriorityPopupKind;
        private DateTime recentlyClosedSourcePriorityPopupAt = DateTime.MinValue;
        private TestOperationState providerTestOperation;
        private TestOperationState mediaTestOperation;
        private CancellationTokenSource providerUsageRefreshCancellation;
        private bool providerUsageRefreshActive;
        private CancellationTokenSource providerModelsRefreshCancellation;
        private bool providerModelsRefreshActive;
        private readonly ObservableCollection<string> providerModelIds = new ObservableCollection<string>();
        private string lastAppliedProviderPreset;
        private MetaDataIASettings observedSettings;
        private string lastProviderTestDetails;
        private string lastMediaTestDetails;
        private bool? providerTestSucceeded;
        private readonly bool themeStandaloneWindow;

        private sealed class TestOperationState
        {
            public CancellationTokenSource Cancellation { get; set; }
            public DispatcherTimer Timer { get; set; }
            public Stopwatch Stopwatch { get; set; }
            public Border Panel { get; set; }
            public ProgressBar Progress { get; set; }
            public TextBlock StatusText { get; set; }
            public TextBlock ElapsedText { get; set; }
            public Button CancelButton { get; set; }
            public Button CopyButton { get; set; }
            public Button TriggerButton { get; set; }
            public object OriginalButtonContent { get; set; }
            public string TargetName { get; set; }
            public bool TimedOut { get; set; }
            public bool CancelledByUser { get; set; }
            public string TechnicalDetails { get; set; }
        }

        private ScrollViewer hostScrollViewer;
        private Window hostWindow;

        public MetaDataIASettingsView()
            : this(false)
        {
        }

        public MetaDataIASettingsView(bool themeStandaloneWindow)
        {
            this.themeStandaloneWindow = themeStandaloneWindow;
            InitializeComponent();
            ProviderModelComboBox.ItemsSource = providerModelIds;
            MoveNavigationItem(LibrarySectionNavigation, FieldsNavigationItem, 0);
            LibrarySectionNavigation.SelectedItem = FieldsNavigationItem;
            Loaded += OnSettingsHostLoaded;
            Unloaded += MetaDataIASettingsView_OnUnloaded;
            DataContextChanged += (s, e) =>
            {
                ObserveSettings(null);
                var viewModel = DataContext as MetaDataIASettingsViewModel;
                if (viewModel != null)
                {
                    ObserveSettings(viewModel.Settings);
                    lastAppliedProviderPreset = viewModel.Settings.ProviderPreset;
                    viewModel.RefreshOriginLibraryIntegrations();
                    LoadPasswordBoxes(viewModel.Settings);
                    ApplyAppearancePreset();
                    BuildAppearancePresetChips();
                    RefreshConfigurationSummary();
                    Dispatcher.BeginInvoke(new Action(() => RefreshProviderUsageDisplay(null)));
                    Dispatcher.BeginInvoke(new Action(async () => await RefreshProviderModelsAsync(false)));
                }
            };
        }

        private static void MoveNavigationItem(TabControl navigation, TabItem item, int targetIndex)
        {
            if (item == null || navigation == null || targetIndex < 0)
            {
                return;
            }

            navigation.Items.Remove(item);
            navigation.Items.Insert(Math.Min(targetIndex, navigation.Items.Count), item);
        }

        private void OnSettingsHostLoaded(object sender, RoutedEventArgs e)
        {
            ApplyAppearancePreset();
            BuildAppearancePresetChips();
            ApplyPreferredWindowSize();
            AttachToHost();
            Dispatcher.BeginInvoke(new Action(AttachToHost), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(AttachToHost), DispatcherPriority.ApplicationIdle);
            Dispatcher.BeginInvoke(new Action(FillSelectedContentHosts), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(FillSelectedContentHosts), DispatcherPriority.ApplicationIdle);
        }

        private void ApplyAppearancePreset()
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            var preset = viewModel != null && viewModel.Settings != null
                ? viewModel.Settings.AppearancePreset
                : SettingsAppearance.Midnight;
            SettingsAppearance.Apply(this, preset);
            if (themeStandaloneWindow)
            {
                SettingsAppearance.ApplyWindow(Window.GetWindow(this), preset);
            }
            RefreshAppearancePresetChips();
        }

        private void BuildAppearancePresetChips()
        {
            if (AppearancePresetChips == null)
            {
                return;
            }

            AppearancePresetChips.Children.Clear();
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            var options = viewModel != null && viewModel.Settings != null
                ? viewModel.Settings.AppearancePresetOptions
                : null;
            if (options == null)
            {
                return;
            }

            foreach (var option in options)
            {
                if (option == null || string.IsNullOrWhiteSpace(option.Value))
                {
                    continue;
                }

                var button = new Button
                {
                    Content = option.DisplayName,
                    Tag = option.Value,
                    MinHeight = 36,
                    Height = 36,
                    MinWidth = 88,
                    Padding = new Thickness(12, 0, 12, 0),
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = Cursors.Hand,
                    Focusable = true,
                    BorderThickness = new Thickness(1),
                    FontSize = 14,
                    Template = CreateAppearanceChipTemplate()
                };
                button.Click += AppearancePresetChip_OnClick;
                button.MouseEnter += AppearancePresetChip_OnMouseEnter;
                button.MouseLeave += AppearancePresetChip_OnMouseLeave;
                AppearancePresetChips.Children.Add(button);
            }

            RefreshAppearancePresetChips();
        }

        private static ControlTemplate CreateAppearanceChipTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetBinding(TextElement.ForegroundProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        private void AppearancePresetChip_OnMouseEnter(object sender, MouseEventArgs e)
        {
            var button = sender as Button;
            if (button == null || IsAppearanceChipSelected(button))
            {
                return;
            }

            var palette = GetCurrentAppearancePalette();
            button.Background = new SolidColorBrush(palette.Hover);
        }

        private void AppearancePresetChip_OnMouseLeave(object sender, MouseEventArgs e)
        {
            var button = sender as Button;
            if (button == null || IsAppearanceChipSelected(button))
            {
                return;
            }

            var palette = GetCurrentAppearancePalette();
            button.Background = new SolidColorBrush(palette.BadgeBg);
        }

        private bool IsAppearanceChipSelected(Button button)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            var selected = viewModel != null && viewModel.Settings != null
                ? SettingsAppearance.Normalize(viewModel.Settings.AppearancePreset)
                : SettingsAppearance.Midnight;
            return string.Equals(button.Tag as string, selected, StringComparison.OrdinalIgnoreCase);
        }

        private SettingsAppearance.Palette GetCurrentAppearancePalette()
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            var selected = viewModel != null && viewModel.Settings != null
                ? viewModel.Settings.AppearancePreset
                : SettingsAppearance.Midnight;
            return SettingsAppearance.GetPalette(selected);
        }

        private void AppearancePresetChip_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var preset = button == null ? null : button.Tag as string;
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null || viewModel.Settings == null || string.IsNullOrWhiteSpace(preset))
            {
                return;
            }

            viewModel.Settings.AppearancePreset = preset;
            ApplyAppearancePreset();
        }

        private void RefreshAppearancePresetChips()
        {
            if (AppearancePresetChips == null)
            {
                return;
            }

            var viewModel = DataContext as MetaDataIASettingsViewModel;
            var selected = viewModel != null && viewModel.Settings != null
                ? SettingsAppearance.Normalize(viewModel.Settings.AppearancePreset)
                : SettingsAppearance.Midnight;
            var palette = SettingsAppearance.GetPalette(selected);
            var accent = new SolidColorBrush(palette.Accent);
            var accentOn = new SolidColorBrush(palette.AccentOn);
            var badgeBg = new SolidColorBrush(palette.BadgeBg);
            var text = new SolidColorBrush(palette.Text);
            accent.Freeze();
            accentOn.Freeze();
            badgeBg.Freeze();
            text.Freeze();

            foreach (var child in AppearancePresetChips.Children)
            {
                var button = child as Button;
                if (button == null)
                {
                    continue;
                }

                var isSelected = string.Equals(button.Tag as string, selected, StringComparison.OrdinalIgnoreCase);
                button.Background = isSelected ? accent : badgeBg;
                button.Foreground = isSelected ? accentOn : text;
                button.BorderBrush = isSelected ? accent : new SolidColorBrush(palette.Border);
                button.BorderThickness = new Thickness(1);
                button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
                // Active chip keeps accent on hover (no alternate hover fill).
            }
        }

        private void AttachToHost()
        {
            DetachFromHost();
            hostScrollViewer = FindAncestorScrollViewer();
            if (hostScrollViewer != null)
            {
                hostScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                hostScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                hostScrollViewer.SizeChanged += OnHostSizeChanged;
            }

            hostWindow = Window.GetWindow(this);
            if (hostWindow != null)
            {
                hostWindow.SizeChanged += OnHostSizeChanged;
            }

            ApplyViewportSize();
        }

        private void DetachFromHost()
        {
            if (hostScrollViewer != null)
            {
                hostScrollViewer.SizeChanged -= OnHostSizeChanged;
                hostScrollViewer = null;
            }

            if (hostWindow != null)
            {
                hostWindow.SizeChanged -= OnHostSizeChanged;
                hostWindow = null;
            }
        }

        private void OnHostSizeChanged(object sender, SizeChangedEventArgs args)
        {
            ApplyViewportSize();
            FillSelectedContentHosts();
        }

        private void RootTabsSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            Dispatcher.BeginInvoke(new Action(FillSelectedContentHosts), DispatcherPriority.Loaded);
        }

        private void FillSelectedContentHosts()
        {
            StretchSelectedContent(this);
        }

        private static void StretchSelectedContent(DependencyObject root)
        {
            if (root == null)
            {
                return;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var presenter = child as ContentPresenter;
                if (presenter != null && presenter.Name == "PART_SelectedContentHost")
                {
                    presenter.HorizontalAlignment = HorizontalAlignment.Stretch;
                    presenter.VerticalAlignment = VerticalAlignment.Stretch;
                    var content = presenter.Content as FrameworkElement;
                    if (content == null && VisualTreeHelper.GetChildrenCount(presenter) > 0)
                    {
                        content = VisualTreeHelper.GetChild(presenter, 0) as FrameworkElement;
                    }

                    if (content != null)
                    {
                        content.HorizontalAlignment = HorizontalAlignment.Stretch;
                        content.VerticalAlignment = VerticalAlignment.Stretch;
                        content.ClearValue(WidthProperty);
                        content.ClearValue(HeightProperty);
                    }
                }

                StretchSelectedContent(child);
            }
        }

        private void ApplyViewportSize()
        {
            double width = 0;
            double height = 0;
            if (hostScrollViewer != null)
            {
                width = hostScrollViewer.ViewportWidth > 8
                    ? hostScrollViewer.ViewportWidth
                    : hostScrollViewer.ActualWidth;
                height = hostScrollViewer.ViewportHeight > 8
                    ? hostScrollViewer.ViewportHeight
                    : hostScrollViewer.ActualHeight;
            }

            if (width < 8 || height < 8)
            {
                var slot = FindWindowGridSlot();
                if (slot.Width > 8)
                {
                    width = slot.Width;
                }
                if (slot.Height > 8)
                {
                    height = slot.Height;
                }
            }

            if ((width < 8 || height < 8) && hostWindow != null)
            {
                var content = hostWindow.Content as FrameworkElement;
                if (content != null)
                {
                    if (width < 8)
                    {
                        width = content.ActualWidth;
                    }
                    if (height < 8)
                    {
                        height = content.ActualHeight;
                    }
                }
            }

            if (width > 8 && Math.Abs(Width - width) > 1)
            {
                Width = width;
            }

            if (height > 8 && Math.Abs(Height - height) > 1)
            {
                Height = height;
            }

            FillSelectedContentHosts();
        }

        private void ToggleTemplateTokens_OnClick(object sender, RoutedEventArgs e)
        {
            var show = TemplateTokensPanel.Visibility != Visibility.Visible;
            TemplateTokensPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            TemplateTokensSpacer.Width = show ? new GridLength(16) : new GridLength(0);
            TemplateEditorColumn.Width = show ? new GridLength(3, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
            TemplateTokensColumn.Width = show ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
            ToggleTokensButton.Content = show
                ? Loc("MTDA_HideTokens", "Hide tokens")
                : Loc("MTDA_ShowTokens", "View tokens");
        }

        private void ExpanderChevronButton_OnClick(object sender, RoutedEventArgs e)
        {
            for (var parent = VisualTreeHelper.GetParent(sender as DependencyObject);
                 parent != null;
                 parent = VisualTreeHelper.GetParent(parent))
            {
                var expander = parent as Expander;
                if (expander == null)
                {
                    continue;
                }

                expander.IsExpanded = !expander.IsExpanded;
                e.Handled = true;
                return;
            }
        }

        private Size FindWindowGridSlot()
        {
            for (var parent = VisualTreeHelper.GetParent(this);
                 parent != null;
                 parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is Window)
                {
                    break;
                }

                var grid = parent as Grid;
                if (grid == null || grid.RowDefinitions.Count < 2 || grid.ActualWidth < 400)
                {
                    continue;
                }

                var rowHeight = grid.RowDefinitions[0].ActualHeight;
                if (rowHeight > 200)
                {
                    return new Size(grid.ActualWidth, rowHeight);
                }
            }

            return new Size(0, 0);
        }

        private ScrollViewer FindAncestorScrollViewer()
        {
            for (var parent = VisualTreeHelper.GetParent(this);
                 parent != null;
                 parent = VisualTreeHelper.GetParent(parent))
            {
                var scrollViewer = parent as ScrollViewer;
                if (scrollViewer != null)
                {
                    return scrollViewer;
                }

                if (parent is Window)
                {
                    return null;
                }
            }

            return null;
        }

        private void ApplyPreferredWindowSize()
        {
            var window = Window.GetWindow(this);
            if (window == null)
            {
                return;
            }

            window.SizeToContent = SizeToContent.Manual;
            if (window.MinWidth < 1000)
            {
                window.MinWidth = 1000;
            }
            if (window.MinHeight < 700)
            {
                window.MinHeight = 700;
            }
            if (window.ActualWidth < 1100 && window.Width < 1100)
            {
                window.Width = 1100;
            }
            if (window.ActualHeight < 780 && window.Height < 780)
            {
                window.Height = 780;
            }
        }

        private void MetaDataIASettingsView_OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachFromHost();
            ObserveSettings(null);
            CancelTestOperation(providerTestOperation, false);
            CancelTestOperation(mediaTestOperation, false);
            if (providerUsageRefreshCancellation != null)
            {
                providerUsageRefreshCancellation.Cancel();
                providerUsageRefreshCancellation.Dispose();
                providerUsageRefreshCancellation = null;
            }
            if (providerModelsRefreshCancellation != null)
            {
                providerModelsRefreshCancellation.Cancel();
                providerModelsRefreshCancellation.Dispose();
                providerModelsRefreshCancellation = null;
            }
        }

        private void ObserveSettings(MetaDataIASettings settings)
        {
            if (observedSettings != null)
            {
                observedSettings.PropertyChanged -= ObservedSettings_OnPropertyChanged;
            }

            observedSettings = settings;
            if (observedSettings != null)
            {
                observedSettings.PropertyChanged += ObservedSettings_OnPropertyChanged;
            }
        }

        private void ObservedSettings_OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e != null && (e.PropertyName == "ProviderPreset" || e.PropertyName == "Endpoint" ||
                              e.PropertyName == "Model" || e.PropertyName == "ApiKey"))
            {
                providerTestSucceeded = null;
            }
            if (e != null && e.PropertyName == "AppearancePreset")
            {
                ApplyAppearancePreset();
            }
            if (e != null && e.PropertyName == "ShowAdvancedOptions" && observedSettings != null &&
                !observedSettings.ShowAdvancedOptions && ReferenceEquals(AdvancedSectionNavigation.SelectedItem, RulesNavigationItem))
            {
                AdvancedSectionNavigation.SelectedItem = MaintenanceNavigationItem;
            }
            RefreshConfigurationSummary();
        }

        private void RefreshConfigurationSummary()
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            var settings = viewModel == null ? null : viewModel.Settings;
            if (settings == null || ConfigurationProviderSummaryText == null)
            {
                return;
            }

            var localEndpoint = !string.IsNullOrWhiteSpace(settings.Endpoint) &&
                                (settings.Endpoint.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 settings.Endpoint.IndexOf("127.0.0.1", StringComparison.OrdinalIgnoreCase) >= 0);
            var providerReady = !string.IsNullOrWhiteSpace(settings.Endpoint) &&
                                !string.IsNullOrWhiteSpace(settings.Model) &&
                                (settings.ProviderPreset == MetaDataIASettings.ProviderLmStudio ||
                                 settings.ProviderPreset == MetaDataIASettings.ProviderOllama ||
                                 localEndpoint ||
                                 !string.IsNullOrWhiteSpace(settings.ApiKey));
            var providerStatus = providerReady ? Loc("MTDA_StatusReady", "Ready") : Loc("MTDA_StatusNeedsConfiguration", "Needs configuration");
            if (providerTestSucceeded.HasValue)
            {
                providerStatus += " · " + (providerTestSucceeded.Value
                    ? Loc("MTDA_SourceStatusTested", "Tested")
                    : Loc("MTDA_SourceStatusError", "Error"));
            }
            ConfigurationProviderSummaryText.Text = string.Format(
                "• {0}: {1}\n• {2}: {3}\n• {4}: {5}",
                Loc("MTDA_Provider", "Provider"),
                string.IsNullOrWhiteSpace(settings.ProviderPreset) ? Loc("MTDA_NotConfigured", "Not configured") : settings.ProviderPreset,
                Loc("MTDA_Model", "Model"),
                string.IsNullOrWhiteSpace(settings.Model) ? Loc("MTDA_NoModel", "No model") : settings.Model,
                Loc("MTDA_Endpoint", "Endpoint"),
                string.IsNullOrWhiteSpace(settings.Endpoint) ? Loc("MTDA_NotConfigured", "Not configured") : settings.Endpoint);
            ConfigurationProviderEndpointText.Visibility = Visibility.Collapsed;
            ConfigurationProviderStatusText.Text = providerStatus;
            var providerStatusBrush = providerReady && providerTestSucceeded != false ? "PositiveRatingBrush" : "WarningBrush";
            ApplyStatusBadgeAppearance(ConfigurationProviderStatusText, providerStatusBrush);

            var enabledFields = new[]
            {
                settings.GenerateDescription, settings.GenerateGenres, settings.GenerateTags,
                settings.GenerateFeatures, settings.GenerateDevelopers, settings.GeneratePublishers,
                settings.GenerateAgeRatings, settings.GenerateRegions, settings.GenerateCategories,
                settings.GenerateSortingName, settings.GenerateLinks, settings.GenerateReleaseDate,
                settings.GenerateSeries
            }.Count(x => x);
            ConfigurationFieldsSummaryText.Text = string.Format(Loc("MTDA_FieldsEnabledSummary", "{0} enabled"), enabledFields);
            var enabledFieldNames = new[]
            {
                settings.GenerateDescription ? Loc("MTDA_Description", "Description") : null,
                settings.GenerateGenres ? Loc("MTDA_Genres", "Genres") : null,
                settings.GenerateTags ? Loc("MTDA_Tags", "Tags") : null,
                settings.GenerateFeatures ? Loc("MTDA_Features", "Features") : null,
                settings.GenerateDevelopers ? Loc("MTDA_Developers", "Developers") : null,
                settings.GeneratePublishers ? Loc("MTDA_Publishers", "Publishers") : null,
                settings.GenerateAgeRatings ? Loc("MTDA_Age", "Age ratings") : null,
                settings.GenerateRegions ? Loc("MTDA_Region", "Regions") : null,
                settings.GenerateCategories ? Loc("MTDA_Categories", "Categories") : null,
                settings.GenerateSortingName ? Loc("MTDA_SortingName", "Sorting name") : null,
                settings.GenerateLinks ? Loc("MTDA_Links", "Links") : null,
                settings.GenerateReleaseDate ? Loc("MTDA_ReleaseDate", "Release date") : null,
                settings.GenerateSeries ? Loc("MTDA_Series", "Series") : null
            }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            ConfigurationFieldsDetailText.Text = enabledFieldNames.Length == 0
                ? Loc("MTDA_None", "None")
                : "• " + string.Join("\n• ", enabledFieldNames);

            var enabledMediaKinds = new[]
            {
                settings.DownloadCoverImage, settings.DownloadIcon, settings.DownloadBackgroundImage
            }.Count(x => x);
            var enabledSources = new[]
            {
                settings.UseOriginIntegrationForMedia, settings.MediaUseSteamOfficial || settings.MediaUseSteamScreenshots,
                settings.MediaUseSteamGridDb || settings.MediaUseSteamGridDbBackgroundGrids, settings.MediaUsePsnStore, settings.MediaUseXboxStore,
                settings.MediaUseEpicStore, settings.MediaUseRawg, settings.MediaUseWallhaven, settings.MediaUseScreenScraper,
                settings.MediaUseGiantBomb, settings.MediaUseMobyGames,
                settings.MediaUseIgdb, settings.MediaUseIgn, settings.MediaUseWebSearch,
                settings.UseVndbMetadata, settings.UseWikidataMetadata
            }.Count(x => x);
            ConfigurationMediaSummaryText.Text = string.Format(
                Loc("MTDA_MediaEnabledSummary", "{0} media types · {1} sources"),
                enabledMediaKinds,
                enabledSources);
            var enabledMediaNames = new[]
            {
                settings.DownloadCoverImage ? Loc("MTDA_Cover", "Cover") : null,
                settings.DownloadIcon ? Loc("MTDA_Icon", "Icon") : null,
                settings.DownloadBackgroundImage ? Loc("MTDA_Background", "Background") : null
            }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            ConfigurationMediaDetailText.Text = enabledMediaNames.Length == 0
                ? Loc("MTDA_None", "None")
                : "• " + string.Join("\n• ", enabledMediaNames);

            // Do not call GetActiveTemplate here: it runs EnsureDefaults, which raises
            // PropertyChanged and would recursively refresh this summary.
            var active = settings.Templates == null
                ? null
                : settings.Templates.FirstOrDefault(x => x != null && string.Equals(x.Name, settings.ActiveTemplateName, StringComparison.OrdinalIgnoreCase))
                  ?? settings.Templates.FirstOrDefault();
            ConfigurationTemplateSummaryText.Text = active == null ? settings.ActiveTemplateName : active.DisplayName;
            SetSummaryStatus(ConfigurationTemplateDetailText, settings.EnableTemplateRules);
            SetSummaryStatus(ConfigurationAutomationSummaryText, settings.AutoImportNewGames);
            SetSummaryStatus(ConfigurationAutomationAiStatusText, settings.AutoImportGenerateMetadata);
            SetSummaryStatus(ConfigurationAutomationMediaStatusText, settings.AutoImportGenerateMedia);
            SetSummaryStatus(ConfigurationOfficialContextStatusText, settings.UseOfficialStoreContext || settings.UseOriginIntegrationAsAiContext);
            SetSummaryStatus(ConfigurationStrictFactsStatusText, settings.StrictCompanyAgeRegion);
            SetSummaryStatus(ConfigurationLocalFallbackStatusText, settings.EnableLocalFallback);

            RefreshMediaSourceStatuses(settings);
        }

        private static string StatusText(bool enabled)
        {
            return enabled
                ? Loc("MTDA_SourceStatusActive", "Active")
                : Loc("MTDA_SourceStatusInactive", "Inactive");
        }

        private static void ApplyStatusBadgeAppearance(TextBlock textBlock, string brushKey, double opacity = 1.0)
        {
            if (textBlock == null || string.IsNullOrWhiteSpace(brushKey))
            {
                return;
            }

            textBlock.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            textBlock.Opacity = 1.0;

            var badge = textBlock.Parent as Border;
            if (badge == null)
            {
                for (var parent = VisualTreeHelper.GetParent(textBlock);
                     parent != null;
                     parent = VisualTreeHelper.GetParent(parent))
                {
                    badge = parent as Border;
                    if (badge != null)
                    {
                        break;
                    }
                }
            }

            if (badge == null)
            {
                return;
            }

            // Same pill shape as neutral badges; background tinted by status color.
            badge.BorderThickness = new Thickness(0);
            badge.BorderBrush = Brushes.Transparent;
            badge.Effect = null;
            badge.Opacity = opacity;

            string backgroundKey;
            if (string.Equals(brushKey, "PositiveRatingBrush", StringComparison.Ordinal))
            {
                backgroundKey = "Narian.BadgeSuccessBg";
            }
            else if (string.Equals(brushKey, "WarningBrush", StringComparison.Ordinal))
            {
                backgroundKey = "Narian.BadgeWarningBg";
            }
            else
            {
                backgroundKey = "Narian.BadgeMutedBg";
            }

            badge.SetResourceReference(Border.BackgroundProperty, backgroundKey);
        }

        private static void SetSummaryStatus(TextBlock textBlock, bool enabled)
        {
            if (textBlock == null)
            {
                return;
            }

            textBlock.Text = StatusText(enabled);
            ApplyStatusBadgeAppearance(
                textBlock,
                enabled ? "PositiveRatingBrush" : "WarningBrush");
        }

        private void RefreshMediaSourceStatuses(MetaDataIASettings settings)
        {
            SetSourceStatus(OriginSourceStatusText, settings.UseOriginIntegrationForMedia, true);
            SetSourceStatus(SteamSourceStatusText, settings.MediaUseSteamOfficial || settings.MediaUseSteamScreenshots, true);
            SetSourceStatus(SteamGridDbSourceStatusText, settings.MediaUseSteamGridDb || settings.MediaUseSteamGridDbBackgroundGrids, !string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey));
            SetSourceStatus(PsnSourceStatusText, settings.MediaUsePsnStore, true);
            SetSourceStatus(XboxSourceStatusText, settings.MediaUseXboxStore, true);
            SetSourceStatus(EpicSourceStatusText, settings.MediaUseEpicStore, true);
            SetSourceStatus(RawgSourceStatusText, settings.MediaUseRawg, !string.IsNullOrWhiteSpace(settings.RawgApiKey));
            SetSourceStatus(WallhavenSourceStatusText, settings.MediaUseWallhaven, true);
            SetSourceStatus(WebSearchSourceStatusText, settings.MediaUseWebSearch, true);
            SetSourceStatus(ScreenScraperSourceStatusText, settings.MediaUseScreenScraper,
                !string.IsNullOrWhiteSpace(settings.ScreenScraperUserName) && !string.IsNullOrWhiteSpace(settings.ScreenScraperPassword) &&
                !string.IsNullOrWhiteSpace(settings.ScreenScraperDeveloperId) && !string.IsNullOrWhiteSpace(settings.ScreenScraperDeveloperPassword));
            SetSourceStatus(GiantBombSourceStatusText, settings.MediaUseGiantBomb, !string.IsNullOrWhiteSpace(settings.GiantBombApiKey));
            SetSourceStatus(MobyGamesSourceStatusText, settings.MediaUseMobyGames, !string.IsNullOrWhiteSpace(settings.MobyGamesApiKey));
            SetSourceStatus(TheGamesDbSourceStatusText, settings.MediaUseTheGamesDb, !string.IsNullOrWhiteSpace(settings.TheGamesDbApiKey));
            SetSourceStatus(IgdbSourceStatusText, settings.MediaUseIgdb,
                !string.IsNullOrWhiteSpace(settings.IgdbClientId) &&
                (!string.IsNullOrWhiteSpace(settings.IgdbClientSecret) || !string.IsNullOrWhiteSpace(settings.IgdbAccessToken)));
            SetSourceStatus(IgnSourceStatusText, settings.MediaUseIgn, true);
            SetSourceStatus(VndbSourceStatusText, settings.UseVndbMetadata, true);
            SetSourceStatus(WikidataSourceStatusText, settings.UseWikidataMetadata, true);
        }

        private static void SetSourceStatus(TextBlock target, bool enabled, bool configured)
        {
            if (target == null)
            {
                return;
            }

            target.Text = !enabled
                ? Loc("MTDA_SourceStatusInactive", "Inactive")
                : !configured
                    ? Loc("MTDA_SourceStatusNeedsKey", "Needs credentials")
                    : Loc("MTDA_SourceStatusActive", "Active");
            var statusBrush = enabled && configured ? "PositiveRatingBrush" : enabled ? "WarningBrush" : "GlyphBrush";
            ApplyStatusBadgeAppearance(target, statusBrush, enabled ? 1.0 : 0.65);
        }

        private void OpenSetupWizard_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.SyncSelectedTemplate();
                viewModel.Plugin.OpenSetupWizard(false);
            }
        }

        private void OpenHistory_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Plugin.ShowHistory();
            }
        }

        private void AuditLibrary_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Plugin.ShowLibraryAudit();
            }
        }

        private void LoadPasswordBoxes(MetaDataIASettings settings)
        {
            if (settings == null)
            {
                return;
            }

            SetPassword(ApiKeyBox, settings.ApiKey);
            SetPassword(SteamGridDbApiKeyBox, settings.SteamGridDbApiKey);
            SetPassword(RawgApiKeyBox, settings.RawgApiKey);
            SetPassword(ScreenScraperPasswordBox, settings.ScreenScraperPassword);
            SetPassword(ScreenScraperDeveloperPasswordBox, settings.ScreenScraperDeveloperPassword);
            SetPassword(GiantBombApiKeyBox, settings.GiantBombApiKey);
            SetPassword(MobyGamesApiKeyBox, settings.MobyGamesApiKey);
            SetPassword(TheGamesDbApiKeyBox, settings.TheGamesDbApiKey);
            SetPassword(IgdbClientIdBox, settings.IgdbClientId);
            SetPassword(IgdbClientSecretBox, settings.IgdbClientSecret);
        }

        private static void SetPassword(PasswordBox box, string value)
        {
            if (box != null && box.Password != (value ?? string.Empty))
            {
                box.Password = value ?? string.Empty;
            }
        }

        private void ApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Settings.ApiKey = ApiKeyBox.Password;
            }
        }

        private void SteamGridDbApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Settings.SteamGridDbApiKey = SteamGridDbApiKeyBox.Password;
            }
        }

        private void RawgApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Settings.RawgApiKey = RawgApiKeyBox.Password;
            }
        }

        private void ExportLibrarySnapshot_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null) viewModel.Plugin.ExportLibrarySnapshot();
        }

        private void ExportLibraryCsv_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null) viewModel.Plugin.ExportLibraryCsv();
        }

        private void ImportLibrarySnapshot_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null) viewModel.Plugin.ImportLibrarySnapshot();
        }

        private void ScreenScraperPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null) viewModel.Settings.ScreenScraperPassword = ScreenScraperPasswordBox.Password;
        }

        private void ScreenScraperDeveloperPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null) viewModel.Settings.ScreenScraperDeveloperPassword = ScreenScraperDeveloperPasswordBox.Password;
        }

        private void GiantBombApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null) viewModel.Settings.GiantBombApiKey = GiantBombApiKeyBox.Password;
        }

        private void MobyGamesApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Settings.MobyGamesApiKey = MobyGamesApiKeyBox.Password;
            }
        }

        private void TheGamesDbApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Settings.TheGamesDbApiKey = TheGamesDbApiKeyBox.Password;
            }
        }

        private void IgdbClientSecretBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Settings.IgdbClientSecret = IgdbClientSecretBox.Password;
            }
        }

        private void IgdbClientIdBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Settings.IgdbClientId = IgdbClientIdBox.Password;
            }
        }

        private void AddTemplate_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.AddTemplate();
                FocusTemplateNameEditor();
            }
        }

        private void DuplicateTemplate_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null && viewModel.SelectedTemplate != null)
            {
                viewModel.DuplicateSelectedTemplate();
                FocusTemplateNameEditor();
            }
        }

        private void DeleteTemplate_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null && viewModel.CanDeleteSelectedTemplate)
            {
                var templateName = viewModel.SelectedTemplate == null ? string.Empty : viewModel.SelectedTemplate.DisplayName;
                var message = string.Format(Loc("MTDA_DeleteTemplateConfirm", "Delete template '{0}'?"), templateName);
                if (MessageBox.Show(message, PluginTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }

                viewModel.DeleteSelectedTemplate();
            }
        }

        private void RestoreTemplates_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                var message = Loc("MTDA_RestoreTemplatesConfirm", "Replace all templates with the defaults? Custom templates will be removed.");
                if (MessageBox.Show(message, PluginTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }

                viewModel.RestoreDefaultTemplates();
            }
        }

        private void SourcesPanel_OnLoaded(object sender, RoutedEventArgs e)
        {
            SortSourcesAlphabetically();
            ApplySourceCapabilityFilter();
        }

        private void SortSourcesAlphabetically()
        {
            if (SourceItemsPanel == null)
            {
                return;
            }

            var sources = SourceItemsPanel.Children.OfType<Expander>().ToList();
            if (sources.Count < 2)
            {
                return;
            }

            foreach (var source in sources)
            {
                SourceItemsPanel.Children.Remove(source);
            }

            foreach (var source in sources.OrderBy(GetSourceDisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                SourceItemsPanel.Children.Add(source);
            }
        }

        private static string GetSourceDisplayName(Expander source)
        {
            return FindVisualChildren<TextBlock>(source)
                .Select(x => x.Text ?? string.Empty)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        }

        private void SourceCapabilityFilter_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplySourceCapabilityFilter();
        }

        private void ApplySourceCapabilityFilter()
        {
            if (SourcesPanel == null) return;

            var filter = "all";
            var selected = SourceCapabilityFilterCombo != null
                ? SourceCapabilityFilterCombo.SelectedItem as ComboBoxItem
                : null;
            if (selected != null && selected.Tag != null)
            {
                filter = Convert.ToString(selected.Tag) ?? "all";
            }

            var requireMetadata = string.Equals(filter, "metadata", StringComparison.OrdinalIgnoreCase);
            var requireMedia = string.Equals(filter, "media", StringComparison.OrdinalIgnoreCase);

            foreach (var source in FindVisualChildren<Expander>(SourcesPanel))
            {
                var title = FindVisualChildren<TextBlock>(source)
                    .Select(x => x.Text ?? string.Empty)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
                var mediaOnly = title.IndexOf("steamgrid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("rawg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("wallhaven", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("giant bomb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("mobygames", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("thegamesdb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("web", StringComparison.OrdinalIgnoreCase) >= 0;
                var metadataOnly = title.IndexOf("vndb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   title.IndexOf("wikidata", StringComparison.OrdinalIgnoreCase) >= 0;
                var hasMetadata = !mediaOnly;
                var hasMedia = !metadataOnly;
                source.Visibility = (!requireMetadata || hasMetadata) && (!requireMedia || hasMedia)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                var typed = child as T;
                if (typed != null) yield return typed;
                foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
            }
        }

        private void LocalizeDefaultTemplates_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null)
            {
                return;
            }

            var changed = viewModel.LocalizeUntouchedDefaultTemplates();
            viewModel.Plugin.Api.Dialogs.ShowMessage(
                changed
                    ? Loc("MTDA_DefaultTemplatesLocalized", "Untouched default templates were updated to the selected output language.")
                    : Loc("MTDA_DefaultTemplatesAlreadyLocalized", "There were no untouched default templates to update."),
                PluginTitle);
        }

        private void ResetAllSettings_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null)
            {
                return;
            }

            var confirmation = Loc("MTDA_ResetAllSettingsConfirm", "Reset all Metadata AI settings, templates, provider credentials and local vocabulary? This does not modify any games.");
            if (MessageBox.Show(confirmation, PluginTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            viewModel.ResetAllSettings();
            viewModel.Plugin.Api.Dialogs.ShowMessage(Loc("MTDA_ResetAllSettingsDone", "Metadata AI settings were reset. The setup assistant will now open."), PluginTitle);
            viewModel.Plugin.OpenSetupWizard(true);
        }

        private void FocusTemplateNameEditor()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TemplateNameBox.Focus();
                TemplateNameBox.SelectAll();
            }), DispatcherPriority.Input);
        }

        private void ExportSettingsBackup_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null || viewModel.Settings == null)
            {
                return;
            }

            try
            {
                viewModel.SyncSelectedTemplate();
                var dialog = new SaveFileDialog
                {
                    Title = Loc("MTDA_ExportSettingsBackup", "Export settings backup"),
                    Filter = "JSON (*.json)|*.json",
                    FileName = "MetadataAISettingsBackup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var backup = new MetaDataIASettingsBackup
                {
                    Settings = Serialization.GetClone(viewModel.Settings)
                };
                backup.Settings.ProtectSecretsForStorage();

                File.WriteAllText(dialog.FileName, Serialization.ToJson(backup, true));
                MessageBox.Show(Loc("MTDA_BackupExported", "Settings backup exported."), PluginTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc("MTDA_BackupExportFailed", "Could not export settings backup.") + "\n\n" + MetadataGenerationService.SanitizeForUser(ex.Message), PluginTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ImportSettingsBackup_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null)
            {
                return;
            }

            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = Loc("MTDA_ImportSettingsBackup", "Import settings backup"),
                    Filter = "JSON (*.json)|*.json"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                MetaDataIASettingsBackup backup;
                if (!Serialization.TryFromJsonFile(dialog.FileName, out backup) ||
                    backup == null ||
                    backup.Settings == null)
                {
                    MessageBox.Show(Loc("MTDA_BackupImportInvalid", "The selected backup file is not valid."), PluginTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var secretsRestored = backup.Settings.UnprotectSecretsAfterLoad();

                var confirm = MessageBox.Show(
                    Loc("MTDA_BackupImportConfirm", "Importing this backup will replace the current Metadata AI settings and save them immediately. Continue?"),
                    PluginTitle,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }

                viewModel.ReplaceSettingsFromBackup(backup.Settings);
                LoadPasswordBoxes(viewModel.Settings);
                MessageBox.Show(Loc("MTDA_BackupImported", "Settings backup imported."), PluginTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                if (!secretsRestored)
                {
                    MessageBox.Show(
                        Loc("MTDA_BackupSecretsUnavailable", "Some API credentials could not be restored because this backup was encrypted for another Windows user or computer. Enter those credentials again."),
                        PluginTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc("MTDA_BackupImportFailed", "Could not import settings backup.") + "\n\n" + MetadataGenerationService.SanitizeForUser(ex.Message), PluginTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CleanupObsoleteMedia_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null || viewModel.Plugin == null || viewModel.Plugin.Api == null)
            {
                return;
            }

            try
            {
                var api = viewModel.Plugin.Api;
                var scan = MediaStorageCleanupService.Scan(api);
                if (scan.FileCount == 0)
                {
                    api.Dialogs.ShowMessage(Loc("MTDA_CleanupObsoleteMediaNone", "No obsolete media files were found."), PluginTitle);
                    return;
                }

                var confirmMessage = string.Format(
                    Loc("MTDA_CleanupObsoleteMediaConfirm", "Metadata AI found {0} unreferenced image file(s), using {1}. These files are not used as a cover, icon, or background by any Playnite game. Delete them?"),
                    scan.FileCount,
                    FormatFileSize(scan.TotalBytes));
                var confirm = api.Dialogs.ShowMessage(confirmMessage, PluginTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }

                var removed = MediaStorageCleanupService.Delete(api, scan);
                api.Dialogs.ShowMessage(
                    string.Format(
                        Loc("MTDA_CleanupObsoleteMediaCompleted", "Cleanup completed. Removed files: {0}. Recovered space: {1}. Files that could not be removed: {2}."),
                        removed.FileCount,
                        FormatFileSize(removed.TotalBytes),
                        removed.FailedCount),
                    PluginTitle);
            }
            catch (Exception ex)
            {
                viewModel.Plugin.Api.Dialogs.ShowErrorMessage(
                    Loc("MTDA_CleanupObsoleteMediaFailed", "Could not clean obsolete media files.") + "\n\n" + MetadataGenerationService.SanitizeForUser(ex.Message),
                    PluginTitle);
            }
        }

        private void ExportDiagnostics_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null || viewModel.Settings == null || viewModel.Plugin == null || viewModel.Plugin.Api == null)
            {
                return;
            }

            try
            {
                viewModel.SyncSelectedTemplate();
                var dialog = new SaveFileDialog
                {
                    Title = Loc("MTDA_ExportDiagnostics", "Export diagnostics"),
                    Filter = "Text (*.txt)|*.txt",
                    FileName = "MetadataAI_Diagnostics_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                File.WriteAllText(dialog.FileName, BuildDiagnosticsReport(viewModel), Encoding.UTF8);
                MessageBox.Show(Loc("MTDA_DiagnosticsExported", "Diagnostics report exported."), PluginTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc("MTDA_DiagnosticsExportFailed", "Could not export diagnostics report.") + "\n\n" + MetadataGenerationService.SanitizeForUser(ex.Message), PluginTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string BuildDiagnosticsReport(MetaDataIASettingsViewModel viewModel)
        {
            var settings = viewModel.Settings;
            var api = viewModel.Plugin.Api;
            var games = api.Database.Games.GetClone().ToList();
            var builder = new StringBuilder();
            builder.AppendLine("Metadata AI diagnostics");
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("Plugin version: " + typeof(MetaDataIAPlugin).Assembly.GetName().Version);
            builder.AppendLine("Playnite mode: " + (api.ApplicationInfo == null ? "Unknown" : api.ApplicationInfo.Mode.ToString()));
            builder.AppendLine("Configuration path: " + (api.Paths == null ? string.Empty : api.Paths.ConfigurationPath));
            builder.AppendLine();

            builder.AppendLine("AI provider");
            builder.AppendLine("- Provider: " + settings.ProviderPreset);
            builder.AppendLine("- Endpoint: " + settings.Endpoint);
            builder.AppendLine("- Model: " + settings.Model);
            builder.AppendLine("- Output language: " + settings.Language);
            builder.AppendLine("- Configured: " + settings.IsConfigured);
            builder.AppendLine("- Strict factual mode: " + settings.StrictCompanyAgeRegion);
            builder.AppendLine("- Official/source context: " + settings.UseOfficialStoreContext);
            builder.AppendLine("- Origin integration as AI context: " + settings.UseOriginIntegrationAsAiContext);
            builder.AppendLine("- Origin factual fields in strict mode: " + settings.UseOriginIntegrationForFactualMetadata);
            builder.AppendLine();

            builder.AppendLine("Enabled metadata fields");
            builder.AppendLine("- Description: " + settings.GenerateDescription + " / " + LocalizeApplyMode(settings.DescriptionApplyMode));
            builder.AppendLine("- Genres: " + settings.GenerateGenres + " / " + LocalizeApplyMode(settings.GenresApplyMode) + " / max " + settings.MaxGenres + " / existing only " + settings.PreferExistingGenres);
            builder.AppendLine("- Tags: " + settings.GenerateTags + " / " + LocalizeApplyMode(settings.TagsApplyMode) + " / max " + settings.MaxTags + " / existing only " + settings.PreferExistingTags);
            builder.AppendLine("- Features: " + settings.GenerateFeatures + " / " + LocalizeApplyMode(settings.FeaturesApplyMode) + " / max " + settings.MaxFeatures + " / existing only " + settings.PreferExistingFeatures);
            builder.AppendLine("- Taxonomy: " + settings.TaxonomyPreset + " / controlled genres " + settings.UseControlledGenreVocabulary + " / controlled features " + settings.UseControlledFeatureVocabulary + " / primary tags " + settings.UsePrimaryTagClassification);
            builder.AppendLine("- Categories: " + settings.GenerateCategories + " / " + LocalizeApplyMode(settings.CategoriesApplyMode) + " / max " + settings.MaxCategories + " / existing only " + settings.PreferExistingCategories);
            builder.AppendLine("- Developers: " + settings.GenerateDevelopers + " / " + LocalizeApplyMode(settings.DevelopersApplyMode) + " / max " + settings.MaxDevelopers);
            builder.AppendLine("- Publishers: " + settings.GeneratePublishers + " / " + LocalizeApplyMode(settings.PublishersApplyMode) + " / max " + settings.MaxPublishers);
            builder.AppendLine("- Age ratings: " + settings.GenerateAgeRatings + " / " + LocalizeApplyMode(settings.AgeRatingsApplyMode) + " / existing only " + settings.PreferExistingAgeRatings);
            builder.AppendLine("- Regions: " + settings.GenerateRegions + " / " + LocalizeApplyMode(settings.RegionsApplyMode));
            builder.AppendLine("- Release date: " + settings.GenerateReleaseDate + " / " + LocalizeApplyMode(settings.ReleaseDateApplyMode));
            builder.AppendLine("- Series: " + settings.GenerateSeries + " / " + LocalizeApplyMode(settings.SeriesApplyMode));
            builder.AppendLine("- Sorting name: " + settings.GenerateSortingName + " / " + LocalizeApplyMode(settings.SortingNameApplyMode));
            builder.AppendLine("- Links: " + settings.GenerateLinks + " / " + LocalizeApplyMode(settings.LinksApplyMode));
            builder.AppendLine();

            builder.AppendLine("Media");
            builder.AppendLine("- Cover enabled: " + settings.DownloadCoverImage + " / " + settings.CoverImageApplyMode + " / " + settings.CoverImagePreset);
            builder.AppendLine("- Icon enabled: " + settings.DownloadIcon + " / " + settings.IconApplyMode + " / " + settings.IconPreset);
            builder.AppendLine("- Background enabled: " + settings.DownloadBackgroundImage + " / " + settings.BackgroundImageApplyMode + " / " + settings.BackgroundImagePreset);
            builder.AppendLine("- Automatic priority: " + settings.MediaAutomaticPriority);
            builder.AppendLine("- Processed image quality: " + settings.ProcessedImageQuality);
            builder.AppendLine("- Minimum quality enabled: " + settings.MediaMinimumQualityEnabled);
            builder.AppendLine("- Minimum cover width: " + settings.MediaMinimumCoverWidth);
            builder.AppendLine("- Minimum icon width: " + settings.MediaMinimumIconWidth);
            builder.AppendLine("- Minimum background width: " + settings.MediaMinimumBackgroundWidth);
            builder.AppendLine("- Repair only when better: " + settings.MediaRepairOnlyWhenBetter);
            builder.AppendLine("- Prefer official: " + settings.MediaPreferOfficial);
            builder.AppendLine("- Avoid NSFW: " + settings.MediaAvoidNsfw);
            builder.AppendLine("- Avoid blurred: " + settings.MediaAvoidBlurred);
            builder.AppendLine("- Avoid console covers: " + settings.MediaAvoidConsoleCovers);
            builder.AppendLine("- Source integration media: " + settings.UseOriginIntegrationForMedia);
            builder.AppendLine("- Steam official: " + settings.MediaUseSteamOfficial);
            builder.AppendLine("- Steam screenshots: " + settings.MediaUseSteamScreenshots);
            builder.AppendLine("- SteamGridDB: " + settings.MediaUseSteamGridDb + " / key set " + HasValue(settings.SteamGridDbApiKey));
            builder.AppendLine("- RAWG: " + settings.MediaUseRawg + " / key set " + HasValue(settings.RawgApiKey));
            builder.AppendLine("- Wallhaven: " + settings.MediaUseWallhaven + " / SFW backgrounds only");
            builder.AppendLine("- ScreenScraper: " + settings.MediaUseScreenScraper + " / account set " + HasValue(settings.ScreenScraperUserName) + " / developer credentials set " + (!string.IsNullOrWhiteSpace(settings.ScreenScraperDeveloperId) && !string.IsNullOrWhiteSpace(settings.ScreenScraperDeveloperPassword) ? "yes" : "no"));
            builder.AppendLine("- Giant Bomb: " + settings.MediaUseGiantBomb + " / key set " + HasValue(settings.GiantBombApiKey));
            builder.AppendLine("- MobyGames: " + settings.MediaUseMobyGames + " / key set " + HasValue(settings.MobyGamesApiKey));
            builder.AppendLine("- IGDB: " + settings.MediaUseIgdb + " / client id set " + HasValue(settings.IgdbClientId) + " / secret set " + HasValue(settings.IgdbClientSecret) + " / access token set " + HasValue(settings.IgdbAccessToken));
            builder.AppendLine("- PS Store: " + settings.MediaUsePsnStore);
            builder.AppendLine("- Xbox Store: " + settings.MediaUseXboxStore);
            builder.AppendLine("- Epic Store: " + settings.MediaUseEpicStore);
            builder.AppendLine("- Cover source priority: " + settings.MediaCoverSourcePriority);
            builder.AppendLine("- Icon source priority: " + settings.MediaIconSourcePriority);
            builder.AppendLine("- Background source priority: " + settings.MediaBackgroundSourcePriority);
            builder.AppendLine();

            builder.AppendLine("Library");
            builder.AppendLine("- Games: " + games.Count);
            builder.AppendLine("- Hidden games: " + games.Count(x => x.Hidden));
            builder.AppendLine("- Games without cover: " + games.Count(x => string.IsNullOrWhiteSpace(x.CoverImage)));
            builder.AppendLine("- Games without icon: " + games.Count(x => string.IsNullOrWhiteSpace(x.Icon)));
            builder.AppendLine("- Games without background: " + games.Count(x => string.IsNullOrWhiteSpace(x.BackgroundImage)));
            builder.AppendLine("- Games without description: " + games.Count(x => string.IsNullOrWhiteSpace(x.Description)));
            builder.AppendLine();
            builder.AppendLine("Secrets are intentionally omitted. Only whether a credential is set is reported.");
            return builder.ToString();
        }

        private static string LocalizeApplyMode(string mode)
        {
            switch (mode)
            {
                case MetaDataIASettings.ApplySkip: return "Skip";
                case MetaDataIASettings.ApplyEmptyOnly: return "Fill if empty";
                case MetaDataIASettings.ApplyAppend: return "Append";
                case MetaDataIASettings.ApplyOverwrite: return "Overwrite";
                default: return mode ?? string.Empty;
            }
        }

        private static string HasValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "no" : "yes";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return (bytes / (1024d * 1024d * 1024d)).ToString("0.##") + " GB";
            }

            if (bytes >= 1024L * 1024L)
            {
                return (bytes / (1024d * 1024d)).ToString("0.##") + " MB";
            }

            if (bytes >= 1024L)
            {
                return (bytes / 1024d).ToString("0.##") + " KB";
            }

            return bytes + " B";
        }

        private async void ApplyProvider_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                CancelProviderModelsRefresh();
                var selectedProvider = viewModel.Settings.ProviderPreset;
                var previousModel = viewModel.Settings.Model;
                var preserveCustomModel = string.Equals(
                    lastAppliedProviderPreset,
                    selectedProvider,
                    StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(previousModel);

                viewModel.Settings.ApplyProviderPreset();
                if (preserveCustomModel)
                {
                    viewModel.Settings.Model = previousModel;
                }

                lastAppliedProviderPreset = selectedProvider;
                var appliedModel = viewModel.Settings.Model;
                providerModelIds.Clear();
                AddCurrentProviderModel(appliedModel);
                viewModel.Settings.Model = appliedModel;
                ProviderModelComboBox.Text = appliedModel ?? string.Empty;
                LoadPasswordBoxes(viewModel.Settings);
                RefreshProviderUsageDisplay(null);
                await RefreshProviderModelsAsync(false);
            }
        }

        private void RestoreEndpoint_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Settings.RestoreProviderEndpoint();
            }
        }

        private void OpenProviderPage_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null || string.IsNullOrWhiteSpace(viewModel.Settings.ProviderKeyUrl))
            {
                return;
            }

            Process.Start(new ProcessStartInfo(viewModel.Settings.ProviderKeyUrl));
        }

        private void ProviderPreset_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => RefreshProviderUsageDisplay(null)));
        }

        private async void RefreshProviderModels_OnClick(object sender, RoutedEventArgs e)
        {
            await RefreshProviderModelsAsync(true);
        }

        private async Task RefreshProviderModelsAsync(bool manual)
        {
            if (providerModelsRefreshActive || ProviderModelComboBox == null || ProviderModelsStatusText == null)
            {
                return;
            }

            var viewModel = DataContext as MetaDataIASettingsViewModel;
            var settings = viewModel == null ? null : viewModel.Settings;
            if (settings == null)
            {
                return;
            }

            AddCurrentProviderModel(settings.Model);
            if (RequiresApiKeyForModelListing(settings) && string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                ProviderModelsStatusText.Text = Loc("MTDA_ProviderModelsApiKeyRequired", "Enter the provider API key to load its available models.");
                return;
            }

            if (providerModelsRefreshCancellation != null)
            {
                providerModelsRefreshCancellation.Cancel();
                providerModelsRefreshCancellation.Dispose();
            }

            providerModelsRefreshCancellation = new CancellationTokenSource();
            var cancellation = providerModelsRefreshCancellation;
            providerModelsRefreshActive = true;
            RefreshProviderModelsButton.IsEnabled = false;
            ProviderModelsStatusText.Text = Loc("MTDA_ProviderModelsLoading", "Loading available models...");

            try
            {
                var models = await ProviderModelService.GetModelsAsync(settings, cancellation.Token);
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                var configuredModel = settings.Model;
                providerModelIds.Clear();
                foreach (var model in models)
                {
                    providerModelIds.Add(model.Id);
                }

                AddCurrentProviderModel(configuredModel);
                if (!string.Equals(settings.Model, configuredModel, StringComparison.Ordinal))
                {
                    settings.Model = configuredModel;
                }

                ProviderModelComboBox.Text = configuredModel ?? string.Empty;
                ProviderModelsStatusText.Text = models.Count == 0
                    ? Loc("MTDA_ProviderModelsEmpty", "The provider did not return compatible text models. You can still enter one manually.")
                    : string.Format(Loc("MTDA_ProviderModelsLoaded", "{0} compatible models available. You can also enter one manually."), models.Count);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ProviderModelsStatusText.Text = manual
                    ? string.Format(Loc("MTDA_ProviderModelsRefreshFailed", "The model list could not be updated: {0}"), ex.Message)
                    : Loc("MTDA_ProviderModelsUnavailable", "The model list is not available right now. You can enter the model manually.");
            }
            finally
            {
                if (ReferenceEquals(providerModelsRefreshCancellation, cancellation))
                {
                    providerModelsRefreshActive = false;
                    RefreshProviderModelsButton.IsEnabled = true;
                    providerModelsRefreshCancellation.Dispose();
                    providerModelsRefreshCancellation = null;
                }
            }
        }

        private void AddCurrentProviderModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model) || providerModelIds.Any(x => string.Equals(x, model, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            providerModelIds.Insert(0, model.Trim());
        }

        private static bool RequiresApiKeyForModelListing(MetaDataIASettings settings)
        {
            return settings.ProviderPreset == MetaDataIASettings.ProviderOpenAI ||
                   settings.ProviderPreset == MetaDataIASettings.ProviderGemini ||
                   settings.ProviderPreset == MetaDataIASettings.ProviderClaude ||
                   settings.ProviderPreset == MetaDataIASettings.ProviderMistral ||
                   settings.ProviderPreset == MetaDataIASettings.ProviderGroq ||
                   settings.ProviderPreset == MetaDataIASettings.ProviderCerebras;
        }

        private void CancelProviderModelsRefresh()
        {
            if (providerModelsRefreshCancellation != null)
            {
                providerModelsRefreshCancellation.Cancel();
                providerModelsRefreshCancellation.Dispose();
                providerModelsRefreshCancellation = null;
            }

            providerModelsRefreshActive = false;
            if (RefreshProviderModelsButton != null)
            {
                RefreshProviderModelsButton.IsEnabled = true;
            }
        }

        private void OpenProviderUsage_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null || string.IsNullOrWhiteSpace(viewModel.Settings.ProviderUsageUrl))
            {
                return;
            }

            Process.Start(new ProcessStartInfo(viewModel.Settings.ProviderUsageUrl));
        }

        private async void RefreshProviderUsage_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null || providerUsageRefreshActive)
            {
                return;
            }

            providerUsageRefreshActive = true;
            var originalContent = RefreshProviderUsageButton.Content;
            RefreshProviderUsageButton.IsEnabled = false;
            RefreshProviderUsageButton.Content = Loc("MTDA_ProviderUsageRefreshing", "Refreshing...");
            ProviderUsageProgress.Visibility = Visibility.Visible;
            ProviderUsageStatusText.Text = Loc("MTDA_ProviderUsageRefreshing", "Refreshing...");
            providerUsageRefreshCancellation = new CancellationTokenSource();
            providerUsageRefreshCancellation.CancelAfter(TimeSpan.FromSeconds(90));

            try
            {
                if (ProviderUsageService.IsLocalProvider(viewModel.Settings))
                {
                    ProviderUsageService.CreateLocalSnapshot(viewModel.Settings);
                    RefreshProviderUsageDisplay(null);
                    return;
                }

                if (ProviderUsageService.UsesDashboardOnly(viewModel.Settings))
                {
                    RefreshProviderUsageDisplay(Loc(
                        "MTDA_ProviderUsageDashboardOnly",
                        "This provider does not expose a portable remaining-quota value to the plugin. Open its usage page for the current account limits."));
                    return;
                }

                if (ProviderUsageService.SupportsDirectRefresh(viewModel.Settings))
                {
                    await ProviderUsageService.RefreshOpenRouterAsync(
                        viewModel.Settings,
                        providerUsageRefreshCancellation.Token);
                }
                else
                {
                    var testSettings = CreateProviderProbeSettings(viewModel.Settings);
                    await new MetadataGenerationService(testSettings, viewModel.Plugin.Api).GenerateAsync(
                        new Game { Name = "Pong" },
                        providerUsageRefreshCancellation.Token);
                }

                var snapshot = ProviderUsageService.GetCached(viewModel.Settings);
                RefreshProviderUsageDisplay(snapshot != null && snapshot.HasLimitData
                    ? Loc("MTDA_ProviderUsageAvailable", "Current provider limits were updated.")
                    : Loc("MTDA_ProviderUsageUnavailable", "The provider did not return usage or limit information."));
            }
            catch (OperationCanceledException)
            {
                RefreshProviderUsageDisplay(Loc(
                    "MTDA_ProviderUsageTimedOut",
                    "The usage query did not finish within 90 seconds."));
            }
            catch (Exception ex)
            {
                RefreshProviderUsageDisplay(MetadataGenerationService.SanitizeForUser(ex.Message));
            }
            finally
            {
                if (providerUsageRefreshCancellation != null)
                {
                    providerUsageRefreshCancellation.Dispose();
                    providerUsageRefreshCancellation = null;
                }

                providerUsageRefreshActive = false;
                ProviderUsageProgress.Visibility = Visibility.Collapsed;
                RefreshProviderUsageButton.IsEnabled = true;
                RefreshProviderUsageButton.Content = originalContent;
            }
        }

        private void RefreshProviderUsageDisplay(string statusOverride)
        {
            if (ProviderUsageStatusText == null || ProviderUsageDetailsText == null ||
                ProviderUsageUpdatedText == null || OpenProviderUsageButton == null)
            {
                return;
            }

            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null)
            {
                return;
            }

            var settings = viewModel.Settings;
            var snapshot = ProviderUsageService.GetCached(settings);
            var exposesQuota = !ProviderUsageService.IsLocalProvider(settings) &&
                               !ProviderUsageService.UsesDashboardOnly(settings);

            if (ProviderUsageActionsPanel != null)
            {
                ProviderUsageActionsPanel.Visibility = exposesQuota ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ProviderUsageProbeHelpText != null)
            {
                ProviderUsageProbeHelpText.Visibility = exposesQuota ? Visibility.Visible : Visibility.Collapsed;
            }

            OpenProviderUsageButton.Visibility = exposesQuota && !string.IsNullOrWhiteSpace(settings.ProviderUsageUrl)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (RefreshProviderUsageButton != null)
            {
                RefreshProviderUsageButton.Visibility = exposesQuota ? Visibility.Visible : Visibility.Collapsed;
            }

            if (!string.IsNullOrWhiteSpace(statusOverride))
            {
                ProviderUsageStatusText.Text = statusOverride;
            }
            else if (ProviderUsageService.IsLocalProvider(settings))
            {
                ProviderUsageStatusText.Text = Loc(
                    "MTDA_ProviderUsageLocal",
                    "Local provider: there is no external API quota. Availability depends on your PC and the local server.");
            }
            else if (ProviderUsageService.UsesDashboardOnly(settings))
            {
                ProviderUsageStatusText.Text = Loc(
                    "MTDA_ProviderUsageDashboardOnly",
                    "This provider does not expose a portable remaining-quota value to the plugin.");
            }
            else if (snapshot == null)
            {
                ProviderUsageStatusText.Text = Loc(
                    "MTDA_ProviderUsageUnknown",
                    "No usage information has been received yet.");
            }
            else
            {
                ProviderUsageStatusText.Text = Loc(
                    "MTDA_ProviderUsageAvailable",
                    "Current provider limits were updated.");
            }

            ProviderUsageDetailsText.Text = BuildProviderUsageDetails(settings, snapshot);
            ProviderUsageUpdatedText.Text = snapshot == null
                ? string.Empty
                : string.Format(
                    Loc("MTDA_ProviderUsageUpdated", "Last updated: {0}"),
                    snapshot.UpdatedAtUtc.ToLocalTime().ToString("g"));
        }

        private static string BuildProviderUsageDetails(MetaDataIASettings settings, ProviderUsageSnapshot snapshot)
        {
            if (snapshot == null || snapshot.IsLocal)
            {
                return string.Empty;
            }

            var lines = new List<string>();
            AddUsageLine(lines, Loc("MTDA_ProviderUsageRequests", "Requests"), snapshot.RequestsRemaining, snapshot.RequestsLimit, snapshot.RequestsReset);
            AddUsageLine(lines, Loc("MTDA_ProviderUsageTokens", "Tokens"), snapshot.TokensRemaining, snapshot.TokensLimit, snapshot.TokensReset);
            AddUsageLine(lines, Loc("MTDA_ProviderUsageInputTokens", "Input tokens"), snapshot.InputTokensRemaining, snapshot.InputTokensLimit, snapshot.InputTokensReset);
            AddUsageLine(lines, Loc("MTDA_ProviderUsageOutputTokens", "Output tokens"), snapshot.OutputTokensRemaining, snapshot.OutputTokensLimit, snapshot.OutputTokensReset);
            AddUsageLine(lines, Loc("MTDA_ProviderUsageCredits", "Credits"), snapshot.CreditsRemaining, snapshot.CreditsLimit, null);

            if (!string.IsNullOrWhiteSpace(snapshot.UsageDaily))
            {
                lines.Add(string.Format(Loc("MTDA_ProviderUsageDaily", "Used today: {0}"), snapshot.UsageDaily));
            }

            if (!string.IsNullOrWhiteSpace(snapshot.UsageMonthly))
            {
                lines.Add(string.Format(Loc("MTDA_ProviderUsageMonthly", "Used this month: {0}"), snapshot.UsageMonthly));
            }

            if (!string.IsNullOrWhiteSpace(snapshot.RetryAfter))
            {
                lines.Add(string.Format(Loc("MTDA_ProviderUsageRetryAfter", "Retry after: {0}"), snapshot.RetryAfter));
            }

            if ((settings.ProviderPreset == MetaDataIASettings.ProviderOpenRouter ||
                 settings.ProviderPreset == MetaDataIASettings.ProviderOpenRouterFree) && snapshot.IsFreeTier)
            {
                lines.Add(Loc(
                    "MTDA_ProviderUsageOpenRouterFreeNote",
                    "OpenRouter identifies this as a free-tier key, but its key endpoint does not report the exact number of free requests remaining today."));
            }

            return lines.Count == 0
                ? Loc("MTDA_ProviderUsageNoHeaders", "No numerical limits were included in the latest provider response.")
                : string.Join(Environment.NewLine, lines);
        }

        private static void AddUsageLine(
            ICollection<string> lines,
            string label,
            string remaining,
            string limit,
            string reset)
        {
            if (string.IsNullOrWhiteSpace(remaining) && string.IsNullOrWhiteSpace(limit))
            {
                return;
            }

            var value = !string.IsNullOrWhiteSpace(remaining) && !string.IsNullOrWhiteSpace(limit)
                ? remaining + " / " + limit
                : (!string.IsNullOrWhiteSpace(remaining) ? remaining : limit);
            if (!string.IsNullOrWhiteSpace(reset))
            {
                value += " (" + string.Format(Loc("MTDA_ProviderUsageReset", "reset: {0}"), reset) + ")";
            }

            lines.Add(label + ": " + value);
        }

        private void OpenSteamGridDbPage_OnClick(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://www.steamgriddb.com/profile/preferences"));
        }

        private void RefreshOriginIntegrations_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.RefreshOriginLibraryIntegrations();
            }
        }

        private void OpenRepository_OnClick(object sender, RoutedEventArgs e)
        {
            OpenExternalUrl("https://github.com/Naerian/playnite-nx-metadata-ia");
        }

        private void OpenWiki_OnClick(object sender, RoutedEventArgs e)
        {
            OpenExternalUrl("https://github.com/Naerian/playnite-nx-metadata-ia/wiki");
        }

        private void OpenReportIssue_OnClick(object sender, RoutedEventArgs e)
        {
            OpenExternalUrl("https://github.com/Naerian/playnite-nx-metadata-ia/issues/new/choose");
        }

        private void OpenKoFi_OnClick(object sender, RoutedEventArgs e)
        {
            OpenExternalUrl("https://ko-fi.com/naerian");
        }

        private static void OpenExternalUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url));
        }

        private void TestMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "fuentes activas", s => { });
        }

        private void TestSteamMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "Steam oficial", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseSteamOfficial = true;
                s.MediaUseSteamScreenshots = true;
            });
        }

        private void TestSteamGridDbMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "SteamGridDB", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseSteamGridDb = true;
                s.MediaUseSteamGridDbBackgroundGrids = true;
            });
        }

        private void TestPsnMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "PlayStation Store", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUsePsnStore = true;
            });
        }

        private void TestXboxMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "Xbox Store", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseXboxStore = true;
            });
        }

        private void TestEpicMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "Epic Store", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseEpicStore = true;
            });
        }

        private void TestRawgMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "RAWG", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseRawg = true;
            });
        }

        private void TestWallhavenMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "Wallhaven", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseWallhaven = true;
            });
        }

        private void TestScreenScraperMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "ScreenScraper", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseScreenScraper = true;
            });
        }

        private void TestGiantBombMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "Giant Bomb", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseGiantBomb = true;
            });
        }

        private void TestMobyGamesMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "MobyGames", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseMobyGames = true;
            });
        }

        private void TestTheGamesDbMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "TheGamesDB", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseTheGamesDb = true;
            });
        }

        private void TestIgdbMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "IGDB", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseIgdb = true;
            });
        }

        private void TestIgnMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "IGN", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseIgn = true;
            });
        }

        private TestOperationState BeginTestOperation(
            Button triggerButton,
            string targetName,
            Border panel,
            ProgressBar progress,
            TextBlock statusText,
            TextBlock elapsedText,
            Button cancelButton,
            Button copyButton)
        {
            var operation = new TestOperationState
            {
                Cancellation = new CancellationTokenSource(),
                Stopwatch = Stopwatch.StartNew(),
                Panel = panel,
                Progress = progress,
                StatusText = statusText,
                ElapsedText = elapsedText,
                CancelButton = cancelButton,
                CopyButton = copyButton,
                TriggerButton = triggerButton,
                OriginalButtonContent = triggerButton == null ? null : triggerButton.Content,
                TargetName = targetName
            };

            if (triggerButton != null)
            {
                triggerButton.IsEnabled = false;
                triggerButton.Content = Loc("MTDA_Testing", "Testing...");
            }

            panel.Visibility = Visibility.Visible;
            progress.Visibility = Visibility.Visible;
            progress.IsIndeterminate = true;
            cancelButton.Visibility = Visibility.Visible;
            cancelButton.IsEnabled = true;
            if (copyButton != null)
            {
                copyButton.Visibility = Visibility.Collapsed;
            }
            statusText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            statusText.Text = string.Format(Loc("MTDA_TestSending", "Sending a test request to {0}..."), targetName);
            elapsedText.Text = FormatTestElapsed(operation.Stopwatch.Elapsed);

            operation.Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            operation.Timer.Tick += (sender, args) =>
            {
                if (operation.Cancellation.IsCancellationRequested)
                {
                    return;
                }

                var elapsed = operation.Stopwatch.Elapsed;
                operation.ElapsedText.Text = FormatTestElapsed(elapsed);
                if (elapsed.TotalSeconds >= 90)
                {
                    operation.TimedOut = true;
                    operation.CancelButton.IsEnabled = false;
                    operation.StatusText.Text = Loc("MTDA_TestTimedOut", "The provider or source did not respond within 90 seconds. It may be busy or unavailable.");
                    operation.Cancellation.Cancel();
                }
                else if (elapsed.TotalSeconds >= 5)
                {
                    operation.StatusText.Text = string.Format(
                        Loc("MTDA_TestWaiting", "Waiting for {0} to respond. Free or busy services may take longer."),
                        operation.TargetName);
                }
            };
            operation.Timer.Start();
            return operation;
        }

        private static string FormatTestElapsed(TimeSpan elapsed)
        {
            return string.Format(Loc("MTDA_TestElapsed", "Elapsed: {0} s"), Math.Max(0, (int)elapsed.TotalSeconds));
        }

        private static void FinishTestOperation(TestOperationState operation, string message, bool success, string technicalDetails = null)
        {
            if (operation == null)
            {
                return;
            }

            operation.Timer.Stop();
            operation.Stopwatch.Stop();
            operation.Progress.IsIndeterminate = false;
            operation.Progress.Visibility = Visibility.Collapsed;
            operation.CancelButton.Visibility = Visibility.Collapsed;
            operation.StatusText.SetResourceReference(TextBlock.ForegroundProperty, success ? "PositiveRatingBrush" : "WarningBrush");
            operation.StatusText.Text = message;
            operation.ElapsedText.Text = FormatTestElapsed(operation.Stopwatch.Elapsed);
            operation.TechnicalDetails = technicalDetails;
            if (operation.CopyButton != null)
            {
                operation.CopyButton.Visibility = string.IsNullOrWhiteSpace(technicalDetails)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            if (operation.TriggerButton != null)
            {
                operation.TriggerButton.Content = operation.OriginalButtonContent;
                operation.TriggerButton.IsEnabled = true;
            }

            operation.Cancellation.Dispose();
        }

        private static void CancelTestOperation(TestOperationState operation, bool showStatus)
        {
            if (operation == null || operation.Cancellation.IsCancellationRequested)
            {
                return;
            }

            operation.CancelledByUser = true;
            operation.CancelButton.IsEnabled = false;
            if (showStatus)
            {
                operation.StatusText.Text = Loc("MTDA_TestCancelling", "Cancelling test...");
            }

            operation.Cancellation.Cancel();
        }

        private void CancelProviderTest_OnClick(object sender, RoutedEventArgs e)
        {
            CancelTestOperation(providerTestOperation, true);
        }

        private void CancelMediaTest_OnClick(object sender, RoutedEventArgs e)
        {
            CancelTestOperation(mediaTestOperation, true);
        }

        private void CopyProviderTestDetails_OnClick(object sender, RoutedEventArgs e)
        {
            CopyTestDetails(lastProviderTestDetails);
        }

        private void CopyMediaTestDetails_OnClick(object sender, RoutedEventArgs e)
        {
            CopyTestDetails(lastMediaTestDetails);
        }

        private static void CopyTestDetails(string details)
        {
            if (!string.IsNullOrWhiteSpace(details))
            {
                Clipboard.SetText(details);
            }
        }

        private void CopyToken_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var token = button == null ? null : button.Tag as string;
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            try
            {
                Clipboard.SetText(token);
            }
            catch (System.Exception)
            {
            }
        }

        private static string BuildTechnicalDetails(System.Exception ex)
        {
            var providerException = ex as AiProviderException;
            var builder = new StringBuilder();
            builder.AppendLine(ex.GetType().FullName);
            builder.AppendLine(ex.Message);
            if (providerException != null && !string.IsNullOrWhiteSpace(providerException.TechnicalDetails))
            {
                builder.AppendLine();
                builder.AppendLine(providerException.TechnicalDetails);
            }
            return builder.ToString().Trim();
        }

        private async void TestMediaSource(Button button, string sourceName, System.Action<MetaDataIASettings> configure)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null || mediaTestOperation != null)
            {
                return;
            }

            MoveMediaTestPanelToSource(button);

            var operation = BeginTestOperation(
                button,
                sourceName,
                MediaTestStatusPanel,
                MediaTestProgress,
                MediaTestStatusText,
                MediaTestElapsedText,
                MediaTestCancelButton,
                MediaTestCopyButton);
            mediaTestOperation = operation;
            lastMediaTestDetails = null;

            try
            {
                var testSettings = Serialization.GetClone(viewModel.Settings);
                testSettings.DownloadBackgroundImage = true;
                testSettings.BackgroundImageApplyMode = MetaDataIASettings.ApplyOverwrite;
                if (configure != null)
                {
                    configure(testSettings);
                }

                if (!string.Equals(sourceName, "fuentes activas", StringComparison.OrdinalIgnoreCase))
                {
                    testSettings.MediaCoverSourcePriority = sourceName;
                    testSettings.MediaIconSourcePriority = sourceName;
                    testSettings.MediaBackgroundSourcePriority = string.Equals(sourceName, "Steam oficial", StringComparison.OrdinalIgnoreCase)
                        ? "Steam oficial, Steam capturas"
                        : sourceName;
                }

                var service = new MediaGenerationService(testSettings);
                var testGame = CreateMediaTestGame(sourceName);
                var coverCount = await service.CountPreviewOptionsAsync(testGame, MediaKind.Cover, operation.Cancellation.Token);
                var iconCount = await service.CountPreviewOptionsAsync(testGame, MediaKind.Icon, operation.Cancellation.Token);
                var backgroundCount = await service.CountPreviewOptionsAsync(testGame, MediaKind.Background, operation.Cancellation.Token);
                operation.Cancellation.Token.ThrowIfCancellationRequested();
                var count = coverCount + iconCount + backgroundCount;

                if (count > 0)
                {
                    FinishTestOperation(
                        operation,
                        string.Format(Loc("MTDA_TestMediaSuccess", "{0} is responding correctly.\n\nTest game: {1}\nCovers: {2}\nIcons: {3}\nBackgrounds: {4}"), sourceName, testGame.Name, coverCount, iconCount, backgroundCount),
                        true);
                }
                else
                {
                    FinishTestOperation(operation, BuildNoMediaCandidatesMessage(sourceName, testGame.Name), false);
                }
                RefreshConfigurationSummary();
            }
            catch (OperationCanceledException)
            {
                FinishTestOperation(
                    operation,
                    operation.TimedOut
                        ? Loc("MTDA_TestTimedOut", "The provider or source did not respond within 90 seconds. It may be busy or unavailable.")
                        : Loc("MTDA_TestCancelled", "Test cancelled."),
                    false);
            }
            catch (System.Exception ex)
            {
                var message = MetadataGenerationService.SanitizeForUser(ex.Message);
                lastMediaTestDetails = BuildTechnicalDetails(ex);
                FinishTestOperation(operation, message, false, lastMediaTestDetails);
                RefreshConfigurationSummary();
            }
            finally
            {
                if (ReferenceEquals(mediaTestOperation, operation))
                {
                    mediaTestOperation = null;
                }
            }
        }

        private void MoveMediaTestPanelToSource(Button triggerButton)
        {
            var expander = FindVisualAncestor<Expander>(triggerButton);
            var targetPanel = expander == null ? null : expander.Content as Panel;
            if (targetPanel == null || targetPanel.Children.Contains(MediaTestStatusPanel))
            {
                return;
            }

            var currentParent = VisualTreeHelper.GetParent(MediaTestStatusPanel) as Panel;
            if (currentParent != null)
            {
                currentParent.Children.Remove(MediaTestStatusPanel);
            }
            targetPanel.Children.Add(MediaTestStatusPanel);
        }

        private static T FindVisualAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            var current = element;
            while (current != null)
            {
                var match = current as T;
                if (match != null)
                {
                    return match;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static Game CreateMediaTestGame(string sourceName)
        {
            if (string.Equals(sourceName, "Xbox Store", StringComparison.OrdinalIgnoreCase))
            {
                return new Game { Name = "Halo Infinite" };
            }

            if (string.Equals(sourceName, "PlayStation Store", StringComparison.OrdinalIgnoreCase))
            {
                return new Game { Name = "Astro Bot" };
            }

            if (string.Equals(sourceName, "Epic Store", StringComparison.OrdinalIgnoreCase))
            {
                return new Game
                {
                    Name = "Fortnite",
                    Links = new ObservableCollection<Link>
                    {
                        new Link("Epic Store", "https://store.epicgames.com/p/fortnite")
                    }
                };
            }

            if (string.Equals(sourceName, "Wallhaven", StringComparison.OrdinalIgnoreCase))
            {
                return new Game { Name = "L.A. Noire" };
            }

            if (string.Equals(sourceName, "ScreenScraper", StringComparison.OrdinalIgnoreCase))
            {
                return new Game { Name = "Sonic the Hedgehog" };
            }

            if (string.Equals(sourceName, "Giant Bomb", StringComparison.OrdinalIgnoreCase))
            {
                return new Game { Name = "L.A. Noire" };
            }

            return new Game { Name = "Hades" };
        }

        private static string BuildNoMediaCandidatesMessage(string sourceName, string gameName)
        {
            if (string.Equals(sourceName, "Epic Store", StringComparison.OrdinalIgnoreCase))
            {
                return string.Format(
                    Loc("MTDA_TestMediaNoCandidatesEpic", "{0} did not return media candidates for the test game ({1}). Epic Store is a best-effort source: it usually needs an existing Epic Store link on the game and the website can block automated reads. Keep it enabled as a secondary source, but prefer SteamGridDB, Steam, RAWG, IGDB or MobyGames for reliable automatic media."),
                    sourceName,
                    gameName);
            }

            if (string.Equals(sourceName, "Xbox Store", StringComparison.OrdinalIgnoreCase))
            {
                return string.Format(
                    Loc("MTDA_TestMediaNoCandidatesXbox", "{0} responded, but did not return useful media for the test game ({1}). The Microsoft Store search can be market-dependent and may return no usable images for some titles. This does not necessarily mean the configuration is wrong."),
                    sourceName,
                    gameName);
            }

            return string.Format(
                Loc("MTDA_TestMediaNoCandidates", "{0} responds, but did not return candidates for the test game ({1}). The connection seems to work, but this source did not find useful media with the current criteria."),
                sourceName,
                gameName);
        }

        private static void DisableAllMediaSources(MetaDataIASettings settings)
        {
            settings.MediaUseSteamOfficial = false;
            settings.MediaUseSteamScreenshots = false;
            settings.MediaUsePsnStore = false;
            settings.MediaUseXboxStore = false;
            settings.MediaUseEpicStore = false;
            settings.MediaUseSteamGridDb = false;
            settings.MediaUseSteamGridDbBackgroundGrids = false;
            settings.MediaUseRawg = false;
            settings.MediaUseWallhaven = false;
            settings.MediaUseScreenScraper = false;
            settings.MediaUseGiantBomb = false;
            settings.MediaUseMobyGames = false;
            settings.MediaUseTheGamesDb = false;
            settings.MediaUseIgdb = false;
            settings.MediaUseIgn = false;
        }

        private static MetaDataIASettings CreateProviderProbeSettings(MetaDataIASettings source)
        {
            var testSettings = Serialization.GetClone(source);
            testSettings.GenerateDescription = true;
            testSettings.DescriptionApplyMode = MetaDataIASettings.ApplyOverwrite;
            testSettings.GenerateGenres = false;
            testSettings.GenerateTags = false;
            testSettings.GenerateFeatures = false;
            testSettings.GenerateDevelopers = false;
            testSettings.GeneratePublishers = false;
            testSettings.GenerateAgeRatings = false;
            testSettings.GenerateRegions = false;
            testSettings.GenerateCategories = false;
            testSettings.Length = "Corta";
            testSettings.UseOfficialStoreContext = false;
            testSettings.EnableLocalFallback = false;
            testSettings.ExtraInstructions = Loc(
                "MTDA_TestProviderInstruction",
                "Connection test: answer with the minimum possible text.");
            return testSettings;
        }

        private async void TestProvider_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null || providerTestOperation != null)
            {
                return;
            }

            var providerName = string.IsNullOrWhiteSpace(viewModel.Settings.ProviderPreset)
                ? Loc("MTDA_Provider", "Provider")
                : viewModel.Settings.ProviderPreset;
            var operation = BeginTestOperation(
                button,
                providerName,
                ProviderTestStatusPanel,
                ProviderTestProgress,
                ProviderTestStatusText,
                ProviderTestElapsedText,
                ProviderTestCancelButton,
                ProviderTestCopyButton);
            providerTestOperation = operation;
            lastProviderTestDetails = null;

            try
            {
                var testSettings = CreateProviderProbeSettings(viewModel.Settings);

                var game = new Game { Name = "Pong" };
                await new MetadataGenerationService(testSettings, viewModel.Plugin.Api).GenerateAsync(game, operation.Cancellation.Token);
                operation.Cancellation.Token.ThrowIfCancellationRequested();
                providerTestSucceeded = true;
                FinishTestOperation(operation, Loc("MTDA_TestProviderSuccess", "The provider is responding correctly."), true);
                RefreshConfigurationSummary();
                RefreshProviderUsageDisplay(null);
            }
            catch (OperationCanceledException)
            {
                FinishTestOperation(
                    operation,
                    operation.TimedOut
                        ? Loc("MTDA_TestTimedOut", "The provider or source did not respond within 90 seconds. It may be busy or unavailable.")
                        : Loc("MTDA_TestCancelled", "Test cancelled."),
                    false);
            }
            catch (System.Exception ex)
            {
                providerTestSucceeded = false;
                var message = MetadataGenerationService.SanitizeForUser(ex.Message);
                lastProviderTestDetails = BuildTechnicalDetails(ex);
                FinishTestOperation(operation, message, false, lastProviderTestDetails);
                RefreshConfigurationSummary();
                RefreshProviderUsageDisplay(null);
            }
            finally
            {
                if (ReferenceEquals(providerTestOperation, operation))
                {
                    providerTestOperation = null;
                }
            }
        }

        private void ConfigureCoverSourcePriority_OnClick(object sender, RoutedEventArgs e)
        {
            ConfigureSourcePriority(sender as FrameworkElement, MediaKind.Cover);
        }

        private void ConfigureIconSourcePriority_OnClick(object sender, RoutedEventArgs e)
        {
            ConfigureSourcePriority(sender as FrameworkElement, MediaKind.Icon);
        }

        private void ConfigureBackgroundSourcePriority_OnClick(object sender, RoutedEventArgs e)
        {
            ConfigureSourcePriority(sender as FrameworkElement, MediaKind.Background);
        }

        private void ConfigureSourcePriority(FrameworkElement target, MediaKind kind)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null || viewModel.Settings == null || target == null)
            {
                return;
            }

            if (WasSameSourcePriorityPopupJustClosed(target, kind))
            {
                recentlyClosedSourcePriorityPopupTarget = null;
                recentlyClosedSourcePriorityPopupKind = null;
                recentlyClosedSourcePriorityPopupAt = DateTime.MinValue;
                return;
            }

            if (sourcePriorityPopup != null)
            {
                if (sourcePriorityPopup.IsOpen && ReferenceEquals(sourcePriorityPopupTarget, target) && sourcePriorityPopupKind == kind)
                {
                    sourcePriorityPopup.IsOpen = false;
                    sourcePriorityPopup = null;
                    sourcePriorityPopupTarget = null;
                    sourcePriorityPopupKind = null;
                    return;
                }

                sourcePriorityPopup.IsOpen = false;
                sourcePriorityPopup = null;
                sourcePriorityPopupTarget = null;
                sourcePriorityPopupKind = null;
            }

            var items = BuildSourcePriorityItems(kind, GetSourcePriorityValue(viewModel.Settings, kind), viewModel.Settings);
            Popup popup = null;
            StackPanel listPanel = null;

            System.Action save = () =>
            {
                if (items.Any(x => x.IsEnabled))
                {
                    SetSourcePriorityValue(viewModel.Settings, kind, SerializeSourcePriority(items));
                }
            };

            System.Action rebuild = null;
            rebuild = () =>
            {
                listPanel.Children.Clear();
                if (!items.Any())
                {
                    var emptyText = new TextBlock
                    {
                        Text = Loc("MTDA_NoMediaCandidates", "No hay fuentes activas para este tipo de media."),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    emptyText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                    listPanel.Children.Add(emptyText);
                    return;
                }

                foreach (var item in items)
                {
                    listPanel.Children.Add(CreateSourcePriorityPopupRow(item, items, kind, save, rebuild));
                }
            };

            var root = new StackPanel
            {
                MinWidth = 300,
                MaxWidth = 380,
                Margin = new Thickness(10)
            };
            var title = new TextBlock
            {
                Text = string.Format(Loc("MTDA_SourcePriorityDialogTitle", "{0} source priority"), MediaKindLabel(kind)),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            root.Children.Add(title);

            var help = new TextBlock
            {
                Text = Loc("MTDA_SourcePriorityDialogHelp", "Enable the sources you want to use and move them up or down. The first enabled source has the highest priority."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            help.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            root.Children.Add(help);

            listPanel = new StackPanel();
            root.Children.Add(listPanel);

            var border = new Border
            {
                Child = root,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                SnapsToDevicePixels = true
            };
            border.Background = SystemColors.WindowBrush;
            border.SetResourceReference(Border.BackgroundProperty, "StandardWindowBackgroundBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "GlyphBrush");

            popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = false,
                Child = border
            };
            popup.Closed += (s, e) =>
            {
                if (ReferenceEquals(sourcePriorityPopup, popup))
                {
                    recentlyClosedSourcePriorityPopupTarget = sourcePriorityPopupTarget;
                    recentlyClosedSourcePriorityPopupKind = sourcePriorityPopupKind;
                    recentlyClosedSourcePriorityPopupAt = DateTime.Now;
                    sourcePriorityPopup = null;
                    sourcePriorityPopupTarget = null;
                    sourcePriorityPopupKind = null;
                }
            };

            rebuild();
            sourcePriorityPopup = popup;
            sourcePriorityPopupTarget = target;
            sourcePriorityPopupKind = kind;
            popup.IsOpen = true;
        }

        private bool WasSameSourcePriorityPopupJustClosed(FrameworkElement target, MediaKind kind)
        {
            return ReferenceEquals(recentlyClosedSourcePriorityPopupTarget, target)
                && recentlyClosedSourcePriorityPopupKind == kind
                && (DateTime.Now - recentlyClosedSourcePriorityPopupAt).TotalMilliseconds < 300;
        }

        private UIElement CreateSourcePriorityPopupRow(SourcePriorityItem item, ObservableCollection<SourcePriorityItem> items, MediaKind kind, System.Action save, System.Action rebuild)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var check = new CheckBox
            {
                Content = item.DisplayName,
                IsChecked = item.IsEnabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            check.Checked += (s, e) =>
            {
                item.IsEnabled = true;
                save();
            };
            check.Unchecked += (s, e) =>
            {
                if (items.Count(x => x.IsEnabled) <= 1)
                {
                    check.IsChecked = true;
                    return;
                }

                item.IsEnabled = false;
                save();
            };
            row.Children.Add(check);

            var up = new Button
            {
                Content = "▲",
                Width = 28,
                Height = 28,
                MinWidth = 28,
                Padding = new Thickness(0),
                FontSize = 9,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            up.Click += (s, e) =>
            {
                MoveSourcePriorityItem(items, item, -1);
                save();
                rebuild();
            };
            Grid.SetColumn(up, 1);
            row.Children.Add(up);

            var down = new Button
            {
                Content = "▼",
                Width = 28,
                Height = 28,
                MinWidth = 28,
                Padding = new Thickness(0),
                FontSize = 9,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            down.Click += (s, e) =>
            {
                MoveSourcePriorityItem(items, item, 1);
                save();
                rebuild();
            };
            Grid.SetColumn(down, 2);
            row.Children.Add(down);

            return row;
        }

        private static DataTemplate CreateSourcePriorityTemplate()
        {
            var template = new DataTemplate(typeof(SourcePriorityItem));
            var checkBox = new FrameworkElementFactory(typeof(CheckBox));
            checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsEnabled") { Mode = BindingMode.TwoWay });
            checkBox.SetBinding(ContentControl.ContentProperty, new Binding("DisplayName"));
            checkBox.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 4));
            template.VisualTree = checkBox;
            return template;
        }

        private static ObservableCollection<SourcePriorityItem> BuildSourcePriorityItems(MediaKind kind, string currentValue, MetaDataIASettings settings)
        {
            var available = GetAvailableSourcePriorityItems(kind)
                .Where(x => IsSourceGloballyActive(settings, x.Key))
                .ToList();
            var current = ParseSourcePriority(currentValue);
            var ordered = new List<SourcePriorityItem>();

            foreach (var source in current)
            {
                var match = available.FirstOrDefault(x => string.Equals(x.Key, source, System.StringComparison.OrdinalIgnoreCase));
                if (match != null && !ordered.Any(x => string.Equals(x.Key, match.Key, System.StringComparison.OrdinalIgnoreCase)))
                {
                    match.IsEnabled = true;
                    ordered.Add(match);
                }
            }

            foreach (var source in available)
            {
                if (!ordered.Any(x => string.Equals(x.Key, source.Key, System.StringComparison.OrdinalIgnoreCase)))
                {
                    source.IsEnabled = false;
                    ordered.Add(source);
                }
            }

            return new ObservableCollection<SourcePriorityItem>(ordered);
        }

        private static bool IsSourceGloballyActive(MetaDataIASettings settings, string source)
        {
            if (settings == null)
            {
                return true;
            }

            if (string.Equals(source, "Steam oficial", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseSteamOfficial;
            }

            if (string.Equals(source, MetaDataIASettings.SourceOriginIntegration, System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.UseOriginIntegrationForMedia;
            }

            if (string.Equals(source, "Steam capturas", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseSteamScreenshots;
            }

            if (string.Equals(source, "SteamGridDB", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseSteamGridDb;
            }

            if (string.Equals(source, "PlayStation Store", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUsePsnStore;
            }

            if (string.Equals(source, "Xbox Store", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseXboxStore;
            }

            if (string.Equals(source, "Epic Store", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseEpicStore;
            }

            if (string.Equals(source, "RAWG", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseRawg;
            }

            if (string.Equals(source, "Wallhaven", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseWallhaven;
            }

            if (string.Equals(source, "ScreenScraper", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseScreenScraper;
            }

            if (string.Equals(source, "Giant Bomb", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseGiantBomb;
            }

            if (string.Equals(source, "MobyGames", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseMobyGames;
            }

            if (string.Equals(source, "IGDB", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseIgdb;
            }

            if (string.Equals(source, MetaDataIASettings.SourceIgn, System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseIgn;
            }

            return true;
        }

        private static List<SourcePriorityItem> GetAvailableSourcePriorityItems(MediaKind kind)
        {
            if (kind == MediaKind.Icon)
            {
                return new List<SourcePriorityItem>
                {
                    Source("SteamGridDB"),
                    Source(MetaDataIASettings.SourceOriginIntegration),
                    Source("Steam oficial"),
                    Source("PlayStation Store"),
                    Source("Xbox Store"),
                    Source("Epic Store"),
                    Source("ScreenScraper")
                };
            }

            if (kind == MediaKind.Background)
            {
                return new List<SourcePriorityItem>
                {
                    Source(MetaDataIASettings.SourceOriginIntegration),
                    Source("Steam oficial"),
                    Source("Steam capturas"),
                    Source("PlayStation Store"),
                    Source("Xbox Store"),
                    Source("Epic Store"),
                    Source("SteamGridDB"),
                    Source(MetaDataIASettings.SourceIgn),
                    Source("ScreenScraper"),
                    Source("RAWG"),
                    Source("Wallhaven"),
                    Source("IGDB"),
                    Source("Giant Bomb"),
                    Source("MobyGames")
                };
            }

            return new List<SourcePriorityItem>
            {
                Source(MetaDataIASettings.SourceOriginIntegration),
                Source("Steam oficial"),
                Source("PlayStation Store"),
                Source("Xbox Store"),
                Source("Epic Store"),
                Source("SteamGridDB"),
                Source("IGDB"),
                Source(MetaDataIASettings.SourceIgn),
                Source("ScreenScraper"),
                Source("RAWG"),
                Source("Giant Bomb"),
                Source("MobyGames")
            };
        }

        private static SourcePriorityItem Source(string name)
        {
            var displayName = string.Equals(name, MetaDataIASettings.SourceOriginIntegration, System.StringComparison.OrdinalIgnoreCase)
                ? Loc("MTDA_SourceOriginIntegration", "Origin library integration")
                : name;
            return new SourcePriorityItem { Key = name, DisplayName = displayName };
        }

        private static List<string> ParseSourcePriority(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static string SerializeSourcePriority(IEnumerable<SourcePriorityItem> items)
        {
            return string.Join(", ", items.Where(x => x.IsEnabled).Select(x => x.Key));
        }

        private static void MoveSelectedSourcePriorityItem(ListBox list, ObservableCollection<SourcePriorityItem> items, int direction)
        {
            var selected = list == null ? null : list.SelectedItem as SourcePriorityItem;
            if (selected == null)
            {
                return;
            }

            var index = items.IndexOf(selected);
            var newIndex = index + direction;
            if (index < 0 || newIndex < 0 || newIndex >= items.Count)
            {
                return;
            }

            items.Move(index, newIndex);
            list.SelectedItem = selected;
        }

        private static void MoveSourcePriorityItem(ObservableCollection<SourcePriorityItem> items, SourcePriorityItem selected, int direction)
        {
            if (selected == null)
            {
                return;
            }

            var index = items.IndexOf(selected);
            var newIndex = index + direction;
            if (index < 0 || newIndex < 0 || newIndex >= items.Count)
            {
                return;
            }

            items.Move(index, newIndex);
        }

        private static string GetSourcePriorityValue(MetaDataIASettings settings, MediaKind kind)
        {
            if (kind == MediaKind.Cover)
            {
                return settings.MediaCoverSourcePriority;
            }

            if (kind == MediaKind.Icon)
            {
                return settings.MediaIconSourcePriority;
            }

            return settings.MediaBackgroundSourcePriority;
        }

        private static void SetSourcePriorityValue(MetaDataIASettings settings, MediaKind kind, string value)
        {
            if (kind == MediaKind.Cover)
            {
                settings.MediaCoverSourcePriority = value;
            }
            else if (kind == MediaKind.Icon)
            {
                settings.MediaIconSourcePriority = value;
            }
            else
            {
                settings.MediaBackgroundSourcePriority = value;
            }
        }

        private static string MediaKindLabel(MediaKind kind)
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

        private static void ApplyWindowStyle(Window window)
        {
            if (window == null)
            {
                return;
            }

            try
            {
                window.SetResourceReference(FrameworkElement.StyleProperty, "StandardWindowStyle");
                window.SetResourceReference(Control.BackgroundProperty, "StandardWindowBackgroundBrush");
            }
            catch
            {
            }
        }

        private class SourcePriorityItem : ObservableObject
        {
            private bool isEnabled;

            public string Key { get; set; }
            public string DisplayName { get; set; }

            public bool IsEnabled
            {
                get { return isEnabled; }
                set { SetValue(ref isEnabled, value); }
            }
        }

        private static string PluginTitle
        {
            get { return Loc("MTDA_PluginName", "Metadata AI"); }
        }

        private static string Loc(string key, string fallback)
        {
            return PluginLocalization.GetString(key, fallback);
        }

    }
}
