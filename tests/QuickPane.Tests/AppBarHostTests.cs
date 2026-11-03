using NUnit.Framework;
using QuickPane.Explorer;
using NM = QuickPane.Interop.NativeMethods;

namespace QuickPane.Tests
{
    /// <summary>The reserved desktop dock asks the shell for a strip at the monitor's left edge. It
    /// used to build that request from the work area, which already excludes the strip the dock
    /// holds, so every repeat moved one dock width right and squeezed the desktop (#145). These
    /// tests pin the request to the monitor's bounds.</summary>
    [TestFixture]
    public class AppBarHostTests
    {
        private static NM.RECT Rect(int left, int top, int right, int bottom)
        {
            return new NM.RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        }

        [Test]
        public void ProposedStrip_IsMonitorLeftEdgeAtFullHeight()
        {
            var strip = AppBarHost.ProposedStrip(Rect(0, 0, 2560, 1440), 220);
            Assert.That(AppBarHost.SameRect(strip, Rect(0, 0, 220, 1440)), Is.True);
        }

        [Test]
        public void ProposedStrip_FollowsAMonitorLeftOfAndAbovePrimary()
        {
            var strip = AppBarHost.ProposedStrip(Rect(-1920, -1080, 0, 0), 300);
            Assert.That(AppBarHost.SameRect(strip, Rect(-1920, -1080, -1620, 0)), Is.True);
        }

        [Test]
        public void ProposedStrip_RepeatedAfterReserving_AsksForTheSameStrip()
        {
            // What the shell does after SETPOS: the work area starts at the strip's right edge. The
            // old code proposed from that edge and walked right on every call; the monitor's bounds
            // do not change, so the second request must equal the first.
            var monitor = Rect(0, 0, 1920, 1080);
            var first = AppBarHost.ProposedStrip(monitor, 220);
            var workAreaAfter = Rect(first.Right, 0, 1920, 1040);
            Assert.That(workAreaAfter.Left, Is.EqualTo(220));

            var second = AppBarHost.ProposedStrip(monitor, 220);
            Assert.That(AppBarHost.SameRect(first, second), Is.True);
        }

        [Test]
        public void SameRect_ComparesEveryEdge()
        {
            var a = Rect(1, 2, 3, 4);
            Assert.That(AppBarHost.SameRect(a, Rect(1, 2, 3, 4)), Is.True);
            Assert.That(AppBarHost.SameRect(a, Rect(0, 2, 3, 4)), Is.False);
            Assert.That(AppBarHost.SameRect(a, Rect(1, 0, 3, 4)), Is.False);
            Assert.That(AppBarHost.SameRect(a, Rect(1, 2, 0, 4)), Is.False);
            Assert.That(AppBarHost.SameRect(a, Rect(1, 2, 3, 0)), Is.False);
        }
    }
}
