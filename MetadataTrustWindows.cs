using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace MetaDataIAPlugin
{
    internal static class MetadataTrustUi
    {
        public enum BadgeKind
        {
            Neutral,
            Muted,
            Success,
            Warning,
            Accent
        }

        public static Window CreateHostWindow(IPlayniteAPI api, WindowCreationOptions options = null)
        {
            if (api != null)
            {
                try
                {
                    return api.Dialogs.CreateWindow(options ?? new WindowCreationOptions
                    {
                        ShowMinimizeButton = true,
                        ShowMaximizeButton = true,
                        ShowCloseButton = true
                    });
                }
                catch
                {
                }
            }

            return new Window();
        }

        public static Window CreatePluginDialog(
            IPlayniteAPI api,
            string title,
            string appearancePreset,
            double width,
            double height,
            double minWidth,
            double minHeight,
            WindowStartupLocation startupLocation = WindowStartupLocation.CenterOwner)
        {
            var window = CreateHostWindow(api);
            window.Title = title ?? string.Empty;
            window.Width = width;
            window.Height = height;
            window.MinWidth = minWidth;
            window.MinHeight = minHeight;
            window.ShowInTaskbar = false;
            ApplyWindowTheme(window, appearancePreset);
            var owner = api == null || api.Dialogs == null ? null : api.Dialogs.GetCurrentAppWindow();
            if (startupLocation == WindowStartupLocation.CenterOwner && owner != null)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            return window;
        }

        public static void SetDialogContent(Window window, UIElement content, string appearancePreset)
        {
            if (window == null)
            {
                return;
            }

            window.Content = content;
            ApplyWindowTheme(window, appearancePreset);
        }

        public static void ApplyWindowTheme(Window window, string appearancePreset = null, bool playniteChrome = true)
        {
            if (window == null)
            {
                return;
            }

            var preset = SettingsAppearance.Normalize(
                string.IsNullOrWhiteSpace(appearancePreset) ? SettingsAppearance.Midnight : appearancePreset);
            var usePlayniteChrome = playniteChrome && window.WindowStyle != WindowStyle.None;
            var playniteHost = IsPlayniteWindowBase(window);
            SettingsAppearance.ApplyWindow(window, preset, true, !playniteHost);
        }

        public static void ApplyPlayniteWindowChrome(Window window)
        {
        }

        private static bool IsPlayniteWindowBase(Window window)
        {
            for (var type = window == null ? null : window.GetType(); type != null; type = type.BaseType)
            {
                if (string.Equals(type.Name, "WindowBase", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static void StyleContentTabs(TabControl tabs)
        {
            if (tabs == null)
            {
                return;
            }

            tabs.Background = Brushes.Transparent;
            tabs.BorderThickness = new Thickness(0);
            try
            {
                tabs.SetResourceReference(FrameworkElement.StyleProperty, "NarianTopTabs");
            }
            catch
            {
            }
        }

        public static Border CreateFramelessDialogShell(UIElement child)
        {
            var shell = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(24, 20, 24, 20),
                SnapsToDevicePixels = true,
                Child = child
            };
            SetResource(shell, Border.BorderBrushProperty, "Narian.Border");
            SetResource(shell, Border.BackgroundProperty, "Narian.Bg");
            return shell;
        }

        public static void PrepareFramelessDialog(Window window, string appearancePreset)
        {
            if (window == null)
            {
                return;
            }

            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.ShowInTaskbar = false;
            window.SnapsToDevicePixels = true;
            ApplyWindowTheme(window, appearancePreset, false);
        }

        public static void SetResource(FrameworkElement element, DependencyProperty property, string key)
        {
            if (element == null)
            {
                return;
            }

            try { element.SetResourceReference(property, key); } catch { }
        }

        public static void ApplySeparatorBrush(Border border)
        {
            if (border == null)
            {
                return;
            }

            SetResource(border, Border.BorderBrushProperty, "Narian.Border");
        }

        public static void ApplyCardChrome(Border card)
        {
            if (card == null)
            {
                return;
            }

            SetResource(card, Border.BackgroundProperty, "Narian.Surface");
            SetResource(card, Border.BorderBrushProperty, "Narian.Border");
            card.CornerRadius = new CornerRadius(4);
            card.SnapsToDevicePixels = true;
            ApplyTextBrush(card);
        }

        public static void ApplyPageBackground(FrameworkElement element)
        {
            if (element == null)
            {
                return;
            }

            if (!TrySetResource(element, Panel.BackgroundProperty, "Narian.Bg") &&
                !TrySetResource(element, Control.BackgroundProperty, "Narian.Bg"))
            {
                SetResource(element, Panel.BackgroundProperty, "WindowBackgroundBrush");
            }
        }

        public static void ApplyHoverBackground(Border border)
        {
            if (border == null)
            {
                return;
            }

            if (!TrySetResource(border, Border.BackgroundProperty, "Narian.Hover"))
            {
                SetResource(border, Border.BackgroundProperty, "ControlBackgroundBrush");
            }
        }

        public static void ApplySelectedChrome(Border border, bool selected)
        {
            if (border == null)
            {
                return;
            }

            if (selected)
            {
                border.BorderThickness = new Thickness(2);
                if (!TrySetResource(border, Border.BorderBrushProperty, "Narian.Accent"))
                {
                    SetResource(border, Border.BorderBrushProperty, "HighlightGlyphBrush");
                }
            }
            else
            {
                border.BorderThickness = new Thickness(1);
                ApplySeparatorBrush(border);
            }
        }

        public static Grid CreatePageShell(out StackPanel headerHost, out Grid bodyHost, out Border footerBar, Thickness? margin = null, string appearancePreset = null)
        {
            var root = new Grid { Margin = margin ?? new Thickness(16) };
            SettingsAppearance.ApplyPresetResources(root.Resources, appearancePreset);
            ApplyPageBackground(root);
            ApplyTextBrush(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            headerHost = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            root.Children.Add(headerHost);

            bodyHost = new Grid();
            Grid.SetRow(bodyHost, 1);
            root.Children.Add(bodyHost);

            footerBar = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 16, 0, 0),
                Margin = new Thickness(0, 16, 0, 0)
            };
            ApplySeparatorBrush(footerBar);
            Grid.SetRow(footerBar, 2);
            root.Children.Add(footerBar);
            return root;
        }

        public static UIElement PageIntro(string title, string subtitle = null)
        {
            var stack = new StackPanel();
            stack.Children.Add(SectionHeader(title, 20, new Thickness(0, 0, 0, 8)));
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                var line = Hint(subtitle, new Thickness(0, 0, 0, 0));
                line.Opacity = 0.78;
                stack.Children.Add(line);
            }

            return stack;
        }

        public static TextBlock FieldLabel(string text)
        {
            var label = Text(text, false);
            label.FontSize = 14;
            label.Margin = new Thickness(0, 0, 0, 4);
            return label;
        }

        public static Border ElevatedCard(UIElement child, Thickness? margin = null, Thickness? padding = null)
        {
            var border = new Border
            {
                Child = child,
                BorderThickness = new Thickness(1),
                Padding = padding ?? new Thickness(16, 12, 16, 12),
                Margin = margin ?? new Thickness(0, 0, 0, 16)
            };
            ApplyCardChrome(border);
            return border;
        }

        public static Border ContentCard(UIElement child, Thickness? margin = null)
        {
            return ElevatedCard(child, margin ?? new Thickness(0), new Thickness(16));
        }

        public static void StylePrimaryButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.IsDefault = true;
            button.MinWidth = 120;
            button.Height = 36;
            button.MinHeight = 36;
            button.Padding = new Thickness(14, 0, 14, 0);
            button.FontSize = 14;
            button.Cursor = System.Windows.Input.Cursors.Hand;
        }

        public static void StyleSecondaryButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.IsDefault = false;
            button.MinWidth = 100;
            button.Height = 36;
            button.MinHeight = 36;
            button.Padding = new Thickness(14, 0, 14, 0);
            button.FontSize = 14;
            button.Cursor = System.Windows.Input.Cursors.Hand;
        }

        public static DockPanel CreateFooterContent(UIElement left = null, params Button[] rightButtons)
        {
            var dock = new DockPanel { LastChildFill = true };
            if (left != null)
            {
                left.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                DockPanel.SetDock(left, Dock.Left);
                dock.Children.Add(left);
            }

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (rightButtons != null)
            {
                for (var i = 0; i < rightButtons.Length; i++)
                {
                    var button = rightButtons[i];
                    if (button == null)
                    {
                        continue;
                    }

                    if (i < rightButtons.Length - 1)
                    {
                        button.Margin = new Thickness(0, 0, 8, 0);
                    }

                    actions.Children.Add(button);
                }
            }

            dock.Children.Add(actions);
            return dock;
        }

        public static void StyleListBox(ListBox list)
        {
            if (list == null)
            {
                return;
            }

            list.BorderThickness = new Thickness(0);
            list.Padding = new Thickness(0);
            list.Background = Brushes.Transparent;
            list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            list.SnapsToDevicePixels = true;
        }

        public static Border ListRow(UIElement content, Thickness? padding = null)
        {
            var row = new Border
            {
                Child = content,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = padding ?? new Thickness(8, 10, 8, 10),
                Background = Brushes.Transparent,
                SnapsToDevicePixels = true
            };
            ApplySeparatorBrush(row);
            return row;
        }

        public static Grid SplitPanes(UIElement left, UIElement right, double leftWidth = 320, double gap = 16)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = leftWidth > 0 ? new GridLength(leftWidth) : new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth > 0 ? 1 : 2, GridUnitType.Star), MinWidth = 240 });
            grid.Children.Add(left);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            return grid;
        }

        public static Border MediaFrame(double width, double height, UIElement child = null, bool showBorder = true)
        {
            var frame = new Border
            {
                Width = width,
                Height = height,
                BorderThickness = showBorder ? new Thickness(1) : new Thickness(0),
                CornerRadius = new CornerRadius(showBorder ? 4 : 0),
                Padding = showBorder ? new Thickness(2) : new Thickness(0),
                ClipToBounds = true,
                SnapsToDevicePixels = true,
                Child = child,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent
            };
            if (showBorder)
            {
                if (!TrySetResource(frame, Border.BackgroundProperty, "Narian.Surface"))
                {
                    SetResource(frame, Border.BackgroundProperty, "ControlBackgroundBrush");
                }

                ApplySeparatorBrush(frame);
            }

            return frame;
        }

        public static Expander ToolsExpander(string header, UIElement content)
        {
            var title = Text(header ?? string.Empty, false);
            title.FontSize = 14;
            title.FontWeight = FontWeights.SemiBold;
            SetResource(title, TextBlock.ForegroundProperty, "Narian.Accent");
            var expander = new Expander
            {
                Header = title,
                IsExpanded = false,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand,
                Content = content
            };
            ApplyTextBrush(expander);
            return expander;
        }

        public static Border VerticalRule()
        {
            var rule = new Border
            {
                Width = 1,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Margin = new Thickness(16, 0, 16, 0),
                SnapsToDevicePixels = true,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ApplySeparatorBrush(rule);
            return rule;
        }

        public static Border BlockSeparator(Thickness? margin = null)
        {
            var line = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = margin ?? new Thickness(0, 16, 0, 16),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                SnapsToDevicePixels = true,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            ApplySeparatorBrush(line);
            return line;
        }

        public static Border EmptyState(string message)
        {
            return SummaryCard(
                Hint(message ?? string.Empty, new Thickness(0)),
                new Thickness(0));
        }

        /// <summary>Overview-style tile (Resumen SummaryCard): surface + border + 16,12 padding.</summary>
        public static Border SummaryCard(UIElement child, Thickness? margin = null, Thickness? padding = null)
        {
            var border = new Border
            {
                Child = child,
                BorderThickness = new Thickness(1),
                Padding = padding ?? new Thickness(16, 12, 16, 12),
                Margin = margin ?? new Thickness(0, 0, 0, 16),
                CornerRadius = new CornerRadius(4),
                SnapsToDevicePixels = true
            };
            ApplyCardChrome(border);
            return border;
        }

        /// <summary>Accent title + bottom border, matching SummaryTitleSeparator.</summary>
        public static Border SummaryCardHeader(string title, UIElement trailing = null)
        {
            var titleBlock = Text(title, false);
            titleBlock.FontSize = 14;
            titleBlock.FontWeight = FontWeights.SemiBold;
            titleBlock.VerticalAlignment = VerticalAlignment.Center;
            if (!TrySetAccentForeground(titleBlock))
            {
                SetResource(titleBlock, TextBlock.ForegroundProperty, "TextBrush");
            }

            UIElement headerContent;
            if (trailing == null)
            {
                headerContent = titleBlock;
            }
            else
            {
                var dock = new DockPanel { LastChildFill = true };
                trailing.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                DockPanel.SetDock(trailing, Dock.Right);
                dock.Children.Add(trailing);
                dock.Children.Add(titleBlock);
                headerContent = dock;
            }

            var header = new Border
            {
                Child = headerContent,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Background = Brushes.Transparent,
                SnapsToDevicePixels = true
            };
            ApplySeparatorBrush(header);
            return header;
        }

        /// <summary>Advanced-tab left rail: filled surface, no outer border, accent section title.</summary>
        public static Grid CreateSidebarLayout(string sidebarTitle, UIElement sidebarBody, UIElement mainContent, double sidebarWidth = 268)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(sidebarWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rail = new Border
            {
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(0),
                SnapsToDevicePixels = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            SetResource(rail, Border.BackgroundProperty, "Narian.Surface");

            var dock = new DockPanel { LastChildFill = true };
            var title = SectionHeader(sidebarTitle ?? string.Empty, 20, new Thickness(8, 4, 8, 12));
            DockPanel.SetDock(title, Dock.Top);
            dock.Children.Add(title);
            dock.Children.Add(sidebarBody ?? new Border());
            rail.Child = dock;
            grid.Children.Add(rail);

            if (mainContent != null)
            {
                Grid.SetColumn(mainContent, 2);
                grid.Children.Add(mainContent);
            }

            return grid;
        }

        public static void ApplyNavItemChrome(Border item, bool selected)
        {
            if (item == null)
            {
                return;
            }

            item.CornerRadius = new CornerRadius(4);
            item.BorderThickness = new Thickness(0);
            item.Padding = new Thickness(16, 8, 16, 8);
            item.MinHeight = 44;
            item.Margin = new Thickness(0, 0, 0, 6);
            item.Cursor = System.Windows.Input.Cursors.Hand;
            item.SnapsToDevicePixels = true;

            if (selected)
            {
                if (!TrySetResource(item, Border.BackgroundProperty, "Narian.Selected"))
                {
                    SetResource(item, Border.BackgroundProperty, "ControlBackgroundBrush");
                }
            }
            else
            {
                item.Background = Brushes.Transparent;
            }
        }

        public static void StyleCardListBox(ListBox list)
        {
            StyleListBox(list);
            if (list == null)
            {
                return;
            }

            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            var template = new ControlTemplate(typeof(ListBoxItem));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            template.VisualTree = presenter;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            list.ItemContainerStyle = style;
        }

        public static Border Badge(string value, BadgeKind kind = BadgeKind.Neutral)
        {
            var label = Text(value, false);
            label.FontSize = 12;
            label.FontWeight = FontWeights.SemiBold;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.TextAlignment = TextAlignment.Center;

            var badge = new Border
            {
                Child = label,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                MinHeight = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 2),
                SnapsToDevicePixels = true
            };

            string backgroundKey = "Narian.BadgeBg";
            string foregroundKey = "TextBrush";
            switch (kind)
            {
                case BadgeKind.Muted:
                    backgroundKey = "Narian.BadgeMutedBg";
                    foregroundKey = "Narian.TextMuted";
                    break;
                case BadgeKind.Success:
                    backgroundKey = "Narian.BadgeSuccessBg";
                    foregroundKey = "PositiveRatingBrush";
                    break;
                case BadgeKind.Warning:
                    backgroundKey = "Narian.BadgeWarningBg";
                    foregroundKey = "WarningBrush";
                    break;
                case BadgeKind.Accent:
                    backgroundKey = "Narian.BadgeBg";
                    foregroundKey = "Narian.Accent";
                    break;
            }

            SetResource(badge, Border.BackgroundProperty, backgroundKey);
            SetResource(label, TextBlock.ForegroundProperty, foregroundKey);

            return badge;
        }

        public static FrameworkElement SelectionMark()
        {
            var mark = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M1,6 L4.5,9.5 L11,2"),
                StrokeThickness = 2.2,
                Stretch = Stretch.Uniform,
                Width = 14,
                Height = 12,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 12, 0),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                SnapsToDevicePixels = true
            };
            if (!TrySetResource(mark, System.Windows.Shapes.Shape.StrokeProperty, "Narian.Accent"))
            {
                SetResource(mark, System.Windows.Shapes.Shape.StrokeProperty, "HighlightGlyphBrush");
            }

            return mark;
        }

        public static Border SectionHeader(string title, double fontSize = 20)
        {
            return SectionHeader(title, fontSize, new Thickness(0, 0, 0, 8));
        }

        public static Border SectionHeader(string title, double fontSize, Thickness margin)
        {
            var heading = Text(title);
            heading.FontSize = fontSize;
            heading.FontWeight = FontWeights.SemiBold;
            if (!TrySetResource(heading, TextBlock.ForegroundProperty, "Narian.Accent"))
            {
                SetResource(heading, TextBlock.ForegroundProperty, "HighlightGlyphBrush");
            }

            var header = new Border
            {
                Child = heading,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 8),
                Margin = margin,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent
            };
            ApplySeparatorBrush(header);
            return header;
        }

        public static Border CardTitleHeader(string title, double fontSize = 14)
        {
            return SectionHeader(title, fontSize, new Thickness(0, 0, 0, 8));
        }

        public static Border CardTitleHeader(string title, double fontSize, Thickness margin)
        {
            return SectionHeader(title, fontSize, margin);
        }

        public static TextBlock Hint(string text, Thickness margin)
        {
            var block = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Opacity = 1,
                Margin = margin
            };
            if (!TrySetResource(block, TextBlock.ForegroundProperty, "Narian.TextMuted"))
            {
                SetResource(block, TextBlock.ForegroundProperty, "GlyphBrush");
            }

            return block;
        }

        public static Border Card(UIElement child, Thickness margin)
        {
            var border = new Border
            {
                Child = child,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16),
                Margin = margin
            };
            ApplyCardChrome(border);
            return border;
        }

        public static bool TrySetAccentForeground(TextBlock block)
        {
            return TrySetResource(block, TextBlock.ForegroundProperty, "Narian.Accent") ||
                   TrySetResource(block, TextBlock.ForegroundProperty, "HighlightGlyphBrush");
        }

        public static bool TrySetResource(FrameworkElement element, DependencyProperty property, string key)
        {
            if (element == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            try
            {
                element.SetResourceReference(property, key);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static FrameworkElement Section(string title, UIElement content, Thickness margin)
        {
            var stack = new StackPanel { Margin = margin };
            stack.Children.Add(SectionHeader(title, 16, new Thickness(0, 0, 0, 10)));

            var body = new Border
            {
                Child = content,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16)
            };
            ApplyCardChrome(body);
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
            SetResource(box, Control.BackgroundProperty, "ControlBackgroundBrush");
            if (!TrySetResource(box, Control.BorderBrushProperty, "Narian.Border"))
            {
                SetResource(box, Control.BorderBrushProperty, "GlyphBrush");
            }

            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(6, 0, 6, 0);
            box.Height = 36;
            box.MinHeight = 36;
            box.MaxHeight = 36;
            box.FontSize = 14;
            box.FontFamily = new FontFamily("Segoe UI");
            box.SnapsToDevicePixels = true;

            // Prefer chrome template when available; otherwise keep a minimal local one.
            var chromeTemplate = box.TryFindResource(typeof(PasswordBox)) as Style;
            if (chromeTemplate != null)
            {
                box.Style = chromeTemplate;
                return;
            }

            var template = new ControlTemplate(typeof(PasswordBox));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(FrameworkElement.HeightProperty, 36.0);
            var host = new FrameworkElementFactory(typeof(ScrollViewer));
            host.Name = "PART_ContentHost";
            host.SetBinding(FrameworkElement.MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
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
                case "min_sys_req": return plugin.Loc("MTDA_FieldMinSysReq", "Minimum system requirements");
                case "recommended_sys_req": return plugin.Loc("MTDA_FieldRecommendedSysReq", "Recommended system requirements");
                default: return field;
            }
        }

        public static string ProvenanceMethod(MetaDataIAPlugin plugin, string method)
        {
            if (string.Equals(method, "trusted-context", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceMethodTrusted", "Trusted context");
            if (string.Equals(method, "ai-normalized", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceMethodNormalized", "AI normalized");
            if (string.Equals(method, "generated-from-identity", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceMethodIdentity", "Generated from game identity");
            if (string.Equals(method, "deterministic", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceMethodDeterministic", "Local deterministic rule");
            if (string.Equals(method, "catalog lookup", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceMethodCatalog", "Catalog lookup");
            if (string.Equals(method, "downloaded-media", StringComparison.OrdinalIgnoreCase)) return plugin.Loc("MTDA_ProvenanceMethodMedia", "Downloaded media");
            return method;
        }

        public static Border ConfidenceBadge(MetaDataIAPlugin plugin, string confidence)
        {
            var kind = BadgeKind.Muted;
            if (string.Equals(confidence, "high", StringComparison.OrdinalIgnoreCase))
            {
                kind = BadgeKind.Success;
            }
            else if (string.Equals(confidence, "medium", StringComparison.OrdinalIgnoreCase))
            {
                kind = BadgeKind.Accent;
            }
            else if (string.Equals(confidence, "low", StringComparison.OrdinalIgnoreCase))
            {
                kind = BadgeKind.Warning;
            }

            var label = string.Equals(confidence, "low", StringComparison.OrdinalIgnoreCase)
                ? plugin.Loc("MTDA_ProvenanceConfidenceLowBadge", "Low")
                : Confidence(plugin, confidence);
            var badge = Badge(label, kind);
            badge.Margin = new Thickness(0, 4, 0, 0);
            badge.HorizontalAlignment = HorizontalAlignment.Left;
            return badge;
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

            var known = KnownProvenanceDetail(plugin, item.Detail);
            if (!string.IsNullOrWhiteSpace(known))
            {
                return known;
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

        private static string KnownProvenanceDetail(MetaDataIAPlugin plugin, string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return string.Empty;
            }

            var igdbMatch = Regex.Match(detail, @"^Matched IGDB game (\d+) and ordered the base games by their first release date\.$");
            if (igdbMatch.Success)
            {
                return string.Format(
                    plugin.Loc("MTDA_ProvenanceDetailCatalogOrdered", "Matched IGDB game {0} and ordered the base games by their first release date."),
                    igdbMatch.Groups[1].Value);
            }

            if (string.Equals(detail, "Matched the game's IGDB collection, but no safe base-game ordinal was available.", StringComparison.Ordinal))
            {
                return plugin.Loc("MTDA_ProvenanceDetailCatalogNoOrder", "Matched the game's IGDB collection, but no safe base-game ordinal was available.");
            }

            if (string.Equals(detail, "Generated locally from an explicit ordinal in the game title.", StringComparison.Ordinal))
            {
                return plugin.Loc("MTDA_ProvenanceDetailOrdinalTitle", "Generated locally from an explicit ordinal in the game title.");
            }

            if (string.Equals(detail, "Derived from the numbered game title without inventing a series name.", StringComparison.Ordinal))
            {
                return plugin.Loc("MTDA_ProvenanceDetailSeriesFromTitle", "Derived from the numbered game title without inventing a series name.");
            }

            if (string.Equals(detail, "Generated locally only when the title contains an explicit ordinal or there is safe local series evidence.", StringComparison.Ordinal))
            {
                return plugin.Loc("MTDA_ProvenanceDetailLocalOrdinalOrSeries", "Generated locally only when the title contains an explicit ordinal or there is safe local series evidence.");
            }

            if (string.Equals(detail, "The value was constrained by trusted source context.", StringComparison.Ordinal))
            {
                return plugin.Loc("MTDA_ProvenanceDetailTrusted", "The value was constrained by trusted source context.");
            }

            if (string.Equals(detail, "The source was supplied as factual context and the AI normalized it.", StringComparison.Ordinal))
            {
                return plugin.Loc("MTDA_ProvenanceDetailNormalizedOfficial", "The source was supplied as factual context and the AI normalized it.");
            }

            if (string.Equals(detail, "Current library metadata was supplied as context and normalized by the AI.", StringComparison.Ordinal))
            {
                return plugin.Loc("MTDA_ProvenanceDetailNormalizedExisting", "Current library metadata was supplied as context and normalized by the AI.");
            }

            if (string.Equals(detail, "No field-specific trusted source was available. Review this value before applying it.", StringComparison.Ordinal))
            {
                return plugin.Loc("MTDA_ProvenanceDetailIdentity", "No field-specific trusted source was available. Review this value before applying it.");
            }

            return string.Empty;
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
        private readonly ObservableCollection<string> providerModelIds = new ObservableCollection<string>();
        private CancellationTokenSource providerModelsRefreshCancellation;
        private bool providerModelsRefreshActive;
        private int page;
        private string selectedProfile = "balanced";
        private bool suppressRecenter;
        private bool userMovedWindow;

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
            Width = 640;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            Style = null;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SnapsToDevicePixels = true;

            // Owner only for modality/z-order; centering uses Application.Current.MainWindow.
            var owner = plugin.Api.Dialogs.GetCurrentAppWindow();
            if (owner != null)
            {
                Owner = owner;
            }

            SettingsAppearance.ApplyWindow(this, working.AppearancePreset);
            EnsureWizardChromeStyles();

            var shell = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                SnapsToDevicePixels = true
            };
            shell.SetResourceReference(Border.BorderBrushProperty, "Narian.Border");
            shell.SetResourceReference(Border.BackgroundProperty, "Narian.Bg");

            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(24, 20, 24, 20) };

            var footer = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 24, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom);

            skipButton.Content = firstRun
                ? plugin.Loc("MTDA_SetupWizardSkip", "Skip for now")
                : plugin.Loc("MTDA_Close", "Close");
            skipButton.MinWidth = 120;
            skipButton.Margin = new Thickness(0, 0, 16, 0);
            skipButton.HorizontalAlignment = HorizontalAlignment.Left;
            skipButton.Click += (s, e) =>
            {
                Skipped = true;
                working.SetupWizardCompleted = true;
                working.SetupWizardMigrationApplied = true;
                DialogResult = false;
            };
            DockPanel.SetDock(skipButton, Dock.Left);
            footer.Children.Add(skipButton);

            var nav = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(nav, Dock.Right);

            backButton.Content = plugin.Loc("MTDA_SetupWizardBack", "Back");
            backButton.MinWidth = 100;
            backButton.Margin = new Thickness(0, 0, 8, 0);
            backButton.Click += (s, e) =>
            {
                if (page > 0)
                {
                    page--;
                    RenderPage();
                }
            };
            nav.Children.Add(backButton);

            nextButton.MinWidth = 140;
            nextButton.IsDefault = true;
            nextButton.Style = TryFindResource("WizardPrimaryButton") as Style;
            nextButton.Click += NextButtonOnClick;
            nav.Children.Add(nextButton);
            footer.Children.Add(nav);
            footer.Children.Add(new Border());
            root.Children.Add(footer);

            // Same layout as CSM: header + step body grow the window (SizeToContent).
            // No inner ScrollViewer — that reads as a nested dialog.
            var main = new StackPanel();
            var dragHeader = new Border
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeAll,
                Padding = new Thickness(0, 0, 0, 8),
                Margin = new Thickness(0, 0, 0, 0)
            };
            dragHeader.MouseLeftButtonDown += OnDragAreaMouseLeftButtonDown;
            var headerStack = new StackPanel();
            stepText.FontSize = 12;
            stepText.Margin = new Thickness(0, 0, 0, 8);
            stepText.SetResourceReference(TextBlock.ForegroundProperty, "Narian.TextMuted");
            titleText.FontSize = 20;
            titleText.FontWeight = FontWeights.SemiBold;
            titleText.TextWrapping = TextWrapping.Wrap;
            titleText.Margin = new Thickness(0, 0, 0, 0);
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "Narian.Accent");
            headerStack.Children.Add(stepText);
            headerStack.Children.Add(titleText);
            dragHeader.Child = headerStack;
            main.Children.Add(dragHeader);
            main.Children.Add(content);
            root.Children.Add(main);

            shell.Child = root;
            Content = shell;
            // Re-apply after Content so window bg matches the chrome border.
            SettingsAppearance.ApplyWindow(this, working.AppearancePreset);

            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += OnWindowLoaded;
            SizeChanged += OnWindowSizeChanged;
            Closed += (s, e) => CancelProviderModelsRefresh();
            RenderPage();
        }

        private void EnsureWizardChromeStyles()
        {
            if (Resources.Contains("WizardSummaryRow"))
            {
                return;
            }

            var summaryRow = new Style(typeof(Border));
            summaryRow.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("ControlBackgroundBrush")));
            summaryRow.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Narian.Border")));
            summaryRow.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
            summaryRow.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(4)));
            summaryRow.Setters.Add(new Setter(Border.PaddingProperty, new Thickness(12, 10, 12, 10)));
            summaryRow.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8)));
            Resources["WizardSummaryRow"] = summaryRow;

            var summaryLabel = new Style(typeof(TextBlock));
            summaryLabel.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("Narian.TextMuted")));
            summaryLabel.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.0));
            summaryLabel.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 4)));
            Resources["WizardSummaryLabel"] = summaryLabel;

            var summaryValue = new Style(typeof(TextBlock));
            summaryValue.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
            summaryValue.Setters.Add(new Setter(TextBlock.FontSizeProperty, 14.0));
            summaryValue.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            summaryValue.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            Resources["WizardSummaryValue"] = summaryValue;

            var baseButton = TryFindResource(typeof(Button)) as Style;
            var primary = baseButton != null ? new Style(typeof(Button), baseButton) : new Style(typeof(Button));
            primary.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Narian.Accent")));
            primary.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Narian.AccentOn")));
            primary.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Narian.Accent")));
            primary.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0)));
            primary.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 120.0));
            var primaryTemplate = new ControlTemplate(typeof(Button));
            var bdFactory = new FrameworkElementFactory(typeof(Border));
            bdFactory.Name = "Bd";
            bdFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bdFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            bdFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            bdFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            bdFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            bdFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            bdFactory.AppendChild(presenter);
            primaryTemplate.VisualTree = bdFactory;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Narian.AccentHover"), "Bd"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Narian.AccentHover"), "Bd"));
            primaryTemplate.Triggers.Add(hover);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55, "Bd"));
            primaryTemplate.Triggers.Add(disabled);
            primary.Setters.Add(new Setter(Control.TemplateProperty, primaryTemplate));
            Resources["WizardPrimaryButton"] = primary;
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
            backButton.Visibility = page == 0 ? Visibility.Collapsed : Visibility.Visible;
            nextButton.Content = page == 3
                ? plugin.Loc("MTDA_SetupWizardFinish", "Save configuration")
                : plugin.Loc("MTDA_SetupWizardNext", "Next");

            if (page == 0) content.Content = BuildPurposePage();
            else if (page == 1) content.Content = BuildProviderPage();
            else if (page == 2) content.Content = BuildFieldsPage();
            else content.Content = BuildSummaryPage();

            Dispatcher.BeginInvoke(new Action(CenterInOwnerOrScreen), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(CenterInOwnerOrScreen), DispatcherPriority.ApplicationIdle);
        }

        private UIElement BuildPurposePage()
        {
            titleText.Text = plugin.Loc("MTDA_SetupWizardPurposeTitle", "Choose language and a safe starting point");
            var panel = new StackPanel();
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SetupWizardPurposeHelp", "The assistant only prepares the configuration. It will not modify any game when you finish."), new Thickness(0, 0, 0, 16)));
            panel.Children.Add(Label(plugin.Loc("MTDA_OutputLanguage", "Output language")));
            var language = new ComboBox
            {
                ItemsSource = working.LanguageOptions,
                DisplayMemberPath = "DisplayName",
                SelectedValuePath = "Code",
                SelectedValue = working.Language,
                MinWidth = 280,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            language.SelectionChanged += (s, e) =>
            {
                if (language.SelectedValue == null) return;
                working.Language = language.SelectedValue.ToString();
                if (firstRun) working.ResetTemplates();
            };
            panel.Children.Add(language);
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_OutputLanguageHelp", "This controls generated metadata and default template headings. The plugin interface follows Playnite's interface language."), new Thickness(0, 8, 0, 16)));

            panel.Children.Add(Label(plugin.Loc("MTDA_SetupWizardProfile", "Configuration profile")));
            var profiles = new List<LocalizedOption>
            {
                new LocalizedOption("balanced", plugin.Loc("MTDA_SetupProfileBalanced", "Balanced and safe (recommended)")),
                new LocalizedOption("missing", plugin.Loc("MTDA_SetupProfileMissing", "Fill missing metadata only")),
                new LocalizedOption("normalize", plugin.Loc("MTDA_SetupProfileNormalize", "Normalize an existing library")),
                new LocalizedOption("media", plugin.Loc("MTDA_SetupProfileMedia", "Media only")),
                new LocalizedOption("current", plugin.Loc("MTDA_SetupProfileCurrent", "Keep current configuration"))
            };
            var profile = new ComboBox
            {
                ItemsSource = profiles,
                DisplayMemberPath = "DisplayName",
                SelectedValuePath = "Value",
                SelectedValue = selectedProfile,
                MinWidth = 360,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            profile.SelectionChanged += (s, e) =>
            {
                if (profile.SelectedValue == null) return;
                selectedProfile = profile.SelectedValue.ToString();
                ApplyProfile(selectedProfile);
            };
            panel.Children.Add(profile);
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SetupWizardProfileHelp", "You can fine-tune every field later. Existing installations are never reset automatically."), new Thickness(0, 8, 0, 0)));
            return panel;
        }

        private UIElement BuildProviderPage()
        {
            titleText.Text = plugin.Loc("MTDA_SetupWizardProviderTitle", "Configure the AI provider");
            var panel = new StackPanel();
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SetupWizardProviderHelp", "Local providers such as LM Studio and Ollama can work without a paid API. The API key can stay empty when the selected provider does not require one."), new Thickness(0, 0, 0, 16)));

            panel.Children.Add(Label(plugin.Loc("MTDA_Provider", "Provider")));
            var provider = new ComboBox
            {
                ItemsSource = working.ProviderPresetOptions,
                DisplayMemberPath = "DisplayName",
                SelectedValuePath = "Value",
                SelectedValue = working.ProviderPreset,
                MinWidth = 360,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(provider);
            if (!string.IsNullOrWhiteSpace(working.ProviderKeyHelp))
            {
                panel.Children.Add(MetadataTrustUi.Hint(working.ProviderKeyHelp, new Thickness(0, 0, 0, 8)));
            }

            if (!string.IsNullOrWhiteSpace(working.ProviderBillingHelp))
            {
                panel.Children.Add(MetadataTrustUi.Hint(working.ProviderBillingHelp, new Thickness(0, 0, 0, 16)));
            }
            else
            {
                panel.Children.Add(new Border { Height = 8 });
            }

            panel.Children.Add(Label(plugin.Loc("MTDA_ApiKey", "API key")));
            var key = new PasswordBox
            {
                Password = working.ApiKey ?? string.Empty,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 36,
                MinHeight = 36,
                MaxHeight = 36,
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 14,
                Margin = new Thickness(0)
            };
            var passwordStyle = TryFindResource(typeof(PasswordBox)) as Style;
            if (passwordStyle != null)
            {
                key.Style = passwordStyle;
            }

            panel.Children.Add(key);
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_ApiKeyHelp", "Stored only on this PC in Playnite plugin settings."), new Thickness(0, 8, 0, 16)));

            panel.Children.Add(Label(plugin.Loc("MTDA_Model", "Model")));
            var modelRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            modelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var model = new ComboBox
            {
                IsEditable = true,
                IsTextSearchEnabled = true,
                ItemsSource = providerModelIds,
                Text = working.Model ?? string.Empty,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(model, 0);
            modelRow.Children.Add(model);
            var refreshModels = new Button
            {
                Content = plugin.Loc("MTDA_RefreshModels", "Refresh models"),
                MinWidth = 140,
                Height = 36
            };
            Grid.SetColumn(refreshModels, 1);
            modelRow.Children.Add(refreshModels);
            panel.Children.Add(modelRow);
            var modelsStatus = MetadataTrustUi.Hint(string.Empty, new Thickness(0, 0, 0, 16));
            panel.Children.Add(modelsStatus);
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SetupWizardProviderAdvancedHelp", "The endpoint is selected automatically. Custom endpoints remain available from Advanced mode in the AI tab."), new Thickness(0, 0, 0, 16)));

            var testButton = new Button
            {
                Content = plugin.Loc("MTDA_TestProvider", "Test provider"),
                MinWidth = 140,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 16)
            };
            testButton.IsDefault = false;
            // Primary look like settings: use accent via IsDefault styling temporarily — keep secondary chrome + accent fill.
            testButton.SetResourceReference(Control.BackgroundProperty, "Narian.Accent");
            testButton.SetResourceReference(Control.ForegroundProperty, "Narian.AccentOn");
            testButton.SetResourceReference(Control.BorderBrushProperty, "Narian.Accent");
            panel.Children.Add(testButton);

            var testStatus = MetadataTrustUi.Text(string.Empty);
            var testStatusBorder = new Border
            {
                Child = testStatus,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 0),
                Visibility = Visibility.Collapsed,
                SnapsToDevicePixels = true
            };
            MetadataTrustUi.SetResource(testStatusBorder, Border.BackgroundProperty, "ControlBackgroundBrush");
            MetadataTrustUi.SetResource(testStatusBorder, Border.BorderBrushProperty, "Narian.Border");
            panel.Children.Add(testStatusBorder);

            Action syncModelText = () =>
            {
                var selected = model.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    working.Model = selected;
                    if (!string.Equals(model.Text, selected, StringComparison.Ordinal))
                    {
                        model.Text = selected;
                    }

                    return;
                }

                working.Model = model.Text;
            };
            model.LostFocus += (s, e) => syncModelText();
            model.DropDownClosed += (s, e) => Dispatcher.BeginInvoke(new Action(syncModelText));
            model.SelectionChanged += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var selected = model.SelectedItem as string;
                    if (string.IsNullOrWhiteSpace(selected) && model.SelectedItem != null)
                    {
                        selected = model.SelectedItem.ToString();
                    }

                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        working.Model = selected;
                        model.Text = selected;
                    }
                    else
                    {
                        syncModelText();
                    }
                }));
            };
            key.PasswordChanged += (s, e) => working.ApiKey = key.Password;

            Func<Task> refreshModelsAsync = async () =>
            {
                working.ApiKey = key.Password;
                working.Model = model.Text;
                await RefreshProviderModelsAsync(model, modelsStatus, refreshModels, true);
            };
            refreshModels.Click += async (s, e) => await refreshModelsAsync();

            provider.SelectionChanged += async (s, e) =>
            {
                if (provider.SelectedValue == null) return;
                working.ProviderPreset = provider.SelectedValue.ToString();
                working.ApplyProviderPreset();
                model.Text = working.Model ?? string.Empty;
                // refresh help hints by rebuilding is heavy; update via status/models only
                await RefreshProviderModelsAsync(model, modelsStatus, refreshModels, false);
            };

            testButton.Click += async (s, e) =>
            {
                working.Model = model.Text;
                working.ApiKey = key.Password;
                testButton.IsEnabled = false;
                backButton.IsEnabled = false;
                nextButton.IsEnabled = false;
                skipButton.IsEnabled = false;
                refreshModels.IsEnabled = false;
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
                        refreshModels.IsEnabled = true;
                    }
                }
            };

            Dispatcher.BeginInvoke(new Action(async () => await RefreshProviderModelsAsync(model, modelsStatus, refreshModels, false)));
            return panel;
        }

        private async Task RefreshProviderModelsAsync(ComboBox modelCombo, TextBlock status, Button refreshButton, bool manual)
        {
            if (providerModelsRefreshActive || modelCombo == null || status == null)
            {
                return;
            }

            AddCurrentProviderModel(working.Model);
            if (RequiresApiKeyForModelListing(working) && string.IsNullOrWhiteSpace(working.ApiKey))
            {
                status.Text = plugin.Loc("MTDA_ProviderModelsApiKeyRequired", "Enter the provider API key to load its available models.");
                return;
            }

            CancelProviderModelsRefresh();
            providerModelsRefreshCancellation = new CancellationTokenSource();
            var cancellation = providerModelsRefreshCancellation;
            providerModelsRefreshActive = true;
            if (refreshButton != null)
            {
                refreshButton.IsEnabled = false;
            }

            status.Text = plugin.Loc("MTDA_ProviderModelsLoading", "Loading available models...");

            try
            {
                var models = await ProviderModelService.GetModelsAsync(working, cancellation.Token);
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                var configuredModel = working.Model;
                providerModelIds.Clear();
                foreach (var option in models)
                {
                    providerModelIds.Add(option.Id);
                }

                AddCurrentProviderModel(configuredModel);
                modelCombo.Text = configuredModel ?? string.Empty;
                status.Text = models.Count == 0
                    ? plugin.Loc("MTDA_ProviderModelsEmpty", "The provider did not return compatible text models. You can still enter one manually.")
                    : string.Format(plugin.Loc("MTDA_ProviderModelsLoaded", "{0} compatible models available. You can also enter one manually."), models.Count);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                status.Text = manual
                    ? string.Format(plugin.Loc("MTDA_ProviderModelsRefreshFailed", "The model list could not be updated: {0}"), ex.Message)
                    : plugin.Loc("MTDA_ProviderModelsUnavailable", "The model list is not available right now. You can enter the model manually.");
            }
            finally
            {
                if (ReferenceEquals(providerModelsRefreshCancellation, cancellation))
                {
                    providerModelsRefreshActive = false;
                    if (refreshButton != null)
                    {
                        refreshButton.IsEnabled = true;
                    }

                    providerModelsRefreshCancellation.Dispose();
                    providerModelsRefreshCancellation = null;
                }
            }
        }

        private void AddCurrentProviderModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model) ||
                providerModelIds.Any(x => string.Equals(x, model, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            providerModelIds.Insert(0, model.Trim());
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

        private UIElement BuildFieldsPage()
        {
            titleText.Text = plugin.Loc("MTDA_SetupWizardFieldsTitle", "Choose what Metadata AI may change");
            var panel = new StackPanel();
            panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SetupWizardFieldsHelp", "These switches control generation. The apply rules selected by the profile still decide whether existing values are preserved, appended or replaced."), new Thickness(0, 0, 0, 16)));
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
            panel.Children.Add(wrap);
            var strict = new CheckBox
            {
                Content = plugin.Loc("MTDA_StrictCompanyAgeRegion", "Do not create developers, publishers, age ratings or regions without trusted evidence"),
                IsChecked = working.StrictCompanyAgeRegion,
                Margin = new Thickness(0, 16, 0, 0),
                FontSize = 14
            };
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
            stack.Children.Add(AddSummaryRow(plugin.Loc("MTDA_OutputLanguage", "Output language"), working.Language));
            stack.Children.Add(AddSummaryRow(plugin.Loc("MTDA_Provider", "Provider"), working.ProviderPreset));
            stack.Children.Add(AddSummaryRow(plugin.Loc("MTDA_Model", "Model"), working.Model));
            stack.Children.Add(AddSummaryRow(plugin.Loc("MTDA_SetupWizardProfile", "Configuration profile"), ResolveProfileDisplayName(selectedProfile)));
            stack.Children.Add(AddSummaryRow(
                plugin.Loc("MTDA_SetupWizardEnabledFields", "Enabled fields"),
                fields.Count == 0 ? plugin.Loc("MTDA_None", "None") : string.Join(", ", fields)));
            stack.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SetupWizardSummaryHelp", "Saving only changes the plugin configuration. Use Simulate changes from the Metadata AI menu to inspect a real game before applying anything."), new Thickness(0, 8, 0, 0)));
            return stack;
        }

        private string ResolveProfileDisplayName(string profile)
        {
            switch ((profile ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "balanced":
                    return plugin.Loc("MTDA_SetupProfileBalanced", "Balanced and safe (recommended)");
                case "missing":
                    return plugin.Loc("MTDA_SetupProfileMissing", "Fill missing metadata only");
                case "normalize":
                    return plugin.Loc("MTDA_SetupProfileNormalize", "Normalize an existing library");
                case "media":
                    return plugin.Loc("MTDA_SetupProfileMedia", "Media only");
                case "current":
                    return plugin.Loc("MTDA_SetupProfileCurrent", "Keep current configuration");
                default:
                    return profile ?? string.Empty;
            }
        }

        private void ApplyProfile(string profile)
        {
            if (profile == "current") return;
            working.AutoImportNewGames = false;
            working.StrictCompanyAgeRegion = true;
            working.UseOfficialStoreContext = true;
            working.UseOriginIntegrationAsAiContext = true;
            working.UseOriginIntegrationForFactualMetadata = true;
            working.GenerateRegions = profile != "media";
            working.GenerateAgeRatings = profile != "media";
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

        private UIElement AddSummaryRow(string label, string value)
        {
            var border = new Border();
            var rowStyle = TryFindResource("WizardSummaryRow") as Style;
            if (rowStyle != null)
            {
                border.Style = rowStyle;
            }
            else
            {
                border.Padding = new Thickness(12, 10, 12, 10);
                border.Margin = new Thickness(0, 0, 0, 8);
                border.CornerRadius = new CornerRadius(4);
                border.BorderThickness = new Thickness(1);
                border.SetResourceReference(Border.BackgroundProperty, "ControlBackgroundBrush");
                border.SetResourceReference(Border.BorderBrushProperty, "Narian.Border");
            }

            var stack = new StackPanel();
            var title = new TextBlock { Text = label ?? string.Empty };
            var titleStyle = TryFindResource("WizardSummaryLabel") as Style;
            if (titleStyle != null)
            {
                title.Style = titleStyle;
            }
            else
            {
                title.FontSize = 12;
                title.Margin = new Thickness(0, 0, 0, 4);
                title.SetResourceReference(TextBlock.ForegroundProperty, "Narian.TextMuted");
            }

            var body = new TextBlock { Text = value ?? string.Empty };
            var valueStyle = TryFindResource("WizardSummaryValue") as Style;
            if (valueStyle != null)
            {
                body.Style = valueStyle;
            }
            else
            {
                body.FontSize = 14;
                body.FontWeight = FontWeights.SemiBold;
                body.TextWrapping = TextWrapping.Wrap;
                body.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            }

            stack.Children.Add(title);
            stack.Children.Add(body);
            border.Child = stack;
            return border;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                Skipped = true;
                working.SetupWizardCompleted = true;
                working.SetupWizardMigrationApplied = true;
                DialogResult = false;
            }
        }

        private void OnDragAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
        {
            if (args.ChangedButton == MouseButton.Left)
            {
                try
                {
                    userMovedWindow = true;
                    DragMove();
                }
                catch
                {
                }
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs args)
        {
            CenterInOwnerOrScreen();
            Dispatcher.BeginInvoke(new Action(CenterInOwnerOrScreen), DispatcherPriority.ApplicationIdle);
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs args)
        {
            if (suppressRecenter || userMovedWindow || !IsLoaded)
            {
                return;
            }

            if (!args.HeightChanged && !args.WidthChanged)
            {
                return;
            }

            CenterInOwnerOrScreen();
        }

        private void CenterInOwnerOrScreen()
        {
            if (userMovedWindow)
            {
                return;
            }

            suppressRecenter = true;
            try
            {
                UpdateLayout();
                var width = ActualWidth;
                var height = ActualHeight;
                if (width < 100 || height < 100 || double.IsNaN(width) || double.IsNaN(height))
                {
                    return;
                }

                var anchor = GetCenteringAnchor();
                Point? centerDip = null;
                if (anchor == null || anchor.WindowState != WindowState.Maximized)
                {
                    centerDip = TryGetWindowCenterDip(anchor);
                }

                double left;
                double top;
                if (centerDip.HasValue)
                {
                    left = centerDip.Value.X - (width / 2.0);
                    top = centerDip.Value.Y - (height / 2.0);
                }
                else
                {
                    var workArea = GetWorkAreaDip(anchor);
                    left = workArea.Left + ((workArea.Width - width) / 2.0);
                    top = workArea.Top + ((workArea.Height - height) / 2.0);
                }

                var clampArea = GetWorkAreaDip(anchor);
                if (width <= clampArea.Width)
                {
                    left = Math.Min(Math.Max(left, clampArea.Left), clampArea.Right - width);
                }
                else
                {
                    left = clampArea.Left;
                }

                if (height <= clampArea.Height)
                {
                    top = Math.Min(Math.Max(top, clampArea.Top), clampArea.Bottom - height);
                }
                else
                {
                    top = clampArea.Top;
                }

                if (!double.IsNaN(left) && !double.IsNaN(top) &&
                    !double.IsInfinity(left) && !double.IsInfinity(top))
                {
                    Left = left;
                    Top = top;
                }
            }
            finally
            {
                suppressRecenter = false;
            }
        }

        private Window GetCenteringAnchor()
        {
            try
            {
                var main = Application.Current != null ? Application.Current.MainWindow : null;
                if (main != null &&
                    main.IsVisible &&
                    main.WindowState != WindowState.Minimized &&
                    main.ActualWidth > 0 &&
                    main.ActualHeight > 0)
                {
                    return main;
                }
            }
            catch
            {
            }

            return Owner;
        }

        private Point? TryGetWindowCenterDip(Window window)
        {
            if (window == null ||
                !window.IsVisible ||
                window.WindowState == WindowState.Minimized ||
                window.ActualWidth <= 0 ||
                window.ActualHeight <= 0)
            {
                return null;
            }

            try
            {
                var centerPx = window.PointToScreen(new Point(
                    window.ActualWidth / 2.0,
                    window.ActualHeight / 2.0));
                var fromDevice = GetTransformFromDevice(this) ?? GetTransformFromDevice(window);
                if (fromDevice == null)
                {
                    return new Point(centerPx.X, centerPx.Y);
                }

                return fromDevice.Value.Transform(new Point(centerPx.X, centerPx.Y));
            }
            catch
            {
                return null;
            }
        }

        private Rect GetWorkAreaDip(Window anchor)
        {
            try
            {
                var screen = GetScreenForWindow(anchor) ?? GetScreenForWindow(this) ?? Forms.Screen.PrimaryScreen;
                if (screen == null)
                {
                    return SystemParameters.WorkArea;
                }

                var pixel = screen.WorkingArea;
                var fromDevice = GetTransformFromDevice(anchor) ?? GetTransformFromDevice(this);
                if (fromDevice == null)
                {
                    return new Rect(pixel.Left, pixel.Top, pixel.Width, pixel.Height);
                }

                var topLeft = fromDevice.Value.Transform(new Point(pixel.Left, pixel.Top));
                var bottomRight = fromDevice.Value.Transform(new Point(pixel.Right, pixel.Bottom));
                return new Rect(topLeft, bottomRight);
            }
            catch
            {
                return SystemParameters.WorkArea;
            }
        }

        private static Forms.Screen GetScreenForWindow(Window window)
        {
            if (window == null)
            {
                return null;
            }

            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle != IntPtr.Zero)
                {
                    return Forms.Screen.FromHandle(handle);
                }

                if (!double.IsNaN(window.Left) && !double.IsNaN(window.Top))
                {
                    var px = GetTransformToDevice(window);
                    if (px != null)
                    {
                        var point = px.Value.Transform(new Point(window.Left + 8, window.Top + 8));
                        return Forms.Screen.FromPoint(new System.Drawing.Point(
                            (int)Math.Round(point.X),
                            (int)Math.Round(point.Y)));
                    }

                    return Forms.Screen.FromPoint(new System.Drawing.Point(
                        (int)Math.Round(window.Left + 8),
                        (int)Math.Round(window.Top + 8)));
                }
            }
            catch
            {
            }

            return null;
        }

        private static Matrix? GetTransformFromDevice(Window window)
        {
            var source = GetPresentationSource(window);
            if (source == null || source.CompositionTarget == null)
            {
                return null;
            }

            return source.CompositionTarget.TransformFromDevice;
        }

        private static Matrix? GetTransformToDevice(Window window)
        {
            var source = GetPresentationSource(window);
            if (source == null || source.CompositionTarget == null)
            {
                return null;
            }

            return source.CompositionTarget.TransformToDevice;
        }

        private static PresentationSource GetPresentationSource(Window window)
        {
            if (window == null)
            {
                return null;
            }

            var source = PresentationSource.FromVisual(window);
            if (source != null)
            {
                return source;
            }

            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle != IntPtr.Zero)
                {
                    return HwndSource.FromHwnd(handle);
                }
            }
            catch
            {
            }

            return null;
        }

        private static TextBlock Label(string value)
        {
            var block = new TextBlock
            {
                Text = value,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            MetadataTrustUi.SetResource(block, TextBlock.ForegroundProperty, "TextBrush");
            return block;
        }

        private static void AddFieldCheck(Panel panel, string text, Func<bool> get, Action<bool> set)
        {
            var check = new CheckBox
            {
                Content = text,
                IsChecked = get(),
                Width = 215,
                Margin = new Thickness(0, 4, 8, 4),
                FontSize = 14
            };
            check.Checked += (s, e) => set(true);
            check.Unchecked += (s, e) => set(false);
            panel.Children.Add(check);
        }
    }

    public sealed class SimulationWindow
    {
        private readonly Window window;
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
            window = MetadataTrustUi.CreatePluginDialog(
                plugin.Api,
                plugin.Loc("MTDA_MenuSimulateChanges", "Preview and choose Metadata AI changes"),
                plugin.GetAppearancePreset(),
                1080,
                760,
                820,
                560);

            StackPanel headerHost;
            Grid bodyHost;
            Border footerBar;
            var root = MetadataTrustUi.CreatePageShell(out headerHost, out bodyHost, out footerBar, null, plugin.GetAppearancePreset());

            headerHost.Children.Add(BuildWindowHeader());

            UIElement bodyContent;
            if (singleGame)
            {
                bodyContent = new ScrollViewer
                {
                    Content = BuildGameContent(this.results[0], false),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(0, 0, 4, 0)
                };
            }
            else
            {
                bodyContent = BuildMultiGameLayout();
            }
            bodyHost.Children.Add(bodyContent);

            selectionSummary.TextWrapping = TextWrapping.Wrap;
            selectionSummary.VerticalAlignment = VerticalAlignment.Center;
            selectionSummary.Margin = new Thickness(0, 0, 16, 0);
            if (!MetadataTrustUi.TrySetResource(selectionSummary, TextBlock.ForegroundProperty, "Narian.TextMuted"))
            {
                MetadataTrustUi.SetResource(selectionSummary, TextBlock.ForegroundProperty, "GlyphBrush");
            }
            selectionSummary.FontSize = 12;
            selectionSummary.FontStyle = FontStyles.Italic;

            applyButton.Content = plugin.Loc("MTDA_SimulationApplySelected", "Apply selected changes");
            MetadataTrustUi.StylePrimaryButton(applyButton);
            applyButton.MinWidth = 190;
            applyButton.Click += (s, e) => { ApplyRequested = true; window.DialogResult = true; };
            var close = new Button { Content = plugin.Loc("MTDA_Close", "Close") };
            MetadataTrustUi.StyleSecondaryButton(close);
            close.Click += (s, e) => window.DialogResult = false;
            footerBar.Child = MetadataTrustUi.CreateFooterContent(selectionSummary, applyButton, close);
            MetadataTrustUi.SetDialogContent(window, root, plugin.GetAppearancePreset());
            UpdateSelectionState();
        }

        public Window Owner { get { return window.Owner; } }

        public bool? ShowDialog()
        {
            return window.ShowDialog();
        }

        private UIElement BuildWindowHeader()
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = singleGame ? new GridLength(96) : new GridLength(0) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = singleGame ? new GridLength(16) : new GridLength(0) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (singleGame)
            {
                header.Children.Add(BuildGameArtwork(results[0].Game));
            }

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleValue = singleGame ? results[0].Game.Name : plugin.Loc("MTDA_MenuSimulateChanges", "Preview and choose Metadata AI changes");
            text.Children.Add(MetadataTrustUi.SectionHeader(titleValue, 20, new Thickness(0, 0, 0, 8)));
            text.Children.Add(MetadataTrustUi.Hint(
                plugin.Loc("MTDA_SimulationHelp", "No changes have been written. Applying will reuse these generated results without calling the AI provider again."),
                new Thickness(0, 0, 0, 0)));
            if (singleGame && results[0].Changes != null && results[0].Changes.Count > 0)
            {
                var verdict = MetadataTrustUi.Text(GameRecommendation(results[0]));
                verdict.FontSize = 14;
                verdict.FontWeight = FontWeights.SemiBold;
                verdict.Margin = new Thickness(0, 10, 0, 0);
                MetadataTrustUi.SetResource(verdict, TextBlock.ForegroundProperty, "Narian.Accent");
                text.Children.Add(verdict);
                text.Children.Add(MetadataTrustUi.Hint(GameRecommendationDetails(results[0]), new Thickness(0, 4, 0, 0)));
            }
            Grid.SetColumn(text, 2);
            header.Children.Add(text);
            return header;
        }

        private UIElement BuildMultiGameLayout()
        {
            MetadataTrustUi.StyleCardListBox(gameList);
            gameList.SelectionChanged += (s, e) =>
            {
                foreach (ListBoxItem row in gameList.Items)
                {
                    var chrome = row.Content as Border;
                    if (chrome != null) MetadataTrustUi.ApplyNavItemChrome(chrome, ReferenceEquals(row, gameList.SelectedItem));
                }
                ShowSelectedSimulationGame();
            };
            foreach (var item in results)
            {
                gameList.Items.Add(BuildSimulationGameItem(item));
            }

            multiGameContent.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var main = new ScrollViewer
            {
                Content = multiGameContent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 4, 0)
            };
            var layout = MetadataTrustUi.CreateSidebarLayout(
                plugin.Loc("MTDA_SimulationGames", "Games"),
                gameList,
                main,
                268);
            if (gameList.Items.Count > 0) gameList.SelectedIndex = 0;
            return layout;
        }

        private ListBoxItem BuildSimulationGameItem(MetadataSimulationResult item)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(BuildGameNavigationArtwork(item == null ? null : item.Game));
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = MetadataTrustUi.Text(item == null || item.Game == null ? plugin.Loc("MTDA_Unknown", "Unknown") : item.Game.Name);
            name.FontWeight = FontWeights.SemiBold;
            name.FontSize = 14;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(name);
            var changeCount = item == null || item.Changes == null ? 0 : item.Changes.Count;
            text.Children.Add(MetadataTrustUi.Hint(
                string.Format(plugin.Loc("MTDA_SimulationChangesCount", "{0} change(s)"), changeCount) + "  ·  " + GameRecommendation(item),
                new Thickness(0, 2, 0, 0)));
            Grid.SetColumn(text, 2);
            grid.Children.Add(text);
            var chrome = new Border { Child = grid };
            MetadataTrustUi.ApplyNavItemChrome(chrome, false);
            return new ListBoxItem
            {
                Content = chrome,
                Tag = item,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
        }

        private UIElement BuildGameNavigationArtwork(Game game)
        {
            var path = ResolveGameMediaPath(game, MediaKind.Cover);
            if (string.IsNullOrWhiteSpace(path)) path = ResolveGameMediaPath(game, MediaKind.Icon);
            var image = CreatePreviewImage(path, false, 64);
            return MetadataTrustUi.MediaFrame(40, 48, image);
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
            multiGameContent.Content = BuildGameContent(item, true);
            UpdateSelectionState();
        }

        private UIElement BuildGameArtwork(Game game)
        {
            var path = ResolveGameMediaPath(game, MediaKind.Cover);
            if (string.IsNullOrWhiteSpace(path)) path = ResolveGameMediaPath(game, MediaKind.Icon);
            var image = CreatePreviewImage(path, false, 92);
            return MetadataTrustUi.MediaFrame(84, 100, image, showBorder: false);
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
            var hasMedia = item.MediaChanges != null && item.MediaChanges.Count > 0;
            if (!string.IsNullOrWhiteSpace(item.Error))
            {
                panel.Children.Add(MetadataTrustUi.Text(item.Error));
            }
            else if (showGameContext && (hasMetadataChanges || hasMedia))
            {
                var verdict = MetadataTrustUi.Text(GameRecommendation(item));
                verdict.FontSize = 14;
                verdict.FontWeight = FontWeights.SemiBold;
                MetadataTrustUi.SetResource(verdict, TextBlock.ForegroundProperty, "Narian.Accent");
                panel.Children.Add(verdict);
                panel.Children.Add(MetadataTrustUi.Hint(GameRecommendationDetails(item), new Thickness(0, 4, 0, 12)));
            }

            if (string.IsNullOrWhiteSpace(item.Error) && item.Result != null && item.Result.SeriesContextDiagnostics != null)
            {
                var seriesContext = item.Result.SeriesContextDiagnostics.ToDisplayText();
                if (!string.IsNullOrWhiteSpace(seriesContext))
                {
                    var diagnostic = MetadataTrustUi.Text(seriesContext);
                    diagnostic.TextWrapping = TextWrapping.Wrap;
                    diagnostic.Margin = new Thickness(0, 0, 0, 12);
                    panel.Children.Add(diagnostic);
                }
            }

            if (!hasMetadataChanges && !hasMedia && string.IsNullOrWhiteSpace(item.Error))
            {
                panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_SimulationNoChanges", "No changes would be made with the current apply rules."), new Thickness(0)));
            }

            if (hasMetadataChanges || hasMedia)
            {
                var gameActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
                var gameRecommended = new Button { Content = plugin.Loc("MTDA_SimulationRecommendedOnly", "Recommended only"), MinWidth = 135, Margin = new Thickness(0, 0, 8, 0) };
                MetadataTrustUi.StyleSecondaryButton(gameRecommended);
                gameRecommended.Click += (s, e) => SetGameSelection(item, change => string.Equals(change.Recommendation, MetadataChangeRecommendationService.Recommended, StringComparison.OrdinalIgnoreCase), true);
                var gameAll = new Button { Content = plugin.Loc("MTDA_SimulationSelectAll", "Select all"), MinWidth = 95, Margin = new Thickness(0, 0, 8, 0) };
                MetadataTrustUi.StyleSecondaryButton(gameAll);
                gameAll.Click += (s, e) => SetGameSelection(item, true);
                var gameNone = new Button { Content = plugin.Loc("MTDA_SimulationSelectNone", "Select none"), MinWidth = 95 };
                MetadataTrustUi.StyleSecondaryButton(gameNone);
                gameNone.Click += (s, e) => SetGameSelection(item, false);
                gameActions.Children.Add(gameRecommended);
                gameActions.Children.Add(gameAll);
                gameActions.Children.Add(gameNone);
                panel.Children.Add(gameActions);
            }

            if (singleGame && !showGameContext)
            {
                panel.Children.Add(BuildMediaSection(item));
            }

            if (hasMetadataChanges)
            {
                var metadataContent = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                for (var index = 0; index < item.Changes.Count; index++)
                {
                    var changeBlock = BuildChange(item, item.Changes[index]);
                    if (index > 0)
                    {
                        var spaced = changeBlock as FrameworkElement;
                        if (spaced != null) spaced.Margin = new Thickness(0, 20, 0, 0);
                    }
                    metadataContent.Children.Add(changeBlock);
                }
                panel.Children.Add(metadataContent);
            }
            return panel;
        }

        private UIElement BuildMediaSection(MetadataSimulationResult item)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var column = 0;
            foreach (var kind in new[] { MediaKind.Cover, MediaKind.Icon, MediaKind.Background })
            {
                if (column > 0)
                {
                    var rule = MetadataTrustUi.VerticalRule();
                    Grid.SetColumn(rule, column - 1);
                    grid.Children.Add(rule);
                }

                var host = new Border
                {
                    MinWidth = 0,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0)
                };
                mediaCards[kind] = host;
                RefreshMediaCard(item, kind, host);
                Grid.SetColumn(host, column);
                grid.Children.Add(host);
                column += 2;
            }

            return grid;
        }

        private void RefreshMediaCard(MetadataSimulationResult item, MediaKind kind, Border host)
        {
            var selected = (item.MediaChanges ?? new List<MediaSimulationChange>()).FirstOrDefault(x => x.Kind == kind);
            var content = new StackPanel();
            UIElement userBadge = null;
            if (selected != null && selected.IsUserChosen)
            {
                userBadge = MetadataTrustUi.Badge(plugin.Loc("MTDA_SimulationUserChosen", "User"), MetadataTrustUi.BadgeKind.Accent);
                userBadge.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            }
            content.Children.Add(MetadataTrustUi.SummaryCardHeader(MediaKindLabel(kind), userBadge));

            var comparison = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
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
                HorizontalAlignment = HorizontalAlignment.Left
            };
            MetadataTrustUi.StyleSecondaryButton(choose);
            choose.Click += (s, e) =>
            {
                var selection = plugin.SelectMediaForSimulation(item.Game, kind, activeSettings, window);
                if (selection == null) return;
                if (item.MediaChanges == null) item.MediaChanges = new List<MediaSimulationChange>();
                var previous = item.MediaChanges.FirstOrDefault(x => x.Kind == kind);
                if (previous != null) item.MediaChanges.Remove(previous);
                selection.IsUserChosen = true;
                selection.IsSelected = true;
                item.MediaChanges.Add(selection);
                RefreshMediaCard(item, kind, host);
                UpdateSelectionState();
            };
            content.Children.Add(choose);
            host.Child = content;
            host.Padding = new Thickness(0);
            host.BorderThickness = new Thickness(0);
            host.Background = Brushes.Transparent;
            host.Margin = new Thickness(0);
        }

        private UIElement BuildMediaPreview(string label, string source, bool remote, MediaKind kind)
        {
            var panel = new StackPanel();
            panel.Children.Add(MetadataTrustUi.CardTitleHeader(label, 14, new Thickness(0, 0, 0, 8)));
            var image = CreatePreviewImage(source, remote, kind == MediaKind.Background ? 260 : 150);
            if (image != null)
            {
                image.Height = 116;
                image.Margin = new Thickness(0, 0, 0, 0);
                panel.Children.Add(image);
            }
            else
            {
                panel.Children.Add(MetadataTrustUi.Hint(plugin.Loc("MTDA_None", "None"), new Thickness(0)));
            }

            return MetadataTrustUi.SummaryCard(panel, new Thickness(0), new Thickness(12, 10, 12, 10));
        }

        private Image CreatePreviewImage(string source, bool remote, int decodeWidth)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            try
            {
                Uri uri;
                if (!Uri.TryCreate(source, UriKind.Absolute, out uri))
                {
                    if (!Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out uri))
                    {
                        return null;
                    }
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.DecodePixelWidth = decodeWidth;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.IgnoreColorProfile;
                bitmap.CacheOption = remote ? BitmapCacheOption.OnDemand : BitmapCacheOption.OnLoad;
                bitmap.UriSource = uri;
                bitmap.EndInit();
                if (!remote)
                {
                    bitmap.Freeze();
                }

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
            var panel = new StackPanel();

            var header = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var identity = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
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
            fieldTitle.FontSize = 14;
            fieldTitle.FontWeight = FontWeights.SemiBold;
            fieldTitle.VerticalAlignment = VerticalAlignment.Center;
            if (!MetadataTrustUi.TrySetAccentForeground(fieldTitle))
            {
                MetadataTrustUi.SetResource(fieldTitle, TextBlock.ForegroundProperty, "TextBrush");
            }

            identity.Children.Add(fieldTitle);
            header.Children.Add(identity);

            var recommendation = new StackPanel { MaxWidth = 420, Margin = new Thickness(20, 0, 0, 0) };
            recommendation.HorizontalAlignment = HorizontalAlignment.Right;
            recommendation.VerticalAlignment = VerticalAlignment.Center;
            recommendation.Children.Add(BuildRecommendationBadge(change));
            Grid.SetColumn(recommendation, 1);
            header.Children.Add(recommendation);

            var headerBorder = new Border
            {
                Child = header,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            MetadataTrustUi.ApplySeparatorBrush(headerBorder);
            panel.Children.Add(headerBorder);

            var recommendationReason = MetadataTrustUi.Hint(FormatSentence(RecommendationReason(change)), new Thickness(0, 0, 0, 0));
            recommendationReason.HorizontalAlignment = HorizontalAlignment.Right;
            recommendationReason.TextAlignment = TextAlignment.Right;
            panel.Children.Add(recommendationReason);

            if (change.Conflict != null && change.Conflict.Values != null && change.Conflict.Values.Count > 1)
            {
                var conflictText = string.Join(Environment.NewLine, change.Conflict.Values.Select(x => "- " + MetadataTrustUi.ProvenanceSource(plugin, x.Source) + ": " + x.Value));
                var conflict = MetadataTrustUi.Text(plugin.Loc("MTDA_SourceConflictTitle", "Trusted sources disagree") + Environment.NewLine + conflictText);
                conflict.TextWrapping = TextWrapping.Wrap;
                conflict.Margin = new Thickness(0, 10, 0, 0);
                panel.Children.Add(conflict);
            }

            var values = new Grid { Margin = new Thickness(0, 12, 0, 0) };
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
                panel.Children.Add(MetadataTrustUi.Hint(
                    plugin.Loc("MTDA_ProvenanceSource", "Source") + ": " + MetadataTrustUi.ProvenanceSource(plugin, change.Provenance.Source) +
                    "  |  " + plugin.Loc("MTDA_ProvenanceConfidence", "Confidence") + ": " + MetadataTrustUi.Confidence(plugin, change.Provenance.Confidence),
                    new Thickness(0, 10, 0, 0)));
            }

            return panel;
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
            SetGameSelection(item, change => selected, selected);
        }

        private void SetGameSelection(MetadataSimulationResult item, Func<MetadataChangeItem, bool> selector)
        {
            SetGameSelection(item, selector, true);
        }

        private void SetGameSelection(MetadataSimulationResult item, Func<MetadataChangeItem, bool> selector, bool mediaSelected)
        {
            if (updatingSelection || item == null) return;
            updatingSelection = true;
            if (item.Changes != null)
            {
                foreach (var change in item.Changes)
                {
                    change.IsSelected = selector(change);
                    CheckBox box;
                    if (fieldSelectors.TryGetValue(change, out box)) box.IsChecked = change.IsSelected;
                }
            }

            if (item.MediaChanges != null)
            {
                foreach (var media in item.MediaChanges)
                {
                    if (media == null || media.Option == null) continue;
                    media.IsSelected = mediaSelected;
                }
            }

            foreach (var pair in mediaCards)
            {
                RefreshMediaCard(item, pair.Key, pair.Value);
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
            var kind = string.Equals(change.Recommendation, MetadataChangeRecommendationService.Recommended, StringComparison.OrdinalIgnoreCase)
                ? MetadataTrustUi.BadgeKind.Success
                : string.Equals(change.Recommendation, MetadataChangeRecommendationService.KeepCurrent, StringComparison.OrdinalIgnoreCase)
                    ? MetadataTrustUi.BadgeKind.Muted
                    : MetadataTrustUi.BadgeKind.Warning;
            var badge = MetadataTrustUi.Badge(RecommendationBadgeLabel(change), kind);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.Margin = new Thickness(0);
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
            panel.Children.Add(MetadataTrustUi.CardTitleHeader(label, 14, new Thickness(0, 0, 0, 8)));
            var text = MetadataTrustUi.Text(string.IsNullOrWhiteSpace(value) ? plugin.Loc("MTDA_None", "None") : value);
            text.Margin = new Thickness(0, 0, 0, 0);
            if (muted)
            {
                text.Opacity = 0.82;
            }

            panel.Children.Add(text);
            return MetadataTrustUi.SummaryCard(panel, new Thickness(0), new Thickness(16, 12, 16, 12));
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

    public sealed class HistoryWindow
    {
        private readonly Window window;
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
            window = MetadataTrustUi.CreatePluginDialog(
                plugin.Api,
                windowTitle,
                plugin.GetAppearancePreset(),
                1100,
                740,
                860,
                560);

            StackPanel headerHost;
            Grid bodyHost;
            Border footerBar;
            var root = MetadataTrustUi.CreatePageShell(out headerHost, out bodyHost, out footerBar, new Thickness(16), plugin.GetAppearancePreset());
            // Title lives in the Advanced-style sidebar; keep page header minimal.
            headerHost.Margin = new Thickness(0);
            headerHost.Visibility = Visibility.Collapsed;

            MetadataTrustUi.StyleCardListBox(list);
            list.SelectionChanged += (s, e) =>
            {
                foreach (ListBoxItem row in list.Items)
                {
                    var chrome = row.Content as Border;
                    if (chrome != null) MetadataTrustUi.ApplyNavItemChrome(chrome, ReferenceEquals(row, list.SelectedItem));
                }
                ShowOperation(SelectedOperation());
            };

            var detailsScroll = new ScrollViewer
            {
                Content = details,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 4, 0)
            };
            bodyHost.Children.Add(MetadataTrustUi.CreateSidebarLayout(
                plugin.Loc("MTDA_HistorySidebarTitle", "Change history"),
                list,
                detailsScroll,
                268));

            statusText.TextWrapping = TextWrapping.Wrap;
            statusText.Visibility = Visibility.Collapsed;
            statusText.Margin = new Thickness(0, 0, 16, 0);
            MetadataTrustUi.SetResource(statusText, TextBlock.ForegroundProperty, "TextBrush");
            statusText.VerticalAlignment = VerticalAlignment.Center;

            var clear = new Button { Content = plugin.Loc("MTDA_HistoryClear", "Clear history") };
            MetadataTrustUi.StyleSecondaryButton(clear);
            clear.MinWidth = 130;
            clear.Margin = new Thickness(0, 0, 8, 0);
            clear.Click += (s, e) => ClearHistory();
            clear.Visibility = HasGameFilter ? Visibility.Collapsed : Visibility.Visible;
            undoAllButton.Content = plugin.Loc("MTDA_HistoryUndoAllSelected", "Undo all selected history");
            MetadataTrustUi.StylePrimaryButton(undoAllButton);
            undoAllButton.MinWidth = 225;
            undoAllButton.Click += (s, e) => UndoSelectedOperation();
            undoAllButton.Visibility = HasGameFilter ? Visibility.Collapsed : Visibility.Visible;
            var close = new Button { Content = plugin.Loc("MTDA_Close", "Close") };
            MetadataTrustUi.StyleSecondaryButton(close);
            close.Click += (s, e) => window.DialogResult = false;

            var leftFooter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftFooter.Children.Add(clear);
            leftFooter.Children.Add(statusText);
            footerBar.Child = MetadataTrustUi.CreateFooterContent(leftFooter, undoAllButton, close);
            MetadataTrustUi.SetDialogContent(window, root, plugin.GetAppearancePreset());
            ReloadOperations(null);
        }

        public bool? ShowDialog()
        {
            return window.ShowDialog();
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
                var panel = new StackPanel();
                var kind = MetadataTrustUi.Text(operation.Kind);
                kind.FontWeight = FontWeights.SemiBold;
                kind.FontSize = 14;
                panel.Children.Add(kind);
                var operationGames = operation.Games ?? new List<MetadataHistoryGameEntry>();
                var relevantCount = HasGameFilter ? operationGames.Count(x => gameIdFilter.Contains(x.GameId)) : operationGames.Count;
                panel.Children.Add(MetadataTrustUi.Hint(operation.CreatedAt.ToString("g") + "  ·  " + string.Format(plugin.Loc("MTDA_HistoryGamesCount", "{0} game(s)"), relevantCount), new Thickness(0, 2, 0, 0)));
                var chrome = new Border { Child = panel };
                MetadataTrustUi.ApplyNavItemChrome(chrome, false);
                list.Items.Add(new ListBoxItem
                {
                    Content = chrome,
                    Tag = operation,
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                });
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
                details.Children.Add(MetadataTrustUi.SummaryCard(MetadataTrustUi.Hint(emptyText, new Thickness(0)), new Thickness(0)));
                return;
            }

            var operationHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            operationHeader.Children.Add(MetadataTrustUi.SectionHeader(operation.Kind, 20, new Thickness(0, 0, 0, 8)));
            operationHeader.Children.Add(MetadataTrustUi.Hint(operation.CreatedAt.ToString("F"), new Thickness(0, 0, 0, 0)));
            details.Children.Add(operationHeader);
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
            info.Children.Add(MetadataTrustUi.SectionHeader(entry.GameName ?? string.Empty, 14, new Thickness(0, 0, 0, 8)));
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

            var undoGame = new Button { Content = plugin.Loc("MTDA_HistoryUndoGame", "Undo this game"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            MetadataTrustUi.StyleSecondaryButton(undoGame);
            undoGame.MinWidth = 150;
            undoGame.Click += (s, e) => UndoGame(operation, entry);
            info.Children.Add(undoGame);
            Grid.SetColumn(info, 2);
            grid.Children.Add(info);
            return MetadataTrustUi.SummaryCard(grid, new Thickness(0, 0, 0, 16));
        }

        private UIElement BuildGameImage(Guid gameId)
        {
            var frame = new Border { Width = 86, Height = 100, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
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
            return new MetadataConfirmationWindow(plugin, window, message).ShowDialog() == true;
        }

        private void SetStatus(string message, bool isError)
        {
            var body = message ?? string.Empty;
            statusText.Text = string.IsNullOrWhiteSpace(body)
                ? string.Empty
                : string.Format(plugin.Loc("MTDA_HistoryLastAction", "Last action: {0}"), body);
            statusText.Visibility = string.IsNullOrWhiteSpace(body) ? Visibility.Collapsed : Visibility.Visible;
            statusText.FontWeight = isError ? FontWeights.SemiBold : FontWeights.Normal;
            statusText.Opacity = isError ? 1 : 0.82;
        }
    }

    internal sealed class MetadataConfirmationWindow : Window
    {
        public MetadataConfirmationWindow(MetaDataIAPlugin plugin, Window owner, string message)
        {
            Title = plugin.Loc("MTDA_PluginName", "Metadata AI");
            Width = 480;
            SizeToContent = SizeToContent.Height;
            MinHeight = 180;
            Owner = owner;
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            MetadataTrustUi.PrepareFramelessDialog(this, plugin.GetAppearancePreset());

            var root = new Grid();
            MetadataTrustUi.ApplyTextBrush(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = MetadataTrustUi.Text(plugin.Loc("MTDA_PluginName", "Metadata AI"));
            heading.FontSize = 20;
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 0, 0, 10);
            MetadataTrustUi.SetResource(heading, TextBlock.ForegroundProperty, "Narian.Accent");
            root.Children.Add(heading);
            var text = MetadataTrustUi.Text(message);
            text.FontSize = 14;
            text.Margin = new Thickness(0, 0, 0, 20);
            Grid.SetRow(text, 1);
            root.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var yes = new Button { Content = plugin.Loc("MTDA_Yes", "Yes"), MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
            MetadataTrustUi.StylePrimaryButton(yes);
            yes.Click += (s, e) => DialogResult = true;
            var cancel = new Button { Content = plugin.Loc("MTDA_Cancel", "Cancel"), MinWidth = 110 };
            MetadataTrustUi.StyleSecondaryButton(cancel);
            cancel.Click += (s, e) => DialogResult = false;
            buttons.Children.Add(yes);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);
            Content = MetadataTrustUi.CreateFramelessDialogShell(root);
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                DialogResult = false;
            };
        }
    }

    internal sealed class MetadataNoticeWindow : Window
    {
        public MetadataNoticeWindow(MetaDataIAPlugin plugin, Window owner, string message)
        {
            Title = plugin.Loc("MTDA_PluginName", "Metadata AI");
            Width = 480;
            SizeToContent = SizeToContent.Height;
            MinHeight = 180;
            Owner = owner;
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            MetadataTrustUi.PrepareFramelessDialog(this, plugin.GetAppearancePreset());

            var root = new Grid();
            MetadataTrustUi.ApplyTextBrush(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = MetadataTrustUi.Text(plugin.Loc("MTDA_PluginName", "Metadata AI"));
            heading.FontSize = 20;
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 0, 0, 10);
            MetadataTrustUi.SetResource(heading, TextBlock.ForegroundProperty, "Narian.Accent");
            root.Children.Add(heading);
            var text = MetadataTrustUi.Text(message);
            text.FontSize = 14;
            text.Margin = new Thickness(0, 0, 0, 20);
            Grid.SetRow(text, 1);
            root.Children.Add(text);
            var close = new Button { Content = plugin.Loc("MTDA_Close", "Close"), MinWidth = 110, HorizontalAlignment = HorizontalAlignment.Right };
            MetadataTrustUi.StyleSecondaryButton(close);
            close.Click += (s, e) => Close();
            Grid.SetRow(close, 2);
            root.Children.Add(close);
            Content = MetadataTrustUi.CreateFramelessDialogShell(root);
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Close();
            };
        }

        public void ShowUntilClosed()
        {
            var owner = Owner;
            var ownerHitTestVisible = owner == null || owner.IsHitTestVisible;
            var frame = new DispatcherFrame();
            EventHandler closed = null;
            closed = (s, e) =>
            {
                Closed -= closed;
                frame.Continue = false;
            };
            Closed += closed;
            try
            {
                if (owner != null && owner.IsVisible)
                {
                    owner.IsHitTestVisible = false;
                }

                Show();
                Dispatcher.PushFrame(frame);
            }
            finally
            {
                if (owner != null && owner.IsVisible)
                {
                    owner.IsHitTestVisible = ownerHitTestVisible;
                }
            }
        }
    }

    internal sealed class MetadataAuditProgressWindow : Window
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Action<CancellationToken, Action<string>> operation;
        private readonly Window operationOwner;
        private readonly Button cancelButton = new Button();
        private TextBlock messageText;
        private bool completed;
        private bool ownerHitTestVisible;

        public Exception Error { get; private set; }
        public bool Cancelled { get; private set; }

        public MetadataAuditProgressWindow(MetaDataIAPlugin plugin, Window owner, string message, Action<CancellationToken> operation)
            : this(plugin, owner, message, (token, report) => operation(token))
        {
        }

        public MetadataAuditProgressWindow(MetaDataIAPlugin plugin, Window owner, string message, Action<CancellationToken, Action<string>> operation)
        {
            this.operation = operation;
            operationOwner = owner;
            Title = plugin.Loc("MTDA_PluginName", "Metadata AI");
            Width = 480;
            SizeToContent = SizeToContent.Height;
            MinHeight = 180;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            ShowInTaskbar = false;
            SnapsToDevicePixels = true;
            MetadataTrustUi.ApplyWindowTheme(this, plugin.GetAppearancePreset(), false);
            if (owner != null)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            var shell = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(24, 20, 24, 20),
                SnapsToDevicePixels = true
            };
            MetadataTrustUi.SetResource(shell, Border.BorderBrushProperty, "Narian.Border");
            MetadataTrustUi.SetResource(shell, Border.BackgroundProperty, "Narian.Bg");

            var root = new Grid();
            MetadataTrustUi.ApplyTextBrush(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = MetadataTrustUi.Text(plugin.Loc("MTDA_PluginName", "Metadata AI"));
            title.FontSize = 20;
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(0, 0, 0, 10);
            MetadataTrustUi.SetResource(title, TextBlock.ForegroundProperty, "Narian.Accent");
            root.Children.Add(title);

            messageText = MetadataTrustUi.Text(message);
            messageText.FontSize = 14;
            messageText.Margin = new Thickness(0, 0, 0, 16);
            Grid.SetRow(messageText, 1);
            root.Children.Add(messageText);

            var progress = new ProgressBar { IsIndeterminate = true, Height = 8, Margin = new Thickness(0, 0, 0, 22) };
            Grid.SetRow(progress, 2);
            root.Children.Add(progress);

            cancelButton.Content = plugin.Loc("MTDA_Cancel", "Cancel");
            cancelButton.MinWidth = 110;
            cancelButton.HorizontalAlignment = HorizontalAlignment.Right;
            MetadataTrustUi.StyleSecondaryButton(cancelButton);
            cancelButton.Click += (s, e) => { Cancelled = true; cancelButton.IsEnabled = false; cancellation.Cancel(); };
            Grid.SetRow(cancelButton, 3);
            root.Children.Add(cancelButton);
            shell.Child = root;
            Content = shell;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Cancelled = true;
                    cancelButton.IsEnabled = false;
                    cancellation.Cancel();
                }
            };
            Loaded += RunOperation;
            Closing += (s, e) =>
            {
                if (completed) return;
                e.Cancel = true;
                Cancelled = true;
                cancelButton.IsEnabled = false;
                cancellation.Cancel();
            };
            Closed += (s, e) =>
            {
                if (operationOwner != null && operationOwner.IsVisible) operationOwner.IsHitTestVisible = ownerHitTestVisible;
                ReleaseModalOwners(owner);
            };
        }

        // ShowDialog creates a theme-dependent modal backdrop in Playnite that can
        // survive closing nested audit windows. Keep this spinner modeless and
        // block only hit testing on its owner while a nested dispatcher frame waits.
        public void ShowUntilCompleted()
        {
            var frame = new DispatcherFrame();
            EventHandler closed = null;
            closed = (s, e) =>
            {
                Closed -= closed;
                frame.Continue = false;
            };
            Closed += closed;
            try
            {
                if (operationOwner != null && operationOwner.IsVisible)
                {
                    ownerHitTestVisible = operationOwner.IsHitTestVisible;
                    operationOwner.IsHitTestVisible = false;
                }
                Show();
                Dispatcher.PushFrame(frame);
            }
            finally
            {
                if (operationOwner != null && operationOwner.IsVisible) operationOwner.IsHitTestVisible = ownerHitTestVisible;
            }
        }

        private async void RunOperation(object sender, RoutedEventArgs e)
        {
            try
            {
                // Playnite's own global progress invokes extension work from an
                // STA worker. Keep the same requirement for this custom progress:
                // metadata integrations and database-backed models can otherwise
                // throw when accessed from a thread-pool MTA worker.
                await RunStaOperation(() => operation(cancellation.Token, UpdateMessage));
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
                Close();
            }
        }

        private static Task RunStaOperation(Action action)
        {
            var completion = new TaskCompletionSource<bool>();
            var worker = new Thread(() =>
            {
                try
                {
                    action();
                    completion.SetResult(true);
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            return completion.Task;
        }

        private void UpdateMessage(string value)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (messageText != null) messageText.Text = value ?? string.Empty;
                }));
            }
            catch { }
        }

        private static void ReleaseModalOwners(Window owner)
        {
            var dispatcher = owner == null ? (Application.Current == null ? null : Application.Current.Dispatcher) : owner.Dispatcher;
            if (dispatcher == null) return;
            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    var current = owner;
                    while (current != null)
                    {
                        if (current.IsVisible) current.IsEnabled = true;
                        current = current.Owner;
                    }
                    if (Application.Current == null) return;
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window != null && window.IsVisible) window.IsEnabled = true;
                    }
                }), DispatcherPriority.ApplicationIdle);
            }
            catch { }
        }
    }

    public sealed class ProvenanceGameGroup
    {
        public string GameName { get; set; }
        public IEnumerable<MetadataFieldProvenance> Provenance { get; set; }
    }

    public sealed class ProvenanceWindow
    {
        private readonly Window window;

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
            window = MetadataTrustUi.CreatePluginDialog(
                plugin.Api,
                titleValue,
                plugin.GetAppearancePreset(),
                820,
                650,
                640,
                440);

            StackPanel headerHost;
            Grid bodyHost;
            Border footerBar;
            var root = MetadataTrustUi.CreatePageShell(out headerHost, out bodyHost, out footerBar, null, plugin.GetAppearancePreset());
            headerHost.Children.Add(MetadataTrustUi.PageIntro(titleValue));

            var stack = new StackPanel();
            foreach (var group in groups)
            {
                if (multiple)
                {
                    stack.Children.Add(MetadataTrustUi.SectionHeader(group.GameName, 16, new Thickness(0, 4, 0, 12)));
                }
                foreach (var item in group.Provenance ?? Enumerable.Empty<MetadataFieldProvenance>())
                {
                    stack.Children.Add(BuildEntry(plugin, item));
                }
            }
            bodyHost.Children.Add(new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            var close = new Button { Content = plugin.Loc("MTDA_Close", "Close") };
            MetadataTrustUi.StyleSecondaryButton(close);
            close.Click += (s, e) => window.Close();
            footerBar.Child = MetadataTrustUi.CreateFooterContent(null, close);
            MetadataTrustUi.SetDialogContent(window, root, plugin.GetAppearancePreset());
        }

        public bool? ShowDialog()
        {
            return window.ShowDialog();
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
            panel.Children.Add(BuildConfidence(plugin, item.Confidence));
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

        private static UIElement BuildConfidence(MetaDataIAPlugin plugin, string confidence)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
            var heading = MetadataTrustUi.Text(plugin.Loc("MTDA_ProvenanceConfidence", "Confidence"));
            heading.FontWeight = FontWeights.SemiBold;
            heading.Opacity = 0.76;
            panel.Children.Add(heading);
            panel.Children.Add(MetadataTrustUi.ConfidenceBadge(plugin, confidence));
            return panel;
        }
    }
}
