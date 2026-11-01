using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using QuickPane.Models;
using QuickPane.Services;

namespace QuickPane.UI.Settings.Pages
{
    /// <summary>The Quick actions settings page (quick actions, first version): a master switch -- the
    /// same setting as the "quickactions" pane section's own Visible flag on the Pane sections page,
    /// so turning it on from either page is the same action -- then one card per built-in action with
    /// its own on/off toggle and Move up/Move down buttons. Reordering follows SectionsPage's own
    /// pattern (rebuild this list in place, not the whole page, and refocus the button that moved),
    /// since the spec asked for the existing pattern rather than a second one. Unlike a
    /// <see cref="SectionSetting"/>, a <see cref="QuickActionSetting"/> carries no separate Order
    /// field: its position in <see cref="AppSettings.QuickActions"/> is its order in the pane, so a
    /// move is a plain list remove/insert with nothing left to renumber afterwards.</summary>
    internal sealed class QuickActionsPage : SettingsPage
    {
        public override string Id => "quickactions";
        public override string Title => "Quick actions";
        public override string Glyph => "";   // Segoe Fluent Icons Flash

        public override IEnumerable<SettingsSearchEntry> SearchEntries
        {
            get
            {
                yield return new SettingsSearchEntry(Id, "quickActionsSwitch", Title,
                    "A Quick actions section in the pane: copy a path, create a new file or folder, or open a terminal or editor.",
                    "quick actions", "copy path", "terminal", "visual studio code", "markdown", "new folder");
            }
        }

        public override UIElement Build(SettingsPageContext ctx)
        {
            var panel = new StackPanel();
            var s = ctx.Settings.Current;
            var cards = ctx.Cards;

            var section = s.Sections != null ? s.Sections.FirstOrDefault(x => x.Type == "quickactions") : null;
            var masterToggle = cards.Toggle(section != null && section.Visible, on =>
            {
                var sec = s.Sections.FirstOrDefault(x => x.Type == "quickactions");
                if (sec != null) sec.Visible = on;
                ctx.Settings.Save();
            }, "Show Quick actions in the pane");
            panel.Children.Add(cards.Card("quickActionsSwitch", Glyph, "Quick actions",
                "A Quick actions section in the pane, acting on the folder it is currently showing. " +
                "Off by default, so the pane still matches Explorer until you turn this on.",
                masterToggle, "Show Quick actions in the pane"));

            var note = new TextBlock
            {
                Text = "Turn an action off to hide it from the pane, or reorder it. Order here becomes top-to-bottom order in the pane.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 12),
            };
            note.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            panel.Children.Add(note);

            var rows = new StackPanel();
            panel.Children.Add(rows);
            Populate(rows, ctx, null, 0);

            return panel;
        }

        /// <summary>(Re)builds every action row into <paramref name="panel"/>, same reasoning as
        /// SectionsPage.Populate: rebuilding only this panel keeps the page's scroll position, and
        /// <paramref name="refocusId"/> (the moved action's Id, or null on first build) plus
        /// <paramref name="refocusDirection"/> (the direction clicked, -1/+1, or 0 for the visibility
        /// toggle) lands keyboard focus back on a button for the same row.</summary>
        private void Populate(StackPanel panel, SettingsPageContext ctx, string refocusId, int refocusDirection)
        {
            panel.Children.Clear();
            var cards = ctx.Cards;
            var list = ActionList(ctx.Settings.Current);
            Button refocusUp = null, refocusDown = null;
            Control refocusToggle = null;

            for (int i = 0; i < list.Count; i++)
            {
                var setting = list[i];
                var def = QuickActionLibrary.Find(setting.Id);
                string name = def != null ? def.Name : setting.Id;
                string description = def != null ? def.Description : "";
                int index = i;

                var toggle = cards.Toggle(setting.Enabled, on =>
                {
                    setting.Enabled = on;
                    ctx.Settings.Save();
                    Populate(panel, ctx, setting.Id, 0);
                }, "Show " + name + " in the pane");

                var up = cards.IconButton("", "Move " + name + " up", () =>
                {
                    if (MoveAction(list, index, -1))
                    {
                        ctx.Settings.Save();
                        Populate(panel, ctx, setting.Id, -1);
                    }
                });
                var down = cards.IconButton("", "Move " + name + " down", () =>
                {
                    if (MoveAction(list, index, 1))
                    {
                        ctx.Settings.Save();
                        Populate(panel, ctx, setting.Id, 1);
                    }
                });
                // First and last rows have nothing to move into; disabling (rather than hiding) keeps
                // every row the same width and still announces the button's name to a screen reader.
                up.IsEnabled = index > 0;
                down.IsEnabled = index < list.Count - 1;

                if (string.Equals(setting.Id, refocusId, System.StringComparison.Ordinal))
                {
                    refocusUp = up;
                    refocusDown = down;
                    refocusToggle = toggle.Tag as Control;   // Toggle() keeps its ToggleButton in Tag
                }

                var controls = new StackPanel { Orientation = Orientation.Horizontal };
                controls.Children.Add(toggle);
                controls.Children.Add(up);
                controls.Children.Add(down);

                panel.Children.Add(cards.Card("action-" + setting.Id, "", name, description, controls));
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

        // ---- reorder logic (unit-tested, same shape as Settings92SectionsPageTests) --------------

        /// <summary>The live, ordered action list -- its position in the list is its order in the
        /// pane, so unlike SectionsPage.OrderedSections there is nothing to sort and no reason to
        /// return a copy: MoveAction mutates this same list, and the caller saves it as-is.</summary>
        internal static List<QuickActionSetting> ActionList(AppSettings settings)
        {
            return settings.QuickActions ?? new List<QuickActionSetting>();
        }

        /// <summary>Moves <paramref name="list"/>[<paramref name="index"/>] one slot in
        /// <paramref name="direction"/> (-1 for Move up, +1 for Move down). Returns false, changing
        /// nothing, when <paramref name="index"/> or its neighbour is out of range -- the first row
        /// has no "up" and the last has no "down".</summary>
        internal static bool MoveAction(List<QuickActionSetting> list, int index, int direction)
        {
            if (list == null) return false;
            int target = index + direction;
            if (index < 0 || index >= list.Count) return false;
            if (target < 0 || target >= list.Count) return false;

            var moved = list[index];
            list.RemoveAt(index);
            list.Insert(target, moved);
            return true;
        }
    }
}
