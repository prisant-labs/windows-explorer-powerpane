using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using QuickPane.Services;

namespace QuickPane.UI.Settings.Pages
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
        public override string Title => "Keyboard";
        public override string Glyph => "\uE765";   // Segoe Fluent Icons KeyboardClassic

        public override IEnumerable<SettingsSearchEntry> SearchEntries
        {
            get
            {
                yield return new SettingsSearchEntry(Id, ToggleCardId, "Let a keyboard shortcut move focus into the pane",
                    "Turn keyboard navigation of the pane on or off.", "hotkey", "shortcut", "focus", "keyboard navigation");
                yield return new SettingsSearchEntry(Id, ShortcutCardId, "Shortcut",
                    "The key combination that moves focus into the pane.", "hotkey", "shortcut", "ctrl+shift");
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

            return panel;
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
