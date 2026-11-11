using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace PowerPane.Services
{
    /// <summary>
    /// Start and stop events for the work that can stall the pane or Explorer: background lane jobs,
    /// pane and group rebuilds, and cross-process calls into Explorer and file dialogs. Two stalls of
    /// several hundred milliseconds and the occasional busy cursor in Explorer (#53) have no known
    /// cause, and the text log cannot show what Explorer's own threads were doing at that moment. A
    /// trace from Windows Performance Recorder or PerfView records these events next to Explorer's
    /// threads, CPU, and waits, so one timeline answers it.
    ///
    /// This is for traces only, and the text log still goes through <see cref="Log"/>. Nothing leaves
    /// the machine: an EventSource writes nothing at all until someone on this computer starts a trace
    /// that asks for it, and every call below checks <see cref="EventSource.IsEnabled()"/> first, so the
    /// cost while no trace runs is one field read. A trace may be shared to ask for help, so payloads
    /// carry kinds, counts, and durations, never a folder or file name.
    ///
    /// Provider name: "PowerPane". Its GUID is derived from that name, so tools take it as "*PowerPane".
    /// </summary>
    [EventSource(Name = "PowerPane")]
    internal sealed class PerfTrace : EventSource
    {
        public static readonly PerfTrace Instance = new PerfTrace();

        private PerfTrace() { }

        public static class Tasks
        {
            public const EventTask LaneJob = (EventTask)1;
            public const EventTask PaneBuild = (EventTask)2;
            public const EventTask GroupBuild = (EventTask)3;
            public const EventTask ShellCall = (EventTask)4;
        }

        // Event ids are part of the trace format: never renumber or reuse one, only add.

        [Event(1, Task = Tasks.LaneJob, Opcode = EventOpcode.Start, Level = EventLevel.Verbose)]
        public void LaneJobStart(string lane, int waiting) { WriteEvent(1, lane, waiting); }

        [Event(2, Task = Tasks.LaneJob, Opcode = EventOpcode.Stop, Level = EventLevel.Verbose)]
        public void LaneJobStop(string lane, long durationMs, bool failed) { WriteEvent(2, lane, durationMs, failed); }

        [Event(3, Task = Tasks.PaneBuild, Opcode = EventOpcode.Start, Level = EventLevel.Informational)]
        public void PaneBuildStart(int sections) { WriteEvent(3, sections); }

        [Event(4, Task = Tasks.PaneBuild, Opcode = EventOpcode.Stop, Level = EventLevel.Informational)]
        public void PaneBuildStop(int sections, long durationMs) { WriteEvent(4, sections, durationMs); }

        [Event(5, Task = Tasks.GroupBuild, Opcode = EventOpcode.Start, Level = EventLevel.Informational)]
        public void GroupBuildStart(int groups) { WriteEvent(5, groups); }

        [Event(6, Task = Tasks.GroupBuild, Opcode = EventOpcode.Stop, Level = EventLevel.Informational)]
        public void GroupBuildStop(int groups, long durationMs) { WriteEvent(6, groups, durationMs); }

        [Event(7, Task = Tasks.ShellCall, Opcode = EventOpcode.Start, Level = EventLevel.Informational)]
        public void ShellCallStart(string kind) { WriteEvent(7, kind); }

        [Event(8, Task = Tasks.ShellCall, Opcode = EventOpcode.Stop, Level = EventLevel.Informational)]
        public void ShellCallStop(string kind, long durationMs, bool ok) { WriteEvent(8, kind, durationMs, ok); }

        // ---- Helpers for callers. Each returns 0 when no trace is listening, and the matching End
        // ---- skips its event for a 0 start, so an untraced call costs one check and no clock read.

        /// <summary>Marks the start of a cross-process call. <paramref name="kind"/> must be a fixed
        /// name, such as "ExplorerNavigate", never anything that holds a path.</summary>
        [NonEvent]
        public static long ShellCallBegin(string kind)
        {
            if (!Instance.IsEnabled()) return 0;
            Instance.ShellCallStart(kind);
            return Stopwatch.GetTimestamp();
        }

        [NonEvent]
        public static void ShellCallEnd(string kind, long started, bool ok)
        {
            if (started == 0) return;
            Instance.ShellCallStop(kind, ElapsedMs(started), ok);
        }

        [NonEvent]
        public static long PaneBuildBegin(int sections)
        {
            if (!Instance.IsEnabled()) return 0;
            Instance.PaneBuildStart(sections);
            return Stopwatch.GetTimestamp();
        }

        [NonEvent]
        public static void PaneBuildEnd(int sections, long started)
        {
            if (started == 0) return;
            Instance.PaneBuildStop(sections, ElapsedMs(started));
        }

        [NonEvent]
        public static long GroupBuildBegin(int groups)
        {
            if (!Instance.IsEnabled()) return 0;
            Instance.GroupBuildStart(groups);
            return Stopwatch.GetTimestamp();
        }

        [NonEvent]
        public static void GroupBuildEnd(int groups, long started)
        {
            if (started == 0) return;
            Instance.GroupBuildStop(groups, ElapsedMs(started));
        }

        [NonEvent]
        internal static long ElapsedMs(long started)
        {
            return (Stopwatch.GetTimestamp() - started) * 1000 / Stopwatch.Frequency;
        }
    }
}
