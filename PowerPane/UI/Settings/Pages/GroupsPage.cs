using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.UI.Settings.Pages
{
    // Matched line by line against SettingsPanel.xaml.cs at commit 068301b (BuildProfileColumn,
    // BuildProfileGroupRow, OnGroupRowsDrop, CommitGroupOrder, OnProfileDrop, BuildAddProfileColumn,
    // BuildProfileFolderRow, SetProfileFolder/Recents/Width, ReloadIfActive), so every card here
    // carries over the same control-to-card mapping the old tab used.

    /// <summary>Lists every profile as a card, with an "Add a profile" card. Clicking a
    /// profile opens its own sub-page through <see cref="SettingsPageContext.OpenSubPage"/>: Rename,
    /// Activate, Remove, the group list (Move up/down, Move to profile, Delete, Add group), and the
    /// profile's own groups-folder, recents-count, and sidebar-width cards. Move to profile produces
    /// the same result on disk as the old drag, with or without Ctrl.
    ///
    /// Gestures that the old window handled by dragging become buttons here: double-click-to-rename
    /// becomes an inline Rename row instead of a dialog, drag-to-reorder becomes Move up/Move down,
    /// and dragging a group onto another profile's column becomes "Move to profile" with an inline
    /// destination picker and a "Copy instead of move" checkbox.
    ///
    /// This page's own cards show instantly with no folder listing; every call into
    /// <c>GroupStore.ListGroups</c> (a <c>Directory.GetDirectories</c> read) and every write
    /// (<c>CreateGroupFolder</c>, <c>DeleteGroupFolder</c>, <c>MoveGroupFolder</c>,
    /// <c>CopyGroupFolder</c>, <c>ReorderGroupFolders</c>) goes through <c>WorkQueue.Post</c>, with
    /// the UI-thread continuation through <c>WorkQueue.PostUI</c> checking
    /// <see cref="SettingsPageContext.IsCurrent"/> before touching the tree or calling
    /// <see cref="SettingsPageContext.OpenSubPage"/>/<see cref="SettingsPageContext.Rebuild"/>, since
    /// the user may have navigated away while the work was queued.</summary>
    internal sealed class GroupsPage : SettingsPage
    {
        public override string Id => "groups";
        public override string Title => "Groups & profiles";

        // Segoe Fluent Icons People
        public override string Glyph => "\uE716";

        private const string GlyphProfile = "\uE716";      // People
        private const string GlyphFolder = "\uE8B7";        // Folder
        private const string GlyphRecents = "\uE823";       // Recent (clock-ish)
        private const string GlyphWidth = "\uE740";         // Width/column
        private const string GlyphAdd = "\uE710";           // Add
        private const string AddProfileCardId = "add-profile";

        public override IEnumerable<SettingsSearchEntry> SearchEntries
        {
            get
            {
                // Only the page itself and the top-level "Add a profile" card are safe search
                // targets: SearchEntries runs with no SettingsStore, so it cannot enumerate
                // profiles, and a sub-page's cards cannot be reached anyway -- Navigate() (called by
                // CommitSearchSelection) always drops any open sub-page.
                yield return new SettingsSearchEntry(Id, null, Title, "Profiles, groups, and pinned folders.", "profile", "group", "pin");
                yield return new SettingsSearchEntry(Id, AddProfileCardId, "Add a profile", "Start another independent workspace.", "profile", "add", "new profile");
            }
        }

        public override UIElement Build(SettingsPageContext ctx)
        {
            var panel = new StackPanel();
            var profiles = ctx.Settings.Current.Profiles ?? new List<Profile>();

            for (int i = 0; i < profiles.Count; i++)
            {
                int index = i; // captured per-iteration
                var p = profiles[index];
                string root = ctx.Settings.ExpandedGroupsPathFor(p);
                var placeholder = ProfileCard(ctx, index, p, null);
                panel.Children.Add(placeholder);
                int rowSlot = panel.Children.Count - 1;

                LoadGroups(ctx, root, groups => ReplaceChild(panel, rowSlot, ProfileCard(ctx, index, p, groups.Count)));
            }

            var addButton = new Button { Content = "Add profile" };
            addButton.Click += (s, e) =>
            {
                var v = TextPrompt.Ask("New profile name", "");
                ctx.Settings.AddProfile(string.IsNullOrWhiteSpace(v) ? null : v);
                ctx.Rebuild();
            };
            panel.Children.Add(ctx.Cards.Card(AddProfileCardId, GlyphAdd, "Add a profile",
                "Start another independent workspace with its own groups.", addButton, "Add profile"));

            return panel;
        }

        /// <summary>Puts <paramref name="element"/> in place of the child at <paramref name="slot"/>,
        /// or does nothing when the slot is gone. Assigning through <c>Children[slot]</c> throws
        /// "Specified index is already in use" whenever the slot is occupied, which it always is
        /// here, so every Settings open logged a crash and left each profile at "Loading groups…"
        /// (#143). Removing first frees the slot.</summary>
        internal static void ReplaceChild(Panel panel, int slot, UIElement element)
        {
            if (slot < 0 || slot >= panel.Children.Count) return;
            panel.Children.RemoveAt(slot);
            panel.Children.Insert(slot, element);
        }

        /// <summary>One profile's row on the top-level page. <paramref name="groupCount"/> is null
        /// until the background listing finishes, matching the old column's "n group(s)" badge.</summary>
        private FrameworkElement ProfileCard(SettingsPageContext ctx, int index, Profile p, int? groupCount)
        {
            bool active = index == ctx.Settings.Current.ActiveProfileIndex;
            string count = groupCount.HasValue
                ? groupCount.Value + (groupCount.Value == 1 ? " group" : " groups")
                : "Loading groups…";
            string desc = count + (active ? " · Active profile" : "");

            return ctx.Cards.ClickableCard("profile-" + index, GlyphProfile, p.Name, desc,
                () => ctx.OpenSubPage(p.Name, c => BuildProfile(c, index)),
                "Open profile " + p.Name);
        }

        // ---- profile sub-page ------------------------------------------------

        private FrameworkElement BuildProfile(SettingsPageContext ctx, int index)
        {
            var s = ctx.Settings.Current;
            var profiles = s.Profiles ?? new List<Profile>();
            if (index < 0 || index >= profiles.Count)
                return ctx.Cards.InfoBar("This profile is gone", "It was removed in another window.", InfoBarSeverity.Informational);

            var p = profiles[index];
            string root = ctx.Settings.ExpandedGroupsPathFor(p);

            var panel = new StackPanel();
            panel.Children.Add(BuildProfileHeaderCard(ctx, index, p));

            panel.Children.Add(ctx.Cards.Subheader("Groups"));
            var expander = ctx.Cards.Expander("groups-" + index, GlyphProfile, "Groups", "The pinned-folder groups this profile shows.");
            var loadingRow = ctx.Cards.ChildRow("Loading…", new TextBlock());
            expander.AddChild(loadingRow);
            panel.Children.Add(expander.Root);

            LoadGroups(ctx, root, groups =>
            {
                // The expander built above is this render's own instance; a stale callback from a
                // page the user has since left is already filtered out by IsCurrent inside LoadGroups.
                expander.AddChild(BuildGroupRows(ctx, index, p, groups));
                loadingRow.Visibility = Visibility.Collapsed;
            });

            panel.Children.Add(ctx.Cards.Subheader("Folder and limits"));
            panel.Children.Add(BuildGroupsFolderCard(ctx, index, p));
            // Card() names whatever control it is given (its Tag box, for a NumberBox), falling back
            // to the bare title when no name is passed, so each per-profile name is passed again
            // here or it would be replaced by "Recents count" / "Sidebar width".
            string recentsName = "Recents count, " + p.Name;
            panel.Children.Add(ctx.Cards.Card("recents-" + index, GlyphRecents, "Recents count",
                "How many recent items the Recent section keeps.",
                ctx.Cards.NumberBox(p.RecentsMaxCount, 5, 50, 1,
                    v => SetProfileRecents(ctx, index, (int)v),
                    recentsName, "Increase recents count", "Decrease recents count"),
                recentsName));
            string widthName = "Sidebar width in pixels, " + p.Name;
            panel.Children.Add(ctx.Cards.Card("width-" + index, GlyphWidth, "Sidebar width",
                "Width of the pane in px.",
                ctx.Cards.NumberBox(p.SidebarWidthPx, 160, 400, 10,
                    v => SetProfileWidth(ctx, index, (int)v),
                    widthName, "Increase sidebar width", "Decrease sidebar width"),
                widthName));

            return panel;
        }

        /// <summary>The profile card: name, group count is irrelevant here so it just reads "Shown
        /// in the pane when active", Activate (or an "Active" label), Rename, and Remove (hidden
        /// when it is the only profile left, same as the old window's <c>s.Profiles.Count &gt; 1</c>
        /// guard) with an inline confirm row beneath the card.</summary>
        private FrameworkElement BuildProfileHeaderCard(SettingsPageContext ctx, int index, Profile p)
        {
            var host = new StackPanel();
            var renameSlot = new StackPanel();
            var removeSlot = new StackPanel();

            bool active = index == ctx.Settings.Current.ActiveProfileIndex;
            var actions = new StackPanel { Orientation = Orientation.Horizontal };

            if (active)
            {
                var activeLabel = new TextBlock { Text = "Active", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                activeLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                actions.Children.Add(activeLabel);
            }
            else
            {
                var activate = new Button { Content = "Activate" };
                AutomationProperties_SetName(activate, "Activate profile " + p.Name);
                activate.Click += (s, e) => { ctx.Settings.SwitchProfile(index); ctx.OpenSubPage(p.Name, c => BuildProfile(c, index)); };
                actions.Children.Add(activate);
            }

            var rename = ctx.Cards.IconButton("\uE70F", "Rename profile " + p.Name, () =>
            {
                removeSlot.Children.Clear();
                renameSlot.Children.Clear();
                renameSlot.Children.Add(BuildRenameRow(ctx, index, p, renameSlot));
            });
            rename.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(rename);

            if ((ctx.Settings.Current.Profiles ?? new List<Profile>()).Count > 1)
            {
                var remove = ctx.Cards.IconButton("\uE74D", "Remove profile " + p.Name, () =>
                {
                    renameSlot.Children.Clear();
                    removeSlot.Children.Clear();
                    removeSlot.Children.Add(ctx.Cards.ConfirmRow(
                        "Remove profile \"" + p.Name + "\"? Its groups folder is left on disk.", "Remove",
                        onConfirm: () => { ctx.Settings.RemoveProfile(index); ctx.Rebuild(); },
                        onCancel: () => removeSlot.Children.Clear()));
                });
                remove.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(remove);
            }

            host.Children.Add(ctx.Cards.Card("profile-header-" + index, GlyphProfile, p.Name,
                "Shown in the pane when active.", actions));
            host.Children.Add(renameSlot);
            host.Children.Add(removeSlot);
            return host;
        }

        private FrameworkElement BuildRenameRow(SettingsPageContext ctx, int index, Profile p, StackPanel slot)
        {
            var box = new TextBox { Text = p.Name, Width = 200, Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties_SetName(box, "New name for profile " + p.Name);
            var save = new Button { Content = "Save", Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel" };

            Action commit = () =>
            {
                var v = box.Text;
                if (!string.IsNullOrWhiteSpace(v))
                {
                    ctx.Settings.RenameProfile(index, v);
                    ctx.OpenSubPage(ctx.Settings.Current.Profiles[index].Name, c => BuildProfile(c, index));
                }
                else
                {
                    slot.Children.Clear();
                }
            };
            save.Click += (s, e) => commit();
            cancel.Click += (s, e) => slot.Children.Clear();
            box.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) commit(); };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 8, 0, 8) };
            row.Children.Add(box);
            row.Children.Add(save);
            row.Children.Add(cancel);
            return row;
        }

        private FrameworkElement BuildGroupsFolderCard(SettingsPageContext ctx, int index, Profile p)
        {
            // Not Cards.PathField: that helper makes the box read-only whenever a Browse handler is
            // given, but the old row (068301b lines 1202-1221) commits a typed-in path too, on Enter
            // or on losing focus. Built by hand here so that behavior survives unchanged.
            string boxName = "Groups folder, " + p.Name;
            var box = new TextBox { Text = p.GroupsPath, MinWidth = 160 };
            AutomationProperties_SetName(box, boxName);
            // A changed folder re-opens this sub-page so the Groups expander below lists the new
            // root's groups instead of the old one -- the old BuildUI() call at 068301b line 1243.
            box.LostKeyboardFocus += (s, e) => { if (SetProfileFolder(ctx, index, box.Text)) ctx.OpenSubPage(p.Name, c => BuildProfile(c, index)); };
            box.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter && SetProfileFolder(ctx, index, box.Text)) ctx.OpenSubPage(p.Name, c => BuildProfile(c, index)); };

            var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
            AutomationProperties_SetName(browse, "Browse for groups folder, " + p.Name);
            browse.Click += (s, e) =>
            {
                var start = Environment.ExpandEnvironmentVariables(p.GroupsPath);
                var picked = FolderPicker.Pick("Pick a groups folder", start);
                if (!string.IsNullOrEmpty(picked))
                {
                    box.Text = picked;
                    if (SetProfileFolder(ctx, index, picked)) ctx.OpenSubPage(p.Name, c => BuildProfile(c, index));
                }
            };

            var row = new DockPanel { LastChildFill = true, Tag = box };
            DockPanel.SetDock(browse, Dock.Right);
            row.Children.Add(browse);
            row.Children.Add(box);

            return ctx.Cards.Card("folder-" + index, GlyphFolder, "Groups folder",
                "Where this profile's pinned-folder groups are stored.", row, boxName);
        }

        // ---- group rows -------------------------------------------------

        // Internal for the page-build test (#120): BuildProfile adds these rows only once the
        // background listing comes back through WorkQueue.PostUI, which never runs in a test host.
        internal FrameworkElement BuildGroupRows(SettingsPageContext ctx, int index, Profile p, List<Tuple<string, string>> groups)
        {
            var host = new StackPanel();
            for (int i = 0; i < groups.Count; i++)
                host.Children.Add(BuildGroupRow(ctx, index, p, groups, i));

            var add = new Button { Content = "Add group" };
            AutomationProperties_SetName(add, "Add group to " + p.Name);
            add.Click += (s, e) =>
            {
                var v = TextPrompt.Ask("New group name", "");
                if (string.IsNullOrWhiteSpace(v)) return;
                string root = ctx.Settings.ExpandedGroupsPathFor(p);
                WorkQueue.Post(() =>
                {
                    GroupStore.CreateGroupFolder(root, v);
                    // GroupStore.Reload() is documented as UI-thread-only (raises Changed and touches
                    // the bound Groups collection synchronously before its own background rescan), so
                    // it runs from the PostUI continuation, never from this worker.
                    WorkQueue.PostUI(() =>
                    {
                        ReloadIfActive(ctx, index);
                        if (ctx.IsCurrent) ctx.OpenSubPage(p.Name, c => BuildProfile(c, index));
                    });
                });
            };
            host.Children.Add(ctx.Cards.TrailingChildRow(add));
            return host;
        }

        private FrameworkElement BuildGroupRow(SettingsPageContext ctx, int profileIndex, Profile p,
            List<Tuple<string, string>> groups, int groupIndex)
        {
            string folder = groups[groupIndex].Item1;
            string name = groups[groupIndex].Item2;
            string root = ctx.Settings.ExpandedGroupsPathFor(p);

            var slot = new StackPanel();

            var actions = new StackPanel { Orientation = Orientation.Horizontal };

            var up = ctx.Cards.IconButton("\uE70E", "Move " + name + " up", () =>
            {
                var order = Reordered(groups.Select(g => g.Item1).ToList(), groupIndex, -1);
                CommitReorder(ctx, profileIndex, p, root, order);
            });
            up.IsEnabled = groupIndex > 0;
            actions.Children.Add(up);

            var down = ctx.Cards.IconButton("\uE70D", "Move " + name + " down", () =>
            {
                var order = Reordered(groups.Select(g => g.Item1).ToList(), groupIndex, 1);
                CommitReorder(ctx, profileIndex, p, root, order);
            });
            down.Margin = new Thickness(4, 0, 0, 0);
            down.IsEnabled = groupIndex < groups.Count - 1;
            actions.Children.Add(down);

            var others = (ctx.Settings.Current.Profiles ?? new List<Profile>())
                .Select((prof, i) => Tuple.Create(prof, i)).Where(x => x.Item2 != profileIndex).ToList();
            var move = ctx.Cards.IconButton("\uE8AB", "Move group " + name + " to another profile", () =>
            {
                slot.Children.Clear();
                if (others.Count == 0) return; // nowhere to move to; mirrors the old window having no other column to drop on
                slot.Children.Add(BuildMoveForm(ctx, profileIndex, p, folder, name, others, slot));
            });
            move.IsEnabled = others.Count > 0;
            move.Margin = new Thickness(4, 0, 0, 0);
            actions.Children.Add(move);

            var del = ctx.Cards.IconButton("\uE74D", "Delete group " + name, () =>
            {
                slot.Children.Clear();
                slot.Children.Add(ctx.Cards.ConfirmRow("Delete group \"" + name + "\"?", "Delete",
                    onConfirm: () =>
                    {
                        WorkQueue.Post(() =>
                        {
                            GroupStore.DeleteGroupFolder(folder);
                            WorkQueue.PostUI(() =>
                            {
                                ReloadIfActive(ctx, profileIndex);
                                if (ctx.IsCurrent) ctx.OpenSubPage(p.Name, c => BuildProfile(c, profileIndex));
                            });
                        });
                    },
                    onCancel: () => slot.Children.Clear()));
            });
            del.Margin = new Thickness(4, 0, 0, 0);
            actions.Children.Add(del);

            var host = new StackPanel();
            host.Children.Add(ctx.Cards.ChildRow(name, actions));
            host.Children.Add(slot);
            return host;
        }

        private void CommitReorder(SettingsPageContext ctx, int profileIndex, Profile p, string root, List<string> newOrder)
        {
            if (newOrder == null) return; // already at an end; the button should be disabled, but guard anyway
            WorkQueue.Post(() =>
            {
                Log.Info("group order in \"" + root + "\" is now " +
                         string.Join(", ", newOrder.Select(f => Path.GetFileName(f))) + ".");
                GroupStore.ReorderGroupFolders(root, newOrder);
                WorkQueue.PostUI(() =>
                {
                    ReloadIfActive(ctx, profileIndex);
                    if (ctx.IsCurrent) ctx.OpenSubPage(p.Name, c => BuildProfile(c, profileIndex));
                });
            });
        }

        /// <summary>The inline destination picker that replaces dragging a group row onto another
        /// profile's column. Produces the same disk result as the old drag with or without Ctrl
        /// held: same-root without Copy checked is the only no-op, via
        /// <see cref="MoveOrCopy"/>.</summary>
        private FrameworkElement BuildMoveForm(SettingsPageContext ctx, int srcIndex, Profile srcProfile,
            string folder, string groupName, List<Tuple<Profile, int>> others, StackPanel slot)
        {
            var combo = new ComboBox { Width = 160, Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties_SetName(combo, "Destination profile for " + groupName);
            foreach (var o in others) combo.Items.Add(o.Item1.Name);
            combo.SelectedIndex = 0;

            var copy = new CheckBox { Content = "Copy instead of move", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

            var go = new Button { Content = "Move", Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel" };
            // The button says what it will do, so a checked box never reads as a move.
            copy.Checked += (s, e) => go.Content = "Copy";
            copy.Unchecked += (s, e) => go.Content = "Move";
            go.Click += (s, e) =>
            {
                var dest = others[combo.SelectedIndex];
                bool doCopy = copy.IsChecked == true;
                string destRoot = ctx.Settings.ExpandedGroupsPathFor(dest.Item1);
                WorkQueue.Post(() =>
                {
                    bool did = MoveOrCopy(folder, destRoot, doCopy);
                    WorkQueue.PostUI(() =>
                    {
                        // Unconditional reload, like the old OnProfileDrop (068301b line 1173): either
                        // side of the move/copy may be the active profile's groups folder.
                        if (did) ReloadGroupsUnconditionally(ctx);
                        if (ctx.IsCurrent) ctx.OpenSubPage(srcProfile.Name, c => BuildProfile(c, srcIndex));
                    });
                });
            };
            cancel.Click += (s, e) => slot.Children.Clear();

            var moveToLabel = new TextBlock { Text = "Move to: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            moveToLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 8, 0, 8) };
            row.Children.Add(moveToLabel);
            row.Children.Add(combo);
            row.Children.Add(copy);
            row.Children.Add(go);
            row.Children.Add(cancel);
            return row;
        }

        // ---- setters (exact mirrors of SettingsPanel.xaml.cs at 068301b) ------

        /// <summary>Returns true when the folder actually changed and was saved, so the caller
        /// knows whether to re-render the sub-page (its Groups expander must now read the new
        /// root). Mirrors 068301b lines 1235-1244, which always called BuildUI() after saving.</summary>
        private static bool SetProfileFolder(SettingsPageContext ctx, int index, string value)
        {
            var s = ctx.Settings.Current;
            if (index < 0 || index >= s.Profiles.Count || string.IsNullOrWhiteSpace(value)) return false;
            if (string.Equals(s.Profiles[index].GroupsPath, value.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            s.Profiles[index].GroupsPath = value.Trim();
            if (index == s.ActiveProfileIndex) s.GroupsPath = value.Trim();
            ctx.Settings.Save();
            return true;
        }

        private static void SetProfileRecents(SettingsPageContext ctx, int index, int v)
        {
            var s = ctx.Settings.Current;
            if (index < 0 || index >= s.Profiles.Count) return;
            s.Profiles[index].RecentsMaxCount = v;
            if (index == s.ActiveProfileIndex) s.RecentsMaxCount = v;
            ctx.Settings.Save();
        }

        private static void SetProfileWidth(SettingsPageContext ctx, int index, int v)
        {
            var s = ctx.Settings.Current;
            if (index < 0 || index >= s.Profiles.Count) return;
            s.Profiles[index].SidebarWidthPx = v;
            if (index == s.ActiveProfileIndex) s.SidebarWidthPx = v;
            ctx.Settings.Save();
        }

        private static void ReloadIfActive(SettingsPageContext ctx, int profileIndex)
        {
            if (profileIndex == ctx.Settings.Current.ActiveProfileIndex && ctx.Groups != null)
                try { ctx.Groups.Reload(); } catch (Exception ex) { Log.Error("reload active groups", ex); }
        }

        private static void ReloadGroupsUnconditionally(SettingsPageContext ctx)
        {
            try { if (ctx.Groups != null) ctx.Groups.Reload(); } catch (Exception ex) { Log.Error("reload after profile move", ex); }
        }

        // ---- testable decisions (no FrameworkElement, no disk) ---------------

        /// <summary>Swaps the folder at <paramref name="index"/> with its neighbour
        /// <paramref name="delta"/> away, or returns null when that would go past either end (the
        /// caller's Move up/down buttons are disabled at the ends, but this is checked again so a
        /// stale click can never run off the list). Mirrors the row order a drag-to-reorder would
        /// have produced in the old window.</summary>
        internal static List<string> Reordered(List<string> folders, int index, int delta)
        {
            if (folders == null) return null;
            int j = index + delta;
            if (index < 0 || index >= folders.Count || j < 0 || j >= folders.Count) return null;
            var list = new List<string>(folders);
            var tmp = list[index];
            list[index] = list[j];
            list[j] = tmp;
            return list;
        }

        /// <summary>Moves or copies a group folder into another profile's expanded groups root,
        /// exactly as the old OnProfileDrop (068301b lines 1155-1174) did for a drag with or without
        /// Ctrl held: a plain move onto the group's own profile is a no-op, a copy never is. Returns
        /// whether a change was made, so the caller knows whether a reload is worth doing.</summary>
        internal static bool MoveOrCopy(string srcFolder, string destExpandedRoot, bool copy)
        {
            if (string.IsNullOrEmpty(srcFolder) || string.IsNullOrEmpty(destExpandedRoot)) return false;
            string srcRoot = Path.GetDirectoryName(srcFolder);
            if (!copy && string.Equals(srcRoot, destExpandedRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return false; // same profile, plain move is a no-op here

            if (copy) GroupStore.CopyGroupFolder(srcFolder, destExpandedRoot);
            else GroupStore.MoveGroupFolder(srcFolder, destExpandedRoot);
            return true;
        }

        private static void LoadGroups(SettingsPageContext ctx, string root, Action<List<Tuple<string, string>>> onLoaded)
        {
            WorkQueue.Post(() =>
            {
                var groups = GroupStore.ListGroups(root);
                WorkQueue.PostUI(() =>
                {
                    if (!ctx.IsCurrent) return;
                    onLoaded(groups);
                });
            });
        }

        // AutomationProperties.SetName takes a DependencyObject; this local alias keeps every call
        // site above readable without an extra using for System.Windows.Automation everywhere.
        private static void AutomationProperties_SetName(System.Windows.DependencyObject element, string name)
        {
            System.Windows.Automation.AutomationProperties.SetName(element, name);
        }
    }
}
