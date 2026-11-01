using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using QuickPane.Services;

namespace QuickPane.Tests
{
    /// <summary>
    /// Covers WorkLane, the background thread behind WorkQueue. Folder clicks run on a lane of their
    /// own so they never wait behind slow work on the general lane (#38). Every wait has a timeout, so
    /// a broken lane fails the test instead of hanging the run.
    /// </summary>
    [TestFixture]
    public class WorkLaneTests
    {
        private const int Timeout = 5000;

        [Test]
        public void Post_RunsWorkInOrder()
        {
            var lane = new WorkLane("test-order");
            var seen = new List<int>();
            var done = new ManualResetEventSlim();
            for (int i = 0; i < 20; i++)
            {
                int n = i;
                lane.Post(() => { lock (seen) seen.Add(n); });
            }
            lane.Post(done.Set);

            Assert.That(done.Wait(Timeout), Is.True);
            Assert.That(seen, Is.EqualTo(System.Linq.Enumerable.Range(0, 20)));
        }

        [Test]
        public void Post_FailingItem_IsReportedAndDoesNotStopTheLane()
        {
            // The handler keeps the expected failure out of the real log.
            Exception reported = null;
            var lane = new WorkLane("test-failure", (message, ex) => reported = ex);
            var done = new ManualResetEventSlim();
            lane.Post(() => { throw new InvalidOperationException("expected by the test"); });
            lane.Post(done.Set);

            Assert.That(done.Wait(Timeout), Is.True);
            Assert.That(reported, Is.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void Post_RunsOnAnStaThread()
        {
            // Shell COM such as Shell.Application and IShellLink expects a single-threaded apartment.
            var lane = new WorkLane("test-sta");
            var apartment = ApartmentState.Unknown;
            var done = new ManualResetEventSlim();
            lane.Post(() => { apartment = Thread.CurrentThread.GetApartmentState(); done.Set(); });

            Assert.That(done.Wait(Timeout), Is.True);
            Assert.That(apartment, Is.EqualTo(ApartmentState.STA));
        }

        [Test]
        public void Post_OneLaneBlocked_OtherLaneStillRuns()
        {
            // The bug: a click queued behind a slow copy or a dead network probe on the shared worker.
            var general = new WorkLane("test-general");
            var navigation = new WorkLane("test-navigation");
            var release = new ManualResetEventSlim();
            var clicked = new ManualResetEventSlim();
            try
            {
                general.Post(() => release.Wait(Timeout));
                navigation.Post(clicked.Set);

                Assert.That(clicked.Wait(Timeout), Is.True);
                Assert.That(release.IsSet, Is.False);
            }
            finally { release.Set(); }
        }

        [Test]
        public void Post_Null_IsIgnored()
        {
            var lane = new WorkLane("test-null");
            var done = new ManualResetEventSlim();
            lane.Post(null);
            lane.Post(done.Set);

            Assert.That(done.Wait(Timeout), Is.True);
        }

        [Test]
        public void Post_QueueBacksUp_LogsOneDepthLine()
        {
            // The bug: a stalled lane's backlog was invisible. Block the lane on the first item, then
            // post 30 more; the depth line should fire exactly once, at 25 (#77).
            var lines = new List<string>();
            var lane = new WorkLane("test-depth", (m, ex) => { }, m => { lock (lines) lines.Add(m); });
            var release = new ManualResetEventSlim();
            try
            {
                lane.Post(() => release.Wait(Timeout));
                // Post() reports the depth synchronously on this thread, so no wait is needed: the
                // lane thread stays blocked on the first item for the whole loop below.
                for (int i = 0; i < 30; i++) lane.Post(() => { });

                Assert.That(lines, Is.EqualTo(new[] { "test-depth queue depth 25" }));
            }
            finally { release.Set(); }
        }

        [Test]
        public void ShouldReportDepth_CrossesLinesAndResetsAtEmpty()
        {
            int next = 25;
            Assert.That(WorkLane.ShouldReportDepth(24, ref next), Is.False);
            Assert.That(next, Is.EqualTo(25));

            Assert.That(WorkLane.ShouldReportDepth(25, ref next), Is.True);
            Assert.That(next, Is.EqualTo(50));

            Assert.That(WorkLane.ShouldReportDepth(30, ref next), Is.False);
            Assert.That(next, Is.EqualTo(50));

            Assert.That(WorkLane.ShouldReportDepth(50, ref next), Is.True);
            Assert.That(next, Is.EqualTo(100));
        }

        [Test]
        public void ShouldReportDepth_EmptyQueueResetsNextLine()
        {
            int next = 200;
            Assert.That(WorkLane.ShouldReportDepth(0, ref next), Is.False);
            Assert.That(next, Is.EqualTo(25));
            Assert.That(WorkLane.ShouldReportDepth(25, ref next), Is.True);
        }

        [Test]
        public void ShouldReportDepth_LargeJump_AdvancesPastIt()
        {
            // A burst that jumps straight from 20 to 120 still gets exactly one line, and the next
            // threshold lands above the jump instead of requiring several more crossings.
            int next = 25;
            Assert.That(WorkLane.ShouldReportDepth(120, ref next), Is.True);
            Assert.That(next, Is.EqualTo(200));
        }
    }
}
