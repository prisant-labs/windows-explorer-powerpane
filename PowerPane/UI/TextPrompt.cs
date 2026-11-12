using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PowerPane.Services;

namespace PowerPane.UI
{
    /// <summary>
    /// Small top-level prompt for a single line of text. The embedded sidebar lives in Explorer's
    /// process and cannot take keyboard focus, so any typing has to happen in a real top-level window
    /// like this one, which becomes foreground and receives the keyboard normally.
    /// </summary>
    internal static class TextPrompt
    {
        /// <summary>Returns the entered text, or null if cancelled or left blank. When
        /// <paramref name="browse"/> is given, the prompt also shows a "Browse..." button that calls it
        /// with the current text and, for a non-null result, replaces the text with it.</summary>
        public static string Ask(string title, string initial = "", Func<string, string> browse = null)
        {
            bool dark = ReadDark();
            var bg = dark ? Color.FromRgb(0x24, 0x24, 0x24) : Color.FromRgb(0xF7, 0xF7, 0xF7);
            var fg = new SolidColorBrush(dark ? Color.FromRgb(0xED, 0xED, 0xED) : Color.FromRgb(0x1A, 0x1A, 0x1A));
            var border = new SolidColorBrush(dark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xCF, 0xCF, 0xCF));

            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                SizeToContent = SizeToContent.Height,
                Width = 320,
                ShowInTaskbar = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var shell = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = border,
                Background = new SolidColorBrush(bg),
                Padding = new Thickness(14)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = fg,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var box = new TextBox { Text = initial ?? string.Empty, FontSize = 13, Padding = new Thickness(4, 3, 4, 3) };
            stack.Children.Add(box);

            string result = null;
            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (browse != null)
            {
                var browseBtn = MakeButton("Browse...", fg, border);
                browseBtn.HorizontalAlignment = HorizontalAlignment.Left;
                browseBtn.Click += (s, e) =>
                {
                    var picked = browse(box.Text);
                    if (picked != null) { box.Text = picked; box.CaretIndex = box.Text.Length; box.Focus(); }
                };
                Grid.SetColumn(browseBtn, 0);
                footer.Children.Add(browseBtn);
            }

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var ok = MakeButton("OK", fg, border);
            var cancel = MakeButton("Cancel", fg, border);
            cancel.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);

            stack.Children.Add(footer);
            shell.Child = stack;
            win.Content = shell;

            Action commit = () => { result = box.Text; win.DialogResult = true; };
            ok.Click += (s, e) => commit();
            cancel.Click += (s, e) => { win.DialogResult = false; };
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; commit(); }
                else if (e.Key == Key.Escape) { e.Handled = true; win.DialogResult = false; }
            };
            win.Loaded += (s, e) => { win.Activate(); box.Focus(); box.SelectAll(); };

            bool? ok2;
            try { ok2 = win.ShowDialog(); }
            catch (Exception ex) { Log.Error("TextPrompt failed", ex); return null; }

            if (ok2 == true && !string.IsNullOrWhiteSpace(result)) return result.Trim();
            return null;
        }

        private static Button MakeButton(string text, Brush fg, Brush border)
        {
            return new Button
            {
                Content = text,
                MinWidth = 74,
                Padding = new Thickness(8, 4, 8, 4),
                Foreground = fg,
                Background = Brushes.Transparent,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
        }

        private static bool ReadDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = key?.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }
    }
}
