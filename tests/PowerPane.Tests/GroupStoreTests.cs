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
    /// Covers TriggersRescan, the filter that stops writes the groups scan never reads from setting off
    /// a full rescan (#49). Paths are invented; no folder is watched.
    /// </summary>
    [TestFixture]
    public class GroupStoreTests
    {
        private const string Tab = @"C:\Demo\Groups\01_Work\01_Projects";

        [Test]
        public void TriggersRescan_PinEditedInPlace_Rescans()
        {
            // A shortcut's target changed in its Properties dialog raises only a write-time change.
            Assert.That(GroupStore.TriggersRescan(WatcherChangeTypes.Changed, Tab + @"\001_Reports.lnk"), Is.True);
            Assert.That(GroupStore.TriggersRescan(WatcherChangeTypes.Changed, Tab + @"\002_Archive.LNK"), Is.True);
        }

        [Test]
        public void TriggersRescan_FolderWriteTime_Ignored()
        {
            // Fires whenever a file inside the tab is added or removed, which raises its own event.
            Assert.That(GroupStore.TriggersRescan(WatcherChangeTypes.Changed, Tab), Is.False);
        }

        [Test]
        public void TriggersRescan_OtherFileWritten_Ignored()
        {
            Assert.That(GroupStore.TriggersRescan(WatcherChangeTypes.Changed, Tab + @"\desktop.ini"), Is.False);
            Assert.That(GroupStore.TriggersRescan(WatcherChangeTypes.Changed, Tab + @"\Thumbs.db"), Is.False);
        }

        [Test]
        public void TriggersRescan_CreateDeleteRename_AlwaysRescan()
        {
            Assert.That(GroupStore.TriggersRescan(WatcherChangeTypes.Created, Tab + @"\003_New.lnk"), Is.True);
            Assert.That(GroupStore.TriggersRescan(WatcherChangeTypes.Deleted, Tab), Is.True);
            Assert.That(GroupStore.TriggersRescan(WatcherChangeTypes.Renamed, Tab + @"\desktop.ini"), Is.True);
        }

        [Test]
        public void TriggersRescan_NullPath_IgnoredForChanges()
        {
            Assert.That(GroupStore.TriggersRescan(WatcherChangeTypes.Changed, null), Is.False);
        }
    
        // ---- profile ownership (#68) -----------------------------------------
        // A profile switch points the store at another groups folder while panes may still hold groups
        // from the old one. The folders the store touches live in the test's own temp directory.

        private const string Root = @"C:\Demo\Profiles\Work\Groups";

        [TestCase(@"C:\Demo\Profiles\Work\Groups\01_Projects")]
        [TestCase(@"C:\Demo\Profiles\Work\Groups\01_Projects\01_Main\001_Reports.lnk")]
        [TestCase(@"c:\demo\profiles\work\groups\01_Projects")]
        public void BelongsTo_InsideTheRoot_IsTrue(string path)
        {
            Assert.That(GroupStore.BelongsTo(path, Root), Is.True);
            Assert.That(GroupStore.BelongsTo(path, Root + @"\"), Is.True);
        }

        [Test]
        public void BelongsTo_TheRootItself_IsTrue()
        {
            // The root can arrive with or without a trailing separator.
            Assert.That(GroupStore.BelongsTo(Root, Root + @"\"), Is.True);
            Assert.That(GroupStore.BelongsTo(Root + @"\", Root), Is.True);
        }

        [TestCase(@"C:\Demo\Profiles\Home\Groups\01_Projects")]
        [TestCase(@"C:\Demo\Profiles\Work\Groups2\01_Projects")]   // shares the root as a prefix
        [TestCase(@"C:\Demo\Profiles\Work")]
        [TestCase("")]
        [TestCase(null)]
        public void BelongsTo_OutsideTheRoot_IsFalse(string path)
        {
            Assert.That(GroupStore.BelongsTo(path, Root), Is.False);
        }

        [Test]
        public void BelongsTo_NoRoot_IsFalse()
        {
            Assert.That(GroupStore.BelongsTo(Root + @"\01_Projects", null), Is.False);
            Assert.That(GroupStore.BelongsTo(Root + @"\01_Projects", @"\"), Is.False);
        }

        [Test]
        public void SameRoot_IgnoresCaseAndTrailingSeparator()
        {
            Assert.That(GroupStore.SameRoot(Root, Root.ToUpperInvariant() + @"\"), Is.True);
            Assert.That(GroupStore.SameRoot(Root, @"C:\Demo\Profiles\Home\Groups"), Is.False);
            Assert.That(GroupStore.SameRoot(null, Root), Is.False);
        }

        private sealed class TwoProfiles : System.IDisposable
        {
            public readonly TempDir Temp = new TempDir();
            public readonly string RootA, RootB, GroupA;
            public readonly SettingsStore Settings;
            public readonly GroupStore Store;
            public readonly List<string> Notes = new List<string>();

            public TwoProfiles() : this(null) { }

            /// <summary>post is the seam PostEdit runs a posted edit through. A synchronous fake
            /// (action => action()) applies a posted edit at once; a queuing fake collects it so the
            /// test can run it after a profile switch (#75).</summary>
            public TwoProfiles(System.Action<System.Action> post)
            {
                RootA = Path.Combine(Temp.Path, "ProfileA");
                RootB = Path.Combine(Temp.Path, "ProfileB");
                GroupA = Path.Combine(RootA, "01_Alpha");
                Directory.CreateDirectory(Path.Combine(GroupA, "01_Main"));
                Directory.CreateDirectory(Path.Combine(GroupA, "02_Second"));
                Directory.CreateDirectory(Path.Combine(RootA, "02_Gamma", "01_Main"));
                Directory.CreateDirectory(Path.Combine(RootB, "01_Beta", "01_Main"));

                var config = AppSettings.CreateDefault();
                config.GroupsPath = RootA;
                config.Profiles = new List<Profile>
                {
                    new Profile { Name = "Profile 1", GroupsPath = RootA, RecentsMaxCount = 15, SidebarWidthPx = 220 }
                };
                using (var fs = File.Create(Path.Combine(Temp.Path, "settings.json")))
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(fs, config);
                Settings = new SettingsStore(Temp.Path);
                Settings.Load();
                Store = new GroupStore(Settings, Notes.Add, post);
            }

            public void SwitchToB() { Settings.Current.GroupsPath = RootB; }

            public string[] Tree()
            {
                return Directory.GetFileSystemEntries(Temp.Path, "*", SearchOption.AllDirectories)
                    .Where(p => !p.EndsWith("settings.json")).OrderBy(p => p).ToArray();
            }

            public void Dispose() { Store.Dispose(); Temp.Dispose(); }
        }

        [Test]
        public void ScanOnce_PublishesTheCurrentRootsGroups()
        {
            using (var f = new TwoProfiles())
            {
                Assert.That(f.Store.ScanOnce(), Is.True);
                Assert.That(f.Store.Groups.Select(g => g.Name), Is.EqualTo(new[] { "Alpha", "Gamma" }));

                f.SwitchToB();
                Assert.That(f.Store.ScanOnce(), Is.True);
                Assert.That(f.Store.Groups.Select(g => g.Name), Is.EqualTo(new[] { "Beta" }));
            }
        }

        [Test]
        public void EditsOnTheOldProfilesGroups_AfterASwitch_ChangeNothing()
        {
            // The reviewer's reproduction: AddTab on a group still showing from profile A created the
            // tab in A. ReorderGroups would also have moved A's group folders into B's root.
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var alpha = f.Store.Groups[0];
                var gamma = f.Store.Groups[1];
                var before = f.Tree();

                f.SwitchToB();
                Assert.That(f.Store.AddTab(alpha, "DuringReload"), Is.Null);
                f.Store.RenameTab(alpha.Tabs[0], "Renamed");
                f.Store.RenameGroup(alpha, "Renamed");
                f.Store.ReorderGroups(new List<PinnedGroup> { gamma, alpha });
                f.Store.ReorderTabs(alpha, new List<PinnedTab> { alpha.Tabs[1], alpha.Tabs[0] });
                f.Store.MoveTabToGroup(alpha.Tabs[1], gamma);
                f.Store.MergeGroups(gamma, alpha);
                f.Store.AddPin(alpha, f.Temp.Path);

                Assert.That(f.Tree(), Is.EqualTo(before));
                Assert.That(f.Notes.Count, Is.EqualTo(8));
            }
        }

        [Test]
        public void EditsOnTheCurrentProfilesGroups_StillApply()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var tab = f.Store.AddTab(f.Store.Groups[0], "Third");

                Assert.That(tab, Is.Not.Null);
                Assert.That(Directory.Exists(Path.Combine(f.GroupA, "03_Third")), Is.True);
                Assert.That(f.Notes, Is.Empty);
            }
        }
    
        [TestCase(@"C:\Demo\Profiles\Work\Groups\01_Projects", @"C:\Demo\Profiles\Work\Groups", true)]
        [TestCase(@"C:\Demo\Profiles\Work\Groups\01_Projects\", @"C:\Demo\Profiles\Work\Groups\", true)]
        [TestCase(@"D:\01_Projects", @"D:\", true)]
        [TestCase(@"C:\Demo\Profiles\Work\Groups\01_Projects\01_Main", @"C:\Demo\Profiles\Work\Groups", false)]
        [TestCase(@"C:\Demo\Profiles\Work\Groups", @"C:\Demo\Profiles\Work\Groups", false)]
        [TestCase(null, @"C:\Demo\Profiles\Work\Groups", false)]
        public void IsGroupIn_OnlyDirectChildren(string path, string root, bool expected)
        {
            // A group from a profile whose folder lies inside this one would pass BelongsTo, and a
            // reorder would move it here.
            Assert.That(GroupStore.IsGroupIn(path, root), Is.EqualTo(expected));
        }

        [Test]
        public void DropIfRootChanged_SameRoot_KeepsTheGroups()
        {
            // A reload of the same folder, such as after a pin's path is edited, must not blank the
            // Groups section for the length of the scan.
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                Assert.That(f.Store.DropIfRootChanged(), Is.False);
                Assert.That(f.Store.Groups.Count, Is.EqualTo(2));
            }
        }

        [Test]
        public void DropIfRootChanged_AfterASwitch_DropsTheOldGroups()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                f.SwitchToB();
                Assert.That(f.Store.DropIfRootChanged(), Is.True);
                Assert.That(f.Store.Groups, Is.Empty);
            }
        }

        [Test]
        public void Publish_ScanOfTheOldRoot_IsDiscarded()
        {
            // A scan of profile A that finishes after the switch to B.
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var shown = f.Store.Groups;
                f.SwitchToB();

                Assert.That(f.Store.Publish(f.RootA, new List<PinnedGroup>()), Is.False);
                Assert.That(f.Store.Groups, Is.SameAs(shown));
                Assert.That(f.Store.Publish(f.RootB, new List<PinnedGroup>()), Is.True);
                Assert.That(f.Store.Groups, Is.Empty);
            }
        }

        [Test]
        public void SeedDefaults_AfterASwitch_WritesNothing()
        {
            // The list is empty because it was dropped, not because profile B is empty.
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var before = f.Tree();
                f.SwitchToB();
                f.Store.DropIfRootChanged();

                f.Store.SeedDefaultsIfEmpty();

                Assert.That(f.Tree(), Is.EqualTo(before));
            }
        }

        [Test]
        public void IsCurrent_RefusesAPinFromTheOldProfile()
        {
            using (var f = new TwoProfiles())
            {
                var pin = Path.Combine(f.GroupA, "01_Main", "001_Reports.lnk");
                Assert.That(f.Store.IsCurrent(pin), Is.True);
                f.SwitchToB();
                Assert.That(f.Store.IsCurrent(pin), Is.False);
                Assert.That(f.Notes.Count, Is.EqualTo(1));
            }
        }

        // ---- PostEdit (#75) ---------------------------------------------------
        // PostEdit runs an edit through the post seam instead of in line, so a UI handler that
        // renames, reorders, or pins something never waits on the scan lock on the calling thread.

        [Test]
        public void PostEdit_SynchronousPost_AppliesTheEdit()
        {
            using (var f = new TwoProfiles(action => action()))
            {
                f.Store.ScanOnce();
                var gamma = f.Store.Groups[1]; // the single-tab group; DisplayName follows its tab

                f.Store.PostEdit(s => s.RenameGroup(gamma, "Renamed"));

                Assert.That(GroupStore.DisplayName(f.Store.Groups[1]), Is.EqualTo("Renamed"));
                Assert.That(f.Notes, Is.Empty);
            }
        }

        [Test]
        public void PostEdit_QueuedEditRunAfterASwitch_LeavesTheTreeUnchanged()
        {
            var queued = new List<System.Action>();
            using (var f = new TwoProfiles(queued.Add))
            {
                f.Store.ScanOnce();
                var alpha = f.Store.Groups[0];
                var before = f.Tree();

                // Root is captured when PostEdit is called, on this (the caller's) thread.
                f.Store.PostEdit(s => s.RenameGroup(alpha, "Renamed"));
                Assert.That(queued.Count, Is.EqualTo(1));

                f.SwitchToB();
                queued[0](); // the posted action, running as if on the worker after the switch

                Assert.That(f.Tree(), Is.EqualTo(before));
                Assert.That(f.Notes.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void MoveGroup_WithAGroupObjectFromBeforeARescan_StillMovesIt()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var alpha = f.Store.Groups[0]; // 01_Alpha

                // A watcher rescan between the click and this call replaces every group with a new
                // object for the same folder; nothing changed on disk.
                f.Store.ScanOnce();

                f.Store.MoveGroup(alpha, +1); // move the OLD alpha object past gamma

                f.Store.ScanOnce();
                Assert.That(f.Store.Groups.Select(g => g.Name), Is.EqualTo(new[] { "Gamma", "Alpha" }));
            }
        }

        [Test]
        public void MergeGroups_AlsoMovesATabThatArrivedAfterTheClick()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var gammaAtClick = f.Store.Groups[1];   // 02_Gamma, one tab
                var alpha = f.Store.Groups[0];          // 01_Alpha: Main, Second

                // An edit queued before the merge moves Alpha's "Second" tab into Gamma.
                f.Store.MoveTabToGroup(alpha.Tabs[1], gammaAtClick);

                // The merge still carries the one-tab Gamma the drag captured. Merging it into Alpha
                // must bring "Second" back too, never delete it with Gamma's folder.
                f.Store.MergeGroups(gammaAtClick, f.Store.Groups.First(g => g.Name == "Alpha"));

                f.Store.ScanOnce();
                Assert.That(f.Store.Groups.Select(g => g.Name), Is.EqualTo(new[] { "Alpha" }));
                Assert.That(f.Store.Groups[0].Tabs.Select(t => t.Name), Is.EquivalentTo(new[] { "Main", "Main", "Second" }));
                Assert.That(Directory.Exists(Path.Combine(f.RootA, "02_Gamma")), Is.False);
            }
        }

        // ---- AddPins into a chosen tab (#242) --------------------------------
        // The "Add to PowerPane" picker pins into the tab the user chose, not the group's active tab.
        // Targets are invented folders and files created inside the test's own temporary folder.

        private static string MakeFolder(TempDir temp, string name)
        {
            var dir = Path.Combine(temp.Path, "Targets", name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string MakeFile(TempDir temp, string name)
        {
            var dir = Path.Combine(temp.Path, "Targets");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, name);
            File.WriteAllText(file, "demo");
            return file;
        }

        [Test]
        public void AddPins_IntoTheSecondTabOfATwoTabGroup_LeavesTheFirstTabAlone()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var alpha = f.Store.Groups.First(g => g.Name == "Alpha");
                Assert.That(alpha.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "Main", "Second" }));
                Assert.That(alpha.ActiveTab, Is.EqualTo(0), "the group's visible tab is the first one");

                var apollo = MakeFolder(f.Temp, "Apollo");
                var zephyr = MakeFolder(f.Temp, "Zephyr");
                f.Store.AddPins(alpha.Tabs[1], new[] { apollo, zephyr });

                var after = f.Store.Groups.First(g => g.Name == "Alpha");
                Assert.That(after.Tabs[0].Items, Is.Empty);
                Assert.That(after.Tabs[1].Items.Select(i => Path.GetFileName(i.TargetPath)),
                    Is.EqualTo(new[] { "Apollo", "Zephyr" }));
                Assert.That(Directory.GetFiles(Path.Combine(f.GroupA, "01_Main"), "*.lnk"), Is.Empty);
            }
        }

        [Test]
        public void AddPins_AFileAndAFolderInOneCall_BothArePinnedInOrder()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var alpha = f.Store.Groups.First(g => g.Name == "Alpha");

                var folder = MakeFolder(f.Temp, "Borealis");
                var file = MakeFile(f.Temp, "notes.txt");
                f.Store.AddPins(alpha.Tabs[0], new[] { file, folder });

                var items = f.Store.Groups.First(g => g.Name == "Alpha").Tabs[0].Items;
                Assert.That(items.Select(i => Path.GetFileName(i.TargetPath)), Is.EqualTo(new[] { "notes.txt", "Borealis" }));
                Assert.That(Directory.GetFiles(Path.Combine(f.GroupA, "01_Main"), "*.lnk").Length, Is.EqualTo(2));
            }
        }

        [Test]
        public void AddPins_AppendsAfterTheExistingPins()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var tab = f.Store.Groups.First(g => g.Name == "Alpha").Tabs[1];
                var first = MakeFolder(f.Temp, "Apollo");
                var second = MakeFolder(f.Temp, "Zephyr");
                var third = MakeFile(f.Temp, "plan.txt");

                f.Store.AddPins(tab, new[] { first });
                tab = f.Store.Groups.First(g => g.Name == "Alpha").Tabs[1];
                f.Store.AddPins(tab, new[] { second, third });

                var items = f.Store.Groups.First(g => g.Name == "Alpha").Tabs[1].Items;
                Assert.That(items.Select(i => Path.GetFileName(i.TargetPath)), Is.EqualTo(new[] { "Apollo", "Zephyr", "plan.txt" }));
            }
        }

        [Test]
        public void AddTab_ThenAddPins_PinsIntoTheNewTab()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var alpha = f.Store.Groups.First(g => g.Name == "Alpha");

                var created = f.Store.AddTab(alpha, "Ideas");
                Assert.That(created, Is.Not.Null);

                var apollo = MakeFolder(f.Temp, "Apollo");
                var file = MakeFile(f.Temp, "sketch.txt");
                f.Store.AddPins(created, new[] { apollo, file });

                var after = f.Store.Groups.First(g => g.Name == "Alpha");
                Assert.That(after.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "Main", "Second", "Ideas" }));
                Assert.That(after.Tabs[2].Items.Select(i => Path.GetFileName(i.TargetPath)), Is.EqualTo(new[] { "Apollo", "sketch.txt" }));
                Assert.That(after.Tabs[0].Items, Is.Empty);
                Assert.That(after.Tabs[1].Items, Is.Empty);
            }
        }

        [Test]
        public void CreateGroup_ThenAddPinsToItsFirstTab_PinsIntoTheNewGroup()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var created = f.Store.CreateGroup("Borealis");
                Assert.That(created, Is.Not.Null);
                Assert.That(created.Tabs.Count, Is.EqualTo(1));

                var file = MakeFile(f.Temp, "brief.txt");
                f.Store.AddPins(created.Active, new[] { file });

                var after = f.Store.Groups.First(g => g.Name == "Borealis");
                Assert.That(after.Tabs[0].Items.Select(i => Path.GetFileName(i.TargetPath)), Is.EqualTo(new[] { "brief.txt" }));
            }
        }

        [Test]
        public void MoveTab_WithAGroupObjectFromBeforeATabWasAdded_KeepsEveryTab()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var alphaAtClick = f.Store.Groups[0];   // Main, Second
                var secondAtClick = alphaAtClick.Tabs[1];

                // An edit queued before the move adds a third tab.
                f.Store.AddTab(alphaAtClick, "Third");

                f.Store.MoveTab(alphaAtClick, secondAtClick, -1);

                f.Store.ScanOnce();
                Assert.That(f.Store.Groups[0].Tabs.Select(t => t.Name), Is.EqualTo(new[] { "Second", "Main", "Third" }));
            }
        }
    }
}
