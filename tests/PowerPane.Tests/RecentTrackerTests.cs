using NUnit.Framework;
using PowerPane.Explorer;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers ShouldQuery, the gate that lets a Recent poll tick skip the cross-process COM query when
    /// the Explorer window in front has not visibly changed (#39). No window is needed.
    /// </summary>
    [TestFixture]
    public class RecentTrackerTests
    {
        [Test]
        public void ShouldQuery_NothingChanged_Skips()
        {
            Assert.That(RecentTracker.ShouldQuery(true, true, true, 2000), Is.False);
        }

        [Test]
        public void ShouldQuery_TitleChanged_Queries()
        {
            // An in-window navigation changes the title to the new folder's name.
            Assert.That(RecentTracker.ShouldQuery(true, true, false, 2000), Is.True);
        }

        [Test]
        public void ShouldQuery_FrontTabChanged_Queries()
        {
            // Switching tabs can keep the title, for example two tabs on folders with the same name.
            Assert.That(RecentTracker.ShouldQuery(true, false, true, 2000), Is.True);
        }

        [Test]
        public void ShouldQuery_OtherWindow_Queries()
        {
            Assert.That(RecentTracker.ShouldQuery(false, true, true, 2000), Is.True);
        }

        [Test]
        public void ShouldQuery_RefreshDue_Queries()
        {
            // Catches a move between two folders with the same name, which leaves the title unchanged.
            Assert.That(RecentTracker.ShouldQuery(true, true, true, RecentTracker.RefreshMs), Is.True);
        }

        [Test]
        public void ShouldQuery_ClockWentBackwards_Queries()
        {
            Assert.That(RecentTracker.ShouldQuery(true, true, true, -1), Is.True);
        }
    }
}
