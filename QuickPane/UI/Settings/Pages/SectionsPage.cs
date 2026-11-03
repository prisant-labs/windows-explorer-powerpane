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
        public override string Glyph => "";   // Segoe Fluent Icons Bulleted list

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

                var up = cards.IconButton("", "Move " + friendly + " up", () =>
                {
                    if (MoveSection(ordered, index, -1))
                    {
                        ctx.Settings.Save();
                        Populate(panel, ctx, sec.Type, -1);
                    }
                });
                var down = cards.IconButton("", "Move " + friendly + " down", () =>
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

                var controls = new StackPanel { Orientation = Orientation.Horizontal };
                controls.Children.Add(toggle);
                controls.Children.Add(up);
                controls.Children.Add(down);

                // No description: the On/Off switch already says whether the section shows, so
                // CompactCard's single-line row replaces Card's two-line one (#154).
                panel.Children.Add(cards.CompactCard("section-" + sec.Type, "", friendly, controls));
            }

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
            if (ordered == null) return false;
            int target = index + direction;
            if (index < 0 || index >= ordered.Count) return false;
            if (target < 0 || target >= ordered.Count) return false;

            var moved = ordered[index];
            ordered.RemoveAt(index);
            ordered.Insert(target, moved);

            for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
            return true;
        }

        /// <summary>Same friendly names as the old window's private <c>Friendly</c> (commit 068301b,
        /// line 1848), copied rather than shared because <c>SettingsPanel.xaml.cs</c> is deleted from
        /// the tree after #90 and this page owns no shared file to put it in.</summary>
        private static string Friendly(string type)
        {
            switch (type)
            {
                case "groups": return "Groups";
                case "recents": return "Recent";
                case "computer": return "This PC";
                case "network": return "Network";
                case "linux": return "Linux";
                case "ssh": return "SSH";
                case "search": return "Search box";
                case "appdefaults": return "App Folders";
                case "recentapps": return "Recent Apps";
                case "quickactions": return "Quick actions";
                default: return type;
            }
        }
    }
}
