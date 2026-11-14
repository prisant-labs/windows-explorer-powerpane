using System;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>The copy actions that use the Explorer selection, and the guard that keeps the selection
    /// Explorer makes after a move up from being copied in place of the folder on screen.</summary>
    [TestFixture]
    public class WindowFolderHistoryTests
    {
        // ---- the guard ----------------------------------------------------------------------------

        // The newest navigation is to the folder shown, and the one before it was the selected folder.
        [TestCase(@"C:\Demo", @"C:\Demo\Sub", new[] { @"C:\Demo", @"C:\Demo\Sub" })]
        // Two levels up at once: the window came from deeper inside the selected folder.
        [TestCase(@"C:\Demo", @"C:\Demo\Sub", new[] { @"C:\Demo", @"C:\Demo\Sub\Deeper" })]
        // Drive root, trailing backslashes, and case.
        [TestCase(@"C:\", @"C:\Demo", new[] { @"c:\", @"c:\demo\" })]
        [TestCase(@"C:\Demo\", @"C:\Demo\Sub\", new[] { @"c:\demo", @"C:\DEMO\SUB" })]
        // A network share.
        [TestCase(@"\\server\share", @"\\server\share\Sub", new[] { @"\\server\share", @"\\server\share\Sub\Deeper" })]
        public void IsUpSelection_TheFolderJustLeft(string folder, string selected, string[] recent)
        {
            Assert.That(WindowFolderHistory.IsUpSelection(folder, new[] { selected }, recent), Is.True);
        }

        // The Codex finding: tab A visited Sub, and in tab B, which shows C:\Demo, the user selects Sub.
        // A tab switch records nothing, so the newest entry is tab A's folder, not the one shown.
        [Test]
        public void IsUpSelection_OnlyTheLatestMoveCounts()
        {
            // Tab A was last in Sub.
            AssertNotClaimed(new[] { @"C:\Demo\Sub", @"C:\Demo" });
            // Nothing newer than Sub was recorded.
            AssertNotClaimed(new[] { @"C:\Demo\Sub" });
            // Sub is further back than the latest move.
            AssertNotClaimed(new[] { @"C:\Demo", @"C:\Other", @"C:\Demo\Sub" });
        }

        private static void AssertNotClaimed(string[] recent)
        {
            Assert.That(WindowFolderHistory.IsUpSelection(@"C:\Demo", new[] { @"C:\Demo\Sub" }, recent), Is.False,
                string.Join(" | ", recent));
        }

        [Test]
        public void IsUpSelection_NeverTwoItems()
        {
            Assert.That(WindowFolderHistory.IsUpSelection(@"C:\Demo",
                new[] { @"C:\Demo\Sub", @"C:\Demo\Other" }, new[] { @"C:\Demo\Sub" }), Is.False);
        }

        [TestCase(@"C:\Demo\Other", Description = "a different subfolder")]
        [TestCase(@"C:\Demo\Subway", Description = "a name that only starts like the folder left")]
        [TestCase(@"C:\Demo\notes.txt", Description = "a file")]
        public void IsUpSelection_NeverAnyOtherItem(string selected)
        {
            Assert.That(WindowFolderHistory.IsUpSelection(@"C:\Demo", new[] { selected },
                new[] { @"C:\Demo", @"C:\Demo\Sub" }), Is.False);
        }

        [Test]
        public void IsUpSelection_NeverAnItemOutsideTheFolder()
        {
            // The history is stale (the window moved elsewhere): the item is not inside the folder shown.
            Assert.That(WindowFolderHistory.IsUpSelection(@"D:\Work", new[] { @"C:\Demo\Sub" },
                new[] { @"C:\Demo\Sub" }), Is.False);
        }

        [Test]
        public void IsUpSelection_NeverWithoutHistoryOrFolder()
        {
            Assert.That(WindowFolderHistory.IsUpSelection(@"C:\Demo", new[] { @"C:\Demo\Sub" }, new string[0]), Is.False);
            Assert.That(WindowFolderHistory.IsUpSelection(null, new[] { @"C:\Demo\Sub" }, new[] { @"C:\Demo\Sub" }), Is.False);
            Assert.That(WindowFolderHistory.IsUpSelection(@"C:\Demo", null, new[] { @"C:\Demo\Sub" }), Is.False);
        }

        // ---- the history ---------------------------------------------------------------------------

        [Test]
        public void Record_KeepsRecentDistinctFoldersPerWindow()
        {
            var window = new IntPtr(0x7E57A1);
            WindowFolderHistory.Record(window, @"C:\Demo\Sub");
            WindowFolderHistory.Record(window, @"C:\Demo\Sub\");
            WindowFolderHistory.Record(window, @"C:\Demo");
            WindowFolderHistory.Record(window, @"C:\");
            Assert.That(WindowFolderHistory.Recent(window), Is.EqualTo(new[] { @"C:\", @"C:\Demo", @"C:\Demo\Sub" }));
            Assert.That(WindowFolderHistory.Recent(new IntPtr(0x7E57A2)), Is.Empty);
        }

        [Test]
        public void Push_MovesARepeatUpAndCapsTheDepth()
        {
            var h = new string[0];
            foreach (var f in new[] { "a", "b", "c", "d", "e", "f", "g" }) h = WindowFolderHistory.Push(h, @"C:\" + f);
            Assert.That(h.Length, Is.EqualTo(WindowFolderHistory.Depth));
            Assert.That(h[0], Is.EqualTo(@"C:\g"));
            Assert.That(h, Does.Not.Contain(@"C:\a"), "the oldest falls off");
            h = WindowFolderHistory.Push(h, @"C:\D\");
            Assert.That(h[0], Is.EqualTo(@"C:\D\"));
            Assert.That(h.Length, Is.EqualTo(WindowFolderHistory.Depth));
            Assert.That(System.Array.FindAll(h, x => x.TrimEnd('\\').Equals(@"C:\d", StringComparison.OrdinalIgnoreCase)).Length, Is.EqualTo(1));
        }

        // The Codex finding: into a subfolder and back faster than the two-second poll. With every
        // navigation recorded as it happens, the subfolder is in the history when Copy path is clicked.
        [Test]
        public void InAndBack_EveryNavigationRecorded_IsCaught()
        {
            var window = new IntPtr(0x7E57B1);
            WindowFolderHistory.Record(window, @"C:\Demo");
            WindowFolderHistory.Record(window, @"C:\Demo\Sub");
            WindowFolderHistory.Record(window, @"C:\Demo");
            Assert.That(WindowFolderHistory.IsUpSelection(@"C:\Demo", new[] { @"C:\Demo\Sub" },
                WindowFolderHistory.Recent(window)), Is.True);
        }

        // Tab A left in Sub, two moves in tab B of the same window, back to A, Up. The history is per
        // window, so the move before the Up looks like tab B's; the guard no longer claims this case, and
        // Copy path copies the selected folder, as it did before the guard existed.
        [Test]
        public void TabLeftInSubfolder_AfterMovesInAnotherTab_IsNotClaimed()
        {
            var window = new IntPtr(0x7E57C1);
            WindowFolderHistory.Record(window, @"C:\Demo\Sub");
            WindowFolderHistory.Record(window, @"C:\Other");
            WindowFolderHistory.Record(window, @"C:\Other\Deeper");
            WindowFolderHistory.Record(window, @"C:\Demo");
            Assert.That(WindowFolderHistory.IsUpSelection(@"C:\Demo", new[] { @"C:\Demo\Sub" },
                WindowFolderHistory.Recent(window)), Is.False);
        }

        [TestCase(@"C:\Demo\Sub", @"C:\Demo\Sub")]
        [TestCase(@"C:\", @"C:\")]
        [TestCase(@"\\server\share\Sub", @"\\server\share\Sub")]
        [TestCase("file:///C:/Demo/My%20Sub", @"C:\Demo\My Sub")]
        [TestCase("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", null)]
        [TestCase("", null)]
        public void FolderFromUrl_KeepsOnlyPlacesOnDisk(string url, string expected)
        {
            Assert.That(PowerPane.Explorer.NavigationListener.FolderFromUrl(url), Is.EqualTo(expected));
        }

        [Test]
        public void FolderFromUrl_IgnoresANonString()
        {
            Assert.That(PowerPane.Explorer.NavigationListener.FolderFromUrl(42), Is.Null);
            Assert.That(PowerPane.Explorer.NavigationListener.FolderFromUrl(null), Is.Null);
        }

        // ---- the copy text -------------------------------------------------------------------------

        private static readonly string[] Selected = { @"C:\Demo\a.txt", @"C:\Demo\Sub" };

        [TestCase("copy-path", @"C:\Demo\a.txt|C:\Demo\Sub")]
        [TestCase("copy-path-quoted", "\"C:\\Demo\\a.txt\"|\"C:\\Demo\\Sub\"")]
        [TestCase("copy-path-forward", "C:/Demo/a.txt|C:/Demo/Sub")]
        [TestCase("copy-folder-name", "a.txt|Sub")]
        [TestCase("copy-file-url", "file:///C:/Demo/a.txt|file:///C:/Demo/Sub")]
        public void CopyTextFor_UsesTheSelectionWhenThereIsOne(string id, string expected)
        {
            Assert.That(QuickActionRunner.CopyTextFor(id, @"C:\Demo", Selected),
                Is.EqualTo(expected.Replace("|", Environment.NewLine)));
        }

        [TestCase(null)]
        [TestCase("empty")]
        public void CopyTextFor_UsesTheFolderWhenNothingIsSelected(string selection)
        {
            Assert.That(QuickActionRunner.CopyTextFor("copy-path", @"C:\Demo", selection == null ? null : new string[0]),
                Is.EqualTo(@"C:\Demo"));
        }

        [TestCase("copy-parent-path", @"C:\")]
        [TestCase("copy-cd-powershell", "Set-Location -LiteralPath 'C:\\Demo'")]
        public void CopyTextFor_FolderOnlyActionsIgnoreTheSelection(string id, string expected)
        {
            Assert.That(QuickActionRunner.CopyTextFor(id, @"C:\Demo", Selected), Is.EqualTo(expected));
        }

        [Test]
        public void SelectionCopyId_EveryMappingIsASelectionAction()
        {
            foreach (var def in QuickActionLibrary.All)
            {
                string id = QuickActionLibrary.SelectionCopyId(def.Id);
                if (id == null) continue;
                Assert.That(def.Kind, Is.EqualTo(QuickActionKind.CopyPath), def.Id);
                Assert.That(QuickActionLibrary.Find(id).Kind, Is.EqualTo(QuickActionKind.Selection), id);
            }
        }
    }
}
