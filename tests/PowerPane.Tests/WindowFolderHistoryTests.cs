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

        // The tracker has caught up (newest is the parent) or not yet (newest is still the child).
        [TestCase(@"C:\Demo", @"C:\Demo\Sub", new[] { @"C:\Demo", @"C:\Demo\Sub" })]
        [TestCase(@"C:\Demo", @"C:\Demo\Sub", new[] { @"C:\Demo\Sub" })]
        // Two levels up faster than the tracker polls: the window was last seen deeper inside.
        [TestCase(@"C:\Demo", @"C:\Demo\Sub", new[] { @"C:\Demo\Sub\Deeper" })]
        // Drive root, trailing backslashes, and case.
        [TestCase(@"C:\", @"C:\Demo", new[] { @"c:\demo\" })]
        [TestCase(@"C:\Demo\", @"C:\Demo\Sub\", new[] { @"C:\DEMO\SUB" })]
        // A network share.
        [TestCase(@"\\server\share", @"\\server\share\Sub", new[] { @"\\server\share\Sub\Deeper" })]
        public void IsUpSelection_TheFolderJustLeft(string folder, string selected, string[] recent)
        {
            Assert.That(WindowFolderHistory.IsUpSelection(folder, new[] { selected }, recent), Is.True);
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
        public void Record_KeepsTheLastTwoDistinctFoldersPerWindow()
        {
            var window = new IntPtr(0x7E57A1);
            WindowFolderHistory.Record(window, @"C:\Demo\Sub");
            WindowFolderHistory.Record(window, @"C:\Demo\Sub\");
            WindowFolderHistory.Record(window, @"C:\Demo");
            WindowFolderHistory.Record(window, @"C:\");
            Assert.That(WindowFolderHistory.Recent(window), Is.EqualTo(new[] { @"C:\", @"C:\Demo" }));
            Assert.That(WindowFolderHistory.Recent(new IntPtr(0x7E57A2)), Is.Empty);
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
