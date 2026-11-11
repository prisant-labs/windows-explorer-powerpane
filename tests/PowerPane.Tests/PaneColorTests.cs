using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>Covers the pane color swatches (#134): the lookup table in ThemeService, the stored
    /// name's normalization and its round trip through SettingsStore, and a WCAG contrast check of
    /// every swatch against both themes' own text colours. A swatch that fails contrast would make
    /// the pane hard to read rather than just look different, so this treats contrast as a build
    /// break, not a visual nitpick.</summary>
    [TestFixture]
    public class PaneColorTests
    {
        // ---- lookup -----------------------------------------------------------

        [Test]
        public void PaneBackground_Default_IsNullInBothModes()
        {
            Assert.That(ThemeService.PaneBackground("Default", dark: false), Is.Null);
            Assert.That(ThemeService.PaneBackground("Default", dark: true), Is.Null);
        }

        [Test]
        public void PaneBackground_UnknownOrMissingName_IsNullInBothModes()
        {
            Assert.That(ThemeService.PaneBackground("Nonsense", dark: false), Is.Null);
            Assert.That(ThemeService.PaneBackground("Nonsense", dark: true), Is.Null);
            Assert.That(ThemeService.PaneBackground(null, dark: false), Is.Null);
            Assert.That(ThemeService.PaneBackground("", dark: true), Is.Null);
        }

        [Test]
        public void PaneBackground_NamedSwatches_AreDistinctInEachMode()
        {
            foreach (var dark in new[] { false, true })
            {
                var seen = new HashSet<Color>();
                foreach (var name in ThemeService.PaneColorNames)
                {
                    if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)) continue;
                    var c = ThemeService.PaneBackground(name, dark);
                    Assert.That(c, Is.Not.Null, name + " should have a " + (dark ? "dark" : "light") + " colour");
                    Assert.That(seen.Add(c.Value), Is.True,
                        name + "'s " + (dark ? "dark" : "light") + " colour repeats another swatch's");
                }
            }
        }

        [Test]
        public void PaneColorNames_AreUnique()
        {
            var names = ThemeService.PaneColorNames;
            var distinct = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            Assert.That(distinct.Count, Is.EqualTo(names.Length));
        }

        [Test]
        public void PaneColorNames_StartsWithDefault()
        {
            Assert.That(ThemeService.PaneColorNames[0], Is.EqualTo("Default"));
        }

        // ---- normalize ----------------------------------------------------------

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("NotAColor")]
        public void NormalizePaneColorName_UnknownOrBlank_FallsBackToDefault(string input)
        {
            Assert.That(ThemeService.NormalizePaneColorName(input), Is.EqualTo("Default"));
        }

        [Test]
        public void NormalizePaneColorName_IsCaseInsensitiveAndTrims()
        {
            Assert.That(ThemeService.NormalizePaneColorName("sage"), Is.EqualTo("Sage"));
            Assert.That(ThemeService.NormalizePaneColorName("  SLATE  "), Is.EqualTo("Slate"));
        }

        [Test]
        public void CreateDefault_PaneColorIsDefault()
        {
            Assert.That(AppSettings.CreateDefault().PaneColor, Is.EqualTo("Default"));
        }

        [Test]
        public void Normalize_UnknownPaneColor_FallsBackToDefault()
        {
            var s = AppSettings.CreateDefault();
            s.PaneColor = "NotAColor";
            s.Normalize();
            Assert.That(s.PaneColor, Is.EqualTo("Default"));
        }

        [Test]
        public void Normalize_MissingPaneColor_FallsBackToDefault()
        {
            // GetUninitializedObject (used when deserializing settings.json) skips field
            // initializers, so a settings file saved before #134 deserializes with PaneColor null,
            // the same way an old file with no keyboardNavHotkey key deserializes KeyboardNavHotkey
            // to null.
            var s = AppSettings.CreateDefault();
            s.PaneColor = null;
            s.Normalize();
            Assert.That(s.PaneColor, Is.EqualTo("Default"));
        }

        [Test]
        public void SaveThenLoad_RoundTripsPaneColor()
        {
            using (var temp = new TempDir())
            {
                var store = new SettingsStore(temp.Path);
                store.Load();
                store.Current.PaneColor = "Sage";
                store.Save();

                var reloaded = new SettingsStore(temp.Path);
                reloaded.Load();

                Assert.That(reloaded.Current.PaneColor, Is.EqualTo("Sage"));
            }
        }

        // ---- contrast -------------------------------------------------------------

        /// <summary>The literal color a theme XAML file stores for a brush key, read from the file
        /// so this test breaks if Theme.Light.xaml or Theme.Dark.xaml's own text colours ever move,
        /// rather than from a copy of the hex that could silently drift out of sync.</summary>
        private static Color ReadThemeColor(string themeFile, string key)
        {
            var path = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "..", "PowerPane", "Themes", themeFile));
            var text = File.ReadAllText(path);
            var m = Regex.Match(text, "<SolidColorBrush x:Key=\"" + Regex.Escape(key) + "\"\\s+Color=\"([^\"]+)\"");
            Assert.That(m.Success, Is.True, key + " not found in " + themeFile);
            return (Color)ColorConverter.ConvertFromString(m.Groups[1].Value);
        }

        private static double Channel(byte v)
        {
            double c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        private static double RelativeLuminance(Color c)
        {
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        /// <summary>The WCAG 2.x contrast ratio between two colors, each treated as opaque: only the
        /// R/G/B channels feed the luminance formula, so a swatch's own alpha (E6) never enters the
        /// comparison. Blending it against whatever sits behind the pane would make the floor depend
        /// on a background PowerPane does not control, which is exactly what the spec's "treating
        /// the swatch as opaque" rules out.</summary>
        private static double ContrastRatio(Color a, Color b)
        {
            double la = RelativeLuminance(a), lb = RelativeLuminance(b);
            double lighter = Math.Max(la, lb), darker = Math.Min(la, lb);
            return (lighter + 0.05) / (darker + 0.05);
        }

        [TestCaseSource(typeof(ThemeService), nameof(ThemeService.PaneColorNames))]
        public void Swatch_MeetsContrastFloor_InLightMode(string name)
        {
            var swatch = ThemeService.PreviewColor(name, dark: false);
            var primary = ReadThemeColor("Theme.Light.xaml", "TextPrimary");
            var secondary = ReadThemeColor("Theme.Light.xaml", "TextSecondary");

            Assert.That(ContrastRatio(swatch, primary), Is.GreaterThanOrEqualTo(7.0), name + " (light) vs primary text");
            Assert.That(ContrastRatio(swatch, secondary), Is.GreaterThanOrEqualTo(4.5), name + " (light) vs secondary text");
        }

        [TestCaseSource(typeof(ThemeService), nameof(ThemeService.PaneColorNames))]
        public void Swatch_MeetsContrastFloor_InDarkMode(string name)
        {
            var swatch = ThemeService.PreviewColor(name, dark: true);
            var primary = ReadThemeColor("Theme.Dark.xaml", "TextPrimary");
            var secondary = ReadThemeColor("Theme.Dark.xaml", "TextSecondary");

            Assert.That(ContrastRatio(swatch, primary), Is.GreaterThanOrEqualTo(7.0), name + " (dark) vs primary text");
            Assert.That(ContrastRatio(swatch, secondary), Is.GreaterThanOrEqualTo(4.5), name + " (dark) vs secondary text");
        }
    }
}
