using System;
using NUnit.Framework;
using PowerPane.Explorer;
using PowerPane.Interop;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers DragYieldActive, the cap that hands a dragged window back to normal layout when the
    /// end-of-drag event is lost, and ResizeBorder.Contains, which tells a resize from a move when a
    /// drag starts. No window or hook is needed.
    /// </summary>
    [TestFixture]
    public class ExplorerWatcherTests
    {
        [Test]
        public void DragYieldActive_WithinCap_IsTrue()
        {
            Assert.That(ExplorerWatcher.DragYieldActive(1000, 1000), Is.True);
            Assert.That(ExplorerWatcher.DragYieldActive(1000, 1000 + ExplorerWatcher.DragYieldCapMs - 1), Is.True);
        }

        [Test]
        public void DragYieldActive_AtOrPastCap_IsFalse()
        {
            Assert.That(ExplorerWatcher.DragYieldActive(1000, 1000 + ExplorerWatcher.DragYieldCapMs), Is.False);
            Assert.That(ExplorerWatcher.DragYieldActive(1000, 1000 + 10 * ExplorerWatcher.DragYieldCapMs), Is.False);
        }

        [Test]
        public void DragYieldActive_AcrossTickCountWrap_StillCountsElapsedTime()
        {
            // Environment.TickCount runs from int.MaxValue to int.MinValue after 24.9 days.
            int start = int.MaxValue - 100;
            Assert.That(ExplorerWatcher.DragYieldActive(start, unchecked(start + 500)), Is.True);
            Assert.That(ExplorerWatcher.DragYieldActive(start, unchecked(start + ExplorerWatcher.DragYieldCapMs + 1)), Is.False);
        }

        private static NativeMethods.RECT Window(int l, int t, int r, int b)
        {
            return new NativeMethods.RECT { Left = l, Top = t, Right = r, Bottom = b };
        }

        private static NativeMethods.POINT At(int x, int y) { return new NativeMethods.POINT { X = x, Y = y }; }

        [TestCase(100, 400, true, TestName = "ResizeBorder.Contains: left edge")]
        [TestCase(107, 400, true, TestName = "ResizeBorder.Contains: last pixel of the left band")]
        [TestCase(108, 400, false, TestName = "ResizeBorder.Contains: just inside the left band")]
        [TestCase(1099, 400, true, TestName = "ResizeBorder.Contains: right edge")]
        [TestCase(600, 799, true, TestName = "ResizeBorder.Contains: bottom edge")]
        [TestCase(600, 203, true, TestName = "ResizeBorder.Contains: top edge")]
        [TestCase(600, 220, false, TestName = "ResizeBorder.Contains: title bar")]
        [TestCase(600, 500, false, TestName = "ResizeBorder.Contains: middle of the window")]
        [TestCase(1100, 400, false, TestName = "ResizeBorder.Contains: just outside the right edge")]
        [TestCase(50, 400, false, TestName = "ResizeBorder.Contains: well outside the window")]
        public void ResizeBorderContains_ClassifiesThePress(int x, int y, bool expected)
        {
            Assert.That(ResizeBorder.Contains(Window(100, 200, 1100, 800), At(x, y), 8), Is.EqualTo(expected));
        }

        [Test]
        public void DragYieldActive_ClockBeforeStart_IsFalse()
        {
            // A start time in the future means the state is stale; never yield on it.
            Assert.That(ExplorerWatcher.DragYieldActive(5000, 4000), Is.False);
        }
    }
}
