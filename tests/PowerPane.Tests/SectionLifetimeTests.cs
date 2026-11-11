using System;
using System.Reflection;
using System.Threading;
using System.Windows.Threading;
using NUnit.Framework;
using PowerPane.Services;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>
    /// A detached section must stay detached (#69). A rebuild queued before its pane closed used to run
    /// afterwards and subscribe the section to the static PathStatus.Changed event again, so it stayed
    /// in memory and rebuilt on every later change. The sections are built but never shown, and no
    /// Application starts, so App.Groups and App.Recents are null and Build stops after wiring.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class SectionLifetimeTests
    {
        private static bool SubscribedToPathStatus(object section)
        {
            var field = typeof(PathStatus).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Static);
            var handlers = field.GetValue(null) as Delegate;
            if (handlers == null) return false;
            foreach (var d in handlers.GetInvocationList())
                if (ReferenceEquals(d.Target, section)) return true;
            return false;
        }

        private static void QueueRebuild(object section)
        {
            section.GetType().GetMethod("QueueRebuild", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(section, null);
        }

        // Runs everything queued at Background priority, which is where the sections queue rebuilds.
        private static void PumpUntilIdle()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private static readonly Action<string> Navigate = path => { };

        [Test]
        public void GroupSection_QueuedRebuildAfterDetach_DoesNotResubscribe()
        {
            var section = new GroupSection();
            section.Build(Navigate);
            Assert.That(SubscribedToPathStatus(section), Is.True);

            QueueRebuild(section);
            section.Detach();
            PumpUntilIdle();

            Assert.That(SubscribedToPathStatus(section), Is.False);
        }

        [Test]
        public void RecentsSection_QueuedRebuildAfterDetach_DoesNotResubscribe()
        {
            var section = new RecentsSection();
            section.Build(Navigate);
            Assert.That(SubscribedToPathStatus(section), Is.True);

            QueueRebuild(section);
            section.Detach();
            PumpUntilIdle();

            Assert.That(SubscribedToPathStatus(section), Is.False);
        }

        [Test]
        public void DetachedSections_IgnoreALaterBuild()
        {
            var groups = new GroupSection();
            var recents = new RecentsSection();
            groups.Detach();
            recents.Detach();

            groups.Build(Navigate);
            recents.Build(Navigate);

            Assert.That(SubscribedToPathStatus(groups), Is.False);
            Assert.That(SubscribedToPathStatus(recents), Is.False);
        }
    }
}
