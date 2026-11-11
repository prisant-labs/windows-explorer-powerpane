using System;
using System.Collections.Generic;
using PowerPane.Services;

namespace PowerPane.Interop
{
    /// <summary>
    /// Managed wrapper around SetWinEventHook. Installed with WINEVENT_OUTOFCONTEXT, so
    /// callbacks are marshalled to the message loop of the thread that created the hook
    /// (the WPF UI thread).
    ///
    /// Hooks for the same event range on the same thread share one native registration through
    /// WinEventHub. ExplorerWatcher, DialogPaneWatcher, and RecentTracker each hook location changes
    /// or foreground changes, and every native hook is a separate cross-process delivery of every
    /// matching event on the desktop, so the same event used to arrive two or three times
    /// (prisant-labs/windows-explorer-powerpane#43).
    /// </summary>
    internal sealed class WinEventHook : IDisposable
    {
        public event Action<uint, IntPtr, int, int, uint> Event;

        private readonly uint _min;
        private readonly uint _max;
        private object _token;

        public WinEventHook(uint eventMin, uint eventMax)
        {
            _min = eventMin;
            _max = eventMax;
        }

        public bool Install()
        {
            if (_token == null) _token = WinEventHub.Default.Attach(_min, _max, Raise);
            return _token != null;
        }

        private void Raise(uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread)
        {
            var handler = Event;
            if (handler != null) handler(eventType, hwnd, idObject, idChild, thread);
        }

        public void Dispose()
        {
            if (_token == null) return;
            WinEventHub.Default.Detach(_token);
            _token = null;
        }
    }

    /// <summary>
    /// Shares one native WinEvent hook among every subscriber for the same event range on the same
    /// thread. The first subscriber installs the hook and the last one to leave removes it. Each
    /// event is handed to every subscriber in turn, and one that throws is logged without stopping
    /// the rest, because a throw would otherwise unwind into unmanaged code. The native calls are
    /// injected so the sharing can be tested without hooking anything.
    /// </summary>
    internal sealed class WinEventHub
    {
        public delegate void Sink(uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread);

        internal static readonly WinEventHub Default = new WinEventHub(
            (min, max, proc) => NativeMethods.SetWinEventHook(min, max, IntPtr.Zero, proc, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS),
            NativeMethods.UnhookWinEvent,
            NativeMethods.GetCurrentThreadId);

        private sealed class Registration
        {
            public uint Min, Max, Thread;
            public IntPtr Handle;
            public NativeMethods.WinEventDelegate Proc; // held so the GC cannot collect it while the hook lives
            public Sink[] Sinks = new Sink[0];           // replaced, never mutated, so a delivery can walk it safely
        }

        private sealed class Token
        {
            public Registration Registration;
            public Sink Sink;
        }

        private readonly Func<uint, uint, NativeMethods.WinEventDelegate, IntPtr> _install;
        private readonly Func<IntPtr, bool> _uninstall;
        private readonly Func<uint> _currentThread;
        private readonly Action<string, Exception> _onError;
        private readonly List<Registration> _registrations = new List<Registration>();
        private readonly object _gate = new object();

        /// <summary>onError receives failures, with a null exception for a hook that would not install.
        /// It defaults to the log; tests pass their own so expected failures stay out of it.</summary>
        internal WinEventHub(Func<uint, uint, NativeMethods.WinEventDelegate, IntPtr> install,
            Func<IntPtr, bool> uninstall, Func<uint> currentThread, Action<string, Exception> onError = null)
        {
            _install = install;
            _uninstall = uninstall;
            _currentThread = currentThread;
            _onError = onError ?? LogError;
        }

        private static void LogError(string message, Exception ex)
        {
            if (ex == null) Log.Error(message);
            else Log.Error(message, ex);
        }

        /// <summary>Number of native hooks installed, for tests.</summary>
        internal int NativeHookCount { get { lock (_gate) return _registrations.Count; } }

        /// <summary>Subscribe a sink to an event range. Returns a token for Detach, or null when the
        /// native hook could not be installed.</summary>
        public object Attach(uint min, uint max, Action<uint, IntPtr, int, int, uint> sink)
        {
            if (sink == null) return null;
            Sink s = (e, h, o, c, t) => sink(e, h, o, c, t);
            uint thread = _currentThread();
            lock (_gate)
            {
                var reg = _registrations.Find(r => r.Min == min && r.Max == max && r.Thread == thread);
                if (reg == null)
                {
                    reg = new Registration { Min = min, Max = max, Thread = thread };
                    var captured = reg;
                    reg.Proc = (hook, e, h, o, c, t, time) => Deliver(captured, e, h, o, c, t);
                    reg.Handle = _install(min, max, reg.Proc);
                    if (reg.Handle == IntPtr.Zero)
                    {
                        _onError("SetWinEventHook failed for range " + min.ToString("X") + "-" + max.ToString("X"), null);
                        return null;
                    }
                    _registrations.Add(reg);
                }
                var sinks = new Sink[reg.Sinks.Length + 1];
                Array.Copy(reg.Sinks, sinks, reg.Sinks.Length);
                sinks[sinks.Length - 1] = s;
                reg.Sinks = sinks;
                return new Token { Registration = reg, Sink = s };
            }
        }

        /// <summary>Unsubscribe. Removes the native hook when its last sink leaves.
        ///
        /// Windows removes a hook only on the thread that installed it, and every caller today detaches
        /// on the UI thread. A detach from another thread, or an unhook that fails, is reported rather
        /// than dropped silently, because either would leave a native hook delivering events with no
        /// record of it here (prisant-labs/windows-explorer-powerpane#73). The unhook is still tried.</summary>
        public void Detach(object token)
        {
            var t = token as Token;
            if (t == null) return;
            lock (_gate)
            {
                var reg = t.Registration;
                int i = Array.IndexOf(reg.Sinks, t.Sink);
                if (i < 0) return;
                uint thread = _currentThread();
                if (thread != reg.Thread)
                    _onError("WinEvent hook for range " + Range(reg) + " detached on thread " + thread
                        + " but installed on thread " + reg.Thread, null);
                var sinks = new Sink[reg.Sinks.Length - 1];
                Array.Copy(reg.Sinks, 0, sinks, 0, i);
                Array.Copy(reg.Sinks, i + 1, sinks, i, reg.Sinks.Length - i - 1);
                reg.Sinks = sinks;
                if (sinks.Length > 0) return;
                _registrations.Remove(reg);
                if (!_uninstall(reg.Handle))
                    _onError("UnhookWinEvent failed for range " + Range(reg), null);
                reg.Handle = IntPtr.Zero;
            }
        }

        private static string Range(Registration reg)
        {
            return reg.Min.ToString("X") + "-" + reg.Max.ToString("X");
        }

        private void Deliver(Registration reg, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread)
        {
            foreach (var sink in reg.Sinks)
            {
                try { sink(eventType, hwnd, idObject, idChild, thread); }
                catch (Exception ex) { _onError("WinEvent handler threw", ex); }
            }
        }
    }
}
