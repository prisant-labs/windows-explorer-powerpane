using System.Collections.Generic;
using NUnit.Framework;
using QuickPane.Explorer;
using NM = QuickPane.Interop.NativeMethods;

namespace QuickPane.Tests
{
    /// <summary>The reserved desktop dock asks the shell for a strip at the monitor's left or right edge. It
    /// used to build that request from the work area, which already excludes the strip the dock
    /// holds, so every repeat moved one dock width right and squeezed the desktop (#145). These
    /// tests pin the request to the monitor's bounds.</summary>
    [TestFixture]
    public class AppBarHostTests
    {
        // ---- #184: the grip and chevron follow the dock's edge ----

        [Test]
        public void ResizeSign_DraggingAGripOutward_Widens()
        {
            // A left grip (right-edge dock, beside pane) widens on a leftward, negative change.
            Assert.That(QuickPane.UI.SidebarControl.ResizeSign(true) * -10, Is.GreaterThan(0));
            Assert.That(QuickPane.UI.SidebarControl.ResizeSign(false) * 10, Is.GreaterThan(0));
        }

        [TestCase(false, false, "\uE76B")]
        [TestCase(true, false, "\uE76C")]
        [TestCase(false, true, "\uE76C")]
        [TestCase(true, true, "\uE76B")]
        public void ToggleGlyph_MirrorsForTheRightEdge(bool collapsed, bool mirrored, string expected)
        {
            Assert.That(QuickPane.UI.SidebarControl.ToggleGlyph(collapsed, mirrored), Is.EqualTo(expected));
        }

        // ---- #185: a maximized window's frame must not cover the whole grip ----

        // Each frame is GetSystemMetricsForDpi(SM_CXSIZEFRAME) + (SM_CXPADDEDBORDER) at that scale, as
        // measured on Windows 11. Whatever the frame covers, a reserved dock must keep the 6 DIP that
        // every other pane's grip has.
        [TestCase(100, 8)]
        [TestCase(125, 9)]
        [TestCase(150, 11)]
        [TestCase(175, 12)]
        [TestCase(200, 13)]
        [TestCase(225, 14)]
        [TestCase(250, 16)]
        [TestCase(300, 18)]
        [TestCase(350, 21)]
        public void ReservedDockGrip_KeepsAUsableWidthBesideAMaximizedFrame(int scalePercent, int framePx)
        {
            double frameDip = framePx * 100.0 / scalePercent;
            Assert.That(QuickPane.UI.SidebarControl.ReservedDockGripWidth - frameDip, Is.GreaterThanOrEqualTo(6));
        }

        private static NM.RECT Rect(int left, int top, int right, int bottom)
        {
            return new NM.RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        }

        [Test]
        public void ProposedStrip_IsMonitorLeftEdgeAtFullHeight()
        {
            var strip = AppBarHost.ProposedStrip(Rect(0, 0, 2560, 1440), 220, false);
            Assert.That(AppBarHost.SameRect(strip, Rect(0, 0, 220, 1440)), Is.True);
        }

        [Test]
        public void ProposedStrip_FollowsAMonitorLeftOfAndAbovePrimary()
        {
            var strip = AppBarHost.ProposedStrip(Rect(-1920, -1080, 0, 0), 300, false);
            Assert.That(AppBarHost.SameRect(strip, Rect(-1920, -1080, -1620, 0)), Is.True);
        }

        [Test]
        public void ProposedStrip_RepeatedAfterReserving_AsksForTheSameStrip()
        {
            // What the shell does after SETPOS: the work area starts at the strip's right edge. The
            // old code proposed from that edge and walked right on every call; the monitor's bounds
            // do not change, so the second request must equal the first.
            var monitor = Rect(0, 0, 1920, 1080);
            var first = AppBarHost.ProposedStrip(monitor, 220, false);
            var workAreaAfter = Rect(first.Right, 0, 1920, 1040);
            Assert.That(workAreaAfter.Left, Is.EqualTo(220));

            var second = AppBarHost.ProposedStrip(monitor, 220, false);
            Assert.That(AppBarHost.SameRect(first, second), Is.True);
        }

        // ---- Right edge (#177) ------------------------------------------------

        [Test]
        public void ProposedStrip_Right_IsMonitorRightEdgeAtFullHeight()
        {
            var strip = AppBarHost.ProposedStrip(Rect(0, 0, 2560, 1440), 220, true);
            Assert.That(AppBarHost.SameRect(strip, Rect(2340, 0, 2560, 1440)), Is.True);
        }

        [Test]
        public void ProposedStrip_Right_FollowsAMonitorLeftOfPrimary()
        {
            var strip = AppBarHost.ProposedStrip(Rect(-1920, -1080, 0, 0), 300, true);
            Assert.That(AppBarHost.SameRect(strip, Rect(-300, -1080, 0, 0)), Is.True);
        }

        [Test]
        public void ProposedStrip_Right_RepeatedAfterReserving_AsksForTheSameStrip()
        {
            // Same #145 invariant as the left edge: the request never depends on the work area.
            var monitor = Rect(0, 0, 1920, 1080);
            var first = AppBarHost.ProposedStrip(monitor, 220, true);
            var second = AppBarHost.ProposedStrip(monitor, 220, true);
            Assert.That(AppBarHost.SameRect(first, second), Is.True);
        }

        [Test]
        public void AdjustQueriedStrip_Left_KeepsLeftAndRederivesRight()
        {
            var adjusted = AppBarHost.AdjustQueriedStrip(Rect(0, 0, 999, 1040), 220, false);
            Assert.That(AppBarHost.SameRect(adjusted, Rect(0, 0, 220, 1040)), Is.True);
        }

        [Test]
        public void AdjustQueriedStrip_Right_KeepsRightAndRederivesLeft()
        {
            // The shell trims the side facing the screen edge; for a right-edge strip that is Right,
            // and Left must follow it so the strip stays the requested width.
            var untouched = AppBarHost.AdjustQueriedStrip(Rect(1700, 0, 1920, 1040), 220, true);
            Assert.That(AppBarHost.SameRect(untouched, Rect(1700, 0, 1920, 1040)), Is.True);
            var trimmed = AppBarHost.AdjustQueriedStrip(Rect(1700, 0, 1860, 1040), 220, true);
            Assert.That(AppBarHost.SameRect(trimmed, Rect(1640, 0, 1860, 1040)), Is.True);
        }

        [Test]
        public void AutoHideRect_Left_CollapsedIsATriggerStripWithDeadCorners()
        {
            var r = AppBarHost.AutoHideRect(Rect(0, 0, 1920, 1040), 220, false, false);
            Assert.That(AppBarHost.SameRect(r, Rect(0, 46, 4, 994)), Is.True);
        }

        [Test]
        public void AutoHideRect_Right_CollapsedIsATriggerStripWithDeadCorners()
        {
            var r = AppBarHost.AutoHideRect(Rect(0, 0, 1920, 1040), 220, false, true);
            Assert.That(AppBarHost.SameRect(r, Rect(1916, 46, 1920, 994)), Is.True);
        }

        [Test]
        public void AutoHideRect_Left_ExpandedSpansTheWorkAreaFromTheLeftEdge()
        {
            var r = AppBarHost.AutoHideRect(Rect(0, 40, 1920, 1080), 220, true, false);
            Assert.That(AppBarHost.SameRect(r, Rect(0, 40, 220, 1080)), Is.True);
        }

        [Test]
        public void AutoHideRect_Right_ExpandedGrowsLeftwardFromTheRightEdge()
        {
            var r = AppBarHost.AutoHideRect(Rect(100, 40, 2020, 1080), 220, true, true);
            Assert.That(AppBarHost.SameRect(r, Rect(1800, 40, 2020, 1080)), Is.True);
        }

        [Test]
        public void AutoHideRect_ShortWorkArea_KeepsTheFullHeightTrigger()
        {
            var r = AppBarHost.AutoHideRect(Rect(0, 0, 800, 100), 220, false, true);
            Assert.That(AppBarHost.SameRect(r, Rect(796, 0, 800, 100)), Is.True);
        }

        [TestCase("right", true)]
        [TestCase("RIGHT", true)]
        [TestCase(" right ", true)]
        [TestCase("left", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("top", false)]
        public void IsRightEdge_OnlyRightIsRight(string edge, bool expected)
        {
            Assert.That(AppBarHost.IsRightEdge(edge), Is.EqualTo(expected));
        }

        // ---- Edges shared with another display (#177) ---------------------------

        private static MonitorDescriptor Mon(string id, NM.RECT rect)
        {
            return new MonitorDescriptor { Id = id, MonitorRect = rect, WorkRect = rect };
        }

        [Test]
        public void IsEdgeShared_SideBySide_RightEdgeOfLeftMonitorIsShared()
        {
            var left = Mon("L", Rect(0, 0, 1920, 1080));
            var right = Mon("R", Rect(1920, 0, 3840, 1080));
            var all = new List<MonitorDescriptor> { left, right };
            Assert.That(AppBarHost.IsEdgeShared(left, all, true), Is.True);
            Assert.That(AppBarHost.IsEdgeShared(left, all, false), Is.False);
            Assert.That(AppBarHost.IsEdgeShared(right, all, false), Is.True);
            Assert.That(AppBarHost.IsEdgeShared(right, all, true), Is.False);
        }

        [Test]
        public void IsEdgeShared_Stacked_NeitherSideIsShared()
        {
            var top = Mon("T", Rect(0, 0, 1920, 1080));
            var bottom = Mon("B", Rect(0, 1080, 1920, 2160));
            var all = new List<MonitorDescriptor> { top, bottom };
            Assert.That(AppBarHost.IsEdgeShared(top, all, true), Is.False);
            Assert.That(AppBarHost.IsEdgeShared(top, all, false), Is.False);
            Assert.That(AppBarHost.IsEdgeShared(bottom, all, true), Is.False);
            Assert.That(AppBarHost.IsEdgeShared(bottom, all, false), Is.False);
        }

        [Test]
        public void IsEdgeShared_NeighborOnlyPartlyOverlappingInHeight_IsShared()
        {
            var main = Mon("M", Rect(0, 0, 1920, 1080));
            var side = Mon("S", Rect(1920, 900, 3840, 1980)); // starts 180 px above main's bottom
            var all = new List<MonitorDescriptor> { main, side };
            Assert.That(AppBarHost.IsEdgeShared(main, all, true), Is.True);
        }

        [Test]
        public void IsEdgeShared_NeighborTouchingOnlyAtACorner_IsNotShared()
        {
            var main = Mon("M", Rect(0, 0, 1920, 1080));
            var diagonal = Mon("D", Rect(1920, 1080, 3840, 2160));
            var all = new List<MonitorDescriptor> { main, diagonal };
            Assert.That(AppBarHost.IsEdgeShared(main, all, true), Is.False);
        }

        [Test]
        public void IsEdgeShared_GapBetweenMonitors_IsNotShared()
        {
            var main = Mon("M", Rect(0, 0, 1920, 1080));
            var far = Mon("F", Rect(2000, 0, 3920, 1080));
            var all = new List<MonitorDescriptor> { main, far };
            Assert.That(AppBarHost.IsEdgeShared(main, all, true), Is.False);
        }

        [Test]
        public void IsEdgeShared_SingleMonitorOrNoList_IsNotShared()
        {
            var only = Mon("O", Rect(0, 0, 1920, 1080));
            Assert.That(AppBarHost.IsEdgeShared(only, new List<MonitorDescriptor> { only }, true), Is.False);
            Assert.That(AppBarHost.IsEdgeShared(only, new List<MonitorDescriptor> { only }, false), Is.False);
            Assert.That(AppBarHost.IsEdgeShared(only, null, true), Is.False);
        }

        [Test]
        public void IsEdgeShared_DockMonitorWithNullId_IsStillSkippedAsItself()
        {
            var fallback = Mon(null, Rect(0, 0, 1920, 1080));
            Assert.That(AppBarHost.IsEdgeShared(fallback, new List<MonitorDescriptor> { fallback }, true), Is.False);
        }

        [Test]
        public void IsEdgeShared_MonitorLeftOfAndAbovePrimary()
        {
            var secondary = Mon("S", Rect(-1920, -200, 0, 880));
            var primary = Mon("P", Rect(0, 0, 2560, 1440));
            var all = new List<MonitorDescriptor> { secondary, primary };
            Assert.That(AppBarHost.IsEdgeShared(secondary, all, true), Is.True);
            Assert.That(AppBarHost.IsEdgeShared(primary, all, false), Is.True);
            Assert.That(AppBarHost.IsEdgeShared(primary, all, true), Is.False);
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
