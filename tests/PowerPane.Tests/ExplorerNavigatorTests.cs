using NUnit.Framework;
using PowerPane.Interop;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers PickActiveTab, the pure choice ExplorerNavigator makes when several shell windows share
    /// one top-level HWND because the Explorer window has Windows 11 tabs (#35). The handles are
    /// invented numbers; no Explorer window is needed.
    /// </summary>
    [TestFixture]
    public class ExplorerNavigatorTests
    {
        [Test]
        public void PickActiveTab_ActiveTabIsNotFirst_PicksTheActiveTab()
        {
            // The bug: the second tab was in front, but the first entry was navigated.
            Assert.That(ExplorerNavigator.PickActiveTab(new long[] { 0x100, 0x200, 0x300 }, 0x200), Is.EqualTo(1));
        }

        [Test]
        public void PickActiveTab_ActiveTabIsLast_PicksTheLast()
        {
            Assert.That(ExplorerNavigator.PickActiveTab(new long[] { 0x100, 0x200, 0x300 }, 0x300), Is.EqualTo(2));
        }

        [Test]
        public void HasAtMostOneTab_NoSecondTab_StopsAtFirstMatch()
        {
            // A single-tab window, or a Windows 10 window, needs only the first matching entry (#40).
            Assert.That(ExplorerNavigator.HasAtMostOneTab(0), Is.True);
        }

        [Test]
        public void HasAtMostOneTab_SecondTabPresent_ReadsTheWholeList()
        {
            Assert.That(ExplorerNavigator.HasAtMostOneTab(0x200), Is.False);
        }

        [Test]
        public void PickActiveTab_SingleWindow_PicksIt()
        {
            Assert.That(ExplorerNavigator.PickActiveTab(new long[] { 0x100 }, 0x100), Is.EqualTo(0));
        }

        [Test]
        public void PickActiveTab_ActiveTabUnknown_FallsBackToFirst()
        {
            // FindWindowEx found no ShellTabWindowClass child, for example while a tab is closing.
            Assert.That(ExplorerNavigator.PickActiveTab(new long[] { 0x100, 0x200 }, 0), Is.EqualTo(0));
        }

        [Test]
        public void PickActiveTab_NoTabMatches_FallsBackToFirst()
        {
            // The active tab opened after the shell windows were listed.
            Assert.That(ExplorerNavigator.PickActiveTab(new long[] { 0x100, 0x200 }, 0x999), Is.EqualTo(0));
        }

        [Test]
        public void PickActiveTab_EveryTabLookupFailed_FallsBackToFirst()
        {
            // A failed IShellBrowser lookup reports 0, which must never match a real active tab.
            Assert.That(ExplorerNavigator.PickActiveTab(new long[] { 0, 0 }, 0x200), Is.EqualTo(0));
        }

        [Test]
        public void PickActiveTab_OneTabLookupFailed_StillFindsTheActiveTab()
        {
            Assert.That(ExplorerNavigator.PickActiveTab(new long[] { 0, 0x200 }, 0x200), Is.EqualTo(1));
        }

        [Test]
        public void PickActiveTab_EmptyOrNull_ReturnsMinusOne()
        {
            Assert.That(ExplorerNavigator.PickActiveTab(new long[0], 0x100), Is.EqualTo(-1));
            Assert.That(ExplorerNavigator.PickActiveTab(null, 0x100), Is.EqualTo(-1));
        }
    }
}
