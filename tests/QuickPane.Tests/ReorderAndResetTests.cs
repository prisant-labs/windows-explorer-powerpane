using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuickPane.Models;
using QuickPane.Services;
using QuickPane.UI.Settings;
using QuickPane.UI.Settings.Pages;

namespace QuickPane.Tests
{
    /// <summary>Drag-to-reorder and Reset to default (#178). A drag itself cannot be tested (tests never
    /// show a window), so these cover the statics a drop and a reset call: the shared
    /// "move from index i to index j" on both pages, the reset decisions, and ReorderDrag's pure
    /// slot arithmetic.</summary>
    [TestFixture]
    public class ReorderAndResetTests
    {
        private static List<SectionSetting> Sections(params string[] types)
        {
            var list = new List<SectionSetting>();
            for (int i = 0; i < types.Length; i++) list.Add(new SectionSetting { Type = types[i], Order = i, Visible = true });
            return list;
        }

        private static List<QuickActionSetting> Actions(params string[] ids)
        {
            return ids.Select(id => new QuickActionSetting { Id = id, Enabled = true }).ToList();
        }

        // ---- MoveSectionTo -----------------------------------------------------------------

        [Test]
        public void MoveSectionTo_FirstToLast()
        {
            var list = Sections("a", "b", "c", "d");
            Assert.That(SectionsPage.MoveSectionTo(list, 0, 3), Is.True);
            Assert.That(list.Select(s => s.Type), Is.EqualTo(new[] { "b", "c", "d", "a" }));
            Assert.That(list.Select(s => s.Order), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        }

        [Test]
        public void MoveSectionTo_LastToFirst()
        {
            var list = Sections("a", "b", "c", "d");
            Assert.That(SectionsPage.MoveSectionTo(list, 3, 0), Is.True);
            Assert.That(list.Select(s => s.Type), Is.EqualTo(new[] { "d", "a", "b", "c" }));
            Assert.That(list.Select(s => s.Order), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        }

        [Test]
        public void MoveSectionTo_MiddleMoves()
        {
            var list = Sections("a", "b", "c", "d", "e");
            Assert.That(SectionsPage.MoveSectionTo(list, 1, 3), Is.True);
            Assert.That(list.Select(s => s.Type), Is.EqualTo(new[] { "a", "c", "d", "b", "e" }));
            Assert.That(SectionsPage.MoveSectionTo(list, 3, 1), Is.True);
            Assert.That(list.Select(s => s.Type), Is.EqualTo(new[] { "a", "b", "c", "d", "e" }));
        }

        [Test]
        public void MoveSectionTo_RenumbersTiedOrders()
        {
            var list = Sections("a", "b", "c");
            list[1].Order = 0;   // a tie, as Normalize's EnsureSection can leave
            Assert.That(SectionsPage.MoveSectionTo(list, 0, 2), Is.True);
            Assert.That(list.Select(s => s.Order), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void MoveSectionTo_SameIndex_IsNoOp()
        {
            var list = Sections("a", "b", "c");
            Assert.That(SectionsPage.MoveSectionTo(list, 1, 1), Is.False);
            Assert.That(list.Select(s => s.Type), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [TestCase(-1, 1)]
        [TestCase(3, 1)]
        [TestCase(1, -1)]
        [TestCase(1, 3)]
        public void MoveSectionTo_OutOfRange_ChangesNothing(int from, int to)
        {
            var list = Sections("a", "b", "c");
            Assert.That(SectionsPage.MoveSectionTo(list, from, to), Is.False);
            Assert.That(list.Select(s => s.Type), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void MoveSectionTo_NullList_ReturnsFalse()
        {
            Assert.That(SectionsPage.MoveSectionTo(null, 0, 1), Is.False);
        }

        [Test]
        public void MoveSection_ButtonPath_StillGoesThroughSharedMove()
        {
            var list = Sections("a", "b", "c");
            Assert.That(SectionsPage.MoveSection(list, 0, -1), Is.False);
            Assert.That(SectionsPage.MoveSection(list, 2, 1), Is.False);
            Assert.That(SectionsPage.MoveSection(list, 0, 1), Is.True);
            Assert.That(list.Select(s => s.Type), Is.EqualTo(new[] { "b", "a", "c" }));
        }

        // ---- MoveActionTo / MoveActionByIdTo -----------------------------------------------

        [Test]
        public void MoveActionTo_FirstToLast()
        {
            var list = Actions("a", "b", "c", "d");
            Assert.That(QuickActionsPage.MoveActionTo(list, 0, 3), Is.True);
            Assert.That(list.Select(x => x.Id), Is.EqualTo(new[] { "b", "c", "d", "a" }));
        }

        [Test]
        public void MoveActionTo_LastToFirst()
        {
            var list = Actions("a", "b", "c", "d");
            Assert.That(QuickActionsPage.MoveActionTo(list, 3, 0), Is.True);
            Assert.That(list.Select(x => x.Id), Is.EqualTo(new[] { "d", "a", "b", "c" }));
        }

        [Test]
        public void MoveActionTo_MiddleMoves()
        {
            var list = Actions("a", "b", "c", "d", "e");
            Assert.That(QuickActionsPage.MoveActionTo(list, 1, 3), Is.True);
            Assert.That(list.Select(x => x.Id), Is.EqualTo(new[] { "a", "c", "d", "b", "e" }));
            Assert.That(QuickActionsPage.MoveActionTo(list, 3, 1), Is.True);
            Assert.That(list.Select(x => x.Id), Is.EqualTo(new[] { "a", "b", "c", "d", "e" }));
        }

        [Test]
        public void MoveActionTo_SameIndex_IsNoOp()
        {
            var list = Actions("a", "b", "c");
            Assert.That(QuickActionsPage.MoveActionTo(list, 2, 2), Is.False);
            Assert.That(list.Select(x => x.Id), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [TestCase(-1, 1)]
        [TestCase(3, 1)]
        [TestCase(1, -1)]
        [TestCase(1, 3)]
        public void MoveActionTo_OutOfRange_ChangesNothing(int from, int to)
        {
            var list = Actions("a", "b", "c");
            Assert.That(QuickActionsPage.MoveActionTo(list, from, to), Is.False);
            Assert.That(list.Select(x => x.Id), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void MoveAction_ButtonPath_StillGoesThroughSharedMove()
        {
            var list = Actions("a", "b", "c");
            Assert.That(QuickActionsPage.MoveAction(list, 0, -1), Is.False);
            Assert.That(QuickActionsPage.MoveAction(list, 0, 1), Is.True);
            Assert.That(list.Select(x => x.Id), Is.EqualTo(new[] { "b", "a", "c" }));
        }

        [Test]
        public void MoveActionByIdTo_UsesTheLiveIndex()
        {
            var settings = new AppSettings { QuickActions = Actions("a", "b", "c") };
            Assert.That(QuickActionsPage.MoveActionByIdTo(settings, "c", 0), Is.True);
            Assert.That(settings.QuickActions.Select(x => x.Id), Is.EqualTo(new[] { "c", "a", "b" }));
            Assert.That(QuickActionsPage.MoveActionByIdTo(settings, "missing", 0), Is.False);
        }

        // ---- ResetSections -----------------------------------------------------------------

        [Test]
        public void ResetSections_RestoresDefaultOrderAndVisibility()
        {
            var settings = AppSettings.CreateDefault();
            var ordered = SectionsPage.OrderedSections(settings);
            SectionsPage.MoveSectionTo(ordered, 0, ordered.Count - 1);
            foreach (var s in settings.Sections) s.Visible = !s.Visible;

            var reset = SectionsPage.ResetSections(settings.Sections);

            var defaults = AppSettings.CreateDefault().Sections.OrderBy(x => x.Order).ToList();
            Assert.That(reset.Select(x => x.Type), Is.EqualTo(defaults.Select(x => x.Type)));
            Assert.That(reset.Select(x => x.Order), Is.EqualTo(defaults.Select(x => x.Order)));
            Assert.That(reset.Select(x => x.Visible), Is.EqualTo(defaults.Select(x => x.Visible)));
        }

        [Test]
        public void ResetSections_KeepsRenamedTitle()
        {
            var current = AppSettings.CreateDefault().Sections;
            current.Single(x => x.Type == "recents").Title = "Lately";
            var reset = SectionsPage.ResetSections(current);
            Assert.That(reset.Single(x => x.Type == "recents").Title, Is.EqualTo("Lately"));
            Assert.That(reset.Single(x => x.Type == "groups").Title, Is.Null);
        }

        [Test]
        public void ResetSections_AddsSectionMissingFromCurrent()
        {
            var current = new List<SectionSetting> { new SectionSetting { Type = "groups", Order = 5, Visible = false } };
            var reset = SectionsPage.ResetSections(current);
            Assert.That(reset.Count, Is.EqualTo(AppSettings.CreateDefault().Sections.Count));
            Assert.That(reset.Single(x => x.Type == "groups").Order, Is.EqualTo(0));
            Assert.That(reset.Single(x => x.Type == "groups").Visible, Is.True);
        }

        [Test]
        public void ResetSections_NullCurrent_GivesDefaults()
        {
            Assert.That(SectionsPage.ResetSections(null).Count, Is.EqualTo(AppSettings.CreateDefault().Sections.Count));
        }

        [Test]
        public void ResetSections_UnknownTypeIsKeptAfterTheDefaults()
        {
            var current = AppSettings.CreateDefault().Sections;
            current.Add(new SectionSetting { Type = "future", Order = -9, Visible = false });
            var reset = SectionsPage.ResetSections(current);
            Assert.That(reset.Last().Type, Is.EqualTo("future"));
            Assert.That(reset.Last().Visible, Is.False);
            Assert.That(reset.Select(x => x.Order).Distinct().Count(), Is.EqualTo(reset.Count));
        }

        [Test]
        public void ResetSections_QuickActionsSectionGoesBackOff()
        {
            var current = AppSettings.CreateDefault().Sections;
            current.Single(x => x.Type == "quickactions").Visible = true;
            var reset = SectionsPage.ResetSections(current);
            Assert.That(reset.Single(x => x.Type == "quickactions").Visible, Is.False);
        }

        // ---- ResetActions ------------------------------------------------------------------

        [Test]
        public void ResetActions_IsLibraryOrderInDefaultState()
        {
            var reset = QuickActionsPage.ResetActions();
            Assert.That(reset.Select(x => x.Id), Is.EqualTo(QuickActionLibrary.All.Select(d => d.Id)));
            Assert.That(reset.Select(x => x.Enabled), Is.EqualTo(QuickActionLibrary.All.Select(d => d.DefaultEnabled)));
        }

        [Test]
        public void ResetActions_MatchesWhatNormalizeSeeds()
        {
            var seeded = new AppSettings { QuickActions = null, Sections = new List<SectionSetting>() };
            seeded.Normalize();
            Assert.That(QuickActionsPage.ResetActions().Select(x => x.Id), Is.EqualTo(seeded.QuickActions.Select(x => x.Id)));
        }

        // ---- ReorderDrag arithmetic --------------------------------------------------------

        [Test]
        public void SlotFor_PicksTheSlotBeforeTheFirstRowBelowThePointer()
        {
            var mids = new List<double> { 10, 30, 50 };
            Assert.That(ReorderDrag.SlotFor(mids, 0), Is.EqualTo(0));
            Assert.That(ReorderDrag.SlotFor(mids, 20), Is.EqualTo(1));
            Assert.That(ReorderDrag.SlotFor(mids, 40), Is.EqualTo(2));
            Assert.That(ReorderDrag.SlotFor(mids, 999), Is.EqualTo(3));
        }

        [Test]
        public void TargetIndex_DownwardSlotsShiftByOne_UpwardSlotsDoNot()
        {
            Assert.That(ReorderDrag.TargetIndex(4, 1), Is.EqualTo(3));   // before row 4 after removing row 1
            Assert.That(ReorderDrag.TargetIndex(1, 3), Is.EqualTo(1));
            Assert.That(ReorderDrag.TargetIndex(1, 1), Is.EqualTo(1));   // slots just above and below a row
            Assert.That(ReorderDrag.TargetIndex(2, 1), Is.EqualTo(1));   // both mean "stay"
        }
    }
}
