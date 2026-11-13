using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PowerPane.Interop;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.UI
{
    /// <summary>The Quick actions section (quick actions, first version): one row per enabled action, in
    /// the order chosen on the Quick actions settings page, each acting on the folder the host
    /// Explorer window currently shows. SidebarControl never builds this section at all inside a
    /// file dialog -- there is no reliable way to read a dialog's current folder the way
    /// ExplorerNavigator.GetCurrentPath reads a real Explorer window's, so the spec's "hide the
    /// section if the current folder is not known there" is decided before this class ever runs.
    /// Rows carry no edit controls, matching every other pane row; reordering and enabling live only
    /// on the Settings page.
    ///
    /// A row does not know its folder when it is built: the pane rebuilds only on a theme or settings
    /// change, never on an Explorer navigation, so caching the folder at build time would let a click
    /// act on wherever the window was when the pane last rebuilt rather than where it is now. Instead
    /// every click resolves the folder fresh, off the UI thread, at the moment it is clicked. The
    /// selection is read the same way, and only for an action that uses it (#191).</summary>
    internal sealed class QuickActionsSection : UserControl
    {
        private readonly StackPanel _root = new StackPanel();
        private bool _expanded = true;
        private bool _wired;
        private bool _detached;
        private IntPtr _hostWindow;

        public QuickActionsSection()
        {
            Focusable = false;
            Content = _root;
            Unloaded += (s, e) => Unwire();
        }

        private void Wire()
        {
            if (_wired || _detached) return;
            _wired = true;
            QuickActionLibrary.CodeAvailabilityChanged += OnCodeAvailabilityChanged;
        }

        private void Unwire()
        {
            if (!_wired) return;
            _wired = false;
            QuickActionLibrary.CodeAvailabilityChanged -= OnCodeAvailabilityChanged;
        }

        /// <summary>Release this section's subscriptions for good. Called by the pane, because a pane
        /// hosted in an HwndSource never raises Unloaded (#69).</summary>
        public void Detach() { _detached = true; Unwire(); }

        private void OnCodeAvailabilityChanged()
        {
            if (_detached) return;
            Build(_hostWindow);
        }

        public void Build(IntPtr hostWindow)
        {
            if (_detached) return;
            _hostWindow = hostWindow;
            Wire();
            _root.Children.Clear();
            if (App.Settings == null) return;

            var items = new StackPanel();
            RotateTransform chevron = null;
            TextBlock label;
            _expanded = UiState.GetExpanded("section:quickactions");
            var header = UiHelpers.BuildHeader(
                RecentsSection.SectionTitle("quickactions", "Quick actions"),
                () =>
                {
                    _expanded = !_expanded;
                    UiState.SetExpanded("section:quickactions", _expanded);
                    UiHelpers.ToggleExpand(items, chevron, _expanded);
                },
                () => RecentsSection.RenameSectionPrompt("quickactions", "Quick actions"),
                out chevron, out label);
            header.ContextMenu = RecentsSection.SectionMenu("quickactions", "Quick actions");

            var settings = App.Settings.Current.QuickActions ?? new List<QuickActionSetting>();
            bool any = false;
            foreach (var setting in settings)
            {
                if (!setting.Enabled) continue;
                var def = QuickActionLibrary.Resolve(App.Settings.Current, setting.Id);
                if (def == null) continue;

                if (!IsAvailable(def)) continue;

                items.Children.Add(BuildRow(def));
                any = true;
            }
            if (!any) items.Children.Add(Hint("Turn on an action on the Quick actions settings page."));

            if (_expanded) chevron.Angle = 90;
            else { items.Visibility = Visibility.Collapsed; items.MaxHeight = 0; }

            _root.Children.Add(header);
            _root.Children.Add(items);
        }

        /// <summary>False for an action whose tool is missing from this machine, so its row never
        /// appears only to do nothing when clicked. Shared with a pinned row's submenu.</summary>
        internal static bool IsAvailable(QuickActionDefinition def)
        {
            if (def.Id == "open-vscode" || def.Id == "open-selected-vscode")
            {
                QuickActionLibrary.BeginCodeCheck();
                return !QuickActionLibrary.CodeCheckDone || QuickActionLibrary.CodeOnPath;
            }
            if (def.Id == "open-wsl") return ShellRootSection.WslPresent();
            return true;
        }

        private FrameworkElement BuildRow(QuickActionDefinition def)
        {
            var icon = new TextBlock
            {
                Text = Glyph(def.Kind),
                FontFamily = UiHelpers.IconFont(),
                FontSize = 14,
                Width = 20,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = UiHelpers.AppBrush("TextSecondary"),
            };

            var text = new TextBlock
            {
                Text = def.Name,
                FontSize = 12,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = UiHelpers.AppBrush("TextPrimary"),
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0) };
            content.Children.Add(icon);
            content.Children.Add(text);

            var row = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 4, 8, 4),
                Cursor = Cursors.Hand,
                Child = content,
                ToolTip = def.Description,
                Focusable = true,
            };
            UiHelpers.Named(row, def.Name);
            var press = new PressFeedback(row);
            row.MouseEnter += (s, e) => press.Rest();
            row.MouseLeave += (s, e) => press.Rest();
            row.GotKeyboardFocus += (s, e) => press.Rest();
            row.LostKeyboardFocus += (s, e) => press.Rest();
            row.MouseLeftButtonDown += (s, e) => press.Down();

            var hostWindow = _hostWindow;
            Action run = () => WorkQueue.Post(() =>
            {
                var folder = ExplorerNavigator.GetCurrentPath(hostWindow);
                List<string> selection = null;
                var status = SelectionReadStatus.Ok;
                if (QuickActionLibrary.UsesSelection(def))
                    status = ExplorerNavigator.GetSelectedPaths(hostWindow, out selection);
                string message = QuickActionRunner.SelectionProblem(status)
                    ?? QuickActionRunner.Run(def, folder, selection);
                if (message != null) WorkQueue.PostUI(() => ShowMessage(row, text, def, message));
            });
            row.MouseLeftButtonUp += (s, e) => { press.Up(); run(); };
            row.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                press.Flash();
                run();
                e.Handled = true;
            };
            return row;
        }

        /// <summary>Darkens a row while it is pressed, so a click shows it landed even when the action
        /// itself shows nothing, such as a copy. A real click lasts well under a tenth of a second, so
        /// the pressed look is held for <see cref="MinPressMs"/> after the button goes down, and the
        /// keyboard gets the same flash. Afterwards the row goes back to the hover look while the
        /// pointer or keyboard focus is still on it.</summary>
        private sealed class PressFeedback
        {
            private const int MinPressMs = 140;
            private readonly Border _row;
            private System.Windows.Threading.DispatcherTimer _timer;
            private bool _down;
            private int _downTick;

            public PressFeedback(Border row) { _row = row; }

            public void Down()
            {
                _down = true;
                _downTick = Environment.TickCount;
                StopTimer();
                _row.Background = PressedBrush();
            }

            public void Up()
            {
                if (!_down) return;
                _down = false;
                int held = unchecked(Environment.TickCount - _downTick);
                if (held >= 0 && held < MinPressMs) RestAfter(MinPressMs - held);
                else Rest();
            }

            public void Flash()
            {
                _row.Background = PressedBrush();
                RestAfter(MinPressMs);
            }

            /// <summary>The look with no press in progress: hover while the pointer or keyboard focus is
            /// on the row, clear otherwise. A pointer that leaves mid-press drops the press.</summary>
            public void Rest()
            {
                if (_down && !_row.IsMouseOver) _down = false;
                if (_down || (_timer != null && _timer.IsEnabled)) return;
                _row.Background = _row.IsMouseOver || _row.IsKeyboardFocused
                    ? UiHelpers.AppBrush("ItemHoverBackground")
                    : Brushes.Transparent;
            }

            private void RestAfter(int ms)
            {
                StopTimer();
                _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                _timer.Tick += (s, e) => { StopTimer(); Rest(); };
                _timer.Start();
            }

            private void StopTimer()
            {
                if (_timer == null) return;
                _timer.Stop();
                _timer = null;
            }

            private static Brush PressedBrush()
            {
                var hover = UiHelpers.AppBrush("ItemHoverBackground") as SolidColorBrush;
                if (hover == null) return UiHelpers.AppBrush("ItemActiveBackground");
                var brush = new SolidColorBrush(PressedColor(hover.Color));
                brush.Freeze();
                return brush;
            }
        }

        /// <summary>The pressed color: the hover color at twice its opacity. Every theme's hover brush is
        /// a translucent accent (15% normally, 25% in high contrast), so doubling it is a step anyone
        /// can see, where the 20% ItemActiveBackground is too close to hover to notice in a click.</summary>
        internal static Color PressedColor(Color hover)
        {
            int alpha = Math.Min(255, hover.A * 2);
            return Color.FromArgb((byte)alpha, hover.R, hover.G, hover.B);
        }

        /// <summary>Puts a short message in the row for a few seconds, because an action that could not
        /// run must not look as if nothing was clicked. The tooltip holds it too, since a narrow pane
        /// trims the row's text. The pane rebuilding meanwhile only leaves this acting on a retired row.</summary>
        private static void ShowMessage(Border row, TextBlock text, QuickActionDefinition def, string message)
        {
            text.Text = message;
            row.ToolTip = message;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                text.Text = def.Name;
                row.ToolTip = def.Description;
            };
            timer.Start();
        }

        // Segoe Fluent Icons code points: Copy, Add, OpenInNewWindow, Pin, SelectAll, CommandPrompt.
        internal static string Glyph(QuickActionKind kind)
        {
            switch (kind)
            {
                case QuickActionKind.CopyPath: return "\uE8C8";
                case QuickActionKind.NewItem: return "\uE710";
                case QuickActionKind.OpenTool: return "\uE8A7";
                case QuickActionKind.FolderAction: return "\uE718";
                case QuickActionKind.Selection: return "\uE8B3";
                case QuickActionKind.Custom: return "\uE756";
                default: return "\uE8C8";
            }
        }

        private static TextBlock Hint(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                Opacity = 0.6,
                Margin = new Thickness(14, 2, 8, 4),
                TextWrapping = TextWrapping.Wrap,
                Foreground = UiHelpers.AppBrush("TextSecondary")
            };
        }
    }
}
