using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

        public List<LibraryAuditIssue> Scan(IEnumerable<Game> games)
        {
            var issues = new List<LibraryAuditIssue>();
            foreach (var game in (games ?? Enumerable.Empty<Game>()).Where(x => x != null).GroupBy(x => x.Id).Select(x => x.First()))
            {
                AddMissing(issues, game, "Description", settings.GenerateDescription, string.IsNullOrWhiteSpace(game.Description));
                AddMissing(issues, game, "Genres", settings.GenerateGenres, game.GenreIds == null || game.GenreIds.Count == 0);
                AddMissing(issues, game, "Tags", settings.GenerateTags, game.TagIds == null || game.TagIds.Count == 0);
                AddMissing(issues, game, "Features", settings.GenerateFeatures, game.FeatureIds == null || game.FeatureIds.Count == 0);
                AddMissing(issues, game, "Developer", settings.GenerateDevelopers, game.DeveloperIds == null || game.DeveloperIds.Count == 0);
                AddMissing(issues, game, "Publisher", settings.GeneratePublishers, game.PublisherIds == null || game.PublisherIds.Count == 0);
                AddMissing(issues, game, "Age ratings", settings.GenerateAgeRatings, game.AgeRatingIds == null || game.AgeRatingIds.Count == 0);
                AddMissing(issues, game, "Regions", settings.GenerateRegions, game.RegionIds == null || game.RegionIds.Count == 0);
                AddMissing(issues, game, "Categories", settings.GenerateCategories, game.CategoryIds == null || game.CategoryIds.Count == 0);
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
            }
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

        private static void AddDuplicates(List<LibraryAuditIssue> issues, Game game, string field, IEnumerable<DatabaseObject> values)
        {
            var names = (values ?? Enumerable.Empty<DatabaseObject>()).Where(x => x != null).Select(x => Normalize(x.Name)).Where(x => x.Length > 0).ToList();
            if (names.Count != names.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                issues.Add(new LibraryAuditIssue { Game = game, Area = "Metadata", Field = field, Severity = "Info", Problem = "duplicate", IsRepairable = true });
        }

        private static string Normalize(string value) { return new string((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()); }
    }

    public sealed class LibraryAuditWindow : Window
    {
        private readonly MetaDataIAPlugin plugin;
        private readonly IList<LibraryAuditIssue> allIssues;
        private readonly Func<LibraryAuditIssue, bool> repair;
        private readonly Func<Game, IList<LibraryAuditIssue>> rescan;
        private readonly ListBox issues = new ListBox();
        private readonly CheckBox showHidden = new CheckBox();
        private readonly TextBlock summary = new TextBlock();
        private readonly TextBlock detailTitle = new TextBlock();
        private readonly TextBlock detailBody = new TextBlock();
        private readonly Button repairButton = new Button();

        public LibraryAuditIssue SelectedIssue
        {
            get
            {
                var item = issues.SelectedItem as ListBoxItem;
                return item == null ? null : item.Tag as LibraryAuditIssue;
            }
        }

        public LibraryAuditWindow(MetaDataIAPlugin plugin, IList<LibraryAuditIssue> data, Func<LibraryAuditIssue, bool> repair, Func<Game, IList<LibraryAuditIssue>> rescan)
        {
            this.plugin = plugin;
            this.allIssues = data ?? new List<LibraryAuditIssue>();
            this.repair = repair;
            this.rescan = rescan;
            Title = plugin.Loc("MTDA_AuditTitle", "Metadata AI library audit"); Width = 1050; Height = 720; MinWidth = 760; MinHeight = 480; ShowInTaskbar = false;
            MetadataTrustUi.ApplyWindowTheme(this);
            var owner = plugin.Api.Dialogs.GetCurrentAppWindow(); if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            var root = new DockPanel { Margin = new Thickness(18) };
            var heading = new TextBlock { Text = plugin.Loc("MTDA_AuditHeading", "Library health and selective repair"), FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            summary.TextWrapping = TextWrapping.Wrap; summary.Opacity = 0.75; summary.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(summary, Dock.Top); root.Children.Add(summary);
            showHidden.Content = plugin.Loc("MTDA_AuditShowHidden", "Show hidden games");
            showHidden.Margin = new Thickness(0, 0, 0, 14);
            showHidden.Checked += (s, e) => RefreshIssues();
            showHidden.Unchecked += (s, e) => RefreshIssues();
            DockPanel.SetDock(showHidden, Dock.Top); root.Children.Add(showHidden);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            repairButton.Content = plugin.Loc("MTDA_AuditRepairSelected", "Repair selected issue"); repairButton.MinWidth = 180; repairButton.Margin = new Thickness(0, 0, 8, 0); repairButton.IsEnabled = false;
            repairButton.Click += (s, e) =>
            {
                var selected = SelectedIssue;
                if (selected == null || repair == null) return;
                if (repair(selected)) RescanIssue(selected);
            };
            var close = new Button { Content = plugin.Loc("MTDA_Close", "Close"), MinWidth = 120 }; close.Click += (s, e) => Close();
            buttons.Children.Add(repairButton); buttons.Children.Add(close); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 270 });
            issues.BorderThickness = new Thickness(0); issues.Padding = new Thickness(0, 0, 14, 0); issues.SelectionChanged += (s, e) => RefreshDetails();
            Grid.SetColumn(issues, 0); content.Children.Add(issues);
            var separator = new Border { BorderThickness = new Thickness(1, 0, 0, 0) };
            MetadataTrustUi.SetResource(separator, Border.BorderBrushProperty, "GlyphBrush");
            Grid.SetColumn(separator, 1); content.Children.Add(separator);

            var detailStack = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            detailTitle.FontSize = 18; detailTitle.FontWeight = FontWeights.SemiBold; detailTitle.TextWrapping = TextWrapping.Wrap;
            detailBody.Margin = new Thickness(0, 10, 0, 0); detailBody.TextWrapping = TextWrapping.Wrap; detailBody.Opacity = 0.82;
            detailStack.Children.Add(detailTitle); detailStack.Children.Add(detailBody);
            Grid.SetColumn(detailStack, 2); content.Children.Add(detailStack);
            root.Children.Add(content);
            Content = root;
            RefreshIssues();
        }

        private void RefreshIssues()
        {
            var visible = allIssues.Where(x => x.Game != null && ((showHidden.IsChecked == true) || !x.Game.Hidden)).ToList();
            summary.Text = string.Format(plugin.Loc("MTDA_AuditSummary", "{0} issue(s) shown. Protected media is reported but never replaced."), visible.Count);
            issues.Items.Clear();
            foreach (var issue in visible) issues.Items.Add(CreateRow(issue));
            if (issues.Items.Count > 0) issues.SelectedIndex = 0; else RefreshDetails();
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
            var grid = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var text = new StackPanel();
            var title = MetadataTrustUi.Text(issue.Game.Name); title.FontWeight = FontWeights.SemiBold; title.FontSize = 15;
            text.Children.Add(title);
            var badges = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            badges.Children.Add(CreateBadge(FieldName(issue), true));
            badges.Children.Add(CreateBadge(ProblemText(issue), false));
            text.Children.Add(badges);
            if (!string.IsNullOrWhiteSpace(issue.SourceName))
            {
                var source = MetadataTrustUi.Text(string.Format(plugin.Loc("MTDA_AuditSource", "Source: {0}"), issue.SourceName)); source.Margin = new Thickness(0, 3, 0, 0); source.Opacity = 0.62; source.FontSize = 11;
                text.Children.Add(source);
            }
            grid.Children.Add(text);
            var border = new Border
            {
                Child = grid,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 7),
                Margin = new Thickness(0, 0, 0, 7)
            };
            MetadataTrustUi.SetResource(border, Border.BorderBrushProperty, "GlyphBrush");
            return new ListBoxItem
            {
                Content = border,
                Tag = issue,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
        }

        private static Border CreateBadge(string value, bool fieldBadge)
        {
            var label = MetadataTrustUi.Text(value);
            label.FontSize = 11;
            label.FontWeight = FontWeights.SemiBold;
            label.VerticalAlignment = VerticalAlignment.Center;
            var badge = new Border
            {
                Child = label,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 4, 8, 4),
                MinHeight = 24,
                Margin = new Thickness(0, 0, 6, 4)
            };
            MetadataTrustUi.SetResource(badge, Border.BorderBrushProperty, "GlyphBrush");
            MetadataTrustUi.SetResource(badge, Border.BackgroundProperty, fieldBadge ? "ControlBackgroundBrush" : "ButtonBackgroundBrush");
            return badge;
        }

        private void RefreshDetails()
        {
            var issue = SelectedIssue;
            if (issue == null)
            {
                detailTitle.Text = plugin.Loc("MTDA_AuditNoSelection", "Select an issue to see its details.");
                detailBody.Text = string.Empty;
                repairButton.IsEnabled = false;
                return;
            }
            detailTitle.Text = issue.Game.Name + " - " + FieldName(issue);
            detailBody.Text = ProblemExplanation(issue) + Environment.NewLine + Environment.NewLine + RecommendedAction(issue);
            repairButton.IsEnabled = issue.IsRepairable && !issue.IsLocked;
        }

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
            if (issue.Problem == "missing-value") return plugin.Loc("MTDA_AuditExplainValueMissing", "This field is enabled in Metadata AI settings but the game currently has no value assigned.");
            if (issue.Problem == "file-missing") return plugin.Loc("MTDA_AuditExplainFileMissing", "The game references an image path, but the file no longer exists on disk.");
            if (issue.Problem == "invalid") return plugin.Loc("MTDA_AuditExplainInvalid", "The referenced file exists, but it is damaged or is not a readable image.");
            if (issue.Problem == "blank") return plugin.Loc("MTDA_AuditExplainBlank", "The image is almost entirely black or transparent and is unlikely to be useful in the interface.");
            if (issue.Problem == "too-small") return string.Format(plugin.Loc("MTDA_AuditExplainTooSmall", "The image is {0}x{1}, below the minimum width configured for this media type."), issue.Width, issue.Height);
            return plugin.Loc("MTDA_AuditExplainDuplicate", "The field contains terms that normalize to the same value and may fragment or clutter the library.");
        }

        private string RecommendedAction(LibraryAuditIssue issue)
        {
            if (issue.IsLocked) return plugin.Loc("MTDA_AuditActionLocked", "This media is protected from the game's Metadata AI context menu. Allow replacement there before repairing it.");
            if (issue.MediaKind.HasValue) return plugin.Loc("MTDA_AuditActionRepair", "Use Repair selected issue to open the corresponding media search and choose a replacement.");
            if (issue.IsRepairable) return string.Format(plugin.Loc("MTDA_AuditActionRepairField", "Use Repair selected issue to run only the configured action for {0}. Other metadata fields will not be changed."), FieldName(issue));
            return plugin.Loc("MTDA_AuditActionReview", "Review this field in Playnite's game editor or run a Metadata AI simulation before applying changes.");
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
                throw new InvalidOperationException("No logo data was returned.");
            if (!IsInstalled(api))
                throw new InvalidOperationException("Extra Metadata Loader is not installed.");

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
