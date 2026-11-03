using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuickPane.UI.Settings
{
    /// <summary>How serious an <see cref="SettingsCards.InfoBar"/> message is. Picks the Fluent
    /// colour pair from <c>Themes/Fluent/Fluent.Settings.xaml</c>'s InfoBar keys (present, and
    /// checked, in all three of Light.xaml, Dark.xaml, and HC.xaml).</summary>
    internal enum InfoBarSeverity { Informational, Success, Warning, Error }

    /// <summary>An expander card returned by <see cref="SettingsCards.Expander"/>: the header looks
    /// like an ordinary <see cref="SettingsCards.Card"/>, and a page adds its child rows below with
    /// <see cref="AddChild"/>.</summary>
    internal sealed class ExpanderCard
    {
        public Border Root { get; }
        private readonly StackPanel _children;
        private readonly ToggleButton _chevron;
        private readonly TextBlock _title;

        internal ExpanderCard(Border root, StackPanel children, ToggleButton chevron, TextBlock title)
        {
            Root = root;
            _children = children;
            _chevron = chevron;
            _title = title;
        }

        /// <summary>The header's title text. Settable so a page can rename the card in place, e.g.
        /// SSH drives after a connection's name is edited: rebuilding the page for that would tear
        /// the tree down while keyboard focus is moving out of the name box, and drop it.</summary>
        public string Title
        {
            get { return _title.Text; }
            set { _title.Text = value; }
        }

        /// <summary>Appends one child row (see <see cref="SettingsCards.ChildRow"/>) below the
        /// divider. Rows appear in the order they are added.</summary>
        public void AddChild(UIElement row) => _children.Children.Add(row);

        public bool IsExpanded
        {
            get { return _chevron.IsChecked == true; }
            set { _chevron.IsChecked = value; }
        }
    }

    /// <summary>Builds every shared Settings control: the settings card, the expander card, child
    /// rows, the toggle switch, the number box, the info bar, and the subtle icon button. Every
    /// colour comes from a Fluent <c>DynamicResource</c> key, never from <c>App.Theme.IsDark</c> or
    /// a QuickPane style key, so a theme change updates these controls live and nothing here can
    /// leak the old Settings look into the pane.
    ///
    /// One instance lives for the life of the <see cref="SettingsWindow"/> and is shared by every
    /// page through <see cref="SettingsPageContext.Cards"/>. It also owns the focus-target registry
    /// that <see cref="FocusTarget"/> and the search box use to land on a specific control
    /// after a page opens; <see cref="BeginPage"/> clears that registry, and the window calls it
    /// right before building whichever page is about to show, since card ids are unique only within
    /// one page at a time.</summary>
    internal sealed class SettingsCards
    {
        /// <summary>SettingsCard's own minimum height (verified from toolkit-SettingsCard.xaml).</summary>
        public const double CardMinHeight = 68;

        public const double CardPadding = 16;

        /// <summary>A single-line card's total height, in the range Windows 11 Settings uses for a
        /// row with no description (see <see cref="CompactCard"/>), versus <see cref="CardMinHeight"/>'s
        /// two-line 68.</summary>
        public const double CompactCardMinHeight = 48;

        /// <summary>A compact card's top/bottom padding -- less than <see cref="CardPadding"/> so a
        /// single line of content plus this padding lands on <see cref="CompactCardMinHeight"/>
        /// instead of <see cref="CardMinHeight"/>.</summary>
        internal const double CompactCardVerticalPadding = 8;

        /// <summary>Below this card width, the control moves under the title instead of beside it.
        /// Exposed so <see cref="Card"/>'s own reflow and a unit test agree on the same number.</summary>
        public const double WrapBelowWidth = 476;

        // Child rows line up under the parent card's title, roughly icon (20) + icon-to-label gap
        // (16), measured from the card's padded content edge; the card's own CardPadding (16) is
        // added on top of this by the shell, for a total of 58 px from the card's true left edge,
        // which is where a child row should line up with the parent card's title.
        private const double ChildRowIndent = 42;

        private readonly Dictionary<string, FrameworkElement> _focusTargets =
            new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);

        /// <summary>Whether a card of this width shows its control under the title (true) or beside
        /// it (false). A pure decision, so it is unit-testable with no UI at all.</summary>
        internal static bool StackControl(double cardWidth) => cardWidth < WrapBelowWidth;

        /// <summary>Clears the focus-target registry. The window calls this immediately before a
        /// page's <see cref="SettingsPage.Build"/>, so stale ids from the previous page can never
        /// answer a search for the new one.</summary>
        public void BeginPage() => _focusTargets.Clear();

        /// <summary>Registers <paramref name="control"/> as the thing a search hit on this card id
        /// should focus. Every builder below that takes a <c>cardId</c> calls this itself; a page
        /// never needs to.</summary>
        public void Register(string cardId, FrameworkElement control)
        {
            if (string.IsNullOrEmpty(cardId) || control == null) return;
            _focusTargets[cardId] = control;
        }

        /// <summary>Moves keyboard focus to the control a search result named. Returns false when
        /// the id is not registered, e.g. a stub page with no real controls yet. Never flips
        /// <c>Focusable</c> on a wrapper panel that was never meant to be a tab stop; when the
        /// registered element is itself a layout wrapper (<see cref="Toggle"/>, <see cref="NumberBox"/>,
        /// <see cref="PathField"/> all stash the control that should actually receive focus in
        /// their returned element's <c>Tag</c>), this unwraps it first.</summary>
        public bool FocusTarget(string cardId)
        {
            FrameworkElement el;
            if (cardId == null || !_focusTargets.TryGetValue(cardId, out el) || el == null) return false;
            var target = Unwrap(el);
            if (target.Focusable)
            {
                target.Focus();
                Keyboard.Focus(target);
                return true;
            }
            return target.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }

        private static FrameworkElement Unwrap(FrameworkElement el) => el.Tag as FrameworkElement ?? el;

        /// <summary>Whether <paramref name="cardId"/> has a registered focus target, with no window
        /// needed -- <see cref="FocusTarget"/> itself calls <c>Focus()</c>/<c>MoveFocus</c>, which
        /// need a live window, so a test that only wants to check registration (e.g. that every
        /// search-index id for a page has a control, #164) uses this instead.</summary>
        internal bool HasFocusTarget(string cardId) => cardId != null && _focusTargets.ContainsKey(cardId);

        // AutomationProperties.Name rejects null ("'' is not a valid value for property 'Name'"), but
        // a builder's name is optional: a toggle passed to Card() or ChildRow() is named there, after
        // it is built. Setting null threw on every build of the Pane page, whose toggles are unnamed.
        internal static void SetAccessibleName(DependencyObject element, string name)
        {
            if (name != null) AutomationProperties.SetName(element, name);
        }

        // ---- settings card ------------------------------------------------

        /// <summary>One settings card: an icon, a title, an optional description, and a control at
        /// the right. Below <see cref="WrapBelowWidth"/> the control moves under the title.
        /// <paramref name="control"/> may be null for a card with no right-hand control (e.g.
        /// About's Licence and Third-party notices cards): placement, focus registration, and the
        /// reflow handler are all skipped in that case, and the title/description simply fill the
        /// row. When non-null, it is registered under <paramref name="cardId"/> for search focus,
        /// and given (or whatever it stashed in <c>Tag</c> -- see <see cref="Unwrap"/>)
        /// <paramref name="accessibleName"/> (or <paramref name="title"/> when none is given).</summary>
        public Border Card(string cardId, string glyph, string title, string description, FrameworkElement control,
            string accessibleName = null)
        {
            return CardCore(cardId, glyph, title, description, control, accessibleName, compact: false);
        }

        /// <summary>A single-line variant of <see cref="Card"/> for a row with nothing to say in a
        /// description, e.g. a Pane sections row, whose On/Off switch already says what a "Shown in
        /// the pane" / "Hidden from the pane" line used to repeat (#154). <see cref="CompactCardMinHeight"/>
        /// replaces <see cref="CardMinHeight"/>, and the icon centres on the title instead of pinning
        /// to the top, since there is no second line to leave room for.</summary>
        public Border CompactCard(string cardId, string glyph, string title, FrameworkElement control,
            string accessibleName = null)
        {
            return CardCore(cardId, glyph, title, null, control, accessibleName, compact: true);
        }

        private Border CardCore(string cardId, string glyph, string title, string description,
            FrameworkElement control, string accessibleName, bool compact)
        {
            TextBlock titleBlock;
            var body = CardBody(glyph, title, description, control, out titleBlock, compact);
            var border = CardShell(body, hoverable: true, compact);
            RegisterAndName(cardId, control, accessibleName, title);

            if (control != null)
            {
                border.SizeChanged += (s, e) =>
                {
                    if (StackControl(e.NewSize.Width)) PlaceControlBelow(control, body, 1);
                    else PlaceControlBeside(control, body, 2);
                };
            }
            return border;
        }

        /// <summary>The icon/title/description/control grid shared by <see cref="Card"/> and an
        /// expander's header, with no border, background, or hover of its own -- the caller wraps
        /// exactly one <see cref="CardShell"/> around the whole card, header and children together,
        /// so an expander never doubles up its border (the bug this split exists to avoid).
        /// <paramref name="control"/> may be null (see <see cref="Card"/>), in which case the third
        /// column is simply left empty. <paramref name="titleBlock"/> hands back the title so an
        /// expander can rename itself in place (<see cref="ExpanderCard.Title"/>).</summary>
        private static Grid CardBody(string glyph, string title, string description, FrameworkElement control,
            out TextBlock titleBlock, bool compact = false)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            double rowMinHeight = compact
                ? CompactCardMinHeight - 2 * CompactCardVerticalPadding
                : CardMinHeight - 2 * CardPadding;
            grid.RowDefinitions.Add(new RowDefinition { MinHeight = rowMinHeight });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var icon = Glyph(glyph, 20);
            // Set here rather than inherited: a card's icon sits under no control that supplies a
            // theme foreground, so it fell back to WPF's black and vanished on a dark card (#149).
            icon.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            icon.Margin = new Thickness(0, 0, 20, 0);
            // Top-aligned so it lines up with a two-line title+description; a compact row has only
            // one line, so the icon centres on it instead (#154).
            icon.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Top;
            Grid.SetColumn(icon, 0);
            Grid.SetRow(icon, 0);
            Grid.SetRowSpan(icon, 2);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleBlock = TitleText(title);
            text.Children.Add(titleBlock);
            if (!string.IsNullOrEmpty(description)) text.Children.Add(DescriptionText(description));
            Grid.SetColumn(text, 1);
            Grid.SetRow(text, 0);

            grid.Children.Add(icon);
            grid.Children.Add(text);

            if (control != null)
            {
                control.VerticalAlignment = VerticalAlignment.Center;
                PlaceControlBeside(control, grid, 2);
                grid.Children.Add(control);
            }
            return grid;
        }

        private void RegisterAndName(string cardId, FrameworkElement control, string accessibleName, string fallbackName)
        {
            if (control == null) return;
            SetAccessibleName(Unwrap(control), accessibleName ?? fallbackName);
            Register(cardId, control);
        }

        /// <summary>A card that opens a sub-page instead of hosting a control: the whole card is a
        /// button, and a 13 px chevron takes the place of a control.</summary>
        public Button ClickableCard(string cardId, string glyph, string title, string description, Action onClick,
            string accessibleName = null)
        {
            var chevron = Glyph("", 13);   // Segoe Fluent Icons ChevronRight
            chevron.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            chevron.VerticalAlignment = VerticalAlignment.Center;
            chevron.Focusable = false;

            var border = Card(cardId, glyph, title, description, chevron, accessibleName);
            border.Focusable = false;   // the wrapping Invokable button below is the real tab stop
            var button = Invokable(border, accessibleName ?? title, onClick);
            Register(cardId, button);   // overrides Card()'s registration of the (non-focusable) chevron with the real tab stop
            return button;
        }

        /// <summary>Wraps arbitrary already-built content in a plain, chrome-free Button: no Fluent
        /// Button skin underneath it, just <paramref name="content"/> with Click, Enter, and Space
        /// all invoking <paramref name="onClick"/>. Used by <see cref="ClickableCard"/> and by
        /// <c>SettingsWindow</c>'s navigation items, which need the same bare-button wrapping.
        /// Sets <see cref="FrameworkElement.HorizontalAlignment"/> to Stretch locally: the Fluent
        /// implicit Button style (Themes/Fluent/Styles/Button.xaml) sets it to Left, and both
        /// callers sit in vertical StackPanels, so without this override the button -- and its
        /// hover/selected background -- shrinks to the width of its content instead of spanning
        /// the pane.</summary>
        internal static Button Invokable(FrameworkElement content, string accessibleName, Action onClick)
        {
            var button = new Button
            {
                Content = content,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Template = BareTemplate(),
            };
            SetAccessibleName(button, accessibleName);
            button.Click += (s, e) => onClick?.Invoke();
            return button;
        }

        // ---- expander card -------------------------------------------------

        /// <summary>An expander card: the header looks like an ordinary <see cref="Card"/>, with an
        /// optional header control (e.g. the Desktop dock toggle) at the right, a chevron, and child
        /// rows below a 1 px divider once expanded. Header and children share exactly one
        /// <see cref="CardShell"/>, so there is only ever one border and one background.</summary>
        public ExpanderCard Expander(string cardId, string glyph, string title, string description,
            FrameworkElement headerControl = null, bool initiallyExpanded = true, string accessibleName = null)
        {
            var chevron = new ToggleButton
            {
                IsChecked = initiallyExpanded,
                Focusable = true,
                Cursor = Cursors.Hand,
                Template = BareTemplate(),
                Content = Glyph("", 13),   // Segoe Fluent Icons ChevronDown
            };
            AutomationProperties.SetName(chevron, (accessibleName ?? title) + " section");

            FrameworkElement headerRight;
            if (headerControl != null)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(headerControl);
                row.Children.Add(new Border { Margin = new Thickness(12, 0, 0, 0), Child = chevron });
                headerRight = row;
            }
            else
            {
                headerRight = chevron;
            }

            TextBlock titleBlock;
            var headerBody = CardBody(glyph, title, description, headerRight, out titleBlock);
            headerBody.Background = Brushes.Transparent;   // hit-testable everywhere, including empty space, so clicking anywhere on the header (not just the text) toggles it
            RegisterAndName(cardId, headerControl ?? (FrameworkElement)chevron, accessibleName, title);

            var children = new StackPanel { Margin = new Thickness(0, 1, 0, 0) };
            var divider = new Border { Height = 1, Margin = new Thickness(ChildRowIndent, 4, 0, 0) };
            divider.SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush");
            children.Children.Add(divider);

            var outer = new StackPanel();
            outer.Children.Add(headerBody);
            outer.Children.Add(children);

            var root = CardShell(outer, hoverable: false);
            root.SizeChanged += (s, e) =>
            {
                if (StackControl(e.NewSize.Width)) PlaceControlBelow(headerRight, headerBody, 1);
                else PlaceControlBeside(headerRight, headerBody, 2);
            };

            Action sync = () => children.Visibility = chevron.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            chevron.Checked += (s, e) => sync();
            chevron.Unchecked += (s, e) => sync();
            sync();

            // Clicking anywhere on the header (not just the chevron) toggles it, matching the
            // Windows 11 Settings app's own expander behavior, but only when there is no separate
            // header control to keep clickable on its own (e.g. the Desktop dock toggle must still
            // toggle the *setting*, not the expander, when pressed). Bubbling events, not
            // tunnelling ones: a direct click on the chevron is handled by its own ButtonBase click
            // processing first (which marks the event handled), so this never double-toggles it.
            if (headerControl == null)
            {
                outer.Children[0].MouseLeftButtonUp += (s, e) => chevron.IsChecked = chevron.IsChecked != true;
                headerBody.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) chevron.IsChecked = chevron.IsChecked != true; };
            }

            return new ExpanderCard(root, children, chevron, titleBlock);
        }

        /// <summary>One row inside an expander, indented to line up with the parent card's title.</summary>
        public DockPanel ChildRow(string label, FrameworkElement control, string accessibleName = null)
        {
            var panel = new DockPanel { Margin = new Thickness(ChildRowIndent, 10, 0, 10), LastChildFill = true };
            control.HorizontalAlignment = HorizontalAlignment.Right;
            control.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(control, Dock.Right);
            panel.Children.Add(control);

            if (!string.IsNullOrEmpty(label))
            {
                var text = new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
                // A local foreground ignores IsEnabled, so a row greyed by LinkEnabled (#150) kept
                // full-strength text; swap to the disabled brush the way a Fluent control does.
                Action paintLabel = () => text.SetResourceReference(TextBlock.ForegroundProperty,
                    text.IsEnabled ? "TextFillColorPrimaryBrush" : "TextFillColorDisabledBrush");
                paintLabel();
                text.IsEnabledChanged += (s, e) => paintLabel();
                panel.Children.Add(text);
            }

            SetAccessibleName(Unwrap(control), accessibleName ?? label);
            return panel;
        }

        /// <summary>A plain, right-aligned child row for a trailing action, e.g. "Add group" at the
        /// end of a Groups expander, rather than a dashed "Add..." box.</summary>
        public DockPanel TrailingChildRow(FrameworkElement control)
        {
            var panel = new DockPanel { Margin = new Thickness(ChildRowIndent, 10, 0, 10) };
            control.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(control, Dock.Right);
            panel.Children.Add(control);
            return panel;
        }

        // ---- toggle switch --------------------------------------------------

        /// <summary>A 40x20 pill toggle switch with "On"/"Off" text to its left, as the Settings app
        /// draws it. Pass the result as a card's or child row's <c>control</c>.</summary>
        public FrameworkElement Toggle(bool isOn, Action<bool> onChanged, string accessibleName = null)
        {
            var thumb = new Ellipse { Width = 12, Height = 12 };
            var thumbHost = new Grid();
            thumbHost.Children.Add(thumb);
            var track = new Border { Width = 40, Height = 20, CornerRadius = new CornerRadius(10), Child = thumbHost };

            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FontSize = 14 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

            var button = new ToggleButton
            {
                IsChecked = isOn,
                Focusable = true,
                Cursor = Cursors.Hand,
                Template = BareTemplate(),
                Content = track,
            };
            SetAccessibleName(button, accessibleName);

            // Painted in code, so the disabled look has to be too: a switch greyed by LinkEnabled
            // (#150) otherwise stayed accent-blue and read as active. The keys are the ones WinUI's
            // ToggleSwitch uses for its Disabled state.
            Action paint = () =>
            {
                bool on = button.IsChecked == true;
                bool enabled = button.IsEnabled;
                label.Text = on ? "On" : "Off";
                label.SetResourceReference(TextBlock.ForegroundProperty, enabled ? "TextFillColorSecondaryBrush" : "TextFillColorDisabledBrush");
                if (on)
                {
                    track.SetResourceReference(Border.BackgroundProperty, enabled ? "AccentFillColorDefaultBrush" : "AccentFillColorDisabledBrush");
                    track.BorderThickness = new Thickness(0);
                    thumb.SetResourceReference(Shape.FillProperty, enabled ? "TextOnAccentFillColorPrimaryBrush" : "TextOnAccentFillColorDisabledBrush");
                    thumb.HorizontalAlignment = HorizontalAlignment.Right;
                    thumb.Margin = new Thickness(0, 0, 3, 0);
                }
                else
                {
                    track.Background = Brushes.Transparent;
                    track.BorderThickness = new Thickness(1);
                    string stroke = enabled ? "ControlStrongStrokeColorDefaultBrush" : "ControlStrongStrokeColorDisabledBrush";
                    track.SetResourceReference(Border.BorderBrushProperty, stroke);
                    thumb.SetResourceReference(Shape.FillProperty, stroke);
                    thumb.HorizontalAlignment = HorizontalAlignment.Left;
                    thumb.Margin = new Thickness(3, 0, 0, 0);
                }
            };
            paint();
            button.Checked += (s, e) => { paint(); onChanged?.Invoke(true); };
            button.Unchecked += (s, e) => { paint(); onChanged?.Invoke(false); };
            button.IsEnabledChanged += (s, e) => paint();

            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Tag = button };
            panel.Children.Add(label);
            panel.Children.Add(button);
            return panel;
        }

        // ---- dependent rows ----------------------------------------------

        /// <summary>Greys each row in <paramref name="rows"/> while <paramref name="parentToggle"/>
        /// (the value <see cref="Toggle"/> returned for the card's own on/off switch) is off, and
        /// un-greys them when it is on, leaving the rows' stored values untouched either way --
        /// Windows Settings greys a dependent control the same way while its parent switch is off
        /// (#150). Safe to call with any <see cref="FrameworkElement"/> that stashes its real
        /// <c>ToggleButton</c> in <c>Tag</c> the way <see cref="Toggle"/> does; does nothing if it
        /// does not.</summary>
        public void LinkEnabled(FrameworkElement parentToggle, params UIElement[] rows)
        {
            var button = parentToggle?.Tag as ToggleButton ?? parentToggle as ToggleButton;
            if (button == null || rows == null) return;
            Action sync = () =>
            {
                bool on = button.IsChecked == true;
                foreach (var row in rows)
                    if (row != null) row.IsEnabled = on;
            };
            button.Checked += (s, e) => sync();
            button.Unchecked += (s, e) => sync();
            sync();
        }

        // ---- number box -------------------------------------------------

        /// <summary>A text box with stacked up/down chevrons, clamped to [<paramref name="min"/>,
        /// <paramref name="max"/>]. <paramref name="increaseName"/>/<paramref name="decreaseName"/>
        /// give the two chevrons their own accessible names, e.g. "Increase recents count". Leaves
        /// the text box's own Background alone (no local brush, dynamic or otherwise): a local value
        /// would outrank the Fluent TextBox style's own hover/focus-state triggers.</summary>
        public FrameworkElement NumberBox(double value, double min, double max, double step, Action<double> onChanged,
            string accessibleName, string increaseName, string decreaseName)
        {
            double current = Clamp(value, min, max);
            var box = new TextBox { Width = 72, Text = FormatNumber(current), TextAlignment = TextAlignment.Left };
            SetAccessibleName(box, accessibleName);

            Action<double> commit = v =>
            {
                current = Clamp(v, min, max);
                box.Text = FormatNumber(current);
                onChanged?.Invoke(current);
            };

            // An integral step (the recents-count, sidebar-width, port, and keepalive boxes) stores
            // (int)v at every call site, so a typed value such as "22.7" must round before it is
            // clamped and shown - otherwise the box displays a fraction that was never saved. Lost
            // focus also fires every time focus merely passes through the box, so skip onChanged
            // (and the SettingsStore.Save() behind it) when the clamped value did not actually change.
            bool integralStep = Math.Floor(step) == step;
            box.LostFocus += (s, e) =>
            {
                double parsed;
                double v = double.TryParse(box.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed) ? parsed : current;
                if (integralStep) v = Math.Round(v);
                double clamped = Clamp(v, min, max);
                if (clamped == current)
                {
                    box.Text = FormatNumber(current);
                    return;
                }
                commit(v);
            };

            var up = new RepeatButton { Content = Glyph("", 8), Height = 10, Width = 18, Padding = new Thickness(0) };
            var down = new RepeatButton { Content = Glyph("", 8), Height = 10, Width = 18, Padding = new Thickness(0) };
            SetAccessibleName(up, increaseName);
            SetAccessibleName(down, decreaseName);
            up.Click += (s, e) => commit(current + step);
            down.Click += (s, e) => commit(current - step);

            var stepper = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-20, 0, 2, 0) };
            stepper.Children.Add(up);
            stepper.Children.Add(down);

            var grid = new Grid { Tag = box };
            grid.Children.Add(box);
            grid.Children.Add(stepper);
            stepper.HorizontalAlignment = HorizontalAlignment.Right;
            return grid;
        }

        // ---- combo box -------------------------------------------------

        /// <summary>A Fluent-themed combo box with one item per (value, label) pair, selecting
        /// whichever value matches <paramref name="current"/> (case-insensitive), or the first item
        /// when nothing matches, and invoking <paramref name="onChanged"/> with the chosen value on
        /// every selection change. Carries no local colour: it relies entirely on the implicit
        /// ComboBox/ComboBoxItem styles merged into the window's Resources by
        /// <c>FluentThemeService</c>.</summary>
        public ComboBox Combo(IEnumerable<KeyValuePair<string, string>> options, string current,
            Action<string> onChanged, double minWidth = 220)
        {
            var box = new ComboBox { MinWidth = minWidth };
            int selected = -1;
            int i = 0;
            foreach (var opt in options)
            {
                box.Items.Add(new ComboBoxItem { Content = opt.Value, Tag = opt.Key });
                if (string.Equals(opt.Key, current, StringComparison.OrdinalIgnoreCase)) selected = i;
                i++;
            }
            box.SelectedIndex = selected >= 0 ? selected : 0;
            box.SelectionChanged += (s, e) =>
            {
                var item = box.SelectedItem as ComboBoxItem;
                if (item != null) onChanged?.Invoke((string)item.Tag);
            };
            return box;
        }

        // ---- info bar -------------------------------------------------

        /// <summary>A status message card: an icon, a title, and a message, tinted by severity.</summary>
        public Border InfoBar(string title, string message, InfoBarSeverity severity)
        {
            string bgKey, iconKey, glyph;
            switch (severity)
            {
                case InfoBarSeverity.Success: bgKey = "InfoBarSuccessSeverityBackgroundBrush"; iconKey = "InfoBarSuccessSeverityIconBackground"; glyph = ""; break;
                case InfoBarSeverity.Warning: bgKey = "InfoBarWarningSeverityBackgroundBrush"; iconKey = "InfoBarWarningSeverityIconBackground"; glyph = ""; break;
                case InfoBarSeverity.Error: bgKey = "InfoBarErrorSeverityBackgroundBrush"; iconKey = "InfoBarErrorSeverityIconBackground"; glyph = ""; break;
                default: bgKey = "InfoBarInformationalSeverityBackgroundBrush"; iconKey = "InfoBarInformationalSeverityIconBackground"; glyph = ""; break;
            }

            var icon = Glyph(glyph, 16);
            icon.SetResourceReference(TextBlock.ForegroundProperty, iconKey);
            icon.Margin = new Thickness(0, 0, 12, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;

            var text = new StackPanel();
            if (!string.IsNullOrEmpty(title)) text.Children.Add(TitleText(title));
            if (!string.IsNullOrEmpty(message)) text.Children.Add(DescriptionText(message));

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(icon);
            row.Children.Add(text);

            var border = new Border
            {
                Padding = new Thickness(CardPadding),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Child = row,
            };
            border.SetResourceReference(Border.BackgroundProperty, bgKey);
            border.SetResourceReference(Border.BorderBrushProperty, "InfoBarBorderBrush");
            AutomationProperties.SetName(border, (title ?? string.Empty) + (string.IsNullOrEmpty(message) ? string.Empty : ". " + message));
            return border;
        }

        // ---- icon button / confirm row ---------------------------------

        /// <summary>A subtle 32x32 icon button (delete, rename, browse, and the like).</summary>
        public Button IconButton(string glyph, string accessibleName, Action onClick)
        {
            var icon = Glyph(glyph, 16);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            var chrome = new Border { CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Child = icon };
            chrome.MouseEnter += (s, e) => chrome.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
            chrome.MouseLeave += (s, e) => chrome.Background = Brushes.Transparent;

            var button = Invokable(chrome, accessibleName, onClick);
            button.Width = 32;
            button.Height = 32;
            return button;
        }

        /// <summary>An inline "Are you sure?" row that replaces a confirm dialog: a message, then
        /// Yes/Cancel buttons. Used for every delete/remove action in the inventory.</summary>
        public FrameworkElement ConfirmRow(string message, string confirmLabel, Action onConfirm, Action onCancel)
        {
            var text = new TextBlock { Text = message, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            var confirm = new Button { Content = confirmLabel ?? "Yes", Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel" };
            confirm.Click += (s, e) => onConfirm?.Invoke();
            cancel.Click += (s, e) => onCancel?.Invoke();

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(ChildRowIndent, 8, 0, 8) };
            row.Children.Add(text);
            row.Children.Add(confirm);
            row.Children.Add(cancel);
            return row;
        }

        /// <summary>A text box with a "Browse" button at the right, for a path setting.</summary>
        public FrameworkElement PathField(string path, Action onBrowse, string accessibleName)
        {
            var box = new TextBox { Text = path, MinWidth = 160, IsReadOnly = onBrowse != null };
            SetAccessibleName(box, accessibleName);
            var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
            browse.Click += (s, e) => onBrowse?.Invoke();
            var row = new DockPanel { LastChildFill = true, Tag = box };
            DockPanel.SetDock(browse, Dock.Right);
            row.Children.Add(browse);
            row.Children.Add(box);
            return row;
        }

        // ---- subheader --------------------------------------------------

        /// <summary>A sentence-case 14 px semibold subheader, for the rare page that groups
        /// cards.</summary>
        public TextBlock Subheader(string text)
        {
            var block = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 24, 0, 8) };
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            return block;
        }

        // ---- shared visuals --------------------------------------------

        /// <summary>Internal, not private: a page with free-form content under its card-style
        /// title (e.g. About's Licence or Third-party notices body) wraps it in exactly this shell
        /// rather than copying the card's padding, corner radius, and brush keys by hand.</summary>
        internal Border CardShell(UIElement content, bool hoverable, bool compact = false)
        {
            var border = new Border
            {
                MinHeight = hoverable ? (compact ? CompactCardMinHeight : CardMinHeight) : 0,
                Padding = compact
                    ? new Thickness(CardPadding, CompactCardVerticalPadding, CardPadding, CompactCardVerticalPadding)
                    : new Thickness(CardPadding),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 4),
                Child = content,
            };
            border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            if (hoverable)
            {
                border.MouseEnter += (s, e) => border.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
                border.MouseLeave += (s, e) => border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
            }
            return border;
        }

        private static TextBlock TitleText(string text)
        {
            var block = new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap };
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            return block;
        }

        private static TextBlock DescriptionText(string text)
        {
            var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            return block;
        }

        private static readonly FontFamily FluentIconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        private static TextBlock Glyph(string glyph, double size)
        {
            return new TextBlock { Text = glyph, FontFamily = FluentIconFont, FontSize = size, Width = size + 4 };
        }

        private static void PlaceControlBeside(FrameworkElement control, Grid grid, int column)
        {
            control.HorizontalAlignment = HorizontalAlignment.Right;
            control.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(control, column);
            Grid.SetRow(control, 0);
        }

        private static void PlaceControlBelow(FrameworkElement control, Grid grid, int row)
        {
            control.HorizontalAlignment = HorizontalAlignment.Left;
            control.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetColumn(control, 1);
            Grid.SetRow(control, row);
        }

        private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        private static string FormatNumber(double v) => v.ToString("0.#", CultureInfo.CurrentCulture);

        private static ControlTemplate _bareTemplate;

        /// <summary>A ControlTemplate that is just its own content, with none of the Fluent implicit
        /// style's chrome. Used so <see cref="Toggle"/>, the expander chevron, icon buttons, and
        /// <see cref="Invokable"/> (which <c>SettingsWindow</c>'s navigation items also use) draw
        /// only the shape built here, not a second button skin underneath it. Shared by both
        /// <see cref="Button"/> and <see cref="ToggleButton"/>, since both derive from
        /// <see cref="ButtonBase"/> and the template carries no type-specific triggers.</summary>
        internal static ControlTemplate BareTemplate()
        {
            if (_bareTemplate == null)
            {
                var factory = new FrameworkElementFactory(typeof(ContentPresenter));
                var template = new ControlTemplate(typeof(ButtonBase)) { VisualTree = factory };
                template.Seal();
                _bareTemplate = template;
            }
            return _bareTemplate;
        }
    }
}
