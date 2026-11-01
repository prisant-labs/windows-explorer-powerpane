using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using NM = QuickPane.Interop.NativeMethods;

namespace QuickPane.Services
{
    /// <summary>
    /// Owns the active theme. Reads AppsUseLightTheme and the accent color from the registry, loads the
    /// matching ResourceDictionary, and overrides the accent brushes from the live system accent.
    /// SystemEvents.UserPreferenceChanged fires when the user flips dark/light or changes accent, so
    /// the sidebar restyles with no restart.
    /// </summary>
    public sealed class ThemeService : IDisposable
    {
        public event Action ThemeChanged;

        /// <summary>Raised after a live PaneColor change (#134) alone, instead of ThemeChanged, which
        /// a pane would otherwise turn into a full section rebuild. A pane's own Border is bound to
        /// SidebarBackground with DynamicResource, but that binding did not pick up a plain resource
        /// dictionary swap on an already-rendered, HwndSource-hosted Border in the sandbox -- only a
        /// fresh SetResourceReference call on that element did -- so a subscriber must force that
        /// refresh itself rather than rely on the dictionary swap alone.</summary>
        public event Action PaneColorChanged;

        public bool IsDark { get; private set; }
        public Color Accent { get; private set; } = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);

        /// <summary>A Windows contrast theme is on. Every brush then comes from the system colours the
        /// person chose, not from the light or dark dictionary (#30).</summary>
        public bool IsHighContrast { get; private set; }

        private const string PersonalizeKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        // The accent is not under Personalize, which holds only the mode and transparency switches.
        // Reading it there always missed, so the pane kept its default blue (#20). DWM holds the
        // accent itself; Explorer\Accent keeps a copy for the Start menu.
        private const string DwmKey = @"SOFTWARE\Microsoft\Windows\DWM";
        private const string ExplorerAccentKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        private ResourceDictionary _current;
        private SettingsStore _settings;
        private string _appliedPaneColor;   // the PaneColor the installed dictionary was built with

        public void Start()
        {
            ReadRegistry();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        /// <summary>Lets Apply read the live PaneColor (#134) without every caller threading an
        /// AppSettings through it. Call once, after both this service and Settings exist. Apply
        /// re-reads <c>_settings.Current</c> on every call rather than caching the AppSettings
        /// instance itself, so a settings reload (which swaps that instance) is picked up with no
        /// extra wiring.</summary>
        public void AttachSettings(SettingsStore settings)
        {
            _settings = settings;
        }

        public void Apply()
        {
            var dict = BuildThemeDictionary();
            if (dict == null) return;
            SwapDictionary(dict);
            ThemeChanged?.Invoke();
        }

        /// <summary>Re-applies a live PaneColor change (#134) without ThemeChanged, which every
        /// pane's OnDataChanged turns into a full section rebuild (SidebarControl.QueueBuild) -- the
        /// spec says a PaneColor change must not do that, since every pane already binds
        /// SidebarBackground through DynamicResource. Rebuilds and swaps in the theme dictionary the
        /// same way Apply() does (BuildThemeDictionary/SwapDictionary), since no smaller change to the
        /// dictionary reached an already-rendered pane in the sandbox: not mutating the live
        /// dictionary's SidebarBackground entry in place, and not removing and re-adding that same
        /// dictionary object either. Even the full swap here does not reach an open pane on its own --
        /// PaneColorChanged below is what actually does, by forcing that one Border to re-pull the
        /// resource. High contrast is left alone, same as Apply(): its own palette already decides
        /// SidebarBackground.</summary>
        public void ApplyPaneColor()
        {
            if (IsHighContrast) return;
            // App calls this on every settings save, such as the end of a width drag, so skip the
            // XAML reload unless the color itself changed.
            var name = NormalizePaneColorName(_settings != null ? _settings.Current?.PaneColor : null);
            if (string.Equals(name, _appliedPaneColor, StringComparison.OrdinalIgnoreCase)) return;
            var dict = BuildThemeDictionary();
            if (dict == null) return;
            SwapDictionary(dict);
            PaneColorChanged?.Invoke();
        }

        /// <summary>Loads the light or dark theme dictionary fresh from its XAML and overrides its
        /// accent brushes (or, under high contrast, its whole palette) and its pane color (#134) from
        /// the live registry/settings state. Shared by Apply() and ApplyPaneColor() so a PaneColor-only
        /// re-apply builds the exact dictionary a real theme switch would, just without firing
        /// ThemeChanged afterward.</summary>
        private ResourceDictionary BuildThemeDictionary()
        {
            var app = Application.Current;
            if (app == null) return null;

            var uri = IsDark
                ? new Uri("/QuickPane;component/Themes/Theme.Dark.xaml", UriKind.Relative)
                : new Uri("/QuickPane;component/Themes/Theme.Light.xaml", UriKind.Relative);

            ResourceDictionary dict;
            try
            {
                dict = (ResourceDictionary)Application.LoadComponent(uri);
            }
            catch (Exception ex)
            {
                Log.Error("theme dictionary load failed", ex);
                return null;
            }

            if (IsHighContrast)
            {
                // The light or dark dictionary still supplies the control styles it merges; only its
                // brushes give way to the contrast colours.
                var palette = HighContrastPalette(SysColor(System.Drawing.SystemColors.Window),
                    SysColor(System.Drawing.SystemColors.WindowText), SysColor(System.Drawing.SystemColors.Highlight),
                    SysColor(System.Drawing.SystemColors.HighlightText), SysColor(System.Drawing.SystemColors.GrayText));
                foreach (var kv in palette)
                {
                    var b = new SolidColorBrush(kv.Value); b.Freeze();
                    dict[kv.Key] = b;
                }
            }
            else
            {
                // Override accent brushes from the live system accent so it always matches Windows.
                var accentBrush = new SolidColorBrush(Accent); accentBrush.Freeze();
                var accentSoft = new SolidColorBrush(WithAlpha(Accent, 0x33)); accentSoft.Freeze();
                var accentHover = new SolidColorBrush(WithAlpha(Accent, 0x26)); accentHover.Freeze();
                dict["AccentBrush"] = accentBrush;
                dict["ItemActiveBackground"] = accentSoft;
                dict["ItemHoverBackground"] = accentHover;

                // A named pane color (#134) overrides the theme's own SidebarBackground. Default and
                // an unrecognized name both come back null from PaneBackground, so the dictionary
                // simply keeps the value Theme.*.xaml just loaded.
                _appliedPaneColor = NormalizePaneColorName(_settings != null ? _settings.Current?.PaneColor : null);
                var paneColor = PaneBackground(_appliedPaneColor, IsDark);
                if (paneColor.HasValue)
                {
                    var sidebarBrush = new SolidColorBrush(paneColor.Value); sidebarBrush.Freeze();
                    dict["SidebarBackground"] = sidebarBrush;
                }
            }

            return dict;
        }

        /// <summary>Removes whichever dictionary Apply()/ApplyPaneColor() last installed and adds
        /// the new one in its place. A real dictionary swap, not a mutation of the live one: WPF
        /// pushes the DynamicResource update through to an HwndSource-hosted pane for this, but not
        /// for an in-place edit (see ApplyPaneColor's notes).</summary>
        private void SwapDictionary(ResourceDictionary dict)
        {
            var app = Application.Current;
            if (app == null) return;
            var merged = app.Resources.MergedDictionaries;
            if (_current != null) merged.Remove(_current);
            merged.Add(dict);
            _current = dict;
        }

        /// <summary>Windows hosted as HwndSource roots fall back to Application resources, so brushes
        /// need nothing here. The title bar does: Windows draws it and keeps it light unless asked, so
        /// a dark Settings window had a white one (#26). Windows without a standard frame (the panes,
        /// the dock, the borderless prompts) draw their own chrome and are left alone.</summary>
        public void AttachWindow(Window window)
        {
            if (window == null || window.WindowStyle == WindowStyle.None) return;
            Action apply = () => ApplyTitleBar(window);
            window.SourceInitialized += (s, e) => apply();
            ThemeChanged += apply;
            window.Closed += (s, e) => ThemeChanged -= apply;
            apply();   // a no-op until the window has a handle; SourceInitialized covers that case
        }

        private void ApplyTitleBar(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = IsDark ? 1 : 0;
            if (NM.DwmSetWindowAttribute(hwnd, NM.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                NM.DwmSetWindowAttribute(hwnd, NM.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref dark, sizeof(int));
            // The title bar of a window already on screen keeps its old colours until the frame is
            // redrawn, so a theme switch would show only after the next activation without this.
            NM.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NM.SWP_NOMOVE | NM.SWP_NOSIZE | NM.SWP_NOZORDER | NM.SWP_NOACTIVATE | NM.SWP_FRAMECHANGED);
        }

        private void ReadRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    var light = key?.GetValue("AppsUseLightTheme");
                    IsDark = light is int && (int)light == 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error("theme registry read failed", ex);
                IsDark = false;
            }

            var accent = ParseAccent(ReadValue(DwmKey, "AccentColor"))
                ?? ParseAccent(ReadValue(ExplorerAccentKey, "AccentColorMenu"));
            if (accent.HasValue) Accent = accent.Value;

            // Asked of Windows each time rather than through WPF's SystemParameters and SystemColors,
            // which cache and may not have refreshed yet when this runs after a contrast switch.
            IsHighContrast = System.Windows.Forms.SystemInformation.HighContrast;
            // A contrast theme sets its own light or dark background; the title bar and the windows
            // that pick a dark or light fill follow that instead of the app mode switch.
            if (IsHighContrast) IsDark = IsDarkColor(SysColor(System.Drawing.SystemColors.Window));
        }

        /// <summary>Every theme brush in a contrast theme, from the system colours. Contrast themes have
        /// no secondary text colour (GrayText means disabled), so secondary text, rules and chevrons all
        /// take WindowText. Hover and selection tint the background with Highlight rather than fill it,
        /// so the WindowText on top stays readable; a full Highlight fill carries HighlightText.</summary>
        internal static System.Collections.Generic.Dictionary<string, Color> HighContrastPalette(
            Color window, Color windowText, Color highlight, Color highlightText, Color grayText)
        {
            return new System.Collections.Generic.Dictionary<string, Color>
            {
                { "SidebarBackground", window },
                { "TextPrimary", windowText },
                { "TextSecondary", windowText },
                { "SeparatorColor", windowText },
                { "ChevronColor", windowText },
                { "DriveBarTrack", grayText },
                { "BrokenLinkColor", grayText },
                { "FieldBackground", window },
                { "FieldBorder", windowText },
                { "BadgeBackground", WithAlpha(highlight, 0x66) },
                { "AccentBrush", highlight },
                { "ItemHoverBackground", WithAlpha(highlight, 0x40) },
                { "ItemActiveBackground", WithAlpha(highlight, 0x66) },
                { "TextOnAccent", highlightText },
            };
        }

        internal static bool IsDarkColor(Color c)
        {
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 128;
        }

        // ---- pane color swatches (#134) ------------------------------------

        /// <summary>Names a person can pick for the pane's background, in the order the Pane page
        /// offers them. "Default" always comes first: it keeps the theme's own SidebarBackground,
        /// and PaneBackground returns null for it, same as for any name this release does not
        /// recognize.</summary>
        internal static readonly string[] PaneColorNames =
        {
            "Default", "Slate", "Blue", "Teal", "Sage", "Sand", "Rose", "Lavender", "Graphite"
        };

        // Each pane color's light/dark pair, keeping the theme's own alpha (E6) so the pane's
        // existing translucency is unchanged. Rose, Lavender, and Graphite's light values are a
        // few points lighter than the #134 spec's first draft: that draft's hex failed
        // PaneColorTests' 4.5:1 secondary-text contrast floor by margins of 0.02-0.3, so each was
        // nudged toward white until it cleared the floor with a small margin. Every dark value, and
        // every other light value, is unchanged from the spec.
        private static readonly System.Collections.Generic.Dictionary<string, Color> PaneColorsLight =
            new System.Collections.Generic.Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "Slate",    Color.FromArgb(0xE6, 0xE6, 0xEB, 0xF0) },
                { "Blue",     Color.FromArgb(0xE6, 0xE3, 0xED, 0xF7) },
                { "Teal",     Color.FromArgb(0xE6, 0xE0, 0xF0, 0xEE) },
                { "Sage",     Color.FromArgb(0xE6, 0xE6, 0xEF, 0xE3) },
                { "Sand",     Color.FromArgb(0xE6, 0xF3, 0xEC, 0xDD) },
                { "Rose",     Color.FromArgb(0xE6, 0xF6, 0xE8, 0xEB) },
                { "Lavender", Color.FromArgb(0xE6, 0xEE, 0xE9, 0xF6) },
                { "Graphite", Color.FromArgb(0xE6, 0xEB, 0xEB, 0xEB) },
            };

        private static readonly System.Collections.Generic.Dictionary<string, Color> PaneColorsDark =
            new System.Collections.Generic.Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "Slate",    Color.FromArgb(0xE6, 0x1F, 0x24, 0x2B) },
                { "Blue",     Color.FromArgb(0xE6, 0x1A, 0x24, 0x33) },
                { "Teal",     Color.FromArgb(0xE6, 0x17, 0x2A, 0x28) },
                { "Sage",     Color.FromArgb(0xE6, 0x1E, 0x2A, 0x1E) },
                { "Sand",     Color.FromArgb(0xE6, 0x2B, 0x26, 0x19) },
                { "Rose",     Color.FromArgb(0xE6, 0x2D, 0x1E, 0x22) },
                { "Lavender", Color.FromArgb(0xE6, 0x24, 0x1F, 0x30) },
                { "Graphite", Color.FromArgb(0xE6, 0x2B, 0x2B, 0x2B) },
            };

        // The literal SidebarBackground Theme.Light.xaml and Theme.Dark.xaml load. Kept here only so
        // PreviewColor can show "Default" an actual color; Apply never reads these, since letting
        // the loaded theme dictionary supply the real value (by leaving SidebarBackground alone) is
        // the whole point of PaneBackground returning null for Default.
        private static readonly Color DefaultPaneLight = Color.FromArgb(0xE6, 0xF3, 0xF3, 0xF3);
        private static readonly Color DefaultPaneDark = Color.FromArgb(0xE6, 0x1E, 0x1E, 0x1E);

        /// <summary>The pane background a named swatch paints, for the given light/dark mode, or
        /// null for "Default" or any name this release does not recognize -- both of which mean
        /// "leave today's theme value alone" (#134).</summary>
        internal static Color? PaneBackground(string name, bool dark)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var table = dark ? PaneColorsDark : PaneColorsLight;
            Color c;
            return table.TryGetValue(name, out c) ? (Color?)c : null;
        }

        /// <summary>Like PaneBackground, but never null: "Default" and an unknown name come back as
        /// the theme's own literal SidebarBackground instead. For the Pane page's swatch row, which
        /// needs a real color to paint even for the swatch that means "don't override anything."</summary>
        internal static Color PreviewColor(string name, bool dark)
        {
            return PaneBackground(name, dark) ?? (dark ? DefaultPaneDark : DefaultPaneLight);
        }

        /// <summary>Maps a stored pane color name to one this release recognizes, case-insensitively,
        /// falling back to "Default" for null, blank, or a name renamed or removed since it was saved
        /// (#134). AppSettings.Normalize calls this, so a pane never reads a name PaneBackground
        /// cannot look up.</summary>
        internal static string NormalizePaneColorName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Default";
            var trimmed = name.Trim();
            foreach (var known in PaneColorNames)
            {
                if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase)) return known;
            }
            return "Default";
        }

        private static Color SysColor(System.Drawing.Color c)
        {
            return Color.FromRgb(c.R, c.G, c.B);
        }

        private static object ReadValue(string keyPath, string name)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
                    return key?.GetValue(name);
            }
            catch (Exception ex)
            {
                Log.Error("theme registry read failed: " + name, ex);
                return null;
            }
        }

        /// <summary>The accent from a registry value, or null when the value is missing or is not a
        /// DWORD. The registry hands a DWORD back as a signed int.</summary>
        internal static Color? ParseAccent(object value)
        {
            if (!(value is int)) return null;
            return AbgrToColor(unchecked((uint)(int)value));
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Accessibility is the category a contrast theme switch arrives as.
            if (e.Category != UserPreferenceCategory.General &&
                e.Category != UserPreferenceCategory.Color &&
                e.Category != UserPreferenceCategory.VisualStyle &&
                e.Category != UserPreferenceCategory.Accessibility)
                return;

            var app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                ReadRegistry();
                Apply();
            }));
        }

        // Registry AccentColor is 0xAABBGGRR; WPF wants ARGB.
        internal static Color AbgrToColor(uint v)
        {
            byte a = (byte)((v >> 24) & 0xFF);
            byte b = (byte)((v >> 16) & 0xFF);
            byte g = (byte)((v >> 8) & 0xFF);
            byte r = (byte)(v & 0xFF);
            if (a == 0) a = 0xFF;
            return Color.FromArgb(a, r, g, b);
        }

        private static Color WithAlpha(Color c, byte a)
        {
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        public void Dispose()
        {
            try { SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; } catch { }
        }
    }
}
