using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using PowerPane.UI.Settings;

namespace PowerPane.Tests
{
    /// <summary>#154: Card()'s two-line, 68 px row (CardMinHeight) was already covered indirectly
    /// through SettingsCardIconTests and SettingsCardsTests. CompactCard() is the new single-line,
    /// 48 px row (CompactCardMinHeight) a Pane sections row uses when it has no description, with
    /// its icon centred on the title instead of pinned to the top. Builds controls only, no
    /// window, same pattern as SettingsCardIconTests.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class SettingsCardsCompactTests
    {
        private const string Glyph = "G";

        private static TextBlock FindIcon(DependencyObject root)
        {
            return Descendants(root).OfType<TextBlock>().First(t => t.Text == Glyph);
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }

        [Test]
        public void CompactCard_IsShorterThanCard_WithIconCentredNotTop()
        {
            var cards = new SettingsCards();
            var compact = cards.CompactCard("compact-test", Glyph, "Title", null);

            Assert.That(compact.MinHeight, Is.EqualTo(SettingsCards.CompactCardMinHeight));
            Assert.That(compact.Padding.Top, Is.EqualTo(SettingsCards.CompactCardVerticalPadding));
            Assert.That(compact.Padding.Bottom, Is.EqualTo(SettingsCards.CompactCardVerticalPadding));
            Assert.That(FindIcon(compact).VerticalAlignment, Is.EqualTo(VerticalAlignment.Center));
        }

        // #203: both reorderable pages build their row controls here, so the gap cannot drift apart.
        [Test]
        public void ReorderControls_LeavesAGapAfterTheSwitch_InOrder()
        {
            var cards = new SettingsCards();
            var toggle = new Border();
            var up = new Button();
            var down = new Button();

            var controls = cards.ReorderControls(toggle, up, down);

            Assert.That(controls.Children.Cast<UIElement>(), Is.EqualTo(new UIElement[] { toggle, up, down }));
            Assert.That(toggle.Margin.Right, Is.EqualTo(SettingsCards.ToggleToMoveGap));
            Assert.That(SettingsCards.ToggleToMoveGap, Is.GreaterThan(0));
        }

        [Test]
        public void Card_KeepsItsOwnTallerHeight_WithIconPinnedToTop()
        {
            var cards = new SettingsCards();
            var ordinary = cards.Card("ordinary-test", Glyph, "Title", "Description", null);

            Assert.That(ordinary.MinHeight, Is.EqualTo(SettingsCards.CardMinHeight));
            Assert.That(ordinary.Padding.Top, Is.EqualTo(SettingsCards.CardPadding));
            Assert.That(ordinary.Padding.Bottom, Is.EqualTo(SettingsCards.CardPadding));
            Assert.That(FindIcon(ordinary).VerticalAlignment, Is.EqualTo(VerticalAlignment.Top));
        }
    }
}
