using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PowerPane.Services
{
    /// <summary>
    /// Applies the ported Microsoft Fluent WPF theme (see PowerPane/Themes/Fluent/NOTICE.md)
    /// to one window's own Resources, never to Application.Current.Resources, so the pane and
    /// every other window PowerPane shows are untouched. Call <see cref="Attach"/> once per
    /// window; it unsubscribes from <see cref="ThemeService.ThemeChanged"/> on Window.Closed.
    /// </summary>
    internal static class FluentThemeService
    {
        private const string ExplorerAccentKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent";

        private static readonly Uri SettingsEntryUri =
            new Uri("/PowerPane;component/Themes/Fluent/Fluent.Settings.xaml", UriKind.Relative);

        /// <summary>Merges the Fluent styles and a matching colour dictionary into
        /// <paramref name="window"/>.Resources, and keeps the colour dictionary in step with
        /// <paramref name="theme"/> for as long as the window stays open.</summary>
        public static void Attach(Window window, ThemeService theme)
        {
            if (window == null || theme == null) return;

            var colorDict = ApplyTo(window.Resources, theme.IsDark, theme.IsHighContrast, theme.Accent);

            Action onThemeChanged = () =>
            {
                try
                {
                    var fresh = BuildColorDictionary(theme.IsDark, theme.IsHighContrast, theme.Accent);
                    int index = window.Resources.MergedDictionaries.IndexOf(colorDict);
                    if (index >= 0) window.Resources.MergedDictionaries[index] = fresh;
                    else window.Resources.MergedDictionaries.Add(fresh);
                    colorDict = fresh;
                }
                catch (Exception ex)
                {
                    Log.Error("FluentThemeService: theme swap failed", ex);
                }
            };
            theme.ThemeChanged += onThemeChanged;
            window.Closed += (s, e) => theme.ThemeChanged -= onThemeChanged;
        }

        /// <summary>Merges the Fluent control styles and a matching colour dictionary into
        /// <paramref name="target"/> (a window's own Resources in production, or a bare,
        /// unattached ResourceDictionary standing in for one in a test, since a test must not
        /// show a window per AGENTS.md). Returns the colour dictionary so a caller can swap it
        /// again later. Never touches <c>Application.Current.Resources</c>: it only ever writes
        /// to the dictionary it was handed.</summary>
        internal static ResourceDictionary ApplyTo(ResourceDictionary target, bool isDark, bool isHighContrast, Color accent)
        {
            var colorDict = BuildColorDictionary(isDark, isHighContrast, accent);
            var stylesDict = (ResourceDictionary)Application.LoadComponent(SettingsEntryUri);
            target.MergedDictionaries.Add(colorDict);
            target.MergedDictionaries.Add(stylesDict);
            return colorDict;
        }

        /// <summary>Loads the colour dictionary named by <see cref="ColorDictionaryName"/> and
        /// feeds it the accent ramp.</summary>
        internal static ResourceDictionary BuildColorDictionary(bool isDark, bool isHighContrast, Color accent)
        {
            var uri = new Uri(
                "/PowerPane;component/Themes/Fluent/Resources/Theme/" + ColorDictionaryName(isDark, isHighContrast) + ".xaml",
                UriKind.Relative);
            var dict = (ResourceDictionary)Application.LoadComponent(uri);
            ApplyAccent(dict, isHighContrast, accent);
            return dict;
        }

        /// <summary>Which of the three ported colour dictionaries applies, matching the choice
        /// <see cref="ThemeService.Apply"/> makes for PowerPane's own Theme.Light/Dark.xaml pair:
        /// high contrast wins over light/dark.</summary>
        internal static string ColorDictionaryName(bool isDark, bool isHighContrast)
        {
            if (isHighContrast) return "HC";
            return isDark ? "Dark" : "Light";
        }

        /// <summary>Sets the seven SystemAccentColor* keys that Microsoft's file normally reads
        /// from .NET 9-only SystemColors statics (see the comment in Resources/Theme/Light.xaml).
        /// Outside high contrast, prefers Windows' own tint/shade ramp from the AccentPalette
        /// registry value, which is exact, and falls back to the flat accent PowerPane already
        /// shows elsewhere (<see cref="ThemeService.Accent"/>) only when that value is absent or
        /// malformed, with no invented shade math: these keys back opaque fills and accent text
        /// (PowerPane/Themes/Fluent/Resources/Theme/Light.xaml and Dark.xaml both route
        /// AccentTextFillColor* and AccentFillColorDefault through them), so a translucent or
        /// synthetically lightened fallback risks unreadable text the one time this path is
        /// actually exercised. Under high contrast the registry's colour accent ramp is never
        /// read at all: every key uses the flat system Highlight colour, the same one
        /// ThemeService's own HighContrastPalette uses for AccentBrush, since a contrast theme's
        /// accent should track the system palette the person chose, not the colour accent most
        /// Windows installs still carry in AccentPalette regardless of contrast mode. This reads
        /// System.Drawing.SystemColors rather than WPF's System.Windows.SystemColors, matching
        /// ThemeService.ReadRegistry's own comment: WPF's copy caches and may not have refreshed
        /// yet right after a contrast switch, which is exactly when this fallback runs live.</summary>
        internal static void ApplyAccent(ResourceDictionary colorDict, bool isHighContrast, Color accent)
        {
            var palette = isHighContrast
                ? FlatPalette(SysColor(System.Drawing.SystemColors.Highlight))
                : ReadAccentPalette() ?? FlatPalette(accent);
            colorDict["SystemAccentColorLight3"] = palette[0];
            colorDict["SystemAccentColorLight2"] = palette[1];
            colorDict["SystemAccentColorLight1"] = palette[2];
            colorDict["SystemAccentColor"] = palette[3];
            colorDict["SystemAccentColorDark1"] = palette[4];
            colorDict["SystemAccentColorDark2"] = palette[5];
            colorDict["SystemAccentColorDark3"] = palette[6];
        }

        /// <summary>Reads the AccentPalette REG_BINARY under Explorer\Accent: eight 4-byte ABGR
        /// quads (the same packing as the AccentColor DWORD ThemeService.AbgrToColor already
        /// decodes), in the order Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3, spare.
        /// Returns the first seven as Colors, or null if the value is missing or the wrong size.</summary>
        internal static Color[] ReadAccentPalette()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(ExplorerAccentKey))
                {
                    var raw = key?.GetValue("AccentPalette") as byte[];
                    return DecodeAccentPalette(raw);
                }
            }
            catch (Exception ex)
            {
                Log.Error("FluentThemeService: AccentPalette read failed", ex);
                return null;
            }
        }

        internal static Color[] DecodeAccentPalette(byte[] raw)
        {
            if (raw == null || raw.Length < 28) return null;   // need at least the 7 shades (28 bytes); the 8th "spare" quad is not always present
            int quads = Math.Min(7, raw.Length / 4);
            var result = new Color[7];
            for (int i = 0; i < 7; i++)
            {
                if (i >= quads) { result[i] = Colors.Transparent; continue; }
                uint v = BitConverter.ToUInt32(raw, i * 4);
                result[i] = ThemeService.AbgrToColor(v);
            }
            return result;
        }

        /// <summary>The fallback ramp when AccentPalette is unavailable: all seven slots are the
        /// same flat colour. See the remark on <see cref="ApplyAccent"/> for why this replaced an
        /// earlier HSL lighten/darken approximation.</summary>
        internal static Color[] FlatPalette(Color color)
        {
            return new[] { color, color, color, color, color, color, color };
        }

        private static Color SysColor(System.Drawing.Color c)
        {
            return Color.FromRgb(c.R, c.G, c.B);
        }
    }
}
