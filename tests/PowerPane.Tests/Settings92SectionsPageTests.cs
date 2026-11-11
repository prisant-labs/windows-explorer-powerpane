using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.UI.Settings.Pages;

namespace PowerPane.Tests
{
    /// <summary>Exercises <see cref="SectionsPage.OrderedSections"/> and
    /// <see cref="SectionsPage.MoveSection"/> with plain <see cref="AppSettings"/>/<see cref="SectionSetting"/>
    /// objects and no window, exactly as #92's Move up/Move down buttons use them.</summary>
    [TestFixture]
    public class Settings92SectionsPageTests
    {
        private static AppSettings Settings(params SectionSetting[] sections)
        {
            return new AppSettings { Sections = new List<SectionSetting>(sections) };
        }

        private static SectionSetting Section(string type, int order)
        {
            return new SectionSetting { Type = type, Order = order, Visible = true };
        }

        [Test]
        public void OrderedSections_SortsByOrder_NotListPosition()
        {
            var settings = Settings(Section("computer", 2), Section("groups", 0), Section("recents", 1));
            var ordered = SectionsPage.OrderedSections(settings);
            Assert.That(ordered.ConvertAll(s => s.Type), Is.EqualTo(new[] { "groups", "recents", "computer" }));
        }

        [Test]
        public void OrderedSections_NullSections_ReturnsEmpty()
        {
            var settings = new AppSettings { Sections = null };
            Assert.That(SectionsPage.OrderedSections(settings), Is.Empty);
        }

        [Test]
        public void MoveSection_Up_SwapsWithPreviousNeighbourAndRenumbers()
        {
            var settings = Settings(Section("groups", 0), Section("recents", 1), Section("computer", 2));
            var ordered = SectionsPage.OrderedSections(settings);

            bool moved = SectionsPage.MoveSection(ordered, 1, -1);

            Assert.That(moved, Is.True);
            Assert.That(ordered.ConvertAll(s => s.Type), Is.EqualTo(new[] { "recents", "groups", "computer" }));
            Assert.That(ordered.ConvertAll(s => s.Order), Is.EqualTo(new[] { 0, 1, 2 }));
            // The underlying Order fields moved, not just the local list -- re-sorting the real
            // settings object must agree with the local list the buttons just redrew from.
            Assert.That(SectionsPage.OrderedSections(settings).ConvertAll(s => s.Type),
                Is.EqualTo(new[] { "recents", "groups", "computer" }));
        }

        [Test]
        public void MoveSection_Down_SwapsWithNextNeighbourAndRenumbers()
        {
            var settings = Settings(Section("groups", 0), Section("recents", 1), Section("computer", 2));
            var ordered = SectionsPage.OrderedSections(settings);

            bool moved = SectionsPage.MoveSection(ordered, 0, 1);

            Assert.That(moved, Is.True);
            Assert.That(ordered.ConvertAll(s => s.Type), Is.EqualTo(new[] { "recents", "groups", "computer" }));
            Assert.That(ordered.ConvertAll(s => s.Order), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void MoveSection_FirstRowUp_DoesNothing()
        {
            var settings = Settings(Section("groups", 0), Section("recents", 1));
            var ordered = SectionsPage.OrderedSections(settings);

            bool moved = SectionsPage.MoveSection(ordered, 0, -1);

            Assert.That(moved, Is.False);
            Assert.That(ordered.ConvertAll(s => s.Type), Is.EqualTo(new[] { "groups", "recents" }));
            Assert.That(ordered.ConvertAll(s => s.Order), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void MoveSection_LastRowDown_DoesNothing()
        {
            var settings = Settings(Section("groups", 0), Section("recents", 1));
            var ordered = SectionsPage.OrderedSections(settings);

            bool moved = SectionsPage.MoveSection(ordered, 1, 1);

            Assert.That(moved, Is.False);
            Assert.That(ordered.ConvertAll(s => s.Type), Is.EqualTo(new[] { "groups", "recents" }));
        }

        [Test]
        public void MoveSection_RenumbersNonContiguousOrderValues()
        {
            // Real settings.json can have negative/non-contiguous Order values (search = -2,
            // appdefaults = -1): a move must renumber every row to a clean 0..n-1, matching the
            // old drag handler's OnSectionsDrop (commit 068301b, line 646), not just swap the two
            // raw numbers.
            var settings = Settings(Section("search", -2), Section("appdefaults", -1), Section("groups", 0));
            var ordered = SectionsPage.OrderedSections(settings);

            Assert.That(SectionsPage.MoveSection(ordered, 0, 1), Is.True);
            Assert.That(ordered.ConvertAll(s => s.Type), Is.EqualTo(new[] { "appdefaults", "search", "groups" }));
            Assert.That(ordered.ConvertAll(s => s.Order), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void MoveSection_TiedOrderValues_StillMoves()
        {
            // Normalize()'s EnsureSection can leave two sections sharing one Order (e.g. a
            // settings.json saved before a newer section type existed). A plain swap of the two
            // raw Order values would report success while nothing visibly moved when the swapped
            // pair ties with a third row; renumbering every row guarantees the new order is distinct
            // and matches what Move up/down just did on screen.
            var settings = Settings(Section("groups", 0), Section("recents", 0), Section("computer", 1));
            var ordered = SectionsPage.OrderedSections(settings);

            Assert.That(SectionsPage.MoveSection(ordered, 0, 1), Is.True);
            Assert.That(ordered.ConvertAll(s => s.Type), Is.EqualTo(new[] { "recents", "groups", "computer" }));
            Assert.That(ordered.ConvertAll(s => s.Order), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void MoveSection_OutOfRangeIndex_ReturnsFalse()
        {
            var settings = Settings(Section("groups", 0), Section("recents", 1));
            var ordered = SectionsPage.OrderedSections(settings);

            Assert.That(SectionsPage.MoveSection(ordered, -1, 1), Is.False);
            Assert.That(SectionsPage.MoveSection(ordered, 5, -1), Is.False);
            Assert.That(SectionsPage.MoveSection(null, 0, 1), Is.False);
        }

        // #207: one shared icon said nothing about the section and read like the drag grip.
        [Test]
        public void SectionGlyph_EveryDefaultSectionHasItsOwnIcon()
        {
            var types = AppSettings.CreateDefault().Sections.Select(s => s.Type).ToList();
            var glyphs = types.Select(SectionsPage.SectionGlyph).ToList();

            Assert.That(types, Has.Count.GreaterThan(1));
            Assert.That(glyphs, Is.Unique);
            Assert.That(glyphs, Has.None.EqualTo(SectionsPage.SectionGlyph("from-a-newer-version")));
        }

        [Test]
        public void SectionGlyph_MatchesThePageOfASectionThatHasOne()
        {
            Assert.That(SectionsPage.SectionGlyph("ssh"), Is.EqualTo(new SshPage().Glyph));
            Assert.That(SectionsPage.SectionGlyph("recentapps"), Is.EqualTo(new RecentAppsPage().Glyph));
            Assert.That(SectionsPage.SectionGlyph("quickactions"), Is.EqualTo(new QuickActionsPage().Glyph));
        }
    }
}
