using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NUnit.Framework;
using PowerPane.UI.Settings;

namespace PowerPane.Tests
{
    /// <summary>The card builders take an optional accessible name, and AutomationProperties.Name
    /// throws on null. The Pane page builds its toggles unnamed and lets Card() and ChildRow() name
    /// them, so a null name must be skipped rather than set. Builds controls only; no window.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class SettingsCardsNamingTests
    {
        [Test]
        public void Toggle_WithoutName_DoesNotThrow()
        {
            var cards = new SettingsCards();
            Assert.DoesNotThrow(() => cards.Toggle(true, null));
        }

        [Test]
        public void ChildRow_NamesAnUnnamedToggle()
        {
            var cards = new SettingsCards();
            var toggle = cards.Toggle(false, null);
            cards.ChildRow("Auto-hide (slide out on hover)", toggle, "Auto-hide the desktop dock");
            var button = (ToggleButton)toggle.Tag;
            Assert.That(AutomationProperties.GetName(button), Is.EqualTo("Auto-hide the desktop dock"));
        }

        [Test]
        public void ChildRow_WithNoLabelOrName_DoesNotThrow()
        {
            var cards = new SettingsCards();
            Assert.DoesNotThrow(() => cards.ChildRow(null, cards.Toggle(false, null)));
        }

        // ---- LinkEnabled (#150) --------------------------------------------

        [Test]
        public void LinkEnabled_ParentOff_StartsDependentRowsDisabled()
        {
            var cards = new SettingsCards();
            var parent = cards.Toggle(false, null);
            var row = cards.ChildRow("Auto-hide", cards.Toggle(true, null));

            cards.LinkEnabled(parent, row);

            Assert.That(row.IsEnabled, Is.False);
        }

        [Test]
        public void LinkEnabled_ParentOn_StartsDependentRowsEnabled()
        {
            var cards = new SettingsCards();
            var parent = cards.Toggle(true, null);
            var row = cards.ChildRow("Auto-hide", cards.Toggle(true, null));

            cards.LinkEnabled(parent, row);

            Assert.That(row.IsEnabled, Is.True);
        }

        [Test]
        public void LinkEnabled_TogglingParent_FlipsEveryDependentRow()
        {
            var cards = new SettingsCards();
            var parent = cards.Toggle(true, null);
            var rowA = cards.ChildRow("Auto-hide", cards.Toggle(true, null));
            var rowB = cards.ChildRow("Show on all virtual desktops", cards.Toggle(false, null));

            cards.LinkEnabled(parent, rowA, rowB);
            var button = (ToggleButton)parent.Tag;

            button.IsChecked = false;
            Assert.That(rowA.IsEnabled, Is.False);
            Assert.That(rowB.IsEnabled, Is.False);

            button.IsChecked = true;
            Assert.That(rowA.IsEnabled, Is.True);
            Assert.That(rowB.IsEnabled, Is.True);
        }

        [Test]
        public void LinkEnabled_DoesNotChangeTheStoredToggleValues()
        {
            // Greying a dependent row must never touch what it stores (#150) -- only whether it can
            // be interacted with right now.
            var cards = new SettingsCards();
            var parent = cards.Toggle(false, null);
            bool? stored = null;
            var dependentToggle = cards.Toggle(true, on => stored = on);
            var row = cards.ChildRow("Auto-hide", dependentToggle);

            cards.LinkEnabled(parent, row);

            Assert.That(stored, Is.Null, "LinkEnabled must not fire the dependent toggle's own onChanged");
            Assert.That(((ToggleButton)dependentToggle.Tag).IsChecked, Is.True);
        }

        [Test]
        public void LinkEnabled_NotAToggle_DoesNotThrow()
        {
            var cards = new SettingsCards();
            var row = cards.ChildRow("Auto-hide", cards.Toggle(true, null));
            Assert.DoesNotThrow(() => cards.LinkEnabled(new FrameworkElement(), row));
        }

        [Test]
        public void LinkEnabled_NoRows_DoesNotThrow()
        {
            var cards = new SettingsCards();
            var parent = cards.Toggle(true, null);
            Assert.DoesNotThrow(() => cards.LinkEnabled(parent));
        }
    }
}
