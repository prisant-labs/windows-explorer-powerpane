using NUnit.Framework;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>Where a name prompt lands next to the control that opened it (#243). Pure
    /// rectangle arithmetic, so no window is made.</summary>
    [TestFixture]
    public class PromptPlacementTests
    {
        private static readonly PixelRect Work = new PixelRect(0, 0, 1920, 1040);

        private static bool Inside(PixelRect r, PixelRect work)
        {
            return r.Left >= work.Left && r.Top >= work.Top && r.Right <= work.Right && r.Bottom <= work.Bottom;
        }

        [Test]
        public void Normal_SitsBelowTheAnchorAndLeftAligned()
        {
            var anchor = PixelRect.FromSize(200, 300, 24, 20);
            var r = PromptPlacement.Compute(anchor, 320, 150, Work, 4);

            Assert.That(r.Left, Is.EqualTo(200));
            Assert.That(r.Top, Is.EqualTo(324));
            Assert.That(r.Width, Is.EqualTo(320));
            Assert.That(r.Height, Is.EqualTo(150));
        }

        [Test]
        public void NoRoomBelow_FlipsAboveTheAnchor()
        {
            var anchor = PixelRect.FromSize(200, 950, 24, 20);
            var r = PromptPlacement.Compute(anchor, 320, 150, Work, 4);

            Assert.That(r.Bottom, Is.EqualTo(950 - 4));
            Assert.That(r.Top, Is.EqualTo(950 - 4 - 150));
        }

        [Test]
        public void NeitherFits_ClampsTheTopIntoTheWorkArea()
        {
            var shortWork = new PixelRect(0, 0, 1920, 200);
            var anchor = PixelRect.FromSize(100, 90, 24, 20);
            var r = PromptPlacement.Compute(anchor, 320, 150, shortWork, 4);

            Assert.That(Inside(r, shortWork), Is.True, r.ToString());
        }

        [Test]
        public void RightEdgeDock_ClampsTheLeftEdge()
        {
            var anchor = PixelRect.FromSize(1880, 300, 24, 20);
            var r = PromptPlacement.Compute(anchor, 320, 150, Work, 4);

            Assert.That(r.Right, Is.EqualTo(Work.Right));
            Assert.That(r.Left, Is.EqualTo(1600));
        }

        [Test]
        public void LeftDock_KeepsTheAnchorsLeftEdge()
        {
            var anchor = PixelRect.FromSize(0, 300, 24, 20);
            var r = PromptPlacement.Compute(anchor, 320, 150, Work, 4);

            Assert.That(r.Left, Is.EqualTo(0));
        }

        [Test]
        public void SecondMonitorWithNegativeOrigin_StaysOnThatMonitor()
        {
            var left = new PixelRect(-1920, -200, 0, 880);
            var anchor = PixelRect.FromSize(-1900, 800, 24, 20);
            var r = PromptPlacement.Compute(anchor, 320, 150, left, 4);

            Assert.That(Inside(r, left), Is.True, r.ToString());
            Assert.That(r.Left, Is.EqualTo(-1900));
            Assert.That(r.Bottom, Is.EqualTo(800 - 4), "no room below, so it flips above");
        }

        [Test]
        public void SecondMonitorToTheRight_StaysOnThatMonitor()
        {
            var right = new PixelRect(1920, 0, 3840, 1040);
            var anchor = PixelRect.FromSize(3830, 300, 24, 20);
            var r = PromptPlacement.Compute(anchor, 320, 150, right, 4);

            Assert.That(Inside(r, right), Is.True, r.ToString());
        }

        [Test]
        public void PromptLargerThanTheWorkArea_PinsToTheOriginOnThatAxis()
        {
            var tiny = new PixelRect(100, 50, 300, 150);
            var anchor = PixelRect.FromSize(150, 60, 24, 20);
            var r = PromptPlacement.Compute(anchor, 320, 150, tiny, 4);

            Assert.That(r.Left, Is.EqualTo(100));
            Assert.That(r.Top, Is.EqualTo(50));
        }

        [TestCase(96, 320)]
        [TestCase(144, 480)]
        [TestCase(192, 640)]
        public void ScaleDip_GivesPixelsAtEachScale(int dpi, int expected)
        {
            Assert.That(PromptPlacement.ScaleDip(320, dpi), Is.EqualTo(expected));
        }

        [TestCase(96)]
        [TestCase(144)]
        [TestCase(192)]
        public void EveryScale_StaysInsideTheWorkAreaForAnchorsAroundTheEdges(int dpi)
        {
            int w = PromptPlacement.ScaleDip(320, dpi);
            int h = PromptPlacement.ScaleDip(150, dpi);
            foreach (int x in new[] { 0, 500, 1900 })
                foreach (int y in new[] { 0, 500, 1020 })
                {
                    var anchor = PixelRect.FromSize(x, y, 24, 20);
                    var r = PromptPlacement.Compute(anchor, w, h, Work, PromptPlacement.ScaleDip(4, dpi));
                    Assert.That(Inside(r, Work), Is.True, "anchor " + anchor + " at " + dpi + " dpi gave " + r);
                }
        }
    }
}
