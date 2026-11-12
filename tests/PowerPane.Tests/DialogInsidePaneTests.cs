using NUnit.Framework;
using PowerPane.Explorer;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers the two size decisions behind the inside pane in a file dialog: fitting a child control
    /// back inside its shifted parent (#238), and the pane width a dialog can give when the user drags
    /// the pane's grip (#239). Neither needs a window.
    /// </summary>
    [TestFixture]
    public class DialogInsidePaneTests
    {
        [Test]
        public void FittedWidth_BandContainerWiderThanItsShiftedParent_EndsAtTheParentsEdge()
        {
            // Measured in Windows Sandbox: the top bar's container is 964 px wide after the shift, and
            // the band container inside it kept the 1184 px it took when the dialog was 220 px wider.
            Assert.That(DialogInsidePane.FittedWidth(228, 228 + 1184, 228, 228 + 964), Is.EqualTo(964));
        }

        [Test]
        public void FittedWidth_ChildThatFits_KeepsItsWidth()
        {
            Assert.That(DialogInsidePane.FittedWidth(300, 900, 228, 1192), Is.EqualTo(600));
            Assert.That(DialogInsidePane.FittedWidth(228, 1192, 228, 1192), Is.EqualTo(964));
        }

        [Test]
        public void FittedWidth_ChildStartingOutsideItsParent_IsLeftAlone()
        {
            Assert.That(DialogInsidePane.FittedWidth(100, 1400, 228, 1192), Is.EqualTo(1300));
            Assert.That(DialogInsidePane.FittedWidth(1200, 1400, 228, 1192), Is.EqualTo(200));
        }

        [Test]
        public void FitWidth_RoomToSpare_TakesTheRequestedWidth()
        {
            Assert.That(DialogInsidePane.FitWidth(300, 1200, 600), Is.EqualTo(300));
        }

        [Test]
        public void FitWidth_RequestOutsideThePaneLimits_IsClampedTo160Through400()
        {
            Assert.That(DialogInsidePane.FitWidth(100, 1600, 600), Is.EqualTo(160));
            Assert.That(DialogInsidePane.FitWidth(900, 1600, 600), Is.EqualTo(400));
        }

        [Test]
        public void FitWidth_RequestThatWouldSqueezeTheContent_IsCutToWhatFits()
        {
            // An 1000 px dialog that must keep 600 px of content can give the pane 400 at most, and
            // a 900 px one only 300.
            Assert.That(DialogInsidePane.FitWidth(400, 1000, 600), Is.EqualTo(400));
            Assert.That(DialogInsidePane.FitWidth(400, 900, 600), Is.EqualTo(300));
        }

        [Test]
        public void WidthFromCursor_FollowsTheCursorDistance_NotASumOfReports()
        {
            // An 80 px drag from 220 px asks for 300 px, however many move reports it took. Adding up a
            // still thumb's reports used to reach the 400 px limit instead (#239).
            Assert.That(PowerPane.UI.SidebarControl.WidthFromCursor(220, 500, 580, false), Is.EqualTo(300));
            Assert.That(PowerPane.UI.SidebarControl.WidthFromCursor(300, 580, 500, false), Is.EqualTo(220));
        }

        [Test]
        public void WidthFromCursor_LeftGrip_WidensWhenDraggedLeft()
        {
            Assert.That(PowerPane.UI.SidebarControl.WidthFromCursor(220, 1000, 940, true), Is.EqualTo(280));
        }

        [Test]
        public void FitWidth_DialogTooNarrowForAnyPane_StillReturnsThePaneMinimum()
        {
            Assert.That(DialogInsidePane.FitWidth(300, 700, 600), Is.EqualTo(160));
        }
    }
}
