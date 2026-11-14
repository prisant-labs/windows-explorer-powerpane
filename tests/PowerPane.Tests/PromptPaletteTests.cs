using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using NUnit.Framework;
using PowerPane.Services;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>The name prompt's colours (#254), decided from plain colour inputs so no window is shown.</summary>
    [TestFixture]
    public class PromptPaletteTests
    {
        private static Dictionary<string, Color> ReadTheme(string themeFile, string paneName = null, bool dark = false)
        {
            var path = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "..", "PowerPane", "Themes", themeFile));
            var text = File.ReadAllText(path);
            var d = new Dictionary<string, Color>();
            foreach (Match m in Regex.Matches(text, "<SolidColorBrush x:Key=\"([^\"]+)\"\\s+Color=\"([^\"]+)\""))
                d[m.Groups[1].Value] = (Color)ColorConverter.ConvertFromString(m.Groups[2].Value);
            var swatch = paneName == null ? null : ThemeService.PaneBackground(paneName, dark);
            if (swatch.HasValue) d["SidebarBackground"] = swatch.Value;
            return d;
        }

        private static Func<string, Color?> Lookup(Dictionary<string, Color> d)
        {
            return key => { Color c; return d.TryGetValue(key, out c) ? (Color?)c : null; };
        }

        [Test]
        public void Light_UsesTheLightThemeBrushes()
        {
            var p = PromptPalette.FromPane(Lookup(ReadTheme("Theme.Light.xaml")), dark: false, highContrast: false);
            Assert.That(p.Shell, Is.EqualTo(Color.FromRgb(0xF3, 0xF3, 0xF3)));
            Assert.That(p.Text, Is.EqualTo(Color.FromRgb(0x1A, 0x1A, 0x1A)));
            Assert.That(p.Border, Is.EqualTo(Color.FromRgb(0xD0, 0xD0, 0xD0)));
            Assert.That(p.FieldFill, Is.EqualTo(Color.FromRgb(0xFF, 0xFF, 0xFF)));
            Assert.That(p.FieldBorder, Is.EqualTo(Color.FromRgb(0xA0, 0xA0, 0xA0)));
            Assert.That(p.Warning, Is.EqualTo(Color.FromRgb(0xB0, 0x00, 0x20)));
        }

        [Test]
        public void Dark_TransparentFieldFillTakesTheShellColour()
        {
            var p = PromptPalette.FromPane(Lookup(ReadTheme("Theme.Dark.xaml")), dark: true, highContrast: false);
            Assert.That(p.Shell, Is.EqualTo(Color.FromRgb(0x1E, 0x1E, 0x1E)), "alpha E6 is forced to opaque");
            Assert.That(p.FieldFill, Is.EqualTo(p.Shell), "FieldBackground is transparent in dark");
            Assert.That(p.Text, Is.EqualTo(Color.FromRgb(0xED, 0xED, 0xED)));
            Assert.That(p.Warning, Is.EqualTo(Color.FromRgb(0xFF, 0x99, 0x99)));
        }

        [Test]
        public void Shell_IsOpaque_AndKeepsTheRgb()
        {
            var d = new Dictionary<string, Color>
            {
                { "SidebarBackground", Color.FromArgb(0xE6, 0x12, 0x34, 0x56) },
                { "TextPrimary", Colors.White }, { "SeparatorColor", Colors.Gray }, { "FieldBorder", Colors.Gray }
            };
            var p = PromptPalette.FromPane(Lookup(d), true, false);
            Assert.That(p.Shell, Is.EqualTo(Color.FromArgb(0xFF, 0x12, 0x34, 0x56)));
        }

        [Test]
        public void HighContrast_WarningIsTextColour_NotGrayText()
        {
            var gray = Color.FromRgb(0xA6, 0xA6, 0xA6);
            var pal = ThemeService.HighContrastPalette(Colors.Black, Colors.White,
                Color.FromRgb(0x1A, 0xEB, 0xFF), Colors.Black, gray);
            var p = PromptPalette.FromPane(Lookup(pal), true, highContrast: true);
            Assert.That(p.Warning, Is.EqualTo(p.Text));
            Assert.That(p.Warning, Is.Not.EqualTo(gray));
            Assert.That(p.HighContrast, Is.True);
            Assert.That(p.Shell, Is.EqualTo(Colors.Black));
        }

        [Test]
        // A null palette makes TextPrompt use FromRegistry(ReadDark()), so the fallback follows the real
        // Windows app mode, not a pane mode that could not be read (#254 review).
        [TestCase("SidebarBackground")]
        [TestCase("TextPrimary")]
        [TestCase("SeparatorColor")]
        [TestCase("FieldBorder")]
        [TestCase("FieldBackground")]
        [TestCase("WarningText")]
        public void MissingBrush_GivesNoPanePalette(string key)
        {
            foreach (var file in new[] { "Theme.Light.xaml", "Theme.Dark.xaml" })
            {
                var d = ReadTheme(file);
                d.Remove(key);
                Assert.That(PromptPalette.FromPane(Lookup(d), file.Contains("Dark"), false), Is.Null, file + " without " + key);
            }
        }

        [Test]
        public void HighContrast_DoesNotNeedWarningText()
        {
            var d = ReadTheme("Theme.Light.xaml");
            d.Remove("WarningText");
            var p = PromptPalette.FromPane(Lookup(d), false, true);
            Assert.That(p, Is.Not.Null);
            Assert.That(p.Warning, Is.EqualTo(p.Text));
        }

        [TestCase(false, 0xF7, 0xF7, 0xF7, 0x1A, 0x1A, 0x1A, 0xCF, 0xCF, 0xCF, 0xB0, 0x00, 0x20)]
        [TestCase(true, 0x24, 0x24, 0x24, 0xED, 0xED, 0xED, 0x3A, 0x3A, 0x3A, 0xFF, 0x99, 0x99)]
        public void FromRegistry_KeepsTheColoursTheSettingsPromptsAlwaysHad(bool dark,
            int sr, int sg, int sb, int tr, int tg, int tb, int br, int bg, int bb, int wr, int wg, int wb)
        {
            var p = PromptPalette.FromRegistry(dark);
            Assert.That(p.Shell, Is.EqualTo(Color.FromRgb((byte)sr, (byte)sg, (byte)sb)));
            Assert.That(p.Text, Is.EqualTo(Color.FromRgb((byte)tr, (byte)tg, (byte)tb)));
            Assert.That(p.Border, Is.EqualTo(Color.FromRgb((byte)br, (byte)bg, (byte)bb)));
            Assert.That(p.Warning, Is.EqualTo(Color.FromRgb((byte)wr, (byte)wg, (byte)wb)));
            Assert.That(p.HighContrast, Is.False);
        }

        [Test]
        public void EveryPaneColour_KeepsTextAndWarningReadable([ValueSource(typeof(ThemeService), nameof(ThemeService.PaneColorNames))] string name,
            [Values(false, true)] bool dark)
        {
            var d = ReadTheme(dark ? "Theme.Dark.xaml" : "Theme.Light.xaml", name, dark);
            var p = PromptPalette.FromPane(Lookup(d), dark, false);
            Assert.That(p.Shell.A, Is.EqualTo(255));
            Assert.That(PromptPalette.ContrastRatio(p.Text, p.Shell), Is.GreaterThanOrEqualTo(7.0), name + " text");
            Assert.That(PromptPalette.ContrastRatio(p.Warning, p.Shell), Is.GreaterThanOrEqualTo(4.5), name + " warning");
            Assert.That(PromptPalette.ContrastRatio(p.Text, p.FieldFill), Is.GreaterThanOrEqualTo(7.0), name + " field text");
        }
    }
}
