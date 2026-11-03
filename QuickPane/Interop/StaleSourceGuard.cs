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
    /// window so WPF's hook skips them. It runs on EVENT_SYSTEM_MOVESIZESTART, which Windows sends
    /// before the MOVESIZEEND that would trip WPF, and once a second for moves of QuickPane's own
    /// windows, whose events the shared hook skips. The owning watcher still tears the pane down on
    /// its own schedule; the guard only makes the gap until then safe.
    /// </summary>
    internal sealed class StaleSourceGuard : IDisposable
    {
        private const int MaxChainDepth = 64;   // a parent/owner loop must not hang the UI thread

        private WinEventHook _moveSize;
        private DispatcherTimer _sweep;

        public void Start()
        {
            // Same range as ExplorerFollowerWatcher and DialogPaneWatcher, so WinEventHub shares one
            // native hook among all three.
            _moveSize = new WinEventHook(NM.EVENT_SYSTEM_MOVESIZESTART, NM.EVENT_SYSTEM_MOVESIZEEND);
            _moveSize.Event += (type, hwnd, idObject, idChild, thread) =>
            {
                if (type == NM.EVENT_SYSTEM_MOVESIZESTART) Check();
            };
            _moveSize.Install();

            _sweep = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _sweep.Tick += (s, e) => Check();
            _sweep.Start();
        }

        private static void Check()
        {
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

                Log.Event("glitch", "QuickPane window " + hwnd.ToString("X") + " sits under window " + dead.ToString("X") +
                    ", which no longer exists, so its content was released before WPF's text-input hook could reach the dead handle.");
                try { source.RootVisual = null; }
                catch (Exception ex)
                {
                    // Dispose clears the root visual before it destroys the window, so even if the
                    // destroy fails on a dead handle the text boxes are already detached.
                    Log.Error("stale source release", ex);
                    try { source.Dispose(); } catch (Exception ex2) { Log.Error("stale source dispose", ex2); }
                }
            }
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
            if (_sweep != null) { _sweep.Stop(); _sweep = null; }
            if (_moveSize != null) { _moveSize.Dispose(); _moveSize = null; }
        }
    }
}
