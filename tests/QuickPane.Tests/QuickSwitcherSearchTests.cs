using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuickPane.Models;
using QuickPane.Services;
using QuickPane.UI;

namespace QuickPane.Tests
{
    /// <summary>
    /// Covers the quick switcher's search and placement (#226). The search runs on every keystroke on
    /// the UI thread, so it works only on entries already in memory; these tests use invented ones.
    /// </summary>
    [TestFixture]
    public class QuickSwitcherSearchTests
    {
        private static SwitcherEntry Pin(string name, string path) { return new SwitcherEntry { Kind = SwitcherKind.Pin, Name = name, Path = path }; }
        private static SwitcherEntry Recent(string name, string path) { return new SwitcherEntry { Kind = SwitcherKind.Recent, Name = name, Path = path }; }
        private static SwitcherEntry Drive(string name, string path) { return new SwitcherEntry { Kind = SwitcherKind.Drive, Name = name, Path = path }; }
        private static SwitcherEntry Action(string name, string id) { return new SwitcherEntry { Kind = SwitcherKind.Action, Name = name, ActionId = id }; }

        private static List<string> Names(IEnumerable<SwitcherEntry> e) { return e.Select(x => x.Name).ToList(); }

        [Test]
        public void Combine_KeepsThePinWhenRecentHasTheSameFolder()
        {
            var all = QuickSwitcherSearch.Combine(
                new[] { Pin("Projects", @"C:\Demo\Projects") },
                new[] { Recent("Projects", @"c:\demo\projects\"), Recent("Docs", @"C:\Demo\Docs") },
                null, null);
            Assert.That(all.Select(e => e.Kind), Is.EqualTo(new[] { SwitcherKind.Pin, SwitcherKind.Recent }));
            Assert.That(Names(all), Is.EqualTo(new[] { "Projects", "Docs" }));
        }

        [Test]
        public void Combine_KeepsActionsWithNoPath()
        {
            var all = QuickSwitcherSearch.Combine(null, null, null, new[] { Action("Copy path", "copy-path"), Action("Open terminal", "open-terminal") });
            Assert.That(all.Count, Is.EqualTo(2));
        }

        [Test]
        public void Rank_EmptyQuery_ListsPinsThenRecentOnly()
        {
            var all = QuickSwitcherSearch.Combine(
                new[] { Pin("Projects", @"C:\Demo\Projects") },
                new[] { Recent("Docs", @"C:\Demo\Docs") },
                new[] { Drive("Local Disk (C:)", @"C:\") },
                new[] { Action("Copy path", "copy-path") });
            Assert.That(Names(QuickSwitcherSearch.Rank("", all)), Is.EqualTo(new[] { "Projects", "Docs" }));
            Assert.That(Names(QuickSwitcherSearch.Rank("   ", all)), Is.EqualTo(new[] { "Projects", "Docs" }));
        }

        [Test]
        public void Rank_NameStartBeatsWordStartBeatsInsideBeatsPath()
        {
            var all = new List<SwitcherEntry>
            {
                Recent("Old reports", @"C:\Demo\Old reports"),          // word start
                Recent("Quarterly", @"C:\Demo\Reports\Quarterly"),      // path only
                Recent("Subreports", @"C:\Demo\Subreports"),            // inside a word
                Recent("Reports", @"C:\Demo\Reports"),                  // name start
            };
            Assert.That(Names(QuickSwitcherSearch.Rank("rep", all)), Is.EqualTo(new[] { "Reports", "Old reports", "Subreports", "Quarterly" }));
        }

        [Test]
        public void Rank_CamelCaseBoundaryCountsAsAWordStart()
        {
            var all = new List<SwitcherEntry> { Recent("Subreports", @"C:\Demo\a"), Recent("MyReports", @"C:\Demo\b") };
            Assert.That(Names(QuickSwitcherSearch.Rank("rep", all)), Is.EqualTo(new[] { "MyReports", "Subreports" }));
        }

        [Test]
        public void Rank_EqualMatches_PinOutranksRecentOutranksDriveOutranksAction()
        {
            var all = new List<SwitcherEntry>
            {
                Action("Data tools", "a"),
                Drive("Data (D:)", @"D:\"),
                Recent("Data", @"C:\Demo\Data2"),
                Pin("Data", @"C:\Demo\Data"),
            };
            Assert.That(QuickSwitcherSearch.Rank("data", all).Select(e => e.Kind),
                Is.EqualTo(new[] { SwitcherKind.Pin, SwitcherKind.Recent, SwitcherKind.Drive, SwitcherKind.Action }));
        }

        [Test]
        public void Rank_EveryTermMustMatch_NameOrPath()
        {
            var all = new List<SwitcherEntry>
            {
                Recent("Projects", @"C:\Demo\Projects"),
                Recent("Projects", @"C:\Other\Projects"),
            };
            var result = QuickSwitcherSearch.Rank("dem proj", all);
            Assert.That(result.Select(e => e.Path), Is.EqualTo(new[] { @"C:\Demo\Projects" }));
        }

        [Test]
        public void Rank_IgnoresLetterCase()
        {
            var all = new List<SwitcherEntry> { Pin("Projects", @"C:\Demo\Projects") };
            Assert.That(QuickSwitcherSearch.Rank("PROJ", all).Count, Is.EqualTo(1));
        }

        [Test]
        public void Rank_NoMatch_IsEmpty()
        {
            var all = new List<SwitcherEntry> { Pin("Projects", @"C:\Demo\Projects") };
            Assert.That(QuickSwitcherSearch.Rank("zzz", all), Is.Empty);
        }

        [Test]
        public void Rank_CapsTheResults()
        {
            var all = Enumerable.Range(0, 50).Select(i => Recent("Folder " + i, @"C:\Demo\Folder " + i)).ToList();
            Assert.That(QuickSwitcherSearch.Rank("folder", all).Count, Is.EqualTo(QuickSwitcherSearch.MaxResults));
            Assert.That(QuickSwitcherSearch.Rank("folder", all, 5).Count, Is.EqualTo(5));
        }

        [Test]
        public void Rank_ManyEntries_StaysFast()
        {
            // 500 pins and 50 Recent folders, typed one letter at a time: each keystroke must leave no
            // visible delay on the UI thread.
            var all = Enumerable.Range(0, 500).Select(i => Pin("Pinned folder " + i, @"C:\Demo\Pins\Pinned folder " + i))
                .Concat(Enumerable.Range(0, 50).Select(i => Recent("Recent " + i, @"C:\Demo\Recent " + i))).ToList();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var q in new[] { "p", "pi", "pin", "pinn", "pinned", "pinned f", "pinned fo", "pinned folder 4" })
                QuickSwitcherSearch.Rank(q, all);
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(200), "eight keystrokes over 550 entries");
        }

        [Test]
        public void Placement_CentresAcrossTheWindowAFifthOfTheWayDown()
        {
            var p = QuickSwitcher.Placement(100, 200, 1000, 800, 520);
            Assert.That(p.X, Is.EqualTo(100 + (1000 - 520) / 2));
            Assert.That(p.Y, Is.EqualTo(200 + 800 / 5));
        }

        [Test]
        public void Placement_ABoxWiderThanTheWindow_AlignsToItsLeftEdge()
        {
            var p = QuickSwitcher.Placement(100, 200, 400, 300, 520);
            Assert.That(p.X, Is.EqualTo(100));
        }

        [Test]
        public void Settings_TheSwitcherStartsOff()
        {
            var s = new AppSettings();
            Assert.That(s.QuickSwitcher, Is.False);
            Assert.That(s.QuickSwitcherHotkey, Is.EqualTo(AppSettings.DefaultQuickSwitcherHotkey));
            uint mods, vk;
            Assert.That(HotkeyService.TryParse(s.QuickSwitcherHotkey, out mods, out vk), Is.True);
        }
    }
}
