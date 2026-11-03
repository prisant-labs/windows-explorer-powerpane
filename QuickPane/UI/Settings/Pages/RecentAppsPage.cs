using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using QuickPane.Models;

namespace QuickPane.UI.Settings.Pages
{
    // Carries over the Recent Apps block from SettingsPanel.xaml.cs's "Pane" tab, at commit
    // 068301b, as its own page instead of sharing Pane's crowded left column.

    /// <summary>A single card with a combo box for the Recent Apps source, and the explanatory note
    /// the old column showed beside the radios, folded into the card's description. Reads
    /// and writes <see cref="AppSettings.RecentAppsSource"/> with the same values ("both", "open",
    /// "dialogs") and the same immediate <c>SettingsStore.Save()</c> as the old
    /// <c>RecentAppsBlock</c>/<c>SourceRadio</c> at commit 068301b.</summary>
    internal sealed class RecentAppsPage : SettingsPage
    {
        public override string Id => "recent";
        public override string Title => "Recent apps";
        public override string Glyph => "\uE81C";   // Segoe Fluent Icons History

        public override IEnumerable<SettingsSearchEntry> SearchEntries
        {
            get { yield return new SettingsSearchEntry(Id, "recentAppsSource", Title, "Which apps' recent folders the pane shows.", "recent", "history", "jump list"); }
        }

        public override UIElement Build(SettingsPageContext ctx)
        {
            var s = ctx.Settings.Current;
            var cards = ctx.Cards;

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

            var panel = new StackPanel();
            panel.Children.Add(cards.Card("recentAppsSource", "\uE81C", "Which apps to list",
                "A row opens its app. The folder that app's dialog was last sent to is on the row's right-click menu.",
                combo, "Recent apps source"));
            return panel;
        }
    }
}
