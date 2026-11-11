using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.UI
{
    /// <summary>
    /// Sidebar section listing saved SSH connections. A connected profile renders as an expandable
    /// root node pointing at its mounted drive, exactly like the Linux section points at \\wsl$. A
    /// disconnected profile renders as a plain row with a Connect action instead of an expander.
    /// </summary>
    internal sealed class SshSection : UserControl
    {
        /// <summary>The section's name until someone renames it. It matches the Settings page, which
        /// #175 named for what a person gets rather than the protocol behind it (#204).</summary>
        internal const string DefaultTitle = "Remote drives";

        private readonly StackPanel _root = new StackPanel();
        private bool _expanded = true;

        public SshSection()
        {
            Focusable = false;
            Content = _root;
        }

        public void Build(Action<string> navigate)
        {
            _root.Children.Clear();
            if (App.Settings == null) return;
            var profiles = App.Settings.Current.SshProfiles;
            if (profiles == null || profiles.Count == 0) return;

            var items = new StackPanel();
            RotateTransform chevron = null;
            TextBlock label;
            _expanded = UiState.GetExpanded("section:ssh");
            var header = UiHelpers.BuildHeader(
                RecentsSection.SectionTitle("ssh", DefaultTitle),
                () => { _expanded = !_expanded; UiState.SetExpanded("section:ssh", _expanded); UiHelpers.ToggleExpand(items, chevron, _expanded); },
                () => RecentsSection.RenameSectionPrompt("ssh", DefaultTitle),
                out chevron, out label);
            header.ContextMenu = RecentsSection.SectionMenu("ssh", DefaultTitle);

            foreach (var profile in profiles)
                items.Children.Add(BuildRow(profile, navigate));

            if (_expanded) chevron.Angle = 90;
            else { items.Visibility = Visibility.Collapsed; items.MaxHeight = 0; }

            _root.Children.Add(header);
            _root.Children.Add(items);
        }

        private static FrameworkElement BuildRow(SshProfile profile, Action<string> navigate)
        {
            bool connected = SshfsService.IsMounted(profile.Name);

            if (connected)
            {
                string drive = SshfsService.DrivePath(profile);
                // No overrideIcon: the node binds its own real icon in the background (#76).
                var node = new FolderTreeNode(profile.Name, drive, true, navigate, 0, false, null);
                node.Row.ContextMenu = BuildMenu(profile, true);
                return node;
            }

            var row = new Grid { Margin = new Thickness(6, 2, 8, 2), Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = profile.Name,
                FontSize = 12,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = UiHelpers.AppBrush("TextPrimary")
            };
            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            var connect = new Button
            {
                Content = "Connect",
                FontSize = 11,
                Padding = new Thickness(6, 1, 6, 1),
                Background = Brushes.Transparent,
                BorderBrush = UiHelpers.AppBrush("SeparatorColor"),
                BorderThickness = new Thickness(1),
                Foreground = UiHelpers.AppBrush("TextPrimary"),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            connect.Click += (s, e) =>
            {
                var err = SshfsService.Mount(profile);
                if (err != null) System.Windows.Forms.MessageBox.Show(err, "PowerPane");
            };
            Grid.SetColumn(connect, 1);
            row.Children.Add(connect);
            row.ContextMenu = BuildMenu(profile, false);
            return row;
        }

        private static ContextMenu BuildMenu(SshProfile profile, bool connected)
        {
            var menu = new ContextMenu();
            var item = new MenuItem { Header = connected ? "Disconnect" : "Connect" };
            item.Click += (s, e) =>
            {
                if (connected) SshfsService.Unmount(profile);
                else
                {
                    var err = SshfsService.Mount(profile);
                    if (err != null) System.Windows.Forms.MessageBox.Show(err, "PowerPane");
                }
            };
            menu.Items.Add(item);
            var settings = new MenuItem { Header = "Remote drive settings..." };
            settings.Click += (s, e) => App.ShowSettings();
            menu.Items.Add(settings);
            return menu;
        }
    }
}
