using System.Linq;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>The words on the "+" menu, the Add entries of the right-click menus, and the prompt
    /// headings (#243). Pure strings: no window and no store.</summary>
    [TestFixture]
    public class AddMenuLabelsTests
    {
        private static PinnedTab Tab(string name) { return new PinnedTab { Name = name }; }

        private static PinnedGroup Group(string name, params string[] tabs)
        {
            var g = new PinnedGroup { Name = name };
            foreach (var t in tabs) g.Tabs.Add(Tab(t));
            return g;
        }

        private static string[] Texts(PinnedGroup g, PinnedTab target, AddKinds kinds)
        {
            return AddMenuLabels.Headers(g, target, kinds).Select(h => h.Text).ToArray();
        }

        [Test]
        public void SingleTabGroup_NamesTheGroupInEveryEntry()
        {
            var apollo = Group("Apollo", "Apollo");

            Assert.That(Texts(apollo, null, AddKinds.All), Is.EqualTo(new[]
            {
                "Add tab to \"Apollo\"...",
                "Add group below \"Apollo\"...",
                "Add folder to \"Apollo\"...",
                "Add file to \"Apollo\"..."
            }));
        }

        [Test]
        public void MultiTabGroup_NamesTheTabsForTabAndGroup_AndTheSelectedTabForFolderAndFile()
        {
            var zephyr = Group("Zephyr", "Mira", "Orion", "Vega");

            Assert.That(Texts(zephyr, zephyr.Tabs[1], AddKinds.All), Is.EqualTo(new[]
            {
                "Add tab to \"Mira / Orion / Vega\"...",
                "Add group below \"Mira / Orion / Vega\"...",
                "Add folder to tab \"Orion\"...",
                "Add file to tab \"Orion\"..."
            }));
        }

        [Test]
        public void NoTarget_UsesTheGroupsActiveTab()
        {
            var zephyr = Group("Zephyr", "Mira", "Orion", "Vega");
            zephyr.ActiveTab = 2;

            Assert.That(Texts(zephyr, null, AddKinds.Folder), Is.EqualTo(new[] { "Add folder to tab \"Vega\"..." }));
        }

        [Test]
        public void TabMenu_HasOnlyFolderAndFile_AimedAtTheRightClickedTab()
        {
            var zephyr = Group("Zephyr", "Mira", "Orion", "Vega");

            var headers = AddMenuLabels.Headers(zephyr, zephyr.Tabs[0], AddKinds.FolderAndFile);

            Assert.That(headers.Select(h => h.Kind), Is.EqualTo(new[] { AddKind.Folder, AddKind.File }));
            Assert.That(headers.Select(h => h.Text), Is.EqualTo(new[]
            {
                "Add folder to tab \"Mira\"...", "Add file to tab \"Mira\"..."
            }));
        }

        [Test]
        public void GroupMenuAndPlusMenu_ShareOneSetOfLabels()
        {
            // Both menus ask for AddKinds.All with the group's active tab, so one function feeds both.
            var zephyr = Group("Zephyr", "Mira", "Orion");
            zephyr.ActiveTab = 1;

            var forPlus = AddMenuLabels.Headers(zephyr, zephyr.Active, AddKinds.All);
            var forGroupMenu = AddMenuLabels.Headers(zephyr, zephyr.Tabs[1], AddKinds.All);

            Assert.That(forPlus.Select(h => h.Header), Is.EqualTo(forGroupMenu.Select(h => h.Header)));
            Assert.That(forPlus.Select(h => h.Kind), Is.EqualTo(new[]
            {
                AddKind.Tab, AddKind.GroupBelow, AddKind.Folder, AddKind.File
            }));
        }

        [Test]
        public void Underscore_IsDoubledInTheHeaderAndLeftAloneInTheText()
        {
            var g = Group("my_notes", "my_notes");

            var tab = AddMenuLabels.Headers(g, null, AddKinds.Tab).Single();

            Assert.That(tab.Header, Is.EqualTo("Add tab to \"my__notes\"..."));
            Assert.That(tab.Text, Is.EqualTo("Add tab to \"my_notes\"..."));
            Assert.That(AddMenuLabels.EscapeAccessKeys("my_notes"), Is.EqualTo("my__notes"));
        }

        [Test]
        public void UnderscoreInATabName_IsDoubledInTheFolderEntry()
        {
            var g = Group("Zephyr", "a_b", "c");

            var folder = AddMenuLabels.Headers(g, g.Tabs[0], AddKinds.Folder).Single();

            Assert.That(folder.Header, Is.EqualTo("Add folder to tab \"a__b\"..."));
        }

        [Test]
        public void LongName_IsShortenedInTheMiddleToTheLimit()
        {
            string name = new string('a', 30) + new string('z', 30);

            string shown = AddMenuLabels.Shorten(name, 32);

            Assert.That(shown.Length, Is.EqualTo(32));
            Assert.That(shown, Does.Contain("..."));
            Assert.That(shown, Does.StartWith("aaaa"));
            Assert.That(shown, Does.EndWith("zzzz"));
        }

        [Test]
        public void LongGroupName_IsShortenedInEntriesAndHeadings_ButTheGroupIsUnchanged()
        {
            string name = new string('a', 30) + new string('z', 30);
            var g = Group(name, name);

            Assert.That(AddMenuLabels.GroupLabel(g).Length, Is.EqualTo(32));
            Assert.That(AddMenuLabels.NewTabTitle(g), Is.EqualTo("New tab in \"" + AddMenuLabels.GroupLabel(g) + "\""));
            Assert.That(g.Name, Is.EqualTo(name));
        }

        [TestCase("short", 32, "short")]
        [TestCase("exactly-ten", 11, "exactly-ten")]
        [TestCase("", 32, "")]
        [TestCase(null, 32, "")]
        public void Shorten_LeavesShortAndEmptyNamesAlone(string input, int max, string expected)
        {
            Assert.That(AddMenuLabels.Shorten(input, max), Is.EqualTo(expected));
        }

        [Test]
        public void NullAndEmptyNames_DoNotThrow()
        {
            Assert.That(AddMenuLabels.EscapeAccessKeys(null), Is.EqualTo(string.Empty));
            Assert.That(AddMenuLabels.GroupLabel(null), Is.EqualTo(string.Empty));
            Assert.That(AddMenuLabels.Headers(null, null, AddKinds.All), Is.Empty);
            Assert.That(AddMenuLabels.TabTarget(Group("G", "A", "B"), Tab(null)), Is.EqualTo("\"A / B\""));
            Assert.That(AddMenuLabels.TabTarget(null, null), Is.EqualTo("\"\""));
        }

        [Test]
        public void GroupLabel_IsTheJoinedTabNames_NotTheGroupsOwnName()
        {
            // Walk D13: a multi-tab group's own name never shows in the pane, so it is not the label.
            Assert.That(AddMenuLabels.GroupLabel(Group("Zephyr", "Mira", "Orion")), Is.EqualTo("Mira / Orion"));
            Assert.That(AddMenuLabels.GroupLabel(Group("", "Mira", "Orion")), Is.EqualTo("Mira / Orion"));
            Assert.That(AddMenuLabels.GroupLabel(Group(null, "Apollo")), Is.EqualTo("Apollo"));
        }

        [Test]
        public void GroupWithBlankTabNames_FallsBackToItsOwnName()
        {
            Assert.That(AddMenuLabels.GroupLabel(Group("Zephyr", "", " ")), Is.EqualTo("Zephyr"));
            Assert.That(AddMenuLabels.GroupLabel(Group("Zephyr", new string[] { null })), Is.EqualTo("Zephyr"));
            Assert.That(AddMenuLabels.GroupLabel(Group("Zephyr")), Is.EqualTo("Zephyr"));
        }

        [Test]
        public void PlusName_AndPromptHeadings_NameTheTabs()
        {
            var zephyr = Group("Zephyr", "Mira", "Orion");

            Assert.That(AddMenuLabels.PlusName(zephyr), Is.EqualTo("Add to \"Mira / Orion\""));
            Assert.That(AddMenuLabels.NewTabTitle(zephyr), Is.EqualTo("New tab in \"Mira / Orion\""));
            Assert.That(AddMenuLabels.NewGroupBelowTitle(zephyr), Is.EqualTo("New group below \"Mira / Orion\""));
        }

        [Test]
        public void PlusName_IsDifferentForGroupsWithDifferentTabs_AndSameForGroupsWithTheSameTabNames()
        {
            Assert.That(AddMenuLabels.PlusName(Group("Apollo", "Mira", "Orion")),
                Is.EqualTo(AddMenuLabels.PlusName(Group("Zephyr", "Mira", "Orion"))));
            Assert.That(AddMenuLabels.PlusName(Group("Apollo", "Apollo")),
                Is.Not.EqualTo(AddMenuLabels.PlusName(Group("Apollo", "Mira", "Orion"))));
        }
    }
}
