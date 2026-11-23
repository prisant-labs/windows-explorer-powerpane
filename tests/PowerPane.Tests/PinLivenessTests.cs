using System.Collections.Generic;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers GroupStore.RefreshLiveness, which the pane runs for each pin as it builds its rows, so a
    /// probe that finds a pinned folder gone dims the row without waiting for the next groups scan
    /// (#136), and PathStatus.ProbeChangesRows, which decides whether a probe result rebuilds the
    /// sections at all. The probe cache is replaced by invented answers; no path is touched.
    /// </summary>
    [TestFixture]
    public class PinLivenessTests
    {
        private static PinnedFolder Pin(string target, bool exists, bool isFile = false)
        {
            return new PinnedFolder { DisplayName = "Reports", TargetPath = target, Exists = exists, IsFile = isFile };
        }

        private static GroupStore.CachedLiveness Cached(bool exists, bool isFile)
        {
            return (string p, bool se, bool sf, out bool e, out bool f) => { e = exists; f = isFile; return true; };
        }

        private static readonly GroupStore.CachedLiveness NotCached =
            (string p, bool se, bool sf, out bool e, out bool f) => { e = true; f = true; return false; };

        [Test]
        public void RefreshLiveness_ProbeFoundTargetGone_PinGoesBroken()
        {
            // The scan saw the share answer; a later probe found it gone.
            var pin = Pin(@"\\demo-server\share\Reports", exists: true);
            GroupStore.RefreshLiveness(pin, Cached(false, false));
            Assert.That(pin.Exists, Is.False);
        }

        [Test]
        public void RefreshLiveness_ProbeFoundTargetBack_PinRecovers()
        {
            var pin = Pin(@"\\demo-server\share\Reports", exists: false);
            GroupStore.RefreshLiveness(pin, Cached(true, false));
            Assert.That(pin.Exists, Is.True);
        }

        [Test]
        public void RefreshLiveness_ProbeFoundAFile_IsFileFollows()
        {
            var pin = Pin(@"C:\Demo\Projects\plan.txt", exists: true, isFile: false);
            GroupStore.RefreshLiveness(pin, Cached(true, true));
            Assert.That(pin.IsFile, Is.True);
        }

        [Test]
        public void RefreshLiveness_AsksTheCacheAboutTheTarget()
        {
            var asked = new List<string>();
            var pin = Pin(@"C:\Demo\Projects", exists: true);
            GroupStore.RefreshLiveness(pin, (string p, bool se, bool sf, out bool e, out bool f) => { asked.Add(p); e = true; f = false; return true; });
            Assert.That(asked, Is.EqualTo(new[] { @"C:\Demo\Projects" }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RefreshLiveness_EvictedFromTheCache_KeepsTheLastKnownState(bool lastKnown)
        {
            // An evicted entry must not reset a pin to the optimistic answer, nor start a probe from
            // the build: past the cache's size that cycle never settled (#136 review).
            var pin = Pin(@"\\demo-server\share\Reports", exists: lastKnown);
            GroupStore.RefreshLiveness(pin, NotCached);
            Assert.That(pin.Exists, Is.EqualTo(lastKnown));
        }

        [Test]
        public void RefreshLiveness_EvictedFromTheCache_PassesWhatTheRowShows()
        {
            // The cache starts a missing entry from what the row shows, so a broken pin evicted from
            // the cache is compared with "broken" when its probe lands, and its recovery rebuilds the
            // pane (#136 second review).
            bool? seenExists = null, seenIsFile = null;
            var pin = Pin(@"\\demo-server\share\Reports", exists: false, isFile: false);
            GroupStore.RefreshLiveness(pin, (string p, bool se, bool sf, out bool e, out bool f) =>
            { seenExists = se; seenIsFile = sf; e = f = false; return false; });
            Assert.That(seenExists, Is.False);
            Assert.That(seenIsFile, Is.False);
        }

        [Test]
        public void ProbeChangesRows_SeededBrokenPinRecovers_Rebuilds()
        {
            // The seeded entry says gone, as the row does; the probe finds the share back.
            Assert.That(PathStatus.ProbeChangesRows(false, false, true, false, firstProbe: true, readerWaiting: false), Is.True);
        }

        [TestCase("")]
        [TestCase(null)]
        public void RefreshLiveness_NoTarget_StaysBrokenWithoutAsking(string target)
        {
            // A shortcut whose target could not be resolved is broken; nothing is asked about it.
            var pin = Pin(target, exists: true, isFile: true);
            GroupStore.RefreshLiveness(pin, (string p, bool se, bool sf, out bool e, out bool f) => { Assert.Fail("asked about an empty target"); e = f = true; return true; });
            Assert.That(pin.Exists, Is.False);
            Assert.That(pin.IsFile, Is.False);
        }

        [Test]
        public void RefreshLiveness_NullPin_DoesNothing()
        {
            Assert.DoesNotThrow(() => GroupStore.RefreshLiveness(null, Cached(true, true)));
        }

        // ---- PathStatus.ProbeChangesRows ---------------------------------------

        [Test]
        public void ProbeChangesRows_FirstProbeConfirmsTheOptimisticAnswer_NoRebuild()
        {
            // An expanded pin on a share rebuilt, and re-listed the share, once per child probe while
            // this returned true (#139 review).
            Assert.That(PathStatus.ProbeChangesRows(true, false, true, false, firstProbe: true, readerWaiting: false), Is.False);
        }

        [Test]
        public void ProbeChangesRows_FirstProbeAReaderWaitsOn_Rebuilds()
        {
            // FolderTreeNode restores an expanded subtree only once ConfirmedAlive says yes.
            Assert.That(PathStatus.ProbeChangesRows(true, false, true, false, firstProbe: true, readerWaiting: true), Is.True);
        }

        [Test]
        public void ProbeChangesRows_PathGone_Rebuilds()
        {
            Assert.That(PathStatus.ProbeChangesRows(true, false, false, false, firstProbe: false, readerWaiting: false), Is.True);
        }

        [Test]
        public void ProbeChangesRows_PathBack_Rebuilds()
        {
            Assert.That(PathStatus.ProbeChangesRows(false, false, true, false, firstProbe: false, readerWaiting: false), Is.True);
        }

        [Test]
        public void ProbeChangesRows_GuessedFolderIsAFile_Rebuilds()
        {
            Assert.That(PathStatus.ProbeChangesRows(true, false, true, true, firstProbe: true, readerWaiting: false), Is.True);
        }

        [Test]
        public void ProbeChangesRows_StillGone_NoRebuild()
        {
            // A gone path's file-vs-folder is not shown, so a different guess there changes nothing.
            Assert.That(PathStatus.ProbeChangesRows(false, true, false, false, firstProbe: false, readerWaiting: false), Is.False);
        }

        [Test]
        public void ProbeChangesRows_ReprobeUnchanged_NoRebuild()
        {
            Assert.That(PathStatus.ProbeChangesRows(true, true, true, true, firstProbe: false, readerWaiting: true), Is.False);
        }
    }
}
