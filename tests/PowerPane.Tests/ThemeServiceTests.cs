using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>Pins how the Windows accent is read from the registry. Windows stores it as a DWORD in
    /// 0xAABBGGRR order, and the registry hands a DWORD back as a signed int.</summary>
    [TestFixture]
    public class ThemeServiceTests
    {
        [Test]
        public void ParseAccent_DefaultBlue()
        {
            Assert.That(ThemeService.ParseAccent(unchecked((int)0xFFD47800)), Is.EqualTo(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4)));
        }

        [Test]
        public void ParseAccent_SwapsRedAndBlue()
        {
            // An orange accent: red F7, green 63, blue 0C.
            Assert.That(ThemeService.ParseAccent(unchecked((int)0xFF0C63F7)), Is.EqualTo(Color.FromArgb(0xFF, 0xF7, 0x63, 0x0C)));
        }

        [Test]
        public void ParseAccent_ZeroAlpha_IsOpaque()
        {
            Assert.That(ThemeService.ParseAccent(0x0012A05B), Is.EqualTo(Color.FromArgb(0xFF, 0x5B, 0xA0, 0x12)));
        }

        [Test]
        public void ParseAccent_MissingValue_IsNull()
        {
            Assert.That(ThemeService.ParseAccent(null), Is.Null);
        }

        [Test]
        public void ParseAccent_NotADword_IsNull()
        {
            Assert.That(ThemeService.ParseAccent(new byte[] { 0x00, 0x78, 0xD4, 0x00 }), Is.Null);
            Assert.That(ThemeService.ParseAccent("#0078D4"), Is.Null);
        }

        // The colours of the default black contrast theme.
        private static readonly Color Black = Color.FromRgb(0x00, 0x00, 0x00);
        private static readonly Color White = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color Cyan = Color.FromRgb(0x1A, 0xEB, 0xFF);
        private static readonly Color Gray = Color.FromRgb(0xA6, 0xA6, 0xA6);

        private static Dictionary<string, Color> BlackPalette()
        {
            return ThemeService.HighContrastPalette(Black, White, Cyan, Black, Gray);
        }

        /// <summary>The theme brush keys, read from the dictionary file the app loads.</summary>
        private static HashSet<string> KeysIn(string themeFile)
        {
            var path = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "..", "PowerPane", "Themes", themeFile));
            var text = File.ReadAllText(path);
            return new HashSet<string>(Regex.Matches(text, "<SolidColorBrush x:Key=\"([^\"]+)\"")
                .Cast<Match>().Select(m => m.Groups[1].Value));
        }

        [TestCase("Theme.Light.xaml")]
        [TestCase("Theme.Dark.xaml")]
        public void HighContrast_CoversEveryThemeBrush(string themeFile)
        {
            var keys = KeysIn(themeFile);
            Assert.That(keys, Is.Not.Empty);
            Assert.That(BlackPalette().Keys, Is.SupersetOf(keys));
        }

        [Test]
        public void HighContrast_UsesTheSystemColours()
        {
            var p = BlackPalette();
            Assert.That(p["SidebarBackground"], Is.EqualTo(Black));
            Assert.That(p["TextPrimary"], Is.EqualTo(White));
            Assert.That(p["TextSecondary"], Is.EqualTo(White), "contrast themes have no secondary text colour");
            Assert.That(p["AccentBrush"], Is.EqualTo(Cyan));
            Assert.That(p["TextOnAccent"], Is.EqualTo(Black));
            Assert.That(p["ItemActiveBackground"], Is.EqualTo(Color.FromArgb(0x66, 0x1A, 0xEB, 0xFF)));
        }

        [Test]
        public void IsDarkColor_SplitsBlackFromWhite()
        {
            Assert.That(ThemeService.IsDarkColor(Black), Is.True);
            Assert.That(ThemeService.IsDarkColor(White), Is.False);
            Assert.That(ThemeService.IsDarkColor(Color.FromRgb(0x20, 0x20, 0x20)), Is.True);
        }
    }
}
