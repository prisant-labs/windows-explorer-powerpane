using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
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
        public override string Glyph => "\uE945";   // Segoe Fluent Icons Flash

        public override IEnumerable<SettingsSearchEntry> SearchEntries
        {
            get
            {
                yield return new SettingsSearchEntry(Id, "quickActionsSwitch", Title,
                    "A Quick actions section in the pane: copy a path, create a new file or folder, or open a terminal or editor.",
                    "quick actions", "copy path", "terminal", "visual studio code", "markdown", "new folder",
                    "folder name", "file url", "powershell", "wsl", "readme", "gitignore", "pin", "selected", "selection", "files");
                yield return new SettingsSearchEntry(Id, CustomCommandsCardId, "Custom commands",
                    "Add a quick action that runs a program of your choice in the folder.",
                    "custom", "command", "template", "run program", "script");
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
            panel.Children.Add(BuildResetRow(rows, ctx));
            panel.Children.Add(cards.Subheader("Custom commands"));
            panel.Children.Add(BuildCustomCommandEditor(rows, ctx));

            return panel;
        }

        // ---- custom commands (#190) ----

        private const string CustomCommandsCardId = "customCommands";
        private const string SampleFolder = @"C:\Demo\Projects\Apollo";

        internal const string CustomCommandHelp =
            "Runs a program in the folder, as you and never elevated. Use a full path, or a program on PATH. " +
            "{paths} is every item selected in Explorer, and {path} and {name} are the first one's path and name. " +
            "With nothing selected, or from a pinned row, those use the folder. {dir} is always the folder. " +
            "QuickPane quotes each value for you, so names with spaces or symbols are safe.";

        // Set by BuildCustomCommandEditor, for the Edit and Delete buttons Populate builds.
        private System.Action<string> _editCustomCommand;
        private System.Action<string, string> _confirmDeleteCustomCommand;

        /// <summary>The form that adds a custom command, or edits the one chosen with a row's Edit
        /// button. It lives in the page panel beside <paramref name="rows"/>, so a Populate does not
        /// wipe what is being typed. The preview and any error update on every keystroke, and only the
        /// check that reads PATH waits until the field loses focus, because that one touches the disk.</summary>
        private FrameworkElement BuildCustomCommandEditor(StackPanel rows, SettingsPageContext ctx)
        {
            string editingId = null;

            var help = new TextBlock { Text = CustomCommandHelp, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            help.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

            var nameBox = new TextBox { MinWidth = 200, Margin = new Thickness(0, 2, 0, 8) };
            AutomationProperties.SetName(nameBox, "Custom command name");
            var templateBox = new TextBox
            {
                MinWidth = 200,
                Margin = new Thickness(0, 2, 0, 8),
                FontFamily = new System.Windows.Media.FontFamily("Consolas, Cascadia Mono, Courier New"),
            };
            AutomationProperties.SetName(templateBox, "Custom command template");

            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

            var save = new Button { Content = "Add command", IsEnabled = false };
            SettingsCards.SetAccessibleName(save, "Add custom command");
            var cancel = new Button { Content = "Cancel", Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
            SettingsCards.SetAccessibleName(cancel, "Cancel editing the custom command");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);

            var slot = new StackPanel();

            System.Action refresh = () =>
            {
                string error = CustomCommandTemplate.Validate(templateBox.Text);
                bool blank = string.IsNullOrWhiteSpace(templateBox.Text);
                status.Text = blank
                    ? "Example: code {path}"
                    : error ?? "Preview: " + CustomCommandTemplate.Preview(templateBox.Text, new[] { SampleFolder }, SampleFolder);
                save.IsEnabled = !blank && error == null && !string.IsNullOrWhiteSpace(nameBox.Text);
            };

            System.Action clear = () =>
            {
                editingId = null;
                nameBox.Text = string.Empty;
                templateBox.Text = string.Empty;
                save.Content = "Add command";
                SettingsCards.SetAccessibleName(save, "Add custom command");
                cancel.Visibility = Visibility.Collapsed;
                refresh();
            };

            nameBox.TextChanged += (s, e) => refresh();
            templateBox.TextChanged += (s, e) => refresh();

            // Tells the user now, rather than at the first click, that this machine cannot run the
            // command. A warning only: the program may be installed later.
            templateBox.LostKeyboardFocus += (s, e) =>
            {
                string template = templateBox.Text;
                if (string.IsNullOrWhiteSpace(template) || CustomCommandTemplate.Validate(template) != null) return;
                WorkQueue.Post(() =>
                {
                    string warning = CustomCommandTemplate.ProgramWarning(template, QuickActionLibrary.FindOnPath);
                    if (warning == null) return;
                    WorkQueue.PostUI(() =>
                    {
                        if (ctx.IsCurrent && templateBox.Text == template) status.Text = warning;
                    });
                });
            };

            save.Click += (s, e) =>
            {
                var settings = ctx.Settings.Current;
                string id = editingId;
                if (id != null) UpdateCustomCommand(settings, id, nameBox.Text, templateBox.Text);
                else id = AddCustomCommand(settings, nameBox.Text, templateBox.Text);
                ctx.Settings.Save();
                clear();
                Populate(rows, ctx, id, 0);
            };
            cancel.Click += (s, e) => clear();

            _editCustomCommand = id =>
            {
                var command = FindCustomCommand(ctx.Settings.Current, id);
                if (command == null) return;
                editingId = id;
                nameBox.Text = command.Name;
                templateBox.Text = command.Template;
                save.Content = "Save changes";
                SettingsCards.SetAccessibleName(save, "Save changes to the custom command");
                cancel.Visibility = Visibility.Visible;
                refresh();
                nameBox.BringIntoView();
                nameBox.Focus();
            };

            _confirmDeleteCustomCommand = (id, name) =>
            {
                slot.Children.Clear();
                slot.Children.Add(ctx.Cards.ConfirmRow(
                    "Delete the custom command \"" + name + "\"?",
                    "Delete",
                    onConfirm: () =>
                    {
                        DeleteCustomCommand(ctx.Settings.Current, id);
                        ctx.Settings.Save();
                        slot.Children.Clear();
                        if (editingId == id) clear();
                        Populate(rows, ctx, null, 0);
                    },
                    onCancel: () => slot.Children.Clear()));
                slot.BringIntoView();
            };

            refresh();

            var nameLabel = new TextBlock { Text = "Name" };
            var templateLabel = new TextBlock { Text = "Command" };
            var body = new StackPanel();
            body.Children.Add(help);
            body.Children.Add(nameLabel);
            body.Children.Add(nameBox);
            body.Children.Add(templateLabel);
            body.Children.Add(templateBox);
            body.Children.Add(status);
            body.Children.Add(buttons);
            body.Children.Add(slot);

            var shell = ctx.Cards.CardShell(body, hoverable: false);
            ctx.Cards.Register(CustomCommandsCardId, nameBox);
            return shell;
        }

        internal static CustomCommand FindCustomCommand(AppSettings settings, string id)
        {
            return settings.CustomCommands != null
                ? settings.CustomCommands.FirstOrDefault(c => string.Equals(c.Id, id, System.StringComparison.Ordinal))
                : null;
        }

        /// <summary>Adds a command, switched on and at the end of the order, and returns its new id.</summary>
        internal static string AddCustomCommand(AppSettings settings, string name, string template)
        {
            string id = CustomCommandTemplate.IdPrefix + System.Guid.NewGuid().ToString("N");
            if (settings.CustomCommands == null) settings.CustomCommands = new List<CustomCommand>();
            settings.CustomCommands.Add(new CustomCommand { Id = id, Name = name.Trim(), Template = template.Trim() });
            if (settings.QuickActions == null) settings.QuickActions = new List<QuickActionSetting>();
            settings.QuickActions.Add(new QuickActionSetting { Id = id, Enabled = true });
            return id;
        }

        internal static bool UpdateCustomCommand(AppSettings settings, string id, string name, string template)
        {
            var command = FindCustomCommand(settings, id);
            if (command == null) return false;
            command.Name = name.Trim();
            command.Template = template.Trim();
            return true;
        }

        internal static bool DeleteCustomCommand(AppSettings settings, string id)
        {
            var command = FindCustomCommand(settings, id);
            if (command == null) return false;
            settings.CustomCommands.Remove(command);
            ActionList(settings).RemoveAll(a => string.Equals(a.Id, id, System.StringComparison.Ordinal));
            return true;
        }

        // ---- reset to default (#178) ----

        /// <summary>The Reset to default button and its inline confirmation slot, under the action
        /// rows. It lives in the page panel, not <paramref name="rows"/>, so an in-place Populate does
        /// not wipe an open confirmation.</summary>
        private FrameworkElement BuildResetRow(StackPanel rows, SettingsPageContext ctx)
        {
            var host = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var slot = new StackPanel();
            var reset = new Button { Content = "Reset to default", HorizontalAlignment = HorizontalAlignment.Left };
            SettingsCards.SetAccessibleName(reset, "Reset quick actions to default");
            reset.Click += (s, e) =>
            {
                slot.Children.Clear();
                slot.Children.Add(ctx.Cards.ConfirmRow(
                    "Restore the default order and on/off state of every built-in quick action? Custom commands are kept, after them. The Show Quick actions switch is not changed.",
                    "Reset",
                    onConfirm: () =>
                    {
                        ctx.Settings.Current.QuickActions = ResetActions(ctx.Settings.Current);
                        ctx.Settings.Save();
                        slot.Children.Clear();
                        Populate(rows, ctx, null, 0);
                    },
                    onCancel: () => slot.Children.Clear()));
            };
            host.Children.Add(reset);
            host.Children.Add(slot);
            return host;
        }

        /// <summary>The list Reset to default produces: every built-in action in
        /// <see cref="QuickActionLibrary.All"/>'s own order and its own default state -- the same source
        /// and the same defaults <c>AppSettings.Normalize</c> uses to seed a missing action, so an action
        /// added in a later version resets correctly. Takes no input: an action has no user-edited field
        /// (its name lives in the library) to carry over.</summary>
        internal static List<QuickActionSetting> ResetActions()
        {
            return QuickActionLibrary.All
                .Select(d => new QuickActionSetting { Id = d.Id, Enabled = d.DefaultEnabled })
                .ToList();
        }

        /// <summary><see cref="ResetActions()"/> for a real settings object: reset is about what QuickPane
        /// shipped, so the user's own commands stay, in their order and state, after the built-ins.</summary>
        internal static List<QuickActionSetting> ResetActions(AppSettings settings)
        {
            var list = ResetActions();
            list.AddRange(ActionList(settings).Where(a => CustomCommandTemplate.IsCustomId(a.Id)));
            return list;
        }

        /// <summary>The icon a row shows here: the same one its row shows in the pane, so the two are
        /// easy to match up. A card reserves the icon's width whether or not it has a glyph, so an
        /// empty glyph left a blank gap before every label (#202). <paramref name="def"/> is null for
        /// an action this version does not know, which still gets an icon so its row lines up.</summary>
        internal static string RowGlyph(QuickActionDefinition def)
        {
            return QuickActionsSection.Glyph(def != null ? def.Kind : QuickActionKind.CopyPath);
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
            var rowWrappers = new List<FrameworkElement>();   // one per row, read by each grip while dragging (#178)

            for (int i = 0; i < list.Count; i++)
            {
                var setting = list[i];
                var def = QuickActionLibrary.Resolve(ctx.Settings.Current, setting.Id);
                string name = def != null ? def.Name : setting.Id;
                string description = def != null ? def.Description : "";
                int index = i;
                string id = setting.Id;   // captured for the handlers below instead of setting/list/index:
                                           // see FindActionById/MoveActionById for why.

                var toggle = cards.Toggle(setting.Enabled, on =>
                {
                    var current = FindActionById(ctx.Settings.Current, id);
                    if (current != null) current.Enabled = on;
                    ctx.Settings.Save();
                    Populate(panel, ctx, id, 0);
                }, "Show " + name + " in the pane");

                var up = cards.IconButton("\uE70E", "Move " + name + " up", () =>
                {
                    if (MoveActionById(ctx.Settings.Current, id, -1))
                    {
                        ctx.Settings.Save();
                        Populate(panel, ctx, id, -1);
                    }
                });
                var down = cards.IconButton("\uE70D", "Move " + name + " down", () =>
                {
                    if (MoveActionById(ctx.Settings.Current, id, 1))
                    {
                        ctx.Settings.Save();
                        Populate(panel, ctx, id, 1);
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
                if (def != null && def.Kind == QuickActionKind.Custom)
                {
                    controls.Children.Add(cards.IconButton("\uE70F", "Edit " + name, () => _editCustomCommand(id)));
                    controls.Children.Add(cards.IconButton("\uE74D", "Delete " + name, () => _confirmDeleteCustomCommand(id, name)));
                }

                var card = cards.Card("action-" + setting.Id, RowGlyph(def), name, description, controls);

                // Drag grip at the row's left edge (#178); see SectionsPage.Populate. Moves by id, not
                // by captured index, for the staleness reason on FindActionById.
                var grip = ReorderDrag.CreateGrip("Drag to reorder " + name, rowWrappers, panel, index, (fromIndex, toIndex) =>
                {
                    if (MoveActionByIdTo(ctx.Settings.Current, id, toIndex))
                    {
                        ctx.Settings.Save();
                        Populate(panel, ctx, id, 0);   // 0 lands focus on the moved row's toggle
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

        /// <summary>Finds the action with <paramref name="id"/> in <paramref name="settings"/>'s
        /// CURRENT QuickActions list, re-read here rather than trusting any list a caller captured at
        /// an earlier Populate(). SettingsStore.Save() always runs AppSettings.Normalize, which always
        /// reassigns QuickActions to a brand-new List instance -- even when nothing in it changed --
        /// so a button built before some OTHER save (the master switch next to it, say) closed over a
        /// list that is no longer the one settings.json round-trips from. Reading the id back out
        /// fresh here, rather than the list itself, is what keeps a Move or a toggle from being lost
        /// to a save that already happened by the time the click arrives.</summary>
        internal static QuickActionSetting FindActionById(AppSettings settings, string id)
        {
            return ActionList(settings).FirstOrDefault(a => string.Equals(a.Id, id, System.StringComparison.Ordinal));
        }

        /// <summary>Same staleness concern as <see cref="FindActionById"/>, for a Move: finds
        /// <paramref name="id"/>'s CURRENT index in <paramref name="settings"/>'s live list and moves
        /// it there, rather than moving whatever list and index a button's closure captured when it
        /// was built.</summary>
        internal static bool MoveActionById(AppSettings settings, string id, int direction)
        {
            var list = ActionList(settings);
            int index = list.FindIndex(a => string.Equals(a.Id, id, System.StringComparison.Ordinal));
            return index >= 0 && MoveAction(list, index, direction);
        }

        /// <summary>A drag-drop's version of <see cref="MoveActionById"/>: moves the action with
        /// <paramref name="id"/> to index <paramref name="to"/> of the live list (#178).</summary>
        internal static bool MoveActionByIdTo(AppSettings settings, string id, int to)
        {
            var list = ActionList(settings);
            int index = list.FindIndex(a => string.Equals(a.Id, id, System.StringComparison.Ordinal));
            return index >= 0 && MoveActionTo(list, index, to);
        }

        /// <summary>Moves <paramref name="list"/>[<paramref name="index"/>] one slot in
        /// <paramref name="direction"/> (-1 for Move up, +1 for Move down). Returns false, changing
        /// nothing, when <paramref name="index"/> or its neighbour is out of range -- the first row
        /// has no "up" and the last has no "down".</summary>
        internal static bool MoveAction(List<QuickActionSetting> list, int index, int direction)
        {
            return MoveActionTo(list, index, index + direction);
        }

        /// <summary>The one place an action changes position, shared by Move up/down (to = from +/- 1)
        /// and a drag-drop (any to, #178): removes <paramref name="list"/>[<paramref name="fromIndex"/>]
        /// and inserts it at <paramref name="toIndex"/>. Returns false, changing nothing, when either
        /// index is out of range or they are equal.</summary>
        internal static bool MoveActionTo(List<QuickActionSetting> list, int fromIndex, int toIndex)
        {
            if (list == null) return false;
            if (fromIndex < 0 || fromIndex >= list.Count) return false;
            if (toIndex < 0 || toIndex >= list.Count) return false;
            if (fromIndex == toIndex) return false;

            var moved = list[fromIndex];
            list.RemoveAt(fromIndex);
            list.Insert(toIndex, moved);
            return true;
        }
    }
}
