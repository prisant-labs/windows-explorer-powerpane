using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuickPane.UI.Settings;

namespace QuickPane.Tests
{
    /// <summary>Exercises <see cref="SettingsSearchIndex"/> with hand-built entries, and separately
    /// against the real page list, with no SettingsStore, no GroupStore, and no window -- exactly
    /// how the search box itself can query the index before any page has ever been opened.</summary>
    [TestFixture]
    public class SettingsSearchIndexTests
    {
        private static SettingsSearchEntry Entry(string pageId, string cardId, string title, string description, params string[] keywords)
            => new SettingsSearchEntry(pageId, cardId, title, description, keywords);

        [Test]
        public void Search_EmptyQuery_ReturnsNothing()
        {
            var entries = new List<SettingsSearchEntry> { Entry("pane", null, "Pane mode", "Inside, beside, or off.") };
            Assert.That(SettingsSearchIndex.Search(entries, ""), Is.Empty);
            Assert.That(SettingsSearchIndex.Search(entries, "   "), Is.Empty);
        }

        [Test]
        public void Search_MatchesTitle_CaseInsensitive()
        {
            var entries = new List<SettingsSearchEntry> { Entry("pane", "pane-mode", "Pane mode", "Inside, beside, or off.") };
            var hits = SettingsSearchIndex.Search(entries, "PANE MODE");
            Assert.That(hits.Select(h => h.CardId), Is.EqualTo(new[] { "pane-mode" }));
        }

        [Test]
        public void Search_MatchesDescriptionAndKeyword()
        {
            var entries = new List<SettingsSearchEntry>
            {
                Entry("keyboard", "shortcut", "Shortcut", "The key combination that moves focus into the pane.", "hotkey"),
            };
            Assert.That(SettingsSearchIndex.Search(entries, "combination"), Has.Count.EqualTo(1));
            Assert.That(SettingsSearchIndex.Search(entries, "hotkey"), Has.Count.EqualTo(1));
            Assert.That(SettingsSearchIndex.Search(entries, "nothing matches this"), Is.Empty);
        }

        [Test]
        public void Search_RanksTitleHitsAboveKeywordHits()
        {
            var entries = new List<SettingsSearchEntry>
            {
                Entry("ssh", "keepalive", "Keepalive", "How often to ping the connection.", "update"),
                Entry("about", "updates", "Check for updates", "See the installed version."),
            };
            var hits = SettingsSearchIndex.Search(entries, "update");
            Assert.That(hits.Select(h => h.CardId), Is.EqualTo(new[] { "updates", "keepalive" }));
        }

        [Test]
        public void Build_FlattensEveryPagesEntries_InPageOrder()
        {
            var pages = new List<SettingsPage> { new FakePage("a", "updates", "update"), new FakePage("b", "x", "y") };
            var built = SettingsSearchIndex.Build(pages);
            Assert.That(built.Select(e => e.PageId), Is.EqualTo(new[] { "a", "b" }));
        }

        // ---- against the real page list (the shell's own nine pages) ----

        private static List<SettingsPage> RealPages() => SettingsWindow.BuildPages();

        [Test]
        public void RealPages_EachHaveANavigationIcon()
        {
            // An edit to the Pane page once emptied its icon strings, which left a blank space in
            // the navigation pane and on four cards.
            foreach (var page in RealPages())
                Assert.That(page.Glyph, Is.Not.Null.And.Not.Empty, page.Id);
        }

        [Test]
        public void RealPages_HaveNineUniqueIds_WithAboutLast()
        {
            // Nine since the Quick actions page (quick actions, first version) joined the eight pages
            // this test used to count.
            var pages = RealPages();
            Assert.That(pages, Has.Count.EqualTo(9));
            Assert.That(pages.Select(p => p.Id).Distinct().Count(), Is.EqualTo(9));
            Assert.That(pages[pages.Count - 1].Id, Is.EqualTo("about"));
        }

        [Test]
        public void RealPages_OpenOnPane_ThenPaneSections_ThenGroups()
        {
            // Settings opens on the first page. The order was chosen in place of a dashboard
            // landing page, so a reorder should be a decision, not an accident.
            var ids = RealPages().Select(p => p.Id).ToList();
            Assert.That(ids, Is.EqualTo(new[]
            {
                "pane", "sections", "groups", "quickactions", "recent", "keyboard", "ssh", "backup", "about",
            }));
        }

        [Test]
        public void RealPages_EverySearchEntry_NamesItsOwnPageId()
        {
            foreach (var page in RealPages())
            {
                foreach (var entry in page.SearchEntries)
                {
                    Assert.That(entry.PageId, Is.EqualTo(page.Id), "an entry from " + page.Id + " named a different page");
                }
            }
        }

        [TestCase("update")]
        [TestCase("version")]
        public void RealPages_SearchingUpdateOrVersion_FindsAbout(string query)
        {
            var index = SettingsSearchIndex.Build(RealPages());
            var hits = SettingsSearchIndex.Search(index, query);
            Assert.That(hits.Any(h => h.PageId == "about"), "expected '" + query + "' to find About");
        }

        private sealed class FakePage : SettingsPage
        {
            private readonly string _id;
            private readonly string _cardId;
            private readonly string _keyword;

            public FakePage(string id, string cardId, string keyword) { _id = id; _cardId = cardId; _keyword = keyword; }

            public override string Id => _id;
            public override string Title => _id;
            public override string Glyph => "\uE946";

            public override IEnumerable<SettingsSearchEntry> SearchEntries
            {
                get { yield return new SettingsSearchEntry(_id, _cardId, _id, _id, _keyword); }
            }

            public override System.Windows.UIElement Build(SettingsPageContext ctx) => null;
        }
    }
}
