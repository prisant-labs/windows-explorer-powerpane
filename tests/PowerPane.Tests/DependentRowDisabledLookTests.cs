using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NUnit.Framework;
using PowerPane.UI.Settings;

namespace PowerPane.Tests
{
    /// <summary>A child row greyed by LinkEnabled must also look greyed (#150), and a disabled On
    /// track must keep a visible outline in high contrast (#165). The switch and the row's label
    /// paint their brushes in code, so they kept the enabled look until they learned to repaint on
    /// IsEnabledChanged. Each test puts known brushes under the theme keys on the row and checks
    /// which one the switch's track and the label resolve to. Builds controls only; no window.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class DependentRowDisabledLookTests
    {
        private static readonly SolidColorBrush Accent = new SolidColorBrush(Colors.Blue);
        private static readonly SolidColorBrush AccentDisabled = new SolidColorBrush(Colors.Gray);
        private static readonly SolidColorBrush Text = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush TextDisabled = new SolidColorBrush(Colors.DimGray);
        private static readonly SolidColorBrush StrongStrokeDisabled = new SolidColorBrush(Colors.Black);

        private static DockPanel Row(out Border track, out TextBlock label)
        {
            var cards = new SettingsCards();
            var toggle = cards.Toggle(true, on => { }, "Dependent option");
            var row = cards.ChildRow("Dependent option", toggle);
            row.Resources["AccentFillColorDefaultBrush"] = Accent;
            row.Resources["AccentFillColorDisabledBrush"] = AccentDisabled;
            row.Resources["TextFillColorPrimaryBrush"] = Text;
            row.Resources["TextFillColorDisabledBrush"] = TextDisabled;
            row.Resources["ControlStrongStrokeColorDisabledBrush"] = StrongStrokeDisabled;
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
            // #165: the enabled On track has no outline, same as before the fix.
            Assert.That(track.BorderThickness, Is.EqualTo(new Thickness(0)));
        }

        [Test]
        public void DisabledRow_PaintsTheDisabledTrackAndLabel()
        {
            Border track; TextBlock label;
            var row = Row(out track, out label);
            row.IsEnabled = false;
            Assert.That(track.Background, Is.SameAs(AccentDisabled));
            Assert.That(label.Foreground, Is.SameAs(TextDisabled));
            // #165: in high contrast AccentFillColorDisabledBrush matches the card behind it, so the
            // disabled On track needs its own outline to stay visible.
            Assert.That(track.BorderBrush, Is.SameAs(StrongStrokeDisabled));
            Assert.That(track.BorderThickness, Is.EqualTo(new Thickness(1)));

            row.IsEnabled = true;
            Assert.That(track.Background, Is.SameAs(Accent));
            Assert.That(label.Foreground, Is.SameAs(Text));
            // Re-enabling restores the original zero-border look exactly.
            Assert.That(track.BorderThickness, Is.EqualTo(new Thickness(0)));
        }

        [TestCase(true, true, "AccentFillColorDefaultBrush", null, 0)]
        [TestCase(true, false, "AccentFillColorDisabledBrush", "ControlStrongStrokeColorDisabledBrush", 1)]
        [TestCase(false, true, null, "ControlStrongStrokeColorDefaultBrush", 1)]
        [TestCase(false, false, null, "ControlStrongStrokeColorDisabledBrush", 1)]
        public void TrackLook_PicksTheKeysForEachOnAndEnabledCombination(
            bool isOn, bool isEnabled, string fillKey, string borderKey, double borderThickness)
        {
            var look = SettingsCards.TrackLook(isOn, isEnabled);
            Assert.That(look.FillKey, Is.EqualTo(fillKey));
            Assert.That(look.BorderKey, Is.EqualTo(borderKey));
            Assert.That(look.BorderThickness, Is.EqualTo(new Thickness(borderThickness)));
        }
    }
}
