using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using PowerPane.Interop;
using PowerPane.Services;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.Explorer
{
    /// <summary>
    /// Watches the desktop for Explorer folder windows and embeds a sidebar in each. Uses two
    /// out-of-process WinEvent hooks: one for create/destroy/show, one for location changes. No code
    /// runs inside explorer.exe, so a fault in PowerPane can never bring Explorer down.
    /// </summary>
    internal sealed class ExplorerWatcher : IDisposable
    {
        /// <summary>Raised when Windows reports a device arrival/removal, so drive lists refresh.</summary>
        public static event Action DrivesChanged;

        private const uint WM_DEVICECHANGE = 0x0219;

        private readonly Dictionary<IntPtr, ExplorerWindow> _windows = new Dictionary<IntPtr, ExplorerWindow>();
        private readonly Dictionary<IntPtr, int> _pending = new Dictionary<IntPtr, int>();

        private WinEventHook _lifecycleHook;   // CREATE / DESTROY / SHOW
        private WinEventHook _locationHook;     // LOCATIONCHANGE
        private WinEventHook _foregroundHook;   // SYSTEM_FOREGROUND
        private WinEventHook _moveSizeHook;     // SYSTEM_MOVESIZESTART / MOVESIZEEND
        private DispatcherTimer _retryTimer;
        private DispatcherTimer _sweepTimer;
        private HwndSource _deviceSink;

        // A snapshot of attached windows for the background enforcer to iterate without touching the
        // UI-thread dictionary or any WPF object.
        private readonly object _snapLock = new object();
        private readonly List<ExplorerWindow> _attached = new List<ExplorerWindow>();
        private Thread _enforcer;
        private volatile bool _stopEnforcer;
        // EnforceFast runs only in short bursts, triggered when a window first shows or is activated,
        // because those are the moments Explorer resets its content to full width outside a drag. There
        // is no constant poll.
        private volatile int _burstUntilTick;
        private readonly System.Threading.AutoResetEvent _burstSignal = new System.Threading.AutoResetEvent(false);
        private void Burst(int ms = 250) { _burstUntilTick = Environment.TickCount + ms; _burstSignal.Set(); }

        // The window whose border the user is dragging. Explorer puts its tab back at full width on every
        // resize, programmatic ones included; outside a drag the correction lands before anyone sees it.
        // During a drag the steps come too fast, so reapplying the split after each one made Explorer's
        // tree and the file list jump under the pane and back about a hundred times in a two-second drag.
        // Holding the split from the enforcer only shortened each jump and made the border trail the
        // cursor, because each correction waits for Explorer's thread to finish its own layout first. So
        // the dragged window is left to Explorer until the drag ends, and the split is reapplied once on
        // release (prisant-labs/windows-explorer-powerpane#99). Leaving Explorer's layout alone put its
        // tree under the pane for the whole drag, so the pane, being the add-on, hides for the drag
        // instead (#102). Capped, so a lost end event cannot leave a window unmanaged.
        private ExplorerWindow _sizing;
        private int _sizingSinceTick;
        internal const int DragYieldCapMs = 30000;

        /// <summary>True while a drag that started at sinceTick is still within the cap. Compares by
        /// subtraction, so it stays right when Environment.TickCount wraps after 24.9 days.</summary>
        internal static bool DragYieldActive(int sinceTick, int nowTick)
        {
            int elapsed = unchecked(nowTick - sinceTick);
            return elapsed >= 0 && elapsed < DragYieldCapMs;
        }

        public void Start()
        {
            _lifecycleHook = new WinEventHook(NM.EVENT_OBJECT_CREATE, NM.EVENT_OBJECT_SHOW);
            _lifecycleHook.Event += OnLifecycleEvent;
            _lifecycleHook.Install();

            _locationHook = new WinEventHook(NM.EVENT_OBJECT_LOCATIONCHANGE, NM.EVENT_OBJECT_LOCATIONCHANGE);
            _locationHook.Event += OnLocationEvent;
            _locationHook.Install();

            // Reapply the split the moment a window is activated, because Explorer recomputes its
            // layout to full width on activation, which is the remaining source of the focus flash.
            _foregroundHook = new WinEventHook(NM.EVENT_SYSTEM_FOREGROUND, NM.EVENT_SYSTEM_FOREGROUND);
            _foregroundHook.Event += OnForegroundEvent;
            _foregroundHook.Install();

            // Leave a window to Explorer for the length of a border drag (see _sizing).
            _moveSizeHook = new WinEventHook(NM.EVENT_SYSTEM_MOVESIZESTART, NM.EVENT_SYSTEM_MOVESIZEEND);
            _moveSizeHook.Event += OnMoveSizeEvent;
            _moveSizeHook.Install();

            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _retryTimer.Tick += OnRetryTick;

            // Safety net: events can be missed, so sweep open Explorer windows once a second.
            _sweepTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _sweepTimer.Tick += (s, e) => Rescan();
            _sweepTimer.Start();

            CreateDeviceSink();
            Rescan();

            // Background enforcer: holds every attached window's content pane in place faster than the
            // screen refreshes, so Explorer's reset on activation is corrected before it is ever shown.
            _enforcer = new Thread(EnforceLoop) { IsBackground = true, Name = "PowerPaneEnforcer", Priority = ThreadPriority.AboveNormal };
            _enforcer.Start();

            Log.Info("ExplorerWatcher started. lifecycleHook=" + (_lifecycleHook != null) + " locationHook=" + (_locationHook != null));
        }

        private void EnforceLoop()
        {
            var buffer = new List<ExplorerWindow>(8);
            while (!_stopEnforcer)
            {
                // Spin fast only inside a burst (a window just shown or activated). Outside a burst, the
                // split is held event-driven from OnLocationEvent, so there is no perpetual cross-process
                // poll to fight Explorer. A window being dragged is skipped (ExplorerWindow.Yielding).
                if (Environment.TickCount - _burstUntilTick < 0)
                {
                    buffer.Clear();
                    lock (_snapLock) buffer.AddRange(_attached);
                    for (int i = 0; i < buffer.Count; i++)
                    {
                        try { buffer[i].EnforceFast(); } catch { }
                    }
                    Thread.Sleep(4); // ~250 Hz, but only for the brief burst
                }
                else
                {
                    _burstSignal.WaitOne(1000); // block with zero CPU until the next burst, 1s safety wake
                }
            }
        }

        private void TrackAttached(ExplorerWindow win, bool add)
        {
            lock (_snapLock)
            {
                if (add) { if (!_attached.Contains(win)) _attached.Add(win); }
                else _attached.Remove(win);
            }
        }

        // ---- window lifecycle ------------------------------------------------

        private void OnLifecycleEvent(uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread)
        {
            if (idObject != NM.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero) return;

            if (eventType == NM.EVENT_OBJECT_DESTROY)
            {
                if (_windows.TryGetValue(hwnd, out var win))
                {
                    _windows.Remove(hwnd);
                    TrackAttached(win, false);
                    ForgetSizing(win);
                    win.Dispose();
                    Log.Info("Explorer window closed " + hwnd.ToString("X"));
                }
                _pending.Remove(hwnd);
                return;
            }

            // CREATE or SHOW
            // A new Windows 11 tab in a window we already hold. Explorer sizes a new tab to full width as
            // it creates and activates it, so lay it out now and hold the split briefly while it settles.
            ExplorerWindow owner;
            IntPtr parent = NM.GetParent(hwnd);
            if (parent != IntPtr.Zero && _windows.TryGetValue(parent, out owner) && owner.IsContentHost(hwnd))
            {
                owner.ApplyLayout();
                Burst();
                return;
            }

            if (!IsExplorerFolder(hwnd)) return;
            if (_windows.ContainsKey(hwnd)) return;
            QueueAttach(hwnd);
        }

        private void OnLocationEvent(uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread)
        {
            if (idObject != NM.OBJID_WINDOW) return;

            if (_windows.TryGetValue(hwnd, out var win))
            {
                // Explorer moved or resized one of our tracked windows. Reapply the split.
                win.ApplyLayout();
                return;
            }

            // Explorer resets a content host to full width when the window activates or resizes, which
            // is the source of the flash on focus, and a new Windows 11 tab starts at full width. Catch
            // that move on the host itself and correct it immediately, before the next sweep, so the
            // shift looks instant. Location events fire system-wide, so the cheap parent lookup comes
            // first and the class check runs only for children of a window we hold.
            if (_windows.Count == 0) return;
            ExplorerWindow owner;
            IntPtr parent = NM.GetParent(hwnd);
            if (parent != IntPtr.Zero && _windows.TryGetValue(parent, out owner) && owner.IsContentHost(hwnd))
                owner.ApplyLayout();
        }

        private void OnForegroundEvent(uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread)
        {
            if (_windows.TryGetValue(hwnd, out var win)) { win.ApplyLayout(); Burst(); }
        }

        private void OnMoveSizeEvent(uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread)
        {
            if (idObject != NM.OBJID_WINDOW || !_windows.TryGetValue(hwnd, out var win)) return;

            if (eventType == NM.EVENT_SYSTEM_MOVESIZESTART)
            {
                // A title-bar move raises the same events but never resizes, so the pane stays up for a
                // move and hides only for a resize. The cursor on the border tells them apart at once;
                // a keyboard resize is caught at its first size change instead. A window still marked
                // from a drag whose end event was lost is released first, so it cannot stay unmanaged
                // until the cap.
                var previous = _sizing;
                if (previous != null && previous != win) ForgetSizing(previous);
                _sizingSinceTick = Environment.TickCount;
                _sizing = win;
                win.BeginDrag(ResizeBorder.CursorOn(hwnd));
                return;
            }

            if (_sizing != win) return;
            ForgetSizing(win);
            Burst(); // Explorer can lay out once more as the drag ends
        }

        // Manage a window normally again: its drag ended, its end event was lost, or it closed. EndDrag
        // reapplies the split and shows the pane again; on a closed window both are no-ops.
        private void ForgetSizing(ExplorerWindow win)
        {
            if (_sizing == win) _sizing = null;
            win.EndDrag();
        }

        private static bool IsExplorerFolder(IntPtr hwnd)
        {
            if (!NM.IsWindow(hwnd)) return false;
            var cls = NM.ClassOf(hwnd);
            return cls == "CabinetWClass" || cls == "ExploreWClass";
        }

        private void QueueAttach(IntPtr hwnd)
        {
            if (!_pending.ContainsKey(hwnd)) _pending[hwnd] = 0;
            if (!_retryTimer.IsEnabled) _retryTimer.Start();
        }

        private static void DumpChildren(IntPtr hwnd)
        {
            try
            {
                Log.Info("Attach gave up on " + hwnd.ToString("X") + " class=" + NM.ClassOf(hwnd) + ". Direct children:");
                NM.EnumChildWindows(hwnd, (h, l) =>
                {
                    if (NM.GetParent(h) != hwnd) return true;
                    NM.RECT r;
                    NM.GetWindowRect(h, out r);
                    Log.Info("   child " + h.ToString("X") + " class='" + NM.ClassOf(h) + "' " +
                             r.Width + "x" + r.Height);
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { Log.Error("DumpChildren failed", ex); }
        }

        private void OnRetryTick(object sender, EventArgs e)
        {
            if (_pending.Count == 0) { _retryTimer.Stop(); return; }

            var done = new List<IntPtr>();
            // Copy keys so we can mutate the dictionary while iterating.
            foreach (var hwnd in new List<IntPtr>(_pending.Keys))
            {
                if (!NM.IsWindow(hwnd)) { done.Add(hwnd); continue; }
                if (_windows.ContainsKey(hwnd)) { done.Add(hwnd); continue; }

                var win = new ExplorerWindow(hwnd);
                win.OnFirstShown = () => Burst(); // hold the split briefly the moment the strip first appears
                if (win.TryAttach())
                {
                    _windows[hwnd] = win;
                    TrackAttached(win, true);
                    Burst();
                    done.Add(hwnd);
                }
                else
                {
                    int attempts = ++_pending[hwnd];
                    if (attempts > 12)
                    {
                        // Give up, but record the child window classes so we can see what the real
                        // content host is on this Windows build if ShellTabWindowClass was not found.
                        DumpChildren(hwnd);
                        done.Add(hwnd);
                        win.Dispose();
                    }
                }
            }

            foreach (var h in done) _pending.Remove(h);
            if (_pending.Count == 0) _retryTimer.Stop();
        }

        /// <summary>Re-sweep open Explorer windows. Reapplies layout to tracked ones, attaches new
        /// ones, and prunes windows whose DESTROY event was missed, so a lost event can no longer
        /// leak an ExplorerWindow (HwndSource + settings-event subscriptions) for the session.</summary>
        public void Rescan()
        {
            // Iterate a snapshot. ApplyLayout drives windows in explorer.exe, and a destroy event that
            // lands mid-pass removes an entry, so walking the live dictionary would throw partway
            // through and leave the remaining windows unlaid. A drag whose end event was lost is
            // released first, so that window is laid out again in this pass.
            var sizing = _sizing;
            if (sizing != null && !DragYieldActive(_sizingSinceTick, Environment.TickCount)) ForgetSizing(sizing);

            List<IntPtr> dead = null;
            foreach (var kv in new List<KeyValuePair<IntPtr, ExplorerWindow>>(_windows))
            {
                if (NM.IsWindow(kv.Key)) kv.Value.ApplyLayout();
                else (dead ?? (dead = new List<IntPtr>())).Add(kv.Key);
            }
            if (dead != null)
            {
                foreach (var hwnd in dead)
                {
                    ExplorerWindow win;
                    if (!_windows.TryGetValue(hwnd, out win)) continue;
                    _windows.Remove(hwnd);
                    TrackAttached(win, false);
                    ForgetSizing(win);
                    try { win.Dispose(); } catch (Exception ex) { Log.Error("prune dispose", ex); }
                    Log.Info("Explorer window pruned (missed destroy) " + hwnd.ToString("X"));
                }
            }

            // Ask Windows for the Explorer windows by class. Enumerating every top-level window and reading
            // each one's class made hundreds of reads and allocations a second to find a handful
            // (prisant-labs/windows-explorer-powerpane#44).
            foreach (var cls in ExplorerClasses)
            {
                for (var hwnd = NM.FindWindowEx(IntPtr.Zero, IntPtr.Zero, cls, null); hwnd != IntPtr.Zero;
                     hwnd = NM.FindWindowEx(IntPtr.Zero, hwnd, cls, null))
                {
                    if (NM.IsWindowVisible(hwnd) && !_windows.ContainsKey(hwnd)) QueueAttach(hwnd);
                }
            }
        }

        private static readonly string[] ExplorerClasses = { "CabinetWClass", "ExploreWClass" };

        // ---- device-change sink ---------------------------------------------

        private void CreateDeviceSink()
        {
            try
            {
                // Top-level hidden window. It must not be message-only, because message-only windows
                // do not receive the WM_DEVICECHANGE broadcast.
                var p = new HwndSourceParameters("PowerPaneDeviceSink")
                {
                    Width = 1,
                    Height = 1,
                    WindowStyle = unchecked((int)NM.WS_POPUP),
                    ExtendedWindowStyle = unchecked((int)NM.WS_EX_TOOLWINDOW) // keep it out of Alt+Tab
                };
                _deviceSink = new HwndSource(p);
                _deviceSink.AddHook(DeviceHook);
            }
            catch (Exception ex)
            {
                Log.Error("device sink create failed", ex);
            }
        }

        private IntPtr DeviceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((uint)msg == WM_DEVICECHANGE)
            {
                try { DrivesChanged?.Invoke(); } catch (Exception ex) { Log.Error("DrivesChanged handler", ex); }
            }
            return IntPtr.Zero;
        }

        // ---- teardown --------------------------------------------------------

        public void Dispose()
        {
            _stopEnforcer = true;
            try { _burstSignal.Set(); } catch { } // wake the enforcer out of its idle wait so it can exit
            try { if (_enforcer != null && !_enforcer.Join(200)) { } } catch { }
            try { _burstSignal.Dispose(); } catch { }
            lock (_snapLock) _attached.Clear();

            try { _retryTimer?.Stop(); } catch { }
            try { _sweepTimer?.Stop(); } catch { }

            foreach (var win in _windows.Values)
            {
                try { win.Dispose(); } catch (Exception ex) { Log.Error("window dispose", ex); }
            }
            _windows.Clear();
            _pending.Clear();

            try { _lifecycleHook?.Dispose(); } catch { }
            try { _locationHook?.Dispose(); } catch { }
            try { _foregroundHook?.Dispose(); } catch { }
            try { _moveSizeHook?.Dispose(); } catch { }
            try { _deviceSink?.Dispose(); } catch { }
        }
    }
}
