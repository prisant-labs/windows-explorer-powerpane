using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using QuickPane.Services;
using QuickPane.UI.Settings.Pages;
using NM = QuickPane.Interop.NativeMethods;

namespace QuickPane.UI.Settings
{
    // ---- the page contract ---------------------------------------------
    //
    // Every page in QuickPane/UI/Settings/Pages implements one SettingsPage. Those page files, and
    // their tests, depend on everything below exactly as written here.

    /// <summary>One entry in the navigation pane. A page's constructor takes no arguments, so
    /// <see cref="SettingsSearchIndex"/> can list <see cref="SearchEntries"/> with no
    /// <c>SettingsStore</c>, no <c>GroupStore</c>, and no window at all: the search index exists,
    /// and is testable, before Settings is ever opened. Only <see cref="Build"/> needs the
    /// real stores, and only while the page is on screen.</summary>
    internal abstract class SettingsPage
    {
        /// <summary>Stable key used for the remembered page, the nav selection, and the
        /// page half of every one of this page's <see cref="SettingsSearchEntry"/>. Never shown to
        /// the user: "groups", "pane", "sections", "quickactions", "recent", "keyboard", "ssh",
        /// "backup", "about".</summary>
        public abstract string Id { get; }

        /// <summary>The nav item's label and this page's 28 px title.</summary>
        public abstract string Title { get; }

        /// <summary>A Segoe Fluent Icons glyph (one UTF-16 char, e.g. "\uE716"), shown at 16 px in
        /// the navigation pane.</summary>
        public abstract string Glyph { get; }

        /// <summary>The entries this page contributes to the search index. The default yields
        /// nothing; override it to list every card a search should be able to land on, each with
        /// this page's own <see cref="Id"/>.</summary>
        public virtual IEnumerable<SettingsSearchEntry> SearchEntries { get { yield break; } }

        /// <summary>Builds this page's content fresh. Called every time the page is opened or the
        /// window rebuilds, so it must read <see cref="SettingsPageContext.Settings"/> and
        /// <see cref="SettingsPageContext.Groups"/> for current state rather than caching anything,
        /// and it must do no disk, network, or COM work itself -- that goes through
        /// <c>WorkQueue</c> before this runs. A <c>WorkQueue</c> callback that touches the
        /// tree, registers a card, or calls <see cref="SettingsPageContext.Rebuild"/> or
        /// <see cref="SettingsPageContext.OpenSubPage"/> after returning must check
        /// <see cref="SettingsPageContext.IsCurrent"/> first, since the user may have navigated
        /// away while it was running.</summary>
        public abstract UIElement Build(SettingsPageContext ctx);
    }

    /// <summary>The three things a page asks of the window, through <see cref="SettingsPageContext"/>.
    /// <see cref="SettingsWindow"/> is the only real implementation; a test supplies its own, so
    /// every page can be built with no window at all (#120).</summary>
    internal interface ISettingsShell
    {
        /// <summary>Bumped on every render; see <see cref="SettingsPageContext.IsCurrent"/>.</summary>
        int RenderGeneration { get; }

        void OpenSubPage(string title, Func<SettingsPageContext, UIElement> build);

        void Rebuild();
    }

    /// <summary>Everything a page needs from the shell, handed to <see cref="SettingsPage.Build"/>
    /// and to every <see cref="SettingsSearchEntry"/> focus target. A page never talks to
    /// <see cref="SettingsWindow"/> directly.</summary>
    internal sealed class SettingsPageContext
    {
        /// <summary>Read and write through this exactly as the old <c>SettingsPanel.xaml.cs</c>
        /// did: every change saves immediately, no Apply button.</summary>
        public SettingsStore Settings { get; }

        public GroupStore Groups { get; }

        /// <summary>The shared card builders (see <c>SettingsCards.cs</c>). One instance for the
        /// whole window; call <c>Cards.Register</c> only through a builder, never directly.</summary>
        public SettingsCards Cards { get; }

        private readonly ISettingsShell _shell;
        private readonly int _generation;

        public SettingsPageContext(SettingsStore settings, GroupStore groups, SettingsCards cards, ISettingsShell shell, int generation)
        {
            Settings = settings;
            Groups = groups;
            Cards = cards;
            _shell = shell;
            _generation = generation;
        }

        /// <summary>True while this context's render is still the one on screen. A WorkQueue
        /// callback can return after the user has navigated away (e.g. SshPage's
        /// SshfsService.IsInstalled check): <see cref="RenderCurrentPage"/> bumps the window's
        /// render generation on every render, top-level or sub-page, so a stale callback can check
        /// this before touching the tree, registering a card, or calling <see cref="Rebuild"/> or
        /// <see cref="OpenSubPage"/>, any of which would otherwise act on whichever page happens to
        /// be current instead of the one that queued the work.</summary>
        public bool IsCurrent { get { return _shell.RenderGeneration == _generation; } }

        /// <summary>Opens a sub-page under the one currently showing, with a breadcrumb reading
        /// "&lt;this page's title&gt; &gt; title". <paramref name="build"/> runs again on every
        /// rebuild while the sub-page stays open, exactly like a top-level page's own
        /// <see cref="SettingsPage.Build"/>. The breadcrumb's parent segment is clickable and
        /// returns to the top-level page.
        ///
        /// <see cref="RenderCurrentPage"/> has no other way to refresh a sub-page in place: calling
        /// this again with the same <paramref name="title"/> and a builder that reads current state
        /// (e.g. after Rename, Move up/down, Delete group, or Add group on a profile sub-page) is
        /// the supported way to re-render it.</summary>
        public void OpenSubPage(string title, Func<SettingsPageContext, UIElement> build)
        {
            _shell.OpenSubPage(title, build);
        }

        /// <summary>Rebuilds the window on the same top-level page, e.g. after Restore
        /// replaces every setting on disk. Drops any open sub-page: a sub-page usually names one
        /// profile or connection, and a restore may have removed exactly that one.</summary>
        public void Rebuild()
        {
            _shell.Rebuild();
        }
    }

    // ---- the window itself ----------------------------------------------

    /// <summary>The native Windows 11 Settings window: a navigation pane with search and its
    /// pages, About last, and a content area that hosts whichever page is selected. Replaces
    /// <c>SettingsPanel.xaml.cs</c> and its tab strip (#90).
    ///
    /// Built entirely in code, like the window it replaces, so a reviewer can compare a handler
    /// here against the matching one in <c>SettingsPanel.xaml.cs</c> at commit 068301b line by
    /// line. Every colour comes from a Fluent <c>DynamicResource</c> key, applied through
    /// <see cref="FluentThemeService"/> into this window's own <see cref="Window.Resources"/> and
    /// nowhere else, never into <c>Application.Current.Resources</c>; nothing here reads
    /// <c>ThemeService.IsDark</c> to pick a colour.</summary>
    public sealed class SettingsWindow : Window, ISettingsShell
    {
        private const double NavWidth = 272;        // 260-280 px is acceptable
        private const double TitleBarHeight = 48;
        // MinWidth = nav (272) + content's left/right margin (32+32) + the narrowest a settings
        // card is allowed to get before its control wraps under the title (476), so the window can
        // never be resized into a state where a card's control overlaps its title.
        private const double MinContentWidth = 32 + SettingsCards.WrapBelowWidth + 32;

        private readonly SettingsStore _settings;
        private readonly GroupStore _groups;
        private readonly ThemeService _theme;
        private readonly SettingsCards _cards = new SettingsCards();
        private readonly List<SettingsPage> _pages;
        private readonly List<SettingsSearchEntry> _searchIndex;

        private string _currentPageId;
        private Tuple<string, Func<SettingsPageContext, UIElement>> _subPage;

        /// <summary>Bumped at the top of every <see cref="RenderCurrentPage"/>, top-level or
        /// sub-page, so a <see cref="SettingsPageContext"/> built for one render can tell, later,
        /// whether it is still the one on screen (<see cref="SettingsPageContext.IsCurrent"/>).</summary>
        private int _renderGeneration;

        int ISettingsShell.RenderGeneration { get { return _renderGeneration; } }

        private readonly List<Button> _navOrder = new List<Button>();
        private readonly Dictionary<string, Border> _navItemsByPageId = new Dictionary<string, Border>(StringComparer.Ordinal);
        private readonly Dictionary<string, Border> _navPills = new Dictionary<string, Border>(StringComparer.Ordinal);

        private TextBox _searchBox;
        private ListBox _searchResults;
        private ContentControl _headerHost;
        private ScrollViewer _bodyScroller;

        // #97: instrumentation only. Measures how long Settings takes to open (this
        // constructor's start to the window's first real paint) and how long each page switch
        // takes, so the sandbox can compare both against the 500 ms open-time budget without
        // guessing. Never read by anything other than the two log lines below -- no behavior
        // depends on the value, and this does not itself move Settings onto its own UI thread;
        // that decision waits for the measurement these two lines produce.
        private readonly Stopwatch _openStopwatch = Stopwatch.StartNew();
        private bool _openTimeLogged;

        public SettingsWindow(SettingsStore settings, GroupStore groups, ThemeService theme, System.Drawing.Icon appIcon)
        {
            _settings = settings;
            _groups = groups;
            _theme = theme;
            _pages = BuildPages();
            _searchIndex = SettingsSearchIndex.Build(_pages);
            _currentPageId = _pages[0].Id;   // "Pane" opens first: the pane's look and the dock are what people change most

            Title = "QuickPane Settings";
            Width = 960;
            Height = 640;
            MinWidth = NavWidth + MinContentWidth;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            Background = Brushes.Transparent;   // lets the Mica/flat backdrop (ApplyBackdrop) show through

            if (appIcon != null)
            {
                try { Icon = Imaging.CreateBitmapSourceFromHIcon(appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
                catch (Exception ex) { Log.Error("SettingsWindow: title icon failed", ex); }
            }

            // GlassFrameThickness(-1) extends the (invisible, since Background is transparent)
            // glass frame over the whole window, which is what lets DWM paint Mica -- and the
            // native Win11 caption buttons UseAeroCaptionButtons asks for -- behind the transparent
            // client area. GlassFrameThickness(0) would remove the DWM frame outright and leave
            // UseAeroCaptionButtons with nothing to draw: no minimize, maximize, or close.
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = TitleBarHeight,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = true,
            });

            // Merges Themes/Fluent into THIS window's own Resources only, never into
            // Application.Current.Resources. Must run before
            // Content is set so the implicit styles are already in scope when pages enter the tree.
            FluentThemeService.Attach(this, theme);

            Content = BuildLayout();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            SourceInitialized += (s, e) => ApplyBackdrop();
            StateChanged += (s, e) => UpdateMaximizedMargin();

            // ContentRendered is the window's first real paint, so it is the "open" half of the
            // 500 ms open-time budget. It can fire more than once (e.g. after a later layout pass), so
            // _openTimeLogged keeps this to the first one, which is the number that matters.
            ContentRendered += (s, e) =>
            {
                if (_openTimeLogged) return;
                _openTimeLogged = true;
                _openStopwatch.Stop();
                Log.Info(FormatOpenTiming(_openStopwatch.ElapsedMilliseconds));
            };

            // ApplyBackdrop reads theme.IsHighContrast, so it must be re-run after a live contrast
            // switch too, not just once at SourceInitialized. See the remark on ApplyBackdrop.
            if (_theme != null)
            {
                _onThemeChangedForBackdrop = () => ApplyBackdrop();
                _theme.ThemeChanged += _onThemeChangedForBackdrop;
                Closed += (s, e) => _theme.ThemeChanged -= _onThemeChangedForBackdrop;
            }

            RenderCurrentPage();
        }

        private Action _onThemeChangedForBackdrop;

        /// <summary>internal, not private, so a unit test can check the real page list (eight
        /// unique ids, About last) with no window and no stores -- see SettingsSearchIndexTests.</summary>
        internal static List<SettingsPage> BuildPages()
        {
            // "About" is last; the nav pane's visual order (BuildNavPane) and
            // RealPages_HaveNineUniqueIds_WithAboutLast both rely on that order, not on a flag
            // checked per page. The first page is the one Settings opens on. Pane, then Pane
            // sections, then Groups & profiles: the maintainer chose this order over a dashboard
            // landing page, which would be built on every open and count against #97's budget.
            return new List<SettingsPage>
            {
                new PanePage(),
                new SectionsPage(),
                new GroupsPage(),
                new QuickActionsPage(),
                new RecentAppsPage(),
                new KeyboardPage(),
                new SshPage(),
                new BackupPage(),
                new AboutPage(),
            };
        }

        // ---- window chrome / Mica -----------------------------------------

        /// <summary>Mica on Windows 11 through DWMWA_SYSTEMBACKDROP_TYPE, or the flat
        /// SolidBackgroundFillColorBase fallback on Windows 10 or any build where the DWM call
        /// fails. The fallback is a DynamicResource, not a literal colour, so it still tracks a
        /// theme change live even though Mica itself does not need that: DWM
        /// repaints Mica for light/dark on its own once ThemeService's dark-title-bar call (#26,
        /// wired through <c>App.ShowSettingsWindow</c>'s existing <c>Theme.AttachWindow</c>) flips.
        ///
        /// On the failure path this also removes the WindowChrome: an opaque fallback colour over
        /// the extended glass frame GlassFrameThickness(-1) asks for would hide the Win11 caption
        /// buttons along with Mica, so Windows 10 instead falls all the way back to a standard
        /// window frame -- which already shows #26's dark title bar natively -- and the custom
        /// title row collapses, since the OS frame now draws the icon and caption itself.
        ///
        /// The sandbox may not render Mica at all; only the user's
        /// own Windows 11 desktop confirms this path actually paints Mica, not just that it did not
        /// error.
        ///
        /// Under high contrast this takes the same fallback path as a failed DWM call, rather than
        /// requesting Mica at all: the nav pane and title row have no background of their own
        /// (every colour here is a DynamicResource, and none of them paint a solid
        /// window-level background), so without this they would sit on whatever DWM paints under
        /// Mica, which is not guaranteed to be SystemColorWindowColor, the colour HC.xaml's
        /// window-text brushes expect behind them. Re-run from the <see cref="ThemeService.ThemeChanged"/>
        /// handler the constructor wires up, so a live switch into or out of high contrast while
        /// Settings is open takes effect without closing it -- one-way for the WindowChrome removal
        /// itself, since <see cref="WindowChrome.SetWindowChrome"/> only removes chrome here, never
        /// restores it; leaving high contrast takes full effect on the next time Settings opens.</summary>
        private void ApplyBackdrop()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            bool highContrast = _theme != null && _theme.IsHighContrast;
            int hr = -1;
            if (!highContrast)
            {
                int backdrop = NM.DWMSBT_MAINWINDOW;
                hr = NM.DwmSetWindowAttribute(hwnd, NM.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            }

            if (hr == 0)
            {
                Background = Brushes.Transparent;
            }
            else
            {
                WindowChrome.SetWindowChrome(this, null);
                if (_titleRowDef != null) _titleRowDef.Height = new GridLength(0);
                SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
            }
        }

        /// <summary>Compensates for the resize-frame overhang WindowChrome leaves when maximized:
        /// without this, a maximized window extends past the monitor
        /// edge by its own resize-border thickness, clipping the title row and the content's left
        /// and top edges. A pure decision, so it is unit-testable with no window at all.</summary>
        internal static Thickness RootMarginFor(WindowState state, Thickness resizeBorder)
        {
            return state == WindowState.Maximized ? resizeBorder : new Thickness(0);
        }

        private void UpdateMaximizedMargin()
        {
            if (_rootGrid != null) _rootGrid.Margin = RootMarginFor(WindowState, SystemParameters.WindowResizeBorderThickness);
        }

        // ---- layout --------------------------------------------------------

        private RowDefinition _titleRowDef;
        private Grid _rootGrid;

        private FrameworkElement BuildLayout()
        {
            var root = new Grid();
            _rootGrid = root;
            _titleRowDef = new RowDefinition { Height = new GridLength(TitleBarHeight) };
            root.RowDefinitions.Add(_titleRowDef);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleRow = BuildTitleRow();
            Grid.SetRow(titleRow, 0);
            root.Children.Add(titleRow);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NavWidth) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 1);

            var nav = BuildNavPane();
            Grid.SetColumn(nav, 0);
            body.Children.Add(nav);

            var content = BuildContentArea();
            Grid.SetColumn(content, 1);
            body.Children.Add(content);

            root.Children.Add(body);
            return root;
        }

        /// <summary>The 48 px caption row: a 16 px app icon and "QuickPane Settings" at 12 px, on
        /// the same transparent backdrop as everything below it, with WindowChrome treating
        /// the row as the drag/caption-button area.</summary>
        private FrameworkElement BuildTitleRow()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (Icon != null)
            {
                panel.Children.Add(new Image { Source = Icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 10, 0) });
            }

            var caption = new TextBlock { Text = "QuickPane Settings", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            panel.Children.Add(caption);

            return panel;
        }

        // ---- navigation pane -------------------------------------------------

        /// <summary>Transparent over the backdrop: NavigationViewExpandedPaneBackground is
        /// SolidBackgroundFillColorTransparent, so it is left with no Background at all here.</summary>
        private FrameworkElement BuildNavPane()
        {
            var dock = new DockPanel();

            _searchBox = new TextBox { Margin = new Thickness(12, 12, 12, 8), Padding = new Thickness(8, 4, 8, 4) };
            AutomationProperties.SetName(_searchBox, "Search settings");
            KeyboardNavigation.SetTabIndex(_searchBox, 0);
            _searchBox.TextChanged += (s, e) => OnSearchTextChanged();

            // WPF's TextBox has no placeholder, so a hint sits over the box while it is empty, as the
            // Windows Settings search box shows one. It ignores the mouse, so clicks reach the box.
            var hint = new TextBlock
            {
                Text = "Search settings",
                FontSize = 14,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(23, 12, 23, 8),
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
            _searchBox.TextChanged += (s, e) => hint.Visibility = _searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            var searchHost = new Grid();
            searchHost.Children.Add(_searchBox);
            searchHost.Children.Add(hint);
            DockPanel.SetDock(searchHost, Dock.Top);
            dock.Children.Add(searchHost);

            _searchResults = new ListBox { Margin = new Thickness(12, 0, 12, 8), Visibility = Visibility.Collapsed, MaxHeight = 280, DisplayMemberPath = "Title" };
            AutomationProperties.SetName(_searchResults, "Search results");
            KeyboardNavigation.SetTabIndex(_searchResults, 1);
            _searchResults.MouseLeftButtonUp += (s, e) => CommitSearchSelection();
            _searchResults.KeyDown += (s, e) => { if (e.Key == Key.Enter) { CommitSearchSelection(); e.Handled = true; } };
            DockPanel.SetDock(_searchResults, Dock.Top);
            dock.Children.Add(_searchResults);

            // Build every nav button, About included, in _pages order (About last) so _navOrder
            // -- what OnNavItemArrowKey's Up/Down walks -- and mainList's visual order both read
            // top to bottom with no special case for About.
            var navButtons = new List<Button>(_pages.Count);
            for (int i = 0; i < _pages.Count; i++) navButtons.Add(BuildNavItem(_pages[i]));
            for (int i = 0; i < navButtons.Count; i++) KeyboardNavigation.SetTabIndex(navButtons[i], 2 + i);

            var mainList = new StackPanel();
            for (int i = 0; i < navButtons.Count; i++) mainList.Children.Add(navButtons[i]);
            var mainScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = mainList,
                Focusable = false,   // a bare ScrollViewer is otherwise its own tab stop between the nav buttons and the page
            };
            dock.Children.Add(mainScroller);   // added last with no Dock set: fills the remaining height below the search box

            return dock;
        }

        /// <summary>One nav item: 36 px tall, 4 px radius, a
        /// neutral selected background (never an accent tint), regular-weight text, and a 3x16 px
        /// accent pill reserved at a fixed width so the icon never shifts when the pill appears.
        /// Wrapped in <see cref="SettingsCards.Invokable"/> so the item is a real button for UI
        /// Automation (a bare <see cref="Border"/> has no automation peer) and gets Enter/Space
        /// activation for free; only the Up/Down arrow-key crossing is handled here.</summary>
        private Button BuildNavItem(SettingsPage page)
        {
            var pill = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Hidden };
            pill.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            _navPills[page.Id] = pill;

            var icon = new TextBlock
            {
                Text = page.Glyph,
                FontFamily = NavIconFont,
                FontSize = 16,
                Width = 16,
                TextAlignment = TextAlignment.Center,
                // Without this the icon (no VerticalAlignment of its own) stretched to the row's
                // full height and drew at the top of that box, while the 14 px label beside it
                // centred in a box sized to its own text -- so the label sat visibly lower than
                // the icon (#154). Matches QuickActionsSection.BuildRow's icon+label row.
                VerticalAlignment = VerticalAlignment.Center,
            };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            var label = new TextBlock
            {
                Text = page.Title,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,   // labels stay on one line
                Margin = new Thickness(16, 0, 0, 0),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0) };
            row.Children.Add(icon);
            row.Children.Add(label);

            // Left, not the default Stretch: a fixed-width element set to stretch is centred, which
            // drew the pill beside the label instead of at the item's left edge.
            var pillHost = new Grid { Width = 11, HorizontalAlignment = HorizontalAlignment.Left };
            pillHost.Children.Add(pill);

            var content = new Grid();
            content.Children.Add(pillHost);
            content.Children.Add(row);

            var bg = new Border
            {
                Height = 36,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(4, 2, 4, 0),
                Padding = new Thickness(1, 0, 12, 0),
                Background = Brushes.Transparent,
                Child = content,
            };

            bg.MouseEnter += (s, e) => { if (!string.Equals(_currentPageId, page.Id, StringComparison.Ordinal)) bg.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush"); };
            bg.MouseLeave += (s, e) => { if (!string.Equals(_currentPageId, page.Id, StringComparison.Ordinal)) bg.Background = Brushes.Transparent; };

            var button = SettingsCards.Invokable(bg, page.Title, () => Navigate(page.Id));
            button.Cursor = Cursors.Hand;
            button.KeyDown += (s, e) => OnNavItemArrowKey(button, e);
            // The implicit Fluent Button style's own FocusVisualStyle rings the whole (stretched,
            // full-width) button; NavItemFocusVisualStyle rings bg's own Margin(4,2,4,0)/
            // CornerRadius(4) instead, so keyboard focus hugs the rounded highlight rather than
            // drawing thin lines above and below it (#150).
            button.SetResourceReference(FrameworkElement.FocusVisualStyleProperty, "NavItemFocusVisualStyle");

            _navOrder.Add(button);
            _navItemsByPageId[page.Id] = bg;
            return button;
        }

        private static readonly FontFamily NavIconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        /// <summary>Up/Down moves to the neighbour in nav order. Enter/Space is the button's own
        /// default activation, so only the arrow keys need handling here.</summary>
        private void OnNavItemArrowKey(Button item, KeyEventArgs e)
        {
            if (e.Key != Key.Up && e.Key != Key.Down) return;

            int index = _navOrder.IndexOf(item);
            if (index < 0) return;
            int next = e.Key == Key.Down ? index + 1 : index - 1;
            if (next < 0 || next >= _navOrder.Count) return;

            var target = _navOrder[next];
            target.Focus();
            Keyboard.Focus(target);
            // Focus() alone does not scroll: mainScroller only grew a scrollbar once About moved
            // inside it (#154), and arrowing Down onto About (or any item a search had scrolled
            // past) must still bring it on screen.
            target.BringIntoView();
            e.Handled = true;
        }

        private void SelectNav(string pageId)
        {
            foreach (var kv in _navItemsByPageId)
            {
                bool selected = string.Equals(kv.Key, pageId, StringComparison.Ordinal);
                if (selected) kv.Value.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
                else kv.Value.Background = Brushes.Transparent;
                _navPills[kv.Key].Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            }
        }

        // ---- search ---------------------------------------------------------

        private void OnSearchTextChanged()
        {
            var query = _searchBox.Text;
            var results = SettingsSearchIndex.Search(_searchIndex, query);
            if (results.Count == 0)
            {
                _searchResults.Visibility = Visibility.Collapsed;
                _searchResults.ItemsSource = null;
            }
            else
            {
                _searchResults.ItemsSource = results;
                _searchResults.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Opens the chosen result's page and moves keyboard focus to its card's
        /// control. The target is not in the visual tree synchronously right after <see cref="Navigate"/>
        /// rebuilds the page, so the focus call runs once the layout pass that follows has finished.</summary>
        private void CommitSearchSelection()
        {
            var entry = _searchResults.SelectedItem as SettingsSearchEntry;
            if (entry == null) return;

            Navigate(entry.PageId);
            _searchBox.Text = string.Empty;
            _searchResults.Visibility = Visibility.Collapsed;

            var cardId = entry.CardId;
            if (cardId != null)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => _cards.FocusTarget(cardId)));
            }
        }

        // ---- content area / page host ---------------------------------------

        /// <summary>A layer over the backdrop with a rounded top-left corner and a 1 px border on
        /// its top and left edges only (NavigationViewContentGridCornerRadius is 8,0,0,0).</summary>
        private FrameworkElement BuildContentArea()
        {
            var border = new Border { CornerRadius = new CornerRadius(8, 0, 0, 0), BorderThickness = new Thickness(1, 1, 0, 0) };
            border.SetResourceReference(Border.BackgroundProperty, "LayerFillColorDefaultBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

            var dock = new DockPanel();
            _headerHost = new ContentControl { Focusable = false };
            DockPanel.SetDock(_headerHost, Dock.Top);
            dock.Children.Add(_headerHost);

            _bodyScroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(32, 0, 32, 24), Focusable = false };
            dock.Children.Add(_bodyScroller);

            border.Child = dock;
            return border;
        }

        private FrameworkElement BuildHeader(string pageTitle, string subPageTitle)
        {
            var panel = new StackPanel { Margin = new Thickness(32, 24, 32, 8) };

            if (subPageTitle == null)
            {
                var t = new TextBlock { Text = pageTitle, FontSize = 28, FontWeight = FontWeights.SemiBold };
                t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
                panel.Children.Add(t);
                return panel;
            }

            // Breadcrumb: "<page title> > <sub title>", parent clickable, e.g. "Groups & profiles > Work".
            var row = new StackPanel { Orientation = Orientation.Horizontal };

            // A plain Focusable TextBlock with hand-rolled Enter/Space gives UI Automation a Text
            // control, not a Button, and a bare TextBlock carries no FocusVisualStyle of its own
            // (the Fluent ring comes from the implicit Button style), so it is wrapped in the same
            // SettingsCards.Invokable the nav items use: a real Button peer, Click/Enter/Space
            // activation and the focus ring all for free, instead of reimplementing them here.
            var parentText = new TextBlock { Text = pageTitle, FontSize = 28, FontWeight = FontWeights.SemiBold };
            parentText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            Action goBack = () => { _subPage = null; RenderCurrentPage(); };
            var parent = SettingsCards.Invokable(parentText, "Back to " + pageTitle, goBack);
            parent.Cursor = Cursors.Hand;

            var sep = new TextBlock { Text = " › ", FontSize = 28, FontWeight = FontWeights.SemiBold };
            sep.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

            var sub = new TextBlock { Text = subPageTitle, FontSize = 28, FontWeight = FontWeights.SemiBold };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            row.Children.Add(parent);
            row.Children.Add(sep);
            row.Children.Add(sub);
            panel.Children.Add(row);
            return panel;
        }

        private void Navigate(string pageId)
        {
            if (!_pages.Any(p => string.Equals(p.Id, pageId, StringComparison.Ordinal))) return;
            _currentPageId = pageId;
            _subPage = null;
            RenderCurrentPage();
        }

        /// <summary>Opens a sub-page under the page currently on screen. Called only through
        /// <see cref="SettingsPageContext.OpenSubPage"/>.</summary>
        void ISettingsShell.OpenSubPage(string title, Func<SettingsPageContext, UIElement> build)
        {
            _subPage = Tuple.Create(title, build);
            RenderCurrentPage();
        }

        /// <summary>Rebuilds on the same top-level page. Judgment call: this remembers the
        /// page id, not an open sub-page -- a sub-page usually names one profile or connection, and
        /// the rebuild that triggers this (e.g. a completed Restore) is exactly the case where that
        /// one thing may no longer exist. Called only through <see cref="SettingsPageContext.Rebuild"/>.</summary>
        void ISettingsShell.Rebuild()
        {
            _subPage = null;
            RenderCurrentPage();
        }

        /// <summary>Builds and shows whichever page (or sub-page) is current. Guards the page's own
        /// <see cref="SettingsPage.Build"/> or sub-page builder: one of the eight page
        /// implementations throwing must not leave Settings unable to open at all, nor leave the
        /// previous page on screen with no explanation while navigating.</summary>
        private void RenderCurrentPage()
        {
            int generation = ++_renderGeneration;
            var switchStopwatch = Stopwatch.StartNew();
            var page = _pages.First(p => string.Equals(p.Id, _currentPageId, StringComparison.Ordinal));
            _cards.BeginPage();
            var ctx = new SettingsPageContext(_settings, _groups, _cards, this, _renderGeneration);

            UIElement body;
            try
            {
                body = _subPage != null ? _subPage.Item2(ctx) : page.Build(ctx);
            }
            catch (Exception ex)
            {
                Log.Error("SettingsWindow: page build failed: " + page.Id, ex);
                body = _cards.InfoBar("This page could not be shown", ex.Message, InfoBarSeverity.Error);
            }

            _headerHost.Content = BuildHeader(page.Title, _subPage?.Item1);
            _bodyScroller.Content = body;
            SelectNav(page.Id);

            // #97: generation 1 is the window's very first render, already covered by
            // the ContentRendered open-time log above, so only later navigations (page switches and
            // sub-page opens) are timed here. The Loaded-priority dispatch waits for the new content
            // to actually lay out and render before the clock stops, not just for this method to
            // return; IsCurrent guards against a slower-to-render page that the user has since
            // navigated away from logging a misleadingly long "switch" for the page now on screen.
            if (generation > 1)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (_renderGeneration != generation) return;
                    switchStopwatch.Stop();
                    Log.Info(FormatPageSwitchTiming(page.Id, switchStopwatch.ElapsedMilliseconds));
                }));
            }
        }

        /// <summary>The "Settings opened in N ms" log line, against the 500 ms open-time budget.
        /// A pure decision, so it is unit-testable without a window.</summary>
        internal static string FormatOpenTiming(long elapsedMs)
        {
            return "SettingsWindow: opened in " + elapsedMs + " ms";
        }

        /// <summary>The "switched to page '...' in N ms" log line. A pure decision,
        /// so it is unit-testable without a window.</summary>
        internal static string FormatPageSwitchTiming(string pageId, long elapsedMs)
        {
            return "SettingsWindow: switched to page '" + pageId + "' in " + elapsedMs + " ms";
        }
    }
}
