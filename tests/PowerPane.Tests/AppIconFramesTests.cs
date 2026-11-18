using NUnit.Framework;
using PowerPane.UI;
using PowerPane.UI.Settings.Pages;

namespace PowerPane.Tests
{
    /// <summary>Covers the frame choice behind the Settings window's title icon (#271), and the About
    /// page's account of where PowerPane came from (#273).</summary>
    [TestFixture]
    public class AppIconFramesTests
    {
        // The frames powerpane.ico holds today, in file order.
        private static readonly int[] Frames = { 16, 24, 32, 48, 64, 72, 96, 128, 256 };

        [TestCase(16, 16)]    // 100 % scale: the 16 px frame itself, never the 32 px one scaled down
        [TestCase(20, 24)]    // 125 %: the next frame up, scaled down a little
        [TestCase(24, 24)]    // 150 %
        [TestCase(32, 32)]    // 200 %
        [TestCase(40, 48)]    // 250 %
        [TestCase(1, 16)]
        public void PickIndex_TakesTheSmallestFrameAtLeastTheTargetSize(int targetPx, int expectedWidth)
        {
            int i = AppIconFrames.PickIndex(Frames, targetPx);
            Assert.That(Frames[i], Is.EqualTo(expectedWidth));
        }

        [Test]
        public void PickIndex_LargerThanEveryFrame_TakesTheLargest()
        {
            Assert.That(Frames[AppIconFrames.PickIndex(Frames, 400)], Is.EqualTo(256));
        }

        [Test]
        public void PickIndex_FramesInAnyOrder()
        {
            var shuffled = new[] { 256, 32, 16, 48, 24 };
            Assert.That(shuffled[AppIconFrames.PickIndex(shuffled, 20)], Is.EqualTo(24));
        }

        [Test]
        public void PickIndex_NoFrames_IsMinusOne()
        {
            Assert.That(AppIconFrames.PickIndex(new int[0], 16), Is.EqualTo(-1));
            Assert.That(AppIconFrames.PickIndex(null, 16), Is.EqualTo(-1));
        }

        [Test]
        public void AboutPage_SaysPowerPaneIsAForkOfQuickPane_AndWhoMadeIt()
        {
            Assert.That(AboutPage.LicenceText, Does.Contain("fork of QuickPane"));
            Assert.That(AboutPage.LicenceText, Does.Contain("PlexPixel"));
            Assert.That(AboutPage.LicenceText, Does.Contain("MIT License"));
            Assert.That(AboutPage.SupportAuthorDescription, Does.Contain("QuickPane"));
            Assert.That(AboutPage.SupportAuthorDescription, Does.Contain("PlexPixel"));
        }
    }
}
