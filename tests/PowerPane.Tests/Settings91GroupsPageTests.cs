using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PowerPane.Services;
using PowerPane.UI.Settings.Pages;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers #91's testable decisions (GroupsPage.Reordered, GroupsPage.MoveOrCopy) against
    /// GroupStore in a temporary directory. These two methods are the page's
    /// Move up/down and Move-to-profile logic with no FrameworkElement involved, so they are
    /// covered directly rather than through the UI the page builds around them. Deletion
    /// (GroupStore.Recycle) is not exercised here: it calls the real Windows Recycle Bin.
    /// </summary>
    [TestFixture]
    public class Settings91GroupsPageTests
    {
        private sealed class ThreeGroups : System.IDisposable
        {
            public readonly TempDir Temp = new TempDir();
            public readonly string RootA, RootB;
            public readonly string Alpha, Beta, Gamma;

            public ThreeGroups()
            {
                RootA = Path.Combine(Temp.Path, "ProfileA");
                RootB = Path.Combine(Temp.Path, "ProfileB");
                Alpha = Path.Combine(RootA, "01_Alpha");
                Beta = Path.Combine(RootA, "02_Beta");
                Gamma = Path.Combine(RootA, "03_Gamma");
                Directory.CreateDirectory(Path.Combine(Alpha, "01_Main"));
                Directory.CreateDirectory(Path.Combine(Beta, "01_Main"));
                Directory.CreateDirectory(Path.Combine(Gamma, "01_Main"));
                Directory.CreateDirectory(RootB);
            }

            public List<string> OrderedFolders()
            {
                return GroupStore.ListGroups(RootA).Select(g => g.Item1).ToList();
            }

            public void Dispose() { Temp.Dispose(); }
        }

        // ---- Reordered (Move up / Move down) ---------------------------------

        [Test]
        public void Reordered_SwapsWithTheNeighbourBelow()
        {
            var folders = new List<string> { @"C:\Demo\01_Alpha", @"C:\Demo\02_Beta", @"C:\Demo\03_Gamma" };
            var result = GroupsPage.Reordered(folders, 0, 1);
            Assert.That(result, Is.EqualTo(new[] { @"C:\Demo\02_Beta", @"C:\Demo\01_Alpha", @"C:\Demo\03_Gamma" }));
        }

        [Test]
        public void Reordered_SwapsWithTheNeighbourAbove()
        {
            var folders = new List<string> { @"C:\Demo\01_Alpha", @"C:\Demo\02_Beta", @"C:\Demo\03_Gamma" };
            var result = GroupsPage.Reordered(folders, 2, -1);
            Assert.That(result, Is.EqualTo(new[] { @"C:\Demo\01_Alpha", @"C:\Demo\03_Gamma", @"C:\Demo\02_Beta" }));
        }

        [Test]
        public void Reordered_AtTheTop_MoveUpReturnsNull()
        {
            var folders = new List<string> { @"C:\Demo\01_Alpha", @"C:\Demo\02_Beta" };
            Assert.That(GroupsPage.Reordered(folders, 0, -1), Is.Null);
        }

        [Test]
        public void Reordered_AtTheBottom_MoveDownReturnsNull()
        {
            var folders = new List<string> { @"C:\Demo\01_Alpha", @"C:\Demo\02_Beta" };
            Assert.That(GroupsPage.Reordered(folders, 1, 1), Is.Null);
        }

        [Test]
        public void Reordered_NullFolders_ReturnsNull()
        {
            Assert.That(GroupsPage.Reordered(null, 0, 1), Is.Null);
        }

        [Test]
        public void MoveUp_WrittenThroughReorderGroupFolders_MatchesDragReorder()
        {
            // The same disk result a drag-to-reorder would have produced: Beta moved above Alpha.
            using (var f = new ThreeGroups())
            {
                var before = f.OrderedFolders();
                var afterOrder = GroupsPage.Reordered(before, 1, -1); // move Beta up
                Assert.That(afterOrder, Is.Not.Null);

                GroupStore.ReorderGroupFolders(f.RootA, afterOrder);

                var names = GroupStore.ListGroups(f.RootA).Select(g => g.Item2).ToList();
                Assert.That(names, Is.EqualTo(new[] { "Beta", "Alpha", "Gamma" }));
            }
        }

        // ---- MoveOrCopy (Move to profile) -------------------------------------

        [Test]
        public void MoveOrCopy_SameRootPlainMove_IsANoOp()
        {
            using (var f = new ThreeGroups())
            {
                bool changed = GroupsPage.MoveOrCopy(f.Beta, f.RootA, copy: false);

                Assert.That(changed, Is.False);
                Assert.That(Directory.Exists(f.Beta), Is.True);
                Assert.That(GroupStore.ListGroups(f.RootA).Select(g => g.Item2), Is.EqualTo(new[] { "Alpha", "Beta", "Gamma" }));
            }
        }

        [Test]
        public void MoveOrCopy_Move_SameDiskResultAsADragWithoutCtrl()
        {
            using (var f = new ThreeGroups())
            {
                bool changed = GroupsPage.MoveOrCopy(f.Beta, f.RootB, copy: false);

                Assert.That(changed, Is.True);
                Assert.That(Directory.Exists(f.Beta), Is.False, "the source folder is gone after a move");
                Assert.That(GroupStore.ListGroups(f.RootA).Select(g => g.Item2), Is.EqualTo(new[] { "Alpha", "Gamma" }));
                var destNames = GroupStore.ListGroups(f.RootB).Select(g => g.Item2).ToList();
                Assert.That(destNames, Is.EqualTo(new[] { "Beta" }));
                Assert.That(Directory.Exists(Path.Combine(GroupStore.ListGroups(f.RootB)[0].Item1, "01_Main")), Is.True);
            }
        }

        [Test]
        public void MoveOrCopy_Copy_SameDiskResultAsADragWithCtrl()
        {
            using (var f = new ThreeGroups())
            {
                bool changed = GroupsPage.MoveOrCopy(f.Beta, f.RootB, copy: true);

                Assert.That(changed, Is.True);
                Assert.That(Directory.Exists(f.Beta), Is.True, "the source folder survives a copy");
                Assert.That(GroupStore.ListGroups(f.RootA).Select(g => g.Item2), Is.EqualTo(new[] { "Alpha", "Beta", "Gamma" }));
                var destNames = GroupStore.ListGroups(f.RootB).Select(g => g.Item2).ToList();
                Assert.That(destNames, Is.EqualTo(new[] { "Beta" }));
                Assert.That(Directory.Exists(Path.Combine(GroupStore.ListGroups(f.RootB)[0].Item1, "01_Main")), Is.True);
            }
        }

        [Test]
        public void MoveOrCopy_SameRootCopy_IsNeverANoOp()
        {
            // Unlike a plain move, copying onto the group's own profile still duplicates it: the old
            // window's OnProfileDrop only special-cased "same root and not copying".
            using (var f = new ThreeGroups())
            {
                bool changed = GroupsPage.MoveOrCopy(f.Beta, f.RootA, copy: true);

                Assert.That(changed, Is.True);
                var names = GroupStore.ListGroups(f.RootA).Select(g => g.Item2).ToList();
                Assert.That(names.Count(n => n == "Beta"), Is.EqualTo(2));
            }
        }

        [Test]
        public void MoveOrCopy_NullSource_IsANoOp()
        {
            using (var f = new ThreeGroups())
            {
                Assert.That(GroupsPage.MoveOrCopy(null, f.RootB, copy: false), Is.False);
                Assert.That(GroupsPage.MoveOrCopy(string.Empty, f.RootB, copy: true), Is.False);
            }
        }
    }
}
