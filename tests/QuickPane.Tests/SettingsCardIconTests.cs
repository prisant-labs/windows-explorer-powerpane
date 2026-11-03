using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using QuickPane.UI.Settings;

namespace QuickPane.Tests
{
    /// <summary>A card's leading icon takes the theme's primary text brush. It used to set no
    /// foreground and inherit WPF's default black, which disappeared on a dark card (#149). Each
    /// test puts a known brush under the theme key on the card itself and checks that the icon
    /// resolves to it. Builds controls only; no window.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class SettingsCardIconTests
    {
        private const string Glyph = "\uE7C4";
        private static readonly SolidColorBrush Themed = new SolidColorBrush(Color.FromRgb(0x12, 0x34, 0x56));

        private static TextBlock FindIcon(DependencyObject root)
        {
            return Descendants(root).OfType<TextBlock>().First(t => t.Text == Glyph);
        }

        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }

        [Test]
        public void Card_IconUsesThemePrimaryTextBrush()
        {
            var card = new SettingsCards().Card("icon-test", Glyph, "Title", "Description", null);
            card.Resources["TextFillColorPrimaryBrush"] = Themed;
            Assert.That(FindIcon(card).Foreground, Is.SameAs(Themed));
        }

        [Test]
        public void Expander_IconUsesThemePrimaryTextBrush()
        {
            var expander = new SettingsCards().Expander("icon-test", Glyph, "Title", "Description");
            expander.Root.Resources["TextFillColorPrimaryBrush"] = Themed;
            Assert.That(FindIcon(expander.Root).Foreground, Is.SameAs(Themed));
        }
    }
}
