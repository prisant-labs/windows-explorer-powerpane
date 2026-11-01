using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuickPane.Models;
using QuickPane.Services;
using QuickPane.UI.Settings.Pages;

namespace QuickPane.Tests
{
    /// <summary>Covers AppSettings.Normalize's handling of QuickActions (quick actions, first version):
    /// unknown ids dropped, duplicates collapsed to their first occurrence, every missing built-in
    /// appended enabled at the end, and a null list seeded from scratch -- plus the master switch's
    /// default and QuickActionsPage.MoveAction, the Move up/down logic Settings92SectionsPageTests
    /// already covers for SectionsPage's own list.</summary>
    [TestFixture]
    public class QuickActionsSettingsTests
    {
        // ---- the master switch: off by default, a real section like every other ----------------

        [Test]
        public void CreateDefault_QuickActionsSection_IsOffByDefault()
        {
            var s = AppSettings.CreateDefault();
            var section = s.Sections.Single(x => x.Type == "quickactions");
            Assert.That(section.Visible, Is.False);
        }

        [Test]
        public void Normalize_FreshSettings_AddsTheQuickActionsSectionStillOff()
        {
            var s = new AppSettings { Sections = new List<SectionSetting>() };
            s.Normalize();
            var section = s.Sections.Single(x => x.Type == "quickactions");
            Assert.That(section.Visible, Is.False);
        }

        [Test]
        public void Normalize_ExistingQuickActionsSection_KeepsItsStoredVisibility()
        {
            var s = AppSettings.CreateDefault();
            s.Sections.Single(x => x.Type == "quickactions").Visible = true;
            s.Normalize();
            Assert.That(s.Sections.Single(x => x.Type == "quickactions").Visible, Is.True);
        }

        // ---- NormalizeQuickActions, through AppSettings.Normalize ------------------------------

        [Test]
        public void Normalize_NullQuickActions_SeedsEveryBuiltInEnabledInLibraryOrder()
        {
            var s = AppSettings.CreateDefault();
            s.QuickActions = null;
            s.Normalize();

            Assert.That(s.QuickActions.Select(a => a.Id),
                Is.EqualTo(QuickActionLibrary.All.Select(d => d.Id)));
            Assert.That(s.QuickActions.All(a => a.Enabled), Is.True);
        }

        [Test]
        public void Normalize_UnknownId_IsDropped()
        {
            var s = AppSettings.CreateDefault();
            s.QuickActions = new List<QuickActionSetting> { new QuickActionSetting { Id = "not-a-real-action", Enabled = true } };
            s.Normalize();

            Assert.That(s.QuickActions.Any(a => a.Id == "not-a-real-action"), Is.False);
        }

        [Test]
        public void Normalize_DuplicateId_KeepsOnlyTheFirstOccurrence()
        {
            var s = AppSettings.CreateDefault();
            s.QuickActions = new List<QuickActionSetting>
            {
                new QuickActionSetting { Id = "copy-path", Enabled = false },
                new QuickActionSetting { Id = "copy-path", Enabled = true },
            };
            s.Normalize();

            var copies = s.QuickActions.Where(a => a.Id == "copy-path").ToList();
            Assert.That(copies, Has.Count.EqualTo(1));
            Assert.That(copies[0].Enabled, Is.False); // the first occurrence's own value survives
        }

        [Test]
        public void Normalize_MissingBuiltIn_IsAppendedEnabledAtTheEnd()
        {
            var s = AppSettings.CreateDefault();
            s.QuickActions = new List<QuickActionSetting> { new QuickActionSetting { Id = "copy-path", Enabled = false } };
            s.Normalize();

            Assert.That(s.QuickActions[0].Id, Is.EqualTo("copy-path"));
            Assert.That(s.QuickActions[0].Enabled, Is.False); // the stored entry's own value is kept
            Assert.That(s.QuickActions.Select(a => a.Id), Is.EqualTo(QuickActionLibrary.All.Select(d => d.Id)));
            Assert.That(s.QuickActions.Skip(1).All(a => a.Enabled), Is.True); // every appended one defaults on
        }

        [Test]
        public void Normalize_PreservesStoredOrderOfKnownActions()
        {
            var s = AppSettings.CreateDefault();
            s.QuickActions = new List<QuickActionSetting>
            {
                new QuickActionSetting { Id = "open-terminal", Enabled = true },
                new QuickActionSetting { Id = "copy-path", Enabled = true },
            };
            s.Normalize();

            int terminalIndex = s.QuickActions.FindIndex(a => a.Id == "open-terminal");
            int copyIndex = s.QuickActions.FindIndex(a => a.Id == "copy-path");
            Assert.That(terminalIndex, Is.LessThan(copyIndex));
        }

        [Test]
        public void Normalize_IsIdempotent()
        {
            var s = AppSettings.CreateDefault();
            s.Normalize();
            var firstPass = s.QuickActions.Select(a => a.Id).ToList();
            s.Normalize();
            Assert.That(s.QuickActions.Select(a => a.Id).ToList(), Is.EqualTo(firstPass));
        }

        // ---- QuickActionsPage.MoveAction (the Settings page's Move up/down) --------------------

        private static List<QuickActionSetting> Actions(params string[] ids)
        {
            return ids.Select(id => new QuickActionSetting { Id = id, Enabled = true }).ToList();
        }

        [Test]
        public void MoveAction_Up_SwapsWithPreviousNeighbour()
        {
            var list = Actions("copy-path", "new-text-file", "open-terminal");
            Assert.That(QuickActionsPage.MoveAction(list, 1, -1), Is.True);
            Assert.That(list.Select(a => a.Id), Is.EqualTo(new[] { "new-text-file", "copy-path", "open-terminal" }));
        }

        [Test]
        public void MoveAction_Down_SwapsWithNextNeighbour()
        {
            var list = Actions("copy-path", "new-text-file", "open-terminal");
            Assert.That(QuickActionsPage.MoveAction(list, 0, 1), Is.True);
            Assert.That(list.Select(a => a.Id), Is.EqualTo(new[] { "new-text-file", "copy-path", "open-terminal" }));
        }

        [Test]
        public void MoveAction_FirstRowUp_DoesNothing()
        {
            var list = Actions("copy-path", "new-text-file");
            Assert.That(QuickActionsPage.MoveAction(list, 0, -1), Is.False);
            Assert.That(list.Select(a => a.Id), Is.EqualTo(new[] { "copy-path", "new-text-file" }));
        }

        [Test]
        public void MoveAction_LastRowDown_DoesNothing()
        {
            var list = Actions("copy-path", "new-text-file");
            Assert.That(QuickActionsPage.MoveAction(list, 1, 1), Is.False);
        }

        [Test]
        public void MoveAction_OutOfRangeIndex_ReturnsFalse()
        {
            var list = Actions("copy-path", "new-text-file");
            Assert.That(QuickActionsPage.MoveAction(list, -1, 1), Is.False);
            Assert.That(QuickActionsPage.MoveAction(list, 5, -1), Is.False);
            Assert.That(QuickActionsPage.MoveAction(null, 0, 1), Is.False);
        }

        [Test]
        public void ActionList_NullQuickActions_ReturnsEmpty()
        {
            var s = new AppSettings { QuickActions = null };
            Assert.That(QuickActionsPage.ActionList(s), Is.Empty);
        }

        // ---- FindActionById / MoveActionById: survive a Normalize() that swapped the list ------
        // (reproduces the real bug: AppSettings.Normalize, which every SettingsStore.Save() call
        // runs -- including the master switch's own save -- always reassigns QuickActions to a new
        // List instance, even when nothing in it changed. A handler built from an earlier Populate()
        // that closed over that old list, rather than re-reading the id, would silently move or
        // toggle nothing once that other save had already happened.)

        [Test]
        public void FindActionById_AfterNormalizeReplacedTheList_StillFindsTheCurrentObject()
        {
            var s = AppSettings.CreateDefault();
            s.Normalize();
            var before = s.QuickActions;

            s.Normalize(); // simulates an unrelated Save() elsewhere, e.g. the master switch
            Assert.That(s.QuickActions, Is.Not.SameAs(before)); // sanity: confirms the premise

            var found = QuickActionsPage.FindActionById(s, "open-terminal");
            Assert.That(found, Is.Not.Null);
            Assert.That(found, Is.SameAs(s.QuickActions.First(a => a.Id == "open-terminal")));
        }

        [Test]
        public void FindActionById_UnknownId_ReturnsNull()
        {
            var s = AppSettings.CreateDefault();
            s.Normalize();
            Assert.That(QuickActionsPage.FindActionById(s, "not-a-real-action"), Is.Null);
        }

        [Test]
        public void MoveActionById_AfterNormalizeReplacedTheList_StillMovesTheCurrentList()
        {
            var s = AppSettings.CreateDefault();
            s.Normalize();
            var before = s.QuickActions;

            s.Normalize(); // simulates an unrelated Save() elsewhere, e.g. the master switch
            Assert.That(s.QuickActions, Is.Not.SameAs(before)); // sanity: confirms the premise

            int originalIndex = s.QuickActions.FindIndex(a => a.Id == "open-terminal");
            bool moved = QuickActionsPage.MoveActionById(s, "open-terminal", -1);

            Assert.That(moved, Is.True);
            Assert.That(s.QuickActions.FindIndex(a => a.Id == "open-terminal"), Is.EqualTo(originalIndex - 1));
        }

        [Test]
        public void MoveActionById_UnknownId_ReturnsFalseAndChangesNothing()
        {
            var s = AppSettings.CreateDefault();
            s.Normalize();
            var idsBefore = s.QuickActions.Select(a => a.Id).ToList();

            Assert.That(QuickActionsPage.MoveActionById(s, "not-a-real-action", -1), Is.False);
            Assert.That(s.QuickActions.Select(a => a.Id), Is.EqualTo(idsBefore));
        }

        // ---- round-trips through settings.json, same shape as PaneColorTests ------------------

        [Test]
        public void SaveThenLoad_RoundTripsQuickActionsAndTheMasterSwitch()
        {
            using (var temp = new TempDir())
            {
                var store = new SettingsStore(temp.Path);
                store.Load();
                store.Current.Sections.Single(x => x.Type == "quickactions").Visible = true;
                store.Current.QuickActions[0].Enabled = false;
                QuickActionsPage.MoveAction(store.Current.QuickActions, 1, -1);
                store.Save();

                var reloaded = new SettingsStore(temp.Path);
                reloaded.Load();

                Assert.That(reloaded.Current.Sections.Single(x => x.Type == "quickactions").Visible, Is.True);
                Assert.That(reloaded.Current.QuickActions.Select(a => a.Id),
                    Is.EqualTo(store.Current.QuickActions.Select(a => a.Id)));
                Assert.That(reloaded.Current.QuickActions.First(a => a.Id == store.Current.QuickActions[0].Id).Enabled,
                    Is.EqualTo(store.Current.QuickActions[0].Enabled));
            }
        }
    }
}
