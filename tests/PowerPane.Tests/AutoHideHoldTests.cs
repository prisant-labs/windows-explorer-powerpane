using System.Threading;
using NUnit.Framework;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>The dock's auto-hide guard when a name prompt is opened from a menu entry (#243).</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class AutoHideHoldTests
    {
        [TearDown]
        public void Reset()
        {
            SidebarControl.SuppressAutoHide = false;
            SidebarControl.ReleaseAutoHide();
        }

        [Test]
        public void MenuClosingWhilePromptIsUp_DoesNotReleasePromptsHold_AndPromptEndDoesNotRestoreMenusFlag()
        {
            // The menu's item click runs first, while the menu still holds the flag.
            SidebarControl.SuppressAutoHide = true;
            SidebarControl.HoldAutoHide();

            // The menu's Closed event clears the flag during the prompt's nested message pump.
            SidebarControl.SuppressAutoHide = false;
            Assert.That(SidebarControl.SuppressAutoHide, Is.True, "the dock stays open while the prompt is up");

            // The prompt closes. Nothing is left holding the dock.
            SidebarControl.ReleaseAutoHide();
            Assert.That(SidebarControl.SuppressAutoHide, Is.False, "the dock collapses normally afterwards");
        }

        [Test]
        public void ReleaseWithoutHold_DoesNotGoNegative()
        {
            SidebarControl.ReleaseAutoHide();
            SidebarControl.HoldAutoHide();
            Assert.That(SidebarControl.SuppressAutoHide, Is.True);
            SidebarControl.ReleaseAutoHide();
            Assert.That(SidebarControl.SuppressAutoHide, Is.False);
        }
    }
}
