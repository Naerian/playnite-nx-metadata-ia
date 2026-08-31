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
        public const string Midnight = "Midnight";
        public const string Paper = "Paper";
        public const string Oled = "OLED";
        public const string Ocean = "Ocean";
        public const string Ember = "Ember";

        public static readonly string[] AllPresets =
        {
            Midnight, Paper, Oled, Ocean, Ember
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
                return Midnight;
            }

            foreach (var id in AllPresets)
            {
                if (string.Equals(id, preset.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return id;
                }
            }

            return Midnight;
        }

        public static Palette GetPalette(string preset)
        {
            preset = Normalize(preset);
            return Palettes[preset];
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
