using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using PowerPane.Interop;
using PowerPane.Services;

namespace PowerPane.Explorer
{
    /// <summary>
    /// Hears every Explorer folder change as it happens, so WindowFolderHistory knows each folder a
    /// window visited however briefly. RecentTracker polls every two seconds, and a visit shorter than
    /// that (into a subfolder and straight back, or two levels up in a row) never reached the history,
    /// so Copy path took the subfolder Explorer selects after Up or Back for a choice the user made.
    /// Each Explorer tab is a shell window that raises DWebBrowserEvents2.NavigateComplete2, with the
    /// new folder's path as its URL argument; this class subscribes to every one.
    ///
    /// Measured in Windows Sandbox before this was written. The listener runs on its own MTA thread.
    /// Explorer calls an event sink synchronously from its own UI thread, so a sink that waited on a
    /// message loop could stall Explorer's navigation; on an STA, a Navigate2 made from the same thread
    /// as the sink hung outright. From an MTA thread, events arrive on COM's own worker threads with no
    /// message loop at all. The cost is that QueryService for the tab's handle fails from this
    /// apartment, so the history is kept per Explorer window rather than per tab, and deep enough
    /// (WindowFolderHistory.Depth) to cover a few navigations in another tab of the same window.
    ///
    /// The sink takes the folder from the event's own argument and records it under a lock. It never
    /// calls back into Explorer, because a later read would already see the folder after the next
    /// navigation, which is exactly the visit this class exists to catch. New windows and tabs are
    /// picked up from DShellWindowsEvents.WindowRegistered, with a slow fallback check of the window
    /// count, and everything is rebuilt after a COM failure, such as an Explorer restart.
    /// </summary>
    internal sealed class NavigationListener : IDisposable
    {
        private const int FallbackCheckMs = 5000;
        private const int RetryAfterFailureMs = 3000;
        private const int MaxWindows = 64;

        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private volatile bool _stop;
        private Thread _thread;

        // Listener thread only.
        private object _shell, _windows;
        private IConnectionPoint _windowsPoint;
        private int _windowsCookie;
        private readonly List<Subscription> _subs = new List<Subscription>();
        private int _lastCount = -1;
        private volatile bool _dirty = true;
        private bool _retrySoon;
        private const int RetrySoonMs = 300;

        private sealed class Subscription
        {
            public object Window;
            public IConnectionPoint Point;
            public int Cookie;
            public NavigationBrowserSink Sink;
        }

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop) { IsBackground = true, Name = "PowerPane navigation listener" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        private void Loop()
        {
            while (!_stop)
            {
                int wait = FallbackCheckMs;
                try
                {
                    _retrySoon = false;
                    EnsureShell();
                    int count = ToInt(Get(_windows, "Count"));
                    if (_dirty || count != _lastCount) Sync(count);
                    PruneClosed();
                    if (_retrySoon) wait = RetrySoonMs;
                }
                catch (Exception ex)
                {
                    // Explorer restarting is the usual cause: every proxy held here is now dead.
                    Log.Event("navigation listener", "shell windows unavailable; rebuilding", ex);
                    Reset(unadvise: false);
                    wait = RetryAfterFailureMs;
                }
                _wake.WaitOne(wait);
            }
            Reset(unadvise: true);
        }

        private void EnsureShell()
        {
            if (_windows != null) return;
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) throw new InvalidOperationException("Shell.Application is not registered.");
            _shell = Activator.CreateInstance(type);
            _windows = Call(_shell, "Windows");
            var container = _windows as IConnectionPointContainer;
            if (container != null)
            {
                Guid iid = typeof(DShellWindowsEvents).GUID;
                container.FindConnectionPoint(ref iid, out _windowsPoint);
                _windowsPoint.Advise(new NavigationWindowsSink(OnWindowsChanged), out _windowsCookie);
            }
            _dirty = true;
        }

        /// <summary>Subscribes to every shell window not already followed. The same COM object comes
        /// back as the same RCW on this thread, so a window seen before is recognized by reference and
        /// never subscribed twice; a second subscription would record every navigation twice.</summary>
        private void Sync(int count)
        {
            _dirty = false;
            _lastCount = count;
            int added = 0;
            for (int i = 0; i < count && _subs.Count < MaxWindows; i++)
            {
                object w = null;
                try { w = Call(_windows, "Item", i); }
                catch { }
                if (w == null) continue;
                if (Followed(w)) continue;

                var container = w as IConnectionPointContainer;
                if (container == null) { Release(w); continue; }
                try
                {
                    Guid iid = typeof(DWebBrowserEvents2).GUID;
                    IConnectionPoint point;
                    container.FindConnectionPoint(ref iid, out point);
                    // A precaution, never seen in testing: a window caught the moment it registers might
                    // not have its HWND yet, and its events would be recorded under no window and lost.
                    // Leave it unfollowed and try again shortly.
                    long hwnd = ToLong(Get(w, "HWND"));
                    if (hwnd == 0) { Release(w); _dirty = true; _retrySoon = true; continue; }
                    var sink = new NavigationBrowserSink(new IntPtr(hwnd), _wake);
                    int cookie;
                    point.Advise(sink, out cookie);
                    _subs.Add(new Subscription { Window = w, Point = point, Cookie = cookie, Sink = sink });
                    added++;
                }
                catch (Exception ex)
                {
                    Log.Event("navigation listener", "could not follow a shell window", ex);
                    Release(w);
                }
            }
            if (added > 0)
            {
                var windows = new List<string>();
                foreach (var sub in _subs) windows.Add(sub.Sink.Window.ToString("X"));
                Log.Event("navigation listener", "following " + _subs.Count + " Explorer tab(s) in window(s) " + string.Join(",", windows));
            }
        }

        // Raised on a COM worker thread when a window or tab opens or closes.
        private void OnWindowsChanged() { _dirty = true; try { _wake.Set(); } catch { } }

        private bool Followed(object w)
        {
            foreach (var s in _subs) if (ReferenceEquals(s.Window, w)) return true;
            return false;
        }

        /// <summary>Drops the tabs that announced OnQuit. Their windows are gone, so unadvising is a
        /// courtesy that may fail.</summary>
        private void PruneClosed()
        {
            for (int i = _subs.Count - 1; i >= 0; i--)
            {
                if (!_subs[i].Sink.Quit) continue;
                Drop(_subs[i], unadvise: true);
                _subs.RemoveAt(i);
                _dirty = true;
            }
        }

        private void Reset(bool unadvise)
        {
            foreach (var s in _subs) Drop(s, unadvise);
            _subs.Clear();
            if (_windowsPoint != null)
            {
                if (unadvise) { try { _windowsPoint.Unadvise(_windowsCookie); } catch { } }
                Release(_windowsPoint);
                _windowsPoint = null;
            }
            Release(_windows); _windows = null;
            Release(_shell); _shell = null;
            _lastCount = -1;
            _dirty = true;
        }

        private static void Drop(Subscription s, bool unadvise)
        {
            if (unadvise) { try { s.Point.Unadvise(s.Cookie); } catch { } }
            Release(s.Point);
            Release(s.Window);
        }

        /// <summary>Stops the listener and unsubscribes from every tab. Unsubscribing is a cross-process
        /// call, so it is given a moment and then abandoned: a PowerPane that has exited leaves Explorer
        /// holding dead proxies, whose events fail at once rather than wait.</summary>
        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            try { _thread?.Join(2000); } catch { }
        }

        /// <summary>The folder a NavigateComplete2 reports, or null when it is not a place on disk. Its
        /// URL argument is the folder's own path, such as C:\Demo or C:\, the same form ExplorerNavigator
        /// reads, and a shell: path arrives already resolved. Anything else, such as This PC's
        /// "::{GUID}" name, is not a folder Copy path could act on.</summary>
        internal static string FolderFromUrl(object url)
        {
            var s = url as string;
            if (string.IsNullOrEmpty(s)) return null;
            if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                try { s = new Uri(s).LocalPath; }
                catch (UriFormatException) { return null; }
            }
            return ExplorerNavigator.IsFileSystemPath(s) ? s : null;
        }

        private static object Get(object o, string name)
        {
            return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);
        }

        private static object Call(object o, string name, params object[] args)
        {
            return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args);
        }

        private static int ToInt(object v)
        {
            try { return v == null ? 0 : Convert.ToInt32(v); }
            catch { return 0; }
        }

        private static long ToLong(object v)
        {
            try { return v == null ? 0 : Convert.ToInt64(v); }
            catch { return 0; }
        }

        private static void Release(object o)
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
            catch { }
        }
    }

    // The event interfaces and sinks are public and ComVisible on purpose. The assembly is
    // ComVisible(false), and COM can see only public types, so as nested or internal types Explorer's
    // QueryInterface for the event interface would fail and Advise would refuse without saying why.

    [ComVisible(true), ComImport, Guid("34A715A0-6587-11D0-924A-0020AFC7AC4D"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface DWebBrowserEvents2
    {
        [DispId(252)] void NavigateComplete2([MarshalAs(UnmanagedType.IDispatch)] object pDisp, [In] ref object URL);
        [DispId(253)] void OnQuit();
    }

    [ComVisible(true), ComImport, Guid("FE4106E0-399A-11D0-A48C-00A0C90A8F39"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface DShellWindowsEvents
    {
        [DispId(200)] void WindowRegistered(int lCookie);
        [DispId(201)] void WindowRevoked(int lCookie);
    }

    /// <summary>One tab's events. Runs on a COM worker thread while Explorer waits, so it only records
    /// under WindowFolderHistory's lock and never calls Explorer.</summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class NavigationBrowserSink : DWebBrowserEvents2
    {
        private readonly IntPtr _window;
        private readonly AutoResetEvent _wake;
        private volatile bool _quit;

        internal NavigationBrowserSink(IntPtr window, AutoResetEvent wake) { _window = window; _wake = wake; }

        internal IntPtr Window { get { return _window; } }

        internal bool Quit { get { return _quit; } }

        public void NavigateComplete2(object pDisp, ref object URL)
        {
            try
            {
                var folder = NavigationListener.FolderFromUrl(URL);
                if (folder != null) WindowFolderHistory.Record(_window, folder);
            }
            catch { }   // never throw into Explorer
        }

        public void OnQuit()
        {
            _quit = true;
            try { _wake.Set(); } catch { }
        }
    }

    /// <summary>Tells the listener a window or tab opened or closed, so it subscribes at once rather
    /// than at the next fallback check.</summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class NavigationWindowsSink : DShellWindowsEvents
    {
        private readonly Action _changed;
        internal NavigationWindowsSink(Action changed) { _changed = changed; }
        public void WindowRegistered(int lCookie) { try { _changed(); } catch { } }
        public void WindowRevoked(int lCookie) { try { _changed(); } catch { } }
    }
}
