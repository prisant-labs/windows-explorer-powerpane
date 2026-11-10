using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using QuickPane.Explorer;
using QuickPane.Models;
using QuickPane.UI;

namespace QuickPane.Tests
{
    /// <summary>
    /// Covers which pins QuickPane lists in its own pickers, and the rule that keeps the pane off
    /// QuickPane's own dialogs. Existence checks are injected, so nothing here touches the disk.
    /// </summary>
    [TestFixture]
    public class PickerPlacesTests
    {
        private static PinnedFolder Pin(string path, bool exists = true, bool isFile = false)
        {
            return new PinnedFolder { DisplayName = path, TargetPath = path, Exists = exists, IsFile = isFile };
        }

        private static PinnedGroup Group(bool expanded, int activeTab, params PinnedTab[] tabs)
        {
            return new PinnedGroup { Name = "Group", Expanded = expanded, ActiveTab = activeTab, Tabs = new List<PinnedTab>(tabs) };
        }

        private static PinnedTab Tab(params PinnedFolder[] pins)
        {
            return new PinnedTab { Name = "Tab", Items = new List<PinnedFolder>(pins) };
        }

        [Test]
        public void Candidates_FollowPaneOrder_AcrossGroups()
        {
            var groups = new List<PinnedGroup>
            {
                Group(true, 0, Tab(Pin(@"C:\Demo\B"), Pin(@"C:\Demo\A"))),
                Group(true, 0, Tab(Pin(@"D:\Work"))),
            };
            Assert.That(PickerPlaces.Candidates(groups, 50),
                Is.EqualTo(new[] { @"C:\Demo\B", @"C:\Demo\A", @"D:\Work" }));
        }

        [Test]
        public void Candidates_UseOnlyTheVisibleTab()
        {
            var groups = new List<PinnedGroup>
            {
                Group(true, 1, Tab(Pin(@"C:\Demo\Hidden")), Tab(Pin(@"C:\Demo\Shown"))),
            };
            Assert.That(PickerPlaces.Candidates(groups, 50), Is.EqualTo(new[] { @"C:\Demo\Shown" }));
        }

        [Test]
        public void Candidates_SkipCollapsedGroups()
        {
            var groups = new List<PinnedGroup>
            {
                Group(false, 0, Tab(Pin(@"C:\Demo\Collapsed"))),
                Group(true, 0, Tab(Pin(@"C:\Demo\Open"))),
            };
            Assert.That(PickerPlaces.Candidates(groups, 50), Is.EqualTo(new[] { @"C:\Demo\Open" }));
        }

        [Test]
        public void Candidates_SkipBlankPaths()
        {
            var groups = new List<PinnedGroup>
            {
                Group(true, 0, Tab(Pin("   "), Pin(null), null, Pin(@"C:\Demo\Kept"))),
            };
            Assert.That(PickerPlaces.Candidates(groups, 50), Is.EqualTo(new[] { @"C:\Demo\Kept" }));
        }

        [Test]
        public void Candidates_IgnoreScanTimeSnapshots_TheFolderCheckDecides()
        {
            // Before its first probe, a pin's IsFile is a guess from the extension, so a folder named
            // like a file reads as a file. The snapshot must not drop it; Reachable's check decides.
            var groups = new List<PinnedGroup>
            {
                Group(true, 0, Tab(
                    Pin(@"C:\Demo\Site.v2", isFile: true),
                    Pin(@"C:\Demo\Back", exists: false))),
            };
            var candidates = PickerPlaces.Candidates(groups, 50);
            Assert.That(candidates, Is.EqualTo(new[] { @"C:\Demo\Site.v2", @"C:\Demo\Back" }));
            Assert.That(PickerPlaces.Reachable(candidates, p => true, 2000, 50), Is.EqualTo(candidates));
        }

        [Test]
        public void Candidates_ListEachFolderOnce_FirstPlaceWins()
        {
            var groups = new List<PinnedGroup>
            {
                Group(true, 0, Tab(Pin(@"C:\Demo\Same"), Pin(@"C:\Demo\Other"))),
                Group(true, 0, Tab(Pin(@"c:\demo\SAME\"))),
            };
            Assert.That(PickerPlaces.Candidates(groups, 50), Is.EqualTo(new[] { @"C:\Demo\Same", @"C:\Demo\Other" }));
        }

        [Test]
        public void Candidates_StopAtTheCap()
        {
            var tab = Tab();
            for (int i = 0; i < 10; i++) tab.Items.Add(Pin(@"C:\Demo\F" + i));
            var result = PickerPlaces.Candidates(new List<PinnedGroup> { Group(true, 0, tab) }, 3);
            Assert.That(result, Is.EqualTo(new[] { @"C:\Demo\F0", @"C:\Demo\F1", @"C:\Demo\F2" }));
        }

        [Test]
        public void Candidates_NullOrEmptyInput_GivesNothing()
        {
            Assert.That(PickerPlaces.Candidates(null, 50), Is.Empty);
            Assert.That(PickerPlaces.Candidates(new List<PinnedGroup> { null, Group(true, 0) }, 50), Is.Empty);
            Assert.That(PickerPlaces.Candidates(new List<PinnedGroup> { Group(true, 0, Tab(Pin(@"C:\Demo\A"))) }, 0), Is.Empty);
        }

        [Test]
        public void Reachable_KeepsOrder_AndDropsFilesMissingAndThrowing()
        {
            // The real check is Directory.Exists, which is false for a file as well as for a folder
            // that is gone.
            var candidates = new List<string> { @"C:\Demo\A", @"C:\Demo\Missing", @"C:\Demo\notes.txt", @"C:\Demo\Throws", @"C:\Demo\B" };
            Func<string, bool> exists = p =>
            {
                if (p.EndsWith("Throws")) throw new UnauthorizedAccessException();
                return !p.EndsWith("Missing") && !p.EndsWith(".txt");
            };
            Assert.That(PickerPlaces.Reachable(candidates, exists, 2000, 50), Is.EqualTo(new[] { @"C:\Demo\A", @"C:\Demo\B" }));
        }

        [Test]
        public void Reachable_CapCountsOnlyFoldersThatAnswered()
        {
            // The cap applies after the check, so files and missing folders early in the list do not
            // use up places that later live folders should get.
            var candidates = new List<string> { @"C:\Demo\Gone1", @"C:\Demo\Gone2", @"C:\Demo\A", @"C:\Demo\B", @"C:\Demo\C" };
            var result = PickerPlaces.Reachable(candidates, p => !p.Contains("Gone"), 2000, 2);
            Assert.That(result, Is.EqualTo(new[] { @"C:\Demo\A", @"C:\Demo\B" }));
        }

        [Test]
        public void Reachable_SlowPinsCostOneBudget_NotOneEach()
        {
            // Three pins on shares that never answer, and one local pin. All are checked at once, so
            // the wait is one budget, the slow ones are left out, and the local one is kept.
            var release = new ManualResetEventSlim(false);
            try
            {
                var candidates = new List<string> { @"\\demo-nas\a", @"C:\Demo\Local", @"\\demo-nas\b", @"\\demo-nas\c" };
                Func<string, bool> exists = p =>
                {
                    if (p.StartsWith(@"\\")) { release.Wait(); return true; }
                    return true;
                };
                var sw = Stopwatch.StartNew();
                var result = PickerPlaces.Reachable(candidates, exists, 300, 50);
                sw.Stop();

                Assert.That(result, Is.EqualTo(new[] { @"C:\Demo\Local" }));
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1500), "three slow pins must not cost three budgets");
            }
            finally { release.Set(); }
        }

        [Test]
        public void Reachable_EmptyInput_GivesNothing()
        {
            Assert.That(PickerPlaces.Reachable(null, p => true, 100, 50), Is.Empty);
            Assert.That(PickerPlaces.Reachable(new List<string>(), p => true, 100, 50), Is.Empty);
        }

        [TestCase(1234u, 1234u, true)]
        [TestCase(1234u, 5678u, false)]
        [TestCase(0u, 0u, false)]
        [TestCase(0u, 1234u, false)]
        public void IsOwnProcess_OnlyThisProcessesLiveWindows(uint windowPid, uint ownPid, bool expected)
        {
            Assert.That(DialogPaneWatcher.IsOwnProcess(windowPid, ownPid), Is.EqualTo(expected));
        }
    }
}
