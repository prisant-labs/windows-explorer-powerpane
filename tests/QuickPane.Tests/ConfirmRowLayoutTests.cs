using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using QuickPane.UI.Settings;

namespace QuickPane.Tests
{
    /// <summary>#178: the Reset to default confirmation is a long sentence. In a horizontal
    /// StackPanel it pushed the Reset button half out of the Settings window and Cancel out of
    /// sight. ConfirmRow now wraps the message and keeps both buttons in their own columns. Builds
    /// controls only, no window, and measures at the width of a narrow Settings content area.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ConfirmRowLayoutTests
    {
        private const string LongMessage =
            "Restore the default order and on/off state of every pane section? Section names you renamed are kept.";

        [Test]
        public void LongMessage_Wraps_AndBothButtonsFitTheWidth()
        {
            var cards = new SettingsCards();
            var row = cards.ConfirmRow(LongMessage, "Reset", () => { }, () => { });

            const double width = 420;
            row.Measure(new Size(width, double.PositiveInfinity));
            row.Arrange(new Rect(0, 0, width, row.DesiredSize.Height));

            var text = LogicalTreeHelper.GetChildren(row).OfType<TextBlock>().Single();
            var buttons = LogicalTreeHelper.GetChildren(row).OfType<Button>().ToList();

            Assert.That(text.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
            Assert.That(buttons.Select(b => (string)b.Content), Is.EqualTo(new[] { "Reset", "Cancel" }));
            Assert.That(row.DesiredSize.Width, Is.LessThanOrEqualTo(width));
            foreach (var b in buttons)
            {
                var right = b.TranslatePoint(new Point(b.ActualWidth, 0), row).X;
                Assert.That(right, Is.LessThanOrEqualTo(width), (string)b.Content + " ends past the row's width");
            }
        }
    }
}
