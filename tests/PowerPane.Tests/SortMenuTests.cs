using System.Linq;
using System.Threading;
using System.Windows.Controls;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>The "Sort pins" submenu (#241): which choice carries the check mark, and what each
    /// choice does. Builds menu items only, no window.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class SortMenuTests
    {
        private static MenuItem Build(PinSortMode current, System.Action<PinSortMode> set = null, System.Action once = null)
        {
            return GroupSection.BuildSortMenu(current, set ?? (m => { }), once ?? (() => { }));
        }

        private static MenuItem[] Choices(MenuItem sort) { return sort.Items.OfType<MenuItem>().ToArray(); }

        private static void Click(MenuItem mi)
        {
            mi.RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent, mi));
        }

        [Test]
        public void Submenu_HasTheFourChoicesInOrder()
        {
            var sort = Build(PinSortMode.Manual);

            Assert.That(sort.Header, Is.EqualTo("Sort pins"));
            Assert.That(Choices(sort).Select(i => (string)i.Header), Is.EqualTo(new[]
            {
                "Manual order", "A to Z (keep sorted)", "Recently added (keep sorted)", "Sort A to Z once"
            }));
            Assert.That(sort.Items.OfType<Separator>().Count(), Is.EqualTo(1));
        }

        [TestCase(PinSortMode.Manual, 0)]
        [TestCase(PinSortMode.Name, 1)]
        [TestCase(PinSortMode.Recent, 2)]
        public void Submenu_ChecksOnlyTheActiveMode(PinSortMode current, int checkedIndex)
        {
            var items = Choices(Build(current));

            for (int i = 0; i < items.Length; i++)
            {
                if (i == checkedIndex)
                {
                    var icon = items[i].Icon as TextBlock;
                    Assert.That(icon, Is.Not.Null, "the active mode has a check");
                    Assert.That(icon.Text, Is.EqualTo("\uE73E"));
                }
                else
                {
                    Assert.That(items[i].Icon, Is.Null, items[i].Header + " has no icon, so its slot takes no width");
                }
            }
        }

        [TestCase(PinSortMode.Manual, 0)]
        [TestCase(PinSortMode.Name, 1)]
        [TestCase(PinSortMode.Recent, 2)]
        public void Submenu_ReportsTheActiveModeAsCheckedToAutomation(PinSortMode current, int checkedIndex)
        {
            var items = Choices(Build(current));

            for (int i = 0; i < 3; i++) Assert.That(items[i].IsChecked, Is.EqualTo(i == checkedIndex), items[i].Header.ToString());
            Assert.That(items[3].IsChecked, Is.False);
        }

        [Test]
        public void SortOnceItem_IsNeverChecked()
        {
            foreach (var mode in new[] { PinSortMode.Manual, PinSortMode.Name, PinSortMode.Recent })
                Assert.That(Choices(Build(mode)).Last().Icon, Is.Null);
        }

        [Test]
        public void Choices_CallTheMatchingAction()
        {
            var modes = new System.Collections.Generic.List<PinSortMode>();
            int once = 0;
            var items = Choices(Build(PinSortMode.Name, modes.Add, () => once++));

            Click(items[0]); Click(items[1]); Click(items[2]); Click(items[3]);

            Assert.That(modes, Is.EqualTo(new[] { PinSortMode.Manual, PinSortMode.Name, PinSortMode.Recent }));
            Assert.That(once, Is.EqualTo(1));
        }
    }
}
