using System;
using System.Windows;
using System.Windows.Media;

namespace PowerPane.UI
{
    /// <summary>
    /// The colours of the name prompt (#254). A prompt opened from the pane takes them from the pane's
    /// own theme brushes, so it matches the menus beside it in light, dark, every pane colour and a
    /// Windows contrast theme. The Settings prompts pass no palette and keep the registry colours they
    /// have always had. Every colour is opaque: the prompt window is transparent, so a translucent
    /// fill would show the desktop through the text.
    /// </summary>
    internal sealed class PromptPalette
    {
        public Color Shell { get; private set; }
        public Color Border { get; private set; }
        public Color Text { get; private set; }
        public Color FieldFill { get; private set; }
        public Color FieldBorder { get; private set; }
        public Color Warning { get; private set; }

        /// <summary>True under a Windows contrast theme, where colour cannot tell a message from other
        /// text, so the message is drawn semibold instead.</summary>
        public bool HighContrast { get; private set; }

        /// <summary>The colours the prompt has always used, picked from the Windows app mode. The
        /// Settings prompts use these, and a pane prompt falls back to them when a brush is missing.
        /// The field colours are not used on this path: the Settings text box stays WPF's default.</summary>
        internal static PromptPalette FromRegistry(bool dark)
        {
            var shell = dark ? Color.FromRgb(0x24, 0x24, 0x24) : Color.FromRgb(0xF7, 0xF7, 0xF7);
            var border = dark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xCF, 0xCF, 0xCF);
            return new PromptPalette
            {
                Shell = shell,
                Border = border,
                Text = dark ? Color.FromRgb(0xED, 0xED, 0xED) : Color.FromRgb(0x1A, 0x1A, 0x1A),
                FieldFill = shell,
                FieldBorder = border,
                Warning = dark ? Color.FromRgb(0xFF, 0x99, 0x99) : Color.FromRgb(0xB0, 0x00, 0x20),
                HighContrast = false
            };
        }

        /// <summary>Decides every colour from the pane's brush colours, given by key. A missing key the
        /// prompt needs gives the registry colours, so a missing brush never draws grey on grey.</summary>
        internal static PromptPalette FromPane(Func<string, Color?> lookup, bool dark, bool highContrast)
        {
            var back = lookup("SidebarBackground");
            var text = lookup("TextPrimary");
            var sep = lookup("SeparatorColor");
            var fieldBorder = lookup("FieldBorder");
            if (!back.HasValue || !text.HasValue || !sep.HasValue || !fieldBorder.HasValue)
                return FromRegistry(dark);

            var shell = Opaque(back.Value);
            var fieldBack = lookup("FieldBackground");
            var fill = fieldBack.HasValue && fieldBack.Value.A == 255 ? fieldBack.Value : shell;

            var warning = text.Value;
            if (!highContrast)
            {
                var warn = lookup("WarningText");
                if (warn.HasValue && ContrastRatio(Opaque(warn.Value), shell) >= 4.5) warning = Opaque(warn.Value);
            }

            return new PromptPalette
            {
                Shell = shell,
                Border = Opaque(sep.Value),
                Text = Opaque(text.Value),
                FieldFill = fill,
                FieldBorder = Opaque(fieldBorder.Value),
                Warning = Opaque(warning),
                HighContrast = highContrast
            };
        }

        /// <summary>The pane's colours as they are now, or null when the app is not running with themes
        /// loaded (before start-up, or in a test), which makes the prompt use the registry colours.</summary>
        internal static PromptPalette CurrentPane()
        {
            try
            {
                if (Application.Current == null) return null;
                bool dark = App.Theme != null && App.Theme.IsDark;
                bool hc = App.Theme != null && App.Theme.IsHighContrast;
                return FromPane(key =>
                {
                    var b = Application.Current.TryFindResource(key) as SolidColorBrush;
                    return b == null ? (Color?)null : b.Color;
                }, dark, hc);
            }
            catch { return null; }
        }

        internal static Color Opaque(Color c)
        {
            return Color.FromRgb(c.R, c.G, c.B);
        }

        private static double Channel(byte v)
        {
            double c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        private static double Luminance(Color c)
        {
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        /// <summary>The WCAG contrast ratio between two opaque colours.</summary>
        internal static double ContrastRatio(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }
    }
}
