using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Plugin-owned settings chrome. Overrides Playnite theme brushes on the
    /// settings UserControl so existing DynamicResource markup picks up presets.
    /// </summary>
    public static class SettingsAppearance
    {
        public const string Default = "Default";
        public const string Midnight = "Midnight";
        public const string Paper = "Paper";
        public const string Oled = "OLED";
        public const string Ocean = "Ocean";
        public const string Ember = "Ember";

        public static readonly string[] AllPresets =
        {
            Default, Midnight, Paper, Oled, Ocean, Ember
        };

        public sealed class Palette
        {
            public Color Bg { get; set; }
            public Color Surface { get; set; }
            public Color Hover { get; set; }
            public Color Selected { get; set; }
            public Color Accent { get; set; }
            public Color AccentHover { get; set; }
            public Color AccentOn { get; set; }
            public Color Text { get; set; }
            public Color TextMuted { get; set; }
            public Color Border { get; set; }
            public Color Success { get; set; }
            public Color Warning { get; set; }
            public Color RowOdd { get; set; }
            public Color RowEven { get; set; }
            public Color TableHeader { get; set; }
            public Color BadgeBg { get; set; }
            public Color BadgeSuccessBg { get; set; }
            public Color BadgeWarningBg { get; set; }
            public Color BadgeMutedBg { get; set; }
            public bool IsLight { get; set; }
        }

        private static readonly Dictionary<string, Palette> Palettes =
            new Dictionary<string, Palette>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    Midnight, new Palette
                    {
                        Bg = Hex("#12151C"),
                        Surface = Hex("#1A1F2A"),
                        Hover = Hex("#242B3A"),
                        Selected = Hex("#2A3348"),
                        Accent = Hex("#6EA8FF"),
                        AccentHover = Hex("#8BBBFF"),
                        AccentOn = Hex("#0B0D12"),
                        Text = Hex("#EEF1F6"),
                        TextMuted = Hex("#8B93A7"),
                        Border = Hex("#2A3140"),
                        Success = Hex("#3DDC97"),
                        Warning = Hex("#E6B84D"),
                        RowOdd = Hex("#161A22"),
                        RowEven = Hex("#1A1F2A"),
                        TableHeader = Hex("#222836"),
                        BadgeBg = Hex("#242B3A"),
                        BadgeSuccessBg = Hex("#1B3A2E"),
                        BadgeWarningBg = Hex("#3A3220"),
                        BadgeMutedBg = Hex("#2A3140"),
                        IsLight = false
                    }
                },
                {
                    Oled, new Palette
                    {
                        Bg = Hex("#000000"),
                        Surface = Hex("#0A0A0A"),
                        Hover = Hex("#161616"),
                        Selected = Hex("#1E1E1E"),
                        Accent = Hex("#6EA8FF"),
                        AccentHover = Hex("#8BBBFF"),
                        AccentOn = Hex("#0B0D12"),
                        Text = Hex("#F2F2F2"),
                        TextMuted = Hex("#9A9A9A"),
                        Border = Hex("#222222"),
                        Success = Hex("#3DDC97"),
                        Warning = Hex("#E6B84D"),
                        RowOdd = Hex("#050505"),
                        RowEven = Hex("#0A0A0A"),
                        TableHeader = Hex("#141414"),
                        BadgeBg = Hex("#161616"),
                        BadgeSuccessBg = Hex("#0F2A20"),
                        BadgeWarningBg = Hex("#2A2414"),
                        BadgeMutedBg = Hex("#1E1E1E"),
                        IsLight = false
                    }
                },
                {
                    Ocean, new Palette
                    {
                        Bg = Hex("#0E151C"),
                        Surface = Hex("#15202B"),
                        Hover = Hex("#1C2B3A"),
                        Selected = Hex("#243648"),
                        Accent = Hex("#3DDCB4"),
                        AccentHover = Hex("#5FE6C4"),
                        AccentOn = Hex("#0B0D12"),
                        Text = Hex("#E8F1F7"),
                        TextMuted = Hex("#8AA0B0"),
                        Border = Hex("#243040"),
                        Success = Hex("#3DDC97"),
                        Warning = Hex("#E6B84D"),
                        RowOdd = Hex("#101820"),
                        RowEven = Hex("#15202B"),
                        TableHeader = Hex("#1A2836"),
                        BadgeBg = Hex("#1C2B3A"),
                        BadgeSuccessBg = Hex("#16362C"),
                        BadgeWarningBg = Hex("#35301C"),
                        BadgeMutedBg = Hex("#243040"),
                        IsLight = false
                    }
                },
                {
                    Ember, new Palette
                    {
                        Bg = Hex("#161311"),
                        Surface = Hex("#1F1A16"),
                        Hover = Hex("#2A231E"),
                        Selected = Hex("#332B24"),
                        Accent = Hex("#E8A05C"),
                        AccentHover = Hex("#F0B57A"),
                        AccentOn = Hex("#0B0D12"),
                        Text = Hex("#F3EEE8"),
                        TextMuted = Hex("#A89888"),
                        Border = Hex("#3A3028"),
                        Success = Hex("#3DDC97"),
                        Warning = Hex("#E6B84D"),
                        RowOdd = Hex("#1A1613"),
                        RowEven = Hex("#1F1A16"),
                        TableHeader = Hex("#26201B"),
                        BadgeBg = Hex("#2A231E"),
                        BadgeSuccessBg = Hex("#1E3428"),
                        BadgeWarningBg = Hex("#3A2E1C"),
                        BadgeMutedBg = Hex("#3A3028"),
                        IsLight = false
                    }
                },
                {
                    Paper, new Palette
                    {
                        Bg = Hex("#F7F8FA"),
                        Surface = Hex("#FFFFFF"),
                        Hover = Hex("#EEF1F6"),
                        Selected = Hex("#E4ECFB"),
                        Accent = Hex("#3B6FE8"),
                        AccentHover = Hex("#2F5FD4"),
                        AccentOn = Hex("#FFFFFF"),
                        Text = Hex("#1A1F2A"),
                        TextMuted = Hex("#5C6578"),
                        Border = Hex("#D5DAE3"),
                        Success = Hex("#1B8A5A"),
                        Warning = Hex("#B8860B"),
                        RowOdd = Hex("#FFFFFF"),
                        RowEven = Hex("#F3F5F8"),
                        TableHeader = Hex("#E8ECF2"),
                        BadgeBg = Hex("#EEF1F6"),
                        BadgeSuccessBg = Hex("#E3F5EC"),
                        BadgeWarningBg = Hex("#F7F0D9"),
                        BadgeMutedBg = Hex("#E8ECF2"),
                        IsLight = true
                    }
                }
            };

        public static string Normalize(string preset)
        {
            if (string.IsNullOrWhiteSpace(preset))
            {
                return Default;
            }

            foreach (var id in AllPresets)
            {
                if (string.Equals(id, preset.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return id;
                }
            }

            return Default;
        }

        public static Palette GetPalette(string preset)
        {
            preset = Normalize(preset);
            if (string.Equals(preset, Default, StringComparison.OrdinalIgnoreCase))
            {
                return SampleThemePalette();
            }

            return Palettes[preset];
        }

        /// <summary>
        /// Default preset: Playnite TextBrush / HighlightGlyphBrush, plus a bg/surface pair
        /// from the theme when available (or a derived equivalent when only one exists).
        /// </summary>
        private static Palette SampleThemePalette()
        {
            var midnight = Palettes[Midnight];
            var text = ColorFromTheme("TextBrush", midnight.Text);
            var accent = ColorFromTheme("HighlightGlyphBrush", midnight.Accent);
            Color bg;
            Color surface;
            ResolveThemeSurfaces(midnight, out bg, out surface);

            var hover = Mix(surface, text, 0.08);
            var border = Mix(surface, text, 0.14);
            var selected = Mix(surface, accent, 0.18);
            var isLight = RelativeLuminance(bg) >= 0.55;

            return new Palette
            {
                Bg = bg,
                Surface = surface,
                Hover = hover,
                Selected = selected,
                Border = border,
                RowOdd = Mix(bg, surface, 0.35),
                RowEven = surface,
                TableHeader = Mix(surface, text, 0.06),
                BadgeBg = hover,
                BadgeSuccessBg = Mix(surface, midnight.Success, 0.22),
                BadgeWarningBg = Mix(surface, midnight.Warning, 0.22),
                BadgeMutedBg = Mix(surface, border, 0.35),
                Success = midnight.Success,
                Warning = midnight.Warning,
                IsLight = isLight,
                Text = text,
                TextMuted = Mix(text, midnight.TextMuted, 0.55),
                Accent = accent,
                AccentHover = Mix(accent, text, 0.18),
                AccentOn = ContrastOn(accent)
            };
        }

        /// <summary>
        /// Picks page bg + raised surface from the theme.
        /// Desktop usually has WindowBackgourndBrush (Playnite typo) + PopupBackgroundBrush;
        /// ControlBackgroundBrush is often Transparent and is skipped.
        /// If only one opaque color exists, derive the missing level. If none, use Midnight.
        /// </summary>
        private static void ResolveThemeSurfaces(Palette midnight, out Color bg, out Color surface)
        {
            Color windowBg;
            Color popup;
            Color control;
            Color controlDark;
            var hasWindow = TryGetOpaqueThemeColor("WindowBackgourndBrush", out windowBg)
                || TryGetOpaqueThemeColor("WindowBackBrush", out windowBg);
            var hasPopup = TryGetOpaqueThemeColor("PopupBackgroundBrush", out popup);
            var hasControl = TryGetOpaqueThemeColor("ControlBackgroundBrush", out control);
            var hasControlDark = TryGetOpaqueThemeColor("ControlBackgroundDarkBrush", out controlDark);

            // Ideal Desktop pair.
            if (hasWindow && hasPopup && !ColorsTooSimilar(windowBg, popup))
            {
                OrderSurfacePair(windowBg, popup, out bg, out surface);
                return;
            }

            // Ideal Fullscreen pair.
            if (hasControlDark && hasControl && !ColorsTooSimilar(controlDark, control))
            {
                OrderSurfacePair(controlDark, control, out bg, out surface);
                return;
            }

            if (hasPopup && hasControl && !ColorsTooSimilar(popup, control))
            {
                OrderSurfacePair(popup, control, out bg, out surface);
                return;
            }

            // Single opaque color → derive the other level so cards still separate from page bg.
            if (hasPopup)
            {
                DeriveSurfacePair(popup, out bg, out surface);
                return;
            }

            if (hasWindow)
            {
                DeriveSurfacePair(windowBg, out bg, out surface);
                return;
            }

            if (hasControlDark)
            {
                DeriveSurfacePair(controlDark, out bg, out surface);
                return;
            }

            if (hasControl)
            {
                DeriveSurfacePair(control, out bg, out surface);
                return;
            }

            bg = midnight.Bg;
            surface = midnight.Surface;
        }

        private static void OrderSurfacePair(Color a, Color b, out Color bg, out Color surface)
        {
            // Darker = page bg, lighter = raised surface (works for light themes too via luminance).
            if (RelativeLuminance(a) <= RelativeLuminance(b))
            {
                bg = a;
                surface = b;
            }
            else
            {
                bg = b;
                surface = a;
            }
        }

        private static void DeriveSurfacePair(Color seed, out Color bg, out Color surface)
        {
            var black = Hex("#000000");
            var white = Hex("#FFFFFF");
            if (RelativeLuminance(seed) >= 0.55)
            {
                // Light theme seed: page a bit darker, surface = seed.
                bg = Mix(seed, black, 0.08);
                surface = seed;
                if (ColorsTooSimilar(bg, surface))
                {
                    bg = Mix(seed, black, 0.14);
                }
            }
            else
            {
                // Dark theme seed: page darker, surface lifted.
                bg = Mix(seed, black, 0.22);
                surface = Mix(seed, white, 0.08);
                if (ColorsTooSimilar(bg, surface))
                {
                    surface = Mix(seed, white, 0.14);
                }
            }
        }

        private static bool TryGetOpaqueThemeColor(string key, out Color color)
        {
            color = default(Color);
            try
            {
                var app = Application.Current;
                if (app == null || !TryExtractColor(app.TryFindResource(key), out color))
                {
                    return false;
                }

                // Desktop themes often set ControlBackgroundBrush to Transparent.
                return color.A >= 220;
            }
            catch
            {
                return false;
            }
        }

        private static bool ColorsTooSimilar(Color a, Color b)
        {
            var dr = a.R - b.R;
            var dg = a.G - b.G;
            var db = a.B - b.B;
            return ((dr * dr) + (dg * dg) + (db * db)) < (28 * 28);
        }

        private static Color ColorFromTheme(string key, Color fallback)
        {
            try
            {
                var app = Application.Current;
                if (app == null)
                {
                    return fallback;
                }

                Color color;
                return TryExtractColor(app.TryFindResource(key), out color) && color.A >= 32
                    ? color
                    : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static bool TryExtractColor(object resource, out Color color)
        {
            color = default(Color);
            var solid = resource as SolidColorBrush;
            if (solid != null)
            {
                color = solid.Color;
                return true;
            }

            var gradient = resource as GradientBrush;
            if (gradient != null && gradient.GradientStops != null && gradient.GradientStops.Count > 0)
            {
                // Prefer the most opaque stop (window gradients often start transparent).
                var bestA = -1;
                foreach (var stop in gradient.GradientStops)
                {
                    if (stop == null)
                    {
                        continue;
                    }

                    if (stop.Color.A > bestA)
                    {
                        bestA = stop.Color.A;
                        color = stop.Color;
                    }
                }

                return bestA >= 0;
            }

            if (resource is Color)
            {
                color = (Color)resource;
                return true;
            }

            return false;
        }

        private static Color ContrastOn(Color background)
        {
            return RelativeLuminance(background) >= 0.55
                ? Hex("#0B0D12")
                : Hex("#FFFFFF");
        }

        private static double RelativeLuminance(Color color)
        {
            return (0.2126 * LuminanceChannel(color.R))
                + (0.7152 * LuminanceChannel(color.G))
                + (0.0722 * LuminanceChannel(color.B));
        }

        private static double LuminanceChannel(byte channel)
        {
            var s = channel / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        private static Color Mix(Color from, Color to, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                (byte)Math.Round(from.A + ((to.A - from.A) * amount)),
                (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
                (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
                (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
        }

        public static void Apply(Control root, string preset)
        {
            if (root == null)
            {
                return;
            }

            var palette = GetPalette(preset);
            EnsureChromeResources(root.Resources);
            ApplyBrushes(root.Resources, palette);
            root.Background = BrushOf(palette.Bg);
            root.SetValue(TextElement.ForegroundProperty, BrushOf(palette.Text));
        }

        /// <summary>
        /// Themes a standalone plugin window (setup wizard, etc.) with the same
        /// chrome as settings. Does not restyle foreign host Save/Cancel buttons.
        /// </summary>
        public static void ApplyWindow(Window window, string preset)
        {
            ApplyWindow(window, preset, true);
        }

        public static void ApplyWindow(Window window, string preset, bool paintWindowSurface)
        {
            ApplyWindow(window, preset, paintWindowSurface, true);
        }

        public static void ApplyWindow(Window window, string preset, bool paintWindowSurface, bool mergeControlStyles)
        {
            if (window == null)
            {
                return;
            }

            var palette = GetPalette(preset);
            if (mergeControlStyles)
            {
                EnsureChromeResources(window.Resources);
            }
            ApplyBrushes(window.Resources, palette);
            var bg = BrushOf(palette.Bg);
            var text = BrushOf(palette.Text);
            window.Foreground = text;
            window.SetValue(TextElement.ForegroundProperty, text);
            window.Background = bg;
            window.BorderBrush = BrushOf(palette.Border);
            ApplyNativeFrame(window, palette);
            EventHandler sourceInitialized = null;
            sourceInitialized = (s, e) =>
            {
                window.SourceInitialized -= sourceInitialized;
                ApplyNativeFrame(window, palette);
            };
            window.SourceInitialized += sourceInitialized;

            var contentControl = window.Content as Control;
            if (contentControl != null)
            {
                contentControl.Background = bg;
                contentControl.SetValue(TextElement.ForegroundProperty, text);
            }
            else
            {
                var contentPanel = window.Content as Panel;
                if (contentPanel != null)
                {
                    contentPanel.Background = bg;
                    contentPanel.SetValue(TextElement.ForegroundProperty, text);
                }
                else
                {
                    var contentBorder = window.Content as Border;
                    if (contentBorder != null)
                    {
                        contentBorder.Background = bg;
                    }
                }
            }
        }

        public static void RefreshPlayniteTitleBar(Window window)
        {
            if (window == null)
            {
                return;
            }

            TryPaintWindowTitleTexts(window, window.Content as DependencyObject, window.Foreground);
        }

        public static void ApplyPresetResources(ResourceDictionary resources, string preset)
        {
            if (resources == null)
            {
                return;
            }

            EnsureChromeResources(resources);
            ApplyBrushes(resources, GetPalette(Normalize(preset)));
        }

        public static void EnsureChromeResources(ResourceDictionary resources)
        {
            if (resources == null)
            {
                return;
            }

            foreach (var merged in resources.MergedDictionaries)
            {
                if (merged != null && merged.Contains("NarianCornerRadius"))
                {
                    return;
                }
            }

            if (resources.Contains("NarianCornerRadius"))
            {
                return;
            }

            try
            {
                resources.MergedDictionaries.Insert(0, new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/MetaDataIAPlugin;component/SettingsChrome.xaml", UriKind.Absolute)
                });
            }
            catch
            {
                // Settings view may already merge chrome relatively; ignore load failures.
            }
        }

        public static void ApplyHostChrome(DependencyObject settingsRoot, Palette palette)
        {
            if (settingsRoot == null || palette == null)
            {
                return;
            }

            var window = Window.GetWindow(settingsRoot);
            if (window == null)
            {
                return;
            }

            var bg = BrushOf(palette.Bg);
            var surface = BrushOf(palette.Surface);
            var text = BrushOf(palette.Text);
            var border = BrushOf(palette.Border);
            var accent = BrushOf(palette.Accent);
            var accentOn = BrushOf(palette.AccentOn);

            window.Background = bg;
            window.Foreground = text;
            window.BorderBrush = border;
            ApplyNativeFrame(window, palette);
            TryPaintWindowTitleTexts(window, settingsRoot, text);

            var contentControl = window.Content as Control;
            if (contentControl != null)
            {
                contentControl.Background = bg;
                contentControl.SetValue(TextElement.ForegroundProperty, text);
            }
            else
            {
                var contentPanel = window.Content as Panel;
                if (contentPanel != null)
                {
                    contentPanel.Background = bg;
                    contentPanel.SetValue(TextElement.ForegroundProperty, text);
                }
                else
                {
                    var contentBorder = window.Content as Border;
                    if (contentBorder != null)
                    {
                        contentBorder.Background = bg;
                    }
                }
            }

            for (var parent = VisualTreeHelper.GetParent(settingsRoot);
                 parent != null && !ReferenceEquals(parent, window);
                 parent = VisualTreeHelper.GetParent(parent))
            {
                PaintBackground(parent, bg);
            }

            var hostButtons = new List<Button>();
            CollectHostButtons(window, settingsRoot, window, hostButtons);
            if (hostButtons.Count == 0)
            {
                return;
            }

            // Prefer rightmost / IsDefault as primary (Save), others secondary (Cancel).
            Button primary = null;
            foreach (var button in hostButtons)
            {
                if (button.IsDefault)
                {
                    primary = button;
                    break;
                }
            }

            if (primary == null)
            {
                primary = hostButtons[hostButtons.Count - 1];
            }

            var hover = BrushOf(palette.Hover);
            var accentHover = BrushOf(palette.AccentHover);

            foreach (var button in hostButtons)
            {
                var isPrimary = ReferenceEquals(button, primary);
                StyleHostButton(button, isPrimary, surface, text, border, accent, accentOn, hover, accentHover);
            }

            LiftHostFooter(hostButtons);
        }

        private static void LiftHostFooter(List<Button> hostButtons)
        {
            if (hostButtons == null || hostButtons.Count == 0)
            {
                return;
            }

            FrameworkElement sharedParent = null;
            foreach (var button in hostButtons)
            {
                var parent = VisualTreeHelper.GetParent(button) as FrameworkElement;
                if (parent == null)
                {
                    continue;
                }

                if (sharedParent == null)
                {
                    sharedParent = parent;
                }
                else if (!ReferenceEquals(sharedParent, parent))
                {
                    sharedParent = null;
                    break;
                }
            }

            if (sharedParent == null)
            {
                return;
            }

            var controlParent = sharedParent as Control;
            if (controlParent != null)
            {
                var padding = controlParent.Padding;
                if (padding.Bottom < 14)
                {
                    controlParent.Padding = new Thickness(padding.Left, padding.Top, padding.Right, 14);
                }
            }
            else
            {
                var borderParent = sharedParent as Border;
                if (borderParent != null)
                {
                    var padding = borderParent.Padding;
                    if (padding.Bottom < 14)
                    {
                        borderParent.Padding = new Thickness(padding.Left, padding.Top, padding.Right, 14);
                    }
                }
            }

            var margin = sharedParent.Margin;
            if (margin.Bottom < 8)
            {
                sharedParent.Margin = new Thickness(margin.Left, margin.Top, margin.Right, 8);
            }
        }

        private static void ApplyBrushes(ResourceDictionary resources, Palette palette)
        {
            SetBrush(resources, "TextBrush", palette.Text);
            SetBrush(resources, "TextBrushDark", palette.Text);
            SetBrush(resources, "GlyphBrush", palette.TextMuted);
            SetBrush(resources, "HighlightGlyphBrush", palette.Accent);
            SetBrush(resources, "ControlBackgroundBrush", palette.Surface);
            SetBrush(resources, "ControlHoverBackgroundBrush", palette.Hover);
            SetBrush(resources, "HoverBrush", palette.Hover);
            SetBrush(resources, "PopupBackgroundBrush", palette.Bg);
            SetBrush(resources, "WindowBackgroundBrush", palette.Bg);
            SetBrush(resources, "StandardWindowBackgroundBrush", palette.Bg);
            SetBrush(resources, "PositiveRatingBrush", palette.Success);
            SetBrush(resources, "WarningBrush", palette.Warning);

            SetBrush(resources, "Narian.Bg", palette.Bg);
            SetBrush(resources, "Narian.Surface", palette.Surface);
            SetBrush(resources, "Narian.Hover", palette.Hover);
            SetBrush(resources, "Narian.Selected", palette.Selected);
            SetBrush(resources, "Narian.Accent", palette.Accent);
            SetBrush(resources, "Narian.AccentHover", palette.AccentHover);
            SetBrush(resources, "Narian.AccentOn", palette.AccentOn);
            SetBrush(resources, "Narian.Text", palette.Text);
            SetBrush(resources, "Narian.TextMuted", palette.TextMuted);
            SetBrush(resources, "Narian.Border", palette.Border);
            SetBrush(resources, "Narian.Success", palette.Success);
            SetBrush(resources, "Narian.RowOdd", palette.RowOdd);
            SetBrush(resources, "Narian.RowEven", palette.RowEven);
            SetBrush(resources, "Narian.TableHeader", palette.TableHeader);
            SetBrush(resources, "Narian.BadgeBg", palette.BadgeBg);
            SetBrush(resources, "Narian.BadgeSuccessBg", palette.BadgeSuccessBg);
            SetBrush(resources, "Narian.BadgeWarningBg", palette.BadgeWarningBg);
            SetBrush(resources, "Narian.BadgeMutedBg", palette.BadgeMutedBg);

            resources["ControlCornerRadius"] = new CornerRadius(4);
        }

        private static void PaintBackground(DependencyObject node, Brush bg)
        {
            var border = node as Border;
            if (border != null)
            {
                border.Background = bg;
                return;
            }

            var panel = node as Panel;
            if (panel != null)
            {
                panel.Background = bg;
                return;
            }

            var scrollViewer = node as ScrollViewer;
            if (scrollViewer != null)
            {
                scrollViewer.Background = bg;
                return;
            }

            var control = node as Control;
            if (control != null)
            {
                control.Background = bg;
            }
        }

        private static void CollectHostButtons(DependencyObject root, DependencyObject settingsRoot, Window window, List<Button> results)
        {
            if (root == null)
            {
                return;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (ReferenceEquals(child, settingsRoot) || IsDescendantOf(child, settingsRoot))
                {
                    continue;
                }

                var button = child as Button;
                if (button != null && button.IsVisible)
                {
                    try
                    {
                        var topLeft = button.TransformToAncestor(window).Transform(new Point(0, 0));
                        if (window.ActualHeight <= 0 || topLeft.Y >= window.ActualHeight * 0.72)
                        {
                            results.Add(button);
                        }
                    }
                    catch
                    {
                        results.Add(button);
                    }
                }

                CollectHostButtons(child, settingsRoot, window, results);
            }
        }

        private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
        {
            for (var current = node; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
            }

            return false;
        }

        private static void StyleHostButton(
            Button button,
            bool primary,
            Brush surface,
            Brush text,
            Brush border,
            Brush accent,
            Brush accentOn,
            Brush hover,
            Brush accentHover)
        {
            button.MinHeight = 36;
            button.MinWidth = 96;
            button.Padding = new Thickness(16, 0, 16, 0);
            button.Margin = new Thickness(0, 0, 8, 14);
            button.FontSize = 14;
            button.BorderThickness = new Thickness(1);
            button.SnapsToDevicePixels = true;
            button.Height = 36;
            button.Cursor = System.Windows.Input.Cursors.Hand;

            if (primary)
            {
                button.Background = accent;
                button.Foreground = accentOn;
                button.BorderBrush = accent;
                button.Uid = "narian-host-primary";
            }
            else
            {
                button.Background = surface;
                button.Foreground = text;
                button.BorderBrush = border;
                button.Uid = "narian-host-secondary";
            }

            button.Template = CreateHostButtonTemplate();
            button.MouseEnter -= HostButton_OnMouseEnter;
            button.MouseLeave -= HostButton_OnMouseLeave;
            button.MouseEnter += HostButton_OnMouseEnter;
            button.MouseLeave += HostButton_OnMouseLeave;
            button.Tag = new object[] { primary, surface, accent, hover, accentHover };
        }

        private static void HostButton_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var button = sender as Button;
            var state = button == null ? null : button.Tag as object[];
            if (button == null || state == null || state.Length < 5)
            {
                return;
            }

            var primary = state[0] is bool && (bool)state[0];
            button.Background = primary ? (Brush)state[4] : (Brush)state[3];
        }

        private static void HostButton_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var button = sender as Button;
            var state = button == null ? null : button.Tag as object[];
            if (button == null || state == null || state.Length < 5)
            {
                return;
            }

            var primary = state[0] is bool && (bool)state[0];
            button.Background = primary ? (Brush)state[2] : (Brush)state[1];
        }

        private static void ApplyNativeFrame(Window window, Palette palette)
        {
            if (window == null || palette == null)
            {
                return;
            }

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                var hwnd = helper.Handle != IntPtr.Zero ? helper.Handle : helper.EnsureHandle();
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                var useDark = palette.IsLight ? 0 : 1;
                DwmSetWindowAttribute(hwnd, 19, ref useDark, sizeof(int));
                DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));
                var caption = ToColorRef(palette.Bg);
                var border = ToColorRef(palette.Border);
                var text = ToColorRef(palette.Text);
                DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
                DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
                DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));
            }
            catch
            {
            }
        }

        private static int ToColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }

        private static void TryPaintWindowTitleTexts(Window window, DependencyObject settingsRoot, Brush text)
        {
            try
            {
                PaintTitleTexts(window, settingsRoot, text, 0);
            }
            catch
            {
            }
        }

        private static void PaintTitleTexts(DependencyObject root, DependencyObject settingsRoot, Brush text, int depth)
        {
            if (root == null || depth > 12)
            {
                return;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (ReferenceEquals(child, settingsRoot) || IsDescendantOf(child, settingsRoot))
                {
                    continue;
                }

                var block = child as TextBlock;
                if (block != null && block.FontSize >= 14 && block.FontWeight >= FontWeights.SemiBold)
                {
                    block.Foreground = text;
                }

                PaintTitleTexts(child, settingsRoot, text, depth + 1);
            }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static ControlTemplate CreateHostButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "Bd";
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);
            borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            borderFactory.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding")
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
            borderFactory.AppendChild(presenter);
            template.VisualTree = borderFactory;
            return template;
        }

        private static void SetBrush(ResourceDictionary resources, string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        private static SolidColorBrush BrushOf(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Color Hex(string value)
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
    }
}
