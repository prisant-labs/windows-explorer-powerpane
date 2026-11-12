using System;
using System.Collections.Generic;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers the decisions behind hiding folders from Recent and Clear Recent (#224): how a recent
    /// item is matched against a hidden folder, which Windows Recent shortcuts a Clear hides, and how
    /// a folder joins the list. All pure; nothing here touches the disk.
    /// </summary>
    [TestFixture]
    public class RecentHiddenFoldersTests
    {
        private static readonly List<string> Hidden = new List<string> { @"C:\Demo\Private" };

        [TestCase(@"C:\Demo\Private")]
        [TestCase(@"C:\Demo\Private\")]
        [TestCase(@"c:\demo\PRIVATE")]
        [TestCase(@"C:\Demo\Private\Sub")]
        [TestCase(@"C:\Demo\Private\Sub\Deeper\")]
        [TestCase(@"C:\Demo\Private\notes.txt")]
        [TestCase(@"C:/Demo/Private/Sub")]
        [TestCase(@"  C:\Demo\Private  ")]
        public void IsHidden_TheFolderAndEverythingInIt(string path)
        {
            Assert.That(RecentFoldersService.IsHidden(path, Hidden), Is.True);
        }

        [TestCase(@"C:\Demo\PrivateStuff")]
        [TestCase(@"C:\Demo\Privat")]
        [TestCase(@"C:\Demo")]
        [TestCase(@"C:\Demo\Other\Private")]
        [TestCase(@"D:\Demo\Private")]
        [TestCase("")]
        [TestCase(null)]
        public void IsHidden_NotASiblingWithTheSamePrefixOrAParent(string path)
        {
            Assert.That(RecentFoldersService.IsHidden(path, Hidden), Is.False);
        }

        [Test]
        public void IsHidden_ListedWithATrailingBackslash_MatchesTheSame()
        {
            var withSlash = new List<string> { @"C:\Demo\Private\" };
            Assert.That(RecentFoldersService.IsHidden(@"C:\Demo\Private", withSlash), Is.True);
            Assert.That(RecentFoldersService.IsHidden(@"C:\Demo\PrivateStuff", withSlash), Is.False);
        }

        [Test]
        public void IsHidden_ADriveRootHidesTheDrive()
        {
            var root = new List<string> { @"E:\" };
            Assert.That(RecentFoldersService.IsHidden(@"E:\Demo\Projects", root), Is.True);
            Assert.That(RecentFoldersService.IsHidden(@"C:\Demo\Projects", root), Is.False);
        }

        [Test]
        public void IsHidden_ANetworkShare()
        {
            var share = new List<string> { @"\\server\share\Private" };
            Assert.That(RecentFoldersService.IsHidden(@"\\SERVER\share\Private\Sub", share), Is.True);
            Assert.That(RecentFoldersService.IsHidden(@"\\server\share\Public", share), Is.False);
        }

        [Test]
        public void IsHidden_NoListOrBlankEntries_HidesNothing()
        {
            Assert.That(RecentFoldersService.IsHidden(@"C:\Demo\Private", null), Is.False);
            Assert.That(RecentFoldersService.IsHidden(@"C:\Demo\Private", new List<string> { "", "  ", null }), Is.False);
        }

        [Test]
        public void ClearedBefore_HidesShortcutsWrittenAtOrBeforeTheClear()
        {
            var cleared = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
            Assert.That(RecentFoldersService.ClearedBefore(cleared.AddMinutes(-5), cleared.Ticks), Is.True);
            Assert.That(RecentFoldersService.ClearedBefore(cleared, cleared.Ticks), Is.True);
            Assert.That(RecentFoldersService.ClearedBefore(cleared.AddSeconds(1), cleared.Ticks), Is.False,
                "a folder opened again after the clear rewrites its shortcut, so it shows again");
        }

        [Test]
        public void ClearedBefore_NeverClearedHidesNothing()
        {
            Assert.That(RecentFoldersService.ClearedBefore(DateTime.MinValue, 0), Is.False);
            Assert.That(RecentFoldersService.ClearedBefore(DateTime.UtcNow, 0), Is.False);
        }

        [Test]
        public void WithHidden_AddsANewFolderWithoutTheTrailingBackslash()
        {
            var updated = RecentFoldersService.WithHidden(null, @"C:\Demo\Private\");
            Assert.That(updated, Is.EqualTo(new[] { @"C:\Demo\Private" }));
        }

        [Test]
        public void WithHidden_ReturnsANewList_NeverEditsTheOldOne()
        {
            // The Recent scan reads the old list from the work lane while the UI adds to it.
            var old = new List<string> { @"C:\Demo\One" };
            var updated = RecentFoldersService.WithHidden(old, @"C:\Demo\Two");
            Assert.That(updated, Is.Not.SameAs(old));
            Assert.That(old, Is.EqualTo(new[] { @"C:\Demo\One" }));
            Assert.That(updated, Is.EqualTo(new[] { @"C:\Demo\One", @"C:\Demo\Two" }));
        }

        [TestCase(@"C:\Demo\Private")]
        [TestCase(@"c:\demo\private\")]
        [TestCase(@"C:\Demo\Private\Sub")]
        [TestCase("")]
        [TestCase(null)]
        public void WithHidden_NothingToAdd_ReturnsNull(string folder)
        {
            Assert.That(RecentFoldersService.WithHidden(Hidden, folder), Is.Null);
        }

        [Test]
        public void WithHidden_ADriveRootKeepsItsBackslash()
        {
            Assert.That(RecentFoldersService.WithHidden(null, @"E:\"), Is.EqualTo(new[] { @"E:\" }));
        }
    }
}
