using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WinForms = System.Windows.Forms;
using PowerPane.Interop;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.UI
{
    /// <summary>The Groups section. Each group holds one or more tabs; a single-tab group looks like a
    /// plain group, several tabs show a scrollable tab row. Pins, tabs, and groups all drag to reorder,
    /// dragging a group onto another merges its tabs in, and dragging a tab to empty space makes it a
    /// group. Naming uses a top-level prompt because the embedded pane cannot host an inline text box.</summary>
    public partial class GroupSection : UserControl
    {
        private const string PinFormat = "PowerPanePin";
        private const string GroupFormat = "PowerPaneGroup";
        private const string TabFormat = "PowerPaneTab";

        private Action<string> _navigate;
        private Point _dragStart;
        private bool _maybeDrag;
        // Every pin, group header and tab shares these fields, so the press has to be tied to the element
        // it landed on; otherwise the drag is claimed by whichever one the pointer has reached by the time
        // it passes the threshold, and the row that moves is not the row that was grabbed.
        private object _pressedSource;
        private readonly Dictionary<FrameworkElement, DropLineAdorner> _adorners = new Dictionary<FrameworkElement, DropLineAdorner>();
        private DragGhostAdorner _ghost;
        private AdornerLayer _ghostLayer;
        private Vector _grab;

        public GroupSection()
        {
            InitializeComponent();
            // Empty space in the section is a drop target: a tab dropped here becomes its own group,
            // and files/folders dropped here are pinned to the last group.
            Root.Background = Brushes.Transparent;
            Root.AllowDrop = true;
            Root.DragOver += OnRootDragOver;
            Root.Drop += OnRootDrop;
            Root.DragLeave += (s, e) => FolderTreeNode.ClearDropHighlight();
            Unloaded += (s, e) => Unwire();
        }

        // The section listens for its own data so a groups change repaints just this section, and
        // probe/icon results arriving from the worker repaint it with the corrected state.
        private bool _sectionWired;
        private bool _rebuildQueued;
        private bool _detached;

        private void Wire()
        {
            if (_sectionWired || _detached) return;
            _sectionWired = true;
            if (App.Groups != null) App.Groups.GroupsChanged += QueueRebuild;
            PathStatus.Changed += QueueRebuild;
        }

        private void Unwire()
        {
            if (!_sectionWired) return;
            _sectionWired = false;
            if (App.Groups != null) App.Groups.GroupsChanged -= QueueRebuild;
            PathStatus.Changed -= QueueRebuild;
        }

        /// <summary>Release this section's subscriptions for good. Called by the pane, because a pane
        /// hosted in an HwndSource never raises Unloaded. It is final: a rebuild queued before the pane
        /// closed used to run afterwards and subscribe the section to the static events again, which
        /// kept it in memory and rebuilding for the life of the process
        /// (prisant-labs/windows-explorer-powerpane#69).</summary>
        public void Detach() { _detached = true; Unwire(); }

        private void QueueRebuild()
        {
            if (_rebuildQueued || _detached) return;
            _rebuildQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _rebuildQueued = false;
                if (_navigate != null && !_detached) Build(_navigate);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnRootDragOver(object sender, DragEventArgs e)
        {
            UpdateGhost(e);
            if (e.Data.GetDataPresent(TabFormat)) e.Effects = DragDropEffects.Move;
            // The Link cursor tells the truth here: dropping on empty space creates a shortcut pin.
            else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Link;
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void OnRootDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(TabFormat))
            {
                e.Handled = true;
                var tr = e.Data.GetData(TabFormat) as TabRef;
                if (tr == null) return;
                var src = FindTab(tr.TabFolder);
                if (src.Item2 != null)
                {
                    var srcGroup = src.Item1; var srcTab = src.Item2;
                    App.Groups.PostEdit(store => store.MoveTabToNewGroup(srcGroup, srcTab));
                }
                return;
            }
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Handled = true;
                FolderTreeNode.ClearDropHighlight();
                PinToLastGroup(e.Data.GetData(DataFormats.FileDrop) as string[]);
            }
        }

        // Pin dropped paths to the last group's active tab; with no groups yet, create one.
        private static void PinToLastGroup(string[] paths)
        {
            if (paths == null || paths.Length == 0 || App.Groups == null) return;
            // Through PostEdit, so a profile switch before this runs cannot create "Pinned" in the
            // new profile's folder: CreateGroup takes no group for a stale check to catch (#75).
            App.Groups.PostEdit(store =>
            {
                try
                {
                    var groups = store.Groups;
                    var last = groups.Count > 0 ? groups[groups.Count - 1] : store.CreateGroup("Pinned");
                    if (last == null) return;
                    store.AddPins(last.Active, paths.Where(PathIsReal).ToList());
                }
                catch (Exception ex) { Log.Error("pin to last group", ex); }
            });
        }

        // Pin dropped paths (files or folders) to a specific group. Runs on the worker because it
        // verifies the paths and writes .lnk files.
        private static void PinPaths(PinnedGroup group, string[] paths)
        {
            if (group == null || paths == null || paths.Length == 0 || App.Groups == null) return;
            App.Groups.PostEdit(store =>
            {
                try { store.AddPins(group.Active, paths.Where(PathIsReal).ToList()); }
                catch (Exception ex) { Log.Error("pin paths", ex); }
            });
        }

        private static bool PathIsReal(string path)
        {
            try { return !string.IsNullOrEmpty(path) && (System.IO.Directory.Exists(path) || System.IO.File.Exists(path)); }
            catch { return false; }
        }

        public void Build(Action<string> navigate)
        {
            if (_detached) return;
            _navigate = navigate;
            Wire();
            Root.Children.Clear();
            _adorners.Clear();
            if (App.Groups == null) return;

            var groups = App.Groups.Groups;
            long traced = PerfTrace.GroupBuildBegin(groups.Count);
            for (int i = 0; i < groups.Count; i++)
            {
                AddGroup(groups[i]);
                if (i < groups.Count - 1) Root.Children.Add(UiHelpers.MakeSeparator());
            }
            if (groups.Count == 0) Root.Children.Add(BuildAddGroupRow());
            PerfTrace.GroupBuildEnd(groups.Count, traced);
        }

        private void Rebuild() { Build(_navigate); }

        private void AddGroup(PinnedGroup group)
        {
            group.Expanded = UiState.GetExpanded("group:" + group.FolderPath, group.Expanded);
            var items = new StackPanel();
            RotateTransform chevron;
            FrameworkElement header = group.Tabs.Count > 1
                ? BuildTabRow(group, items, out chevron)
                : BuildSingleHeader(group, items, out chevron);

            // active tab pins (folders and files alike; files open, folders navigate)
            var active = group.Active;
            if (active != null)
            {
                var capturedGroup = group;
                var pinCtx = new PinDropContext
                {
                    GroupName = GroupStore.DisplayName(group),
                    PinHere = paths => PinPaths(capturedGroup, paths)
                };
                foreach (var pin in active.Items)
                {
                    var captured = pin;
                    var node = new FolderTreeNode(pin.DisplayName, pin.TargetPath, pin.Exists, _navigate, 0,
                        captured.IsFile, null, pinCtx);
                    LazyContextMenu.Attach(node.Row, () => BuildPinMenu(group, active, captured, node.Row));
                    WirePinDragSource(node.Row, captured);
                    items.Children.Add(node);
                }
            }
            EnableItemsDrop(items, group);

            if (!group.Expanded) { items.Visibility = Visibility.Collapsed; items.MaxHeight = 0; }
            else chevron.Angle = 90;

            // Header and rows go in together, so a group with nothing left after filtering goes away
            // instead of leaving its name over an empty gap.
            var block = new StackPanel { Tag = UiHelpers.FilterBlockTag };
            block.Children.Add(header);
            block.Children.Add(items);
            Root.Children.Add(block);
        }

        // ---- single-tab header ----
        private FrameworkElement BuildSingleHeader(PinnedGroup group, StackPanel items, out RotateTransform chevron)
        {
            var tab = group.Tabs[0];
            TextBlock label;
            Grid header = null;
            header = UiHelpers.BuildHeader(tab.Name,
                () => { group.Expanded = !group.Expanded; UiState.SetExpanded("group:" + group.FolderPath, group.Expanded); UiHelpers.ToggleExpand(items, ChevronOf(header), group.Expanded); },
                () => RenameGroupOrTab(group, tab, header),
                out chevron, out label);
            header.ContextMenu = BuildGroupMenu(group, tab, header);
            AddPlusButton(header, group);
            WireGroupDrag(header, group);
            EnableGroupDrop(header, group);
            _chevrons[header] = chevron;
            return header;
        }

        private readonly Dictionary<FrameworkElement, RotateTransform> _chevrons = new Dictionary<FrameworkElement, RotateTransform>();
        private RotateTransform ChevronOf(FrameworkElement header) { RotateTransform r; return _chevrons.TryGetValue(header, out r) ? r : null; }

        // ---- multi-tab row ----
        private FrameworkElement BuildTabRow(PinnedGroup group, StackPanel items, out RotateTransform chevron)
        {
            var grid = new Grid { Margin = new Thickness(6, 6, 8, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            chevron = new RotateTransform(0);
            var glyph = new TextBlock
            {
                Text = "\uE76C",
                FontFamily = UiHelpers.IconFont(),
                FontSize = 10,
                Foreground = UiHelpers.AppBrush("ChevronColor"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = chevron
            };
            var rt = chevron;
            var chevHit = new Border { Width = 18, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = glyph };
            chevHit.MouseLeftButtonUp += (s, e) => { group.Expanded = !group.Expanded; UiState.SetExpanded("group:" + group.FolderPath, group.Expanded); UiHelpers.ToggleExpand(items, rt, group.Expanded); };
            WireGroupDrag(chevHit, group); // drag the chevron to move the whole tab group
            Grid.SetColumn(chevHit, 0);
            grid.Children.Add(chevHit);

            var bar = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < group.Tabs.Count; i++)
                bar.Children.Add(BuildTabButton(group, group.Tabs[i], i));

            var scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = bar,
                Tag = "tabscroll" // SidebarControl's wheel handler routes horizontal scroll here
            };
            scroller.AllowDrop = true;
            scroller.DragOver += (s, e) => OnTabBarDragOver(bar, group, e);
            scroller.DragLeave += (s, e) => RemoveTabPlaceholder();
            scroller.Drop += (s, e) => OnTabBarDrop(bar, group, e);
            Grid.SetColumn(scroller, 1);
            grid.Children.Add(scroller);

            var addTab = new Button
            {
                Content = "+",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = UiHelpers.AppBrush("ChevronColor"),
                Cursor = Cursors.Hand,
                Padding = new Thickness(6, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = AddMenuLabels.PlusName(group)
            };
            UiHelpers.Named(addTab, AddMenuLabels.PlusName(group));
            // Opens the Add menu rather than a name box, so a folder, a file, or a group can be added
            // from the same button. The menu is built on each click, so it sees the selected tab.
            addTab.Click += (s, e) => GroupAddMenu.Open(addTab, group, null);
            Grid.SetColumn(addTab, 2);
            grid.Children.Add(addTab);

            grid.ContextMenu = BuildGroupMenu(group, group.Active, grid);
            // The row is a drop target for groups (merge) and tabs (move in).
            EnableGroupDrop(grid, group);
            return grid;
        }

        private FrameworkElement BuildTabButton(PinnedGroup group, PinnedTab tab, int index)
        {
            bool activeTab = index == group.ActiveTab;
            var text = new TextBlock
            {
                Text = tab.Name,
                FontSize = 12,
                FontWeight = activeTab ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = activeTab ? UiHelpers.AppBrush("TextPrimary") : UiHelpers.AppBrush("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var underline = new Border
            {
                Height = 2,
                Margin = new Thickness(0, 2, 0, 0),
                Background = activeTab ? UiHelpers.AppBrush("AccentBrush") : Brushes.Transparent,
                CornerRadius = new CornerRadius(1)
            };
            var stack = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
            stack.Children.Add(text);
            stack.Children.Add(underline);

            var btn = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = stack, Tag = "tab" };
            btn.ContextMenu = BuildTabMenu(group, tab, btn);

            btn.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { e.Handled = true; RenameTab(group, tab, btn); return; }
                BeginMaybeDrag(btn, e);
            };
            btn.PreviewMouseLeftButtonUp += (s, e) =>
            {
                // Only the tab the press landed on counts as a click, so releasing over a tab after
                // pressing somewhere else does not switch to it.
                if (_maybeDrag && ReferenceEquals(_pressedSource, btn))
                {
                    App.Groups.SetActiveTab(group, index);
                    group.Expanded = true; // clicking a tab while collapsed opens the group on that tab
                    UiState.SetExpanded("group:" + group.FolderPath, true);
                    Rebuild();
                }
                EndMaybeDrag();
            };
            btn.MouseMove += (s, e) =>
            {
                if (!DragStarted(btn, e)) return;
                try
                {
                    var data = new DataObject();
                    data.SetData(TabFormat, new TabRef { GroupFolder = group.FolderPath, TabFolder = tab.FolderPath });
                    SidebarControl.SuppressAutoHide = true;
                    // No floating ghost for tabs; the inline placeholder is the preview.
                    DragDrop.DoDragDrop(btn, data, DragDropEffects.Move);
                }
                catch (Exception ex) { Log.Error("tab drag", ex); }
                finally { SidebarControl.SuppressAutoHide = false; RemoveTabPlaceholder(); }
            };

            return btn;
        }

        private FrameworkElement _tabPlaceholder;
        private System.Windows.Controls.Panel _tabPlaceholderBar;
        private int _tabPlaceholderIndex = -1;

        private void OnTabBarDragOver(StackPanel bar, PinnedGroup group, DragEventArgs e)
        {
            e.Handled = true;
            if (e.Data.GetDataPresent(TabFormat))
            {
                int index = ComputeTabIndex(bar, e);
                // Only move the placeholder when the target index actually changes, so it does not jitter.
                if (index != _tabPlaceholderIndex || !ReferenceEquals(_tabPlaceholderBar, bar))
                {
                    var tr = e.Data.GetData(TabFormat) as TabRef;
                    var src = tr != null ? FindTab(tr.TabFolder) : Tuple.Create<PinnedGroup, PinnedTab>(null, null);
                    ShowTabPlaceholder(bar, index, src.Item2 != null ? src.Item2.Name : "Tab");
                }
                e.Effects = DragDropEffects.Move;
                return;
            }
            UpdateGhost(e);
            RemoveTabPlaceholder();
            if (e.Data.GetDataPresent(GroupFormat) || e.Data.GetDataPresent(PinFormat)) e.Effects = DragDropEffects.Move;
            else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Link; // dropping here pins (creates a shortcut)
            else e.Effects = DragDropEffects.None;
        }

        private void OnTabBarDrop(StackPanel bar, PinnedGroup group, DragEventArgs e)
        {
            e.Handled = true;
            RemoveTabPlaceholder();
            int index = ComputeTabIndex(bar, e);
            if (e.Data.GetDataPresent(TabFormat))
            {
                var tr = e.Data.GetData(TabFormat) as TabRef;
                if (tr != null) DropTabAtIndex(group, index, tr);
                return;
            }
            OnGroupDrop(group, e);
        }

        private int ComputeTabIndex(System.Windows.Controls.Panel bar, DragEventArgs e)
        {
            double x = e.GetPosition(bar).X;
            int idx = 0;
            foreach (var child in bar.Children)
            {
                var fe = child as FrameworkElement;
                if (fe == null) continue;
                var t = fe.Tag as string;
                if (t != "tab") continue; // skip the placeholder
                double left = fe.TranslatePoint(new Point(0, 0), bar).X;
                if (x > left + fe.ActualWidth / 2) idx++; else break;
            }
            return idx;
        }

        private void ShowTabPlaceholder(StackPanel bar, int index, string name)
        {
            RemoveTabPlaceholder();
            var ph = new Border
            {
                Tag = "ph",
                Background = UiHelpers.AppBrush("ItemHoverBackground"),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(2, 0, 2, 0),
                Child = new TextBlock
                {
                    Text = name,
                    FontSize = 12,
                    Opacity = 0.6,
                    Foreground = UiHelpers.AppBrush("TextPrimary"),
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            int insertAt = Math.Min(index, bar.Children.Count);
            bar.Children.Insert(insertAt, ph);
            _tabPlaceholder = ph;
            _tabPlaceholderBar = bar;
            _tabPlaceholderIndex = index;
        }

        private void RemoveTabPlaceholder()
        {
            if (_tabPlaceholder != null && _tabPlaceholderBar != null && _tabPlaceholderBar.Children.Contains(_tabPlaceholder))
                _tabPlaceholderBar.Children.Remove(_tabPlaceholder);
            _tabPlaceholder = null;
            _tabPlaceholderBar = null;
            _tabPlaceholderIndex = -1;
        }

        private void DropTabAtIndex(PinnedGroup destGroup, int index, TabRef tr)
        {
            var src = FindTab(tr.TabFolder);
            if (src.Item2 == null) return;
            if (ReferenceEquals(src.Item1, destGroup))
            {
                var ordered = destGroup.Tabs.Where(t => !ReferenceEquals(t, src.Item2)).ToList();
                if (index < 0) index = 0;
                if (index > ordered.Count) index = ordered.Count;
                ordered.Insert(index, src.Item2);
                App.Groups.PostEdit(store => store.ReorderTabs(destGroup, ordered));
            }
            else
            {
                var srcTab = src.Item2;
                App.Groups.PostEdit(store => store.MoveTabToGroup(srcTab, destGroup));
            }
        }

        // ---- group drag (header / row) ----
        private void WireGroupDrag(FrameworkElement source, PinnedGroup group)
        {
            source.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) return;
                BeginMaybeDrag(source, e);
            };
            source.PreviewMouseLeftButtonUp += (s, e) => EndMaybeDrag();
            source.MouseMove += (s, e) =>
            {
                if (!DragStarted(source, e)) return;
                try
                {
                    var data = new DataObject();
                    data.SetData(GroupFormat, new GroupRef { FolderPath = group.FolderPath });
                    StartGhost(source, e);
                    DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
                }
                catch (Exception ex) { Log.Error("group drag", ex); }
                finally { EndGhost(); }
            };
        }

        private void EnableGroupDrop(FrameworkElement target, PinnedGroup group)
        {
            target.AllowDrop = true;
            target.DragOver += (s, e) =>
            {
                UpdateGhost(e);
                if (e.Data.GetDataPresent(GroupFormat))
                {
                    // Top third inserts before, bottom third inserts after (with a line), the middle
                    // merges the dragged group in as tabs.
                    double y = e.GetPosition(target).Y, h = Math.Max(1, target.ActualHeight);
                    if (y < h * 0.33) Adorner(target).Update(1);
                    else if (y > h * 0.66) Adorner(target).Update(h - 1);
                    else HideAdorner(target);
                    e.Effects = DragDropEffects.Move;
                }
                else
                {
                    HideAdorner(target);
                    if (e.Data.GetDataPresent(TabFormat) || e.Data.GetDataPresent(PinFormat)) e.Effects = DragDropEffects.Move;
                    else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Link; // dropping here pins (creates a shortcut)
                    else e.Effects = DragDropEffects.None;
                }
                e.Handled = true;
            };
            target.DragLeave += (s, e) => HideAdorner(target);
            target.Drop += (s, e) =>
            {
                e.Handled = true;
                HideAdorner(target);
                if (e.Data.GetDataPresent(GroupFormat))
                {
                    var gr = e.Data.GetData(GroupFormat) as GroupRef;
                    var src = App.Groups.Groups.FirstOrDefault(g => gr != null && string.Equals(g.FolderPath, gr.FolderPath, StringComparison.OrdinalIgnoreCase));
                    if (src == null || ReferenceEquals(src, group)) return;
                    double y = e.GetPosition(target).Y, h = Math.Max(1, target.ActualHeight);
                    if (y >= h * 0.33 && y <= h * 0.66) App.Groups.PostEdit(store => store.MergeGroups(src, group));
                    else ReorderGroupRelative(src, group, y > h * 0.66);
                    return;
                }
                OnGroupDrop(group, e);
            };
        }

        private void ReorderGroupRelative(PinnedGroup dragged, PinnedGroup target, bool after)
        {
            var ordered = App.Groups.Groups.ToList();
            ordered.Remove(dragged);
            int idx = ordered.FindIndex(g => ReferenceEquals(g, target));
            if (idx < 0) return;
            ordered.Insert(after ? idx + 1 : idx, dragged);
            App.Groups.PostEdit(store => store.ReorderGroups(ordered));
        }

        private void OnGroupDrop(PinnedGroup group, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(GroupFormat))
                {
                    var gr = e.Data.GetData(GroupFormat) as GroupRef;
                    var src = App.Groups.Groups.FirstOrDefault(g => string.Equals(g.FolderPath, gr.FolderPath, StringComparison.OrdinalIgnoreCase));
                    if (src != null && !ReferenceEquals(src, group)) App.Groups.PostEdit(store => store.MergeGroups(src, group));
                    return;
                }
                if (e.Data.GetDataPresent(TabFormat))
                {
                    var tr = e.Data.GetData(TabFormat) as TabRef;
                    var src = FindTab(tr.TabFolder);
                    if (src.Item2 != null && !ReferenceEquals(src.Item1, group))
                    {
                        var srcTab = src.Item2;
                        App.Groups.PostEdit(store => store.MoveTabToGroup(srcTab, group));
                    }
                    return;
                }
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    // Dropping on a group header/tab row pins the items (files or folders).
                    PinPaths(group, e.Data.GetData(DataFormats.FileDrop) as string[]);
                    return;
                }
                if (e.Data.GetDataPresent(PinFormat))
                {
                    var pr = e.Data.GetData(PinFormat) as PinRef;
                    if (pr != null) MovePinToTabEnd(pr.LinkPath, group.Active);
                }
            }
            catch (Exception ex) { Log.Error("group drop", ex); }
        }

        /// <summary>Remember the element a press landed on and start watching for a drag.</summary>
        private void BeginMaybeDrag(object source, MouseButtonEventArgs e)
        {
            _pressedSource = source;
            _dragStart = e.GetPosition(null);
            _maybeDrag = true;
        }

        /// <summary>True once the pointer has travelled far enough for the press on this element to be a drag.</summary>
        private bool DragStarted(object source, MouseEventArgs e)
        {
            if (!_maybeDrag || !ReferenceEquals(_pressedSource, source)) return false;
            if (e.LeftButton != MouseButtonState.Pressed) { _maybeDrag = false; return false; }
            var p = e.GetPosition(null);
            if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return false;
            _maybeDrag = false;
            return true;
        }

        private void EndMaybeDrag() { _maybeDrag = false; _pressedSource = null; }

        // ---- pins ----
        private void WirePinDragSource(FrameworkElement source, PinnedFolder pin)
        {
            source.PreviewMouseLeftButtonDown += (s, e) => BeginMaybeDrag(source, e);
            source.PreviewMouseLeftButtonUp += (s, e) => EndMaybeDrag();
            source.MouseMove += (s, e) =>
            {
                if (!DragStarted(source, e)) return;
                try
                {
                    var data = new DataObject();
                    data.SetData(PinFormat, new PinRef { LinkPath = pin.LinkPath });
                    StartGhost(source, e);
                    DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
                }
                catch (Exception ex) { Log.Error("pin drag", ex); }
                finally { EndGhost(); }
            };
        }

        private void EnableItemsDrop(StackPanel items, PinnedGroup group)
        {
            items.AllowDrop = true;
            items.DragOver += (s, e) =>
            {
                e.Handled = true;
                UpdateGhost(e);
                if (e.Data.GetDataPresent(GroupFormat))
                {
                    // Dropping a group over the body of another group reorders it just after.
                    Adorner(items).Update(items.ActualHeight > 0 ? items.ActualHeight - 1 : 0);
                    e.Effects = DragDropEffects.Move;
                    return;
                }
                if (e.Data.GetDataPresent(TabFormat)) { e.Effects = DragDropEffects.None; return; }
                int index; double lineY;
                ComputeItemDrop(items, e, out index, out lineY);
                Adorner(items).Update(lineY);
                // Reordering a pin is a move; a FileDrop between rows pins there (creates a shortcut).
                e.Effects = e.Data.GetDataPresent(PinFormat) ? DragDropEffects.Move : DragDropEffects.Link;
            };
            items.DragLeave += (s, e) => HideAdorner(items);
            items.Drop += (s, e) =>
            {
                HideAdorner(items);
                e.Handled = true;
                if (e.Data.GetDataPresent(GroupFormat))
                {
                    var gr = e.Data.GetData(GroupFormat) as GroupRef;
                    var src = App.Groups.Groups.FirstOrDefault(g => gr != null && string.Equals(g.FolderPath, gr.FolderPath, StringComparison.OrdinalIgnoreCase));
                    if (src != null && !ReferenceEquals(src, group)) ReorderGroupRelative(src, group, true);
                    return;
                }
                int index; double lineY;
                ComputeItemDrop(items, e, out index, out lineY);
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    // Pin files or folders at the drop line. Verification and .lnk writes run on the
                    // worker so a slow source volume cannot stall the input-attached UI thread.
                    FolderTreeNode.ClearDropHighlight();
                    var dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
                    var tab = group.Active;
                    int at = index;
                    App.Groups.PostEdit(store =>
                    {
                        try
                        {
                            foreach (var path in dropped ?? new string[0])
                                if (PathIsReal(path)) { store.AddPinAtIndex(tab, path, at); at++; }
                        }
                        catch (Exception ex) { Log.Error("pin at index", ex); }
                    });
                    return;
                }
                if (e.Data.GetDataPresent(PinFormat))
                {
                    var pr = e.Data.GetData(PinFormat) as PinRef;
                    if (pr != null) MovePinToIndex(pr.LinkPath, group.Active, index);
                }
            };
        }

        private void ComputeItemDrop(StackPanel items, DragEventArgs e, out int index, out double lineY)
        {
            double y = e.GetPosition(items).Y;
            index = items.Children.Count;
            lineY = items.ActualHeight > 0 ? items.ActualHeight - 1 : 0;
            for (int i = 0; i < items.Children.Count; i++)
            {
                var child = items.Children[i] as FrameworkElement;
                if (child == null) continue;
                double top = child.TranslatePoint(new Point(0, 0), items).Y;
                double mid = top + child.ActualHeight / 2;
                if (y < mid) { index = i; lineY = top; return; }
            }
        }

        private void MovePinToIndex(string linkPath, PinnedTab destTab, int index)
        {
            var hit = FindPin(linkPath);
            if (hit.Item2 == null || destTab == null) return;
            if (!ReferenceEquals(hit.Item1, destTab))
            {
                var folder = hit.Item2;
                // A pin dropped at a line on a sorted tab goes in at that line, in the order shown,
                // and the tab becomes Manual (#241). A manual tab keeps its append-at-end behavior.
                if (destTab.SortMode != PinSortMode.Manual)
                    App.Groups.PostEdit(store => store.MovePinToTabAt(folder, destTab, index));
                else
                    App.Groups.PostEdit(store => store.MovePinToTab(folder, destTab));
                return;
            }
            // The index counts the dragged row too. A drop that leaves the order as it is posts
            // nothing, so it never turns a sorted tab to Manual.
            var ordered = PinSorter.PlaceAtLine(destTab.Items, hit.Item2, index);
            if (ordered == null) return;
            App.Groups.PostEdit(store => store.ReorderPins(destTab, ordered));
        }

        private void MovePinToTabEnd(string linkPath, PinnedTab destTab)
        {
            var hit = FindPin(linkPath);
            if (hit.Item2 != null && destTab != null)
            {
                var folder = hit.Item2;
                App.Groups.PostEdit(store => store.MovePinToTab(folder, destTab));
            }
        }

        private static Tuple<PinnedTab, PinnedFolder> FindPin(string linkPath)
        {
            foreach (var g in App.Groups.Groups)
                foreach (var t in g.Tabs)
                {
                    var hit = t.Items.FirstOrDefault(p => string.Equals(p.LinkPath, linkPath, StringComparison.OrdinalIgnoreCase));
                    if (hit != null) return Tuple.Create(t, hit);
                }
            return Tuple.Create<PinnedTab, PinnedFolder>(null, null);
        }

        private static Tuple<PinnedGroup, PinnedTab> FindTab(string tabFolder)
        {
            foreach (var g in App.Groups.Groups)
            {
                var t = g.Tabs.FirstOrDefault(x => string.Equals(x.FolderPath, tabFolder, StringComparison.OrdinalIgnoreCase));
                if (t != null) return Tuple.Create(g, t);
            }
            return Tuple.Create<PinnedGroup, PinnedTab>(null, null);
        }

        // ---- menus ----
        private ContextMenu BuildGroupMenu(PinnedGroup group, PinnedTab activeTab, FrameworkElement owner)
        {
            var menu = new ContextMenu();
            menu.Items.Add(Item("Rename", () => RenameGroupOrTab(group, group.Tabs.Count == 1 ? group.Tabs[0] : activeTab, owner)));
            menu.Items.Add(new Separator());
            // The same entries as the "+" button's menu, each naming its target. Prompts open next to
            // the control this menu belongs to, read when an entry is chosen.
            GroupAddMenu.AddGroupMenuEntries(menu, group, activeTab, owner);
            // A group with one tab has no tab row, so this header menu is the only menu that tab has.
            // With several tabs, sorting is chosen per tab from the tab's own menu.
            if (group.Tabs.Count == 1) menu.Items.Add(BuildSortMenu(group.Tabs[0]));
            menu.Items.Add(Item("Move up", () => App.Groups.PostEdit(store => store.MoveGroup(group, -1))));
            menu.Items.Add(Item("Move down", () => App.Groups.PostEdit(store => store.MoveGroup(group, +1))));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Delete group", () => ConfirmDeleteGroup(group)));
            return menu;
        }

        private ContextMenu BuildTabMenu(PinnedGroup group, PinnedTab tab, FrameworkElement owner)
        {
            var menu = new ContextMenu();
            menu.Items.Add(Item("Rename tab", () => RenameTab(group, tab, owner)));
            menu.Items.Add(new Separator());
            // These land in the tab that was right-clicked, not the group's active tab: a group with
            // several tabs otherwise has no way to add a pin anywhere but the narrow strip past the
            // last tab, which always targets whichever tab happened to be active.
            GroupAddMenu.AddTabMenuEntries(menu, group, tab, owner);
            menu.Items.Add(BuildSortMenu(tab));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Move tab left", () => App.Groups.PostEdit(store => store.MoveTab(group, tab, -1))));
            menu.Items.Add(Item("Move tab right", () => App.Groups.PostEdit(store => store.MoveTab(group, tab, +1))));
            menu.Items.Add(Item("Make tab its own group", () => App.Groups.PostEdit(store => store.MoveTabToNewGroup(group, tab))));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Delete tab", () => ConfirmDeleteTab(group, tab)));
            return menu;
        }

        // The "Sort pins" submenu for one tab (#241). The pane rebuilds on GroupsChanged, so the
        // check mark reads the mode the tab has when the menu is built.
        private static MenuItem BuildSortMenu(PinnedTab tab)
        {
            return BuildSortMenu(tab.SortMode,
                mode => App.Groups.PostEdit(store => store.SetPinSort(tab, mode)),
                () => App.Groups.PostEdit(store => store.SortPinsOnce(tab)));
        }

        internal static MenuItem BuildSortMenu(PinSortMode current, Action<PinSortMode> setMode, Action sortOnce)
        {
            var sort = new MenuItem { Header = "Sort pins" };
            sort.Items.Add(SortChoice("Manual order", PinSortMode.Manual, current, setMode));
            sort.Items.Add(SortChoice("A to Z (keep sorted)", PinSortMode.Name, current, setMode));
            sort.Items.Add(SortChoice("Recently added (keep sorted)", PinSortMode.Recent, current, setMode));
            sort.Items.Add(new Separator());
            // Never checked: it writes the order once and leaves the tab in Manual.
            sort.Items.Add(Item("Sort A to Z once", sortOnce));
            return sort;
        }

        private static MenuItem SortChoice(string header, PinSortMode mode, PinSortMode current, Action<PinSortMode> setMode)
        {
            var mi = Item(header, () => setMode(mode));
            // The pane's template draws no check of its own, so the glyph below is what people see.
            // IsChecked is what UI Automation reports, so a screen reader can tell which mode is on.
            mi.IsCheckable = true;
            mi.IsChecked = mode == current;
            if (mode == current)
                mi.Icon = new TextBlock { Text = "\uE73E", FontFamily = UiHelpers.IconFont(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            return mi;
        }

        /// <summary>
        /// Point a pin somewhere else without removing and re-pinning it. A pin whose target moved, or
        /// was captured wrongly in the first place, keeps its name and its place in the group and only
        /// its destination changes. The text is taken as typed, because a share that is offline right
        /// now is still the path that was meant.
        /// </summary>
        private void EditPinPath(PinnedFolder pin, FrameworkElement owner)
        {
            try
            {
                if (pin == null || string.IsNullOrEmpty(pin.LinkPath)) return;
                // IsFile comes from the background probe cache (see LoadPins), never a live disk check
                // here, so choosing the picker never risks the same input-queue freeze that drove that
                // design in the first place.
                Func<string, string> browse = pin.IsFile
                    ? (Func<string, string>)BrowseForFile
                    : BrowseForFolder;
                var typed = AnchoredPrompt.Ask(owner, new PromptOptions { Title = "Edit path", Initial = pin.TargetPath ?? "", Browse = browse });
                if (typed == null) return;
                typed = typed.Trim().Trim('"');
                if (typed.Length == 0) return;
                if (string.Equals(typed, pin.TargetPath, StringComparison.OrdinalIgnoreCase)) return;
                // The prompt pumps messages, so the profile can change while it is open, and this pin
                // would then belong to the profile just left (#68).
                if (App.Groups == null || !App.Groups.IsCurrent(pin.LinkPath)) return;

                ShellLink.Create(pin.LinkPath, typed);
                Log.Info("pin \"" + pin.DisplayName + "\" now points at " + typed + ".");
                if (App.Groups != null) App.Groups.Reload();
            }
            catch (Exception ex) { Log.Error("edit the path of '" + (pin == null ? "" : pin.DisplayName) + "'", ex); }
        }

        private ContextMenu BuildPinMenu(PinnedGroup group, PinnedTab tab, PinnedFolder pin, FrameworkElement owner)
        {
            var menu = new ContextMenu();
            if (pin.IsFile)
            {
                menu.Items.Add(Item("Open file", () => OpenPinFile(pin)));
                menu.Items.Add(Item("Open containing folder", () =>
                {
                    try
                    {
                        var d = System.IO.Path.GetDirectoryName(pin.TargetPath);
                        if (!string.IsNullOrEmpty(d)) _navigate(d);
                    }
                    catch (Exception ex) { Log.Error("open containing folder", ex); }
                }));
            }
            else
            {
                menu.Items.Add(Item("Open", () => { if (pin.Exists) _navigate(pin.TargetPath); }));
                menu.Items.Add(Item("Open in new window", () => ExplorerNavigator.OpenNewWindow(pin.TargetPath)));
                var quickActions = BuildQuickActionsSubmenu(pin.TargetPath);
                if (quickActions != null) menu.Items.Add(quickActions);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Rename", () => { var v = AnchoredPrompt.Ask(owner, new PromptOptions { Title = "Rename pin", Initial = pin.DisplayName }); if (v != null) App.Groups.PostEdit(store => store.RenamePin(pin, v)); }));
            menu.Items.Add(Item("Edit path", () => EditPinPath(pin, owner)));
            menu.Items.Add(new Separator());

            var moveTo = new MenuItem { Header = "Move to group" };
            foreach (var g in App.Groups.Groups.Where(x => !ReferenceEquals(x, group)))
            {
                var dest = g;
                moveTo.Items.Add(Item(GroupStore.DisplayName(g), () => App.Groups.PostEdit(store => store.MovePinToGroup(pin, dest))));
            }
            if (moveTo.Items.Count == 0) moveTo.IsEnabled = false;
            menu.Items.Add(moveTo);

            menu.Items.Add(Item("Move up", () => App.Groups.PostEdit(store => store.MovePin(tab, pin, -1))));
            menu.Items.Add(Item("Move down", () => App.Groups.PostEdit(store => store.MovePin(tab, pin, +1))));
            menu.Items.Add(Item("Copy path", () => SafeClipboard(pin.TargetPath)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Remove pin", () => App.Groups.PostEdit(store => store.RemovePin(pin))));
            return menu;
        }

        /// <summary>"Quick actions" submenu for a pinned folder row (quick actions, first version): one
        /// item per enabled action, acting on this row's own folder. That folder is already known
        /// synchronously here, unlike the pane's own Quick actions section, which has to resolve "the
        /// folder the host Explorer window shows" through a COM call first -- so this still posts to
        /// WorkQueue (every action's own disk/process work must stay off the UI thread the same way)
        /// but with the folder already in hand. Null when the master switch is off or nothing is
        /// enabled, so an empty submenu never appears.</summary>
        private static MenuItem BuildQuickActionsSubmenu(string targetPath)
        {
            var settings = App.Settings != null ? App.Settings.Current : null;
            if (settings == null || settings.Sections == null || settings.QuickActions == null) return null;

            var section = settings.Sections.FirstOrDefault(x => x.Type == "quickactions");
            if (section == null || !section.Visible) return null;

            var menu = new MenuItem { Header = "Quick actions" };
            foreach (var setting in settings.QuickActions)
            {
                if (!setting.Enabled) continue;
                var def = QuickActionLibrary.Resolve(settings, setting.Id);
                if (def == null || !QuickActionLibrary.AppliesToPinnedRow(def)) continue;
                if (!QuickActionsSection.IsAvailable(def)) continue;

                var folder = targetPath;
                menu.Items.Add(Item(def.Name, () => WorkQueue.Post(() => QuickActionRunner.Run(def, folder, null))));
            }
            return menu.Items.Count > 0 ? menu : null;
        }

        private void ConfirmDeleteGroup(PinnedGroup group)
        {
            int count = group.Tabs.Sum(t => t.Items.Count);
            if (count > 0)
            {
                var r = WinForms.MessageBox.Show(
                    "Delete this group and its " + count + " pin(s)?", "PowerPane",
                    WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Warning);
                if (r != WinForms.DialogResult.Yes) return;
            }
            App.Groups.PostEdit(store => store.DeleteGroup(group));
        }

        private void ConfirmDeleteTab(PinnedGroup group, PinnedTab tab)
        {
            int count = tab.Items.Count;
            if (count > 0)
            {
                var r = WinForms.MessageBox.Show(
                    "Delete this tab and its " + count + " pin(s)?", "PowerPane",
                    WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Warning);
                if (r != WinForms.DialogResult.Yes) return;
            }
            App.Groups.PostEdit(store => store.DeleteTab(group, tab));
        }

        private void RenameGroupOrTab(PinnedGroup group, PinnedTab tab, FrameworkElement owner)
        {
            if (group.Tabs.Count == 1) RenameGroupSingle(group, owner);
            else RenameTab(group, tab, owner);
        }

        private void RenameGroupSingle(PinnedGroup group, FrameworkElement owner)
        {
            var v = AnchoredPrompt.Ask(owner, new PromptOptions
            {
                Title = "Rename",
                Initial = group.Tabs.Count == 1 ? group.Tabs[0].Name : GroupStore.DisplayName(group),
                InputName = "New group name",
                Validate = text => TabNameRules.Validate(text, null)
            });
            if (v != null) App.Groups.PostEdit(store => store.RenameGroup(group, v));
        }

        private void RenameTab(PinnedGroup group, PinnedTab tab, FrameworkElement owner)
        {
            // The tab's own name is not a duplicate of itself, so only the other tabs count.
            var v = AnchoredPrompt.Ask(owner, new PromptOptions
            {
                Title = "Rename tab",
                Initial = tab.Name,
                InputName = "New tab name",
                Validate = GroupAddMenu.RenameTabValidator(group, tab)
            });
            if (v != null) App.Groups.PostEdit(store => store.RenameTab(tab, v));
        }

        // ---- add group affordances ----
        private void AddPlusButton(Grid header, PinnedGroup group)
        {
            var plus = new Button
            {
                Content = "+",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Opacity = 0,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = UiHelpers.AppBrush("ChevronColor"),
                Cursor = Cursors.Hand,
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = AddMenuLabels.PlusName(group)
            };
            UiHelpers.Named(plus, AddMenuLabels.PlusName(group));
            // Hidden until the pointer is over the header, so the pane stays quiet. It also shows while
            // it has keyboard focus and while its menu is open, which the hover rule alone would hide.
            bool hovered = false, menuOpen = false;
            Action refresh = () => plus.Opacity = GroupAddMenu.PlusOpacity(hovered, plus.IsKeyboardFocused, menuOpen);
            plus.Click += (s, e) => GroupAddMenu.Open(plus, group, open => { menuOpen = open; refresh(); });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(plus, header.ColumnDefinitions.Count - 1);
            header.Children.Add(plus);
            header.MouseEnter += (s, e) => { hovered = true; refresh(); };
            header.MouseLeave += (s, e) => { hovered = false; refresh(); };
            plus.GotKeyboardFocus += (s, e) => refresh();
            plus.LostKeyboardFocus += (s, e) => refresh();
        }

        private FrameworkElement BuildAddGroupRow()
        {
            var btn = new Button
            {
                Content = "+  Add group",
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Foreground = UiHelpers.AppBrush("TextSecondary"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12
            };
            btn.Click += (s, e) =>
            {
                var v = AnchoredPrompt.Ask(btn, new PromptOptions
                {
                    Title = "New group",
                    InputName = "New group name",
                    Validate = text => TabNameRules.Validate(text, null)
                });
                if (!string.IsNullOrWhiteSpace(v)) App.Groups.PostEdit(store => store.CreateGroup(v));
            };
            return UiHelpers.Named(btn, "Add group");
        }

        // ---- drag ghost / drop line ----
        private void StartGhost(FrameworkElement source, MouseEventArgs e)
        {
            SidebarControl.SuppressAutoHide = true;
            try
            {
                var layer = AdornerLayer.GetAdornerLayer(Root);
                if (layer == null) return;
                _grab = (Vector)e.GetPosition(source);
                _ghost = new DragGhostAdorner(Root, source, source.RenderSize);
                layer.Add(_ghost);
                _ghostLayer = layer;
            }
            catch (Exception ex) { Log.Error("start ghost", ex); }
        }

        private void UpdateGhost(DragEventArgs e)
        {
            if (_ghost == null) return;
            var p = e.GetPosition(Root);
            _ghost.SetPosition(new Point(p.X - _grab.X, p.Y - _grab.Y));
        }

        private void EndGhost()
        {
            SidebarControl.SuppressAutoHide = false;
            try { if (_ghost != null && _ghostLayer != null) _ghostLayer.Remove(_ghost); } catch { }
            _ghost = null; _ghostLayer = null;
            FolderTreeNode.ClearDropHighlight(); // no row keeps a drag highlight after any drag ends
        }

        private DropLineAdorner Adorner(FrameworkElement host)
        {
            DropLineAdorner a;
            if (_adorners.TryGetValue(host, out a)) return a;
            var layer = AdornerLayer.GetAdornerLayer(host);
            a = new DropLineAdorner(host);
            if (layer != null) layer.Add(a);
            _adorners[host] = a;
            return a;
        }

        private void HideAdorner(FrameworkElement host)
        {
            DropLineAdorner a;
            if (_adorners.TryGetValue(host, out a)) a.Hide();
        }

        // ---- helpers ----
        internal static MenuItem Item(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (s, e) => { try { action(); } catch (Exception ex) { Log.Error("menu action", ex); } };
            return mi;
        }

        internal static string PickFolder(string startFolder = null)
        {
            return FolderPicker.Pick("Pick a folder to pin", startFolder);
        }

        internal static string PickFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Pick a file to pin" };
            PickerPlaces.AddTo(dlg);
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        // ---- Edit path Browse callbacks ----
        private static string BrowseForFolder(string current)
        {
            return FolderPicker.Pick("Pick a folder", NearestExistingFolder(current));
        }

        private static string BrowseForFile(string current)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Pick a file" };
            // GetDirectoryName throws on an empty string or on stray invalid characters, both of which
            // the text box can hold if the user cleared it or typed something odd before clicking
            // Browse, so the parent folder is computed defensively rather than trusted outright.
            string parent = null;
            try { parent = System.IO.Path.GetDirectoryName(current ?? string.Empty); } catch { }
            var dir = NearestExistingFolder(parent);
            if (!string.IsNullOrEmpty(dir)) dlg.InitialDirectory = dir;
            PickerPlaces.AddTo(dlg);
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        /// <summary>Walk up from a path that may no longer exist to the nearest ancestor folder that
        /// does, so a picker opens somewhere useful instead of wherever it defaults to. Skipped for a
        /// UNC path: probing an unreachable share one directory at a time can hang far longer than a
        /// user will wait for a dialog to open. Each check gives up after a second, and the walk stops
        /// at the first check that times out, because a volume that does not answer will not answer
        /// one level up either. A disconnected mapped drive used to time out at every level
        /// (prisant-labs/windows-explorer-powerpane#41).</summary>
        private static string NearestExistingFolder(string path)
        {
            return NearestExistingFolder(path, null);
        }

        internal static string NearestExistingFolder(string path, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(path)) return null;
            bool timedOut;
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
                return PathStatus.FolderExistsWithin(path, PathStatus.InteractiveProbeMs, out timedOut, exists) ? path : null;
            try
            {
                var dir = path;
                while (!string.IsNullOrEmpty(dir))
                {
                    if (PathStatus.FolderExistsWithin(dir, PathStatus.InteractiveProbeMs, out timedOut, exists)) return dir;
                    if (timedOut) return null;
                    dir = System.IO.Path.GetDirectoryName(dir);
                }
                return null;
            }
            catch { return null; }
        }

        private static void OpenPinFile(PinnedFolder pin)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(pin.TargetPath) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("open pinned file", ex); }
        }

        private static void SafeClipboard(string text)
        {
            try { if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text); }
            catch (Exception ex) { Log.Error("clipboard set", ex); }
        }

        private sealed class PinRef { public string LinkPath { get; set; } }
        private sealed class GroupRef { public string FolderPath { get; set; } }
        private sealed class TabRef { public string GroupFolder { get; set; } public string TabFolder { get; set; } }
    }

    internal static class PinnedFolderExtensions
    {
        public static bool IsBrokenSafe(this PinnedFolder f) { return f == null || !f.Exists; }
    }
}
