using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NUnit.Framework;
using QuickPane.UI.Settings;

namespace QuickPane.Tests
{
    /// <summary>A child row greyed by LinkEnabled must also look greyed (#150). The switch and the
    /// row's label paint their brushes in code, so they kept the enabled look until they learned to
    /// repaint on IsEnabledChanged. Each test puts known brushes under the theme keys on the row and
    /// checks which one the switch's track and the label resolve to. Builds controls only; no window.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class DependentRowDisabledLookTests
    {
        private static readonly SolidColorBrush Accent = new SolidColorBrush(Colors.Blue);
        private static readonly SolidColorBrush AccentDisabled = new SolidColorBrush(Colors.Gray);
        private static readonly SolidColorBrush Text = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush TextDisabled = new SolidColorBrush(Colors.DimGray);

        private static DockPanel Row(out Border track, out TextBlock label)
        {
            var cards = new SettingsCards();
            var toggle = cards.Toggle(true, on => { }, "Dependent option");
            var row = cards.ChildRow("Dependent option", toggle);
            row.Resources["AccentFillColorDefaultBrush"] = Accent;
            row.Resources["AccentFillColorDisabledBrush"] = AccentDisabled;
            row.Resources["TextFillColorPrimaryBrush"] = Text;
            row.Resources["TextFillColorDisabledBrush"] = TextDisabled;
            track = (Border)((ToggleButton)toggle.Tag).Content;
            label = row.Children.OfType<TextBlock>().Single();
            return row;
        }

        [Test]
        public void EnabledRow_KeepsTheAccentTrackAndPrimaryLabel()
        {
            Border track; TextBlock label;
            Row(out track, out label);
            Assert.That(track.Background, Is.SameAs(Accent));
            Assert.That(label.Foreground, Is.SameAs(Text));
        }

        [Test]
        public void DisabledRow_PaintsTheDisabledTrackAndLabel()
        {
            Border track; TextBlock label;
            var row = Row(out track, out label);
            row.IsEnabled = false;
            Assert.That(track.Background, Is.SameAs(AccentDisabled));
            Assert.That(label.Foreground, Is.SameAs(TextDisabled));

            row.IsEnabled = true;
            Assert.That(track.Background, Is.SameAs(Accent));
            Assert.That(label.Foreground, Is.SameAs(Text));
        }
    }
}
