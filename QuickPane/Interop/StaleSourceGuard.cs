using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using QuickPane.Services;
using NM = QuickPane.Interop.NativeMethods;

namespace QuickPane.Interop
{
    /// <summary>
    /// Keeps WPF's text-input hook from ending the process when a QuickPane window loses the
    /// window above it (#148).
    ///
    /// WPF registers every rendered TextBox, such as a pane's filter box, with the text services
    /// framework whether or not it ever had focus, and installs one global hook for
    /// EVENT_SYSTEM_MOVESIZEEND. When any window on the desktop finishes a move or resize, that
    /// hook walks GetParent up from each text box's window, and WPF's GetParent wrapper throws on a
    /// destroyed handle. The throw happens inside GetMessage, outside the dispatcher's exception
    /// wrapper, so DispatcherUnhandledException never sees it and the process exits. A pane's window
    /// is a child of an Explorer window or dialog in another process, or owned by one, so when that
    /// process dies the chain above the pane can hold a dead handle before any watcher notices.
    ///
    /// The guard walks the same chain for every live HwndSource on this thread and releases the
    /// content of any whose chain reaches a dead handle, which detaches its text boxes from the
    /// window so WPF's hook skips them. The order of two out-of-context hooks for the same event is
    /// not documented, so nothing here can promise to run between a handle dying and WPF's hook
    /// reaching it; each trigger below shrinks that gap instead (#159):
    ///   - EVENT_OBJECT_DESTROY for any window. In the #148 crash, fourteen Explorer windows' DESTROY
    ///     events arrived and their panes were torn down at once, but none arrived for one more
    ///     cabinet, which died 4.5 s later and took the process with it. Any destroy on the desktop
    ///     now re-checks every chain, so a host whose own DESTROY is lost can still be caught by the
    ///     events of the windows dying around it. A chain that dies at the very window being
    ///     destroyed is left to its watcher, which handles that same event (see LeaveToWatcher).
    ///   - EVENT_SYSTEM_MOVESIZESTART, which Windows raises before the MOVESIZEEND that trips WPF.
    ///   - WM_ENTERSIZEMOVE in each of QuickPane's own windows. The shared hook skips this process,
    ///     so it never saw a drag of the Settings window, while WPF's hook does; the message is sent
    ///     to the window before its move loop starts, so it lands before that move's MOVESIZEEND.
    ///   - MOVESIZEEND and WM_EXITSIZEMOVE, as cheap extra chances only: either may arrive after
    ///     WPF's hook has already run for the same move.
    ///   - A one-second sweep at Render priority. It was Background, which stops firing while the
    ///     dispatcher is busy, and an Explorer restart is exactly when it is busiest.
    /// The owning watcher still tears the pane down on its own schedule; the guard only makes the
    /// gap until then safe. A guarded message loop that catches WPF's exception was ruled out: the
    /// logged stack trace of the crash ends at the WinEvent callback, with no dispatcher frame
    /// above it, which suggests the throw never unwound into any frame a catch in Main could own.
    /// </summary>
    internal sealed class StaleSourceGuard : IDisposable
    {
        private const int MaxChainDepth = 64;   // a parent/owner loop must not hang the UI thread

        // Read by the own-window hook, which outlives any one guard because class handlers cannot be
        // removed. UI thread only.
        private static bool _running;
        private static bool _classHandlerRegistered;
        private static readonly HwndSourceHook OwnWindowHook = OnOwnWindowMessage;

        private WinEventHook _moveSize;
        private WinEventHook _lifecycle;
        private DispatcherTimer _sweep;

        public void Start()
        {
            _running = true;

            // Same range as ExplorerFollowerWatcher and DialogPaneWatcher, so WinEventHub shares one
            // native hook among all three.
            _moveSize = new WinEventHook(NM.EVENT_SYSTEM_MOVESIZESTART, NM.EVENT_SYSTEM_MOVESIZEEND);
            _moveSize.Event += OnWinEvent;
            _moveSize.Install();

            // Same range as ExplorerWatcher and ExplorerFollowerWatcher, for the same reason. Only
            // DESTROY is acted on; CREATE and SHOW are dropped by CheckOnEvent.
            _lifecycle = new WinEventHook(NM.EVENT_OBJECT_CREATE, NM.EVENT_OBJECT_SHOW);
            _lifecycle.Event += OnWinEvent;
            _lifecycle.Install();

            // Every WPF window this process opens gets the move hook as it loads, which is before it
            // can take input: Settings, prompts, beside panes, the dock, and any window added later.
            if (!_classHandlerRegistered)
            {
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                    new RoutedEventHandler((s, e) => HookOwnWindow(s as Window)));
                _classHandlerRegistered = true;
            }
            var app = Application.Current;
            if (app != null) foreach (Window w in app.Windows) HookOwnWindow(w);

            _sweep = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1) };
            _sweep.Tick += (s, e) => Check();
            _sweep.Start();
        }

        private static void OnWinEvent(uint type, IntPtr hwnd, int idObject, int idChild, uint thread)
        {
            if (!CheckOnEvent(type, idObject, idChild)) return;
            Check(type == NM.EVENT_OBJECT_DESTROY ? hwnd : IntPtr.Zero);
        }

        /// <summary>True when a check run for the destruction of <paramref name="destroyed"/> should
        /// leave a source whose chain dies at <paramref name="dead"/> to the watcher that owns it.
        /// That is the routine close of a pane's own host: the watcher tears the pane down from this
        /// same event, as it did for every window but one in the #148 crash, and releasing first would
        /// only journal a false glitch. A chain that dies anywhere else means a destroy that never
        /// reached its watcher, and is released at once (#159).</summary>
        internal static bool LeaveToWatcher(IntPtr dead, IntPtr destroyed)
        {
            return destroyed != IntPtr.Zero && dead == destroyed;
        }

        /// <summary>True for the WinEvents that should run a check (#159): the start of any move or
        /// size loop, its end (a late extra chance), and the destruction of a window itself rather
        /// than of an object inside one.</summary>
        internal static bool CheckOnEvent(uint eventType, int idObject, int idChild)
        {
            if (eventType == NM.EVENT_SYSTEM_MOVESIZESTART || eventType == NM.EVENT_SYSTEM_MOVESIZEEND) return true;
            return eventType == NM.EVENT_OBJECT_DESTROY && idObject == NM.OBJID_WINDOW && idChild == NM.CHILDID_SELF;
        }

        /// <summary>True for the messages a QuickPane window receives as its own move or size loop
        /// begins or ends (#159). The begin is the one that matters; see the class summary.</summary>
        internal static bool CheckOnMessage(int msg)
        {
            return msg == NM.WM_ENTERSIZEMOVE || msg == NM.WM_EXITSIZEMOVE;
        }

        private static void HookOwnWindow(Window window)
        {
            if (window == null) return;
            try
            {
                var source = PresentationSource.FromVisual(window) as HwndSource;
                if (source == null || source.IsDisposed) return;
                // Loaded can fire again for the same window; removing first keeps one hook per source.
                source.RemoveHook(OwnWindowHook);
                source.AddHook(OwnWindowHook);
                // The one trace that this hook is in place, since no test can show a window.
                Log.Info("stale source guard watches moves of " + window.GetType().Name + " " + source.Handle.ToString("X"));
            }
            catch (Exception ex) { Log.Error("stale source guard window hook", ex); }
        }

        private static IntPtr OnOwnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_running && CheckOnMessage(msg))
            {
                try { Check(IntPtr.Zero); }
                catch (Exception ex) { Log.Error("stale source check on own move", ex); }
            }
            return IntPtr.Zero;
        }

        private static void Check() { Check(IntPtr.Zero); }

        /// <param name="destroyed">The window whose EVENT_OBJECT_DESTROY prompted this check, or
        /// zero for every other trigger.</param>
        private static void Check(IntPtr destroyed)
        {
            bool leftToWatcher = false;
            // Copied first: releasing a source can change the live list while it is being walked.
            var sources = new List<HwndSource>();
            foreach (var o in PresentationSource.CurrentSources)
            {
                var source = o as HwndSource;
                if (source != null && source.CheckAccess() && !source.IsDisposed && source.RootVisual != null)
                    sources.Add(source);
            }

            foreach (var source in sources)
            {
                IntPtr dead;
                IntPtr hwnd = source.Handle;
                if (ChainIsAlive(hwnd, NM.IsWindow, NM.GetParent, out dead)) continue;
                if (LeaveToWatcher(dead, destroyed)) { leftToWatcher = true; continue; }

                Log.Event("glitch", "QuickPane window " + hwnd.ToString("X") + " sits under window " + dead.ToString("X") +
                    ", which no longer exists, so its content was released before WPF's text-input hook could reach the dead handle.");
                if (ReleaseContent(source, "stale source release")) continue;
                // Dispose clears the root visual before it destroys the window, so even if the
                // destroy fails on a dead handle the text boxes are already detached.
                try { source.Dispose(); } catch (Exception ex2) { Log.Error("stale source dispose", ex2); }
            }

            // Look again once this event has reached every sink, the watchers included, and release
            // whatever no watcher took, such as a window no watcher tracks. Send is the earliest a
            // posted check can run; it is not ordered against WPF's own hook.
            if (leftToWatcher && _running)
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Check));
        }

        /// <summary>Detach a pane's content from its window, so WPF's move/size hook no longer walks up
        /// from its text boxes. Pane teardown calls this first, before any call into the host's
        /// process that could block, and before HwndSource.Dispose, whose own release a dead handle
        /// could interrupt (#159). Returns false, after logging, when the release threw.</summary>
        internal static bool ReleaseContent(HwndSource source, string what)
        {
            if (source == null || source.IsDisposed || source.RootVisual == null) return true;
            try { source.RootVisual = null; return true; }
            catch (Exception ex) { Log.Error(what, ex); return false; }
        }

        /// <summary>True when <paramref name="hwnd"/> and every window above it, by the same
        /// GetParent walk WPF's move/size hook makes (a child's parent, a popup's owner), is still a
        /// window. Otherwise false, with <paramref name="dead"/> set to the first handle that is not.
        /// The walk ends where GetParent returns zero, or after <see cref="MaxChainDepth"/> steps.</summary>
        internal static bool ChainIsAlive(IntPtr hwnd, Func<IntPtr, bool> isWindow, Func<IntPtr, IntPtr> getParent, out IntPtr dead)
        {
            dead = IntPtr.Zero;
            for (int depth = 0; hwnd != IntPtr.Zero && depth < MaxChainDepth; depth++)
            {
                if (!isWindow(hwnd)) { dead = hwnd; return false; }
                hwnd = getParent(hwnd);
            }
            return true;
        }

        public void Dispose()
        {
            _running = false;
            if (_sweep != null) { _sweep.Stop(); _sweep = null; }
            if (_moveSize != null) { _moveSize.Dispose(); _moveSize = null; }
            if (_lifecycle != null) { _lifecycle.Dispose(); _lifecycle = null; }
        }
    }
}
