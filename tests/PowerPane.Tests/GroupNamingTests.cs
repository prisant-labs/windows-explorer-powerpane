using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// What name a group and a tab end up with, which the "+" menu labels and the name prompt depend on
    /// (#243). Everything runs in the test's own temp folder with invented names.
    /// </summary>
    [TestFixture]
    public class GroupNamingTests
    {
        private sealed class Fixture : System.IDisposable
        {
            public readonly TempDir Temp = new TempDir();
            public readonly string Root;
            public readonly GroupStore Store;

            public Fixture()
            {
                Root = Path.Combine(Temp.Path, "Groups");
                Directory.CreateDirectory(Path.Combine(Root, "01_Apollo", "01_Apollo"));
                Directory.CreateDirectory(Path.Combine(Root, "02_Zephyr", "01_Mira"));
                Directory.CreateDirectory(Path.Combine(Root, "02_Zephyr", "02_Orion"));

                var config = AppSettings.CreateDefault();
                config.GroupsPath = Root;
                config.Profiles = new List<Profile>
                {
                    new Profile { Name = "Profile 1", GroupsPath = Root, RecentsMaxCount = 15, SidebarWidthPx = 220 }
                };
                using (var fs = File.Create(Path.Combine(Temp.Path, "settings.json")))
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(fs, config);
                var settings = new SettingsStore(Temp.Path);
                settings.Load();
                Store = new GroupStore(settings, s => { }, null);
                Store.ScanOnce();
            }

            public PinnedGroup Group(string name) { return Store.Groups.First(g => g.Name == name); }

            public void Dispose() { Store.Dispose(); Temp.Dispose(); }
        }

        [Test]
        public void AddTab_WithANameAnotherTabHas_StillCreatesASecondFolder()
        {
            // The store allows it: the tab folders differ by their order prefix. The name prompt is the
            // guard against a duplicate, through TabNameRules.
            using (var f = new Fixture())
            {
                var zephyr = f.Group("Zephyr");

                f.Store.AddTab(zephyr, "Mira");

                Assert.That(Directory.Exists(Path.Combine(f.Root, "02_Zephyr", "03_Mira")), Is.True);
                Assert.That(f.Group("Zephyr").Tabs.Select(t => t.Name), Is.EqualTo(new[] { "Mira", "Orion", "Mira" }));
            }
        }

        [Test]
        public void CreateGroup_NamesTheGroupAndItsFirstTabAlike()
        {
            using (var f = new Fixture())
            {
                var created = f.Store.CreateGroup("Hermes");

                Assert.That(created.Name, Is.EqualTo("Hermes"));
                Assert.That(created.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "Hermes" }));
            }
        }

        [Test]
        public void CreateGroup_WithABlankOrInvalidName_IsCalledGroup()
        {
            using (var f = new Fixture())
            {
                Assert.That(f.Store.CreateGroup("   ").Name, Is.EqualTo("Group"));
                Assert.That(f.Store.CreateGroup("?*").Name, Is.EqualTo("Group"));
            }
        }

        [Test]
        public void CreateGroup_RemovesInvalidCharactersFromTheName()
        {
            using (var f = new Fixture())
            {
                Assert.That(f.Store.CreateGroup("Hermes:2").Name, Is.EqualTo("Hermes2"));
            }
        }

        [Test]
        public void CreateGroupAfter_NamesTheNewGroupFromTheTypedName()
        {
            using (var f = new Fixture())
            {
                var created = f.Store.CreateGroupAfter(f.Group("Apollo"), "Hermes");

                Assert.That(created.Name, Is.EqualTo("Hermes"));
                Assert.That(f.Store.Groups.Select(g => g.Name), Is.EqualTo(new[] { "Apollo", "Hermes", "Zephyr" }));
            }
        }

        [Test]
        public void MoveTabToNewGroup_NamesTheNewGroupAfterTheTab_AndKeepsTheOldGroupsName()
        {
            using (var f = new Fixture())
            {
                var zephyr = f.Group("Zephyr");

                f.Store.MoveTabToNewGroup(zephyr, zephyr.Tabs[1]);

                Assert.That(f.Store.Groups.Select(g => g.Name), Is.EqualTo(new[] { "Apollo", "Zephyr", "Orion" }));
                Assert.That(f.Group("Zephyr").Tabs.Select(t => t.Name), Is.EqualTo(new[] { "Mira" }));
                // A group left with one tab keeps its own name, which no longer matches the tab shown.
                Assert.That(f.Group("Zephyr").Name, Is.Not.EqualTo(f.Group("Zephyr").Tabs[0].Name));
            }
        }

        [Test]
        public void MergeGroups_KeepsTheDestinationsName()
        {
            using (var f = new Fixture())
            {
                f.Store.MergeGroups(f.Group("Apollo"), f.Group("Zephyr"));

                Assert.That(f.Store.Groups.Select(g => g.Name), Is.EqualTo(new[] { "Zephyr" }));
                Assert.That(f.Group("Zephyr").Tabs.Select(t => t.Name), Is.EqualTo(new[] { "Mira", "Orion", "Apollo" }));
            }
        }

        [Test]
        public void RenamingTheOnlyTab_ThroughRenameGroup_RenamesTheGroupToo()
        {
            using (var f = new Fixture())
            {
                f.Store.RenameGroup(f.Group("Apollo"), "Artemis");

                var renamed = f.Store.Groups.First(g => g.Name == "Artemis");
                Assert.That(renamed.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "Artemis" }));
            }
        }
    }
}
