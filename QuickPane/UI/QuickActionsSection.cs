using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickPane.Interop;
using QuickPane.Models;
using QuickPane.Services;

namespace QuickPane.UI
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
    /// every click resolves the folder fresh, off the UI thread, at the moment it is clicked.</summary>
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
                var def = QuickActionLibrary.Find(setting.Id);
                if (def == null) continue;

                if (def.Id == "open-vscode")
                {
                    QuickActionLibrary.BeginCodeCheck();
                    if (QuickActionLibrary.CodeCheckDone && !QuickActionLibrary.CodeOnPath) continue;
                }

                items.Children.Add(BuildRow(def));
                any = true;
            }
            if (!any) items.Children.Add(Hint("Turn on an action on the Quick actions settings page."));

            if (_expanded) chevron.Angle = 90;
            else { items.Visibility = Visibility.Collapsed; items.MaxHeight = 0; }

            _root.Children.Add(header);
            _root.Children.Add(items);
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
            row.MouseEnter += (s, e) => row.Background = UiHelpers.AppBrush("ItemHoverBackground");
            row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
            row.GotKeyboardFocus += (s, e) => row.Background = UiHelpers.AppBrush("ItemHoverBackground");
            row.LostKeyboardFocus += (s, e) => row.Background = Brushes.Transparent;

            var hostWindow = _hostWindow;
            var id = def.Id;
            Action run = () => WorkQueue.Post(() =>
            {
                var folder = ExplorerNavigator.GetCurrentPath(hostWindow);
                QuickActionRunner.Run(id, folder);
            });
            row.MouseLeftButtonUp += (s, e) => run();
            row.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                run();
                e.Handled = true;
            };
            return row;
        }

        // Segoe Fluent Icons code points: Copy, Add, OpenInNewWindow.
        private static string Glyph(QuickActionKind kind)
        {
            switch (kind)
            {
                case QuickActionKind.CopyPath: return "";
                case QuickActionKind.NewItem: return "";
                case QuickActionKind.OpenTool: return "";
                default: return "";
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
