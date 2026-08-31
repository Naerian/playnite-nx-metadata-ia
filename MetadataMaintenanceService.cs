using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using Microsoft.Win32;

namespace MetaDataIAPlugin
{
    public class GameMediaLockState
    {
        public bool Cover { get; set; }
        public bool Icon { get; set; }
        public bool Background { get; set; }
        public bool Logo { get; set; }
    }

    public sealed class MetadataMaintenanceStateService
    {
        private readonly string path;
        private readonly object sync = new object();
        private Dictionary<Guid, GameMediaLockState> locks;

        public MetadataMaintenanceStateService(string pluginUserDataPath)
        {
            path = Path.Combine(pluginUserDataPath, "maintenance-state.json");
        }

        public bool IsLocked(Guid gameId, MediaKind kind)
        {
            lock (sync)
            {
                var state = Load().ContainsKey(gameId) ? Load()[gameId] : null;
                if (state == null) return false;
                if (kind == MediaKind.Cover) return state.Cover;
                if (kind == MediaKind.Icon) return state.Icon;
                if (kind == MediaKind.Background) return state.Background;
                return state.Logo;
            }
        }

        public bool Toggle(Guid gameId, MediaKind kind)
        {
            lock (sync)
            {
                GameMediaLockState state;
                if (!Load().TryGetValue(gameId, out state))
                {
                    state = new GameMediaLockState();
                    locks[gameId] = state;
                }

                bool value;
                if (kind == MediaKind.Cover) value = state.Cover = !state.Cover;
                else if (kind == MediaKind.Icon) value = state.Icon = !state.Icon;
                else if (kind == MediaKind.Background) value = state.Background = !state.Background;
                else value = state.Logo = !state.Logo;
                Save();
                return value;
            }
        }

        public void SetAll(Guid gameId, bool value)
        {
            lock (sync)
            {
                Load()[gameId] = new GameMediaLockState { Cover = value, Icon = value, Background = value, Logo = value };
                Save();
            }
        }

        private Dictionary<Guid, GameMediaLockState> Load()
        {
            if (locks != null) return locks;
            try
            {
                locks = File.Exists(path)
                    ? JsonConvert.DeserializeObject<Dictionary<Guid, GameMediaLockState>>(File.ReadAllText(path))
                    : null;
            }
            catch { locks = null; }
            return locks ?? (locks = new Dictionary<Guid, GameMediaLockState>());
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(locks, Formatting.Indented));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
    }

    public class MediaQualityInfo
    {
        public bool Exists { get; set; }
        public bool IsValid { get; set; }
        public bool IsTooSmall { get; set; }
        public bool IsMostlyBlank { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string FullPath { get; set; }
        public string Problem { get; set; }
        public long PixelArea { get { return (long)Width * Height; } }
    }

    public static class MediaQualityInspector
    {
        public static MediaQualityInfo Inspect(IPlayniteAPI api, Game game, MediaKind kind, MetaDataIASettings settings)
        {
            var result = new MediaQualityInfo();
            var reference = kind == MediaKind.Cover ? game.CoverImage : kind == MediaKind.Icon ? game.Icon : game.BackgroundImage;
            if (string.IsNullOrWhiteSpace(reference))
            {
                result.Problem = "missing";
                return result;
            }

            try
            {
                result.FullPath = ResolveMediaPath(api, game, reference);
                result.Exists = !string.IsNullOrWhiteSpace(result.FullPath) && File.Exists(result.FullPath);
                if (!result.Exists) { result.Problem = "file-missing"; return result; }
                using (var image = System.Drawing.Image.FromFile(result.FullPath))
                {
                    result.Width = image.Width;
                    result.Height = image.Height;
                    result.IsValid = image.Width > 0 && image.Height > 0;
                    var minimum = kind == MediaKind.Cover ? settings.MediaMinimumCoverWidth : kind == MediaKind.Icon ? settings.MediaMinimumIconWidth : settings.MediaMinimumBackgroundWidth;
                    result.IsTooSmall = settings.MediaMinimumQualityEnabled && image.Width < minimum;
                    result.IsMostlyBlank = IsMostlyBlank(image, kind);
                }
                result.Problem = result.IsMostlyBlank ? "blank" : result.IsTooSmall ? "too-small" : string.Empty;
            }
            catch
            {
                result.Problem = "invalid";
            }
            return result;
        }

        private static string ResolveMediaPath(IPlayniteAPI api, Game game, string reference)
        {
            if (Path.IsPathRooted(reference) && File.Exists(reference)) return reference;

            string fullPath = null;
            try { fullPath = api.Database.GetFullFilePath(reference); } catch { }
            if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath)) return fullPath;

            var normalized = reference.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            var candidates = new[]
            {
                Path.Combine(api.Paths.ConfigurationPath, normalized),
                Path.Combine(api.Paths.ConfigurationPath, "library", "files", normalized),
                Path.Combine(api.Paths.ConfigurationPath, "library", "files", game.Id.ToString(), Path.GetFileName(normalized))
            };
            return candidates.FirstOrDefault(File.Exists) ?? fullPath;
        }

        public static bool IsMateriallyBetter(MediaQualityInfo current, MediaPreviewOption proposed)
        {
            if (proposed == null) return false;
            if (current == null || !current.Exists || !current.IsValid || current.IsMostlyBlank) return true;
            if (proposed.Width <= 0 || proposed.Height <= 0) return false;
            if (current.IsTooSmall && proposed.Width > current.Width) return true;
            return (long)proposed.Width * proposed.Height >= current.PixelArea * 1.35;
        }

        private static bool IsMostlyBlank(System.Drawing.Image source, MediaKind kind)
        {
            using (var sample = new Bitmap(24, 24))
            using (var graphics = Graphics.FromImage(sample))
            {
                graphics.DrawImage(source, 0, 0, 24, 24);
                var blank = 0;
                for (var y = 0; y < 24; y++)
                for (var x = 0; x < 24; x++)
                {
                    var c = sample.GetPixel(x, y);
                    if ((kind != MediaKind.Icon && c.A < 12) || (c.A > 200 && c.R < 8 && c.G < 8 && c.B < 8)) blank++;
                }
                return blank >= 520;
            }
        }
    }

    public class LibraryAuditIssue
    {
        public Game Game { get; set; }
        public string Area { get; set; }
        public string Field { get; set; }
        public string Severity { get; set; }
        public string Problem { get; set; }
        public MediaKind? MediaKind { get; set; }
        public bool IsRepairable { get; set; }
        public bool IsLocked { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string SourceName { get; set; }
        public bool IsSuggestion { get; set; }
        [JsonIgnore]
        public MetaDataIASettings FocusedSettings { get; set; }
        [JsonIgnore]
        public bool SelectedForAction { get; set; }
        [JsonIgnore]
        public string LastRepairMessage { get; set; }
    }

    public sealed class LibraryAuditRepairResult
    {
        public bool Resolved { get; set; }
        public string Message { get; set; }
    }

    public sealed class LibraryAuditDecision
    {
        public Guid GameId { get; set; }
        public string Game { get; set; }
        public string Area { get; set; }
        public string Field { get; set; }
        public string Problem { get; set; }
        public string Action { get; set; }
    }

    public static class LibraryAuditDecisionFile
    {
        public static List<LibraryAuditDecision> Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return new List<LibraryAuditDecision>();
            return string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
                ? (JsonConvert.DeserializeObject<List<LibraryAuditDecision>>(File.ReadAllText(path)) ?? new List<LibraryAuditDecision>())
                : ReadCsv(File.ReadAllLines(path));
        }

        private static List<LibraryAuditDecision> ReadCsv(IEnumerable<string> lines)
        {
            var values = (lines ?? Enumerable.Empty<string>()).Skip(1).Select(ParseCsv).Where(x => x.Count >= 6).ToList();
            return values.Select(x => new LibraryAuditDecision
            {
                GameId = Guid.Parse(x[0]), Game = x[1], Area = x[2], Field = x[3], Problem = x[4], Action = x[5]
            }).ToList();
        }

        private static List<string> ParseCsv(string value)
        {
            var result = new List<string>(); var builder = new StringBuilder(); var quoted = false;
            foreach (var character in value ?? string.Empty)
            {
                if (character == '"') { quoted = !quoted; continue; }
                if (character == ',' && !quoted) { result.Add(builder.ToString()); builder.Clear(); continue; }
                builder.Append(character);
            }
            result.Add(builder.ToString());
            return result;
        }
    }

    public sealed class LibrarySnapshot
    {
        public int SchemaVersion { get; set; }
        public DateTime ExportedAt { get; set; }
        public List<Game> Games { get; set; }
    }

    public sealed class LibraryAuditService
    {
        private readonly IPlayniteAPI api;
        private readonly MetaDataIASettings settings;
        private readonly MetadataMaintenanceStateService state;

        public LibraryAuditService(IPlayniteAPI api, MetaDataIASettings settings, MetadataMaintenanceStateService state)
        {
            this.api = api; this.settings = settings; this.state = state;
        }

        public List<LibraryAuditIssue> Scan(
            IEnumerable<Game> games,
            CancellationToken cancellationToken = default(CancellationToken),
            Action<int, int, Game> progressChanged = null)
        {
            var issues = new List<LibraryAuditIssue>();
            var targets = (games ?? Enumerable.Empty<Game>()).Where(x => x != null).GroupBy(x => x.Id).Select(x => x.First()).ToList();
            for (var index = 0; index < targets.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var game = targets[index];
                if (progressChanged != null) progressChanged(index, targets.Count, game);
                AddMissing(issues, game, "Description", settings.GenerateDescription, string.IsNullOrWhiteSpace(game.Description));
                AddMissing(issues, game, "Genres", settings.GenerateGenres, game.GenreIds == null || game.GenreIds.Count == 0);
                AddMissing(issues, game, "Tags", settings.GenerateTags, game.TagIds == null || game.TagIds.Count == 0);
                AddMissing(issues, game, "Features", settings.GenerateFeatures, game.FeatureIds == null || game.FeatureIds.Count == 0);
                AddMissing(issues, game, "Developer", settings.GenerateDevelopers, game.DeveloperIds == null || game.DeveloperIds.Count == 0);
                AddMissing(issues, game, "Publisher", settings.GeneratePublishers, game.PublisherIds == null || game.PublisherIds.Count == 0);
                AddMissing(issues, game, "Age ratings", settings.GenerateAgeRatings, game.AgeRatingIds == null || game.AgeRatingIds.Count == 0);
                AddOptionalMissing(issues, game, "Regions", settings.GenerateRegions, game.RegionIds == null || game.RegionIds.Count == 0);
                AddOptionalMissing(issues, game, "Categories", settings.GenerateCategories, game.CategoryIds == null || game.CategoryIds.Count == 0);
                AddMissing(issues, game, "Links", settings.GenerateLinks, game.Links == null || game.Links.Count == 0);
                AddMissing(issues, game, "Release date", settings.GenerateReleaseDate, !game.ReleaseDate.HasValue);
                AddMissing(
                    issues,
                    game,
                    "Series",
                    settings.GenerateSeries,
                    (game.SeriesIds == null || game.SeriesIds.Count == 0) &&
                    SortingNameService.HasSeriesEvidence(api, game) &&
                    !string.IsNullOrWhiteSpace(SortingNameService.GenerateSeriesName(api, game)));
                AddMissing(
                    issues,
                    game,
                    "Sorting name",
                    settings.GenerateSortingName,
                    string.IsNullOrWhiteSpace(game.SortingName) && !string.IsNullOrWhiteSpace(SortingNameService.Generate(api, game)));
                AddDuplicates(issues, game, "Tags", game.Tags);
                AddDuplicates(issues, game, "Features", game.Features);
                AddDuplicates(issues, game, "Genres", game.Genres);
                AddDuplicates(issues, game, "Developers", game.Developers);
                AddDuplicates(issues, game, "Publishers", game.Publishers);
                AddDuplicates(issues, game, "Age ratings", game.AgeRatings);
                AddDuplicates(issues, game, "Regions", game.Regions);
                AddDuplicates(issues, game, "Categories", game.Categories);
                AddDuplicates(issues, game, "Series", game.Series);

                foreach (var kind in new[] { MediaKind.Cover, MediaKind.Icon, MediaKind.Background })
                {
                    var enabled = kind == MediaKind.Cover ? settings.DownloadCoverImage : kind == MediaKind.Icon ? settings.DownloadIcon : settings.DownloadBackgroundImage;
                    if (!enabled) continue;
                    var quality = MediaQualityInspector.Inspect(api, game, kind, settings);
                    if (string.IsNullOrWhiteSpace(quality.Problem)) continue;
                    issues.Add(new LibraryAuditIssue
                    {
                        Game = game,
                        Area = "Media",
                        Field = kind.ToString(),
                        Severity = quality.Exists ? "Warning" : "Error",
                        Problem = quality.Problem,
                        MediaKind = kind,
                        IsRepairable = true,
                        IsLocked = state.IsLocked(game.Id, kind),
                        Width = quality.Width,
                        Height = quality.Height
                    });
                }
                if (progressChanged != null) progressChanged(index + 1, targets.Count, game);
            }
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var issue in issues) issue.SourceName = GetSourceName(issue.Game);
            return issues
                .GroupBy(x => x.Game.Id + "|" + x.Area + "|" + x.Field + "|" + x.Problem, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderByDescending(x => x.Severity == "Error")
                .ThenBy(x => x.Game.Name)
                .ThenBy(x => x.Field)
                .ToList();
        }

        private string GetSourceName(Game game)
        {
            if (game == null) return string.Empty;
            if (game.Source != null && !string.IsNullOrWhiteSpace(game.Source.Name)) return game.Source.Name;
            if (game.PluginId == Guid.Empty || api.Addons == null || api.Addons.Plugins == null) return string.Empty;
            try
            {
                var plugin = api.Addons.Plugins.OfType<Playnite.SDK.Plugins.LibraryPlugin>().FirstOrDefault(x => x.Id == game.PluginId);
                return plugin == null ? string.Empty : plugin.Name;
            }
            catch { return string.Empty; }
        }

        private static void AddMissing(List<LibraryAuditIssue> issues, Game game, string field, bool enabled, bool missing)
        {
            if (enabled && missing) issues.Add(new LibraryAuditIssue { Game = game, Area = "Metadata", Field = field, Severity = "Warning", Problem = "missing-value", IsRepairable = true });
        }

        private static void AddOptionalMissing(List<LibraryAuditIssue> issues, Game game, string field, bool enabled, bool missing)
        {
            if (enabled && missing) issues.Add(new LibraryAuditIssue { Game = game, Area = "Suggestions", Field = field, Severity = "Info", Problem = "optional-missing", IsRepairable = false, IsSuggestion = true });
        }

        private static void AddDuplicates(List<LibraryAuditIssue> issues, Game game, string field, IEnumerable<DatabaseObject> values)
        {
            var names = (values ?? Enumerable.Empty<DatabaseObject>()).Where(x => x != null).Select(x => Normalize(x.Name)).Where(x => x.Length > 0).ToList();
            if (names.Count != names.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                issues.Add(new LibraryAuditIssue { Game = game, Area = "Metadata", Field = field, Severity = "Info", Problem = "duplicate", IsRepairable = true });
        }

        private static string Normalize(string value) { return new string((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()); }
    }

    public sealed class LibraryAuditWindow
    {
        private readonly Window window;
        private readonly MetaDataIAPlugin plugin;
        private readonly IList<LibraryAuditIssue> allIssues;
        private readonly Func<LibraryAuditIssue, bool, CancellationToken, LibraryAuditRepairResult> repair;
        private readonly Func<Game, IList<LibraryAuditIssue>> rescan;
        private readonly Action<Game> openEditor;
        private readonly bool showHiddenOption;
        private readonly ListBox issues = new ListBox();
        private readonly CheckBox showHidden = new CheckBox();
        private readonly CheckBox showSuggestions = new CheckBox();
        private readonly CheckBox selectAll = new CheckBox();
        private readonly ComboBox typeFilter = new ComboBox();
        private readonly ComboBox fieldFilter = new ComboBox();
        private readonly ComboBox statusFilter = new ComboBox();
        private readonly TextBlock summary = new TextBlock();
        private readonly TextBlock batchStatus = new TextBlock();
        private readonly Button historyButton = new Button();
        private readonly TextBlock detailTitle = new TextBlock();
        private readonly TextBlock detailSource = new TextBlock();
        private readonly TextBlock informationBody = new TextBlock();
        private readonly TextBlock resolutionBody = new TextBlock();
        private readonly TextBlock resultBody = new TextBlock();
        private readonly Button applySelectedButton = new Button();
        private readonly Button editGameButton = new Button();
        private readonly Button markModifiedButton = new Button();
        private Border resultSection;
        private string lastBatchSummary;
        private bool updatingFilters;

        private sealed class AuditFilterOption
        {
            public string Value { get; set; }
            public string DisplayName { get; set; }
        }

        public Window Host { get { return window; } }

        public LibraryAuditIssue SelectedIssue
        {
            get
            {
                var item = issues.SelectedItem as ListBoxItem;
                return item == null ? null : item.Tag as LibraryAuditIssue;
            }
        }

        public LibraryAuditWindow(
            MetaDataIAPlugin plugin,
            IList<LibraryAuditIssue> data,
            Func<LibraryAuditIssue, bool, CancellationToken, LibraryAuditRepairResult> repair,
            Func<Game, IList<LibraryAuditIssue>> rescan,
            Action<Game> openEditor,
            bool showHiddenOption)
        {
            this.plugin = plugin;
            this.allIssues = data ?? new List<LibraryAuditIssue>();
            this.repair = repair;
            this.rescan = rescan;
            this.openEditor = openEditor;
            this.showHiddenOption = showHiddenOption;
            // Playnite's themed owner relation can retain a dim backdrop after a
            // modeless audit is closed. The audit manages input blocking itself.
            window = MetadataTrustUi.CreatePluginDialog(
                plugin.Api,
                plugin.Loc("MTDA_AuditTitle", "Metadata AI library audit"),
                plugin.GetAppearancePreset(),
                1080,
                740,
                820,
                520,
                WindowStartupLocation.CenterScreen);
            window.Tag = this;

            StackPanel headerHost;
            Grid bodyHost;
            Border footerBar;
            var root = MetadataTrustUi.CreatePageShell(out headerHost, out bodyHost, out footerBar, null, plugin.GetAppearancePreset());

            headerHost.Children.Add(MetadataTrustUi.PageIntro(
                plugin.Loc("MTDA_AuditHeading", "Library health and selective repair")));
            summary.TextWrapping = TextWrapping.Wrap;
            summary.Margin = new Thickness(0, 8, 0, 0);
            if (!MetadataTrustUi.TrySetResource(summary, TextBlock.ForegroundProperty, "Narian.TextMuted"))
            {
                MetadataTrustUi.SetResource(summary, TextBlock.ForegroundProperty, "GlyphBrush");
            }
            summary.FontSize = 12;
            summary.FontStyle = FontStyles.Italic;
            headerHost.Children.Add(summary);

            batchStatus.TextWrapping = TextWrapping.Wrap;
            batchStatus.FontWeight = FontWeights.SemiBold;
            batchStatus.VerticalAlignment = VerticalAlignment.Center;
            MetadataTrustUi.SetResource(batchStatus, TextBlock.ForegroundProperty, "TextBrush");
            historyButton.Content = plugin.Loc("MTDA_AuditOpenHistory", "View change history");
            MetadataTrustUi.StyleSecondaryButton(historyButton);
            historyButton.MinWidth = 150;
            historyButton.Margin = new Thickness(14, 0, 0, 0);
            historyButton.Click += (s, e) => plugin.ShowHistory();
            var batchStatusPanel = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
            batchStatusPanel.Children.Add(batchStatus);
            batchStatusPanel.Children.Add(historyButton);
            var batchCard = MetadataTrustUi.SummaryCard(batchStatusPanel, new Thickness(0, 12, 0, 0));
            batchCard.Visibility = Visibility.Collapsed;
            headerHost.Children.Add(batchCard);

            showHidden.Content = plugin.Loc("MTDA_AuditShowHidden", "Show hidden games");
            showHidden.Checked += (s, e) => RefreshIssues();
            showHidden.Unchecked += (s, e) => RefreshIssues();
            showSuggestions.Content = plugin.Loc("MTDA_AuditShowSuggestions", "Show optional enrichment suggestions");
            showSuggestions.Checked += (s, e) => RefreshIssues();
            showSuggestions.Unchecked += (s, e) => RefreshIssues();
            selectAll.Content = plugin.Loc("MTDA_AuditSelectAll", "Select all");
            selectAll.Checked += (s, e) => SetAllVisibleSelections(true);
            selectAll.Unchecked += (s, e) => SetAllVisibleSelections(false);
            var auditOptions = new WrapPanel { Orientation = Orientation.Horizontal };
            selectAll.Margin = new Thickness(0, 0, 24, 4);
            showHidden.Margin = new Thickness(0, 0, 24, 4);
            showSuggestions.Margin = new Thickness(0, 0, 0, 4);
            auditOptions.Children.Add(selectAll);
            if (showHiddenOption) auditOptions.Children.Add(showHidden);
            auditOptions.Children.Add(showSuggestions);

            ConfigureFilter(typeFilter, plugin.Loc("MTDA_AuditFilterType", "Type"));
            ConfigureFilter(fieldFilter, plugin.Loc("MTDA_AuditFilterField", "Field"));
            ConfigureFilter(statusFilter, plugin.Loc("MTDA_AuditFilterStatus", "Status"));
            typeFilter.SelectionChanged += (s, e) => { if (!updatingFilters) RefreshIssues(); };
            fieldFilter.SelectionChanged += (s, e) => { if (!updatingFilters) RefreshIssues(); };
            statusFilter.SelectionChanged += (s, e) => { if (!updatingFilters) RefreshIssues(); };
            var filters = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var typeFilterControl = CreateFilterControl(plugin.Loc("MTDA_AuditFilterType", "Type"), typeFilter);
            var fieldFilterControl = CreateFilterControl(plugin.Loc("MTDA_AuditFilterField", "Field"), fieldFilter);
            var statusFilterControl = CreateFilterControl(plugin.Loc("MTDA_AuditFilterStatus", "Status"), statusFilter);
            filters.Children.Add(typeFilterControl);
            Grid.SetColumn(fieldFilterControl, 2); filters.Children.Add(fieldFilterControl);
            Grid.SetColumn(statusFilterControl, 4); filters.Children.Add(statusFilterControl);

            MetadataTrustUi.StyleCardListBox(issues);
            issues.SelectionChanged += (s, e) =>
            {
                foreach (ListBoxItem row in issues.Items)
                {
                    var chrome = row.Content as Border;
                    if (chrome != null) MetadataTrustUi.ApplyNavItemChrome(chrome, ReferenceEquals(row, issues.SelectedItem));
                }
                RefreshDetails();
            };

            var filtersBody = new StackPanel();
            filters.Margin = new Thickness(0);
            filtersBody.Children.Add(filters);
            auditOptions.Margin = new Thickness(0, 12, 0, 12);
            filtersBody.Children.Add(auditOptions);

            var issuePane = new DockPanel();
            DockPanel.SetDock(filtersBody, Dock.Top);
            issuePane.Children.Add(filtersBody);
            issuePane.Children.Add(issues);

            detailTitle.FontSize = 20;
            detailTitle.FontWeight = FontWeights.SemiBold;
            detailTitle.TextWrapping = TextWrapping.Wrap;
            detailTitle.Margin = new Thickness(0);
            if (!MetadataTrustUi.TrySetAccentForeground(detailTitle))
            {
                MetadataTrustUi.SetResource(detailTitle, TextBlock.ForegroundProperty, "TextBrush");
            }
            var titleSeparator = new Border
            {
                Child = detailTitle,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Background = System.Windows.Media.Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            MetadataTrustUi.ApplySeparatorBrush(titleSeparator);
            detailSource.FontSize = 12;
            detailSource.FontStyle = FontStyles.Italic;
            detailSource.Margin = new Thickness(0, 0, 0, 12);
            if (!MetadataTrustUi.TrySetResource(detailSource, TextBlock.ForegroundProperty, "Narian.TextMuted"))
            {
                MetadataTrustUi.SetResource(detailSource, TextBlock.ForegroundProperty, "GlyphBrush");
            }
            ConfigureDetailBody(informationBody);
            ConfigureDetailBody(resolutionBody);
            ConfigureDetailBody(resultBody);

            editGameButton.Content = plugin.Loc("MTDA_AuditOpenGameEditor", "Open game editor");
            editGameButton.Margin = new Thickness(0, 14, 8, 0);
            MetadataTrustUi.StyleSecondaryButton(editGameButton);
            editGameButton.MinWidth = 170;
            editGameButton.HorizontalAlignment = HorizontalAlignment.Left;
            editGameButton.Click += (s, e) => OpenSelectedGameEditor();
            markModifiedButton.Content = plugin.Loc("MTDA_AuditMarkModified", "Mark as modified");
            markModifiedButton.Margin = new Thickness(0, 14, 0, 0);
            MetadataTrustUi.StyleSecondaryButton(markModifiedButton);
            markModifiedButton.MinWidth = 165;
            markModifiedButton.HorizontalAlignment = HorizontalAlignment.Left;
            markModifiedButton.Click += (s, e) => MarkSelectedAsModified();
            var manualActions = new StackPanel { Orientation = Orientation.Horizontal };
            manualActions.Children.Add(editGameButton);
            manualActions.Children.Add(markModifiedButton);

            var identityCardBody = new StackPanel();
            identityCardBody.Children.Add(titleSeparator);
            identityCardBody.Children.Add(detailSource);
            identityCardBody.Children.Add(informationBody);
            var identityCard = MetadataTrustUi.SummaryCard(identityCardBody, new Thickness(0, 0, 0, 16));

            var resolutionCardBody = new StackPanel();
            resolutionCardBody.Children.Add(MetadataTrustUi.SummaryCardHeader(plugin.Loc("MTDA_AuditResolution", "Resolution")));
            resolutionCardBody.Children.Add(resolutionBody);
            resolutionCardBody.Children.Add(manualActions);
            var resolutionCard = MetadataTrustUi.SummaryCard(resolutionCardBody, new Thickness(0, 0, 0, 16));

            var resultBodyPanel = new StackPanel();
            resultBodyPanel.Children.Add(MetadataTrustUi.SummaryCardHeader(plugin.Loc("MTDA_AuditResolutionResult", "Resolution result")));
            resultBodyPanel.Children.Add(resultBody);
            resultSection = MetadataTrustUi.SummaryCard(resultBodyPanel, new Thickness(0));
            resultSection.Visibility = Visibility.Collapsed;

            var detailStack = new StackPanel();
            detailStack.Children.Add(identityCard);
            detailStack.Children.Add(resolutionCard);
            detailStack.Children.Add(resultSection);
            var detailScroll = new ScrollViewer
            {
                Content = detailStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var split = MetadataTrustUi.SplitPanes(issuePane, detailScroll, 0, 16);
            split.ColumnDefinitions[0].Width = new GridLength(3, GridUnitType.Star);
            split.ColumnDefinitions[2].Width = new GridLength(2, GridUnitType.Star);
            split.ColumnDefinitions[2].MinWidth = 280;
            bodyHost.Children.Add(split);

            var exportCsv = new Button { Content = plugin.Loc("MTDA_AuditExportCsv", "Export CSV") };
            MetadataTrustUi.StyleSecondaryButton(exportCsv);
            exportCsv.MinWidth = 110;
            exportCsv.Margin = new Thickness(0, 0, 8, 0);
            exportCsv.Click += (s, e) => ExportDecisions(false);
            var exportJson = new Button { Content = plugin.Loc("MTDA_AuditExportJson", "Export JSON") };
            MetadataTrustUi.StyleSecondaryButton(exportJson);
            exportJson.MinWidth = 115;
            exportJson.Margin = new Thickness(0, 0, 8, 0);
            exportJson.Click += (s, e) => ExportDecisions(true);
            var import = new Button { Content = plugin.Loc("MTDA_AuditImportDecisions", "Import decisions") };
            MetadataTrustUi.StyleSecondaryButton(import);
            import.MinWidth = 145;
            import.Click += (s, e) => ImportDecisions();
            var transferButtons = new StackPanel { Orientation = Orientation.Horizontal };
            transferButtons.Children.Add(exportCsv);
            transferButtons.Children.Add(exportJson);
            transferButtons.Children.Add(import);

            applySelectedButton.Content = plugin.Loc("MTDA_AuditApplySelected", "Repair marked issues");
            MetadataTrustUi.StylePrimaryButton(applySelectedButton);
            applySelectedButton.MinWidth = 185;
            applySelectedButton.Click += (s, e) => ApplySelected();
            var close = new Button { Content = plugin.Loc("MTDA_Close", "Close") };
            MetadataTrustUi.StyleSecondaryButton(close);
            close.MinWidth = 120;
            close.Click += (s, e) => window.Close();

            footerBar.Child = MetadataTrustUi.CreateFooterContent(transferButtons, applySelectedButton, close);
            MetadataTrustUi.SetDialogContent(window, root, plugin.GetAppearancePreset());
            RefreshIssues();
        }

        private void RefreshIssues()
        {
            UpdateFilterOptions();
            var visible = allIssues.Where(x => x.Game != null &&
                (!showHiddenOption || showHidden.IsChecked == true || !x.Game.Hidden) &&
                (showSuggestions.IsChecked == true || !x.IsSuggestion) &&
                MatchesFilters(x)).ToList();
            var actualCount = allIssues.Count(x => x.Game != null && !x.IsSuggestion && (!showHiddenOption || showHidden.IsChecked == true || !x.Game.Hidden));
            var suggestionCount = allIssues.Count(x => x.Game != null && x.IsSuggestion && (!showHiddenOption || showHidden.IsChecked == true || !x.Game.Hidden));
            summary.Text = string.Format(plugin.Loc("MTDA_AuditSummaryDetailed", "{0} issue(s) found. {1} optional enrichment suggestion(s) are available."), actualCount, suggestionCount);
            batchStatus.Text = lastBatchSummary ?? string.Empty;
            var statusPanel = batchStatus.Parent as StackPanel;
            if (statusPanel != null)
            {
                var visibility = string.IsNullOrWhiteSpace(batchStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
                statusPanel.Visibility = visibility;
                var statusCard = statusPanel.Parent as Border;
                if (statusCard != null) statusCard.Visibility = visibility;
            }
            issues.Items.Clear();
            foreach (var issue in visible) issues.Items.Add(CreateRow(issue));
            if (issues.Items.Count > 0) issues.SelectedIndex = 0; else RefreshDetails();
            RefreshActionState();
        }

        private void RescanIssue(LibraryAuditIssue selected)
        {
            if (selected == null || selected.Game == null || rescan == null) return;
            var game = selected.Game;
            var refreshed = (rescan(game) ?? new List<LibraryAuditIssue>())
                .Where(x => SameAuditField(x, selected))
                .ToList();

            for (var index = allIssues.Count - 1; index >= 0; index--)
            {
                if (SameAuditField(allIssues[index], selected)) allIssues.RemoveAt(index);
            }
            foreach (var issue in refreshed) allIssues.Add(issue);
            RefreshIssues();
        }

        private static bool SameAuditField(LibraryAuditIssue left, LibraryAuditIssue right)
        {
            return left != null && right != null &&
                   left.Game != null && right.Game != null &&
                   left.Game.Id == right.Game.Id &&
                   string.Equals(left.Area, right.Area, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.Field, right.Field, StringComparison.OrdinalIgnoreCase);
        }

        private ListBoxItem CreateRow(LibraryAuditIssue issue)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var select = new CheckBox { IsChecked = issue.SelectedForAction, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
            select.Checked += (s, e) => { issue.SelectedForAction = true; RefreshActionState(); };
            select.Unchecked += (s, e) => { issue.SelectedForAction = false; RefreshActionState(); };
            grid.Children.Add(select);
            var text = new StackPanel();
            var headerTrailing = string.IsNullOrWhiteSpace(issue.LastRepairMessage)
                ? null
                : (UIElement)CreateWarningBadge(plugin.Loc("MTDA_AuditReview", "Review"));
            // List item title uses the same accent + bottom border pattern as Resumen.
            text.Children.Add(MetadataTrustUi.SummaryCardHeader(issue.Game.Name, headerTrailing));
            var badges = new WrapPanel { Margin = new Thickness(0, 0, 0, 0) };
            badges.Children.Add(CreateFieldBadge(FieldName(issue), issue));
            badges.Children.Add(CreateBadge(ProblemText(issue), false));
            text.Children.Add(badges);
            Grid.SetColumn(text, 1); grid.Children.Add(text);
            var chrome = new Border { Child = grid };
            MetadataTrustUi.ApplyNavItemChrome(chrome, false);
            chrome.Padding = new Thickness(8, 10, 8, 10);
            return new ListBoxItem
            {
                Content = chrome,
                Tag = issue,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
        }

        private static void ConfigureDetailBody(TextBlock body)
        {
            body.Margin = new Thickness(0, 4, 0, 0);
            body.TextWrapping = TextWrapping.Wrap;
            MetadataTrustUi.SetResource(body, TextBlock.ForegroundProperty, "TextBrush");
        }

        private static UIElement CreateDetailSection(string title, TextBlock body, Thickness margin)
        {
            var section = new StackPanel { Margin = margin };
            section.Children.Add(MetadataTrustUi.SectionHeader(title, 14, new Thickness(0, 0, 0, 8)));
            section.Children.Add(body);
            return section;
        }

        private static Border CreateBadge(string value, bool fieldBadge)
        {
            return MetadataTrustUi.Badge(
                value,
                fieldBadge ? MetadataTrustUi.BadgeKind.Accent : MetadataTrustUi.BadgeKind.Warning);
        }

        private static Border CreateFieldBadge(string value, LibraryAuditIssue issue)
        {
            var kind = issue != null && issue.IsSuggestion
                ? MetadataTrustUi.BadgeKind.Warning
                : issue != null && issue.MediaKind.HasValue
                    ? MetadataTrustUi.BadgeKind.Success
                    : MetadataTrustUi.BadgeKind.Accent;
            return MetadataTrustUi.Badge(value, kind);
        }

        private static Border CreateWarningBadge(string value)
        {
            var badge = MetadataTrustUi.Badge(value, MetadataTrustUi.BadgeKind.Warning);
            badge.Margin = new Thickness(8, 0, 0, 0);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            return badge;
        }

        private void RefreshDetails()
        {
            var issue = SelectedIssue;
            if (issue == null)
            {
                detailTitle.Text = plugin.Loc("MTDA_AuditNoSelection", "Select an issue to see its details.");
                detailSource.Text = string.Empty;
                informationBody.Text = string.Empty;
                resolutionBody.Text = string.Empty;
                resultBody.Text = string.Empty;
                if (resultSection != null) resultSection.Visibility = Visibility.Collapsed;
                editGameButton.IsEnabled = false;
                markModifiedButton.IsEnabled = false;
                return;
            }
            detailTitle.Text = issue.Game.Name + " - " + FieldName(issue);
            detailSource.Text = string.IsNullOrWhiteSpace(issue.SourceName)
                ? string.Empty
                : string.Format(plugin.Loc("MTDA_AuditSource", "Source: {0}"), issue.SourceName);
            informationBody.Text = ProblemExplanation(issue);
            resolutionBody.Text = RecommendedAction(issue);
            resultBody.Text = issue.LastRepairMessage ?? string.Empty;
            if (resultSection != null) resultSection.Visibility = string.IsNullOrWhiteSpace(resultBody.Text) ? Visibility.Collapsed : Visibility.Visible;
            editGameButton.IsEnabled = issue.Game != null && openEditor != null;
            markModifiedButton.IsEnabled = issue != null;
            RefreshActionState();
        }

        private void MarkSelectedAsModified()
        {
            var selected = SelectedIssue;
            if (selected == null) return;
            allIssues.Remove(selected);
            RefreshIssues();
        }

        private void OpenSelectedGameEditor()
        {
            var selected = SelectedIssue;
            if (selected == null || selected.Game == null || openEditor == null) return;
            var owner = window.Owner;
            var ownerHitTestVisible = owner == null || owner.IsHitTestVisible;
            try
            {
                // Let Playnite own its editor without another visible themed child.
                // Otherwise a nested modal backdrop can remain after the editor closes.
                window.Hide();
                if (owner != null && owner.IsVisible) owner.IsHitTestVisible = true;
                openEditor(selected.Game);
            }
            finally
            {
                if (owner != null && owner.IsVisible) owner.IsHitTestVisible = ownerHitTestVisible;
                if (!window.IsVisible) window.Show();
                window.Activate();
            }
        }

        private void ConfigureFilter(ComboBox filter, string label)
        {
            filter.MinWidth = 150;
            filter.ToolTip = label;
            filter.DisplayMemberPath = "DisplayName";
            filter.SelectedValuePath = "Value";
        }

        private static StackPanel CreateFilterControl(string label, ComboBox filter)
        {
            var panel = new StackPanel();
            panel.Children.Add(MetadataTrustUi.FieldLabel(label));
            panel.Children.Add(filter);
            return panel;
        }

        private void UpdateFilterOptions()
        {
            updatingFilters = true;
            try
            {
                UpdateFilterItems(typeFilter, new[]
                {
                    new AuditFilterOption { Value = "all", DisplayName = plugin.Loc("MTDA_AuditFilterAllTypes", "All types") },
                    new AuditFilterOption { Value = "metadata", DisplayName = plugin.Loc("MTDA_AuditFilterMetadata", "Metadata") },
                    new AuditFilterOption { Value = "media", DisplayName = plugin.Loc("MTDA_AuditFilterMedia", "Media") },
                    new AuditFilterOption { Value = "suggestion", DisplayName = plugin.Loc("MTDA_AuditFilterSuggestions", "Suggestions") }
                });
                var fields = allIssues.Where(x => x != null && x.Game != null)
                    .Select(x => new { Value = x.Field ?? string.Empty, Name = FieldName(x) })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                    .GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new AuditFilterOption { Value = x.First().Value, DisplayName = x.First().Name })
                    .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                fields.Insert(0, new AuditFilterOption { Value = "all", DisplayName = plugin.Loc("MTDA_AuditFilterAllFields", "All fields") });
                UpdateFilterItems(fieldFilter, fields);
                UpdateFilterItems(statusFilter, new[]
                {
                    new AuditFilterOption { Value = "all", DisplayName = plugin.Loc("MTDA_AuditFilterAllStates", "All states") },
                    new AuditFilterOption { Value = "pending", DisplayName = plugin.Loc("MTDA_AuditFilterPending", "Pending") },
                    new AuditFilterOption { Value = "review", DisplayName = plugin.Loc("MTDA_AuditFilterReview", "Requires review") }
                });
            }
            finally { updatingFilters = false; }
        }

        private static void UpdateFilterItems(ComboBox filter, IEnumerable<AuditFilterOption> values)
        {
            var selected = filter.SelectedValue as string ?? "all";
            filter.ItemsSource = values.ToList();
            filter.SelectedValue = selected;
            if (filter.SelectedIndex < 0) filter.SelectedIndex = 0;
        }

        private bool MatchesFilters(LibraryAuditIssue issue)
        {
            var type = typeFilter.SelectedValue as string ?? "all";
            var field = fieldFilter.SelectedValue as string ?? "all";
            var status = statusFilter.SelectedValue as string ?? "all";
            if (type == "metadata" && (issue.MediaKind.HasValue || issue.IsSuggestion)) return false;
            if (type == "media" && !issue.MediaKind.HasValue) return false;
            if (type == "suggestion" && !issue.IsSuggestion) return false;
            if (field != "all" && !string.Equals(field, issue.Field, StringComparison.OrdinalIgnoreCase)) return false;
            if (status == "pending" && !string.IsNullOrWhiteSpace(issue.LastRepairMessage)) return false;
            if (status == "review" && string.IsNullOrWhiteSpace(issue.LastRepairMessage)) return false;
            return true;
        }

        private void RefreshActionState()
        {
            if (applySelectedButton != null)
            {
                applySelectedButton.IsEnabled = allIssues.Any(x => x.SelectedForAction && x.IsRepairable && !x.IsLocked);
            }
        }

        private void SetAllVisibleSelections(bool selected)
        {
            foreach (var issue in allIssues.Where(x => x.Game != null && (!showHiddenOption || showHidden.IsChecked == true || !x.Game.Hidden)))
            {
                if (issue.IsRepairable && !issue.IsLocked) issue.SelectedForAction = selected;
            }
            RefreshIssues();
        }

        private void ApplySelected()
        {
            var selected = allIssues.Where(x => x.SelectedForAction && x.IsRepairable && !x.IsLocked).ToList();
            if (selected.Count == 0) return;
            var workItems = selected.Select(issue => Tuple.Create(issue, CreateBackgroundCopy(issue))).ToList();
            var completed = new List<Tuple<LibraryAuditIssue, LibraryAuditRepairResult>>();
            var progress = new MetadataAuditProgressWindow(
                plugin,
                window,
                string.Format(plugin.Loc("MTDA_AuditBatchProgress", "Resolving marked issues: {0} of {1}"), 0, workItems.Count),
                (token, report) =>
                {
                    for (var index = 0; index < workItems.Count; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        var originalIssue = workItems[index].Item1;
                        var workerIssue = workItems[index].Item2;
                        report(string.Format(
                            plugin.Loc("MTDA_AuditBatchProgressGame", "Resolving marked issues: {0} of {1} - {2}"),
                            index + 1,
                            workItems.Count,
                            originalIssue.Game == null ? string.Empty : originalIssue.Game.Name));
                        LibraryAuditRepairResult result;
                        try { result = repair(workerIssue, true, token) ?? new LibraryAuditRepairResult(); }
                        catch (Exception ex) { result = new LibraryAuditRepairResult { Resolved = false, Message = ex.Message }; }
                        completed.Add(Tuple.Create(originalIssue, result));
                    }
                });
            progress.ShowUntilCompleted();
            var repaired = 0;
            var unresolved = 0;
            foreach (var attempt in completed)
            {
                var issue = attempt.Item1;
                var result = attempt.Item2;
                if (result.Resolved)
                {
                    repaired++;
                    RescanIssue(issue);
                }
                else
                {
                    unresolved++;
                    issue.SelectedForAction = false;
                    issue.LastRepairMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? plugin.Loc("MTDA_AuditNoReliableValue", "No reliable value could be determined for this field. The issue will remain in the audit.")
                        : result.Message;
                }
            }
            lastBatchSummary = string.Format(plugin.Loc("MTDA_AuditBatchResult", "Repair complete: {0} resolved · {1} need review."), repaired, unresolved);
            RefreshIssues();
            RefreshActionState();
        }

        private LibraryAuditIssue CreateBackgroundCopy(LibraryAuditIssue issue)
        {
            if (issue == null) return null;
            var copy = new LibraryAuditIssue
            {
                Game = issue.Game == null ? null : Serialization.GetClone(issue.Game),
                Area = issue.Area,
                Field = issue.Field,
                Severity = issue.Severity,
                Problem = issue.Problem,
                MediaKind = issue.MediaKind,
                IsRepairable = issue.IsRepairable,
                IsLocked = issue.IsLocked,
                Width = issue.Width,
                Height = issue.Height,
                SourceName = issue.SourceName,
                IsSuggestion = issue.IsSuggestion
            };
            copy.FocusedSettings = plugin.CreateAuditFocusedSettings(copy);
            return copy;
        }

        private List<LibraryAuditDecision> Decisions()
        {
            return allIssues.Where(x => x != null && x.Game != null).Select(x => new LibraryAuditDecision
            {
                GameId = x.Game.Id, Game = x.Game.Name, Area = x.Area, Field = x.Field, Problem = x.Problem,
                Action = x.SelectedForAction ? "Apply" : "Skip"
            }).ToList();
        }

        private void ExportDecisions(bool json)
        {
            if (plugin.Api.Dialogs.ShowMessage(plugin.Loc("MTDA_AuditDecisionExportHelp", "This export lists the issues found by the audit. Edit only the Action column: Apply selects an issue for repair and Skip leaves it unchanged. It does not edit game metadata directly."), plugin.Loc("MTDA_AuditTitle", "Metadata AI library audit"), MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            {
                return;
            }
            var dialog = new SaveFileDialog
            {
                Filter = json ? "JSON (*.json)|*.json" : "CSV (*.csv)|*.csv",
                FileName = "metadata-ai-audit-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + (json ? ".json" : ".csv")
            };
            if (dialog.ShowDialog(window) != true) return;
            var decisions = Decisions();
            if (json)
            {
                File.WriteAllText(dialog.FileName, JsonConvert.SerializeObject(decisions, Formatting.Indented), Encoding.UTF8);
            }
            else
            {
                var lines = new List<string> { "GameId,Game,Area,Field,Problem,Action" };
                lines.AddRange(decisions.Select(x => string.Join(",", Csv(x.GameId.ToString()), Csv(x.Game), Csv(x.Area), Csv(x.Field), Csv(x.Problem), Csv(x.Action))));
                File.WriteAllLines(dialog.FileName, lines, Encoding.UTF8);
            }
        }

        private void ImportDecisions()
        {
            var dialog = new OpenFileDialog { Filter = "Audit decisions (*.json;*.csv)|*.json;*.csv|JSON (*.json)|*.json|CSV (*.csv)|*.csv" };
            if (dialog.ShowDialog(window) != true) return;
            List<LibraryAuditDecision> decisions;
            try { decisions = LibraryAuditDecisionFile.Read(dialog.FileName); }
            catch
            {
                plugin.Api.Dialogs.ShowMessage(plugin.Loc("MTDA_AuditImportInvalid", "The audit file could not be read."), plugin.Loc("MTDA_AuditTitle", "Metadata AI library audit"));
                return;
            }
            foreach (var decision in decisions ?? new List<LibraryAuditDecision>())
            {
                var issue = allIssues.FirstOrDefault(x => x.Game != null && x.Game.Id == decision.GameId &&
                    string.Equals(x.Area, decision.Area, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Field, decision.Field, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Problem, decision.Problem, StringComparison.OrdinalIgnoreCase));
                if (issue != null) issue.SelectedForAction = string.Equals(decision.Action, "Apply", StringComparison.OrdinalIgnoreCase);
            }
            RefreshIssues();
        }

        private static string Csv(string value) { return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\""; }

        private string FieldName(LibraryAuditIssue issue)
        {
            if (issue.Area == "Media")
            {
                if (issue.Field == "Cover") return plugin.Loc("MTDA_Cover", "Cover");
                if (issue.Field == "Icon") return plugin.Loc("MTDA_Icon", "Icon");
                if (issue.Field == "Background") return plugin.Loc("MTDA_Background", "Background");
            }
            return MetadataTrustUi.FieldName(plugin, FieldKey(issue.Field));
        }

        private static string FieldKey(string value)
        {
            if (value == "Description") return "description";
            if (value == "Genres") return "genres";
            if (value == "Tags") return "tags";
            if (value == "Features") return "features";
            if (value == "Developer") return "developers";
            if (value == "Publisher") return "publishers";
            if (value == "Developers") return "developers";
            if (value == "Publishers") return "publishers";
            if (value == "Age ratings") return "ageRatings";
            if (value == "Regions") return "regions";
            if (value == "Categories") return "categories";
            if (value == "Links") return "links";
            if (value == "Release date") return "releaseDate";
            if (value == "Series") return "series";
            if (value == "Sorting name") return "sortingName";
            return value;
        }

        private string ProblemText(LibraryAuditIssue issue)
        {
            if (issue.Problem == "missing" || issue.Problem == "missing-value") return plugin.Loc("MTDA_AuditProblemMissing", "Missing value");
            if (issue.Problem == "optional-missing") return plugin.Loc("MTDA_AuditProblemOptional", "Optional enrichment");
            if (issue.Problem == "file-missing") return plugin.Loc("MTDA_AuditProblemFileMissing", "Referenced file is missing");
            if (issue.Problem == "invalid") return plugin.Loc("MTDA_AuditProblemInvalid", "Image cannot be read");
            if (issue.Problem == "blank") return plugin.Loc("MTDA_AuditProblemBlank", "Image is almost blank");
            if (issue.Problem == "too-small") return string.Format(plugin.Loc("MTDA_AuditProblemTooSmall", "Low resolution ({0}x{1})"), issue.Width, issue.Height);
            if (issue.Problem == "duplicate") return plugin.Loc("MTDA_AuditProblemDuplicate", "Duplicate or equivalent terms");
            return issue.Problem;
        }

        private string ProblemExplanation(LibraryAuditIssue issue)
        {
            if (issue.Problem == "missing" && issue.MediaKind.HasValue)
                return plugin.Loc("MTDA_AuditExplainMediaMissing", "No game-specific image is assigned. Playnite or the active theme may still display a platform, source, or theme fallback image.");
            if (issue.Problem == "optional-missing") return plugin.Loc("MTDA_AuditExplainOptional", "This optional library field is empty. Playnite integrations often do not provide it, so it is offered only as an enrichment suggestion.");
            if (issue.Problem == "missing-value") return plugin.Loc("MTDA_AuditExplainValueMissing", "This field is enabled in Metadata AI settings but the game currently has no value assigned.");
            if (issue.Problem == "file-missing") return plugin.Loc("MTDA_AuditExplainFileMissing", "The game references an image path, but the file no longer exists on disk.");
            if (issue.Problem == "invalid") return plugin.Loc("MTDA_AuditExplainInvalid", "The referenced file exists, but it is damaged or is not a readable image.");
            if (issue.Problem == "blank") return plugin.Loc("MTDA_AuditExplainBlank", "The image is almost entirely black or transparent and is unlikely to be useful in the interface.");
            if (issue.Problem == "too-small") return string.Format(plugin.Loc("MTDA_AuditExplainTooSmall", "The image is {0}x{1}, below the minimum width configured for this media type."), issue.Width, issue.Height);
            return plugin.Loc("MTDA_AuditExplainDuplicate", "The field contains terms that normalize to the same value and may fragment or clutter the library.");
        }

        private string RecommendedAction(LibraryAuditIssue issue)
        {
            if (issue.IsSuggestion) return plugin.Loc("MTDA_AuditActionOptional", "Optional field: review or fill it manually in Playnite if it is useful for your library organization.");
            if (issue.IsLocked) return plugin.Loc("MTDA_AuditActionLocked", "This media is protected from the game's Metadata AI context menu. Allow replacement there before repairing it.");
            if (issue.MediaKind.HasValue) return plugin.Loc("MTDA_AuditActionRepair", "Use Repair selected issue to open the corresponding media search and choose a replacement.");
            if (issue.IsRepairable) return string.Format(plugin.Loc("MTDA_AuditActionRepairField", "Use Repair selected issue to run only the configured action for {0}. Other metadata fields will not be changed."), FieldName(issue));
            return plugin.Loc("MTDA_AuditActionReview", "Review this field in Playnite's game editor or run a Metadata AI simulation before applying changes.");
        }

    }

    public sealed class LibraryTransferSelectionWindow
    {
        private readonly Window window;
        private readonly List<Game> allGames;
        private readonly IPlayniteAPI api;
        private readonly ListBox list = new ListBox();
        private readonly CheckBox selectAll = new CheckBox();
        private readonly HashSet<Guid> selectedIds = new HashSet<Guid>();
        public List<Game> Result { get; private set; }

        public LibraryTransferSelectionWindow(MetaDataIAPlugin plugin, IEnumerable<Game> allGames)
        {
            this.allGames = (allGames ?? Enumerable.Empty<Game>()).Where(x => x != null).ToList();
            api = plugin.Api;
            window = MetadataTrustUi.CreatePluginDialog(
                plugin.Api,
                plugin.Loc("MTDA_LibraryExportScopeTitle", "Choose games to export"),
                plugin.GetAppearancePreset(),
                720,
                620,
                560,
                420);
            var root = new DockPanel { Margin = new Thickness(18) };
            SettingsAppearance.ApplyPresetResources(root.Resources, plugin.GetAppearancePreset());
            MetadataTrustUi.ApplyPageBackground(root);
            var heading = MetadataTrustUi.Text(plugin.Loc("MTDA_LibraryExportScopeHelp", "Choose whether to export your complete library or only the games currently selected in Playnite.")); heading.TextWrapping = TextWrapping.Wrap; heading.Margin = new Thickness(0, 0, 0, 12); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            selectAll.Content = plugin.Loc("MTDA_LibrarySelectAll", "Select all"); selectAll.Margin = new Thickness(0, 0, 0, 12); selectAll.Checked += (s, e) => { foreach (var game in this.allGames) selectedIds.Add(game.Id); Refresh(); }; selectAll.Unchecked += (s, e) => { selectedIds.Clear(); Refresh(); }; DockPanel.SetDock(selectAll, Dock.Top); root.Children.Add(selectAll);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; var confirm = new Button { Content = plugin.Loc("MTDA_Continue", "Continue"), MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) }; confirm.Click += (s, e) => { Result = this.allGames.Where(x => selectedIds.Contains(x.Id)).ToList(); window.DialogResult = true; }; var cancel = new Button { Content = plugin.Loc("MTDA_Cancel", "Cancel"), MinWidth = 120 }; cancel.Click += (s, e) => window.DialogResult = false; buttons.Children.Add(confirm); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            list.BorderThickness = new Thickness(0); root.Children.Add(list);
            MetadataTrustUi.SetDialogContent(window, root, plugin.GetAppearancePreset());
            Refresh();
        }

        public bool? ShowDialog()
        {
            return window.ShowDialog();
        }

        private void Refresh()
        {
            list.Items.Clear();
            foreach (var game in allGames.Take(250))
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3), Cursor = Cursors.Hand };
                var check = new CheckBox { IsChecked = selectedIds.Contains(game.Id), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }; check.Checked += (s, e) => selectedIds.Add(game.Id); check.Unchecked += (s, e) => selectedIds.Remove(game.Id); panel.Children.Add(check);
                panel.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource is CheckBox) return; check.IsChecked = check.IsChecked != true; };
                var image = new System.Windows.Controls.Image { Width = 34, Height = 34, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 8, 0) }; var hasImage = TrySetImage(image, game); panel.Children.Add(hasImage ? (UIElement)image : DefaultIcon());
                var text = MetadataTrustUi.Text(game.Name); text.VerticalAlignment = VerticalAlignment.Center; text.TextWrapping = TextWrapping.Wrap; panel.Children.Add(text); list.Items.Add(panel);
            }
        }

        private bool TrySetImage(System.Windows.Controls.Image image, Game game)
        {
            var path = ResolveMediaPath(game == null ? null : game.CoverImage) ?? ResolveMediaPath(game == null ? null : game.Icon); if (path == null) return false;
            try { image.Source = new BitmapImage(new Uri(path, UriKind.Absolute)); return true; } catch { return false; }
        }

        private string ResolveMediaPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null; if (Path.IsPathRooted(value) && File.Exists(value)) return value;
            try { var path = api.Database.GetFullFilePath(value); if (File.Exists(path)) return path; } catch { }
            return null;
        }

        private static UIElement DefaultIcon()
        {
            var fallback = MetadataTrustUi.Text("🎮"); fallback.Width = 34; fallback.Height = 34; fallback.FontSize = 18; fallback.TextAlignment = TextAlignment.Center; fallback.VerticalAlignment = VerticalAlignment.Center; fallback.Margin = new Thickness(0, 0, 8, 0); return fallback;
        }
    }

    public sealed class LibraryTransferPreviewWindow
    {
        private readonly Window window;
        public bool Confirmed { get; private set; }
        public List<string> SelectedEntries { get; private set; }
        public LibraryTransferPreviewWindow(MetaDataIAPlugin plugin, string summary, IEnumerable<string> entries)
        {
            window = MetadataTrustUi.CreatePluginDialog(
                plugin.Api,
                plugin.Loc("MTDA_LibraryImportPreviewTitle", "Review library import"),
                plugin.GetAppearancePreset(),
                720,
                560,
                520,
                380);
            var values = (entries ?? Enumerable.Empty<string>()).Take(250).ToList(); var selected = new HashSet<string>(values);
            var root = new DockPanel { Margin = new Thickness(18) };
            SettingsAppearance.ApplyPresetResources(root.Resources, plugin.GetAppearancePreset());
            MetadataTrustUi.ApplyPageBackground(root);
            var heading = MetadataTrustUi.Text(summary); heading.TextWrapping = TextWrapping.Wrap; heading.Margin = new Thickness(0, 0, 0, 12); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            var all = new CheckBox { Content = plugin.Loc("MTDA_LibrarySelectAll", "Select all"), IsChecked = true, Margin = new Thickness(0, 0, 0, 12) }; all.Checked += (s, e) => { selected.Clear(); foreach (var value in values) selected.Add(value); }; all.Unchecked += (s, e) => selected.Clear(); DockPanel.SetDock(all, Dock.Top); root.Children.Add(all);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; var confirm = new Button { Content = plugin.Loc("MTDA_ImportLibrarySnapshot", "Import library"), MinWidth = 140, Margin = new Thickness(0, 0, 8, 0) }; confirm.Click += (s, e) => { SelectedEntries = selected.ToList(); Confirmed = true; window.DialogResult = true; }; var cancel = new Button { Content = plugin.Loc("MTDA_Cancel", "Cancel"), MinWidth = 120 }; cancel.Click += (s, e) => window.DialogResult = false; buttons.Children.Add(confirm); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var list = new ListBox { BorderThickness = new Thickness(0) }; foreach (var entry in values) { var separator = entry.IndexOf('|'); var label = separator >= 0 ? entry.Substring(separator + 1) : entry; var check = new CheckBox { Content = label, IsChecked = true, Margin = new Thickness(4) }; check.Checked += (s, e) => selected.Add(entry); check.Unchecked += (s, e) => selected.Remove(entry); list.Items.Add(check); } root.Children.Add(list);
            MetadataTrustUi.SetDialogContent(window, root, plugin.GetAppearancePreset());
        }

        public bool? ShowDialog()
        {
            return window.ShowDialog();
        }
    }

    public static class ExtraMetadataLogoService
    {
        public const string ExtensionId = "ExtraMetadataLoader_705fdbca-e1fc-4004-b839-1d040b8b4429";
        public static readonly Guid PluginId = Guid.Parse("705fdbca-e1fc-4004-b839-1d040b8b4429");

        public static bool IsInstalled(IPlayniteAPI api)
        {
            return api != null && Directory.Exists(Path.Combine(api.Paths.ConfigurationPath, "Extensions", ExtensionId));
        }

        public static string GetLogoPath(IPlayniteAPI api, Guid gameId)
        {
            return Path.Combine(api.Paths.ConfigurationPath, "ExtraMetadata", "games", gameId.ToString(), "Logo.png");
        }

        public static void Apply(IPlayniteAPI api, Game game, GeneratedMediaFile file)
        {
            if (api == null || game == null || file == null || file.Content == null || file.Content.Length == 0)
                throw new InvalidOperationException(PluginLocalization.GetString("MTDA_ErrorNoLogoData", "No logo data was returned."));
            if (!IsInstalled(api))
                throw new InvalidOperationException(PluginLocalization.GetString("MTDA_ErrorEmlNotInstalled", "Extra Metadata Loader is not installed."));

            var path = GetLogoPath(api, game.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            using (var source = System.Drawing.Image.FromStream(new MemoryStream(file.Content)))
            {
                source.Save(temp, System.Drawing.Imaging.ImageFormat.Png);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            api.Database.Games.Update(game);
            NotifyLogoUpdated(api, game);
        }

        private static void NotifyLogoUpdated(IPlayniteAPI api, Game game)
        {
            try
            {
                var plugin = api.Addons == null || api.Addons.Plugins == null
                    ? null
                    : api.Addons.Plugins.FirstOrDefault(x => x.Id == PluginId);
                if (plugin == null) return;
                var method = plugin.GetType().GetMethod("OnLogoUpdated", new[] { typeof(Game) });
                if (method != null) method.Invoke(plugin, new object[] { game });
            }
            catch
            {
                // The file is already valid; older Extra Metadata Loader versions can refresh on the next selection.
            }
        }
    }
}
