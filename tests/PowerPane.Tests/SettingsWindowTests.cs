using System.Windows;
using NUnit.Framework;
using PowerPane.UI.Settings;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.Tests
{
    /// <summary>The one layout decision in SettingsWindow.cs that can be tested with no window at
    /// all: how much margin compensates for the resize-frame overhang WindowChrome leaves when
    /// maximized. Every testable decision lives in an internal static method.</summary>
    [TestFixture]
    public class SettingsWindowTests
    {
        [Test]
        public void RootMarginFor_Maximized_UsesResizeBorderThickness()
        {
            var border = new Thickness(7);
            Assert.That(SettingsWindow.RootMarginFor(WindowState.Maximized, border), Is.EqualTo(border));
        }

        [TestCase(WindowState.Normal)]
        [TestCase(WindowState.Minimized)]
        public void RootMarginFor_NotMaximized_IsZero(WindowState state)
        {
            var border = new Thickness(7);
            Assert.That(SettingsWindow.RootMarginFor(state, border), Is.EqualTo(new Thickness(0)));
        }

        [Test]
        public void RootMarginFor_MaximizedWithoutChrome_IsZero()
        {
            // High contrast or no Mica: a standard frame, whose client area is inside the monitor.
            Assert.That(SettingsWindow.RootMarginFor(WindowState.Maximized, new Thickness(8), false), Is.EqualTo(new Thickness(0)));
        }

        // #276: the icon and title centre on the caption buttons' band, not on the 48 px row.

        [Test]
        public void TitleBand_At100Percent_IsTheButtonsBand()
        {
            double top, height;
            SettingsWindow.TitleBand(1, 31, 1.0, 0, 48, out top, out height);
            Assert.That(top, Is.EqualTo(1));
            Assert.That(height, Is.EqualTo(30));
        }

        [Test]
        public void TitleBand_At150Percent_ConvertsToDips()
        {
            double top, height;
            SettingsWindow.TitleBand(0, 45, 1.5, 0, 48, out top, out height);
            Assert.That(top, Is.EqualTo(0));
            Assert.That(height, Is.EqualTo(30));
        }

        [Test]
        public void TitleBand_Maximized_SubtractsTheRootMargin()
        {
            // Maximized, the window starts 7 px above the monitor and the root grid is shifted down
            // by the same 7, so buttons at 7..37 in the window sit at 0..30 in the row.
            double top, height;
            SettingsWindow.TitleBand(7, 37, 1.0, 7, 48, out top, out height);
            Assert.That(top, Is.EqualTo(0));
            Assert.That(height, Is.EqualTo(30));
        }

        [Test]
        public void TitleBand_BandAboveTheRow_IsClippedToTheRow()
        {
            double top, height;
            SettingsWindow.TitleBand(0, 30, 1.0, 7, 48, out top, out height);
            Assert.That(top, Is.EqualTo(0));
            Assert.That(height, Is.EqualTo(23));
        }

        [Test]
        public void TitleBand_TallerThanTheRow_IsCappedAtTheRow()
        {
            double top, height;
            SettingsWindow.TitleBand(0, 80, 1.0, 0, 48, out top, out height);
            Assert.That(top, Is.EqualTo(0));
            Assert.That(height, Is.EqualTo(48));
        }

        [TestCase(0, 0)]      // DWM gave nothing
        [TestCase(30, 10)]    // an inverted rectangle
        [TestCase(0, 12)]     // too short to hold the 16 px icon
        public void TitleBand_UnusableBounds_FallBackToTheStandardTitleBar(int topPx, int bottomPx)
        {
            double top, height;
            SettingsWindow.TitleBand(topPx, bottomPx, 1.0, 0, 48, out top, out height);
            Assert.That(top, Is.EqualTo(0));
            Assert.That(height, Is.EqualTo(32));
        }

        [Test]
        public void TitleBand_ZeroScale_IsTreatedAsOne()
        {
            double top, height;
            SettingsWindow.TitleBand(2, 32, 0, 0, 48, out top, out height);
            Assert.That(top, Is.EqualTo(2));
            Assert.That(height, Is.EqualTo(30));
        }

        private static NM.RECT Rect(int l, int t, int r, int b)
        {
            return new NM.RECT { Left = l, Top = t, Right = r, Bottom = b };
        }

        [Test]
        public void OverhangFor_MaximizedAt100Percent_IsTheEightPixelFrame()
        {
            // Measured in Windows Sandbox: the maximized window rectangle starts at -8,-8 on a
            // 3056 x 1687 monitor while the visible frame is the monitor itself.
            var o = SettingsWindow.OverhangFor(Rect(-8, -8, 3064, 1695), Rect(0, 0, 3056, 1687), 1.0);
            Assert.That(o, Is.EqualTo(new Thickness(8)));
        }

        [Test]
        public void OverhangFor_At150Percent_ConvertsToDips()
        {
            var o = SettingsWindow.OverhangFor(Rect(-12, -12, 1932, 1092), Rect(0, 0, 1920, 1080), 1.5);
            Assert.That(o, Is.EqualTo(new Thickness(8)));
        }

        [Test]
        public void OverhangFor_EmptyRectangle_IsZero()
        {
            Assert.That(SettingsWindow.OverhangFor(Rect(0, 0, 0, 0), Rect(0, 0, 100, 100), 1.0), Is.EqualTo(new Thickness(0)));
            Assert.That(SettingsWindow.OverhangFor(Rect(0, 0, 100, 100), Rect(0, 0, 0, 0), 1.0), Is.EqualTo(new Thickness(0)));
        }

        [Test]
        public void OverhangFor_VisibleFrameOutsideTheWindow_IsNeverNegative()
        {
            var o = SettingsWindow.OverhangFor(Rect(10, 10, 90, 90), Rect(0, 0, 100, 100), 1.0);
            Assert.That(o, Is.EqualTo(new Thickness(0)));
        }

        // #97: the two timing log lines are the only part of the instrumentation that
        // is a pure decision, so they are the only part covered here; the Stopwatch calls around
        // them need a real window and are left to the sandbox's timing measurements.

        [Test]
        public void FormatOpenTiming_IncludesElapsedMilliseconds()
        {
            Assert.That(SettingsWindow.FormatOpenTiming(123), Is.EqualTo("SettingsWindow: opened in 123 ms"));
        }

        [Test]
        public void FormatPageSwitchTiming_IncludesPageIdAndElapsedMilliseconds()
        {
            Assert.That(SettingsWindow.FormatPageSwitchTiming("groups", 45),
                Is.EqualTo("SettingsWindow: switched to page 'groups' in 45 ms"));
        }
    }
}
