using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using QuickPane.UI.Settings;

namespace QuickPane.Tests
{
    /// <summary>#201: a page body that is one card filled the whole page, because a ScrollViewer
    /// arranges its content at the viewport height. The test measures a real ScrollViewer rather
    /// than only reading the property, so it fails for the reason a person would see.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class SettingsPageBodyTests
    {
        private static double ArrangedHeight(FrameworkElement body)
        {
            var scroller = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            scroller.Measure(new Size(600, 800));
            scroller.Arrange(new Rect(0, 0, 600, 800));
            scroller.UpdateLayout();
            return body.ActualHeight;
        }

        [Test]
        public void WithoutPinning_ASingleCardFillsTheViewport()
        {
            var card = new SettingsCards().Card("stretch-test", "G", "Title", "Description", null);
            // Less than 800 only by the card's own bottom margin.
            Assert.That(ArrangedHeight(card), Is.GreaterThan(700));
        }

        [Test]
        public void PinToTop_KeepsASingleCardAtItsOwnHeight()
        {
            var card = new SettingsCards().Card("pin-test", "G", "Title", "Description", null);
            SettingsWindow.PinToTop(card);
            Assert.That(ArrangedHeight(card), Is.LessThan(200));
        }

        [Test]
        public void PinToTop_AcceptsANullBody()
        {
            Assert.DoesNotThrow(() => SettingsWindow.PinToTop(null));
        }
    }
}
