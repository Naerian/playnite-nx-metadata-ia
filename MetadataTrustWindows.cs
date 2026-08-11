using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MetaDataIAPlugin
{
    internal static class MetadataTrustUi
    {
        public static void ApplyWindowTheme(Window window)
        {
            SetResource(window, FrameworkElement.StyleProperty, "StandardWindowStyle");
            SetResource(window, Control.BackgroundProperty, "StandardWindowBackgroundBrush");
            SetResource(window, Control.ForegroundProperty, "TextBrush");
        }

        public static void SetResource(FrameworkElement element, DependencyProperty property, string key)
        {
            if (element == null)
            {
                return;
            }

            try { element.SetResourceReference(property, key); } catch { }
        }

        public static TextBlock Hint(string text, Thickness margin)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = margin };
            SetResource(block, TextBlock.ForegroundProperty, "TextBrush");
            return block;
        }

        public static Border Card(UIElement child, Thickness margin)
        {
            var border = new Border { Child = child, BorderThickness = new Thickness(1), Padding = new Thickness(12), Margin = margin, CornerRadius = new CornerRadius(3) };
            SetResource(border, Border.BackgroundProperty, "ControlBackgroundBrush");
            SetResource(border, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
            ApplyTextBrush(border);
            return border;
        }

        public static FrameworkElement Section(string title, UIElement content, Thickness margin)
        {
            var stack = new StackPanel { Margin = margin };
            var heading = Text(title);
            heading.FontWeight = FontWeights.SemiBold;
            var header = new Border
            {
                Child = heading,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 6),
                Margin = new Thickness(0, 0, 0, 10)
            };
            SetResource(header, Border.BorderBrushProperty, "GlyphBrush");
            stack.Children.Add(header);

            var body = new Border
            {
                Child = content,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(3)
            };
            SetResource(body, Border.BackgroundProperty, "ControlBackgroundBrush");
            SetResource(body, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
            ApplyTextBrush(body);
            stack.Children.Add(body);
            return stack;
        }

        public static TextBlock Text(string value, bool wrap = true)
        {
            var block = new TextBlock { Text = value ?? string.Empty, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
            SetResource(block, TextBlock.ForegroundProperty, "TextBrush");
            return block;
        }

        public static void ApplyTextBrush(FrameworkElement element)
        {
            SetResource(element, System.Windows.Documents.TextElement.ForegroundProperty, "TextBrush");
        }

        public static void ApplySecurePasswordBox(PasswordBox box)
        {
            if (box == null)
            {
                return;
            }

            SetResource(box, Control.ForegroundProperty, "TextBrush");
            SetResource(box, PasswordBox.CaretBrushProperty, "TextBrush");
            SetResource(box, Control.BackgroundProperty, "PopupBackgroundBrush");
            SetResource(box, Control.BorderBrushProperty, "GlyphBrush");
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(6, 4, 6, 4);
            box.MinHeight = 30;
            box.SnapsToDevicePixels = true;

            var template = new ControlTemplate(typeof(PasswordBox));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            var host = new FrameworkElementFactory(typeof(ScrollViewer));
            host.Name = "PART_ContentHost";
            host.SetBinding(FrameworkElement.MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(host);
            template.VisualTree = border;
            box.Template = template;
        }

        public static string FieldName(MetaDataIAPlugin plugin, string field)
        {
            switch (field)
            {
                case "description": return plugin.Loc("MTDA_Description", "Description");
                case "genres": return plugin.Loc("MTDA_Genres", "Genres");
                case "tags": return plugin.Loc("MTDA_Tags", "Tags");
                case "features": return plugin.Loc("MTDA_Features", "Features");
                case "developers": return plugin.Loc("MTDA_Developers", "Developers");
                case "publishers": return plugin.Loc("MTDA_Publishers", "Publishers");
                case "ageRatings": return plugin.Loc("MTDA_Age", "Age rating");
                case "regions": return plugin.Loc("MTDA_Region", "Region");
                case "categories": return plugin.Loc("MTDA_Categories", "Categories");
                case "sortingName": return plugin.Loc("MTDA_SortingName", "Sorting name");
                case "links": return plugin.Loc("MTDA_Links", "Links");
                case "releaseDate": return plugin.Loc("MTDA_ReleaseDate", "Release date");
                case "series": return plugin.Loc("MTDA_Series", "Series");
                case "cover": return plugin.Loc("MTDA_Cover", "Cover");
                case "icon": return plugin.Loc("MTDA_Icon", "Icon");
                case "background": return plugin.Loc("MTDA_Background", "Background");
                default: return field;
            }
        }

        public static string ProvenanceMethod(MetaDataIAPlugin plugin, string method)
        {
            if (string.Equals(method, "trusted-context", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceMethodTrusted", "Trusted context");
            if (string.Equals(method, "ai-normalized", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceMethodNormalized", "AI normalized");
            if (string.Equals(method, "generated-from-identity", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceMethodIdentity", "Generated from game identity");
            if (string.Equals(method, "deterministic", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceMethodDeterministic", "Local deterministic rule");
            if (string.Equals(method, "downloaded-media", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceMethodMedia", "Downloaded media");
            return method;
        }

        public static string Confidence(MetaDataIAPlugin plugin, string confidence)
        {
            if (string.Equals(confidence, "high", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceConfidenceHigh", "High");
            if (string.Equals(confidence, "medium", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceConfidenceMedium", "Medium");
            if (string.Equals(confidence, "low", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceConfidenceLow", "Low; review recommended");
            return confidence;
        }

        public static string ProvenanceSource(MetaDataIAPlugin plugin, string source)
        {
            if (string.Equals(source, "Existing Playnite metadata", StringComparison.OrdinalIgnoreCase))
            {
                return plugin.Loc("MTDA_ProvenanceSourceExisting", "Existing Playnite metadata");
            }

            if (string.Equals(source, "Metadata AI local rule", StringComparison.OrdinalIgnoreCase))
            {
                return plugin.Loc("MTDA_ProvenanceSourceLocalRule", "Metadata AI local rule");
            }

            const string providerPrefix = "AI provider: ";
            if (!string.IsNullOrWhiteSpace(source) && source.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return plugin.Loc("MTDA_ProvenanceSourceAiProvider", "AI provider") + ": " + source.Substring(providerPrefix.Length);
            }

            return string.IsNullOrWhiteSpace(source) ? plugin.Loc("MTDA_Unknown", "Unknown") : source;
        }

        public static string ProvenanceExplanation(MetaDataIAPlugin plugin, MetadataFieldProvenance item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (string.Equals(item.Method, "trusted-context", StringComparison.OrdinalIgnoreCase))
            {
                return plugin.Loc("MTDA_ProvenanceDetailTrusted", "The value was constrained by trusted source context.");
            }

            if (string.Equals(item.Method, "ai-normalized", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(item.Source, "Existing Playnite metadata", StringComparison.OrdinalIgnoreCase)
                    ? plugin.Loc("MTDA_ProvenanceDetailNormalizedExisting", "Current library metadata was supplied as context and normalized by the AI.")
                    : plugin.Loc("MTDA_ProvenanceDetailNormalizedOfficial", "The source was supplied as factual context and the AI normalized it.");
            }

            if (string.Equals(item.Method, "generated-from-identity", StringComparison.OrdinalIgnoreCase))
            {
                return plugin.Loc("MTDA_ProvenanceDetailIdentity", "No field-specific trusted source was available. Review this value before applying it.");
            }

            if (string.Equals(item.Method, "deterministic", StringComparison.OrdinalIgnoreCase))
            {
                return plugin.Loc("MTDA_ProvenanceDetailDeterministic", "Metadata AI calculated this value locally without asking the AI provider.");
            }

            if (string.Equals(item.Method, "downloaded-media", StringComparison.OrdinalIgnoreCase))
            {
                return plugin.Loc("MTDA_ProvenanceDetailMedia", "This media file was downloaded from the indicated source.");
            }

            return item.Detail ?? string.Empty;
        }
    }

    public sealed class SetupWizardWindow : Window
    {
        private readonly MetaDataIAPlugin plugin;
        private readonly MetaDataIASettings working;
        private readonly bool firstRun;
        private readonly ContentControl content = new ContentControl();
        private readonly TextBlock stepText = new TextBlock();
        private readonly TextBlock titleText = new TextBlock();
        private readonly Button backButton = new Button();
        private readonly Button nextButton = new Button();
        private readonly Button skipButton = new Button();
        private int page;
        private string selectedProfile = "balanced";

        public MetaDataIASettings ResultSettings { get; private set; }
        public bool Skipped { get; private set; }

        public SetupWizardWindow(MetaDataIAPlugin plugin, MetaDataIASettings current, bool firstRun)
        {
            this.plugin = plugin;
            this.firstRun = firstRun;
            working = Serialization.GetClone(current ?? new MetaDataIASettings());
            working.EnsureDefaults();
            selectedProfile = firstRun ? "balanced" : "current";
            if (firstRun)
            {
                working.Language = ResolvePlayniteLanguage();
                working.ResetTemplates();
                ApplyProfile(selectedProfile);
            }

            Title = plugin.Loc("MTDA_SetupWizardTitle", "Metadata AI setup assistant");
            Width = 780;
            Height = 620;
            MinWidth = 680;
            MinHeight = 520;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            MetadataTrustUi.ApplyWindowTheme(this);

            var owner = plugin.Api.Dialogs.GetCurrentAppWindow();
            if (owner != null)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            var root = new Grid { Margin = new Thickness(20) };
            MetadataTrustUi.ApplyTextBrush(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            stepText.Opacity = 0.7;
            titleText.FontSize = 22;
            titleText.FontWeight = FontWeights.SemiBold;
            titleText.Margin = new Thickness(0, 4, 0, 0);
            header.Children.Add(stepText);
            header.Children.Add(titleText);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            skipButton.Content = firstRun ? plugin.Loc("MTDA_SetupWizardSkip", "Skip for now") : plugin.Loc("MTDA_Close", "Close");
            skipButton.MinWidth = 110;
            skipButton.Click += (s, e) => { Skipped = true; DialogResult = false; };
            buttons.Children.Add(skipButton);

            backButton.Content = plugin.Loc("MTDA_SetupWizardBack", "Back");
            backButton.MinWidth = 100;
            backButton.Margin = new Thickness(0, 0, 8, 0);
            backButton.Click += (s, e) => { if (page > 0) { page--; RenderPage(); } };
            Grid.SetColumn(backButton, 2);
            buttons.Children.Add(backButton);

            nextButton.MinWidth = 120;
            nextButton.Click += NextButtonOnClick;
            Grid.SetColumn(nextButton, 3);
            buttons.Children.Add(nextButton);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Content = root;
            RenderPage();
        }

        private void NextButtonOnClick(object sender, RoutedEventArgs e)
        {
            if (page < 3)
            {
                page++;
                RenderPage();
                return;
            }

            working.SetupWizardCompleted = true;
            working.SetupWizardMigrationApplied = true;
            ResultSettings = working;
            DialogResult = true;
        }

        private void RenderPage()
        {
            stepText.Text = string.Format(plugin.Loc("MTDA_SetupWizardStep", "Step {0} of 4"), page + 1);
            backButton.IsEnabled = page > 0;
            nextButton.Content = page == 3 ? plugin.Loc("MTDA_SetupWizardFinish", "Save configuration") : plugin.Loc("MTDA_SetupWizardNext", "Next");

            if (page == 0) content.Content = BuildPurposePage();
            else if (page == 1) content.Content = BuildProviderPage();
            else if (page == 2) content.Content = BuildFieldsPage();
            else content.Content = BuildSummaryPage();
        }

        private UIElement BuildPurposePage()
        {
            titleText.Text = plugin.Loc("MTDA_SetupWizardPurposeTitle", "Choose language and a safe starting point");
            var panel = new StackPanel();
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SetupWizardPurposeHelp", "The assistant only prepares the configuration. It will not modify any game when you finish."), new Thickness(0, 0, 0, 16)));
            panel.Children.Add(Label(plugin.Loc("MTDA_OutputLanguage", "Output language")));
            var language = new ComboBox { ItemsSource = working.LanguageOptions, DisplayMemberPath = "DisplayName", SelectedValuePath = "Code", SelectedValue = working.Language, MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 20) };
            language.SelectionChanged += (s, e) =>
            {
                if (language.SelectedValue == null) return;
                working.Language = language.SelectedValue.ToString();
                if (firstRun) working.ResetTemplates();
            };
            panel.Children.Add(language);
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_OutputLanguageHelp", "This controls generated metadata and default template headings. The plugin interface follows Playnite's interface language."), new Thickness(0, -12, 0, 20)));

            panel.Children.Add(Label(plugin.Loc("MTDA_SetupWizardProfile", "Configuration profile")));
            var profiles = new List<LocalizedOption>
            {
                new LocalizedOption("balanced", plugin.Loc("MTDA_SetupProfileBalanced", "Balanced and safe (recommended)")),
                new LocalizedOption("missing", plugin.Loc("MTDA_SetupProfileMissing", "Fill missing metadata only")),
                new LocalizedOption("normalize", plugin.Loc("MTDA_SetupProfileNormalize", "Normalize an existing library")),
                new LocalizedOption("media", plugin.Loc("MTDA_SetupProfileMedia", "Media only")),
                new LocalizedOption("current", plugin.Loc("MTDA_SetupProfileCurrent", "Keep current configuration"))
            };
            var profile = new ComboBox { ItemsSource = profiles, DisplayMemberPath = "DisplayName", SelectedValuePath = "Value", SelectedValue = selectedProfile, MinWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 8) };
            profile.SelectionChanged += (s, e) =>
            {
                if (profile.SelectedValue == null) return;
                selectedProfile = profile.SelectedValue.ToString();
                ApplyProfile(selectedProfile);
            };
            panel.Children.Add(profile);
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SetupWizardProfileHelp", "You can fine-tune every field later. Existing installations are never reset automatically."), new Thickness(0, 0, 0, 0)));
            return panel;
        }

        private UIElement BuildProviderPage()
        {
            titleText.Text = plugin.Loc("MTDA_SetupWizardProviderTitle", "Configure the AI provider");
            var panel = new StackPanel();
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SetupWizardProviderHelp", "Local providers such as LM Studio and Ollama can work without a paid API. The API key can stay empty when the selected provider does not require one."), new Thickness(0, 0, 0, 16)));
            panel.Children.Add(Label(plugin.Loc("MTDA_Provider", "Provider")));
            var provider = new ComboBox { ItemsSource = working.ProviderPresetOptions, DisplayMemberPath = "DisplayName", SelectedValuePath = "Value", SelectedValue = working.ProviderPreset, MinWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 14) };
            panel.Children.Add(provider);
            panel.Children.Add(Label(plugin.Loc("MTDA_Model", "Model")));
            var model = new TextBox { Text = working.Model, MinWidth = 480, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 14) };
            panel.Children.Add(model);
            panel.Children.Add(Label(plugin.Loc("MTDA_ApiKey", "API key")));
            var key = new PasswordBox { Password = working.ApiKey ?? string.Empty, MinWidth = 480, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 8) };
            MetadataTrustUi.ApplySecurePasswordBox(key);
            panel.Children.Add(key);
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SetupWizardProviderAdvancedHelp", "The endpoint is selected automatically. Custom endpoints remain available from Advanced mode in the AI tab."), new Thickness(0, 0, 0, 0)));

            var testPanel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var testButton = new Button { Content = plugin.Loc("MTDA_TestProvider", "Test provider"), MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left };
            var testStatus = MetadataTrustUi.Text(string.Empty);
            var testStatusBorder = MetadataTrustUi.Card(testStatus, new Thickness(0, 10, 0, 0));
            testStatusBorder.Visibility = Visibility.Collapsed;
            testButton.Click += async (s, e) =>
            {
                working.Model = model.Text;
                working.ApiKey = key.Password;
                testButton.IsEnabled = false;
                backButton.IsEnabled = false;
                nextButton.IsEnabled = false;
                skipButton.IsEnabled = false;
                testStatusBorder.Visibility = Visibility.Visible;
                testStatus.Text = plugin.Loc("MTDA_TestSending", "Sending a test request to {0}...").Replace("{0}", working.ProviderPreset);
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90)))
                {
                    try
                    {
                        await new MetadataGenerationService(working, plugin.Api).GenerateAsync(new Game { Name = "Hades" }, cancellation.Token);
                        testStatus.Text = plugin.Loc("MTDA_SetupWizardProviderTestSuccess", "Provider test completed successfully.");
                    }
                    catch (OperationCanceledException)
                    {
                        testStatus.Text = plugin.Loc("MTDA_TestTimedOut", "The provider or source did not respond within 90 seconds. It may be busy or unavailable.");
                    }
                    catch (Exception ex)
                    {
                        testStatus.Text = MetadataGenerationService.SanitizeForUser(ex.Message);
                    }
                    finally
                    {
                        testButton.IsEnabled = true;
                        backButton.IsEnabled = page > 0;
                        nextButton.IsEnabled = true;
                        skipButton.IsEnabled = true;
                    }
                }
            };
            testPanel.Children.Add(testButton);
            testPanel.Children.Add(testStatusBorder);
            panel.Children.Add(testPanel);

            provider.SelectionChanged += (s, e) =>
            {
                if (provider.SelectedValue == null) return;
                working.ProviderPreset = provider.SelectedValue.ToString();
                working.ApplyProviderPreset();
                model.Text = working.Model;
            };
            model.TextChanged += (s, e) => working.Model = model.Text;
            key.PasswordChanged += (s, e) => working.ApiKey = key.Password;
            return panel;
        }

        private UIElement BuildFieldsPage()
        {
            titleText.Text = plugin.Loc("MTDA_SetupWizardFieldsTitle", "Choose what Metadata AI may change");
            var panel = new StackPanel();
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SetupWizardFieldsHelp", "These switches control generation. The apply rules selected by the profile still decide whether existing values are preserved, appended or replaced."), new Thickness(0, 0, 0, 14)));
            var wrap = new WrapPanel();
            AddFieldCheck(wrap, plugin.Loc("MTDA_Description", "Description"), () => working.GenerateDescription, v => working.GenerateDescription = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_Genres", "Genres"), () => working.GenerateGenres, v => working.GenerateGenres = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_Tags", "Tags"), () => working.GenerateTags, v => working.GenerateTags = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_Features", "Features"), () => working.GenerateFeatures, v => working.GenerateFeatures = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_Categories", "Categories"), () => working.GenerateCategories, v => working.GenerateCategories = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_Developers", "Developers"), () => working.GenerateDevelopers, v => working.GenerateDevelopers = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_Publishers", "Publishers"), () => working.GeneratePublishers, v => working.GeneratePublishers = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_Age", "Age rating"), () => working.GenerateAgeRatings, v => working.GenerateAgeRatings = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_Region", "Region"), () => working.GenerateRegions, v => working.GenerateRegions = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_SortingName", "Sorting name"), () => working.GenerateSortingName, v => working.GenerateSortingName = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_Links", "Links"), () => working.GenerateLinks, v => working.GenerateLinks = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_Cover", "Cover"), () => working.DownloadCoverImage, v => working.DownloadCoverImage = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_Icon", "Icon"), () => working.DownloadIcon, v => working.DownloadIcon = v);
            AddFieldCheck(wrap, plugin.Loc("MTDA_Background", "Background"), () => working.DownloadBackgroundImage, v => working.DownloadBackgroundImage = v);
            panel.Children.Add(MetadataTrustUi.Card(wrap, new Thickness(0)));
            var strict = new CheckBox { Content = plugin.Loc("MTDA_StrictCompanyAgeRegion", "Do not create developers, publishers, age ratings or regions without trusted evidence"), IsChecked = working.StrictCompanyAgeRegion, Margin = new Thickness(0, 16, 0, 4) };
            strict.Checked += (s, e) => working.StrictCompanyAgeRegion = true;
            strict.Unchecked += (s, e) => working.StrictCompanyAgeRegion = false;
            panel.Children.Add(strict);
            return panel;
        }

        private UIElement BuildSummaryPage()
        {
            titleText.Text = plugin.Loc("MTDA_SetupWizardSummaryTitle", "Review the configuration");
            var fields = new List<string>();
            if (working.GenerateDescription) fields.Add(plugin.Loc("MTDA_Description", "Description"));
            if (working.GenerateGenres) fields.Add(plugin.Loc("MTDA_Genres", "Genres"));
            if (working.GenerateTags) fields.Add(plugin.Loc("MTDA_Tags", "Tags"));
            if (working.GenerateFeatures) fields.Add(plugin.Loc("MTDA_Features", "Features"));
            if (working.GenerateCategories) fields.Add(plugin.Loc("MTDA_Categories", "Categories"));
            if (working.GenerateDevelopers) fields.Add(plugin.Loc("MTDA_Developers", "Developers"));
            if (working.GeneratePublishers) fields.Add(plugin.Loc("MTDA_Publishers", "Publishers"));
            if (working.GenerateAgeRatings) fields.Add(plugin.Loc("MTDA_Age", "Age rating"));
            if (working.GenerateRegions) fields.Add(plugin.Loc("MTDA_Region", "Region"));
            if (working.GenerateSortingName) fields.Add(plugin.Loc("MTDA_SortingName", "Sorting name"));
            if (working.GenerateLinks) fields.Add(plugin.Loc("MTDA_Links", "Links"));
            if (working.DownloadCoverImage) fields.Add(plugin.Loc("MTDA_Cover", "Cover"));
            if (working.DownloadIcon) fields.Add(plugin.Loc("MTDA_Icon", "Icon"));
            if (working.DownloadBackgroundImage) fields.Add(plugin.Loc("MTDA_Background", "Background"));

            var stack = new StackPanel();
            stack.Children.Add(SummaryLine(plugin.Loc("MTDA_OutputLanguage", "Output language"), working.Language));
            stack.Children.Add(SummaryLine(plugin.Loc("MTDA_Provider", "Provider"), working.ProviderPreset));
            stack.Children.Add(SummaryLine(plugin.Loc("MTDA_Model", "Model"), working.Model));
            stack.Children.Add(SummaryLine(plugin.Loc("MTDA_SetupWizardProfile", "Configuration profile"), selectedProfile));
            stack.Children.Add(SummaryLine(plugin.Loc("MTDA_SetupWizardEnabledFields", "Enabled fields"), fields.Count == 0 ? plugin.Loc("MTDA_None", "None") : string.Join(", ", fields)));
            stack.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SetupWizardSummaryHelp", "Saving only changes the plugin configuration. Use Simulate changes from the Metadata AI menu to inspect a real game before applying anything."), new Thickness(0, 16, 0, 0)));
            return MetadataTrustUi.Card(stack, new Thickness(0));
        }

        private void ApplyProfile(string profile)
        {
            if (profile == "current") return;
            working.AutoImportNewGames = false;
            working.StrictCompanyAgeRegion = true;
            working.UseOfficialStoreContext = true;
            working.UseOriginIntegrationAsAiContext = true;
            working.UseOriginIntegrationForFactualMetadata = true;
            working.GenerateRegions = false;
            working.GenerateAgeRatings = false;
            working.MaxDevelopers = 1;
            working.MaxPublishers = 1;
            working.MaxTags = 9;
            working.MaxFeatures = 7;
            working.PreferExistingCategories = true;

            var emptyOnly = profile == "missing" || profile == "balanced";
            var mode = emptyOnly ? MetaDataIASettings.ApplyEmptyOnly : MetaDataIASettings.ApplyOverwrite;
            working.DescriptionApplyMode = mode;
            working.GenresApplyMode = emptyOnly ? MetaDataIASettings.ApplyEmptyOnly : MetaDataIASettings.ApplyAppend;
            working.TagsApplyMode = emptyOnly ? MetaDataIASettings.ApplyEmptyOnly : MetaDataIASettings.ApplyAppend;
            working.FeaturesApplyMode = emptyOnly ? MetaDataIASettings.ApplyEmptyOnly : MetaDataIASettings.ApplyAppend;
            working.CategoriesApplyMode = MetaDataIASettings.ApplyAppend;
            working.DevelopersApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            working.PublishersApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            working.LinksApplyMode = emptyOnly ? MetaDataIASettings.ApplyEmptyOnly : MetaDataIASettings.ApplyAppend;

            working.GenerateDescription = profile != "media";
            working.GenerateGenres = profile != "media";
            working.GenerateTags = profile != "media";
            working.GenerateFeatures = profile != "media";
            working.GenerateCategories = profile != "media";
            working.GenerateDevelopers = profile != "media";
            working.GeneratePublishers = profile != "media";
            working.GenerateSortingName = profile != "media";
            working.GenerateLinks = profile != "media";
            working.DownloadCoverImage = profile == "media";
            working.DownloadIcon = profile == "media";
            working.DownloadBackgroundImage = profile == "media";
            working.CoverImageApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            working.IconApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            working.BackgroundImageApplyMode = MetaDataIASettings.ApplyEmptyOnly;
            working.ExistingMetadataMode = profile == "normalize" ? "Normalizar" : working.ExistingMetadataMode;
        }

        private string ResolvePlayniteLanguage()
        {
            var configured = plugin.Api == null || plugin.Api.ApplicationSettings == null
                ? null
                : plugin.Api.ApplicationSettings.Language;
            var normalized = (configured ?? string.Empty).Replace('_', '-').Trim();
            var exact = working.LanguageOptions.FirstOrDefault(x => string.Equals(x.Code, normalized, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact.Code;

            var shortCode = normalized.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var match = working.LanguageOptions.FirstOrDefault(x => string.Equals(x.Code, shortCode, StringComparison.OrdinalIgnoreCase));
            return match == null ? "en" : match.Code;
        }

        private static TextBlock Label(string value)
        {
            return new TextBlock { Text = value, FontWeight = FontWeights.SemiBold };
        }

        private static UIElement SummaryLine(string label, string value)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = value ?? string.Empty, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            return panel;
        }

        private static void AddFieldCheck(Panel panel, string text, Func<bool> get, Action<bool> set)
        {
            var check = new CheckBox { Content = text, IsChecked = get(), Width = 215, Margin = new Thickness(0, 5, 8, 5) };
            check.Checked += (s, e) => set(true);
            check.Unchecked += (s, e) => set(false);
            panel.Children.Add(check);
        }
    }

    public sealed class SimulationWindow : Window
    {
        private readonly MetaDataIAPlugin plugin;
        private readonly IList<MetadataSimulationResult> results;
        private readonly MetaDataIASettings activeSettings;
        private readonly bool singleGame;
        private readonly Dictionary<MetadataChangeItem, CheckBox> fieldSelectors = new Dictionary<MetadataChangeItem, CheckBox>();
        private readonly Dictionary<MetadataSimulationResult, CheckBox> gameSelectors = new Dictionary<MetadataSimulationResult, CheckBox>();
        private readonly Dictionary<MediaKind, Border> mediaCards = new Dictionary<MediaKind, Border>();
        private readonly TextBlock selectionSummary = new TextBlock();
        private readonly Button applyButton = new Button();
        private readonly ListBox gameList = new ListBox();
        private readonly ContentControl multiGameContent = new ContentControl();
        private bool updatingSelection;
        public bool ApplyRequested { get; private set; }

        public SimulationWindow(MetaDataIAPlugin plugin, IList<MetadataSimulationResult> results, MetaDataIASettings activeSettings)
        {
            this.plugin = plugin;
            this.results = results ?? new List<MetadataSimulationResult>();
            this.activeSettings = activeSettings;
            singleGame = this.results.Count == 1 && this.results[0].Game != null;
            Title = plugin.Loc("MTDA_MenuSimulateChanges", "Preview and choose Metadata AI changes");
            Width = 1040;
            Height = 760;
            MinWidth = 780;
            MinHeight = 540;
            ShowInTaskbar = false;
            MetadataTrustUi.ApplyWindowTheme(this);
            var owner = plugin.Api.Dialogs.GetCurrentAppWindow();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }

            var root = new Grid { Margin = new Thickness(18) };
            MetadataTrustUi.ApplyTextBrush(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var intro = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            intro.Children.Add(BuildWindowHeader());
            root.Children.Add(intro);

            UIElement bodyContent;
            if (singleGame)
            {
                bodyContent = new ScrollViewer
                {
                    Content = BuildGameContent(this.results[0], false),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
            }
            else
            {
                bodyContent = BuildMultiGameLayout();
            }
            Grid.SetRow(bodyContent, 1);
            root.Children.Add(bodyContent);

            var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selectionSummary.TextWrapping = TextWrapping.Wrap;
            selectionSummary.VerticalAlignment = VerticalAlignment.Center;
            selectionSummary.Margin = new Thickness(2, 0, 16, 0);
            MetadataTrustUi.SetResource(selectionSummary, TextBlock.ForegroundProperty, "TextBrush");
            footer.Children.Add(selectionSummary);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            applyButton.Content = plugin.Loc("MTDA_SimulationApplySelected", "Apply selected changes");
            applyButton.MinWidth = 190;
            applyButton.Margin = new Thickness(0, 0, 8, 0);
            applyButton.Click += (s, e) => { ApplyRequested = true; DialogResult = true; };
            var close = new Button { Content = plugin.Loc("MTDA_Close", "Close"), MinWidth = 100 };
            close.Click += (s, e) => DialogResult = false;
            buttons.Children.Add(applyButton);
            buttons.Children.Add(close);
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            Content = root;
            UpdateSelectionState();
        }

        private UIElement BuildWindowHeader()
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = singleGame ? new GridLength(112) : new GridLength(0) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (singleGame)
            {
                header.Children.Add(BuildGameArtwork(results[0].Game));
            }

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleValue = singleGame ? results[0].Game.Name : plugin.Loc("MTDA_MenuSimulateChanges", "Preview and choose Metadata AI changes");
            var title = MetadataTrustUi.Text(titleValue);
            title.FontSize = 24;
            title.FontWeight = FontWeights.SemiBold;
            text.Children.Add(title);
            if (singleGame)
            {
                var subtitle = MetadataTrustUi.Text(plugin.Loc("MTDA_MenuSimulateChanges", "Preview and choose Metadata AI changes"));
                subtitle.FontSize = 14;
                subtitle.Opacity = 0.78;
                subtitle.Margin = new Thickness(0, 3, 0, 0);
                text.Children.Add(subtitle);
            }

            text.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SimulationHelp", "No changes have been written. Applying will reuse these generated results without calling the AI provider again."), new Thickness(0, 8, 0, 0)));
            text.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SimulationRecommendationHelp", "Recommendations are calculated locally from completeness, provenance, confidence, and possible information loss. They do not call the AI provider again and cannot replace your own review."), new Thickness(0, 4, 0, 0)));
            Grid.SetColumn(text, 1);
            header.Children.Add(text);
            return header;
        }

        private UIElement BuildMultiGameLayout()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            gameList.BorderThickness = new Thickness(0);
            gameList.Padding = new Thickness(0, 0, 12, 0);
            gameList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            gameList.SelectionChanged += (s, e) => ShowSelectedSimulationGame();
            foreach (var item in results)
            {
                gameList.Items.Add(BuildSimulationGameItem(item));
            }
            grid.Children.Add(gameList);

            var separator = new Border { BorderThickness = new Thickness(1, 0, 0, 0) };
            MetadataTrustUi.SetResource(separator, Border.BorderBrushProperty, "GlyphBrush");
            Grid.SetColumn(separator, 1);
            grid.Children.Add(separator);

            multiGameContent.Margin = new Thickness(14, 0, 0, 0);
            multiGameContent.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(multiGameContent, 2);
            grid.Children.Add(multiGameContent);
            if (gameList.Items.Count > 0) gameList.SelectedIndex = 0;
            return grid;
        }

        private ListBoxItem BuildSimulationGameItem(MetadataSimulationResult item)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(BuildGameNavigationArtwork(item == null ? null : item.Game));
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = MetadataTrustUi.Text(item == null || item.Game == null ? plugin.Loc("MTDA_Unknown", "Unknown") : item.Game.Name);
            name.FontWeight = FontWeights.SemiBold;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(name);
            var changeCount = item == null || item.Changes == null ? 0 : item.Changes.Count;
            text.Children.Add(MetadataTrustUi.Hint(
                string.Format(plugin.Loc("MTDA_SimulationChangesCount", "{0} change(s)"), changeCount) + "  |  " + GameRecommendation(item),
                new Thickness(0, 3, 0, 0)));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var row = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 5, 4, 8) };
            MetadataTrustUi.SetResource(row, Border.BorderBrushProperty, "GlyphBrush");
            return new ListBoxItem { Content = row, Tag = item, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        }

        private UIElement BuildGameNavigationArtwork(Game game)
        {
            var frame = new Border { Width = 48, Height = 58, Padding = new Thickness(2), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
            MetadataTrustUi.SetResource(frame, Border.BorderBrushProperty, "GlyphBrush");
            var path = ResolveGameMediaPath(game, MediaKind.Cover);
            if (string.IsNullOrWhiteSpace(path)) path = ResolveGameMediaPath(game, MediaKind.Icon);
            var image = CreatePreviewImage(path, false, 64);
            if (image != null) frame.Child = image;
            return frame;
        }

        private void ShowSelectedSimulationGame()
        {
            var selected = gameList.SelectedItem as ListBoxItem;
            var item = selected == null ? null : selected.Tag as MetadataSimulationResult;
            if (item == null)
            {
                multiGameContent.Content = null;
                return;
            }
            multiGameContent.Content = new ScrollViewer
            {
                Content = BuildGameContent(item, true),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            UpdateSelectionState();
        }

        private UIElement BuildGameArtwork(Game game)
        {
            var frame = new Border
            {
                Width = 96,
                Height = 112,
                Padding = new Thickness(2),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            MetadataTrustUi.SetResource(frame, Border.BackgroundProperty, "ControlBackgroundBrush");
            MetadataTrustUi.SetResource(frame, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
            var path = ResolveGameMediaPath(game, MediaKind.Cover);
            if (string.IsNullOrWhiteSpace(path)) path = ResolveGameMediaPath(game, MediaKind.Icon);
            var image = CreatePreviewImage(path, false, 92);
            if (image != null) frame.Child = image;
            return frame;
        }

        private UIElement BuildGame(MetadataSimulationResult item)
        {
            var headerText = (item.Game == null ? plugin.Loc("MTDA_Unknown", "Unknown") : item.Game.Name) + " (" + (item.Changes == null ? 0 : item.Changes.Count) + ")";
            if (item.Changes != null && item.Changes.Count > 0)
            {
                headerText += " - " + GameRecommendation(item);
            }
            var header = MetadataTrustUi.Text(headerText);
            header.FontWeight = FontWeights.SemiBold;
            return new Expander
            {
                Header = header,
                IsExpanded = (item.Changes != null && item.Changes.Count > 0) || !string.IsNullOrWhiteSpace(item.Error),
                Content = new Border { Child = BuildGameContent(item, true), Margin = new Thickness(0, 10, 0, 14) }
            };
        }

        private UIElement BuildGameContent(MetadataSimulationResult item, bool showGameContext)
        {
            var panel = new StackPanel();
            var hasMetadataChanges = item.Changes != null && item.Changes.Count > 0;
            if (!string.IsNullOrWhiteSpace(item.Error))
            {
                panel.Children.Add(MetadataTrustUi.Text(item.Error));
            }
            else if (!hasMetadataChanges)
            {
                panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SimulationNoChanges", "No changes would be made with the current apply rules."), new Thickness(0)));
            }
            else
            {
                var verdict = MetadataTrustUi.Text(GameRecommendation(item));
                verdict.FontSize = 16;
                verdict.FontWeight = FontWeights.SemiBold;
                panel.Children.Add(verdict);
                panel.Children.Add(MetadataTrustUi.Hint(GameRecommendationDetails(item), new Thickness(0, 3, 0, 10)));
            }

            if (singleGame && !showGameContext)
            {
                panel.Children.Add(BuildMediaSection(item));
            }

            if (hasMetadataChanges)
            {
                var metadataContent = new StackPanel();
                var gameActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
                var gameCheck = new CheckBox
                {
                    Content = plugin.Loc("MTDA_SimulationApplyGame", "Apply metadata changes for this game"),
                    IsThreeState = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 18, 0)
                };
                gameCheck.Checked += (s, e) => SetGameSelection(item, true);
                gameCheck.Unchecked += (s, e) => SetGameSelection(item, false);
                gameSelectors[item] = gameCheck;
                gameActions.Children.Add(gameCheck);
                var gameRecommended = new Button { Content = plugin.Loc("MTDA_SimulationRecommendedOnly", "Recommended only"), MinWidth = 135, Margin = new Thickness(0, 0, 8, 0) };
                gameRecommended.Click += (s, e) => SetGameSelection(item, change => string.Equals(change.Recommendation, MetadataChangeRecommendationService.Recommended, StringComparison.OrdinalIgnoreCase));
                var gameAll = new Button { Content = plugin.Loc("MTDA_SimulationSelectAll", "Select all"), MinWidth = 95, Margin = new Thickness(0, 0, 8, 0) };
                gameAll.Click += (s, e) => SetGameSelection(item, true);
                var gameNone = new Button { Content = plugin.Loc("MTDA_SimulationSelectNone", "Select none"), MinWidth = 95 };
                gameNone.Click += (s, e) => SetGameSelection(item, false);
                gameActions.Children.Add(gameRecommended);
                gameActions.Children.Add(gameAll);
                gameActions.Children.Add(gameNone);
                metadataContent.Children.Add(gameActions);

                foreach (var change in item.Changes)
                {
                    metadataContent.Children.Add(BuildChange(item, change));
                }

                panel.Children.Add(BuildPlainSection(
                    plugin.Loc("MTDA_SimulationMetadataTitle", "Metadata changes"),
                    metadataContent,
                    new Thickness(0, 4, 0, 0)));
            }
            return panel;
        }

        private UIElement BuildMediaSection(MetadataSimulationResult item)
        {
            var grid = new UniformGrid { Columns = 3 };
            foreach (var kind in new[] { MediaKind.Cover, MediaKind.Icon, MediaKind.Background })
            {
                var card = new Border { Margin = new Thickness(5), MinWidth = 220 };
                mediaCards[kind] = card;
                RefreshMediaCard(item, kind, card);
                grid.Children.Add(card);
            }

            return BuildPlainSection(
                plugin.Loc("MTDA_SimulationMediaTitle", "Media preview"),
                grid,
                new Thickness(0, 4, 0, 18));
        }

        private UIElement BuildPlainSection(string title, UIElement content, Thickness margin)
        {
            var stack = new StackPanel { Margin = margin };
            var heading = MetadataTrustUi.Text(title);
            heading.FontSize = 17;
            heading.FontWeight = FontWeights.SemiBold;
            var header = new Border
            {
                Child = heading,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 7),
                Margin = new Thickness(0, 0, 0, 12)
            };
            MetadataTrustUi.SetResource(header, Border.BorderBrushProperty, "GlyphBrush");
            stack.Children.Add(header);
            stack.Children.Add(content);
            return stack;
        }

        private void RefreshMediaCard(MetadataSimulationResult item, MediaKind kind, Border host)
        {
            var selected = (item.MediaChanges ?? new List<MediaSimulationChange>()).FirstOrDefault(x => x.Kind == kind);
            var content = new StackPanel();
            var title = MetadataTrustUi.Text(MediaKindLabel(kind));
            title.FontSize = 15;
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(0, 0, 0, 8);
            content.Children.Add(title);

            var comparison = new Grid();
            comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            comparison.Children.Add(BuildMediaPreview(
                plugin.Loc("MTDA_SimulationCurrentMedia", "Current"),
                ResolveGameMediaPath(item.Game, kind),
                false,
                kind));
            var proposed = BuildMediaPreview(
                plugin.Loc("MTDA_SimulationProposedMedia", "Proposed"),
                selected == null || selected.Option == null ? null : selected.Option.Url,
                true,
                kind);
            Grid.SetColumn(proposed, 2);
            comparison.Children.Add(proposed);
            content.Children.Add(comparison);

            if (selected != null && selected.Option != null)
            {
                var source = string.IsNullOrWhiteSpace(selected.Option.SourceName)
                    ? plugin.Loc("MTDA_UnknownSource", "Unknown source")
                    : selected.Option.SourceName;
                var size = selected.Option.Width > 0 && selected.Option.Height > 0
                    ? selected.Option.Width + " x " + selected.Option.Height
                    : plugin.Loc("MTDA_Unknown", "Unknown");
                content.Children.Add(MetadataTrustUi.Hint(source + "  |  " + size, new Thickness(0, 7, 0, 0)));
            }

            var apply = new CheckBox
            {
                Content = plugin.Loc("MTDA_SimulationApplyMedia", "Apply this media change"),
                IsChecked = selected != null && selected.IsSelected,
                IsEnabled = selected != null,
                Margin = new Thickness(0, 10, 0, 8)
            };
            apply.Checked += (s, e) =>
            {
                if (selected != null && !updatingSelection) { selected.IsSelected = true; UpdateSelectionState(); }
            };
            apply.Unchecked += (s, e) =>
            {
                if (selected != null && !updatingSelection) { selected.IsSelected = false; UpdateSelectionState(); }
            };
            content.Children.Add(apply);

            var choose = new Button
            {
                Content = selected == null
                    ? string.Format(plugin.Loc("MTDA_SimulationChooseMedia", "Choose {0}"), MediaKindLabel(kind).ToLowerInvariant())
                    : plugin.Loc("MTDA_SimulationChangeMedia", "Change selection"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 7, 10, 7)
            };
            choose.Click += (s, e) =>
            {
                var selection = plugin.SelectMediaForSimulation(item.Game, kind, activeSettings, this);
                if (selection == null) return;
                var previous = item.MediaChanges.FirstOrDefault(x => x.Kind == kind);
                if (previous != null) item.MediaChanges.Remove(previous);
                item.MediaChanges.Add(selection);
                RefreshMediaCard(item, kind, host);
                UpdateSelectionState();
            };
            content.Children.Add(choose);
            host.Child = MetadataTrustUi.Card(content, new Thickness(0));
        }

        private UIElement BuildMediaPreview(string label, string source, bool remote, MediaKind kind)
        {
            var panel = new StackPanel();
            var heading = MetadataTrustUi.Text(label);
            heading.FontWeight = FontWeights.SemiBold;
            heading.Opacity = remote ? 1 : 0.72;
            panel.Children.Add(heading);
            var frame = new Border
            {
                Height = 116,
                Margin = new Thickness(0, 5, 0, 0),
                Padding = new Thickness(3),
                BorderThickness = new Thickness(1)
            };
            MetadataTrustUi.SetResource(frame, Border.BackgroundProperty, "StandardWindowBackgroundBrush");
            MetadataTrustUi.SetResource(frame, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
            var image = CreatePreviewImage(source, remote, kind == MediaKind.Background ? 260 : 150);
            frame.Child = image != null
                ? (UIElement)image
                : MetadataTrustUi.Hint(plugin.Loc("MTDA_None", "None"), new Thickness(6));
            panel.Children.Add(frame);
            return panel;
        }

        private Image CreatePreviewImage(string source, bool remote, int decodeWidth)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.DecodePixelWidth = decodeWidth;
                bitmap.CacheOption = remote ? BitmapCacheOption.OnDemand : BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(source, remote ? UriKind.Absolute : UriKind.Absolute);
                bitmap.EndInit();
                if (!remote) bitmap.Freeze();
                return new Image { Source = bitmap, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch };
            }
            catch
            {
                return null;
            }
        }

        private string ResolveGameMediaPath(Game game, MediaKind kind)
        {
            if (game == null) return null;
            var reference = kind == MediaKind.Cover ? game.CoverImage : kind == MediaKind.Icon ? game.Icon : game.BackgroundImage;
            if (string.IsNullOrWhiteSpace(reference)) return null;
            try
            {
                var path = plugin.Api.Database.GetFullFilePath(reference);
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }

        private string MediaKindLabel(MediaKind kind)
        {
            return kind == MediaKind.Cover
                ? plugin.Loc("MTDA_Cover", "Cover")
                : kind == MediaKind.Icon
                    ? plugin.Loc("MTDA_Icon", "Icon")
                    : plugin.Loc("MTDA_Background", "Background");
        }

        private UIElement BuildChange(MetadataSimulationResult item, MetadataChangeItem change)
        {
            var card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 14)
            };
            MetadataTrustUi.SetResource(card, Border.BackgroundProperty, "ControlBackgroundBrush");
            MetadataTrustUi.SetResource(card, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");

            var panel = new StackPanel();
            card.Child = panel;
            var header = new Grid { Margin = new Thickness(14, 11, 14, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var identity = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var selector = new CheckBox
            {
                IsChecked = change.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = plugin.Loc("MTDA_SimulationApplyField", "Apply this field"),
                Margin = new Thickness(0, 0, 10, 0)
            };
            selector.Checked += (s, e) => ChangeSelection(item, change, true);
            selector.Unchecked += (s, e) => ChangeSelection(item, change, false);
            fieldSelectors[change] = selector;
            identity.Children.Add(selector);
            var fieldTitle = MetadataTrustUi.Text(MetadataTrustUi.FieldName(plugin, change.Field));
            fieldTitle.FontSize = 16;
            fieldTitle.FontWeight = FontWeights.SemiBold;
            identity.Children.Add(fieldTitle);
            header.Children.Add(identity);

            var recommendation = new StackPanel { MaxWidth = 500, Margin = new Thickness(20, 0, 0, 0) };
            recommendation.HorizontalAlignment = HorizontalAlignment.Right;
            recommendation.Children.Add(BuildRecommendationBadge(change));
            var recommendationReason = MetadataTrustUi.Hint(FormatSentence(RecommendationReason(change)), new Thickness(0, 5, 0, 0));
            recommendationReason.TextAlignment = TextAlignment.Right;
            recommendation.Children.Add(recommendationReason);
            Grid.SetColumn(recommendation, 1);
            header.Children.Add(recommendation);

            var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
            MetadataTrustUi.SetResource(headerBorder, Border.BorderBrushProperty, "GlyphBrush");
            panel.Children.Add(headerBorder);

            if (change.Conflict != null && change.Conflict.Values != null && change.Conflict.Values.Count > 1)
            {
                var conflictText = string.Join(Environment.NewLine, change.Conflict.Values.Select(x => "- " + MetadataTrustUi.ProvenanceSource(plugin, x.Source) + ": " + x.Value));
                var conflict = MetadataTrustUi.Text(plugin.Loc("MTDA_SourceConflictTitle", "Trusted sources disagree") + Environment.NewLine + conflictText);
                conflict.TextWrapping = TextWrapping.Wrap;
                var conflictBorder = new Border { Child = conflict, Padding = new Thickness(14, 10, 14, 10), BorderThickness = new Thickness(0, 0, 0, 1) };
                MetadataTrustUi.SetResource(conflictBorder, Border.BackgroundProperty, "NotificationBackgroundBrush");
                MetadataTrustUi.SetResource(conflictBorder, Border.BorderBrushProperty, "GlyphBrush");
                panel.Children.Add(conflictBorder);
            }

            var values = new Grid { Margin = new Thickness(14, 13, 14, 12) };
            values.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            values.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            values.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            values.Children.Add(BuildValuePanel(plugin.Loc("MTDA_SimulationBefore", "Before"), FormatPreview(change.Before), true));
            var after = BuildValuePanel(plugin.Loc("MTDA_SimulationAfter", "After"), FormatPreview(change.After), false);
            Grid.SetColumn(after, 2);
            values.Children.Add(after);
            panel.Children.Add(values);

            if (change.Provenance != null)
            {
                var provenance = MetadataTrustUi.Hint(
                    plugin.Loc("MTDA_ProvenanceSource", "Source") + ": " + MetadataTrustUi.ProvenanceSource(plugin, change.Provenance.Source) +
                    "  |  " + plugin.Loc("MTDA_ProvenanceConfidence", "Confidence") + ": " + MetadataTrustUi.Confidence(plugin, change.Provenance.Confidence),
                    new Thickness(14, 8, 14, 9));
                var provenanceBorder = new Border { Child = provenance, BorderThickness = new Thickness(0, 1, 0, 0) };
                MetadataTrustUi.SetResource(provenanceBorder, Border.BorderBrushProperty, "GlyphBrush");
                panel.Children.Add(provenanceBorder);
            }

            return card;
        }

        private void ChangeSelection(MetadataSimulationResult item, MetadataChangeItem change, bool selected)
        {
            if (updatingSelection) return;
            change.IsSelected = selected;
            UpdateGameSelector(item);
            UpdateSelectionState(false);
        }

        private void SetSelection(Func<MetadataChangeItem, bool> selector)
        {
            updatingSelection = true;
            foreach (var pair in fieldSelectors)
            {
                pair.Key.IsSelected = selector(pair.Key);
                pair.Value.IsChecked = pair.Key.IsSelected;
            }
            updatingSelection = false;
            UpdateSelectionState();
        }

        private void SetGameSelection(MetadataSimulationResult item, bool selected)
        {
            SetGameSelection(item, change => selected);
        }

        private void SetGameSelection(MetadataSimulationResult item, Func<MetadataChangeItem, bool> selector)
        {
            if (updatingSelection || item == null || item.Changes == null) return;
            updatingSelection = true;
            foreach (var change in item.Changes)
            {
                change.IsSelected = selector(change);
                CheckBox box;
                if (fieldSelectors.TryGetValue(change, out box)) box.IsChecked = change.IsSelected;
            }
            updatingSelection = false;
            UpdateSelectionState();
        }

        private void UpdateSelectionState(bool updateGames = true)
        {
            if (updateGames)
            {
                foreach (var item in results) UpdateGameSelector(item);
            }

            var allChanges = results.Where(x => x.Changes != null).SelectMany(x => x.Changes).ToList();
            var selected = allChanges.Count(x => x.IsSelected);
            var selectedMedia = results
                .Where(x => x.MediaChanges != null)
                .SelectMany(x => x.MediaChanges)
                .Count(x => x != null && x.Option != null && x.IsSelected);
            selectionSummary.Text = string.Format(
                plugin.Loc("MTDA_SimulationSelectedSummary", "{0} of {1} metadata changes selected | {2} media changes selected"),
                selected,
                allChanges.Count,
                selectedMedia);
            applyButton.IsEnabled = selected > 0 || selectedMedia > 0;
        }

        private void UpdateGameSelector(MetadataSimulationResult item)
        {
            if (item == null || item.Changes == null || item.Changes.Count == 0) return;
            CheckBox selector;
            if (!gameSelectors.TryGetValue(item, out selector)) return;
            var selected = item.Changes.Count(x => x.IsSelected);
            updatingSelection = true;
            selector.IsChecked = selected == 0 ? false : selected == item.Changes.Count ? (bool?)true : null;
            updatingSelection = false;
        }

        private string GameRecommendation(MetadataSimulationResult item)
        {
            var changes = item == null || item.Changes == null ? new List<MetadataChangeItem>() : item.Changes;
            var recommended = changes.Count(x => string.Equals(x.Recommendation, MetadataChangeRecommendationService.Recommended, StringComparison.OrdinalIgnoreCase));
            var keep = changes.Count(x => string.Equals(x.Recommendation, MetadataChangeRecommendationService.KeepCurrent, StringComparison.OrdinalIgnoreCase));
            if (recommended > 0 && keep == 0) return plugin.Loc("MTDA_SimulationWorthApplying", "Worth applying");
            if (recommended > 0) return plugin.Loc("MTDA_SimulationReviewRecommended", "Review recommended");
            if (changes.Any(x => string.Equals(x.Recommendation, MetadataChangeRecommendationService.Review, StringComparison.OrdinalIgnoreCase))) return plugin.Loc("MTDA_SimulationReviewRecommended", "Review recommended");
            return plugin.Loc("MTDA_SimulationKeepCurrent", "Keeping current data is safer");
        }

        private string GameRecommendationDetails(MetadataSimulationResult item)
        {
            var changes = item == null || item.Changes == null ? new List<MetadataChangeItem>() : item.Changes;
            var recommended = changes.Count(x => string.Equals(x.Recommendation, MetadataChangeRecommendationService.Recommended, StringComparison.OrdinalIgnoreCase));
            var review = changes.Count(x => string.Equals(x.Recommendation, MetadataChangeRecommendationService.Review, StringComparison.OrdinalIgnoreCase));
            var keep = changes.Count(x => string.Equals(x.Recommendation, MetadataChangeRecommendationService.KeepCurrent, StringComparison.OrdinalIgnoreCase));
            return string.Format(plugin.Loc("MTDA_SimulationRecommendationSummary", "Recommended: {0} | Review: {1} | Keep current: {2}"), recommended, review, keep);
        }

        private UIElement BuildRecommendationBadge(MetadataChangeItem change)
        {
            var label = MetadataTrustUi.Text(RecommendationBadgeLabel(change), false);
            label.Foreground = Brushes.White;
            label.FontWeight = FontWeights.SemiBold;
            label.VerticalAlignment = VerticalAlignment.Center;
            var badge = new Border
            {
                Child = label,
                Padding = new Thickness(9, 3, 9, 3),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            badge.Background = new SolidColorBrush(
                string.Equals(change.Recommendation, MetadataChangeRecommendationService.Recommended, StringComparison.OrdinalIgnoreCase)
                    ? Color.FromRgb(38, 126, 68)
                    : string.Equals(change.Recommendation, MetadataChangeRecommendationService.KeepCurrent, StringComparison.OrdinalIgnoreCase)
                        ? Color.FromRgb(76, 96, 122)
                        : Color.FromRgb(166, 108, 0));
            return badge;
        }

        private string RecommendationBadgeLabel(MetadataChangeItem change)
        {
            if (string.Equals(change.Recommendation, MetadataChangeRecommendationService.Recommended, StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_SimulationRecommended", "Recommended");
            if (string.Equals(change.Recommendation, MetadataChangeRecommendationService.KeepCurrent, StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_SimulationKeepBadge", "Keep");
            return plugin.Loc("MTDA_SimulationReview", "Review");
        }

        private static string FormatSentence(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return value;
            }

            var first = char.ToUpper(value[0], CultureInfo.CurrentUICulture);
            return first + value.Substring(1);
        }

        private string RecommendationReason(MetadataChangeItem change)
        {
            switch (change.RecommendationReason)
            {
                case MetadataChangeRecommendationService.ReasonMissing: return plugin.Loc("MTDA_SimulationReasonMissing", "fills an empty field");
                case MetadataChangeRecommendationService.ReasonTrusted: return plugin.Loc("MTDA_SimulationReasonTrusted", "supported by a trusted source or high-confidence evidence");
                case MetadataChangeRecommendationService.ReasonDeterministic: return plugin.Loc("MTDA_SimulationReasonDeterministic", "calculated locally using a deterministic rule");
                case MetadataChangeRecommendationService.ReasonAddsInformation: return plugin.Loc("MTDA_SimulationReasonAddsInformation", "adds information without reducing the current list");
                case MetadataChangeRecommendationService.ReasonLowConfidence: return plugin.Loc("MTDA_SimulationReasonLowConfidence", "the source confidence is low");
                case MetadataChangeRecommendationService.ReasonRemovesInformation: return plugin.Loc("MTDA_SimulationReasonRemovesInformation", "the proposed list contains fewer items than the current one");
                case MetadataChangeRecommendationService.ReasonShorterDescription: return plugin.Loc("MTDA_SimulationReasonShorterDescription", "the proposed description is substantially shorter");
                case MetadataChangeRecommendationService.ReasonEmptyResult: return plugin.Loc("MTDA_SimulationReasonEmptyResult", "the proposed value is empty");
                case MetadataChangeRecommendationService.ReasonSourceConflict: return plugin.Loc("MTDA_SimulationReasonSourceConflict", "trusted sources provide different values; automatic application is blocked");
                default: return plugin.Loc("MTDA_SimulationReasonReplacesExisting", "replaces existing information and should be reviewed");
            }
        }

        private UIElement BuildValuePanel(string label, string value, bool muted)
        {
            var panel = new StackPanel { MinHeight = 82 };
            var heading = MetadataTrustUi.Text(label);
            heading.FontWeight = FontWeights.SemiBold;
            heading.Opacity = muted ? 0.72 : 1;
            panel.Children.Add(heading);
            var text = MetadataTrustUi.Text(string.IsNullOrWhiteSpace(value) ? plugin.Loc("MTDA_None", "None") : value);
            text.Margin = new Thickness(0, 4, 0, 0);
            text.Opacity = muted ? 0.72 : 1;
            panel.Children.Add(text);
            var border = new Border { Child = panel, Padding = new Thickness(11), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) };
            MetadataTrustUi.SetResource(border, Border.BackgroundProperty, "StandardWindowBackgroundBrush");
            MetadataTrustUi.SetResource(border, Border.BorderBrushProperty, "DetailsViewBannerPanelBorderBrush");
            return border;
        }

        private static string FormatPreview(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) return value;
            value = Regex.Replace(value, "<br\\s*/?>", Environment.NewLine, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "</p\\s*>", Environment.NewLine + Environment.NewLine, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "<li[^>]*>", "- ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "</li\\s*>", Environment.NewLine, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "<[^>]+>", string.Empty);
            value = WebUtility.HtmlDecode(value).Trim();
            return value.Length > 1400 ? value.Substring(0, 1400) + "..." : value;
        }
    }

    public sealed class HistoryWindow : Window
    {
        private readonly MetaDataIAPlugin plugin;
        private readonly MetadataHistoryService history;
        private readonly ListBox list = new ListBox();
        private readonly StackPanel details = new StackPanel();
        private readonly Button undoAllButton = new Button();
        private readonly TextBlock statusText = new TextBlock();
        private readonly HashSet<Guid> gameIdFilter;
        private readonly string gameNameFilter;

        private bool HasGameFilter { get { return gameIdFilter != null && gameIdFilter.Count > 0; } }

        public HistoryWindow(MetaDataIAPlugin plugin, MetadataHistoryService history)
            : this(plugin, history, (IEnumerable<Guid>)null, null)
        {
        }

        public HistoryWindow(MetaDataIAPlugin plugin, MetadataHistoryService history, Guid? gameIdFilter, string gameNameFilter)
            : this(plugin, history, gameIdFilter.HasValue ? new[] { gameIdFilter.Value } : null, gameNameFilter)
        {
        }

        public HistoryWindow(MetaDataIAPlugin plugin, MetadataHistoryService history, IEnumerable<Guid> gameIds, string gameNameFilter)
        {
            this.plugin = plugin;
            this.history = history;
            gameIdFilter = gameIds == null ? null : new HashSet<Guid>(gameIds);
            this.gameNameFilter = gameNameFilter;
            var windowTitle = HasGameFilter
                ? gameIdFilter.Count == 1
                    ? string.Format(plugin.Loc("MTDA_HistoryGameTitle", "Metadata AI history - {0}"), gameNameFilter)
                    : string.Format(plugin.Loc("MTDA_HistorySelectedTitle", "Metadata AI history - {0} selected games"), gameIdFilter.Count)
                : plugin.Loc("MTDA_HistoryTitle", "Metadata AI history");
            Title = windowTitle;
            Width = 1080;
            Height = 720;
            MinWidth = 820;
            MinHeight = 540;
            ShowInTaskbar = false;
            MetadataTrustUi.ApplyWindowTheme(this);
            var owner = plugin.Api.Dialogs.GetCurrentAppWindow();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }

            var root = new Grid { Margin = new Thickness(18) };
            MetadataTrustUi.ApplyTextBrush(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = MetadataTrustUi.Text(windowTitle);
            title.FontSize = 22;
            title.FontWeight = FontWeights.SemiBold;
            var titleBorder = new Border { Child = title, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 8), Margin = new Thickness(0, 0, 0, 14) };
            MetadataTrustUi.SetResource(titleBorder, Border.BorderBrushProperty, "GlyphBrush");
            root.Children.Add(titleBorder);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            list.BorderThickness = new Thickness(0);
            list.Padding = new Thickness(0, 0, 12, 0);
            list.SelectionChanged += (s, e) => ShowOperation(SelectedOperation());
            body.Children.Add(list);
            var separator = new Border { BorderThickness = new Thickness(1, 0, 0, 0) };
            MetadataTrustUi.SetResource(separator, Border.BorderBrushProperty, "GlyphBrush");
            Grid.SetColumn(separator, 1);
            body.Children.Add(separator);
            var detailsScroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(14, 0, 4, 0) };
            Grid.SetColumn(detailsScroll, 2);
            body.Children.Add(detailsScroll);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            statusText.TextWrapping = TextWrapping.Wrap;
            statusText.Visibility = Visibility.Collapsed;
            statusText.Margin = new Thickness(2, 12, 2, 0);
            MetadataTrustUi.SetResource(statusText, TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetRow(statusText, 2);
            root.Children.Add(statusText);

            var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var clear = new Button { Content = plugin.Loc("MTDA_HistoryClear", "Clear history"), MinWidth = 130 };
            clear.Click += (s, e) => ClearHistory();
            clear.Visibility = HasGameFilter ? Visibility.Collapsed : Visibility.Visible;
            buttons.Children.Add(clear);
            undoAllButton.Content = plugin.Loc("MTDA_HistoryUndoAllSelected", "Undo all selected history");
            undoAllButton.MinWidth = 225;
            undoAllButton.Margin = new Thickness(0, 0, 8, 0);
            undoAllButton.Click += (s, e) => UndoSelectedOperation();
            undoAllButton.Visibility = HasGameFilter ? Visibility.Collapsed : Visibility.Visible;
            Grid.SetColumn(undoAllButton, 2);
            buttons.Children.Add(undoAllButton);
            var close = new Button { Content = plugin.Loc("MTDA_Close", "Close"), MinWidth = 100 };
            close.Click += (s, e) => DialogResult = false;
            Grid.SetColumn(close, 3);
            buttons.Children.Add(close);
            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);
            Content = root;
            ReloadOperations(null);
        }

        private MetadataHistoryOperation SelectedOperation()
        {
            var selected = list.SelectedItem as ListBoxItem;
            return selected == null ? null : selected.Tag as MetadataHistoryOperation;
        }

        private void ReloadOperations(Guid? preferredId)
        {
            list.Items.Clear();
            var operations = history.GetOperations();
            if (HasGameFilter)
            {
                operations = operations
                    .Where(x => x.Games != null && x.Games.Any(y => gameIdFilter.Contains(y.GameId)))
                    .ToList();
            }

            foreach (var operation in operations)
            {
                var panel = new StackPanel { Margin = new Thickness(2, 2, 2, 8) };
                var kind = MetadataTrustUi.Text(operation.Kind);
                kind.FontWeight = FontWeights.SemiBold;
                panel.Children.Add(kind);
                var operationGames = operation.Games ?? new List<MetadataHistoryGameEntry>();
                var relevantCount = HasGameFilter ? operationGames.Count(x => gameIdFilter.Contains(x.GameId)) : operationGames.Count;
                panel.Children.Add(MetadataTrustUi.Hint(operation.CreatedAt.ToString("g") + "  |  " + string.Format(plugin.Loc("MTDA_HistoryGamesCount", "{0} game(s)"), relevantCount), new Thickness(0, 3, 0, 0)));
                var row = new Border { Child = panel, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 5, 7, 5) };
                MetadataTrustUi.SetResource(row, Border.BorderBrushProperty, "GlyphBrush");
                list.Items.Add(new ListBoxItem { Content = row, Tag = operation, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }

            var match = preferredId.HasValue
                ? list.Items.Cast<ListBoxItem>().FirstOrDefault(x => ((MetadataHistoryOperation)x.Tag).Id == preferredId.Value)
                : null;
            if (match != null) list.SelectedItem = match;
            else if (list.Items.Count > 0) list.SelectedIndex = 0;
            else ShowOperation(null);
        }

        private void ShowOperation(MetadataHistoryOperation operation)
        {
            details.Children.Clear();
            undoAllButton.IsEnabled = operation != null;
            if (operation == null)
            {
                var emptyText = HasGameFilter
                    ? gameIdFilter.Count == 1
                        ? string.Format(plugin.Loc("MTDA_HistoryGameEmpty", "There is no recorded Metadata AI history for {0}."), gameNameFilter)
                        : plugin.Loc("MTDA_HistorySelectedEmpty", "There is no recorded Metadata AI history for the selected games.")
                    : plugin.Loc("MTDA_HistoryEmpty", "There are no recorded Metadata AI changes yet.");
                details.Children.Add(MetadataTrustUi.Hint(emptyText, new Thickness(0)));
                return;
            }

            var title = MetadataTrustUi.Text(operation.Kind);
            title.FontSize = 18;
            title.FontWeight = FontWeights.SemiBold;
            var operationHeader = new StackPanel();
            operationHeader.Children.Add(title);
            operationHeader.Children.Add(MetadataTrustUi.Hint(operation.CreatedAt.ToString("F"), new Thickness(0, 3, 0, 0)));
            var operationBorder = new Border { Child = operationHeader, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 9), Margin = new Thickness(0, 0, 0, 14) };
            MetadataTrustUi.SetResource(operationBorder, Border.BorderBrushProperty, "GlyphBrush");
            details.Children.Add(operationBorder);
            var entries = operation.Games ?? new List<MetadataHistoryGameEntry>();
            if (HasGameFilter)
            {
                entries = entries.Where(x => gameIdFilter.Contains(x.GameId)).ToList();
            }

            foreach (var entry in entries)
            {
                details.Children.Add(BuildGameEntry(operation, entry));
            }
        }

        private UIElement BuildGameEntry(MetadataHistoryOperation operation, MetadataHistoryGameEntry entry)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var image = BuildGameImage(entry.GameId);
            grid.Children.Add(image);

            var info = new StackPanel();
            var name = MetadataTrustUi.Text(entry.GameName);
            name.FontWeight = FontWeights.SemiBold;
            name.FontSize = 15;
            info.Children.Add(name);
            var fields = string.Join(", ", (entry.ChangedFields ?? new List<string>()).Select(x => MetadataTrustUi.FieldName(plugin, x)));
            info.Children.Add(MetadataTrustUi.Text(plugin.Loc("MTDA_HistoryChangedFields", "Changed fields") + ": " + fields));
            var sources = (entry.Provenance ?? new List<MetadataFieldProvenance>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Source))
                .Select(x => MetadataTrustUi.ProvenanceSource(plugin, x.Source))
                .Distinct()
                .ToList();
            if (sources.Count > 0)
            {
                info.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_ProvenanceSource", "Source") + ": " + string.Join(", ", sources), new Thickness(0, 4, 0, 0)));
            }

            var undoGame = new Button { Content = plugin.Loc("MTDA_HistoryUndoGame", "Undo this game"), MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            undoGame.Click += (s, e) => UndoGame(operation, entry);
            info.Children.Add(undoGame);
            Grid.SetColumn(info, 2);
            grid.Children.Add(info);
            return MetadataTrustUi.Card(grid, new Thickness(0, 0, 0, 10));
        }

        private UIElement BuildGameImage(Guid gameId)
        {
            var frame = new Border { Width = 86, Height = 100, BorderThickness = new Thickness(1), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            MetadataTrustUi.SetResource(frame, Border.BorderBrushProperty, "GlyphBrush");
            MetadataTrustUi.SetResource(frame, Border.BackgroundProperty, "ControlBackgroundBrush");
            try
            {
                var game = plugin.Api.Database.Games[gameId];
                var reference = game == null ? null : (!string.IsNullOrWhiteSpace(game.CoverImage) ? game.CoverImage : game.Icon);
                var path = string.IsNullOrWhiteSpace(reference) ? null : plugin.Api.Database.GetFullFilePath(reference);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    frame.Child = new Image { Source = bitmap, Stretch = Stretch.Uniform };
                }
            }
            catch { }
            return frame;
        }

        private void UndoGame(MetadataHistoryOperation operation, MetadataHistoryGameEntry entry)
        {
            var message = string.Format(plugin.Loc("MTDA_HistoryUndoGameConfirm", "Restore {0} to its state before this Metadata AI operation? Later manual changes to the same fields may be overwritten."), entry.GameName);
            if (!Confirm(message)) return;
            try
            {
                if (history.UndoGame(operation.Id, entry.GameId))
                {
                    SetStatus(string.Format(plugin.Loc("MTDA_HistoryUndoGameComplete", "Metadata AI restored {0}."), entry.GameName), false);
                    ReloadOperations(operation.Id);
                }
            }
            catch (Exception ex)
            {
                SetStatus(MetadataGenerationService.SanitizeForUser(ex.Message), true);
            }
        }

        private void UndoSelectedOperation()
        {
            var operation = SelectedOperation();
            if (operation == null) return;
            if (!Confirm(plugin.Loc("MTDA_HistoryUndoConfirm", "Restore every game changed by the selected operation to its previous Metadata AI state? Later manual changes to those same fields may be overwritten."))) return;
            try
            {
                var restored = history.UndoOperation(operation.Id);
                SetStatus(string.Format(plugin.Loc("MTDA_HistoryUndoComplete", "Metadata AI restored {0} game(s)."), restored), false);
                ReloadOperations(null);
            }
            catch (Exception ex)
            {
                SetStatus(MetadataGenerationService.SanitizeForUser(ex.Message), true);
            }
        }

        private void ClearHistory()
        {
            if (!Confirm(plugin.Loc("MTDA_HistoryClearConfirm", "Clear the Metadata AI history and its media backups? This cannot be undone."))) return;
            try
            {
                history.Clear();
                SetStatus(plugin.Loc("MTDA_HistoryCleared", "Metadata AI history was cleared."), false);
                ReloadOperations(null);
            }
            catch (Exception ex)
            {
                SetStatus(MetadataGenerationService.SanitizeForUser(ex.Message), true);
            }
        }

        private bool Confirm(string message)
        {
            return new MetadataConfirmationWindow(plugin, this, message).ShowDialog() == true;
        }

        private void SetStatus(string message, bool isError)
        {
            statusText.Text = message ?? string.Empty;
            statusText.Visibility = Visibility.Visible;
            statusText.FontWeight = isError ? FontWeights.SemiBold : FontWeights.Normal;
            statusText.Opacity = isError ? 1 : 0.82;
        }
    }

    internal sealed class MetadataConfirmationWindow : Window
    {
        public MetadataConfirmationWindow(MetaDataIAPlugin plugin, Window owner, string message)
        {
            Title = plugin.Loc("MTDA_PluginName", "Metadata AI");
            Width = 560;
            SizeToContent = SizeToContent.Height;
            MinHeight = 190;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            MetadataTrustUi.ApplyWindowTheme(this);

            var root = new Grid { Margin = new Thickness(20) };
            MetadataTrustUi.ApplyTextBrush(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = MetadataTrustUi.Text(message);
            text.FontSize = 14;
            text.Margin = new Thickness(0, 0, 0, 20);
            root.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var yes = new Button { Content = plugin.Loc("MTDA_Yes", "Yes"), MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
            yes.Click += (s, e) => DialogResult = true;
            var cancel = new Button { Content = plugin.Loc("MTDA_Cancel", "Cancel"), MinWidth = 100 };
            cancel.Click += (s, e) => DialogResult = false;
            buttons.Children.Add(yes);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);
            Content = root;
        }
    }

    internal sealed class MetadataNoticeWindow : Window
    {
        public MetadataNoticeWindow(MetaDataIAPlugin plugin, Window owner, string message)
        {
            Title = plugin.Loc("MTDA_PluginName", "Metadata AI");
            Width = 560;
            SizeToContent = SizeToContent.Height;
            MinHeight = 170;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            MetadataTrustUi.ApplyWindowTheme(this);

            var root = new Grid { Margin = new Thickness(20) };
            MetadataTrustUi.ApplyTextBrush(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = MetadataTrustUi.Text(message);
            text.FontSize = 14;
            text.Margin = new Thickness(0, 0, 0, 20);
            root.Children.Add(text);
            var close = new Button { Content = plugin.Loc("MTDA_Close", "Close"), MinWidth = 110, HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (s, e) => DialogResult = true;
            Grid.SetRow(close, 1);
            root.Children.Add(close);
            Content = root;
        }
    }

    internal sealed class MetadataAuditProgressWindow : Window
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Action<CancellationToken> operation;
        private readonly Button cancelButton = new Button();
        private bool completed;

        public Exception Error { get; private set; }
        public bool Cancelled { get; private set; }

        public MetadataAuditProgressWindow(MetaDataIAPlugin plugin, Window owner, string message, Action<CancellationToken> operation)
        {
            this.operation = operation;
            Title = plugin.Loc("MTDA_PluginName", "Metadata AI");
            Width = 520;
            SizeToContent = SizeToContent.Height;
            MinHeight = 180;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            MetadataTrustUi.ApplyWindowTheme(this);

            var root = new Grid { Margin = new Thickness(20) };
            MetadataTrustUi.ApplyTextBrush(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = MetadataTrustUi.Text(message);
            text.FontSize = 14;
            text.Margin = new Thickness(0, 0, 0, 16);
            root.Children.Add(text);
            var progress = new ProgressBar { IsIndeterminate = true, Height = 8, Margin = new Thickness(0, 0, 0, 18) };
            Grid.SetRow(progress, 1);
            root.Children.Add(progress);
            cancelButton.Content = plugin.Loc("MTDA_Cancel", "Cancel");
            cancelButton.MinWidth = 110;
            cancelButton.HorizontalAlignment = HorizontalAlignment.Right;
            cancelButton.Click += (s, e) => { Cancelled = true; cancelButton.IsEnabled = false; cancellation.Cancel(); };
            Grid.SetRow(cancelButton, 2);
            root.Children.Add(cancelButton);
            Content = root;

            Loaded += RunOperation;
            Closing += (s, e) =>
            {
                if (completed) return;
                e.Cancel = true;
                Cancelled = true;
                cancelButton.IsEnabled = false;
                cancellation.Cancel();
            };
        }

        private async void RunOperation(object sender, RoutedEventArgs e)
        {
            try
            {
                await Task.Run(() => operation(cancellation.Token));
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
            }
            catch (Exception ex)
            {
                Error = ex;
            }

            completed = true;
            if (IsVisible)
            {
                DialogResult = Error == null && !Cancelled;
            }
        }
    }

    public sealed class ProvenanceGameGroup
    {
        public string GameName { get; set; }
        public IEnumerable<MetadataFieldProvenance> Provenance { get; set; }
    }

    public sealed class ProvenanceWindow : Window
    {
        public ProvenanceWindow(MetaDataIAPlugin plugin, string gameName, IEnumerable<MetadataFieldProvenance> provenance)
            : this(plugin, new[] { new ProvenanceGameGroup { GameName = gameName, Provenance = provenance } })
        {
        }

        public ProvenanceWindow(MetaDataIAPlugin plugin, IEnumerable<ProvenanceGameGroup> values)
        {
            var groups = (values ?? Enumerable.Empty<ProvenanceGameGroup>()).Where(x => x != null).ToList();
            var multiple = groups.Count > 1;
            var titleValue = multiple
                ? string.Format(plugin.Loc("MTDA_ProvenanceSelectedTitle", "Metadata provenance - {0} selected games"), groups.Count)
                : plugin.Loc("MTDA_ProvenanceTitle", "Metadata provenance") + " - " + (groups.FirstOrDefault() == null ? string.Empty : groups[0].GameName);
            Title = titleValue;
            Width = 820;
            Height = 650;
            MinWidth = 640;
            MinHeight = 440;
            ShowInTaskbar = false;
            MetadataTrustUi.ApplyWindowTheme(this);
            var owner = plugin.Api.Dialogs.GetCurrentAppWindow();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }

            var root = new Grid { Margin = new Thickness(18) };
            MetadataTrustUi.ApplyTextBrush(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = MetadataTrustUi.Text(titleValue);
            title.FontSize = 21;
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(0, 0, 0, 14);
            root.Children.Add(title);

            var stack = new StackPanel();
            foreach (var group in groups)
            {
                if (multiple)
                {
                    var gameTitle = MetadataTrustUi.Text(group.GameName);
                    gameTitle.FontSize = 18;
                    gameTitle.FontWeight = FontWeights.SemiBold;
                    var gameHeader = new Border { Child = gameTitle, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 7), Margin = new Thickness(0, 4, 0, 12) };
                    MetadataTrustUi.SetResource(gameHeader, Border.BorderBrushProperty, "GlyphBrush");
                    stack.Children.Add(gameHeader);
                }
                foreach (var item in group.Provenance ?? Enumerable.Empty<MetadataFieldProvenance>())
                {
                    stack.Children.Add(BuildEntry(plugin, item));
                }
            }
            var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            var close = new Button { Content = plugin.Loc("MTDA_Close", "Close"), MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            close.Click += (s, e) => Close();
            Grid.SetRow(close, 2);
            root.Children.Add(close);
            Content = root;
        }

        private static UIElement BuildEntry(MetaDataIAPlugin plugin, MetadataFieldProvenance item)
        {
            var panel = new StackPanel();

            var facts = new Grid();
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            facts.Children.Add(BuildFact(plugin.Loc("MTDA_ProvenanceSource", "Source"), MetadataTrustUi.ProvenanceSource(plugin, item.Source)));
            var method = BuildFact(plugin.Loc("MTDA_ProvenanceMethod", "Method"), MetadataTrustUi.ProvenanceMethod(plugin, item.Method));
            Grid.SetColumn(method, 2);
            facts.Children.Add(method);
            panel.Children.Add(facts);
            panel.Children.Add(BuildFact(plugin.Loc("MTDA_ProvenanceConfidence", "Confidence"), MetadataTrustUi.Confidence(plugin, item.Confidence), new Thickness(0, 9, 0, 0)));
            panel.Children.Add(MetadataTrustUi.Hint(MetadataTrustUi.ProvenanceExplanation(plugin, item), new Thickness(0, 9, 0, 0)));
            if (string.Equals(item.Method, "downloaded-media", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Detail))
            {
                panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_ProvenanceOriginalUrl", "Original URL") + ": " + item.Detail, new Thickness(0, 5, 0, 0)));
            }
            return MetadataTrustUi.Section(MetadataTrustUi.FieldName(plugin, item.Field), panel, new Thickness(0, 0, 0, 16));
        }

        private static UIElement BuildFact(string label, string value, Thickness? margin = null)
        {
            var panel = new StackPanel { Margin = margin ?? new Thickness(0) };
            var heading = MetadataTrustUi.Text(label);
            heading.FontWeight = FontWeights.SemiBold;
            heading.Opacity = 0.76;
            panel.Children.Add(heading);
            var content = MetadataTrustUi.Text(value);
            content.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(content);
            return panel;
        }
    }
}
