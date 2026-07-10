using System.Windows;
using System.Windows.Controls;
using System;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

        public MetaDataIASettingsView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                var viewModel = DataContext as MetaDataIASettingsViewModel;
                if (viewModel != null)
                {
                    LoadPasswordBoxes(viewModel.Settings);
                }
            };
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
            SetPassword(MobyGamesApiKeyBox, settings.MobyGamesApiKey);
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

        private void MobyGamesApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Settings.MobyGamesApiKey = MobyGamesApiKeyBox.Password;
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
            }
        }

        private void DeleteTemplate_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.DeleteSelectedTemplate();
            }
        }

        private void RestoreTemplates_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.RestoreDefaultTemplates();
            }
        }

        private void ApplyProvider_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Settings.ApplyProviderPreset();
                LoadPasswordBoxes(viewModel.Settings);
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

        private void OpenSteamGridDbPage_OnClick(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://www.steamgriddb.com/profile/preferences"));
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

        private void TestRawgMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "RAWG", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseRawg = true;
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

        private void TestIgdbMedia_OnClick(object sender, RoutedEventArgs e)
        {
            TestMediaSource(sender as Button, "IGDB", s =>
            {
                DisableAllMediaSources(s);
                s.MediaUseIgdb = true;
            });
        }

        private async void TestMediaSource(Button button, string sourceName, System.Action<MetaDataIASettings> configure)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null)
            {
                return;
            }

            try
            {
                if (button != null)
                {
                    button.IsEnabled = false;
                }

                var testSettings = Serialization.GetClone(viewModel.Settings);
                testSettings.DownloadBackgroundImage = true;
                testSettings.BackgroundImageApplyMode = MetaDataIASettings.ApplyOverwrite;
                if (configure != null)
                {
                    configure(testSettings);
                }

                var service = new MediaGenerationService(testSettings);
                var testGame = new Game { Name = "Hades" };
                var coverCount = await service.CountPreviewOptionsAsync(testGame, MediaKind.Cover);
                var iconCount = await service.CountPreviewOptionsAsync(testGame, MediaKind.Icon);
                var backgroundCount = await service.CountPreviewOptionsAsync(testGame, MediaKind.Background);
                var count = coverCount + iconCount + backgroundCount;

                if (count > 0)
                {
                    MessageBox.Show(
                        string.Format(Loc("MTDA_TestMediaSuccess", "{0} is responding correctly.\n\nTest game: {1}\nCovers: {2}\nIcons: {3}\nBackgrounds: {4}"), sourceName, testGame.Name, coverCount, iconCount, backgroundCount),
                        PluginTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(string.Format(Loc("MTDA_TestMediaNoCandidates", "{0} responds, but did not return candidates for the test game ({1}). The connection seems to work, but this source did not find useful media with the current criteria."), sourceName, testGame.Name), PluginTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(MetadataGenerationService.SanitizeForUser(ex.Message), PluginTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (button != null)
                {
                    button.IsEnabled = true;
                }
            }
        }

        private static void DisableAllMediaSources(MetaDataIASettings settings)
        {
            settings.MediaUseSteamOfficial = false;
            settings.MediaUseSteamScreenshots = false;
            settings.MediaUseSteamGridDb = false;
            settings.MediaUseSteamGridDbBackgroundGrids = false;
            settings.MediaUseRawg = false;
            settings.MediaUseMobyGames = false;
            settings.MediaUseIgdb = false;
        }

        private async void TestProvider_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null)
            {
                return;
            }

            try
            {
                if (button != null)
                {
                    button.IsEnabled = false;
                }

                var testSettings = Serialization.GetClone(viewModel.Settings);
                testSettings.GenerateDescription = true;
                testSettings.GenerateGenres = false;
                testSettings.GenerateTags = false;
                testSettings.GenerateFeatures = false;
                testSettings.GenerateDevelopers = false;
                testSettings.GeneratePublishers = false;
                testSettings.GenerateAgeRatings = false;
                testSettings.GenerateRegions = false;
                testSettings.GenerateCategories = false;
                testSettings.Length = "Corta";
                testSettings.EnableLocalFallback = false;
                testSettings.ExtraInstructions = Loc("MTDA_TestProviderInstruction", "Connection test: answer with the minimum possible text.");

                var game = new Game { Name = "Pong" };
                await new MetadataGenerationService(testSettings).GenerateAsync(game);
                MessageBox.Show(Loc("MTDA_TestProviderSuccess", "The provider is responding correctly."), PluginTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(MetadataGenerationService.SanitizeForUser(ex.Message), PluginTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (button != null)
                {
                    button.IsEnabled = true;
                }
            }
        }

        private void UseLmStudio_OnClick(object sender, RoutedEventArgs e)
        {
            ApplyFreeLocalProvider(MetaDataIASettings.ProviderLmStudio);
        }

        private void UseOllama_OnClick(object sender, RoutedEventArgs e)
        {
            ApplyFreeLocalProvider(MetaDataIASettings.ProviderOllama);
        }

        private void ApplyFreeLocalProvider(string provider)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel == null)
            {
                return;
            }

            viewModel.Settings.ApplyFreeLocalPreset(provider);
            LoadPasswordBoxes(viewModel.Settings);
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

            if (string.Equals(source, "Steam capturas", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseSteamScreenshots;
            }

            if (string.Equals(source, "SteamGridDB", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseSteamGridDb;
            }

            if (string.Equals(source, "RAWG", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseRawg;
            }

            if (string.Equals(source, "MobyGames", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseMobyGames;
            }

            if (string.Equals(source, "IGDB", System.StringComparison.OrdinalIgnoreCase))
            {
                return settings.MediaUseIgdb;
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
                    Source("Steam oficial")
                };
            }

            if (kind == MediaKind.Background)
            {
                return new List<SourcePriorityItem>
                {
                    Source("Steam oficial"),
                    Source("Steam capturas"),
                    Source("SteamGridDB"),
                    Source("RAWG"),
                    Source("IGDB"),
                    Source("MobyGames")
                };
            }

            return new List<SourcePriorityItem>
            {
                Source("Steam oficial"),
                Source("SteamGridDB"),
                Source("IGDB"),
                Source("RAWG"),
                Source("MobyGames")
            };
        }

        private static SourcePriorityItem Source(string name)
        {
            return new SourcePriorityItem { Key = name, DisplayName = name };
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
