using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using Playnite.SDK.Data;
using Playnite.SDK.Models;

namespace MetaDataIAPlugin
{
    public partial class MetaDataIASettingsView : UserControl
    {
        public MetaDataIASettingsView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                var viewModel = DataContext as MetaDataIASettingsViewModel;
                if (viewModel != null)
                {
                    ApiKeyBox.Password = viewModel.Settings.ApiKey ?? string.Empty;
                }
            };
        }

        private void ApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MetaDataIASettingsViewModel;
            if (viewModel != null)
            {
                viewModel.Settings.ApiKey = ApiKeyBox.Password;
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
                ApiKeyBox.Password = viewModel.Settings.ApiKey ?? string.Empty;
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
            ApiKeyBox.Password = string.Empty;
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
