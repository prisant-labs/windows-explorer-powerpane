using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using QuickPane.Interop;
using QuickPane.Services;
using QuickPane.UI;
using NM = QuickPane.Interop.NativeMethods;

namespace QuickPane.Explorer
{
    /// <summary>
    /// Optional desktop dock. A borderless top-level window at the left edge of the screen. Every
    /// folder click opens a new Explorer window. In reserved mode it registers as an AppBar so the
    /// desktop keeps that strip clear. In auto-hide mode it is a 1px trigger that slides out on hover
    /// and back when the cursor leaves, overlaying whatever is beneath it.
    /// </summary>
    internal sealed class AppBarHost : IDisposable
    {
        private readonly bool _autoHide;
        private readonly bool _allDesktops;
        private Window _window;
        private IntPtr _handle;
        private uint _callbackMsg;
        private bool _registered;
        private NM.RECT _reserved;      // the strip last passed to ABM_SETPOS
        private bool _haveReserved;
        private string _lastMonitorId;  // the resolved monitor's Id as of the last reposition
        private bool _haveLastMonitorId;
        private SidebarControl _sidebar;
        private DispatcherTimer _collapseTimer;
        private DispatcherTimer _pinTimer;
        private WinEventHook _foregroundHook;
        private IntPtr _lastExplorer;
        private bool _expanded;
        private bool _disposed;

        public AppBarHost(bool autoHide, bool allDesktops) { _autoHide = autoHide; _allDesktops = allDesktops; }

        public void Start()
        {
            bool dark = App.Theme != null && App.Theme.IsDark;
            _sidebar = new SidebarControl();
            _sidebar.Attach(NavigateOrOpen); // navigate the active Explorer, or open a new one
            _sidebar.TitleToggle = () =>
            {
                // For the dock, the title toggle flips auto-hide on or off.
                var st = App.Settings.Current;
                st.DesktopDockAutoHide = !st.DesktopDockAutoHide;
                App.Settings.Save();
            };

            var wa = SystemParameters.WorkArea;
            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = _autoHide, // overlay when auto-hiding; reserved mode does not need topmost
                Title = "QuickPane",
                Background = new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3)),
                Content = _sidebar,
                Left = wa.Left,
                Top = wa.Top,
                Height = wa.Height,
                Width = _autoHide ? 1 : WidthDip()
            };
            App.Theme?.AttachWindow(_window);
            _window.SourceInitialized += OnSourceInitialized;
            _window.Closed += (s, e) => RemoveAppBar();
            _window.Show();

            if (_allDesktops) PinAllDesktops();

            if (_autoHide)
            {
                _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                _collapseTimer.Tick += (s, e) =>
                {
                    _collapseTimer.Stop();
                    // Do not collapse while a drag or a context menu is in progress, or while the
                    // cursor is still over the pane (a menu popup sits outside the window bounds).
                    if (SidebarControl.SuppressAutoHide || CursorOverWindow()) { _collapseTimer.Start(); return; }
                    Collapse();
                };
                _window.MouseEnter += (s, e) => { _collapseTimer.Stop(); Expand(); };
                _window.MouseLeave += (s, e) => { _collapseTimer.Stop(); _collapseTimer.Start(); };
            }

            if (App.Settings != null)
            {
                App.Settings.Changed += OnSettingsChanged;
                App.Settings.WidthChangedLive += OnSettingsChanged;
            }

            // Track the last active Explorer window so a dock click navigates it instead of always
            // opening a new one.
            _foregroundHook = new WinEventHook(NM.EVENT_SYSTEM_FOREGROUND, NM.EVENT_SYSTEM_FOREGROUND);
            _foregroundHook.Event += OnForeground;
            _foregroundHook.Install();
        }

        // Pin the dock to every virtual desktop. This uses the same private shell interface that Task
        // View's "Show this window on all desktops" uses; there is no documented API for it. The window's
        // application view only exists after it has rendered, so the first attempts right after Show can
        // no-op. Retry a few times until it takes (or give up quietly on builds that do not support it).
        private void PinAllDesktops()
        {
            if (_pinTimer != null) return;
            int attempts = 0;
            _pinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _pinTimer.Tick += (s, e) =>
            {
                attempts++;
                bool ok = _handle != IntPtr.Zero && VirtualDesktop.PinWindow(_handle);
                if (ok || attempts >= 8) { _pinTimer.Stop(); _pinTimer = null; }
            };
            _pinTimer.Start();
        }

        private void OnForeground(uint evt, IntPtr hwnd, int idObject, int idChild, uint thread)
        {
            var c = NM.ClassOf(hwnd);
            if (c == "CabinetWClass" || c == "ExploreWClass") _lastExplorer = hwnd;
        }

        private int WidthPx()
        {
            int w = App.Settings != null ? App.Settings.Current.SidebarWidthPx : 220;
            if (w < 160) w = 160; if (w > 400) w = 400;
            return w;
        }

        private double WidthDip()
        {
            double scale = 1.0;
            try { if (_window != null) scale = VisualTreeHelper.GetDpi(_window).DpiScaleX; } catch { }
            if (scale <= 0) scale = 1.0;
            return WidthPx() / scale;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _handle = new WindowInteropHelper(_window).Handle;
            HwndSource.FromHwnd(_handle)?.AddHook(WndProc);

            // Tool window keeps the dock out of the Alt+Tab switcher, but a tool window has no shell
            // application view and so cannot be pinned to all virtual desktops. When the user wants it on
            // every desktop, leave it as a normal (pinnable) window; otherwise hide it from Alt+Tab.
            var ex = NM.GetWindowLongPtr(_handle, NM.GWL_EXSTYLE).ToInt64();
            if (!_allDesktops)
                NM.SetWindowLongPtr(_handle, NM.GWL_EXSTYLE, new IntPtr(ex | NM.WS_EX_TOOLWINDOW));

            if (_autoHide)
            {
                SetAutoHide(false); // start collapsed at the left edge
            }
            else
            {
                _callbackMsg = NM.RegisterWindowMessage("QuickPaneAppBarMessage");
                var abd = new NM.APPBARDATA { cbSize = Marshal.SizeOf(typeof(NM.APPBARDATA)), hWnd = _handle, uCallbackMessage = _callbackMsg };
                NM.SHAppBarMessage(NM.ABM_NEW, ref abd);
                _registered = true;
                SetReservedPosition();
            }
        }

        /// <summary>Work area of the monitor the dock is on. The primary screen's work area describes
        /// the wrong rectangle entirely on a second display, which put the dock on the wrong screen and
        /// gave it the wrong height. Since #146 "the monitor the dock is on" is the user's chosen
        /// display (Settings > Pane > Desktop dock > Display), not simply the one the window happens
        /// to sit nearest to.</summary>
        private bool GetWorkArea(out NM.RECT rc)
        {
            rc = ResolveChosenMonitor().WorkRect;
            return rc.Width > 0 && rc.Height > 0;
        }

        /// <summary>The monitor this dock should sit on: the user's Settings > Pane choice (by
        /// MonitorPicker's stable id), or the live OS primary when nothing was chosen or the chosen
        /// one is not connected (#146). Falls back to the window's own nearest-monitor lookup, the
        /// pre-#146 behavior, only when Windows reports no monitors at all through EnumDisplayMonitors
        /// -- which should not happen on a real desktop, but a dock that still places itself somewhere
        /// beats one that throws.</summary>
        private MonitorDescriptor ResolveChosenMonitor()
        {
            var monitors = MonitorPicker.DescribeMonitors();
            if (monitors.Count == 0)
            {
                return new MonitorDescriptor
                {
                    Id = null,
                    MonitorRect = NM.MonitorRectFor(_handle),
                    WorkRect = NM.WorkAreaFor(_handle),
                    IsPrimary = true,
                };
            }
            string storedId = App.Settings != null ? App.Settings.Current.DesktopDockDisplay : null;
            return MonitorPicker.ResolveMonitor(monitors, storedId);
        }

        // Position the auto-hide dock on the LEFT screen edge, full work-area height. Collapsed it is a
        // thin trigger strip; expanded it grows rightward keeping its left edge at the screen edge.
        private void SetAutoHide(bool expanded)
        {
            if (_handle == IntPtr.Zero) return;
            NM.RECT wa; GetWorkArea(out wa);
            int width = expanded ? WidthPx() : 4;
            int top = wa.Top;
            int height = wa.Bottom - wa.Top;
            if (!expanded)
            {
                // Leave the top and bottom corners out of the collapsed trigger so the maximized-window
                // menu (top-left) and the taskbar/Start corner (bottom-left) stay reachable without the
                // pane sliding out by accident. The expanded pane still spans the full height.
                const int dead = 46;
                if (height > 2 * dead + 10) { top = wa.Top + dead; height = (wa.Bottom - wa.Top) - 2 * dead; }
            }
            NM.SetWindowPos(_handle, NM.HWND_TOP, wa.Left, top, width, height,
                NM.SWP_NOACTIVATE | NM.SWP_SHOWWINDOW);
        }

        private void SetReservedPosition()
        {
            if (_handle == IntPtr.Zero) return;
            int width = WidthPx();

            // Which monitor to reserve against: the user's Settings > Pane > Desktop dock > Display
            // choice, resolved live (#146). When that choice changed since the last call -- a
            // different pick, or a fallback to/from the primary because the chosen monitor connected
            // or disconnected -- the old reservation belongs to a different monitor's geometry, so
            // forget it and force a fresh ABM_SETPOS even if the newly queried rect coincidentally
            // matches the old one. Repeated calls with the same monitor keep the #145 guard below.
            var monitor = ResolveChosenMonitor();
            if (!_haveLastMonitorId || !string.Equals(monitor.Id, _lastMonitorId, StringComparison.Ordinal))
            {
                _haveReserved = false;
                _lastMonitorId = monitor.Id;
                _haveLastMonitorId = true;
            }

            var abd = new NM.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(NM.APPBARDATA)),
                hWnd = _handle,
                uEdge = NM.ABE_LEFT
            };
            // Reserve against the monitor the dock occupies, so a second screen gets a strip of the
            // right height in the right place instead of the primary screen's geometry. The proposal
            // starts from the monitor's full bounds and QUERYPOS moves it clear of the taskbar and any
            // other appbar. Starting from the work area made every call begin one dock width further
            // right, because the work area already excludes the strip this dock holds, so each settings
            // save or ABN_POSCHANGED walked the dock across the screen and squeezed every maximized
            // window into what was left (#145).
            abd.rc = ProposedStrip(monitor.MonitorRect, width);
            NM.SHAppBarMessage(NM.ABM_QUERYPOS, ref abd);
            abd.rc.Right = abd.rc.Left + width;

            // Every SETPOS makes Windows recompute the work area and re-lay-out the desktop, and this
            // runs on every settings save, so only reserve again when the strip actually changes.
            if (_haveReserved && SameRect(abd.rc, _reserved)) return;
            NM.SHAppBarMessage(NM.ABM_SETPOS, ref abd);
            _reserved = abd.rc; _haveReserved = true;
            NM.MoveWindow(_handle, abd.rc.Left, abd.rc.Top, abd.rc.Right - abd.rc.Left, abd.rc.Bottom - abd.rc.Top, true);
        }

        /// <summary>The strip a left-edge dock asks the shell for: the monitor's full height, at its
        /// left edge, <paramref name="widthPx"/> wide. It depends only on the monitor and the width,
        /// never on the work area, so asking twice asks for the same strip (#145).</summary>
        internal static NM.RECT ProposedStrip(NM.RECT monitor, int widthPx)
        {
            return new NM.RECT { Left = monitor.Left, Top = monitor.Top, Right = monitor.Left + widthPx, Bottom = monitor.Bottom };
        }

        internal static bool SameRect(NM.RECT a, NM.RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        private bool CursorOverWindow()
        {
            if (_handle == IntPtr.Zero) return false;
            NM.POINT pt;
            if (!NM.GetCursorPos(out pt)) return false;
            NM.RECT r;
            if (!NM.GetWindowRect(_handle, out r)) return false;
            return pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;
        }

        private void Expand()
        {
            if (_expanded) return;
            _expanded = true;
            SetAutoHide(true);
        }

        private void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            SetAutoHide(false);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NM.WM_DISPLAYCHANGE)
            {
                // A monitor was connected, disconnected, rearranged, or changed resolution (#146).
                // SetReservedPosition and SetAutoHide each already skip re-asking the shell (or
                // re-positioning) for a strip identical to the last one, so a burst of DISPLAYCHANGE
                // messages from one physical change does not loop (the same guard #145 added).
                try { if (_autoHide) SetAutoHide(_expanded); else SetReservedPosition(); }
                catch (Exception ex) { Log.Error("appbar displaychange", ex); }
                // Not marking handled: WM_DISPLAYCHANGE is informational, and other code on this
                // window (none today) should still see it.
            }
            if (!_autoHide && _callbackMsg != 0 && (uint)msg == _callbackMsg && wParam.ToInt32() == NM.ABN_POSCHANGED)
            {
                SetReservedPosition();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            try
            {
                if (_autoHide) SetAutoHide(_expanded);
                else SetReservedPosition();
            }
            catch (Exception ex) { Log.Error("appbar reposition", ex); }
        }

        private void NavigateOrOpen(string path)
        {
            App.Recents?.RecordNavigation(path);
            // If an Explorer window is open, change its location; otherwise open a new one. After the
            // new window opens it becomes the active Explorer, so the next click navigates it. The
            // COM navigation runs on the navigation lane because it blocks for as long as Explorer is busy.
            var last = _lastExplorer;
            WorkQueue.PostNavigation(() =>
            {
                if (last != IntPtr.Zero && NM.IsWindow(last) && ExplorerNavigator.Navigate(last, path))
                {
                    NM.SetForegroundWindow(last); // bring the navigated window to the front
                    return;
                }
                ExplorerNavigator.OpenNewWindow(path);
            });
        }

        private void RemoveAppBar()
        {
            if (!_registered || _handle == IntPtr.Zero) return;
            try
            {
                var abd = new NM.APPBARDATA { cbSize = Marshal.SizeOf(typeof(NM.APPBARDATA)), hWnd = _handle };
                NM.SHAppBarMessage(NM.ABM_REMOVE, ref abd);
            }
            catch (Exception ex) { Log.Error("appbar remove", ex); }
            _registered = false;
            _haveReserved = false;
            _haveLastMonitorId = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (App.Settings != null)
            {
                App.Settings.Changed -= OnSettingsChanged;
                App.Settings.WidthChangedLive -= OnSettingsChanged;
            }
            try { _collapseTimer?.Stop(); } catch { }
            try { _pinTimer?.Stop(); } catch { }
            try { _foregroundHook?.Dispose(); } catch { }
            RemoveAppBar();
            try { _sidebar?.Detach(); } catch (Exception ex) { Log.Error("sidebar detach", ex); }
            try { _window?.Close(); } catch (Exception ex) { Log.Error("appbar window close", ex); }
            _window = null;
            _sidebar = null;
        }
    }
}
