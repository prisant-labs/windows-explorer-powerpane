using System.Windows;
using NUnit.Framework;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers where the picker opens, in device pixels. The monitors are invented rectangles, and
    /// nothing here shows a window.
    /// </summary>
    [TestFixture]
    public class PickerPlacementTests
    {
        private static readonly Rect Primary = new Rect(0, 0, 1920, 1040);      // 1080p minus a taskbar
        private static readonly Rect LeftOfPrimary = new Rect(-1280, -100, 1280, 960);
        private static readonly Size Window = new Size(300, 400);

        [Test]
        public void Place_PointerInTheMiddle_PutsTheCornerTheOffsetAboveAndLeft()
        {
            var p = PickerPlacement.Place(new Point(800, 500), Window, Primary, 20);
            Assert.That(p, Is.EqualTo(new Point(780, 480)));
        }

        [Test]
        public void Place_ZeroOffset_PutsTheCornerOnThePointer()
        {
            var p = PickerPlacement.Place(new Point(800, 500), Window, Primary, 0);
            Assert.That(p, Is.EqualTo(new Point(800, 500)));
        }

        [Test]
        public void Place_TopLeftCorner_StaysOnTheWorkArea()
        {
            var p = PickerPlacement.Place(new Point(5, 5), Window, Primary, 20);
            Assert.That(p, Is.EqualTo(new Point(0, 0)));
        }

        [Test]
        public void Place_TopRightCorner_IsPushedLeft()
        {
            var p = PickerPlacement.Place(new Point(1915, 5), Window, Primary, 20);
            Assert.That(p, Is.EqualTo(new Point(1920 - 300, 0)));
        }

        [Test]
        public void Place_BottomLeftCorner_IsPushedUp()
        {
            var p = PickerPlacement.Place(new Point(5, 1035), Window, Primary, 20);
            Assert.That(p, Is.EqualTo(new Point(0, 1040 - 400)));
        }

        [Test]
        public void Place_BottomRightCorner_IsPushedLeftAndUp()
        {
            var p = PickerPlacement.Place(new Point(1919, 1039), Window, Primary, 20);
            Assert.That(p, Is.EqualTo(new Point(1920 - 300, 1040 - 400)));
            Assert.That(p.X + Window.Width, Is.LessThanOrEqualTo(Primary.Right));
            Assert.That(p.Y + Window.Height, Is.LessThanOrEqualTo(Primary.Bottom));
        }

        [Test]
        public void Place_SecondMonitorWithANegativeOrigin_StaysOnThatMonitor()
        {
            // Pointer near the top-left of the monitor to the left of and above the primary one.
            var topLeft = PickerPlacement.Place(new Point(-1275, -95), Window, LeftOfPrimary, 20);
            Assert.That(topLeft, Is.EqualTo(new Point(-1280, -100)));

            // Pointer near its bottom-right corner.
            var bottomRight = PickerPlacement.Place(new Point(-5, 855), Window, LeftOfPrimary, 20);
            Assert.That(bottomRight, Is.EqualTo(new Point(0 - 300, 860 - 400)));
            Assert.That(bottomRight.X, Is.GreaterThanOrEqualTo(LeftOfPrimary.Left));
            Assert.That(bottomRight.X + Window.Width, Is.LessThanOrEqualTo(LeftOfPrimary.Right));
        }

        [Test]
        public void Place_WindowTallerThanTheWorkArea_KeepsItsTopLeftVisible()
        {
            var tall = new Size(300, 1500);
            var p = PickerPlacement.Place(new Point(900, 700), tall, Primary, 20);
            Assert.That(p.Y, Is.EqualTo(Primary.Top));
            Assert.That(p.X, Is.EqualTo(880));
        }

        [Test]
        public void Place_WindowWiderThanTheWorkArea_KeepsItsTopLeftVisible()
        {
            var wide = new Size(2500, 400);
            var p = PickerPlacement.Place(new Point(900, 700), wide, LeftOfPrimary, 20);
            Assert.That(p.X, Is.EqualTo(LeftOfPrimary.Left));
        }

        [Test]
        public void Place_ADifferentSizeAfterTheListsFill_IsPlacedAgainFromTheSamePointer()
        {
            // The window grows from 300 x 250 to 300 x 700 once the tab list fills, near the bottom.
            var pointer = new Point(600, 800);
            var small = PickerPlacement.Place(pointer, new Size(300, 250), Primary, 20);
            var grown = PickerPlacement.Place(pointer, new Size(300, 700), Primary, 20);
            Assert.That(small.Y, Is.EqualTo(780));
            Assert.That(grown.Y, Is.EqualTo(1040 - 700));
        }
    }
}
