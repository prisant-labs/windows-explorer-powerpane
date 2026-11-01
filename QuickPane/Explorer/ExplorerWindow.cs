using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media;
using QuickPane.Interop;
using QuickPane.Services;
using QuickPane.UI;
using NM = QuickPane.Interop.NativeMethods;

namespace QuickPane.Explorer
{
    /// <summary>
    /// Owns the embedded sidebar for a single Explorer window. It reparents a WPF HwndSource as a
    /// child of the CabinetWClass window and shifts Explorer's content hosts (one ShellTabWindowClass
    /// per tab) to the right by the sidebar width. Layout is reapplied whenever Explorer relays out,
    /// because Explorer restores its content hosts to full width on every resize.
    /// </summary>
    internal sealed class ExplorerWindow : IDisposable
    {
        private const string TabClass = "ShellTabWindowClass";

        private readonly IntPtr _cabinet;
        private readonly Action<string> _navOverride;
        private IntPtr _contentHost;      // the host found at attach; only the fallback layout relies on it
        private bool _tabbed;             // true when the content lives in ShellTabWindowClass children
        private HwndSource _host;
        private IntPtr _hostHandle; // cached so the background enforcer never touches WPF objects
        private SidebarControl _sidebar;
        private volatile int _width;
        private volatile int _contentTop;
        private volatile bool _applying;
        private volatile bool _disposed;
        private volatile bool _collapsed;
        private volatile bool _ready;     // false until the window is presented and its toolbar laid out
        private int _attachTick;          // tick recorded at attach, for the readiness grace fallback
        public Action OnFirstShown;       // watcher hooks this to run a brief hold-burst when the strip first shows
        private const int CollapsedPx = 36;
        private int _logCount;

        private int EffWidth() { return _collapsed ? CollapsedPx : _width; }

        public void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            _sidebar?.SetCollapsed(_collapsed);
            ApplyLayout();
        }

        public IntPtr Cabinet { get { return _cabinet; } }
        public bool Attached { get { return _host != null; } }

        /// <summary>True when hwnd is one of this window's content hosts, including a tab opened after
        /// attach. The watcher asks only about children of this cabinet, so the class lookup stays off
        /// the hot path of system-wide location events.</summary>
        public bool IsContentHost(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (hwnd == _contentHost) return true;
            return _tabbed && NM.GetParent(hwnd) == _cabinet && NM.ClassOf(hwnd) == TabClass;
        }

        /// <summary>
        /// Walk this window's content hosts: pass IntPtr.Zero for the first, then the previous host.
        /// Windows 11 gives every Explorer tab its own ShellTabWindowClass child. All of them stay
        /// visible, with the active tab on top, so shifting only the one found at attach left every
        /// other tab at full width, over or under the pane (lso2/QuickPane#3, prisant-labs/windows-explorer-quickpane#4).
        /// The tabs are walked live on every pass because they open and close while the window lives;
        /// a cached handle also stopped all layout once the tab it named was closed. FindWindowEx
        /// allocates nothing, so the enforcer thread can walk at its burst rate. If a tab closes
        /// mid-walk, FindWindowEx returns zero, the walk ends early, and the next pass catches up.
        /// </summary>
        private IntPtr NextContentHost(IntPtr after)
        {
            if (_tabbed) return NM.FindWindowEx(_cabinet, after, TabClass, null);
            return after == IntPtr.Zero && NM.IsWindow(_contentHost) ? _contentHost : IntPtr.Zero;
        }

        public ExplorerWindow(IntPtr cabinet, Action<string> navigate = null)
        {
            _cabinet = cabinet;
            _navOverride = navigate; // null -> drive a real Explorer window; set -> e.g. a file dialog
            _width = ClampWidth(App.Settings != null ? App.Settings.Current.SidebarWidthPx : 220);
        }

        public bool TryAttach()
        {
            if (_disposed || _host != null) return _host != null;
            if (!NM.IsWindow(_cabinet)) return false;

            _contentHost = FindContentHost(_cabinet);
            if (_contentHost == IntPtr.Zero) return false; // children not ready yet, caller retries
            _tabbed = NM.ClassOf(_contentHost) == TabClass;

            try
            {
                _sidebar = new SidebarControl();
                _sidebar.HostWindow = _cabinet;   // set before Attach, which is what builds the sections
                _sidebar.Attach(NavigateInThisWindow);
                _sidebar.TitleToggle = ToggleCollapsed;

                NM.RECT client;
                NM.GetClientRect(_cabinet, out client);
                _contentTop = MeasureContentTop(client);

                var p = new HwndSourceParameters("QuickPaneSidebar")
                {
                    ParentWindow = _cabinet,
                    WindowStyle = unchecked((int)(NM.WS_CHILD | NM.WS_CLIPSIBLINGS | NM.WS_CLIPCHILDREN)),
                    ExtendedWindowStyle = unchecked((int)NM.WS_EX_NOACTIVATE),
                    PositionX = 0,
                    PositionY = _contentTop,
                    Width = _width,
                    Height = Math.Max(0, client.Height - _contentTop)
                };
                _host = new HwndSource(p);
                _hostHandle = _host.Handle;
                _host.RootVisual = _sidebar;
                // Clear the child HWND to an opaque theme color, so even before the WPF visual tree
                // paints there is a solid strip. This both removes any see-through and acts as a
                // diagnostic: a blank colored strip would mean the HWND presents but the tree does not.
                bool dark = App.Theme != null && App.Theme.IsDark;
                _host.CompositionTarget.BackgroundColor = dark
                    ? Color.FromRgb(0x20, 0x20, 0x20)
                    : Color.FromRgb(0xF3, 0xF3, 0xF3);

                // Created hidden. ApplyLayout reveals the strip only once the window is presented and its
                // toolbar has laid out, so it never flashes blank or sits over the toolbar during load.
                _attachTick = Environment.TickCount;
                ApplyLayout();

                if (App.Settings != null)
                {
                    App.Settings.Changed += OnSettingsChanged;
                    App.Settings.WidthChangedLive += OnSettingsChanged;
                }
                Log.Info("Attached sidebar to Explorer window " + _cabinet.ToString("X") +
                         " (content host " + _contentHost.ToString("X") + ")");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TryAttach failed for " + _cabinet.ToString("X"), ex);
                SafeTeardownHost();
                return false;
            }
        }

        private void NavigateInThisWindow(string path)
        {
            App.Recents?.RecordNavigation(path);
            if (_navOverride != null) _navOverride(path);
            else ExplorerNavigator.NavigateAsync(_cabinet, path); // never block the input-attached UI thread
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            int w = ClampWidth(App.Settings.Current.SidebarWidthPx);
            if (w != _width)
            {
                _width = w;
                ApplyLayout();
            }
        }

        // The content hosts for one ApplyLayout pass, walked once and reused for measuring, checking,
        // and moving. Each of those used to walk the tabs again, so a pass walked them two or three
        // times, and the 1-second sweep runs a pass for every window
        // (prisant-labs/windows-explorer-quickpane#45). UI thread only; the enforcer walks live.
        private readonly List<IntPtr> _passHosts = new List<IntPtr>(4);

        private void CollectContentHosts(List<IntPtr> into)
        {
            into.Clear();
            for (var c = NextContentHost(IntPtr.Zero); c != IntPtr.Zero; c = NextContentHost(c)) into.Add(c);
        }

        private bool _inPass;

        /// <summary>Reposition the content host and the sidebar. Called on attach and on relayout.</summary>
        public void ApplyLayout()
        {
            // A pass that re-entered before the first finished would refill _passHosts while the first
            // is still reading it. The next event or sweep catches up instead.
            if (_inPass) return;
            _inPass = true;
            try { ApplyLayoutPass(); }
            finally { _inPass = false; }
        }

        private void ApplyLayoutPass()
        {
            if (_applying || _host == null) return;
            if (!NM.IsWindow(_cabinet)) return;
            // A tab that closes after this walk only makes its SetWindowPos fail harmlessly, and the
            // next pass walks again.
            CollectContentHosts(_passHosts);
            if (_passHosts.Count == 0) return;

            NM.RECT client;
            if (!NM.GetClientRect(_cabinet, out client)) return;
            int w = client.Width;
            int h = client.Height;
            int width = EffWidth();
            if (w <= width + 40 || h <= 0) return; // too small to split sanely

            int top = MeasureContentTop(client, _passHosts);

            // Hold everything until the window is actually presented and its toolbar has laid out.
            // Attaching mid-load measured contentTop as 0, which placed the strip over the toolbar and
            // shifted the content away while the strip's own window was still hidden, leaving the blank
            // gap on the left. Require the cabinet visible and a real content top, with a time fallback
            // so a window that never reports a toolbar still attaches.
            if (!_ready)
            {
                if (!NM.IsWindowVisible(_cabinet)) return;
                if (top <= 0 && Environment.TickCount - _attachTick < 5000) return;
                _ready = true;
                // Place the strip before revealing it, so it never flashes at its creation spot.
                NM.SetWindowPos(_host.Handle, NM.HWND_TOP, 0, top, width, Math.Max(0, h - top), NM.SWP_NOACTIVATE);
                NM.ShowWindow(_host.Handle, NM.SW_SHOWNA);
                try { OnFirstShown?.Invoke(); } catch { } // brief hold-burst while Explorer settles the new window
            }

            _contentTop = top;
            int contentH = h - top;

            // During a drag, leave Explorer's tabs where Explorer puts them and only keep the pane itself
            // the right size, hidden or not. The pane is our own window, so moving it fights nothing. A
            // resize the cursor test did not recognise (a keyboard resize) hides the pane at its first
            // size change.
            if (Yielding)
            {
                if (!_paneHiddenForDrag && (w != _dragStartW || h != _dragStartH)) HidePaneForDrag();
                if (!RectMatches(_host.Handle, 0, top, width, contentH))
                    NM.SetWindowPos(_host.Handle, NM.HWND_TOP, 0, top, width, contentH, NM.SWP_NOACTIVATE | NM.SWP_DEFERERASE);
                return;
            }

            int sidebarX = 0, sidebarY = top, sidebarW = width, sidebarH = contentH;
            int contentX = width, contentY = top, contentW = w - width, contentH2 = contentH;

            // Skip if every host is already where we want it (within tolerance) so we do not fight
            // Explorer's own identical relayout and create a feedback loop. Our own moves raise location
            // events that land back here, find everything placed, and stop.
            if (AllContentPlaced(_passHosts, contentX, contentY, contentW, contentH2)) return;

            bool verbose = _logCount < 3;
            if (verbose)
            {
                _logCount++;
                Log.Info("ApplyLayout cabinet=" + _cabinet.ToString("X") + " client=" + w + "x" + h +
                         " contentTop=" + top + " sidebar=(" + sidebarX + "," + sidebarY + " " + sidebarW + "x" + sidebarH +
                         ") content=(" + contentX + "," + contentY + " " + contentW + "x" + contentH2 + ")");
            }

            _applying = true;
            try
            {
                // Independent SetWindowPos calls, NOT a DeferWindowPos batch. DeferWindowPos cannot
                // combine windows from different threads, and Explorer's content hosts live in another
                // process, so a batch silently fails and nothing moves. Separate calls each succeed:
                // every host that has drifted shifts right, and our sidebar is forced to the TOP so
                // Explorer's content can never paint over it (SetParent leaves it at the bottom).
                foreach (var c in _passHosts)
                {
                    if (!NeedsMove(c, contentX, contentY, contentW, contentH2)) continue;
                    NM.SetWindowPos(c, IntPtr.Zero, contentX, contentY, contentW, contentH2,
                        NM.SWP_NOZORDER | NM.SWP_NOACTIVATE | NM.SWP_DEFERERASE);
                }
                NM.SetWindowPos(_host.Handle, NM.HWND_TOP, sidebarX, sidebarY, sidebarW, sidebarH,
                    NM.SWP_NOACTIVATE | NM.SWP_DEFERERASE);
            }
            catch (Exception ex)
            {
                Log.Error("ApplyLayout failed", ex);
            }
            finally
            {
                _applying = false;
            }

            if (verbose)
            {
                NM.RECT hr, cr;
                NM.GetWindowRect(_host.Handle, out hr);
                NM.GetWindowRect(_passHosts[0], out cr);
                Log.Info("  post-layout hostHwnd=" + _host.Handle.ToString("X") +
                         " hostScreenRect=(" + hr.Left + "," + hr.Top + " " + hr.Width + "x" + hr.Height + ")" +
                         " hostVisible=" + NM.IsWindowVisible(_host.Handle) +
                         " hostParentIsCabinet=" + (NM.GetParent(_host.Handle) == _cabinet) +
                         " | contentScreenRect=(" + cr.Left + "," + cr.Top + " " + cr.Width + "x" + cr.Height + ")");
            }
        }

        public bool IsApplying { get { return _applying; } }

        /// <summary>True while the user drags this window. The tabs are then left where Explorer puts
        /// them until the drag ends; see ExplorerWatcher._sizing for why.</summary>
        public volatile bool Yielding;

        private bool _paneHiddenForDrag;      // UI thread only
        private int _dragStartW, _dragStartH; // client size when the drag began

        /// <summary>A drag of this window began. For a resize, the pane hides, so Explorer's own layout,
        /// its tree included, shows whole and steady until the drag ends (#102). For a move it stays up,
        /// because a move never changes the layout.</summary>
        public void BeginDrag(bool resizing)
        {
            NM.RECT client;
            if (NM.GetClientRect(_cabinet, out client)) { _dragStartW = client.Width; _dragStartH = client.Height; }
            Yielding = true;
            if (resizing) HidePaneForDrag();
        }

        /// <summary>The drag ended, or it is being abandoned: reapply the split, then show the pane.
        /// Showing it after the layout pass means it reappears beside Explorer's tree rather than over it.</summary>
        public void EndDrag()
        {
            Yielding = false;
            ApplyLayout();
            if (!_paneHiddenForDrag) return;
            _paneHiddenForDrag = false;
            if (_host != null && _ready && !_disposed) NM.ShowWindow(_host.Handle, NM.SW_SHOWNA);
        }

        private void HidePaneForDrag()
        {
            if (_host == null || !_ready || _paneHiddenForDrag) return;
            _paneHiddenForDrag = true;
            NM.ShowWindow(_host.Handle, NM.SW_HIDE);
        }

        /// <summary>
        /// Hold the content pane and sidebar in place using only Win32 calls, safe to invoke from the
        /// background enforcer thread. It reads nothing from WPF, acts only when something has drifted,
        /// and corrects Explorer's activation reset within a couple milliseconds so the move is never
        /// presented to the screen.
        /// </summary>
        public void EnforceFast()
        {
            var host = _hostHandle;
            if (_disposed || host == IntPtr.Zero) return;
            if (!_ready) return; // nothing is shifted or shown until the window is presented
            if (_applying || Yielding) return;
            if (!NM.IsWindow(_cabinet)) return;

            NM.RECT client;
            if (!NM.GetClientRect(_cabinet, out client)) return;
            int w = client.Width, h = client.Height;
            int width = EffWidth(), top = _contentTop;
            if (w <= width + 40 || h <= 0) return;
            int contentH = h - top;

            for (var c = NextContentHost(IntPtr.Zero); c != IntPtr.Zero; c = NextContentHost(c))
            {
                if (!NeedsMove(c, width, top, w - width, contentH)) continue;
                NM.SetWindowPos(c, IntPtr.Zero, width, top, w - width, contentH,
                    NM.SWP_NOZORDER | NM.SWP_NOACTIVATE | NM.SWP_DEFERERASE);
            }
            if (!RectMatches(host, 0, top, width, contentH))
            {
                NM.SetWindowPos(host, NM.HWND_TOP, 0, top, width, contentH,
                    NM.SWP_NOACTIVATE | NM.SWP_DEFERERASE);
            }
        }

        // True if hwnd already sits at (x,y,cx,cy) in cabinet client coordinates, within a 2px slop.
        private bool RectMatches(IntPtr hwnd, int x, int y, int cx, int cy)
        {
            NM.RECT r;
            if (!NM.GetWindowRect(hwnd, out r)) return false;
            var tl = new NM.POINT { X = r.Left, Y = r.Top };
            NM.ScreenToClient(_cabinet, ref tl);
            return Near(tl.X, x) && Near(tl.Y, y) && Near(r.Width, cx) && Near(r.Height, cy);
        }

        // True when a content host sits somewhere other than (x,y,cx,cy). A tab Explorer has not sized
        // yet is left alone: its first layout raises a location event that brings it back here.
        private bool NeedsMove(IntPtr hwnd, int x, int y, int cx, int cy)
        {
            NM.RECT r;
            if (!NM.GetWindowRect(hwnd, out r) || r.Width <= 0 || r.Height <= 0) return false;
            var tl = new NM.POINT { X = r.Left, Y = r.Top };
            NM.ScreenToClient(_cabinet, ref tl);
            return !(Near(tl.X, x) && Near(tl.Y, y) && Near(r.Width, cx) && Near(r.Height, cy));
        }

        private bool AllContentPlaced(List<IntPtr> hosts, int x, int y, int cx, int cy)
        {
            foreach (var c in hosts)
            {
                if (NeedsMove(c, x, y, cx, cy)) return false;
            }
            return true;
        }

        private static bool Near(int a, int b) { return Math.Abs(a - b) <= 2; }

        // Content top in cabinet client coordinates (below the toolbar/ribbon band). Every tab shares it.
        // The highest reading wins, because a tab Explorer is still creating can sit at the client's top
        // edge for a moment, and reading 0 from it would pull the pane and every tab up over the toolbar.
        private int MeasureContentTop(NM.RECT client)
        {
            var hosts = new List<IntPtr>(4);
            CollectContentHosts(hosts);
            return MeasureContentTop(client, hosts);
        }

        private int MeasureContentTop(NM.RECT client, List<IntPtr> hosts)
        {
            int best = 0;
            try
            {
                foreach (var c in hosts)
                {
                    NM.RECT r;
                    if (!NM.GetWindowRect(c, out r) || r.Width <= 0 || r.Height <= 0) continue;
                    var tl = new NM.POINT { X = r.Left, Y = r.Top };
                    NM.ScreenToClient(_cabinet, ref tl);
                    if (tl.Y >= 0 && tl.Y < client.Height && tl.Y > best) best = tl.Y;
                }
            }
            catch { }
            return best;
        }

        /// <summary>Find the child window that hosts the folder content, so we can shift it right.</summary>
        private static IntPtr FindContentHost(IntPtr cabinet)
        {
            // Win10 folder windows nest the content in ShellTabWindowClass.
            var tab = NM.FindWindowEx(cabinet, IntPtr.Zero, "ShellTabWindowClass", null);
            if (tab != IntPtr.Zero) return tab;

            // Fallback: the largest direct child that is not our own sidebar.
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            NM.EnumChildWindows(cabinet, (h, l) =>
            {
                if (NM.GetParent(h) != cabinet) return true; // direct children only
                var cls = NM.ClassOf(h);
                if (cls == "QuickPaneSidebar") return true;
                NM.RECT rr;
                if (NM.GetWindowRect(h, out rr))
                {
                    long area = (long)Math.Max(0, rr.Width) * Math.Max(0, rr.Height);
                    if (area > bestArea) { bestArea = area; best = h; }
                }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        private static int ClampWidth(int w)
        {
            if (w < 160) return 160;
            if (w > 400) return 400;
            return w;
        }

        // ---- teardown --------------------------------------------------------

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (App.Settings != null)
            {
                App.Settings.Changed -= OnSettingsChanged;
                App.Settings.WidthChangedLive -= OnSettingsChanged;
            }

            // Restore Explorer's content hosts to full width so the window looks normal afterwards.
            try
            {
                if (_ready && NM.IsWindow(_cabinet))
                {
                    NM.RECT client;
                    if (NM.GetClientRect(_cabinet, out client))
                    {
                        int top = _contentTop;
                        for (var c = NextContentHost(IntPtr.Zero); c != IntPtr.Zero; c = NextContentHost(c))
                        {
                            NM.SetWindowPos(c, IntPtr.Zero, 0, top, client.Width, client.Height - top,
                                NM.SWP_NOZORDER | NM.SWP_NOACTIVATE);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("restore content host failed", ex);
            }

            SafeTeardownHost();
        }

        private void SafeTeardownHost()
        {
            _hostHandle = IntPtr.Zero; // stop the enforcer touching this window first
            try { _sidebar?.Detach(); } catch (Exception ex) { Log.Error("sidebar detach", ex); }
            try { _host?.Dispose(); } catch (Exception ex) { Log.Error("host dispose", ex); }
            _host = null;
            _sidebar = null;
        }
    }
}
