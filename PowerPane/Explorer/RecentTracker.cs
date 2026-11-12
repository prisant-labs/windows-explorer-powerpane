using System;
using System.Windows.Threading;
using PowerPane.Interop;
using PowerPane.Services;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.Explorer
{
    /// <summary>
    /// Records the folder of whatever Explorer window the user is browsing, so Recent reflects real
    /// usage and not only folders opened through PowerPane. It reacts to window activation and runs a
    /// slow safety poll. There is deliberately no NAMECHANGE hook: that event fires for nearly every
    /// control text change on the desktop, and answering each one with a cross-process Shell COM
    /// enumeration stalled the input queue the UI thread shares with Explorer. The COM query itself
    /// always runs on the background worker, never on the UI thread.
    /// </summary>
    internal sealed class RecentTracker : IDisposable
    {
        private WinEventHook _fg;
        private DispatcherTimer _poll;
        private volatile bool _busy;    // one COM query in flight at a time
        private IntPtr _lastHwnd;
        private string _lastPath;

        public void Start()
        {
            _fg = new WinEventHook(NM.EVENT_SYSTEM_FOREGROUND, NM.EVENT_SYSTEM_FOREGROUND);
            _fg.Event += OnEvt; _fg.Install();

            // In-window navigations do not change the foreground window, so a light poll of the
            // foreground Explorer window catches those. RecordNavigation already skips repeats.
            _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
            _poll.Tick += (s, e) => Track(NM.GetForegroundWindow(), fromPoll: true);
            _poll.Start();

            Track(NM.GetForegroundWindow(), fromPoll: false); // record whatever is already in front
        }

        private void OnEvt(uint evt, IntPtr hwnd, int idObject, int idChild, uint thread)
        {
            if (idObject != NM.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero) return;
            NoteApp(hwnd);
            Track(hwnd, fromPoll: false);
        }

        // Which program was just switched to. Reading the path is a pair of handle calls, so it happens
        // inline; deciding whether it belongs on the list, and writing the file, happens on the worker.
        private void NoteApp(IntPtr hwnd)
        {
            if (App.RecentApps == null) return;
            if (hwnd == _lastAppHwnd) return;
            _lastAppHwnd = hwnd;
            try
            {
                var exe = NM.ExePathOf(hwnd);
                if (string.IsNullOrEmpty(exe)) return;
                WorkQueue.Post(() =>
                {
                    try { App.RecentApps.RecordRunning(exe); }
                    catch (Exception ex) { Log.Error("note the app in front", ex); }
                });
            }
            catch (Exception ex) { Log.Error("read the app in front", ex); }
        }

        private IntPtr _lastAppHwnd;

        // What the window looked like at the last COM query: which window, which tab was in front, and
        // its title. All three are plain Win32 reads, with no cross-process COM.
        private IntPtr _queriedHwnd;
        private long _queriedTab;
        private string _queriedTitle;
        private int _queriedTick;
        internal const int RefreshMs = 30000;

        private void Track(IntPtr hwnd, bool fromPoll)
        {
            if (hwnd == IntPtr.Zero || _busy) return;
            var c = NM.ClassOf(hwnd);
            if (c != "CabinetWClass" && c != "ExploreWClass") return;

            // A poll tick whose window, front tab, and title all match the last query skips the COM
            // query, which walks every shell window inside Explorer's process. Nearly every tick used to
            // find the same folder as the one before (prisant-labs/windows-explorer-powerpane#39).
            long tab = NM.FindWindowEx(hwnd, IntPtr.Zero, "ShellTabWindowClass", null).ToInt64();
            string title = NM.TextOf(hwnd);
            int now = Environment.TickCount;
            if (fromPoll && !ShouldQuery(hwnd == _queriedHwnd, tab == _queriedTab,
                    string.Equals(title, _queriedTitle, StringComparison.Ordinal), unchecked(now - _queriedTick)))
                return;
            _queriedHwnd = hwnd;
            _queriedTab = tab;
            _queriedTitle = title;
            _queriedTick = now;

            _busy = true;
            WorkQueue.Post(() =>
            {
                try
                {
                    var path = ExplorerNavigator.GetCurrentPath(hwnd);
                    if (!string.IsNullOrEmpty(path) &&
                        !(hwnd == _lastHwnd && string.Equals(path, _lastPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        _lastHwnd = hwnd;
                        _lastPath = path;
                        App.Recents?.RecordNavigation(path);
                    }
                }
                catch (Exception ex) { Log.Error("recent track", ex); }
                finally { _busy = false; }
            });
        }

        /// <summary>Whether a poll tick should run the COM query. Any change to the window, its front tab,
        /// or its title queries at once. Otherwise the query still runs every RefreshMs, which catches a
        /// move between two folders with the same name, since the title shows only the folder's name.
        /// Activation events never ask: they always query.</summary>
        internal static bool ShouldQuery(bool sameWindow, bool sameTab, bool sameTitle, int msSinceLastQuery)
        {
            if (!sameWindow || !sameTab || !sameTitle) return true;
            return msSinceLastQuery < 0 || msSinceLastQuery >= RefreshMs;
        }

        public void Dispose()
        {
            try { _poll?.Stop(); } catch { }
            try { _fg?.Dispose(); } catch { }
        }
    }
}
