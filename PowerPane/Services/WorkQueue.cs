using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace PowerPane.Services
{
    /// <summary>
    /// Background STA workers for everything that can touch a slow disk, the network, or
    /// cross-process COM: shortcut resolution, Shell.Application navigation, icon extraction, and
    /// existence probes. This matters because in Inside mode the UI thread is input-attached to
    /// Explorer (and to any app hosting a file dialog we embedded in), so a single blocking call on
    /// the UI thread stalls input for the whole shell even though CPU stays idle. Posting here means
    /// the only threads that ever wait on I/O are these.
    ///
    /// Folder clicks get a lane of their own. They used to share the one worker with the Recent
    /// poll, path probes, drag-and-drop copies, and the update check, so a click waited for whatever
    /// was running, for seconds behind a large copy or a dead network share
    /// (prisant-labs/windows-explorer-powerpane#38).
    /// </summary>
    internal static class WorkQueue
    {
        private static readonly WorkLane General = new WorkLane("PowerPaneWorker");
        private static readonly WorkLane Navigation = new WorkLane("PowerPaneNavigator");

        // settings.json writes get a lane of their own too. App exit, session end, and a backup wait
        // for the pending write with a bounded Flush, and on the general lane that wait would sit
        // behind a group scan or a probe of a dead share, the same stall #38 took clicks off it for
        // (prisant-labs/windows-explorer-powerpane#163).
        private static readonly WorkLane SettingsWrites = new WorkLane("PowerPaneSettingsWriter");

        /// <summary>Queue work on the general worker. Never blocks the caller.</summary>
        public static void Post(Action work) { General.Post(work); }

        /// <summary>Queue a settings.json write. Only <see cref="SettingsStore"/> posts here, so a
        /// Flush waits for that write and nothing else.</summary>
        public static void PostSettingsWrite(Action work) { SettingsWrites.Post(work); }

        /// <summary>Queue a navigation the user asked for: driving an Explorer window or a file
        /// dialog to a folder. Never blocks the caller. Keep anything else off this lane, so a click
        /// only ever waits for an earlier click.</summary>
        public static void PostNavigation(Action work) { Navigation.Post(work); }

        /// <summary>Run on the WPF UI thread without waiting for it.</summary>
        public static void PostUI(Action work)
        {
            if (work == null) return;
            var app = System.Windows.Application.Current;
            var disp = app != null ? app.Dispatcher : null;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(work);
        }
    }

    /// <summary>
    /// One background STA thread that runs posted work in order. The thread starts on the first
    /// post, and a failing item is reported without stopping the ones behind it. Failures go to the
    /// log unless a test passes its own handler.
    /// </summary>
    internal sealed class WorkLane
    {
        // A task that runs long enough to matter gets its own log line, so a stalled lane shows up
        // without waiting for someone to notice the symptom (prisant-labs/windows-explorer-powerpane#77).
        private const long SlowTaskMs = 5000;

        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly object _gate = new object();
        private readonly string _name;
        private readonly Action<string, Exception> _onError;
        private readonly Action<string> _onInfo;
        private Thread _thread;
        private int _nextDepthThreshold = 25;

        public WorkLane(string name, Action<string, Exception> onError = null, Action<string> onInfo = null)
        {
            _name = name;
            _onError = onError ?? Log.Error;
            _onInfo = onInfo ?? Log.Info;
        }

        public void Post(Action work)
        {
            if (work == null) return;
            EnsureThread();
            int depth;
            try { _queue.Add(work); depth = _queue.Count; }
            catch (InvalidOperationException) { /* shutting down */ return; }
            bool report;
            lock (_gate) { report = ShouldReportDepth(depth, ref _nextDepthThreshold); }
            if (report) _onInfo(_name + " queue depth " + depth);
        }

        /// <summary>Whether the backlog just crossed a reporting line: 25, 50, 100, and doubling from
        /// there, so a stalled lane's queue shows up in the log without a line for every single post.
        /// Depth 0 resets the next line back to 25, so the next burst reports from the start again.</summary>
        internal static bool ShouldReportDepth(int depth, ref int nextThreshold)
        {
            if (depth == 0) { nextThreshold = 25; return false; }
            if (depth < nextThreshold) return false;
            while (nextThreshold <= depth) nextThreshold *= 2;
            return true;
        }

        private void EnsureThread()
        {
            if (_thread != null) return;
            lock (_gate)
            {
                if (_thread != null) return;
                var t = new Thread(Loop) { IsBackground = true, Name = _name };
                // STA so the shell COM used here (IShellLink, Shell.Application, SHGetFileInfo)
                // behaves the way it does on the UI thread.
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                _thread = t;
            }
        }

        private void Loop()
        {
            var sw = new Stopwatch();
            var trace = PerfTrace.Instance;
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                // Checked per job, so a trace started while the lane runs picks it up at the next job.
                bool traced = trace.IsEnabled();
                if (traced) trace.LaneJobStart(_name, _queue.Count);
                bool failed = false;
                sw.Restart();
                try { work(); }
                catch (Exception ex) { failed = true; _onError("background work failed on " + _name, ex); }
                var ms = sw.ElapsedMilliseconds;
                if (traced) trace.LaneJobStop(_name, ms, failed);
                var waiting = _queue.Count;
                if (ms > SlowTaskMs)
                    _onInfo("task on " + _name + " ran " + ms + " ms; " + waiting + " waiting");
                if (waiting == 0) lock (_gate) { ShouldReportDepth(0, ref _nextDepthThreshold); }
            }
        }
    }

    /// <summary>
    /// Merges every Request() that arrives while a run is queued or in flight into the one about to
    /// run, instead of posting a new run for each. The groups watcher used to re-arm a 250 ms timer on
    /// every change and post a fresh full scan each time it fired, so separate bursts during a stalled
    /// lane stacked up scans that each walked every pin again
    /// (prisant-labs/windows-explorer-powerpane#77). Clearing the pending flag before running work means
    /// a Request() that arrives mid-run is not lost: it schedules exactly one more run after this one.
    /// </summary>
    internal sealed class CoalescedPost
    {
        private readonly Action _work;
        private readonly Action<Action> _post;
        private int _pending;

        public CoalescedPost(Action work, Action<Action> post)
        {
            _work = work;
            _post = post;
        }

        public void Request()
        {
            if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0) return;
            _post(Run);
        }

        private void Run()
        {
            Interlocked.Exchange(ref _pending, 0);
            _work();
        }
    }
}
