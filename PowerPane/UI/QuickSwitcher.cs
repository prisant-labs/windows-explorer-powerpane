using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PowerPane.Interop;
using PowerPane.Models;
using PowerPane.Services;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.UI
{
    /// <summary>
    /// The quick switcher (#226): a shortcut opens a search box over the window in front, which finds
    /// pins, Recent folders, drives, and quick actions together. Enter navigates the Explorer window or
    /// file dialog that was in front when the box opened, or runs the action against that Explorer
    /// window. Escape closes the box and gives focus back.
    ///
    /// The box follows the pane's rules, not the Settings window's: the pane's own theme brushes and no
    /// Fluent styles. It runs on the UI thread, which in inside mode shares Explorer's input queue, so
    /// typing only filters what PowerPane already holds in memory (<see cref="QuickSwitcherSearch"/>),
    /// a folder goes to Explorer through the navigation lane, and an action reads the window's folder
    /// and selection on the general lane, as a pane row does.
    /// </summary>
    internal sealed class QuickSwitcher : Window
    {
        internal enum TargetKind { Explorer, FileDialog, Other }

        private const string GlyphPin = "\uE718";      // Pin
        private const string GlyphRecent = "\uE81C";   // History
        private const string GlyphDrive = "\uEDA2";    // HardDrive
        private const string GlyphAction = "\uE945";   // LightningBolt
        private const string GlyphFile = "\uE8A5";     // Document
        private const string GlyphSearch = "\uE721";   // Search

        private static QuickSwitcher _open;

        private readonly IntPtr _target;
        private readonly TargetKind _targetKind;
        private readonly List<SwitcherEntry> _entries;
        private readonly TextBox _box;
        private readonly StackPanel _list;
        private readonly TextBlock _status;
        private List<SwitcherEntry> _shown = new List<SwitcherEntry>();
        private int _selected;
        private bool _closing;
        private bool _busy;

        /// <summary>Open the switcher over the window in front, or close it if it is already open.
        /// Called on the UI thread from the shortcut, before anything of ours takes focus, so the
        /// window in front is still the one the user was working in.</summary>
        public static void Toggle()
        {
            try
            {
                if (_open != null) { _open.CloseAndReturn(); return; }

                IntPtr fg = NM.GetForegroundWindow();
                var kind = Classify(fg);
                if (kind == TargetKind.Other && IsOurs(fg)) fg = IntPtr.Zero;   // never "return" focus to our own pane or Settings
                var w = new QuickSwitcher(fg, kind, Gather(kind));
                _open = w;
                w.Show();
                w.Activate();
                w._box.Focus();
            }
            catch (Exception ex) { Log.Error("quick switcher open", ex); _open = null; }
        }

        internal static TargetKind Classify(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NM.IsWindow(hwnd)) return TargetKind.Other;
            var cls = NM.ClassOf(hwnd);
            if (cls == "CabinetWClass" || cls == "ExploreWClass") return TargetKind.Explorer;
            if (IsOurs(hwnd)) return TargetKind.Other;
            return DialogNavigator.IsFileDialog(hwnd) ? TargetKind.FileDialog : TargetKind.Other;
        }

        private static bool IsOurs(IntPtr hwnd)
        {
            uint pid;
            NM.GetWindowThreadProcessId(hwnd, out pid);
            return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        }

        /// <summary>Everything the switcher can find, from memory only. Actions act on an Explorer
        /// window's folder and selection, so they are offered only when one is in front.</summary>
        private static List<SwitcherEntry> Gather(TargetKind kind)
        {
            var pins = new List<SwitcherEntry>();
            if (App.Groups != null)
            {
                foreach (var g in App.Groups.Groups)
                {
                    bool tabbed = g.Tabs.Count > 1;
                    foreach (var tab in g.Tabs)
                        foreach (var item in tab.Items)
                        {
                            if (!item.Exists || string.IsNullOrEmpty(item.TargetPath)) continue;
                            pins.Add(new SwitcherEntry
                            {
                                Kind = SwitcherKind.Pin, Name = item.DisplayName, Path = item.TargetPath, IsFile = item.IsFile,
                                Detail = tabbed ? g.Name + " › " + tab.Name : g.Name,
                            });
                        }
                }
            }

            var recent = new List<SwitcherEntry>();
            if (App.Recents != null)
                foreach (var r in App.Recents.Items)
                    recent.Add(new SwitcherEntry { Kind = SwitcherKind.Recent, Name = r.DisplayName, Path = r.TargetPath, IsFile = r.IsFile, Detail = r.TargetPath });

            var drives = new List<SwitcherEntry>();
            if (App.Drives != null)
                foreach (var d in App.Drives.GetDrives())
                    drives.Add(new SwitcherEntry { Kind = SwitcherKind.Drive, Name = d.Label + " (" + d.Letter + ")", Path = d.RootPath, Detail = d.RootPath });

            var actions = new List<SwitcherEntry>();
            if (kind == TargetKind.Explorer && App.Settings != null)
            {
                var s = App.Settings.Current;
                foreach (var setting in s.QuickActions ?? new List<QuickActionSetting>())
                {
                    if (!setting.Enabled) continue;
                    var def = QuickActionLibrary.Resolve(s, setting.Id);
                    if (def == null || !QuickActionsSection.IsAvailable(def)) continue;
                    actions.Add(new SwitcherEntry { Kind = SwitcherKind.Action, Name = def.Name, ActionId = def.Id, Detail = def.Description });
                }
            }

            return QuickSwitcherSearch.Combine(pins, recent, drives, actions);
        }

        private QuickSwitcher(IntPtr target, TargetKind targetKind, List<SwitcherEntry> entries)
        {
            _target = target;
            _targetKind = targetKind;
            _entries = entries;

            Title = "Quick switcher";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Width = 520;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000; Top = -32000;   // placed over the target in Loaded, once its size is known
            Background = Opaque(UiHelpers.AppBrush("SidebarBackground"));
            BorderBrush = UiHelpers.AppBrush("SeparatorColor");
            BorderThickness = new Thickness(1);

            var searchIcon = new TextBlock
            {
                Text = GlyphSearch, FontFamily = UiHelpers.IconFont(), FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0),
                Foreground = UiHelpers.AppBrush("TextSecondary"),
            };
            _box = new TextBox
            {
                FontSize = 15,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = UiHelpers.AppBrush("TextPrimary"),
                CaretBrush = UiHelpers.AppBrush("TextPrimary"),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0, 8, 8, 8),
            };
            UiHelpers.Named(_box, "Search pins, Recent, drives, and actions");
            var boxRow = new DockPanel { Background = UiHelpers.AppBrush("FieldBackground") };
            DockPanel.SetDock(searchIcon, Dock.Left);
            boxRow.Children.Add(searchIcon);
            boxRow.Children.Add(_box);

            _list = new StackPanel { Margin = new Thickness(4) };
            AutomationProperties_Name(_list, "Results");
            _status = new TextBlock
            {
                FontSize = 12, Margin = new Thickness(12, 2, 12, 8), TextWrapping = TextWrapping.Wrap,
                Foreground = UiHelpers.AppBrush("TextSecondary"), Visibility = Visibility.Collapsed,
            };

            var root = new StackPanel();
            root.Children.Add(boxRow);
            root.Children.Add(_list);
            root.Children.Add(_status);
            Content = root;

            _box.TextChanged += (s, e) => { if (!_busy) Refresh(); };
            _box.PreviewKeyDown += OnKey;
            Loaded += (s, e) => PlaceOverTarget();
            // Focus() right after Activate() can miss while the window has no Win32 focus yet, so
            // focus lands again whenever activation completes.
            Activated += (s, e) => Keyboard.Focus(_box);
            Deactivated += (s, e) => { if (!_closing && !_busy) { _closing = true; Close(); } };
            Closed += (s, e) => { if (_open == this) _open = null; };

            Refresh();
        }

        private static void AutomationProperties_Name(DependencyObject o, string name)
        {
            System.Windows.Automation.AutomationProperties.SetName(o, name);
        }

        private static Brush Opaque(Brush b)
        {
            // The pane's background is slightly translucent, which a window without AllowsTransparency
            // would draw over black. AllowsTransparency costs a software-rendered layered window.
            var sc = b as SolidColorBrush;
            return sc == null ? b : new SolidColorBrush(Color.FromRgb(sc.Color.R, sc.Color.G, sc.Color.B));
        }

        // ---- results ---------------------------------------------------------------------

        private void Refresh()
        {
            _shown = QuickSwitcherSearch.Rank(_box.Text, _entries);
            _selected = 0;
            _list.Children.Clear();
            for (int i = 0; i < _shown.Count; i++) _list.Children.Add(BuildRow(_shown[i], i));
            if (_shown.Count == 0)
                ShowStatus(_entries.Count == 0 ? "Nothing to search yet: pin a folder, or browse one so Recent has it." : "No match.");
            else
                HideStatus();
            PaintSelection();
        }

        private FrameworkElement BuildRow(SwitcherEntry e, int index)
        {
            var icon = new TextBlock
            {
                Text = GlyphFor(e), FontFamily = UiHelpers.IconFont(), FontSize = 14, Width = 22,
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = UiHelpers.AppBrush("TextSecondary"),
            };
            var name = new TextBlock
            {
                Text = e.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = UiHelpers.AppBrush("TextPrimary"),
            };
            var detail = new TextBlock
            {
                Text = e.Detail ?? string.Empty, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = UiHelpers.AppBrush("TextSecondary"),
            };
            var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            text.Children.Add(name);
            if (!string.IsNullOrEmpty(e.Detail)) text.Children.Add(detail);

            var content = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            content.Children.Add(icon);
            content.Children.Add(text);

            var row = new ResultRow
            {
                Background = Brushes.Transparent, CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 5, 8, 5), Cursor = Cursors.Hand, Child = content,
            };
            UiHelpers.Named(row, KindLabel(e.Kind) + ", " + e.Name);
            row.MouseEnter += (s, a) => { _selected = index; PaintSelection(); };
            row.MouseLeftButtonUp += (s, a) => Choose(e);
            return row;
        }

        private static string GlyphFor(SwitcherEntry e)
        {
            switch (e.Kind)
            {
                case SwitcherKind.Pin: return e.IsFile ? GlyphFile : GlyphPin;
                case SwitcherKind.Recent: return e.IsFile ? GlyphFile : GlyphRecent;
                case SwitcherKind.Drive: return GlyphDrive;
                default: return GlyphAction;
            }
        }

        private static string KindLabel(SwitcherKind k)
        {
            switch (k)
            {
                case SwitcherKind.Pin: return "Pin";
                case SwitcherKind.Recent: return "Recent";
                case SwitcherKind.Drive: return "Drive";
                default: return "Action";
            }
        }

        private void PaintSelection()
        {
            for (int i = 0; i < _list.Children.Count; i++)
            {
                var row = _list.Children[i] as Border;
                if (row == null) continue;
                bool selected = i == _selected;
                row.Background = selected ? UiHelpers.AppBrush("ItemActiveBackground") : Brushes.Transparent;
                System.Windows.Automation.AutomationProperties.SetItemStatus(row, selected ? "Selected" : string.Empty);
            }
        }

        /// <summary>A result row. A plain Border has no UI Automation peer, so a screen reader and
        /// UI Automation never saw the rows or their names (#228). The
        /// peer reports a list item named by AutomationProperties.Name, and ItemStatus marks the row
        /// Enter would choose, since keyboard focus stays in the search box.</summary>
        private sealed class ResultRow : Border
        {
            protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
            {
                return new ResultRowPeer(this);
            }
        }

        private sealed class ResultRowPeer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer
        {
            public ResultRowPeer(FrameworkElement owner) : base(owner) { }

            protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore()
            {
                return System.Windows.Automation.Peers.AutomationControlType.ListItem;
            }

            protected override string GetClassNameCore() { return "QuickSwitcherResult"; }
            protected override bool IsControlElementCore() { return true; }
            protected override bool IsContentElementCore() { return true; }
        }

        private void ShowStatus(string text) { _status.Text = text; _status.Visibility = Visibility.Visible; }
        private void HideStatus() { _status.Visibility = Visibility.Collapsed; }

        private void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    CloseAndReturn();
                    e.Handled = true;
                    break;
                case Key.Down:
                    if (_shown.Count > 0) { _selected = (_selected + 1) % _shown.Count; PaintSelection(); }
                    e.Handled = true;
                    break;
                case Key.Up:
                    if (_shown.Count > 0) { _selected = (_selected + _shown.Count - 1) % _shown.Count; PaintSelection(); }
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (!_busy && _selected >= 0 && _selected < _shown.Count) Choose(_shown[_selected]);
                    e.Handled = true;
                    break;
            }
        }

        // ---- acting ----------------------------------------------------------------------

        private void Choose(SwitcherEntry e)
        {
            if (_busy) return;
            try
            {
                if (e.ActionId != null) { RunAction(e); return; }
                string path = e.Path;
                if (string.IsNullOrEmpty(path)) return;

                if (e.IsFile)
                {
                    // The file's program takes the foreground, so focus is not handed back.
                    CloseQuietly();
                    WorkQueue.Post(() =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
                        catch (Exception ex) { Log.Error("quick switcher open file", ex); }
                    });
                    return;
                }

                switch (_targetKind)
                {
                    case TargetKind.Explorer:
                        App.Recents?.RecordNavigation(path);
                        var cabinet = _target;
                        CloseAndReturn();
                        ExplorerNavigator.NavigateAsync(cabinet, path);
                        break;
                    case TargetKind.FileDialog:
                        var dlg = _target;
                        CloseAndReturn();
                        WorkQueue.PostNavigation(() => DialogNavigator.Navigate(dlg, path));
                        break;
                    default:
                        // No Explorer window or dialog was in front, so open one, as the desktop dock does.
                        App.Recents?.RecordNavigation(path);
                        CloseQuietly();
                        WorkQueue.PostNavigation(() => ExplorerNavigator.OpenNewWindow(path));
                        break;
                }
            }
            catch (Exception ex) { Log.Error("quick switcher choose", ex); CloseQuietly(); }
        }

        /// <summary>Runs an action the way its pane row does: the folder and the selection are read on
        /// the general lane, and a failed selection read stays a failure. The box stays open until the
        /// action answers, so a refusal shows here instead of the box vanishing as if it worked.</summary>
        private void RunAction(SwitcherEntry e)
        {
            var s = App.Settings != null ? App.Settings.Current : null;
            var def = s == null ? null : QuickActionLibrary.Resolve(s, e.ActionId);
            if (def == null || _targetKind != TargetKind.Explorer) return;

            _busy = true;
            _box.IsReadOnly = true;
            ShowStatus("Running " + def.Name + "…");
            var hostWindow = _target;
            WorkQueue.Post(() =>
            {
                string message;
                try
                {
                    var folder = ExplorerNavigator.GetCurrentPath(hostWindow);
                    List<string> selection = null;
                    var status = SelectionReadStatus.Ok;
                    if (QuickActionLibrary.UsesSelection(def))
                        status = ExplorerNavigator.GetSelectedPaths(hostWindow, out selection);
                    message = QuickActionRunner.SelectionProblem(status) ?? QuickActionRunner.Run(def, folder, selection);
                }
                catch (Exception ex) { Log.Error("quick switcher action", ex); message = "The action could not run."; }

                WorkQueue.PostUI(() =>
                {
                    if (_closing) return;
                    _busy = false;
                    _box.IsReadOnly = false;
                    // A program the action started may already have the foreground; leave it there.
                    if (message == null) { CloseQuietly(); return; }
                    ShowStatus(message);
                });
            });
        }

        private void CloseQuietly()
        {
            if (_closing) return;
            _closing = true;
            Close();
        }

        /// <summary>Close and give the foreground back to the window that was in front.</summary>
        private void CloseAndReturn()
        {
            var target = _target;
            CloseQuietly();
            if (target != IntPtr.Zero && NM.IsWindow(target)) NM.SetForegroundWindow(target);
        }

        // ---- placement -------------------------------------------------------------------

        /// <summary>Centre the box across the target window, a fifth of the way down, in physical
        /// pixels, so a monitor at another scale needs no conversion. With nothing in front, the
        /// primary display's work area stands in.</summary>
        private void PlaceOverTarget()
        {
            try
            {
                var own = new WindowInteropHelper(this).Handle;
                NM.RECT me, area;
                if (!NM.GetWindowRect(own, out me)) return;
                if (_target == IntPtr.Zero || !NM.GetWindowRect(_target, out area) || area.Width < 200 || area.Height < 100)
                {
                    var wa = SystemParameters.WorkArea;
                    var src = PresentationSource.FromVisual(this);
                    double sx = src != null ? src.CompositionTarget.TransformToDevice.M11 : 1.0;
                    double sy = src != null ? src.CompositionTarget.TransformToDevice.M22 : 1.0;
                    area = new NM.RECT { Left = (int)(wa.Left * sx), Top = (int)(wa.Top * sy), Right = (int)(wa.Right * sx), Bottom = (int)(wa.Bottom * sy) };
                }
                var pos = Placement(area.Left, area.Top, area.Width, area.Height, me.Width);
                NM.SetWindowPos(own, IntPtr.Zero, pos.X, pos.Y, 0, 0, NM.SWP_NOSIZE | NM.SWP_NOZORDER | NM.SWP_NOACTIVATE);
            }
            catch (Exception ex) { Log.Error("quick switcher placement", ex); }
        }

        /// <summary>Where the box's top-left corner goes: centred across the area, a fifth of the way
        /// down. Pure, so it is unit-tested.</summary>
        internal static System.Drawing.Point Placement(int left, int top, int width, int height, int boxWidth)
        {
            int x = left + (width - boxWidth) / 2;
            if (boxWidth > width) x = left;
            int y = top + height / 5;
            return new System.Drawing.Point(x, y);
        }
    }
}
