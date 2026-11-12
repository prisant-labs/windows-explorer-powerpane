using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers ShouldBeat, which decides when the heartbeat rewrites session.state instead of doing it
    /// every 4 seconds all day (#50). No file is written.
    /// </summary>
    [TestFixture]
    public class CrashGuardTests
    {
        [Test]
        public void ShouldBeat_ActivityChanged_Writes()
        {
            Assert.That(CrashGuard.ShouldBeat(true, 4000), Is.True);
        }

        [Test]
        public void ShouldBeat_IdleUnderAMinute_Skips()
        {
            Assert.That(CrashGuard.ShouldBeat(false, 4000), Is.False);
            Assert.That(CrashGuard.ShouldBeat(false, CrashGuard.IdleHeartbeatMs - 1), Is.False);
        }

        [Test]
        public void ShouldBeat_IdleForAMinute_Writes()
        {
            Assert.That(CrashGuard.ShouldBeat(false, CrashGuard.IdleHeartbeatMs), Is.True);
        }

        [Test]
        public void ShouldBeat_ClockWentBackwards_Writes()
        {
            Assert.That(CrashGuard.ShouldBeat(false, -1), Is.True);
        }
    }
}
