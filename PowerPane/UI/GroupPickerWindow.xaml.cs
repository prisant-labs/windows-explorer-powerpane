using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using PowerPane.Models;
using PowerPane.Services;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.UI
{
    /// <summary>
    /// Borderless popup shown by the "Add to PowerPane" context menu verb. It names the items being
    /// added, lists the groups with a "New group..." entry and the chosen group's tabs with a
    /// "New tab..." entry, then writes the pins into the chosen tab. It styles itself directly from
    /// the registry theme because in pin mode the full ThemeService is not running.
    /// </summary>
    public partial class GroupPickerWindow : Window
    {
        private readonly GroupStore _groups;
        private readonly List<string> _paths = new List<string>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly bool _notRunning;
        private List<PinnedGroup> _groupSnapshot = new List<PinnedGroup>();
        private bool _building;

        // Where the pointer was when the verb was chosen, in device pixels. The window is placed from
        // this on every size or scale change, so it stays fully on the pointer's own monitor.
        private NM.POINT _pointer;
        private bool _havePointer;
        private bool _placing;

        /// <param name="notRunning">True when no tray instance is running, which adds a one-line hint
        /// that the pins appear when PowerPane starts.</param>
        internal GroupPickerWindow(GroupStore groups, IList<string> paths, PinMemory last, bool notRunning)
        {
            InitializeComponent();
            _groups = groups;
            _notRunning = notRunning;
            ApplyTheme();
            NameControls();
            AddPaths(paths);
            BuildLists(last);

            if (_notRunning)
            {
                HintText.Text = "PowerPane is not running. The pins appear when it starts.";
                HintText.Visibility = Visibility.Visible;
            }

            SourceInitialized += (s, e) => PlaceNearPointer();
            SizeChanged += (s, e) => PlaceNearPointer();
            Loaded += (s, e) => FocusStartList();
        }

        // ---- items -----------------------------------------------------------

        /// <summary>Adds items that arrived while the picker is open, so a slow launch of a
        /// multi-selection is not lost. A path already in the list is not added twice.</summary>
        internal void AddPaths(IEnumerable<string> paths)
        {
            if (paths == null) return;
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p) || !_seen.Add(p)) continue;
                _paths.Add(p);
            }
            string summary = PinPickerModel.ItemSummary(_paths);
            PathText.Text = summary;
            AutomationProperties.SetName(PathText, "Items to add: " + summary);
        }

        /// <summary>Flashes the window so a late launch is noticed when the picker is behind another
        /// window. It never takes focus.</summary>
        internal void FlashForAttention()
        {
            NM.FlashForAttention(new WindowInteropHelper(this).Handle);
        }

        // ---- placement -------------------------------------------------------

        /// <summary>Shows the picker with its corner near the pointer position taken when the verb was
        /// chosen, on the monitor that pointer is on.</summary>
        internal void ShowAt(NM.POINT pointerPx)
        {
            _pointer = pointerPx;
            _havePointer = true;
            Show();
            Activate();
            Topmost = true;
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            PlaceNearPointer();
        }

        /// <summary>Positions the window in device pixels against the work area of the monitor under the
        /// pointer. The scale is that monitor's own, from GetDpiForMonitor. A window that has not yet
        /// moved to that monitor still has its old monitor's size, so its size is scaled to what it
        /// will be there before the arithmetic runs.</summary>
        private void PlaceNearPointer()
        {
            if (!_havePointer || _placing) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            _placing = true;
            try
            {
                NM.RECT work;
                int monitorDpi;
                NM.MonitorAtPoint(_pointer, out work, out monitorDpi);

                NM.RECT r;
                if (!NM.GetWindowRect(hwnd, out r)) return;
                int windowDpi = NM.DpiForWindow(hwnd);
                double ratio = windowDpi > 0 ? (double)monitorDpi / windowDpi : 1.0;

                var size = new Size(r.Width * ratio, r.Height * ratio);
                var area = new Rect(work.Left, work.Top, work.Width, work.Height);
                int offset = (int)Math.Round(20.0 * monitorDpi / 96.0);
                var corner = PickerPlacement.Place(new Point(_pointer.X, _pointer.Y), size, area, offset);

                int x = (int)Math.Round(corner.X), y = (int)Math.Round(corner.Y);
                if (x == r.Left && y == r.Top) return;
                NM.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, NM.SWP_NOSIZE | NM.SWP_NOZORDER | NM.SWP_NOACTIVATE);
            }
            catch (Exception ex)
            {
                Log.Error("picker placement failed", ex);
            }
            finally { _placing = false; }
        }

        // ---- lists -----------------------------------------------------------

        private const string NewGroupText = "New group...";
        private const string NewTabText = "New tab...";
        private const string FirstTabText = "A new first tab";

        private void BuildLists(PinMemory last)
        {
            _groupSnapshot = _groups.Groups.ToList();
            var choice = PinPickerModel.DefaultChoice(_groupSnapshot, last);

            _building = true;
            try
            {
                GroupList.Items.Clear();
                foreach (var g in _groupSnapshot) GroupList.Items.Add(MakeItem(g.Name, g));
                GroupList.Items.Add(MakeItem(NewGroupText, null));
                GroupList.SelectedIndex = choice.IsNewGroup ? _groupSnapshot.Count : choice.GroupIndex;
            }
            finally { _building = false; }

            FillTabs(choice.TabIndex);
            UpdateNameBoxes();
            if (_groupSnapshot.Count == 0) NewGroupBox.Visibility = Visibility.Visible;
        }

        private ListBoxItem MakeItem(string text, object tag)
        {
            return new ListBoxItem { Content = text, Tag = tag, Foreground = _fg, Padding = new Thickness(4, 3, 4, 3) };
        }

        /// <summary>Refills the tab list for the selected group. "New group..." implies a new first tab,
        /// so its list is a single disabled entry.</summary>
        private void FillTabs(int wantedTab)
        {
            _building = true;
            try
            {
                TabList.Items.Clear();
                var group = SelectedGroup();
                if (IsNewGroupSelected())
                {
                    TabList.Items.Add(MakeItem(FirstTabText, null));
                    TabList.SelectedIndex = 0;
                    TabList.IsEnabled = false;
                    return;
                }

                TabList.IsEnabled = true;
                var tabs = group != null ? group.Tabs : new List<PinnedTab>();
                foreach (var t in tabs)
                    TabList.Items.Add(MakeItem(string.IsNullOrWhiteSpace(t.Name) ? "Main" : t.Name, t));
                TabList.Items.Add(MakeItem(NewTabText, null));

                int index = wantedTab == PinChoice.NewIndex ? tabs.Count : wantedTab;
                if (index < 0 || index > tabs.Count) index = 0;
                TabList.SelectedIndex = index;
            }
            finally { _building = false; }
        }

        private bool IsNewGroupSelected()
        {
            return GroupList.SelectedIndex < 0 || GroupList.SelectedIndex >= _groupSnapshot.Count;
        }

        private PinnedGroup SelectedGroup()
        {
            int i = GroupList.SelectedIndex;
            return i >= 0 && i < _groupSnapshot.Count ? _groupSnapshot[i] : null;
        }

        private bool IsNewTabSelected()
        {
            var group = SelectedGroup();
            return group != null && TabList.SelectedIndex >= group.Tabs.Count;
        }

        private void UpdateNameBoxes()
        {
            NewGroupBox.Visibility = IsNewGroupSelected() ? Visibility.Visible : Visibility.Collapsed;
            NewTabBox.Visibility = IsNewTabSelected() ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnGroupChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_building) return;
            FillTabs(0);
            UpdateNameBoxes();
        }

        private void OnTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_building) return;
            UpdateNameBoxes();
        }

        /// <summary>The group list has focus on open, so Up and Down choose a group at once. With no
        /// groups the only thing to do is name the first one, so the name box takes focus.</summary>
        private void FocusStartList()
        {
            if (_groupSnapshot.Count == 0) { NewGroupBox.Focus(); return; }
            var item = GroupList.ItemContainerGenerator.ContainerFromIndex(GroupList.SelectedIndex) as ListBoxItem;
            if (item != null) item.Focus(); else GroupList.Focus();
        }

        // ---- confirm ---------------------------------------------------------

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            try
            {
                PinnedGroup group = null;
                PinnedTab tab = null;

                if (IsNewGroupSelected())
                {
                    group = _groups.CreateGroup(PinPickerModel.ResolveName(NewGroupBox.Text, PinPickerModel.DefaultGroupName));
                    tab = group != null ? group.Active : null;
                }
                else
                {
                    group = SelectedGroup();
                    if (group != null)
                    {
                        if (IsNewTabSelected())
                            tab = _groups.AddTab(group, PinPickerModel.ResolveName(NewTabBox.Text, PinPickerModel.DefaultTabName));
                        else
                            tab = group.Tabs[TabList.SelectedIndex];
                    }
                }

                if (group != null && tab != null)
                {
                    _groups.AddPins(tab, _paths.ToList());
                    Remember(group, tab);
                }
                else
                {
                    Log.Info("Add to PowerPane found no group or tab to add to.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("pin confirm failed", ex);
            }
            Close();
        }

        /// <summary>Saves the group and tab for the next picker. AddPins turns an implicit tab into a
        /// numbered subfolder, so the tab is looked up again in the rescanned store rather than saved
        /// from the object used to pin.</summary>
        private void Remember(PinnedGroup group, PinnedTab tab)
        {
            try
            {
                var memory = new PinMemory { GroupFolder = group.FolderPath, TabFolder = tab.FolderPath };
                var rescanned = _groups.Groups.FirstOrDefault(g =>
                    string.Equals(g.FolderPath, group.FolderPath, StringComparison.OrdinalIgnoreCase));
                if (rescanned != null)
                {
                    var found = rescanned.Tabs.FirstOrDefault(t =>
                        string.Equals(t.FolderPath, tab.FolderPath, StringComparison.OrdinalIgnoreCase));
                    if (found == null && string.Equals(tab.FolderPath, group.FolderPath, StringComparison.OrdinalIgnoreCase))
                        found = rescanned.Tabs.FirstOrDefault();   // the implicit tab became the first subfolder
                    if (found != null) memory.TabFolder = found.FolderPath;
                }
                memory.Save(PinMemory.DefaultPath);
            }
            catch (Exception ex)
            {
                Log.Error("pin picker memory not saved", ex);
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ---- screen readers --------------------------------------------------

        private void NameControls()
        {
            AutomationProperties.SetName(this, "Add to PowerPane");
            AutomationProperties.SetName(GroupList, "Group");
            AutomationProperties.SetName(TabList, "Tab");
            AutomationProperties.SetName(NewGroupBox, "New group name");
            AutomationProperties.SetName(NewTabBox, "New tab name");
            AutomationProperties.SetName(HintText, "PowerPane is not running");
            AutomationProperties.SetName(OkBtn, "Add");
            AutomationProperties.SetName(CancelBtn, "Cancel");
            AutomationProperties.SetName(TitleText, "Add to PowerPane");
        }

        // ---- self-styling from registry theme ----
        private Brush _fg = Brushes.Black;

        private void ApplyTheme()
        {
            bool dark = false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = key?.GetValue("AppsUseLightTheme");
                    dark = v is int && (int)v == 0;
                }
            }
            catch { }

            var bg = dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF7, 0xF7, 0xF7);
            var border = dark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xCF, 0xCF, 0xCF);
            _fg = new SolidColorBrush(dark ? Color.FromRgb(0xED, 0xED, 0xED) : Color.FromRgb(0x1A, 0x1A, 0x1A));
            var borderBrush = new SolidColorBrush(border);

            Shell.Background = new SolidColorBrush(bg);
            Shell.BorderBrush = borderBrush;
            TitleText.Foreground = _fg;
            PathText.Foreground = _fg;
            GroupLabel.Foreground = _fg;
            TabLabel.Foreground = _fg;
            HintText.Foreground = _fg;
            GroupList.Foreground = _fg;
            TabList.Foreground = _fg;
            GroupList.BorderBrush = borderBrush;
            TabList.BorderBrush = borderBrush;
        }
    }
}
