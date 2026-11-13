using System;
using System.Linq;
using System.Threading;
using System.Windows;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using PowerPane.Explorer;
using PowerPane.Interop;
using PowerPane.Models;
using PowerPane.Services;
using PowerPane.UI;

namespace PowerPane
{
    /// <summary>
    /// Application entry point and process-wide service host.
    ///
    /// Two run modes:
    ///   normal      -> system tray app that watches Explorer windows and embeds a sidebar in each.
    ///   --pin PATH... -> short-lived popup ("Add to PowerPane") that adds the items to a chosen group and tab, then exits.
    /// The running tray instance notices the new .lnk through GroupStore's FileSystemWatcher,
    /// so a pin made from the context menu shows up in every open sidebar within a few hundred ms.
    /// </summary>
    public partial class App : Application
    {
        private const string PinVerb = "--pin";

        private Mutex _mutex;
        private bool _ownsMutex;
        private WinForms.NotifyIcon _tray;
        private ExplorerWatcher _watcher;
        private ExplorerFollowerWatcher _follower;
        private AppBarHost _appbar;
        private RecentTracker _recentTracker;
        private DialogPaneWatcher _dialogPanes;
        private StaleSourceGuard _staleSources;
        private Window _settingsWindow;

        // Process-wide services. Set once during normal startup.
        public static SettingsStore Settings { get; private set; }
        public static GroupStore Groups { get; private set; }
        public static RecentFoldersService Recents { get; private set; }
        public static DriveService Drives { get; private set; }
        public static RecentAppsService RecentApps { get; private set; }
        public static AppDefaultsService AppDefaults { get; private set; }
        public static ThemeService Theme { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Log.Init();
            Log.Info("PowerPane starting. args=[" + string.Join(" ", e.Args) + "]");

            // Last-resort exception logging. Dispatcher exceptions are contained so one bad event
            // handler cannot take down every embedded pane, while appdomain and task faults are written
            // to the journal before the CLR acts on them. A terminating appdomain fault is the one case
            // the process does not survive, so it is stamped into the heartbeat as well, which lets the
            // watchdog name the cause rather than only reporting that the app vanished.
            DispatcherUnhandledException += (s, a) =>
            {
                Log.Event("crash", "unhandled exception on the UI thread, contained so the panes survive", a.Exception);
                a.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, a) =>
            {
                var ex2 = a.ExceptionObject as Exception;
                var wrapped = ex2 ?? new Exception(a.ExceptionObject != null ? a.ExceptionObject.ToString() : "unknown");
                if (a.IsTerminating)
                {
                    Log.Activity("terminating: " + wrapped.GetType().Name + ": " + wrapped.Message);
                    Log.Event("crash", "unhandled exception is terminating the process", wrapped);
                }
                else Log.Event("crash", "unhandled exception on a background thread", wrapped);
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, a) =>
            {
                Log.Event("crash", "unobserved task exception", a.Exception);
                a.SetObserved();
            };

            try
            {
                if (e.Args.Length >= 2 && string.Equals(e.Args[0], Watchdog.Verb, StringComparison.OrdinalIgnoreCase))
                {
                    RunWatchdogMode(e.Args[1]);
                    return;
                }

                if (e.Args.Length >= 1 && string.Equals(e.Args[0], ShutdownSignal.Verb, StringComparison.OrdinalIgnoreCase))
                {
                    RunExitMode();
                    return;
                }

                if (e.Args.Length >= 2 && string.Equals(e.Args[0], PinVerb, StringComparison.OrdinalIgnoreCase))
                {
                    RunPinMode(e.Args);
                    return;
                }

                if (!ClaimSingleInstance())
                {
                    Log.Info("Another PowerPane instance is already running. Exiting.");
                    Shutdown();
                    return;
                }

                CrashGuard.BeginSession();
                ShutdownSignal.Listen(() => Dispatcher.BeginInvoke(new Action(Shutdown)));
                Watchdog.Launch();
                ShortcutRepair.EnsureStartMenu();
                StartTrayApp();
            }
            catch (Exception ex)
            {
                Log.Event("crash", "startup failed, so the app is exiting", ex);
                Shutdown();
            }
        }

        // ---- Exit mode (used by the installer and the uninstaller) -----------

        /// <summary>Ask the running tray instance to shut down and block until it and its watchdog are
        /// gone. The exit code is what an installer reads to decide whether the files are free, so a
        /// timeout has to report failure rather than pretend the app closed.</summary>
        private void RunExitMode()
        {
            int code = ShutdownSignal.RequestExit(TimeSpan.FromSeconds(15)) ? 0 : 1;
            Shutdown(code);
        }

        // ---- Watchdog mode (companion process) -------------------------------

        /// <summary>Wait for the tray instance to exit, then either exit quietly or restart it. No
        /// mutex, no tray icon, no hooks, and no windows, because this process exists only to outlive
        /// whatever takes the app down.</summary>
        private void RunWatchdogMode(string pidArg)
        {
            int pid;
            if (!int.TryParse(pidArg, out pid)) { Shutdown(); return; }
            Watchdog.Run(pid, () => Dispatcher.BeginInvoke(new Action(Shutdown)));
        }

        // ---- Normal tray mode ------------------------------------------------

        private void StartTrayApp()
        {
            Settings = new SettingsStore();
            Settings.Load();

            Theme = new ThemeService();
            Theme.AttachSettings(Settings);   // so Apply can read the saved PaneColor (#134)
            Theme.Start();           // reads registry, installs the first theme dictionary
            Theme.Apply();

            Groups = new GroupStore(Settings);
            Groups.Start();          // initial scan + FileSystemWatcher on the groups folder, on the worker

            // If the groups path or mode changes in settings, react. Reload groups only when the
            // groups folder actually changed: reloading on every save also ran on width release and
            // section renames, re-creating the FileSystemWatcher and rescanning for nothing.
            string lastGroupsPath = Settings.ExpandedGroupsPath;
            Settings.Changed += (s, e) =>
            {
                try
                {
                    var p = Settings.ExpandedGroupsPath;
                    if (!string.Equals(p, lastGroupsPath, StringComparison.OrdinalIgnoreCase))
                    {
                        lastGroupsPath = p;
                        Groups.Reload();
                    }
                }
                catch (Exception ex) { Log.Error("groups reload", ex); }
                // Picks up a PaneColor change (#134) by swapping just that one brush; every open pane
                // already binds SidebarBackground through DynamicResource, so this alone shows the new
                // color live. Deliberately not Theme.Apply(): that reloads the whole theme dictionary
                // and fires ThemeChanged, which would rebuild every pane on every settings save, not
                // just a PaneColor one.
                try { Theme.ApplyPaneColor(); } catch (Exception ex) { Log.Error("pane color re-apply", ex); }
                // Defer the host switch so we never tear down a window from inside its own event.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { ApplyHosts(); } catch (Exception ex) { Log.Error("apply hosts", ex); }
                    try { ApplyHotkeys(); } catch (Exception ex) { Log.Error("apply hotkeys", ex); }
                }));
            };

            Recents = new RecentFoldersService(Settings);
            Recents.Start();

            _recentTracker = new RecentTracker();
            _recentTracker.Start();   // records the folder of whichever Explorer window is browsed

            // Before any pane exists, so no window of ours is ever left where a dead parent can
            // crash WPF's text-input hook (#148).
            _staleSources = new StaleSourceGuard();
            _staleSources.Start();

            _dialogPanes = new DialogPaneWatcher();
            _dialogPanes.Start();     // adds a pane beside file Open/Save dialogs, in every pane mode

            Drives = new DriveService();
            // The quick pass above only reads DriveType now, so every row starts with a default label
            // and Ready=true (#76); this warms the real details before This PC is ever opened.
            Drives.RefreshAsync();
            RecentApps = new RecentAppsService();
            AppDefaults = new AppDefaultsService();

            _hotkeys = new HotkeyService();
            _hotkeys.Pressed += FocusPaneFromHotkey;
            // WM_HOTKEY arrives on this thread, and the window in front is still the user's, so the
            // switcher records it before it takes focus (#226).
            _switcherHotkey = new HotkeyService(HotkeyService.QuickSwitcherId, "quick switcher");
            _switcherHotkey.Pressed += () => Dispatcher.BeginInvoke(new Action(UI.QuickSwitcher.Toggle));
            ApplyHotkeys();

            CreateTrayIcon();
            UpdateNotifier.Start(ShowUpdateNotice);

            ApplyHosts();            // starts the in-window pane and/or the desktop dock per settings

            Log.Info("PowerPane running.");

            // Move the breadcrumb off "starting", or a crash hours later would be journaled against
            // startup and point the search at the wrong place entirely.
            Log.Activity("running");
        }

        private static HotkeyService _hotkeys;
        private static HotkeyService _switcherHotkey;

        /// <summary>Re-read the keyboard settings and register or drop each shortcut.</summary>
        private void ApplyHotkeys()
        {
            if (_hotkeys == null || Settings == null) return;
            var s = Settings.Current;
            _hotkeys.Apply(s.KeyboardNav, s.KeyboardNavHotkey);
            if (_switcherHotkey != null) _switcherHotkey.Apply(s.QuickSwitcher, s.QuickSwitcherHotkey);
        }

        /// <summary>Put focus in the pane belonging to whatever dialog is in front.</summary>
        private void FocusPaneFromHotkey()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var fg = Interop.NativeMethods.GetForegroundWindow();
                    if (_dialogPanes != null && _dialogPanes.FocusPaneFor(fg)) return;
                    Log.Info("keyboard navigation shortcut pressed with no pane on the window in front.");
                }
                catch (Exception ex) { Log.Error("focus pane hotkey", ex); }
            }));
        }

        private string _windowMode;
        private bool _dockOn;
        private bool _dockAutoHide;
        private bool _dockAllDesktops;

        /// <summary>Reconcile the running hosts with settings. One window mode (inside or beside) is
        /// always active, and the desktop dock is an independent add-on.</summary>
        private void ApplyHosts()
        {
            var s = Settings.Current;
            string mode = (s.Mode ?? "inside").Trim().ToLowerInvariant();
            if (mode != "beside" && mode != "off") mode = "inside";
            bool wantDock = s.DesktopDock;
            bool autoHide = s.DesktopDockAutoHide;
            bool allDesktops = s.DesktopDockAllDesktops;

            if (mode != _windowMode)
            {
                try { _watcher?.Dispose(); } catch (Exception ex) { Log.Error("watcher dispose", ex); }
                _watcher = null;
                try { _follower?.Dispose(); } catch (Exception ex) { Log.Error("follower dispose", ex); }
                _follower = null;

                if (mode == "beside") { _follower = new ExplorerFollowerWatcher(); _follower.Start(); }
                else if (mode == "inside") { _watcher = new ExplorerWatcher(); _watcher.Start(); }
                // mode == "off": no window pane
                _windowMode = mode;
            }

            bool dockRestart = wantDock && _dockOn && (autoHide != _dockAutoHide || allDesktops != _dockAllDesktops);
            if ((!wantDock && _dockOn) || dockRestart)
            {
                try { _appbar?.Dispose(); } catch (Exception ex) { Log.Error("appbar dispose", ex); }
                _appbar = null; _dockOn = false;
            }
            if (wantDock && !_dockOn)
            {
                _appbar = new AppBarHost(autoHide, allDesktops); _appbar.Start();
                _dockOn = true; _dockAutoHide = autoHide; _dockAllDesktops = allDesktops;
            }

            Log.Info("Hosts: mode=" + _windowMode + " dock=" + _dockOn + " autohide=" + _dockAutoHide + " allDesktops=" + _dockAllDesktops);
            Log.Activity("running, mode=" + _windowMode + ", dock=" + _dockOn);
        }

        private bool ClaimSingleInstance()
        {
            _mutex = new Mutex(true, CrashGuard.InstanceMutexName, out _ownsMutex);
            return _ownsMutex;
        }

        private void CreateTrayIcon()
        {
            _tray = new WinForms.NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = "PowerPane",
                Visible = true
            };

            var menu = new WinForms.ContextMenuStrip();

            // Hidden until a newer version is known (#176). Opening below sets its text and visibility.
            var updateItem = new WinForms.ToolStripMenuItem();
            var updateSeparator = new WinForms.ToolStripSeparator();
            updateItem.Click += (s, a) => UpdateService.OpenReleasePage();
            menu.Items.Add(updateItem);
            menu.Items.Add(updateSeparator);

            var profileMenu = new WinForms.ToolStripMenuItem("Profile");
            menu.Items.Add(profileMenu);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Settings", null, (s, a) => Dispatcher.Invoke(ShowSettingsWindow));
            menu.Items.Add("Reload groups", null, (s, a) => Dispatcher.Invoke(() => Groups.Reload()));
            menu.Items.Add("Re-attach sidebars", null, (s, a) => Dispatcher.Invoke(() => { if (_watcher != null) _watcher.Rescan(); }));

            var toolsMenu = new WinForms.ToolStripMenuItem("Troubleshooting");
            toolsMenu.DropDownItems.Add("Open log folder", null, (s, a) => Log.OpenFolder());
            toolsMenu.DropDownItems.Add("Create desktop shortcut", null, (s, a) => CreateDesktopShortcut());
            toolsMenu.DropDownItems.Add("Restart PowerPane", null, (s, a) => Dispatcher.Invoke(RestartSelf));
            menu.Items.Add(toolsMenu);

            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, a) => Dispatcher.Invoke(Shutdown));

            // Rebuild the Profile submenu each time the tray menu opens, with a check on the active one.
            menu.Opening += (s, a) =>
            {
                var available = UpdateNotifier.Available;
                updateItem.Visible = updateSeparator.Visible = available != null;
                if (available != null) updateItem.Text = "Version " + UpdateService.Display(available) + " is available";

                profileMenu.DropDownItems.Clear();
                var profiles = Settings.Current.Profiles;
                if (profiles == null) return;
                for (int i = 0; i < profiles.Count; i++)
                {
                    int idx = i;
                    var item = new WinForms.ToolStripMenuItem(profiles[i].Name)
                    {
                        Checked = (i == Settings.Current.ActiveProfileIndex),
                        CheckOnClick = false
                    };
                    item.Click += (s2, a2) => Dispatcher.Invoke(() => Settings.SwitchProfile(idx));
                    profileMenu.DropDownItems.Add(item);
                }
            };
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, a) => Dispatcher.Invoke(ShowSettingsWindow);
            _tray.BalloonTipClicked += (s, a) => UpdateService.OpenReleasePage();
        }

        /// <summary>The one-time notice for a newly found version (#176): a tray balloon, which Windows
        /// shows as a notification and holds back while the user is in a full-screen app or Do Not
        /// Disturb. The balloon's only action is opening the release page.</summary>
        private void ShowUpdateNotice(Version latest)
        {
            try
            {
                Log.Info("update notice: " + UpdateService.Display(latest) + " is available.");
                if (_tray != null)
                    _tray.ShowBalloonTip(10000, UpdateNotifier.NoticeTitle(latest),
                        "Click to open the release page. Nothing is downloaded until you choose to.",
                        WinForms.ToolTipIcon.Info);
            }
            catch (Exception ex) { Log.Error("show the update notice", ex); }
        }

        private static void CreateDesktopShortcut()
        {
            var path = ShortcutRepair.CreateDesktop();
            WinForms.MessageBox.Show(
                path != null ? "Desktop shortcut created:\n" + path
                             : "The desktop shortcut could not be created. See events.log in the log folder.",
                "PowerPane");
        }

        /// <summary>Relaunch and exit. The heartbeat is stamped clean first so the watchdog treats this
        /// as an orderly exit and does not add a restart of its own on top of this one.</summary>
        private void RestartSelf()
        {
            try
            {
                var exe = Log.ExePath();
                if (string.IsNullOrEmpty(exe)) return;
                Log.Event("restart", "restart requested from the tray menu.");
                CrashGuard.MarkCleanExit();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex) { Log.Event("restart", "restart from the tray menu failed", ex); }
            Shutdown();
        }

        private static Drawing.Icon LoadAppIcon()
        {
            try
            {
                var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var ico = Drawing.Icon.ExtractAssociatedIcon(exe);
                if (ico != null) return ico;
            }
            catch (Exception ex) { Log.Error("load tray icon", ex); }
            return Drawing.SystemIcons.Application;
        }

        /// <summary>Open (or activate) the real Settings window from anywhere in the app, e.g. the
        /// sidebar's own gear button, rather than embedding a second copy of the settings UI in place.</summary>
        public static void ShowSettings()
        {
            var app = Application.Current as App;
            if (app != null) app.ShowSettingsWindow();
        }

        private void ShowSettingsWindow()
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }

            // SettingsWindow (PowerPane/UI/Settings/SettingsWindow.cs, #90) replaces SettingsPanel
            // and its tab strip with the Windows 11 Settings app layout. It merges the ported
            // Fluent styles into its own Resources itself (never Application.Current.Resources:
            // every Explorer pane inherits whatever lives there), so this call does not repeat
            // FluentThemeService.Attach the way the old window's host code did.
            _settingsWindow = new PowerPane.UI.Settings.SettingsWindow(Settings, Groups, Theme, _tray?.Icon);
            _settingsWindow.Closed += (s, a) => _settingsWindow = null;
            Theme.AttachWindow(_settingsWindow);
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        // ---- Pin mode (the "Add to PowerPane" context menu verb) -------------

        // Explorer may start the verb once per selected item, each in its own process. Every launch
        // writes its paths to PinQueue, and one launch (the one that claims the picker mutex) waits for
        // the burst to end, drains the queue, and shows a single picker. The others exit at once. The
        // owner keeps draining while its picker is open, and checks the queue once more after it
        // releases the mutex, so a launch that arrives late is shown a second picker, never dropped.
        private PinQueue _pinQueue;
        private Mutex _pinMutex;
        private GroupStore _pinGroups;
        private System.Windows.Threading.DispatcherTimer _pinWait;
        private System.Windows.Threading.DispatcherTimer _pinPoll;
        private PowerPane.Interop.NativeMethods.POINT _pinPointer;
        private bool _havePinPointer;
        private int _pinIdleRounds;

        private void RunPinMode(string[] args)
        {
            try
            {
                var own = PinRequest.ParseArguments(args);
                if (own.Count == 0)
                {
                    Log.Info("Add to PowerPane got no usable path. args=[" + string.Join(" ", args) + "]");
                    Shutdown();
                    return;
                }

                // The pointer is read now, because the picker may open up to three seconds later.
                _havePinPointer = PowerPane.Interop.NativeMethods.GetCursorPos(out _pinPointer);

                _pinQueue = PinQueue.Default;
                var written = new System.Collections.Generic.List<string>();
                try { _pinQueue.Enqueue(own, written); }
                catch (Exception ex)
                {
                    Log.Error("Add to PowerPane could not write the queue", ex);
                }

                // Paths that never reached the queue are shown from memory, so a failed write loses nothing.
                var unwritten = own.Where(p => !written.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();

                if (!ClaimPinPicker())
                {
                    if (unwritten.Count == 0)
                    {
                        Shutdown();   // another launch owns the picker and will drain this launch's files
                        return;
                    }
                    WaitForPinPicker(unwritten);   // nobody else can show these, so wait for a turn
                    return;
                }

                StartPinOwner(unwritten);
            }
            catch (Exception ex)
            {
                Log.Error("Pin mode failed", ex);
                ReleasePinPicker();
                Shutdown();
            }
        }

        private void StartPinOwner(System.Collections.Generic.List<string> unwritten)
        {
            var settings = new SettingsStore();
            settings.Load();
            _pinGroups = new GroupStore(settings);
            BeginPinRound(unwritten.Count > 0 ? unwritten : null);
        }

        /// <summary>A launch that lost the picker but could not queue its own paths waits here, up to the
        /// queue's maximum age, and shows them itself once the picker frees.</summary>
        private void WaitForPinPicker(System.Collections.Generic.List<string> unwritten)
        {
            DateTime giveUp = DateTime.UtcNow + PinQueue.MaxAge;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, a) =>
            {
                try
                {
                    if (ClaimPinPicker())
                    {
                        timer.Stop();
                        _havePinPointer = PowerPane.Interop.NativeMethods.GetCursorPos(out _pinPointer);
                        StartPinOwner(unwritten);
                    }
                    else if (DateTime.UtcNow > giveUp)
                    {
                        timer.Stop();
                        Log.Error("Add to PowerPane gave up waiting for the picker with " + unwritten.Count + " unqueued items");
                        Shutdown();
                    }
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    Log.Error("Pin mode wait failed", ex);
                    ReleasePinPicker();
                    Shutdown();
                }
            };
            timer.Start();
        }

        private bool ClaimPinPicker()
        {
            Mutex m;
            if (!PinQueue.TryClaim(PinQueue.PickerMutexName, out m)) return false;
            _pinMutex = m;
            return true;
        }

        private void ReleasePinPicker()
        {
            var m = _pinMutex;
            _pinMutex = null;
            if (m == null) return;
            try { m.ReleaseMutex(); } catch (Exception) { }
            m.Dispose();
        }

        /// <summary>Waits until no new queue file has arrived for a moment (or the cap is reached),
        /// then shows the picker.</summary>
        private void BeginPinRound(System.Collections.Generic.List<string> fallback)
        {
            DateTime started = DateTime.UtcNow;
            _pinWait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _pinWait.Tick += (s, a) =>
            {
                DateTime last = _pinQueue.LatestArrivalUtc() ?? started;
                if (!PinQueue.QuietWindowOver(started, last, DateTime.UtcNow)) return;
                _pinWait.Stop();
                ShowPinPicker(fallback);
            };
            _pinWait.Start();
        }

        private void ShowPinPicker(System.Collections.Generic.List<string> fallback)
        {
            try
            {
                var items = _pinQueue.Drain();
                if (fallback != null)
                    items.AddRange(fallback.Where(p => !items.Contains(p, StringComparer.OrdinalIgnoreCase)));
                if (items.Count == 0) { _pinIdleRounds++; FinishPinRound(); return; }
                _pinIdleRounds = 0;

                _pinGroups.ScanOnce();   // no watcher needed for a one-shot popup
                var picker = new GroupPickerWindow(_pinGroups, items, PinMemory.Load(PinMemory.DefaultPath), !CrashGuard.InstanceHeld());

                _pinPoll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _pinPoll.Tick += (s, a) =>
                {
                    var more = _pinQueue.Drain();
                    if (more.Count == 0) return;
                    picker.AddPaths(more);
                    picker.DrawAttention();
                };
                picker.Closed += (s, a) =>
                {
                    _pinPoll.Stop();
                    FinishPinRound();
                };
                _pinPoll.Start();

                PowerPane.Interop.NativeMethods.POINT pt = _pinPointer;
                if (!_havePinPointer) _havePinPointer = PowerPane.Interop.NativeMethods.GetCursorPos(out pt);
                picker.ShowAt(pt);
            }
            catch (Exception ex)
            {
                Log.Error("Pin picker failed", ex);
                FinishPinRound();
            }
        }

        /// <summary>Releases the picker mutex and looks at the queue one last time. A file that arrived
        /// in between means a launch lost the mutex race while this picker was closing, so a second
        /// round starts for it.</summary>
        private void FinishPinRound()
        {
            try
            {
                ReleasePinPicker();
                // A file Drain cannot read or delete stays pending, so a round that drained nothing is
                // allowed to repeat only once. Otherwise the process would loop until the file turned stale.
                if (_pinIdleRounds < 2 && _pinQueue.PendingCount() > 0 && ClaimPinPicker())
                {
                    _havePinPointer = PowerPane.Interop.NativeMethods.GetCursorPos(out _pinPointer);
                    BeginPinRound(null);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Pin mode final queue check failed", ex);
                ReleasePinPicker();
            }
            Shutdown();
        }

        // ---- Teardown --------------------------------------------------------

        /// <summary>Sign-out and shutdown can end the process once the session-end messages are
        /// answered, before the Shutdown that WPF queues here ever reaches OnExit, so the pending
        /// settings write is flushed now. The Flush in OnExit is then a no-op (#163).</summary>
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            try { Settings?.Flush(); } catch (Exception ex) { Log.Error("settings flush on session end", ex); }
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Info("PowerPane shutting down.");
            Log.Activity("shutting down");

            // Stamp the heartbeat before anything is torn down, because teardown drives foreign windows
            // and can take long enough for the watchdog to see the process go while the file still reads
            // as a live session. CrashGuard ignores the call in pin and watchdog mode, where this process
            // never opened the session the file describes.
            CrashGuard.MarkCleanExit();
            ShutdownSignal.Stop();
            UpdateNotifier.Stop();

            try { _recentTracker?.Dispose(); } catch (Exception ex) { Log.Error("recent tracker dispose", ex); }
            try { _dialogPanes?.Dispose(); } catch (Exception ex) { Log.Error("dialog panes dispose", ex); }
            try { _watcher?.Dispose(); } catch (Exception ex) { Log.Error("watcher dispose", ex); }
            try { _follower?.Dispose(); } catch (Exception ex) { Log.Error("follower dispose", ex); }
            try { _appbar?.Dispose(); } catch (Exception ex) { Log.Error("appbar dispose", ex); }
            try { Recents?.Dispose(); } catch (Exception ex) { Log.Error("recents dispose", ex); }
            try { Groups?.Dispose(); } catch (Exception ex) { Log.Error("groups dispose", ex); }
            try { Theme?.Dispose(); } catch (Exception ex) { Log.Error("theme dispose", ex); }
            try { _staleSources?.Dispose(); } catch (Exception ex) { Log.Error("stale source guard dispose", ex); }

            // After the teardown above, which can still save (a pane persisting its width), and before
            // the process ends: the settings lane is a background thread, so a write still queued
            // there would be dropped with it (#163).
            try { Settings?.Flush(); } catch (Exception ex) { Log.Error("settings flush on exit", ex); }

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }

            if (_mutex != null)
            {
                if (_ownsMutex) { try { _mutex.ReleaseMutex(); } catch { } }
                _mutex.Dispose();
            }

            base.OnExit(e);
        }
    }
}
