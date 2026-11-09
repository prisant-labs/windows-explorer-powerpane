using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using QuickPane.Models;
using QuickPane.Services;

namespace QuickPane.UI.Settings.Pages
{
    // Carries over the Recent Apps block from SettingsPanel.xaml.cs's "Pane" tab, at commit
    // 068301b, as its own page instead of sharing Pane's crowded left column. The page took the
    // Recent section's privacy controls as well in #224, and its title widened to "Recent" to match.

    /// <summary>The Recent page. "Recent folders" holds the folders Recent never shows, and Clear
    /// Recent. "Recent apps" is a single card with a combo box for the Recent Apps source, which reads
    /// and writes <see cref="AppSettings.RecentAppsSource"/> with the same values ("both", "open",
    /// "dialogs") and the same immediate <c>SettingsStore.Save()</c> as the old
    /// <c>RecentAppsBlock</c>/<c>SourceRadio</c> at commit 068301b.</summary>
    internal sealed class RecentAppsPage : SettingsPage
    {
        public override string Id => "recent";
        public override string Title => "Recent";
        public override string Glyph => "\uE81C";   // Segoe Fluent Icons History

        private const string GlyphHide = "\uED1A";    // Segoe Fluent Icons Hide
        private const string GlyphDelete = "\uE74D";  // Segoe Fluent Icons Delete
        private const string GlyphClear = "\uE894";   // Segoe Fluent Icons Clear

        public override IEnumerable<SettingsSearchEntry> SearchEntries
        {
            get
            {
                yield return new SettingsSearchEntry(Id, "recentHidden", "Folders Recent never shows",
                    "Folders, and everything inside them, that Recent never shows or records.", "hide", "private", "exclude", "privacy", "never show");
                yield return new SettingsSearchEntry(Id, "recentClear", "Clear Recent",
                    "Empty the Recent section.", "clear", "history", "privacy", "forget");
                yield return new SettingsSearchEntry(Id, "recentAppsSource", "Recent apps", "Which apps' recent folders the pane shows.", "recent", "history", "jump list");
            }
        }

        public override UIElement Build(SettingsPageContext ctx)
        {
            var s = ctx.Settings.Current;
            var cards = ctx.Cards;
            var panel = new StackPanel();

            panel.Children.Add(cards.Subheader("Recent folders"));
            panel.Children.Add(BuildHiddenCard(ctx));
            panel.Children.Add(BuildClearCard(ctx));

            string current = (s.RecentAppsSource ?? "both").ToLowerInvariant();
            var combo = cards.Combo(
                new[]
                {
                    new KeyValuePair<string, string>("both", "Apps I open and apps I save from"),
                    new KeyValuePair<string, string>("open", "Only apps I open"),
                    new KeyValuePair<string, string>("dialogs", "Only apps whose Save and Open dialogs I use"),
                },
                current,
                v => { s.RecentAppsSource = v; ctx.Settings.Save(); });

            panel.Children.Add(cards.Subheader("Recent apps"));
            panel.Children.Add(cards.Card("recentAppsSource", "\uE81C", "Which apps to list",
                "A row opens its app. The folder that app's dialog was last sent to is on the row's right-click menu.",
                combo, "Recent apps source"));
            return panel;
        }

        /// <summary>The hidden-folders list: one row per folder with a remove button, then Add folder.
        /// Rows are rebuilt in place after each change, so focus stays on this page.</summary>
        private static UIElement BuildHiddenCard(SettingsPageContext ctx)
        {
            var cards = ctx.Cards;
            var expander = cards.Expander("recentHidden", GlyphHide, "Folders Recent never shows",
                "Recent neither shows nor records these folders, or anything inside them. A Recent row's right-click menu adds one too.",
                initiallyExpanded: true);

            var rows = new StackPanel();
            expander.AddChild(rows);

            Action refresh = null;
            refresh = () =>
            {
                rows.Children.Clear();
                var hidden = ctx.Settings.Current.RecentHiddenFolders;
                if (hidden == null || hidden.Count == 0)
                {
                    rows.Children.Add(cards.ChildRow("No folders are hidden.", new Border()));
                    return;
                }
                foreach (var folder in hidden)
                {
                    string f = folder;
                    var remove = cards.IconButton(GlyphDelete, "Show " + f + " in Recent again", () =>
                    {
                        App.Recents?.UnhideFolder(f);
                        refresh();
                    });
                    rows.Children.Add(cards.ChildRow(f, remove, "Show " + f + " in Recent again"));
                }
            };
            refresh();

            var add = new Button { Content = "Add folder…" };
            System.Windows.Automation.AutomationProperties.SetName(add, "Add a folder Recent never shows");
            add.Click += (sender, e) =>
            {
                var picked = FolderPicker.Pick("Pick a folder Recent never shows");
                if (string.IsNullOrEmpty(picked)) return;
                App.Recents?.HideFolder(picked);
                refresh();
            };
            expander.AddChild(cards.TrailingChildRow(add));
            return expander.Root;
        }

        private static UIElement BuildClearCard(SettingsPageContext ctx)
        {
            var cards = ctx.Cards;
            var clear = new Button { Content = "Clear" };
            var card = cards.Card("recentClear", GlyphClear, "Clear Recent",
                "Empties the Recent section. Windows' own Recent list and Quick Access keep their items, and a folder you open again shows again.",
                clear, "Clear Recent");
            clear.Click += (sender, e) =>
            {
                App.Recents?.ClearRecent();
                clear.Content = "Cleared";
                clear.IsEnabled = false;
            };
            return card;
        }
    }
}
