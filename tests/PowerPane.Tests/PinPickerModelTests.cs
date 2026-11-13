using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers the pure decisions of the "Add to PowerPane" picker. Groups are built in memory from
    /// invented paths, and the memory file lives in the test's own temporary folder.
    /// </summary>
    [TestFixture]
    public class PinPickerModelTests
    {
        private const string Root = @"C:\Demo\Groups";

        private static PinnedGroup Group(string name, params string[] tabs)
        {
            var folder = Root + @"\" + name;
            var g = new PinnedGroup { Name = name, FolderPath = folder };
            foreach (var t in tabs) g.Tabs.Add(new PinnedTab { Name = t, FolderPath = folder + @"\" + t });
            return g;
        }

        private static List<PinnedGroup> TwoGroups()
        {
            return new List<PinnedGroup>
            {
                Group("01_Apollo", "01_Main", "02_Notes"),
                Group("02_Zephyr", "01_Main", "02_Archive", "03_Ideas")
            };
        }

        // ---- DefaultChoice ----------------------------------------------------

        [Test]
        public void DefaultChoice_NoGroups_IsNewGroup()
        {
            var choice = PinPickerModel.DefaultChoice(new List<PinnedGroup>(), new PinMemory());
            Assert.That(choice.IsNewGroup, Is.True);
            Assert.That(choice.IsNewTab, Is.False);
            Assert.That(PinPickerModel.DefaultChoice(null, null).IsNewGroup, Is.True);
        }

        [Test]
        public void DefaultChoice_NothingRemembered_IsTheFirstGroupAndFirstTab()
        {
            foreach (var memory in new[] { null, new PinMemory() })
            {
                var choice = PinPickerModel.DefaultChoice(TwoGroups(), memory);
                Assert.That(choice.GroupIndex, Is.EqualTo(0));
                Assert.That(choice.TabIndex, Is.EqualTo(0));
            }
        }

        [Test]
        public void DefaultChoice_RememberedGroupAndTab_AreSelected()
        {
            var memory = new PinMemory { GroupFolder = Root + @"\02_Zephyr", TabFolder = Root + @"\02_Zephyr\03_Ideas" };
            var choice = PinPickerModel.DefaultChoice(TwoGroups(), memory);
            Assert.That(choice.GroupIndex, Is.EqualTo(1));
            Assert.That(choice.TabIndex, Is.EqualTo(2));
        }

        [Test]
        public void DefaultChoice_PathsMatchIgnoringCaseAndATrailingBackslash()
        {
            var memory = new PinMemory { GroupFolder = (Root + @"\02_zephyr\").ToLowerInvariant(), TabFolder = (Root + @"\02_Zephyr\02_Archive").ToUpperInvariant() };
            var choice = PinPickerModel.DefaultChoice(TwoGroups(), memory);
            Assert.That(choice.GroupIndex, Is.EqualTo(1));
            Assert.That(choice.TabIndex, Is.EqualTo(1));
        }

        [Test]
        public void DefaultChoice_RememberedGroupIsGone_FallsBackToTheFirstGroupAndTab()
        {
            var memory = new PinMemory { GroupFolder = Root + @"\03_Borealis", TabFolder = Root + @"\03_Borealis\01_Main" };
            var choice = PinPickerModel.DefaultChoice(TwoGroups(), memory);
            Assert.That(choice.GroupIndex, Is.EqualTo(0));
            Assert.That(choice.TabIndex, Is.EqualTo(0));
        }

        [Test]
        public void DefaultChoice_RememberedTabIsGone_KeepsTheGroupAndUsesItsFirstTab()
        {
            // The remembered tab no longer exists in the remembered group, such as one that was
            // deleted or whose folder was renamed.
            var memory = new PinMemory { GroupFolder = Root + @"\02_Zephyr", TabFolder = Root + @"\02_Zephyr\09_Removed" };
            var choice = PinPickerModel.DefaultChoice(TwoGroups(), memory);
            Assert.That(choice.GroupIndex, Is.EqualTo(1));
            Assert.That(choice.TabIndex, Is.EqualTo(0));
        }

        [Test]
        public void DefaultChoice_ATabFromAnotherGroup_IsNotUsed()
        {
            var memory = new PinMemory { GroupFolder = Root + @"\01_Apollo", TabFolder = Root + @"\02_Zephyr\03_Ideas" };
            var choice = PinPickerModel.DefaultChoice(TwoGroups(), memory);
            Assert.That(choice.GroupIndex, Is.EqualTo(0));
            Assert.That(choice.TabIndex, Is.EqualTo(0));
        }

        [Test]
        public void DefaultChoice_AGroupWithNoTabs_IsNewTab()
        {
            var groups = new List<PinnedGroup> { Group("01_Apollo") };
            var choice = PinPickerModel.DefaultChoice(groups, null);
            Assert.That(choice.GroupIndex, Is.EqualTo(0));
            Assert.That(choice.IsNewTab, Is.True);
        }

        // ---- ItemSummary ------------------------------------------------------

        [Test]
        public void ItemSummary_OneItem_IsItsFullPath()
        {
            Assert.That(PinPickerModel.ItemSummary(new[] { @"C:\Demo\Projects\Apollo" }), Is.EqualTo(@"C:\Demo\Projects\Apollo"));
        }

        [Test]
        public void ItemSummary_ThreeItems_NamesAllThree()
        {
            var paths = new[] { @"C:\Demo\Apollo", @"C:\Demo\Zephyr\notes.txt", @"D:\Demo\Borealis\" };
            Assert.That(PinPickerModel.ItemSummary(paths), Is.EqualTo("3 items: Apollo, notes.txt, Borealis"));
        }

        [Test]
        public void ItemSummary_TwelveItems_NamesThreeAndCountsTheRest()
        {
            var paths = Enumerable.Range(1, 12).Select(i => @"C:\Demo\Item" + i).ToArray();
            Assert.That(PinPickerModel.ItemSummary(paths), Is.EqualTo("12 items: Item1, Item2, Item3 and 9 more"));
        }

        [Test]
        public void ItemSummary_ADriveRoot_ShowsTheDrive()
        {
            Assert.That(PinPickerModel.ItemSummary(new[] { @"D:\", @"C:\Demo\Apollo" }), Is.EqualTo(@"2 items: D:\, Apollo"));
        }

        [Test]
        public void ItemSummary_NoItems_SaysSo()
        {
            Assert.That(PinPickerModel.ItemSummary(new string[0]), Is.EqualTo("No items"));
            Assert.That(PinPickerModel.ItemSummary(null), Is.EqualTo("No items"));
        }

        // ---- ResolveName ------------------------------------------------------

        [TestCase(null, "Group", "Group")]
        [TestCase("", "Group", "Group")]
        [TestCase("   ", "Tab", "Tab")]
        [TestCase("Apollo", "Group", "Apollo")]
        [TestCase("  Apollo notes  ", "Tab", "Apollo notes")]
        public void ResolveName_FallsBackOnlyWhenBlank(string typed, string fallback, string expected)
        {
            Assert.That(PinPickerModel.ResolveName(typed, fallback), Is.EqualTo(expected));
        }

        [Test]
        public void TheFallbackNames_AreTheOnesTheStoreActuallyUses()
        {
            using (var t = new TempDir())
            {
                var config = AppSettings.CreateDefault();
                config.GroupsPath = Path.Combine(t.Path, "Groups");
                Directory.CreateDirectory(config.GroupsPath);
                config.Profiles = new List<Profile>
                {
                    new Profile { Name = "Profile 1", GroupsPath = config.GroupsPath, RecentsMaxCount = 15, SidebarWidthPx = 220 }
                };
                using (var fs = File.Create(Path.Combine(t.Path, "settings.json")))
                    new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AppSettings)).WriteObject(fs, config);
                var settings = new PowerPane.Services.SettingsStore(t.Path);
                settings.Load();
                using (var store = new PowerPane.Services.GroupStore(settings, info => { }, action => action()))
                {
                    var group = store.CreateGroup("");
                    var tab = store.AddTab(group, "");

                    Assert.That(group.Name, Is.EqualTo(PinPickerModel.DefaultGroupName));
                    Assert.That(tab.Name, Is.EqualTo(PinPickerModel.DefaultTabName));
                }
            }
        }

        // ---- PinMemory --------------------------------------------------------

        [Test]
        public void PinMemory_SaveThenLoad_RoundTrips()
        {
            using (var t = new TempDir())
            {
                var file = Path.Combine(t.Path, "Sub", "pin-picker.txt");   // the folder does not exist yet
                var saved = new PinMemory { GroupFolder = Root + @"\01_Apollo", TabFolder = Root + @"\01_Apollo\02_Notes" };

                Assert.That(saved.Save(file), Is.True);

                var loaded = PinMemory.Load(file);
                Assert.That(loaded.GroupFolder, Is.EqualTo(saved.GroupFolder));
                Assert.That(loaded.TabFolder, Is.EqualTo(saved.TabFolder));
                Assert.That(File.ReadAllLines(file).Length, Is.EqualTo(2));
            }
        }

        [Test]
        public void PinMemory_MissingFile_IsEmpty()
        {
            using (var t = new TempDir())
            {
                var loaded = PinMemory.Load(Path.Combine(t.Path, "nothing.txt"));
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded.GroupFolder, Is.Null);
                Assert.That(loaded.TabFolder, Is.Null);
                Assert.That(PinMemory.Load(null).GroupFolder, Is.Null);
            }
        }

        [Test]
        public void PinMemory_GarbledFile_IsEmptyOrPartial_NeverThrows()
        {
            using (var t = new TempDir())
            {
                var file = Path.Combine(t.Path, "pin-picker.txt");

                File.WriteAllBytes(file, new byte[] { 0, 1, 2, 3, 0xFF, 0xFE, 0, 7 });
                var binary = PinMemory.Load(file);
                Assert.That(binary.GroupFolder, Is.Null);
                Assert.That(binary.TabFolder, Is.Null);

                File.WriteAllText(file, "");
                Assert.That(PinMemory.Load(file).GroupFolder, Is.Null);

                File.WriteAllText(file, Root + @"\01_Apollo");   // only the first line
                var oneLine = PinMemory.Load(file);
                Assert.That(oneLine.GroupFolder, Is.EqualTo(Root + @"\01_Apollo"));
                Assert.That(oneLine.TabFolder, Is.Null);
            }
        }

        [Test]
        public void PinMemory_SaveWithoutAGroup_WritesNothing()
        {
            using (var t = new TempDir())
            {
                var file = Path.Combine(t.Path, "pin-picker.txt");
                Assert.That(new PinMemory().Save(file), Is.False);
                Assert.That(File.Exists(file), Is.False);
            }
        }

        [Test]
        public void PinMemory_SaveToAnUnwritableLocation_ReturnsFalse()
        {
            using (var t = new TempDir())
            {
                // The target "file" is an existing folder, so the write fails.
                var asFolder = Path.Combine(t.Path, "pin-picker.txt");
                Directory.CreateDirectory(asFolder);
                Assert.That(new PinMemory { GroupFolder = Root + @"\01_Apollo" }.Save(asFolder), Is.False);
            }
        }
    }
}
