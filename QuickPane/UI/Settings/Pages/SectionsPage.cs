using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using QuickPane.Models;

namespace QuickPane.UI.Settings.Pages
{
    // Carries over every control from SettingsPanel.xaml.cs's "Pane" tab (right column), at commit
    // 068301b.

    /// <summary>One card per pane section, each with a visibility toggle and Move up / Move down
    /// buttons in place of today's drag-reorder. Visibility reads and writes the same
    /// <see cref="SectionSetting.Visible"/> field, with the same immediate
    /// <c>SettingsStore.Save()</c>, as the old <c>BuildSectionRow</c> checkbox at commit 068301b.
    /// Reorder writes the same <see cref="SectionSetting.Order"/> field the old drag handler did --
    /// <c>OnSectionsDrop</c> (line 646) re-numbers every section 0..n-1 in its new on-screen order
    /// rather than swapping two Order values, which matters because <c>Normalize</c>'s
    /// <c>EnsureSection</c> can leave two sections sharing one Order (e.g. a settings.json saved
    /// before "network" existed). <see cref="MoveSection"/> renumbers for the same reason, and is
    /// unit-tested in Settings92SectionsPageTests.</summary>
    internal sealed class SectionsPage : SettingsPage
    {
        public override string Id => "sections";
        public override string Title => "Pane sections";
        public override string Glyph => "\uE8FD";   // Segoe Fluent Icons Bulleted list

        public override IEnumerable<SettingsSearchEntry> SearchEntries
        {
            get { yield return new SettingsSearchEntry(Id, null, Title, "Which sections the pane shows, and in what order.", "section", "order", "reorder"); }
        }

        public override UIElement Build(SettingsPageContext ctx)
        {
            var panel = new StackPanel();
            Populate(panel, ctx, null, 0);
            return panel;
        }

        /// <summary>(Re)builds every section row into <paramref name="panel"/>. Called once from
        /// <see cref="Build"/> and again, in place, after a Move up/down click: rebuilding only this
        /// panel -- not <see cref="SettingsPageContext.Rebuild"/>, which tears down and redraws the
        /// whole page -- keeps the page's scroll position, and <paramref name="refocusType"/> (the
        /// moved section's <see cref="SectionSetting.Type"/>, or null on first build) plus
        /// <paramref name="refocusDirection"/> (the direction that was clicked, -1 or +1, or 0 for
        /// the visibility toggle, which lands focus back on that row's toggle) lets the
        /// click that triggered the move land keyboard focus back on a button for the same row,
        /// which a full <c>Rebuild()</c> would otherwise drop. Prefers the button for the same
        /// direction clicked; falls back to the opposite direction when the row is now at that
        /// end of the list and the same-direction button is disabled.</summary>
        private void Populate(StackPanel panel, SettingsPageContext ctx, string refocusType, int refocusDirection)
        {
            panel.Children.Clear();
            var cards = ctx.Cards;
            var ordered = OrderedSections(ctx.Settings.Current);
            Button refocusUp = null, refocusDown = null;
            Control refocusToggle = null;
            var rowWrappers = new List<FrameworkElement>();   // one per row, read by each grip while dragging (#178)

            var note = new TextBlock
            {
                Text = "Turn a section off to hide it from the pane, or reorder it. Order here becomes top-to-bottom order in the pane.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            };
            note.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            panel.Children.Add(note);

            for (int i = 0; i < ordered.Count; i++)
            {
                var sec = ordered[i];
                string friendly = Friendly(sec.Type);
                int index = i;   // captured per-iteration

                // Rebuilt after a toggle (not just Move up/down) so refocusDirection == 0 below can
                // land keyboard focus back on this row's own toggle, the same way a move lands it on
                // this row's Move button.
                var toggle = cards.Toggle(sec.Visible, on =>
                {
                    sec.Visible = on;
                    ctx.Settings.Save();
                    Populate(panel, ctx, sec.Type, 0);
                }, "Show " + friendly + " in the pane");

                var up = cards.IconButton("\uE971", "Move " + friendly + " up", () =>
                {
                    if (MoveSection(ordered, index, -1))
                    {
                        ctx.Settings.Save();
                        Populate(panel, ctx, sec.Type, -1);
                    }
                });
                var down = cards.IconButton("\uE972", "Move " + friendly + " down", () =>
                {
                    if (MoveSection(ordered, index, 1))
                    {
                        ctx.Settings.Save();
                        Populate(panel, ctx, sec.Type, 1);
                    }
                });
                // First and last rows have nothing to move into; disabling (rather than hiding) keeps
                // every row the same width and still announces the button's name to a screen reader.
                up.IsEnabled = index > 0;
                down.IsEnabled = index < ordered.Count - 1;

                if (string.Equals(sec.Type, refocusType, System.StringComparison.Ordinal))
                {
                    refocusUp = up;
                    refocusDown = down;
                    refocusToggle = toggle.Tag as Control;   // Toggle() keeps its ToggleButton in Tag
                }

                var controls = cards.ReorderControls(toggle, up, down);

                // No description line: the On/Off switch already says whether the section shows, so
                // CompactCard's single-line row replaces Card's two-line one (#154). What the section
                // is goes in the info button (#205).
                var card = cards.CompactCard("section-" + sec.Type, SectionGlyph(sec.Type), friendly, controls, info: Describe(sec.Type));

                // Drag grip at the row's left edge (#178). The grip lives in a wrapper beside the card,
                // so the card itself stays the one registered for search focus.
                var grip = ReorderDrag.CreateGrip("Drag to reorder " + friendly, rowWrappers, panel, index, (fromIndex, toIndex) =>
                {
                    if (MoveSectionTo(ordered, fromIndex, toIndex))
                    {
                        ctx.Settings.Save();
                        // direction 0 lands focus on the moved row's toggle: the grip is not a tab stop.
                        Populate(panel, ctx, sec.Type, 0);
                    }
                });
                var wrapper = new Grid();
                wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grip.Margin = new Thickness(0, 0, 4, 4);   // bottom 4 matches the card's own bottom margin
                grip.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetColumn(grip, 0);
                Grid.SetColumn(card, 1);
                wrapper.Children.Add(grip);
                wrapper.Children.Add(card);
                rowWrappers.Add(wrapper);
                panel.Children.Add(wrapper);
            }

            panel.Children.Add(BuildResetRow(panel, ctx));

            Control refocusButton = refocusDirection == 0
                ? refocusToggle
                : refocusDirection < 0
                    ? (refocusUp != null && refocusUp.IsEnabled ? refocusUp : refocusDown)
                    : (refocusDown != null && refocusDown.IsEnabled ? refocusDown : refocusUp);

            if (refocusButton != null)
            {
                var target = refocusButton;
                panel.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new System.Action(() =>
                {
                    target.Focus();
                    Keyboard.Focus(target);
                }));
            }
        }

        // ---- reset to default (#178) ----

        /// <summary>The Reset to default button and the inline confirmation slot under it. Confirmation
        /// is inline (<see cref="SettingsCards.ConfirmRow"/>), never a MessageBox, like Groups and SSH.</summary>
        private FrameworkElement BuildResetRow(StackPanel panel, SettingsPageContext ctx)
        {
            var host = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var slot = new StackPanel();
            var reset = new Button { Content = "Reset to default", HorizontalAlignment = HorizontalAlignment.Left };
            SettingsCards.SetAccessibleName(reset, "Reset pane sections to default");
            reset.Click += (s, e) =>
            {
                slot.Children.Clear();
                slot.Children.Add(ctx.Cards.ConfirmRow(
                    "Restore the default order and on/off state of every pane section? Section names you renamed are kept.",
                    "Reset",
                    onConfirm: () =>
                    {
                        var current = ctx.Settings.Current;
                        current.Sections = ResetSections(current.Sections);
                        ctx.Settings.Save();
                        Populate(panel, ctx, null, 0);
                    },
                    onCancel: () => slot.Children.Clear()));
            };
            host.Children.Add(reset);
            host.Children.Add(slot);
            return host;
        }

        /// <summary>The list <paramref name="current"/> becomes after Reset to default: every section
        /// the app seeds (<see cref="AppSettings.CreateDefault"/>, the same list a fresh install starts
        /// from, so a section added in a later version resets correctly) gets its default Order and
        /// Visible, and keeps its existing <see cref="SectionSetting.Title"/> if the user renamed it.
        /// A section type the defaults do not know (an entry from a newer version, after a downgrade) is
        /// left untouched and placed after the defaults rather than dropped. Returns a new list sorted
        /// by Order; the input list is not changed except for an unknown entry's Order.</summary>
        internal static List<SectionSetting> ResetSections(List<SectionSetting> current)
        {
            var defaults = AppSettings.CreateDefault().Sections;
            var result = new List<SectionSetting>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var def in defaults)
            {
                var existing = current == null ? null : current.FirstOrDefault(x => x != null && x.Type == def.Type);
                seen.Add(def.Type);
                result.Add(new SectionSetting
                {
                    Type = def.Type,
                    Visible = def.Visible,
                    Order = def.Order,
                    Title = existing != null ? existing.Title : null,
                });
            }
            if (current != null)
            {
                int next = defaults.Max(d => d.Order) + 1;
                foreach (var extra in current.Where(x => x != null && x.Type != null && !seen.Contains(x.Type)).OrderBy(x => x.Order).ToList())
                {
                    if (!seen.Add(extra.Type)) continue;
                    result.Add(new SectionSetting { Type = extra.Type, Visible = extra.Visible, Order = next++, Title = extra.Title });
                }
            }
            return result.OrderBy(x => x.Order).ToList();
        }

        // ---- reorder logic (unit-tested since it moved out of the UI) ----

        /// <summary>Sections sorted by <see cref="SectionSetting.Order"/>, same sort the old
        /// <c>SectionsBlock</c> used (<c>s.Sections.OrderBy(x =&gt; x.Order)</c>). The returned list
        /// is a fresh list, but its elements are the same <see cref="SectionSetting"/> instances held
        /// by <paramref name="settings"/>.Sections, so mutating one through <see cref="MoveSection"/>
        /// mutates the real setting.</summary>
        internal static List<SectionSetting> OrderedSections(AppSettings settings)
        {
            if (settings.Sections == null) return new List<SectionSetting>();
            return settings.Sections.OrderBy(x => x.Order).ToList();
        }

        /// <summary>Moves <paramref name="ordered"/>[<paramref name="index"/>] one slot in
        /// <paramref name="direction"/> (-1 for Move up, +1 for Move down), then re-numbers every
        /// entry's <see cref="SectionSetting.Order"/> to its new 0..n-1 position -- the same
        /// renumbering the old drag handler did in <c>OnSectionsDrop</c> (commit 068301b, line 646),
        /// not a plain two-value swap: a swap alone can leave two sections tied on the same Order
        /// (<c>Normalize</c>'s <c>EnsureSection</c> can introduce a tie for anyone whose settings
        /// predate a newer section type), and a tie would make this method report success while the
        /// display order never actually changes. Returns false, changing nothing, when
        /// <paramref name="index"/> or the neighbour is out of range -- the first row has no "up" and
        /// the last has no "down". A pure decision over the list's own <c>Order</c> values, so it is
        /// unit-testable with no <c>SettingsStore</c> at all.</summary>
        internal static bool MoveSection(List<SectionSetting> ordered, int index, int direction)
        {
            return MoveSectionTo(ordered, index, index + direction);
        }

        /// <summary>The one place a section changes position, shared by Move up/down (to = from +/- 1)
        /// and a drag-drop (any to, #178): takes <paramref name="ordered"/>[<paramref name="from"/>] out
        /// and puts it at index <paramref name="to"/>, then renumbers every Order 0..n-1 as described on
        /// <see cref="MoveSection"/>. Returns false, changing nothing, when either index is out of range
        /// or they are equal (nothing to move).</summary>
        internal static bool MoveSectionTo(List<SectionSetting> ordered, int from, int to)
        {
            if (ordered == null) return false;
            if (from < 0 || from >= ordered.Count) return false;
            if (to < 0 || to >= ordered.Count) return false;
            if (from == to) return false;

            var moved = ordered[from];
            ordered.RemoveAt(from);
            ordered.Insert(to, moved);

            for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
            return true;
        }

        /// <summary>Each section's own icon. All ten rows once shared the bulleted list, which said
        /// nothing about the section and read like the six-dot drag grip beside it (#207). A section
        /// with a Settings page of its own uses that page's icon. A type this version does not know
        /// keeps the generic list, since its row has nothing more specific to say.</summary>
        internal static string SectionGlyph(string type)
        {
            switch (type)
            {
                case "search": return "\uE721";        // Search
                case "appdefaults": return "\uE8B7";   // Folder
                case "groups": return "\uE718";        // Pin
                case "recents": return "\uE823";       // Recent
                case "computer": return "\uE7F4";      // TVMonitor
                case "network": return "\uE774";       // Globe
                case "linux": return "\uE756";         // CommandPrompt
                case "ssh": return "\uE968";           // the Remote drives page's icon
                case "recentapps": return "\uE81C";    // the Recent apps page's icon
                case "quickactions": return "\uE945";  // the Quick actions page's icon
                default: return "\uE8FD";              // BulletedList
            }
        }

        /// <summary>What a section is, for its row's info button (#205), including when it does not
        /// appear, because a section that is on but missing from the pane otherwise looks broken. The
        /// conditions match <c>SidebarControl.BuildSection</c>. Null for a section type this version
        /// does not know, which then gets no button.</summary>
        internal static string Describe(string type)
        {
            switch (type)
            {
                case "groups": return "Your pinned folders, organized into groups.";
                case "recents": return "Folders you recently browsed in Explorer.";
                case "computer": return "A This PC row that opens the folder and expands to your drives.";
                case "network": return "A Network row that opens your network locations.";
                case "linux": return "Your WSL distributions, browsed like drives. Appears only when WSL is installed.";
                case "ssh": return "Remote folders connected as drive letters. Appears only after you add a connection on the Remote drives page.";
                case "search": return "A box that filters the rows already in the pane as you type. It does not search the disk.";
                case "appdefaults": return "The folder you set as an app's starting point. Appears only in that app's Open and Save dialogs. To set one, right-click a folder while the dialog is open and choose \"Always start here\".";
                case "recentapps": return "The programs you have been using. A row opens its app, and its right-click menu goes to the folder that app's dialog last used.";
                case "quickactions": return "Buttons that act on the folder on screen, such as copying its path. Choose them on the Quick actions page. Not shown in file dialogs or on the desktop dock.";
                default: return null;
            }
        }

        /// <summary>Same friendly names as the old window's private <c>Friendly</c> (commit 068301b,
        /// line 1848), copied rather than shared because <c>SettingsPanel.xaml.cs</c> is deleted from
        /// the tree after #90 and this page owns no shared file to put it in.</summary>
        internal static string Friendly(string type)
        {
            switch (type)
            {
                case "groups": return "Groups";
                case "recents": return "Recent";
                case "computer": return "This PC";
                case "network": return "Network";
                case "linux": return "Linux";
                case "ssh": return SshSection.DefaultTitle;
                case "search": return "Search box";
                case "appdefaults": return "App Folders";
                case "recentapps": return "Recent Apps";
                case "quickactions": return "Quick actions";
                default: return type;
            }
        }
    }
}
