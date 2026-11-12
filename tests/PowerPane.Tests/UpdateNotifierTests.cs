using System;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>Pins the two decisions behind the automatic update notice (#176): when a check is due,
    /// and when a found version deserves its one notice.</summary>
    [TestFixture]
    public class UpdateNotifierTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

        private static string Ago(double hours)
        {
            return UpdateNotifier.FormatTime(Now.AddHours(-hours));
        }

        [Test]
        public void IsCheckDue_NeverChecked_IsDue()
        {
            Assert.That(UpdateNotifier.IsCheckDue(false, null, Now), Is.True);
        }

        [Test]
        public void IsCheckDue_Off_IsNeverDue()
        {
            Assert.That(UpdateNotifier.IsCheckDue(true, null, Now), Is.False);
            Assert.That(UpdateNotifier.IsCheckDue(true, Ago(100), Now), Is.False);
        }

        [TestCase(1, false)]
        [TestCase(23.9, false)]
        [TestCase(24, true)]
        [TestCase(100, true)]
        public void IsCheckDue_FollowsTheDailyInterval(double hoursAgo, bool expected)
        {
            Assert.That(UpdateNotifier.IsCheckDue(false, Ago(hoursAgo), Now), Is.EqualTo(expected));
        }

        [Test]
        public void IsCheckDue_UnreadableTime_IsDue()
        {
            Assert.That(UpdateNotifier.IsCheckDue(false, "yesterday-ish", Now), Is.True);
        }

        [Test]
        public void IsCheckDue_TimeInTheFuture_IsDue()
        {
            // A clock that moved back must not silence the check until the clock catches up.
            Assert.That(UpdateNotifier.IsCheckDue(false, Ago(-48), Now), Is.True);
        }

        [Test]
        public void ShouldNotify_NewerThanRunningAndNeverNoticed_IsTrue()
        {
            Assert.That(UpdateNotifier.ShouldNotify(new Version(3, 12, 0), new Version(3, 11, 0, 2), null), Is.True);
        }

        [Test]
        public void ShouldNotify_NotNewerThanRunning_IsFalse()
        {
            Assert.That(UpdateNotifier.ShouldNotify(new Version(3, 11, 0, 2), new Version(3, 11, 0, 2), null), Is.False);
            Assert.That(UpdateNotifier.ShouldNotify(new Version(3, 10, 0), new Version(3, 11, 0, 2), null), Is.False);
        }

        [Test]
        public void ShouldNotify_AlreadyNoticed_IsFalse()
        {
            Assert.That(UpdateNotifier.ShouldNotify(new Version(3, 12, 0), new Version(3, 11, 0), "3.12.0"), Is.False);
        }

        [Test]
        public void ShouldNotify_AZeroRevisionReleaseMatchesItsDisplayedRecord()
        {
            // The notice stores the display form, which drops the zero, so this repeated every day.
            var latest = new Version(3, 12, 0, 0);
            Assert.That(UpdateService.Display(latest), Is.EqualTo("3.12.0"));
            Assert.That(UpdateNotifier.ShouldNotify(latest, new Version(3, 11, 0, 2), "3.12.0"), Is.False);
        }

        [Test]
        public void ShouldNotify_AThreePartTagIsNotNewerThanTheSameRunningBuild()
        {
            Assert.That(UpdateNotifier.ShouldNotify(new Version(3, 12, 0), new Version(3, 12, 0, 0), null), Is.False);
        }

        [Test]
        public void ShouldNotify_NewerThanTheLastNotice_IsTrue()
        {
            Assert.That(UpdateNotifier.ShouldNotify(new Version(3, 12, 0, 1), new Version(3, 11, 0), "3.12.0"), Is.True);
        }

        [Test]
        public void ShouldNotify_UnreadableLastNotice_IsTrue()
        {
            Assert.That(UpdateNotifier.ShouldNotify(new Version(3, 12, 0), new Version(3, 11, 0), "not a version"), Is.True);
        }

        [Test]
        public void NoticeTitle_KeepsAForkBuildsFourthPart()
        {
            Assert.That(UpdateNotifier.NoticeTitle(new Version(3, 11, 0, 3)), Is.EqualTo("PowerPane 3.11.0.3 is available"));
        }

        [Test]
        public void Settings_AutomaticCheckIsOnByDefault()
        {
            Assert.That(AppSettings.CreateDefault().UpdateCheckOff, Is.False);
        }
    }
}
