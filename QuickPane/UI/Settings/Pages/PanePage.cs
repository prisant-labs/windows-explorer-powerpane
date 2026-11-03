using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using QuickPane.Explorer;
using QuickPane.Models;
using QuickPane.Services;

namespace QuickPane.UI.Settings.Pages
{
    // Carries over every control from SettingsPanel.xaml.cs's "Pane" tab (left column), at commit
    // 068301b.

    /// <summary>Pane mode and File dialogs cards (combo boxes), a Pane color card (swatches, #134),
    /// a Desktop dock expander (toggle in the header, auto-hide, all-desktops, and which display it
    /// sits on as child rows, #146), and a Profile tabs expander (toggle in the header, auto-hide as
    /// a child row). Every setting here reads and writes the same <see cref="AppSettings"/> field,
    /// with the same values and the same immediate <c>SettingsStore.Save()</c>, as the matching
    /// handler in <c>SettingsPanel.xaml.cs</c> at commit 068301b (<c>PaneModeBlock</c>,
    /// <c>DialogModeBlock</c>, <c>DockBlock</c>, <c>ProfileTabsBlock</c>). Each expander's dependent
    /// child rows grey out while its own header toggle is off, via <c>SettingsCards.LinkEnabled</c>,
    /// matching how Windows Settings treats a dependent control (#150).</summary>
    internal sealed class PanePage : SettingsPage
    {
        public override string Id => "pane";
        public override string Title => "Pane";
        public override string Glyph => "\uE90C";   // Segoe Fluent Icons Dock panel; escaped, since an edit once dropped the literal character

        public override IEnumerable<SettingsSearchEntry> SearchEntries
        {
            get
            {
                yield return new SettingsSearchEntry(Id, "paneMode", "Pane mode", "Where the pane attaches to Explorer windows.", "inside", "beside");
                yield return new SettingsSearchEntry(Id, "fileDialogs", "File dialogs", "Where the pane attaches to Open and Save dialogs.", "dialog", "open", "save");
                yield return new SettingsSearchEntry(Id, "paneColor", "Pane color", "A background tint for the pane.", "color", "colour", "swatch", "theme");
                yield return new SettingsSearchEntry(Id, "desktopDock", "Desktop dock", "A screen-edge dock independent of the window pane.", "dock", "auto-hide", "virtual desktop");
                yield return new SettingsSearchEntry(Id, "desktopDockDisplay", "Display", "Which display the desktop dock sits on.", "monitor", "screen", "multi-monitor", "multiple monitors");
                yield return new SettingsSearchEntry(Id, "profileTabs", "Profile tabs", "The row of profile tabs under the title.", "profile tabs", "tab row");
            }
        }

        public override UIElement Build(SettingsPageContext ctx)
        {
            var s = ctx.Settings.Current;
            var cards = ctx.Cards;
            var panel = new StackPanel();

            // ---- Pane mode (old: PaneModeBlock, lines 379-388) ----
            string mode = (s.Mode ?? "inside").ToLowerInvariant();
            var modeCombo = cards.Combo(
                new[]
                {
                    new KeyValuePair<string, string>("inside", "Inside Explorer window"),
                    new KeyValuePair<string, string>("beside", "Beside Explorer window"),
                    new KeyValuePair<string, string>("off", "Off (desktop dock only)"),
                },
                mode,
                v => { s.Mode = v; ctx.Settings.Save(); });
            panel.Children.Add(cards.Card("paneMode", "\uE7C4", "Pane mode",
                "Where the pane attaches to an Explorer window", modeCombo, "Pane mode"));

            // ---- File dialogs (old: DialogModeBlock, lines 393-412) ----
            string dialogMode = (s.DialogMode ?? "same").ToLowerInvariant();
            var dialogCombo = cards.Combo(
                new[]
                {
                    new KeyValuePair<string, string>("same", "Same as Explorer windows"),
                    new KeyValuePair<string, string>("beside", "Beside the dialog"),
                    new KeyValuePair<string, string>("off", "Off"),
                },
                dialogMode,
                v => { s.DialogMode = v; ctx.Settings.Save(); });
            panel.Children.Add(cards.Card("fileDialogs", "\uE8B7", "File dialogs",
                "Off also hides App Folders and \"Always start here\", which only appear in dialog panes.",
                dialogCombo, "File dialogs"));

            // ---- Pane color (#134) ----
            panel.Children.Add(BuildPaneColorCard(ctx, s));

            // ---- Desktop dock (old: DockBlock, lines 414-424) ----
            var dockToggle = cards.Toggle(s.DesktopDock, on => { s.DesktopDock = on; ctx.Settings.Save(); });
            var dockExpander = cards.Expander("desktopDock", "\uE90C", "Desktop dock",
                "Pin a small dock to a screen edge", dockToggle, accessibleName: "Show desktop dock");
            var autoHideRow = cards.ChildRow("Auto-hide (slide out on hover)",
                cards.Toggle(s.DesktopDockAutoHide, on => { s.DesktopDockAutoHide = on; ctx.Settings.Save(); }),
                "Auto-hide the desktop dock");
            var allDesktopsRow = cards.ChildRow("Show on all virtual desktops",
                cards.Toggle(s.DesktopDockAllDesktops, on => { s.DesktopDockAllDesktops = on; ctx.Settings.Save(); }),
                "Show the desktop dock on all virtual desktops");
            // Always shown, even with one monitor connected: it then just lists that one monitor
            // plus "Primary display", which keeps the row's presence (and search entry) the same on
            // every machine rather than making the Pane page's layout depend on how many displays
            // happen to be connected right now (#146).
            var displayRow = cards.ChildRow("Display", BuildDisplayCombo(ctx, s), "Which display the desktop dock sits on");
            dockExpander.AddChild(autoHideRow);
            dockExpander.AddChild(allDesktopsRow);
            dockExpander.AddChild(displayRow);
            // Auto-hide, all-desktops, and which display all do nothing while the dock itself is off
            // (#150); grey them out the way the header toggle's own on/off state already implies.
            cards.LinkEnabled(dockToggle, autoHideRow, allDesktopsRow, displayRow);
            panel.Children.Add(dockExpander.Root);

            // ---- Profile tabs (old: ProfileTabsBlock, lines 470-487) ----
            var tabsToggle = cards.Toggle(s.ShowProfileTabs, on => { s.ShowProfileTabs = on; ctx.Settings.Save(); });
            var tabsExpander = cards.Expander("profileTabs", "\uE8AB", "Profile tabs",
                "The row appears once there are two or more profiles.", tabsToggle,
                accessibleName: "Show profiles tab row");
            var tabsAutoHideRow = cards.ChildRow("Auto-hide (slide out on hover)",
                cards.Toggle(s.ProfileTabsAutoHide, on => { s.ProfileTabsAutoHide = on; ctx.Settings.Save(); }),
                "Auto-hide the profiles tab row");
            tabsExpander.AddChild(tabsAutoHideRow);
            // Auto-hide does nothing while the tab row itself is off (#150).
            cards.LinkEnabled(tabsToggle, tabsAutoHideRow);
            panel.Children.Add(tabsExpander.Root);

            return panel;
        }

        // ---- Display picker (#146) ------------------------------------------

        /// <summary>The Desktop dock's Display row: "Primary display" (tracks whichever monitor
        /// Windows currently calls primary, live) plus one entry per connected monitor, built by the
        /// same pure <see cref="MonitorPicker.BuildDisplayOptions"/> AppBarHost's own tests exercise
        /// with an invented list. Stores an empty value back as null, matching
        /// <see cref="MonitorPicker.PrimaryDisplayId"/> and <see cref="AppSettings.DesktopDockDisplay"/>'s
        /// own "null means primary" convention.</summary>
        private static ComboBox BuildDisplayCombo(SettingsPageContext ctx, AppSettings settings)
        {
            var monitors = MonitorPicker.DescribeMonitors();
            var options = MonitorPicker.BuildDisplayOptions(monitors);
            string current = settings.DesktopDockDisplay ?? MonitorPicker.PrimaryDisplayId;
            return ctx.Cards.Combo(options, current, v =>
            {
                settings.DesktopDockDisplay = string.IsNullOrEmpty(v) ? null : v;
                ctx.Settings.Save();
            });
        }

        // ---- Pane color card (#134) -----------------------------------------

        // Shared by every swatch: a rounded square filled from the swatch's own Background, with a
        // checkmark that shows only while IsChecked, and a thicker, accent-colored border while the
        // swatch itself holds keyboard focus. Built once and reused, since nothing in it varies per
        // swatch except properties already set on the control (Background, Foreground, BorderBrush).
        private static ControlTemplate _swatchTemplate;

        private static readonly FontFamily FluentIconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        /// <summary>A wrapping row of swatches, modelled on Windows 11 Personalization > Colors: a
        /// rounded square per color, a check mark on the one <paramref name="settings"/>.PaneColor
        /// names, a tooltip and an AutomationProperties.Name carrying the color's name, and keyboard
        /// focus with Space/Enter selection. Previews each color for the pane's current light/dark
        /// mode. Disabled, with a note, under Windows high contrast, which ThemeService.Apply already
        /// ignores PaneColor under, the same as the accent color.</summary>
        private static Border BuildPaneColorCard(SettingsPageContext ctx, AppSettings settings)
        {
            var cards = ctx.Cards;
            bool highContrast = App.Theme != null && App.Theme.IsHighContrast;
            bool dark = App.Theme != null && App.Theme.IsDark;
            string current = ThemeService.NormalizePaneColorName(settings.PaneColor);

            var title = new TextBlock { Text = "Pane color", FontSize = 14, TextWrapping = TextWrapping.Wrap };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            string descriptionText = highContrast
                ? "Tints the pane's background. Ignored while Windows runs a high contrast theme, like the accent color."
                : "Tints the pane's background. Default matches Explorer's own color.";
            var description = new TextBlock
            {
                Text = descriptionText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 12),
            };
            description.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

            // The light tints are close to one another by design, so the chosen one is named in text
            // as well; the tooltip alone needs a hover to tell Sage from Teal.
            var selectedName = new TextBlock { Text = current, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
            selectedName.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, IsEnabled = !highContrast };
            RadioButton firstSwatch = null;
            foreach (var name in ThemeService.PaneColorNames)
            {
                var swatch = BuildSwatch(name, dark, string.Equals(name, current, StringComparison.OrdinalIgnoreCase),
                    chosen => { settings.PaneColor = chosen; selectedName.Text = chosen; ctx.Settings.Save(); });
                if (firstSwatch == null) firstSwatch = swatch;
                wrap.Children.Add(swatch);
            }

            var body = new StackPanel();
            body.Children.Add(title);
            body.Children.Add(description);
            body.Children.Add(wrap);
            body.Children.Add(selectedName);

            var border = cards.CardShell(body, hoverable: false);
            if (firstSwatch != null) cards.Register("paneColor", firstSwatch);
            return border;
        }

        /// <summary>One 32x32 swatch: a RadioButton so the group (all swatches share one GroupName)
        /// stays single-select with native arrow-key and Tab navigation, templated to look like a
        /// plain color square rather than a radio dot. <paramref name="onSelected"/> fires only when
        /// a different swatch becomes checked, same as a combo box's SelectionChanged.</summary>
        private static RadioButton BuildSwatch(string name, bool dark, bool isChecked, Action<string> onSelected)
        {
            // Opaque in the preview: at the pane's own E6 alpha the card's translucent background
            // shows through and the light tints wash out into one another.
            var color = ThemeService.PreviewColor(name, dark);
            color.A = 0xFF;
            var fill = new SolidColorBrush(color); fill.Freeze();
            var checkForeground = ThemeService.IsDarkColor(color) ? Brushes.White : Brushes.Black;

            var radio = new RadioButton
            {
                GroupName = "PaneColorSwatch",
                // The Fluent RadioButton style sets MinWidth 120, which spread the swatches far apart.
                Width = 32,
                MinWidth = 0,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                Focusable = true,
                Template = SwatchTemplate(),
                Background = fill,
                Foreground = checkForeground,
                BorderThickness = new Thickness(1),
                IsChecked = isChecked,
                Tag = name,
            };
            radio.SetResourceReference(Control.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            AutomationProperties.SetName(radio, name);
            ToolTipService.SetToolTip(radio, name);

            radio.Checked += (s, e) => onSelected?.Invoke(name);
            // RadioButton answers Space out of the box (ButtonBase), but not Enter; the spec asks
            // for both, same as a Windows 11 color swatch.
            radio.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { radio.IsChecked = true; e.Handled = true; }
            };
            return radio;
        }

        private static ControlTemplate SwatchTemplate()
        {
            if (_swatchTemplate != null) return _swatchTemplate;

            var swatch = new FrameworkElementFactory(typeof(Border), "Swatch");
            swatch.SetValue(FrameworkElement.WidthProperty, 32.0);
            swatch.SetValue(FrameworkElement.HeightProperty, 32.0);
            swatch.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            swatch.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            swatch.SetBinding(Border.BackgroundProperty,
                new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            swatch.SetBinding(Border.BorderBrushProperty,
                new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });

            var check = new FrameworkElementFactory(typeof(TextBlock), "Check");
            check.SetValue(TextBlock.TextProperty, "\uE73E");   // Segoe Fluent Icons CheckMark, same glyph as CheckBoxCheckedGlyph
            check.SetValue(TextBlock.FontFamilyProperty, FluentIconFont);
            check.SetValue(TextBlock.FontSizeProperty, 14.0);
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            check.SetBinding(TextBlock.ForegroundProperty,
                new Binding("Foreground") { RelativeSource = RelativeSource.TemplatedParent });

            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.AppendChild(swatch);
            grid.AppendChild(check);

            var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = grid };

            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Check"));
            template.Triggers.Add(checkedTrigger);

            var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focusTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Swatch"));
            focusTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentFillColorDefaultBrush"), "Swatch"));
            template.Triggers.Add(focusTrigger);

            template.Seal();
            _swatchTemplate = template;
            return _swatchTemplate;
        }
    }
}
