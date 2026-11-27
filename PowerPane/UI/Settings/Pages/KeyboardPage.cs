using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PowerPane.Services;

namespace PowerPane.UI.Settings.Pages
{
    // Carries over every control from SettingsPanel.xaml.cs's "Hotkeys" tab, at commit 068301b.
    // The existing hotkey validation tests still pass unchanged.

    /// <summary>A toggle for "Let a keyboard shortcut move focus into the pane", and a Shortcut
    /// card with a text box that validates as today and reverts to the last valid value.
    ///
    /// Both controls write straight through <see cref="SettingsPageContext.Settings"/> and call
    /// <c>Save()</c>, exactly like the old checkbox and text box at commit 068301b
    /// (<c>SettingsPanel.xaml.cs</c>'s <c>HotkeysBlock</c>) and like every other settings.json
    /// writer in the app (<c>SidebarControl.xaml.cs</c>, <c>AppBarHost.cs</c>,
    /// <c>RecentsSection.xaml.cs</c>). <c>Save()</c> serializes on this thread and queues the file
    /// write on the settings lane itself (#163), so no caller posts it.</summary>
    internal sealed class KeyboardPage : SettingsPage
    {
        private const string ToggleCardId = "keyboard-enabled";
        private const string ShortcutCardId = "keyboard-shortcut";

        public override string Id => "keyboard";
        // The page id stays "keyboard", so a search link or a saved page from before the click
        // settings (#269) still lands here.
        public override string Title => "Keyboard and mouse";
        public override string Glyph => "\uE765";   // Segoe Fluent Icons KeyboardClassic

        public override IEnumerable<SettingsSearchEntry> SearchEntries
        {
            get
            {
                yield return new SettingsSearchEntry(Id, ToggleCardId, "Let a keyboard shortcut move focus into the pane",
                    "Turn keyboard navigation of the pane on or off.", "hotkey", "shortcut", "focus", "keyboard navigation");
                yield return new SettingsSearchEntry(Id, ShortcutCardId, "Shortcut",
                    "The key combination that moves focus into the pane.", "hotkey", "shortcut", "ctrl+shift");
                yield return new SettingsSearchEntry(Id, SwitcherToggleCardId, "Let a keyboard shortcut open the quick switcher",
                    "Search pins, Recent, drives, and actions from the keyboard.", "quick switcher", "search", "jump", "go to", "palette", "hotkey");
                yield return new SettingsSearchEntry(Id, SwitcherShortcutCardId, "Quick switcher shortcut",
                    "The key combination that opens the quick switcher.", "hotkey", "shortcut", "ctrl+alt");
                yield return new SettingsSearchEntry(Id, PinToggleCardId, "Let keyboard shortcuts open pinned folders",
                    "A prefix plus a number from 1 to 9 opens a pinned folder.", "pin shortcut", "hotkey", "number", "favourite", "ctrl+alt");
                yield return new SettingsSearchEntry(Id, PinPrefixCardId, "Pin shortcut prefix",
                    "The modifiers in front of a pin shortcut's number.", "hotkey", "shortcut", "ctrl+alt", "altgr");
                yield return new SettingsSearchEntry(Id, PinListCardId, "Assigned pin shortcuts",
                    "The pinned folders that have a shortcut in this profile.", "pin shortcut", "hotkey", "remove");
                yield return new SettingsSearchEntry(Id, FolderClicksCardId, "Clicking a folder",
                    "Choose what each click on a folder row does.", "mouse", "click", "middle click", "new window", "copy path", "ctrl", "shift");
                yield return new SettingsSearchEntry(Id, FileClicksCardId, "Clicking a file",
                    "Choose what each click on a file row does.", "mouse", "click", "middle click", "containing folder", "copy path", "ctrl", "shift");
            }
        }

        public override UIElement Build(SettingsPageContext ctx)
        {
            var settings = ctx.Settings;
            var s = settings.Current;

            var panel = new StackPanel();
            panel.Children.Add(ctx.Cards.Subheader("Keyboard navigation"));

            var toggle = ctx.Cards.Toggle(s.KeyboardNav, enabled =>
            {
                s.KeyboardNav = enabled;
                settings.Save();
            }, "Let a keyboard shortcut move focus into the pane");

            panel.Children.Add(ctx.Cards.Card(ToggleCardId, Glyph, "Let a keyboard shortcut move focus into the pane",
                "Once focus is in the pane, the arrow keys move between folders and Enter opens one.", toggle,
                "Let a keyboard shortcut move focus into the pane"));

            var box = new TextBox { Text = s.KeyboardNavHotkey, MinWidth = 160 };
            box.LostFocus += (sender, e) =>
            {
                bool accepted;
                string result = ValidateHotkeyText(box.Text, s.KeyboardNavHotkey, out accepted);
                box.Text = result;
                if (accepted)
                {
                    s.KeyboardNavHotkey = result;
                    settings.Save();
                }
            };

            panel.Children.Add(ctx.Cards.Card(ShortcutCardId, Glyph, "Shortcut",
                "Modifiers joined with +, ending in a key, e.g. ctrl+shift+Q. At least one modifier is required.",
                box, "Keyboard navigation shortcut"));

            BuildQuickSwitcher(ctx, panel);
            BuildPinShortcuts(ctx, panel);
            BuildClicks(ctx, panel);
            return panel;
        }

        private const string FolderClicksCardId = "clicks-folder";
        private const string FileClicksCardId = "clicks-file";
        private const string MouseGlyph = "\uE962";   // Segoe Fluent Icons Mouse

        /// <summary>One card for folder rows and one for file rows, each with a box per click gesture
        /// (#269). Each box writes straight through and saves, like the toggles above, and the pane
        /// reads the choice at click time, so nothing needs rebuilding.</summary>
        private static void BuildClicks(SettingsPageContext ctx, StackPanel panel)
        {
            panel.Children.Add(ctx.Cards.Subheader("Mouse"));
            panel.Children.Add(BuildClickCard(ctx, false, FolderClicksCardId, "Clicking a folder",
                "What each click on a folder in the pane does. A quick action is listed once it applies to a folder, and runs only while it and the Quick actions section are on. In an Open or Save dialog, a new window opens the folder in the dialog instead.").Root);
            panel.Children.Add(BuildClickCard(ctx, true, FileClicksCardId, "Clicking a file",
                "What each click on a pinned or recent file in the pane does.").Root);
        }

        private static ExpanderCard BuildClickCard(SettingsPageContext ctx, bool forFile, string cardId, string title, string description)
        {
            var settings = ctx.Settings;
            var s = settings.Current;
            var card = ctx.Cards.Expander(cardId, MouseGlyph, title, description, initiallyExpanded: false);
            foreach (var gesture in ClickActions.Gestures)
            {
                var g = gesture;
                var combo = ctx.Cards.Combo(ClickActions.Options(s, g, forFile), ClickActions.Stored(s, g, forFile), id =>
                {
                    ClickActions.Set(s, g, forFile, id);
                    settings.Save();
                });
                card.AddChild(ctx.Cards.ChildRow(ClickActions.GestureLabel(g), combo,
                    ClickActions.GestureLabel(g) + (forFile ? " on a file" : " on a folder")));
            }
            return card;
        }

        private const string SwitcherToggleCardId = "switcher-enabled";
        private const string SwitcherShortcutCardId = "switcher-shortcut";
        private const string SwitcherGlyph = "\uE721";   // Segoe Fluent Icons Search

        /// <summary>The quick switcher's toggle and shortcut (#226), validated the same way as the
        /// keyboard navigation shortcut above.</summary>
        private static void BuildQuickSwitcher(SettingsPageContext ctx, StackPanel panel)
        {
            var settings = ctx.Settings;
            var s = settings.Current;
            panel.Children.Add(ctx.Cards.Subheader("Quick switcher"));

            var toggle = ctx.Cards.Toggle(s.QuickSwitcher, enabled =>
            {
                s.QuickSwitcher = enabled;
                settings.Save();
            }, "Let a keyboard shortcut open the quick switcher");
            panel.Children.Add(ctx.Cards.Card(SwitcherToggleCardId, SwitcherGlyph, "Let a keyboard shortcut open the quick switcher",
                "Type part of a pin, a Recent folder, a drive, or an action, then press Enter. The Explorer window or file dialog in front goes there, and Escape closes the box.",
                toggle, "Let a keyboard shortcut open the quick switcher"));

            var box = new TextBox { Text = s.QuickSwitcherHotkey, MinWidth = 160 };
            box.LostFocus += (sender, e) =>
            {
                bool accepted;
                string result = ValidateHotkeyText(box.Text, s.QuickSwitcherHotkey, out accepted);
                box.Text = result;
                if (accepted)
                {
                    s.QuickSwitcherHotkey = result;
                    settings.Save();
                }
            };
            panel.Children.Add(ctx.Cards.Card(SwitcherShortcutCardId, SwitcherGlyph, "Quick switcher shortcut",
                "Modifiers joined with +, ending in a key, e.g. ctrl+alt+Space. Pick a combination other programs do not use, because PowerPane takes it from all of them.",
                box, "Quick switcher shortcut"));
        }

        private const string PinToggleCardId = "pin-shortcuts-enabled";
        private const string PinPrefixCardId = "pin-shortcuts-prefix";
        private const string PinListCardId = "pin-shortcuts-list";
        private const string PinGlyph = "\uE718";   // Segoe MDL2 Pin

        /// <summary>Pin shortcuts (#299): the switch, the prefix, and the active profile's assigned
        /// numbers. Each number is given to a folder from its pin's right-click menu. A number whose
        /// folder is no longer pinned stays listed, so it can be removed by hand, and a number Windows
        /// refused says so.</summary>
        private static void BuildPinShortcuts(SettingsPageContext ctx, StackPanel panel)
        {
            var settings = ctx.Settings;
            var s = settings.Current;
            panel.Children.Add(ctx.Cards.Subheader("Pin shortcuts"));

            var toggle = ctx.Cards.Toggle(s.PinHotkeys, enabled =>
            {
                s.PinHotkeys = enabled;
                settings.Save();
            }, "Let keyboard shortcuts open pinned folders");
            panel.Children.Add(ctx.Cards.Card(PinToggleCardId, PinGlyph, "Let keyboard shortcuts open pinned folders",
                "Give a folder pin a number from 1 to 9 with Assign shortcut in its right-click menu. The prefix and the number then send the Explorer window or file dialog in front to that folder, or open a new window when neither is in front.",
                toggle, "Let keyboard shortcuts open pinned folders"));

            var box = new TextBox { Text = s.PinHotkeyModifiers, MinWidth = 160 };
            box.LostFocus += (sender, e) =>
            {
                bool accepted;
                string result = ValidatePinPrefixText(box.Text, s.PinHotkeyModifiers, out accepted);
                box.Text = result;
                if (accepted && result != s.PinHotkeyModifiers)
                {
                    s.PinHotkeyModifiers = result;
                    settings.Save();
                }
            };
            panel.Children.Add(ctx.Cards.Card(PinPrefixCardId, PinGlyph, "Pin shortcut prefix",
                "Modifiers joined with +, such as ctrl+alt; the number is added after them. On a keyboard where the right Alt key types extra characters, Ctrl+Alt and a number can be one of those characters, and PowerPane takes each assigned combination from every program. Choose another prefix, such as ctrl+shift, if you type them.",
                box, "Pin shortcut prefix"));

            var profile = s.ActiveProfile;
            string profileName = profile == null ? "this profile" : profile.Name;
            var list = ctx.Cards.Expander(PinListCardId, PinGlyph, "Assigned pin shortcuts",
                "The pinned folders with a shortcut in " + profileName + ". Each profile has its own.",
                accessibleName: "Assigned pin shortcuts");
            var entries = profile == null ? null : profile.PinHotkeys;
            var targets = PinHotkeyModel.FolderPinTargets(App.Groups == null ? null : App.Groups.Groups);
            var orphans = PinHotkeyModel.Orphans(entries, targets);
            var refused = App.PinHotkeysRefused ?? new List<int>();
            var shown = (entries ?? new List<Models.PinHotkeyEntry>()).Where(x => x != null).OrderBy(x => x.Slot).ToList();
            if (shown.Count == 0)
                list.AddChild(ctx.Cards.ChildRow("No pinned folder has a shortcut yet.", new TextBlock()));
            foreach (var entry in shown)
            {
                int slot = entry.Slot;
                string label = PinHotkeyModel.SlotLabel(s.PinHotkeyModifiers, slot) + ": " + GroupSection.PinHotkeyLabel(entry.Target);
                string note = PinShortcutNote(orphans.Any(o => o.Slot == slot), s.PinHotkeys && refused.Contains(slot));
                if (note != null) label += " (" + note + ")";
                // ChildRow names its control, so the button's own name goes in again there; passing the
                // row's label would make a screen reader announce the Remove button as the folder.
                string removeName = "Remove pin shortcut " + PinHotkeyModel.SlotLabel(s.PinHotkeyModifiers, slot);
                var remove = ctx.Cards.IconButton("\uE74D", removeName, () =>
                {
                    if (profile == null || !ReferenceEquals(settings.Current.ActiveProfile, profile)) return;
                    profile.PinHotkeys = PinHotkeyModel.Unassign(profile.PinHotkeys, slot);
                    settings.Save();
                    ctx.Rebuild();
                });
                list.AddChild(ctx.Cards.ChildRow(label, remove, removeName));
            }
            panel.Children.Add(list.Root);

            // The keys are registered after the save that changed them, so the refusals can arrive
            // after this page is built. Rebuild once they differ from what the page shows.
            string seen = string.Join(",", refused);
            Action onApplied = null;
            onApplied = () =>
            {
                if (!ctx.IsCurrent) { App.PinHotkeysApplied -= onApplied; return; }
                if (string.Join(",", App.PinHotkeysRefused ?? new List<int>()) == seen) return;
                App.PinHotkeysApplied -= onApplied;
                ctx.Rebuild();
            };
            App.PinHotkeysApplied += onApplied;
        }

        /// <summary>The note beside an assigned pin shortcut, or null when it works: the folder is no
        /// longer pinned, or Windows refused the key because another program holds it (#299).</summary>
        internal static string PinShortcutNote(bool orphaned, bool refused)
        {
            if (orphaned) return "no longer pinned";
            if (refused) return "Windows refused this key, because another program holds it";
            return null;
        }

        /// <summary>A pin shortcut prefix is kept when it is modifiers alone, stored in the order
        /// ctrl, alt, shift, win; anything else reverts to <paramref name="lastValid"/> (#299).</summary>
        internal static string ValidatePinPrefixText(string input, string lastValid, out bool accepted)
        {
            var v = PinHotkeyModel.ValidateModifiers(input);
            accepted = v != null;
            return v ?? lastValid;
        }

        /// <summary>The same decision as the old text box's <c>LostFocus</c> handler
        /// (<c>SettingsPanel.xaml.cs</c> at 068301b): a value <see cref="HotkeyService.TryParse"/>
        /// accepts is trimmed and kept; anything else reverts to <paramref name="lastValid"/> rather
        /// than being stored. A pure decision with no UI, so it is unit-testable on its own.</summary>
        internal static string ValidateHotkeyText(string input, string lastValid, out bool accepted)
        {
            uint mods, vk;
            if (HotkeyService.TryParse(input, out mods, out vk))
            {
                accepted = true;
                return input.Trim();
            }

            accepted = false;
            return lastValid;
        }
    }
}
