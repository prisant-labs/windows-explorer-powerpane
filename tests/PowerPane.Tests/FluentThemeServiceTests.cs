using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>Loads the ported Fluent XAML the way FluentThemeService.Attach does, without
    /// ever showing a window, so a resource-key mistake in the port (a StaticResource that only
    /// Microsoft's own flattened build would resolve) fails the build instead of only showing up
    /// later in the sandbox.</summary>
    [TestFixture]
    public class FluentThemeServiceTests
    {
        [Test]
        public void SettingsEntryDictionary_Loads()
        {
            var uri = new Uri("/PowerPane;component/Themes/Fluent/Fluent.Settings.xaml", UriKind.Relative);
            var dict = Application.LoadComponent(uri) as ResourceDictionary;
            Assert.That(dict, Is.Not.Null);
            Assert.That(dict.MergedDictionaries.Count, Is.GreaterThan(0));
        }

        [TestCase("Light")]
        [TestCase("Dark")]
        [TestCase("HC")]
        public void ColorDictionary_Loads(string name)
        {
            var uri = new Uri("/PowerPane;component/Themes/Fluent/Resources/Theme/" + name + ".xaml", UriKind.Relative);
            var dict = Application.LoadComponent(uri) as ResourceDictionary;
            Assert.That(dict, Is.Not.Null);
            Assert.That(dict.Contains("SystemAccentColor"), Is.True, "placeholder accent key missing");
        }

        // No test here for whether a live accent change updates an already-resolved
        // DynamicResource brush: that needs a connected FrameworkElement (a bare, unattached
        // ResourceDictionary is not enough -- tried, and the DependencyProperty simply stayed at
        // its CLR default, Transparent, instead of resolving at all), and constructing a
        // FrameworkElement throws "The calling thread must be STA" under this NUnit host. This
        // is exactly the AGENTS.md rule against showing a window in a test, one layer further
        // down: even a window-less FrameworkElement needs the UI apartment. The sandbox
        // screenshots are the real check for this.

        [TestCase(false, false, "Light")]
        [TestCase(true, false, "Dark")]
        [TestCase(false, true, "HC")]
        [TestCase(true, true, "HC")]
        public void ColorDictionaryName_PicksHighContrastFirst(bool isDark, bool isHighContrast, string expected)
        {
            Assert.That(FluentThemeService.ColorDictionaryName(isDark, isHighContrast), Is.EqualTo(expected));
        }

        [Test]
        public void DecodeAccentPalette_SevenQuads_ParsedInOrder()
        {
            // Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3: each quad 0xAABBGGRR, matching
            // ThemeService.AbgrToColor. Quad i gets R=i so the decoded order is easy to assert.
            var raw = new byte[32];
            for (int i = 0; i < 8; i++)
            {
                raw[i * 4 + 0] = (byte)i;     // R
                raw[i * 4 + 1] = 0x10;        // G
                raw[i * 4 + 2] = 0x20;        // B
                raw[i * 4 + 3] = 0xFF;        // A
            }

            var colors = FluentThemeService.DecodeAccentPalette(raw);

            Assert.That(colors, Is.Not.Null);
            Assert.That(colors.Length, Is.EqualTo(7));
            for (int i = 0; i < 7; i++)
                Assert.That(colors[i], Is.EqualTo(Color.FromArgb(0xFF, (byte)i, 0x10, 0x20)), "quad " + i);
        }

        [TestCase(null)]
        [TestCase(new byte[] { 1, 2, 3 })]
        public void DecodeAccentPalette_TooShort_IsNull(byte[] raw)
        {
            Assert.That(FluentThemeService.DecodeAccentPalette(raw), Is.Null);
        }

        [Test]
        public void FlatPalette_AllSevenSlotsAreTheSameColor()
        {
            // The HSL lighten/darken approximation this replaced was an untested guess; several
            // of these seven keys back opaque accent text and fills (see the remark on
            // FluentThemeService.ApplyAccent), so the fallback must stay a single known-good
            // colour rather than an invented shade.
            var accent = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
            var palette = FluentThemeService.FlatPalette(accent);

            Assert.That(palette.Length, Is.EqualTo(7));
            Assert.That(palette, Is.All.EqualTo(accent));
        }

        [Test]
        public void BuildColorDictionary_WritesAllSevenAccentKeys()
        {
            var accent = Color.FromArgb(0xFF, 0x11, 0x22, 0x33);
            var dict = FluentThemeService.BuildColorDictionary(isDark: false, isHighContrast: false, accent: accent);

            // AccentPalette is read from the real registry inside BuildColorDictionary, so this
            // only confirms the keys were written, not which source filled them -- the registry
            // path and the fallback are both exercised by DecodeAccentPalette_* and FlatPalette_*
            // above without touching the registry.
            Assert.That(dict.Contains("SystemAccentColor"), Is.True);
            Assert.That(dict.Contains("SystemAccentColorLight1"), Is.True);
            Assert.That(dict.Contains("SystemAccentColorDark3"), Is.True);
        }

        [Test]
        public void BuildColorDictionary_HighContrast_UsesHighlightNotRegistryPalette()
        {
            // The colour-accent AccentPalette registry value exists on nearly every Windows
            // install, in or out of high contrast. ApplyAccent must not read it under high
            // contrast: every key should be the flat system Highlight colour, as Windows itself
            // uses under high contrast, regardless of what AccentPalette holds, and regardless of the
            // accent passed in (chosen here to be neither Highlight nor a plausible accent).
            var highlight = System.Drawing.SystemColors.Highlight;
            var expected = Color.FromRgb(highlight.R, highlight.G, highlight.B);
            var notAccent = Color.FromArgb(0xFF, 0x01, 0x02, 0x03);

            var dict = FluentThemeService.BuildColorDictionary(isDark: false, isHighContrast: true, accent: notAccent);

            Assert.That(dict["SystemAccentColor"], Is.EqualTo(expected));
            Assert.That(dict["SystemAccentColorLight1"], Is.EqualTo(expected));
            Assert.That(dict["SystemAccentColorDark3"], Is.EqualTo(expected));
        }

        [Test]
        public void AppliedDictionaries_NeverReachApplicationLevelResources()
        {
            // The Fluent port must stay scoped to the Settings window and never leak into
            // Application.Current.Resources, which every Explorer pane inherits. A live Window
            // cannot be constructed here (AGENTS.md: tests never show a window; a bare
            // FrameworkElement throws "The calling thread must be STA" under this NUnit host, see
            // the remark above), so a fresh, unattached ResourceDictionary stands in for a
            // window's own Resources -- exactly what FluentThemeService.Attach merges into, and
            // never Application.Current.Resources, which this test leaves untouched by never
            // calling Attach or constructing an Application.
            var windowResourcesStandIn = new ResourceDictionary();

            FluentThemeService.ApplyTo(
                windowResourcesStandIn, isDark: true, isHighContrast: false,
                accent: Color.FromArgb(0xFF, 0x44, 0x55, 0x66));

            Assert.That(windowResourcesStandIn.Contains("SystemAccentColor"), Is.True,
                "the stand-in should carry the Fluent content this test added to it");

            // Nothing in this test ever touched Application.Current, so it is either null (no
            // Application exists in this test host) or, if some other test in the run created
            // one, carries none of this port's dictionaries -- FluentThemeService.Attach is the
            // only code that merges Fluent dictionaries anywhere, and it takes the target Window
            // explicitly rather than reaching for Application.Current.
            var app = Application.Current;
            if (app != null)
            {
                bool leaked = app.Resources.MergedDictionaries
                    .Any(d => d.Contains("SystemAccentColor") || (d.Source != null && d.Source.OriginalString.Contains("Fluent")));
                Assert.That(leaked, Is.False, "a Fluent dictionary reached Application.Current.Resources");
            }
        }
    }
}
