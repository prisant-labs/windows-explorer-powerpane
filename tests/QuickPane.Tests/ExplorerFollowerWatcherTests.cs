using NUnit.Framework;
using QuickPane.Explorer;

namespace QuickPane.Tests
{
    /// <summary>
    /// Covers FollowerPane.DragTimedOut, the pure decision behind the beside-pane drag safety net
    /// (prisant-labs/windows-explorer-quickpane#147). A beside pane hides for the length of a drag of
    /// the window it follows and reappears once MOVESIZEEND arrives; this is the fallback for a drag
    /// whose end event never does. No window, no timer, and no live mouse state are needed for any of
    /// this.
    /// </summary>
    [TestFixture]
    public class ExplorerFollowerWatcherTests
    {
        [Test]
        public void NotDragging_NeverTimesOut()
        {
            // Even past the cap, with the button up, a pane that was never marked as dragging has
            // nothing to clear.
            Assert.That(FollowerPane.DragTimedOut(dragging: false, elapsedMs: 10000, leftButtonDown: false, capMs: 4000), Is.False);
        }

        [Test]
        public void Dragging_BeforeCap_NeverTimesOut()
        {
            // A drag still well within its first few seconds is a normal drag, button down or not.
            Assert.That(FollowerPane.DragTimedOut(dragging: true, elapsedMs: 1000, leftButtonDown: true, capMs: 4000), Is.False);
            Assert.That(FollowerPane.DragTimedOut(dragging: true, elapsedMs: 1000, leftButtonDown: false, capMs: 4000), Is.False);
        }

        [Test]
        public void Dragging_PastCap_ButtonStillDown_NeverTimesOut()
        {
            // A long but genuine drag must not be cut short just because it ran past the cap; only the
            // button coming up says the drag is actually over.
            Assert.That(FollowerPane.DragTimedOut(dragging: true, elapsedMs: 60000, leftButtonDown: true, capMs: 4000), Is.False);
        }

        [Test]
        public void Dragging_PastCap_ButtonUp_TimesOut()
        {
            // The MOVESIZEEND that should have cleared the flag never arrived, the cap has elapsed, and
            // the button is no longer down: treat the drag as over.
            Assert.That(FollowerPane.DragTimedOut(dragging: true, elapsedMs: 4000, leftButtonDown: false, capMs: 4000), Is.True);
        }

        [Test]
        public void Dragging_RightAtCap_ButtonUp_TimesOut()
        {
            // The boundary itself counts as expired (">=" in the implementation), not just values past it.
            Assert.That(FollowerPane.DragTimedOut(dragging: true, elapsedMs: 4000, leftButtonDown: false, capMs: 4000), Is.True);
            Assert.That(FollowerPane.DragTimedOut(dragging: true, elapsedMs: 3999, leftButtonDown: false, capMs: 4000), Is.False);
        }
    }
}
