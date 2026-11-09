using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using QuickPane.Services;

namespace QuickPane.Tests
{
    /// <summary>
    /// Covers PerfTrace, the EventSource that marks lane jobs, pane builds, and cross-process calls
    /// for a performance trace. A malformed EventSource fails silently at run time, so the manifest
    /// is generated strictly here. The payload rule matters because a trace may be shared for help.
    /// </summary>
    [TestFixture]
    public class PerfTraceTests
    {
        private const int Timeout = 5000;

        private sealed class Collector : EventListener
        {
            public readonly List<EventWrittenEventArgs> Events = new List<EventWrittenEventArgs>();

            protected override void OnEventWritten(EventWrittenEventArgs e)
            {
                lock (Events) Events.Add(e);
            }

            public List<EventWrittenEventArgs> Snapshot()
            {
                lock (Events) return Events.ToList();
            }
        }

        [Test]
        public void Manifest_IsValid()
        {
            // Strict mode throws on a mismatched id, a wrong WriteEvent argument count, or a bad
            // task or opcode, any of which would otherwise drop the event without a word.
            var manifest = EventSource.GenerateManifest(typeof(PerfTrace), "QuickPane.exe", EventManifestOptions.Strict);
            Assert.That(manifest, Is.Not.Null.And.Contains("QuickPane"));
        }

        [Test]
        public void ProviderName_IsQuickPane()
        {
            Assert.That(EventSource.GetName(typeof(PerfTrace)), Is.EqualTo("QuickPane"));
        }

        [Test]
        public void NoEvent_TakesAPathOrName()
        {
            // Payloads carry kinds, counts, and durations. The only string parameters are a lane's
            // fixed thread name and a call's fixed kind, never a folder or file name.
            var allowedStrings = new[] { "lane", "kind" };
            var events = typeof(PerfTrace).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<EventAttribute>() != null)
                .ToList();
            Assert.That(events, Is.Not.Empty);
            foreach (var m in events)
                foreach (var p in m.GetParameters().Where(p => p.ParameterType == typeof(string)))
                    Assert.That(allowedStrings, Does.Contain(p.Name), m.Name + "." + p.Name);
        }

        [Test]
        public void ProviderGuid_MatchesTheRecordingProfile()
        {
            // scripts/trace/QuickPane.wprp names the provider by this GUID, because WPR takes a GUID
            // or a registered name, and an EventSource is not registered. The GUID is derived from the
            // name, so renaming the provider breaks the profile, and this test says so.
            Assert.That(EventSource.GetGuid(typeof(PerfTrace)), Is.EqualTo(new Guid("c52f37e7-4bdd-5f3c-332f-56c59bf9dc07")));
        }

        [Test]
        public void End_WithAZeroStart_WritesNothing()
        {
            // A Begin returns 0 when no trace was listening, so a trace that starts mid-call must not
            // get a stop with no start and a duration measured from 0.
            using (var listener = new Collector())
            {
                listener.EnableEvents(PerfTrace.Instance, EventLevel.Verbose);
                PerfTrace.ShellCallEnd("ZeroStart", 0, true);
                PerfTrace.PaneBuildEnd(7, 0);
                PerfTrace.GroupBuildEnd(7, 0);
                Assert.That(listener.Snapshot().Where(e => e.EventName != "EventSourceMessage"), Is.Empty);
            }
        }

        [Test]
        public void LaneJob_WritesStartAndStopWithTheLaneName()
        {
            using (var listener = new Collector())
            {
                listener.EnableEvents(PerfTrace.Instance, EventLevel.Verbose);
                var lane = new WorkLane("trace-test-lane", (m, ex) => { }, m => { });
                var done = new ManualResetEventSlim();
                lane.Post(() => { });
                lane.Post(() => { throw new InvalidOperationException("expected"); });
                lane.Post(done.Set);
                Assert.That(done.Wait(Timeout), Is.True);
                // The stop for the last job is written after done.Set runs, so give it a moment.
                SpinWait.SpinUntil(() => listener.Snapshot().Count(e => e.EventName == "LaneJobStop"
                    && (string)e.Payload[0] == "trace-test-lane") >= 3, Timeout);

                var mine = listener.Snapshot().Where(e => e.Payload.Count > 0 && (e.Payload[0] as string) == "trace-test-lane").ToList();
                Assert.That(mine.Count(e => e.EventName == "LaneJobStart"), Is.EqualTo(3));
                var stops = mine.Where(e => e.EventName == "LaneJobStop").ToList();
                Assert.That(stops.Count, Is.EqualTo(3));
                Assert.That(stops.Count(e => (bool)e.Payload[2]), Is.EqualTo(1), "exactly one job failed");
            }
        }

        [Test]
        public void ShellCall_WritesStartAndStopWhenTraced()
        {
            using (var listener = new Collector())
            {
                listener.EnableEvents(PerfTrace.Instance, EventLevel.Informational);
                long started = PerfTrace.ShellCallBegin("TestCall");
                Assert.That(started, Is.Not.EqualTo(0));
                PerfTrace.ShellCallEnd("TestCall", started, false);

                var mine = listener.Snapshot().Where(e => e.Payload.Count > 0 && (e.Payload[0] as string) == "TestCall").ToList();
                Assert.That(mine.Select(e => e.EventName), Is.EqualTo(new[] { "ShellCallStart", "ShellCallStop" }));
                Assert.That((bool)mine[1].Payload[2], Is.False);
            }
        }
    }
}
