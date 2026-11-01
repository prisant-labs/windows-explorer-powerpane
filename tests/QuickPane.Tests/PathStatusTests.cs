using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using QuickPane.Services;
using QuickPane.UI;

namespace QuickPane.Tests
{
    /// <summary>
    /// Covers the bounded folder check that keeps Edit path's Browse button from freezing on a share
    /// that does not answer (#41). The probes are fakes, so no disk or network is touched.
    /// </summary>
    [TestFixture]
    public class PathStatusTests
    {
        private static bool Hang(string path) { Thread.Sleep(3000); return true; }

        [Test]
        public void FolderExistsWithin_FastAnswer_ReturnsIt()
        {
            bool timedOut;
            Assert.That(PathStatus.FolderExistsWithin(@"C:\Demo\Projects", 1000, out timedOut, p => true), Is.True);
            Assert.That(timedOut, Is.False);
            Assert.That(PathStatus.FolderExistsWithin(@"C:\Demo\Gone", 1000, out timedOut, p => false), Is.False);
            Assert.That(timedOut, Is.False);
        }

        [Test]
        public void FolderExistsWithin_NoAnswer_GivesUpAtTheTimeout()
        {
            bool timedOut;
            var clock = Stopwatch.StartNew();
            Assert.That(PathStatus.FolderExistsWithin(@"\\demo-server\share", 100, out timedOut, Hang), Is.False);
            Assert.That(timedOut, Is.True);
            Assert.That(clock.ElapsedMilliseconds, Is.LessThan(2000));
        }

        [Test]
        public void FolderExistsWithin_ProbeThrows_ReadsAsMissing()
        {
            bool timedOut;
            Assert.That(PathStatus.FolderExistsWithin(@"C:\Demo", 1000, out timedOut,
                p => { throw new UnauthorizedAccessException("expected by the test"); }), Is.False);
        }

        [Test]
        public void FolderExistsWithin_EmptyPath_IsFalse()
        {
            bool timedOut;
            Assert.That(PathStatus.FolderExistsWithin("", 1000, out timedOut, p => true), Is.False);
        }

        // ---- AnyFolderWithin (#75) --------------------------------------------
        // Drop classification used to call Directory.Exists in a loop straight on the UI thread.

        [Test]
        public void AnyFolderWithin_OneSourceIsAFolder_ReturnsTrue()
        {
            bool timedOut;
            Assert.That(PathStatus.AnyFolderWithin(
                new[] { @"C:\Demo\File.txt", @"C:\Demo\Folder" }, 1000, out timedOut,
                p => p == @"C:\Demo\Folder"), Is.True);
            Assert.That(timedOut, Is.False);
        }

        [Test]
        public void AnyFolderWithin_NoFolderAmongSources_ReturnsFalse()
        {
            bool timedOut;
            Assert.That(PathStatus.AnyFolderWithin(
                new[] { @"C:\Demo\File.txt", @"C:\Demo\Other.txt" }, 1000, out timedOut, p => false), Is.False);
            Assert.That(timedOut, Is.False);
        }

        [Test]
        public void AnyFolderWithin_NoAnswer_GivesUpAtTheTimeoutAndReadsAsAFolder()
        {
            // A timeout reads as "contains a folder": the caller offers the ambiguous-drop menu
            // rather than moving silently what might have been one.
            bool timedOut;
            var clock = Stopwatch.StartNew();
            Assert.That(PathStatus.AnyFolderWithin(new[] { @"\\demo-server\share" }, 100, out timedOut, Hang), Is.True);
            Assert.That(timedOut, Is.True);
            Assert.That(clock.ElapsedMilliseconds, Is.LessThan(2000));
        }

        [Test]
        public void AnyFolderWithin_EmptyOrNullSources_IsFalse()
        {
            bool timedOut;
            Assert.That(PathStatus.AnyFolderWithin(new string[0], 1000, out timedOut, p => true), Is.False);
            Assert.That(PathStatus.AnyFolderWithin(null, 1000, out timedOut, p => true), Is.False);
        }

        [Test]
        public void NearestExistingFolder_WalksUpToAnExistingAncestor()
        {
            Func<string, bool> exists = p => p == @"C:\Demo\Projects";
            Assert.That(GroupSection.NearestExistingFolder(@"C:\Demo\Projects\Old\Gone", exists), Is.EqualTo(@"C:\Demo\Projects"));
        }

        [Test]
        public void NearestExistingFolder_VolumeNotAnswering_StopsAfterOneCheck()
        {
            // The bug: a disconnected mapped drive timed out once for every level of the path.
            int calls = 0;
            Func<string, bool> exists = p => { Interlocked.Increment(ref calls); return Hang(p); };
            var clock = Stopwatch.StartNew();
            Assert.That(GroupSection.NearestExistingFolder(@"Z:\Demo\Projects\Deep\Folder", exists), Is.Null);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(clock.ElapsedMilliseconds, Is.LessThan(PathStatus.InteractiveProbeMs + 1500));
        }
    }
}
