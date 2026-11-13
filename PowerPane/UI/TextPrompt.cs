using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using PowerPane.Services;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.UI
{
    /// <summary>What a name prompt needs beyond a heading and a starting text (#243).</summary>
    internal sealed class PromptOptions
    {
        public string Title { get; set; }
        public string Initial { get; set; }

        /// <summary>Name for the text box in UI Automation. Defaults to <see cref="Title"/>.</summary>
        public string InputName { get; set; }

        /// <summary>Shows a Browse button that is given the current text.</summary>
        public Func<string, string> Browse { get; set; }

        /// <summary>Screen rectangle, in device pixels, of the control that opened the prompt. When set,
        /// the prompt opens next to it on that control's monitor. When null it is centred on screen.</summary>
        public PixelRect? Anchor { get; set; }

        /// <summary>The window that holds the anchor, whose monitor and scale decide the placement.</summary>
        public IntPtr AnchorHwnd { get; set; }

        /// <summary>Returns a message while the text is not acceptable, and null when it is. While a
        /// message shows, OK is disabled and Enter does nothing.</summary>
        public Func<string, string> Validate { get; set; }
    }

    /// <summary>
    /// Small top-level prompt for a single line of text. The embedded sidebar lives in Explorer's
    /// process and cannot take keyboard focus, so any typing has to happen in a real top-level window
    /// like this one, which becomes foreground and receives the keyboard normally.
    /// It deliberately has no owner: an owner in Explorer's process would join the two input queues.
    /// </summary>
    internal static class TextPrompt
    {
        /// <summary>Width of the prompt in device-independent units.</summary>
        private const double WidthDip = 320;

        /// <summary>Space between the anchor control and the prompt, in device-independent units.</summary>
        private const double GapDip = 4;

        /// <summary>Returns the entered text, or null if cancelled or left blank. When
        /// <paramref name="browse"/> is given, the prompt also shows a "Browse..." button that calls it
        /// with the current text and, for a non-null result, replaces the text with it. This overload
        /// opens centred on screen; the Settings window uses it.</summary>
        public static string Ask(string title, string initial = "", Func<string, string> browse = null)
        {
            return Ask(new PromptOptions { Title = title, Initial = initial, Browse = browse });
        }

        /// <summary>Returns the entered text, or null if cancelled or left blank.</summary>
        internal static string Ask(PromptOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");
            string title = options.Title ?? string.Empty;
            var validate = options.Validate;

            bool dark = ReadDark();
            var bg = dark ? Color.FromRgb(0x24, 0x24, 0x24) : Color.FromRgb(0xF7, 0xF7, 0xF7);
            var fg = new SolidColorBrush(dark ? Color.FromRgb(0xED, 0xED, 0xED) : Color.FromRgb(0x1A, 0x1A, 0x1A));
            var border = new SolidColorBrush(dark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xCF, 0xCF, 0xCF));
            var warn = new SolidColorBrush(dark ? Color.FromRgb(0xFF, 0x99, 0x99) : Color.FromRgb(0xB0, 0x00, 0x20));

            var win = new Window
            {
                Title = title, // lets UI Automation find the window by its heading
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                SizeToContent = SizeToContent.Height,
                Width = WidthDip,
                ShowInTaskbar = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = options.Anchor.HasValue
                    ? WindowStartupLocation.Manual
                    : WindowStartupLocation.CenterScreen
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
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var box = new TextBox { Text = options.Initial ?? string.Empty, FontSize = 13, Padding = new Thickness(4, 3, 4, 3) };
            AutomationProperties.SetName(box, options.InputName ?? title);
            stack.Children.Add(box);

            // The message line keeps room for two lines whether or not a message shows, so the prompt
            // never changes height after it has been placed.
            TextBlock message = null;
            if (validate != null)
            {
                message = new TextBlock
                {
                    Foreground = warn,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = 30,
                    Margin = new Thickness(0, 6, 0, 0)
                };
                AutomationProperties.SetName(message, "Name problem");
                AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
                stack.Children.Add(message);
            }

            string result = null;
            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (options.Browse != null)
            {
                var browse = options.Browse;
                var browseBtn = MakeButton("Browse...", fg, border);
                AutomationProperties.SetName(browseBtn, "Browse");
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
            AutomationProperties.SetName(ok, "OK");
            AutomationProperties.SetName(cancel, "Cancel");
            cancel.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);

            stack.Children.Add(footer);
            shell.Child = stack;
            win.Content = shell;

            // OK and Enter are available only when the text is acceptable. A prompt with no validator
            // accepts anything, as it always has.
            Func<bool> acceptable = () =>
            {
                if (validate == null) return true;
                if (string.IsNullOrWhiteSpace(box.Text)) return false;
                return Problem(validate, box.Text) == null;
            };
            Action refresh = () =>
            {
                if (validate == null) return;
                string problem = string.IsNullOrWhiteSpace(box.Text) ? null : Problem(validate, box.Text);
                message.Text = problem ?? string.Empty;
                ok.IsEnabled = acceptable();
                ok.Opacity = ok.IsEnabled ? 1.0 : 0.5;
            };
            box.TextChanged += (s, e) => refresh();
            refresh();

            Action commit = () =>
            {
                if (!acceptable()) return;
                result = box.Text;
                win.DialogResult = true;
            };
            ok.Click += (s, e) => commit();
            cancel.Click += (s, e) => { win.DialogResult = false; };
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; commit(); }
                else if (e.Key == Key.Escape) { e.Handled = true; win.DialogResult = false; }
            };

            if (options.Anchor.HasValue)
            {
                var anchor = options.Anchor.Value;
                var anchorHwnd = options.AnchorHwnd;
                // Placed before the window is shown, so it never flashes somewhere else first.
                win.SourceInitialized += (s, e) => PlaceNextToAnchor(win, shell, anchor, anchorHwnd);
                // And checked once WPF has laid it out, in case the real size differs from the estimate.
                win.Loaded += (s, e) => CorrectPlacement(win, anchor, anchorHwnd);
            }
            win.Loaded += (s, e) => { win.Activate(); box.Focus(); box.SelectAll(); };

            bool? ok2;
            try { ok2 = win.ShowDialog(); }
            catch (Exception ex) { Log.Error("TextPrompt failed", ex); return null; }

            if (ok2 == true && !string.IsNullOrWhiteSpace(result)) return result.Trim();
            return null;
        }

        /// <summary>The validator's message for the text, or null. A validator that throws counts as
        /// having no objection, so a bug there cannot stop a person naming something.</summary>
        private static string Problem(Func<string, string> validate, string text)
        {
            try { return validate(text); }
            catch (Exception ex) { Log.Error("TextPrompt validator", ex); return null; }
        }

        /// <summary>Put the window below its anchor in device pixels, from an estimated size, before it
        /// is shown. Pixels, not WPF units, so the monitor's scale cannot shift it by rounding.</summary>
        private static void PlaceNextToAnchor(Window win, FrameworkElement shell, PixelRect anchor, IntPtr anchorHwnd)
        {
            try
            {
                var hwnd = new WindowInteropHelper(win).Handle;
                if (hwnd == IntPtr.Zero) return;

                int dpi = NM.DpiForWindow(anchorHwnd);
                var work = ToPixelRect(NM.WorkAreaFor(anchorHwnd));

                shell.Measure(new Size(WidthDip, double.PositiveInfinity));
                int width = PromptPlacement.ScaleDip(WidthDip, dpi);
                int height = PromptPlacement.ScaleDip(shell.DesiredSize.Height, dpi);
                var spot = PromptPlacement.Compute(anchor, width, height, work, PromptPlacement.ScaleDip(GapDip, dpi));

                NM.SetWindowPos(hwnd, IntPtr.Zero, spot.Left, spot.Top, spot.Width, spot.Height,
                    NM.SWP_NOZORDER | NM.SWP_NOACTIVATE);
            }
            catch (Exception ex) { Log.Error("TextPrompt placement", ex); }
        }

        /// <summary>Move the laid-out window if its real size puts it outside where the anchor says it
        /// belongs. Leaves the size alone.</summary>
        private static void CorrectPlacement(Window win, PixelRect anchor, IntPtr anchorHwnd)
        {
            try
            {
                var hwnd = new WindowInteropHelper(win).Handle;
                NM.RECT r;
                if (hwnd == IntPtr.Zero || !NM.GetWindowRect(hwnd, out r)) return;

                int dpi = NM.DpiForWindow(anchorHwnd);
                var work = ToPixelRect(NM.WorkAreaFor(anchorHwnd));
                var spot = PromptPlacement.Compute(anchor, r.Width, r.Height, work, PromptPlacement.ScaleDip(GapDip, dpi));
                if (spot.Left == r.Left && spot.Top == r.Top) return;

                NM.SetWindowPos(hwnd, IntPtr.Zero, spot.Left, spot.Top, 0, 0,
                    NM.SWP_NOSIZE | NM.SWP_NOZORDER | NM.SWP_NOACTIVATE);
            }
            catch (Exception ex) { Log.Error("TextPrompt placement check", ex); }
        }

        private static PixelRect ToPixelRect(NM.RECT r)
        {
            return new PixelRect(r.Left, r.Top, r.Right, r.Bottom);
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
