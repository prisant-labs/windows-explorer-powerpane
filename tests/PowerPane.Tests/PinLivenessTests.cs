using System;
using System.Collections.Generic;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers GroupStore.RefreshLiveness, which the pane runs for each pin as it builds its rows, so a
    /// probe that finds a pinned folder gone dims the row without waiting for the next groups scan
    /// (#136). The probe cache is replaced by invented answers; no path is touched.
    /// </summary>
    [TestFixture]
    public class PinLivenessTests
    {
        private static PinnedFolder Pin(string target, bool exists, bool isFile = false)
        {
            return new PinnedFolder { DisplayName = "Reports", TargetPath = target, Exists = exists, IsFile = isFile };
        }

        [Test]
        public void RefreshLiveness_ProbeFoundTargetGone_PinGoesBroken()
        {
            // The scan saw the share answer; a later probe found it gone.
            var pin = Pin(@"\\demo-server\share\Reports", exists: true);
            GroupStore.RefreshLiveness(pin, p => false, p => false);
            Assert.That(pin.Exists, Is.False);
        }

        [Test]
        public void RefreshLiveness_ProbeFoundTargetBack_PinRecovers()
        {
            var pin = Pin(@"\\demo-server\share\Reports", exists: false);
            GroupStore.RefreshLiveness(pin, p => true, p => false);
            Assert.That(pin.Exists, Is.True);
        }

        [Test]
        public void RefreshLiveness_ProbeFoundAFile_IsFileFollows()
        {
            var pin = Pin(@"C:\Demo\Projects\plan.txt", exists: true, isFile: false);
            GroupStore.RefreshLiveness(pin, p => true, p => true);
            Assert.That(pin.IsFile, Is.True);
        }

        [Test]
        public void RefreshLiveness_AsksTheCacheAboutTheTarget()
        {
            var asked = new List<string>();
            var pin = Pin(@"C:\Demo\Projects", exists: true);
            GroupStore.RefreshLiveness(pin, p => { asked.Add(p); return true; }, p => false);
            Assert.That(asked, Is.EqualTo(new[] { @"C:\Demo\Projects" }));
        }

        [TestCase("")]
        [TestCase(null)]
        public void RefreshLiveness_NoTarget_StaysBrokenWithoutAsking(string target)
        {
            // A shortcut whose target could not be resolved is broken; nothing is asked about it.
            var pin = Pin(target, exists: true, isFile: true);
            Func<string, bool> fail = p => { Assert.Fail("asked about an empty target"); return true; };
            GroupStore.RefreshLiveness(pin, fail, fail);
            Assert.That(pin.Exists, Is.False);
            Assert.That(pin.IsFile, Is.False);
        }

        [Test]
        public void RefreshLiveness_NullPin_DoesNothing()
        {
            Assert.DoesNotThrow(() => GroupStore.RefreshLiveness(null, p => true, p => true));
        }
    }
}
