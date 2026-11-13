using System.Linq;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Controls;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>The Add entries shared by the "+" menu and the right-click menus (#243). Builds menu
    /// items only: no window is shown, no prompt opens, and no entry is clicked.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class GroupAddMenuTests
    {
        private static PinnedGroup Zephyr()
        {
            var g = new PinnedGroup { Name = "Zephyr", ActiveTab = 1 };
            foreach (var n in new[] { "Mira", "Orion", "Vega" }) g.Tabs.Add(new PinnedTab { Name = n });
            return g;
        }

        private static System.Func<PromptAnchor?> NoAnchor { get { return () => null; } }

        private static string[] Headers(ItemsControl menu)
        {
            return menu.Items.OfType<MenuItem>().Select(i => (string)i.Header).ToArray();
        }

        [Test]
        public void PlusMenu_HasFourEntriesInOrder_WithASeparatorBeforeFolderAndFile()
        {
            var g = Zephyr();

            var menu = GroupAddMenu.BuildMenu(g, g.Active, NoAnchor);

            Assert.That(menu.Items.Count, Is.EqualTo(5));
            Assert.That(menu.Items[2], Is.TypeOf<Separator>());
            Assert.That(Headers(menu), Is.EqualTo(new[]
            {
                "Add tab to \"Zephyr\"...",
                "Add group below \"Zephyr\"...",
                "Add folder to tab \"Orion\"...",
                "Add file to tab \"Orion\"..."
            }));
        }

        [Test]
        public void GroupRightClickMenu_UsesTheSameEntriesAsThePlusMenu()
        {
            var g = Zephyr();
            var plus = GroupAddMenu.BuildMenu(g, g.Active, NoAnchor);

            // The group menu puts Rename and a separator first, then asks for the same four entries.
            var group = new ContextMenu();
            group.Items.Add(new MenuItem { Header = "Rename" });
            group.Items.Add(new Separator());
            GroupAddMenu.AddEntries(group, g, g.Active, AddKinds.All, NoAnchor);

            Assert.That(Headers(group).Skip(1), Is.EqualTo(Headers(plus)));
        }

        [Test]
        public void TabRightClickMenu_HasOnlyFolderAndFile_AimedAtTheClickedTab()
        {
            var g = Zephyr();
            var menu = new ContextMenu();

            GroupAddMenu.AddEntries(menu, g, g.Tabs[0], AddKinds.FolderAndFile, NoAnchor);

            Assert.That(menu.Items.OfType<Separator>(), Is.Empty);
            Assert.That(Headers(menu), Is.EqualTo(new[]
            {
                "Add folder to tab \"Mira\"...", "Add file to tab \"Mira\"..."
            }));
        }

        [Test]
        public void SingleTabGroup_NamesTheGroupInFolderAndFile()
        {
            var g = new PinnedGroup { Name = "Apollo" };
            g.Tabs.Add(new PinnedTab { Name = "Apollo" });

            var menu = GroupAddMenu.BuildMenu(g, g.Active, NoAnchor);

            Assert.That(Headers(menu).Skip(2), Is.EqualTo(new[]
            {
                "Add folder to \"Apollo\"...", "Add file to \"Apollo\"..."
            }));
        }

        [Test]
        public void EveryEntry_HasAnAutomationNameEqualToItsText()
        {
            var g = new PinnedGroup { Name = "my_notes" };
            g.Tabs.Add(new PinnedTab { Name = "my_notes" });

            var items = GroupAddMenu.BuildMenu(g, g.Active, NoAnchor).Items.OfType<MenuItem>().ToArray();

            Assert.That(items.Select(i => AutomationProperties.GetName(i)), Is.EqualTo(new[]
            {
                "Add tab to \"my_notes\"...",
                "Add group below \"my_notes\"...",
                "Add folder to \"my_notes\"...",
                "Add file to \"my_notes\"..."
            }));
            // The header carries the doubled underscore so WPF shows one.
            Assert.That((string)items[0].Header, Is.EqualTo("Add tab to \"my__notes\"..."));
        }

        [TestCase(false, false, false, 0.0)]
        [TestCase(true, false, false, 1.0)]
        [TestCase(false, true, false, 1.0)]
        [TestCase(false, false, true, 1.0)]
        [TestCase(true, true, true, 1.0)]
        public void PlusOpacity_IsHiddenOnlyWhenNothingKeepsItUp(bool hovered, bool focused, bool menuOpen, double expected)
        {
            Assert.That(GroupAddMenu.PlusOpacity(hovered, focused, menuOpen), Is.EqualTo(expected));
        }
    }
}
