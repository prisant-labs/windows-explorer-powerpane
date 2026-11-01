using System;
using System.Collections.Generic;
using NUnit.Framework;
using QuickPane.Services;

namespace QuickPane.Tests
{
    /// <summary>
    /// Covers CoalescedPost, which merges a burst of Request() calls into the one run already queued
    /// or in flight, so separate watcher bursts do not each post a full group scan (#77). A fake post
    /// just collects the posted actions instead of running them, so a test controls exactly when each
    /// run happens.
    /// </summary>
    [TestFixture]
    public class CoalescedPostTests
    {
        [Test]
        public void Request_ThreeCallsBeforeAnyRun_PostsOnce()
        {
            var posted = new List<Action>();
            int runs = 0;
            var coalesced = new CoalescedPost(() => runs++, posted.Add);

            coalesced.Request();
            coalesced.Request();
            coalesced.Request();

            Assert.That(posted.Count, Is.EqualTo(1));
            Assert.That(runs, Is.EqualTo(0));
        }

        [Test]
        public void Request_AfterRunningThePostedAction_PostsAgain()
        {
            var posted = new List<Action>();
            int runs = 0;
            var coalesced = new CoalescedPost(() => runs++, posted.Add);

            coalesced.Request();
            posted[0](); // clears the flag, then runs the work
            Assert.That(runs, Is.EqualTo(1));

            coalesced.Request();
            Assert.That(posted.Count, Is.EqualTo(2));
        }

        [Test]
        public void Request_MadeInsideWork_PostsExactlyOneMoreRun()
        {
            // The flag clears before work() runs, so a change that arrives mid-scan still schedules
            // one more scan instead of being swallowed by the scan in progress.
            var posted = new List<Action>();
            CoalescedPost coalesced = null;
            coalesced = new CoalescedPost(() => coalesced.Request(), posted.Add);

            coalesced.Request();
            Assert.That(posted.Count, Is.EqualTo(1));

            posted[0]();
            Assert.That(posted.Count, Is.EqualTo(2), "the Request() inside work() should have posted one more run");

            // A second Request() while the posted run above is still pending is merged, not posted again.
            coalesced.Request();
            Assert.That(posted.Count, Is.EqualTo(2));
        }
    }
}
