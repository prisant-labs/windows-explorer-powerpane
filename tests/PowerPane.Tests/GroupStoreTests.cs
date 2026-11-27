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
        public void CreateGroupAfter_AGroupFromTheOldProfile_CreatesNothing()
        {
            // The "New group below" prompt can stay open across a profile switch (#243).
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var alpha = f.Store.Groups[0];
                f.SwitchToB();
                f.Store.ScanOnce();
                var before = f.Tree();

                var created = f.Store.CreateGroupAfter(alpha, "Hermes");

                Assert.That(created, Is.Null);
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

        // ---- selecting the new tab (#253) -------------------------------------
        // "Add tab" from the pane's "+" menu asks the scan to select the tab it made. The paths in the
        // helper tests are invented; the store tests work in the test's own temp directory.

        private static readonly System.DateTime At = new System.DateTime(2026, 1, 1, 12, 0, 0, System.DateTimeKind.Utc);

        private static readonly string[] AlphaTabs =
        {
            @"C:\Demo\Groups\01_Alpha\01_Mira", @"C:\Demo\Groups\01_Alpha\02_Orion", @"C:\Demo\Groups\01_Alpha\03_Vega"
        };

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void PendingSelectionIndex_TabPresent_ReturnsItsIndex(int index)
        {
            var pending = new Dictionary<string, System.DateTime> { { AlphaTabs[index], At } };
            Assert.That(GroupStore.PendingSelectionIndex(AlphaTabs, pending), Is.EqualTo(index));
        }

        [Test]
        public void PendingSelectionIndex_PathCaseDiffers_StillMatches()
        {
            // Compared ignoring case, like the _activeTabs keys, whatever comparer the map was built with.
            var pending = new Dictionary<string, System.DateTime> { { AlphaTabs[1].ToUpperInvariant(), At } };
            Assert.That(GroupStore.PendingSelectionIndex(AlphaTabs, pending), Is.EqualTo(1));
        }

        [Test]
        public void PendingSelectionIndex_NoMatch_ReturnsMinusOne()
        {
            var other = new Dictionary<string, System.DateTime> { { @"C:\Demo\Groups\02_Gamma\01_Main", At } };
            Assert.That(GroupStore.PendingSelectionIndex(AlphaTabs, other), Is.EqualTo(-1));
            Assert.That(GroupStore.PendingSelectionIndex(AlphaTabs, new Dictionary<string, System.DateTime>()), Is.EqualTo(-1));
            Assert.That(GroupStore.PendingSelectionIndex(AlphaTabs, null), Is.EqualTo(-1));
        }

        [Test]
        public void PendingSelectionIndex_TwoRequestsInOneGroup_TheLaterWins()
        {
            // Two quick "Add tab" edits whose tabs one scan finds together.
            var pending = new Dictionary<string, System.DateTime>
            {
                { AlphaTabs[2], At.AddSeconds(1) },
                { AlphaTabs[1], At },
            };
            Assert.That(GroupStore.PendingSelectionIndex(AlphaTabs, pending), Is.EqualTo(2));
        }

        [Test]
        public void ExpirePending_DropsOnlyOldEntries()
        {
            var pending = new Dictionary<string, System.DateTime>
            {
                { AlphaTabs[0], At.AddSeconds(-9) },
                { AlphaTabs[1], At.AddSeconds(-11) },
                { AlphaTabs[2], At },
            };

            GroupStore.ExpirePending(pending, At);

            Assert.That(pending.Keys, Is.EquivalentTo(new[] { AlphaTabs[0], AlphaTabs[2] }));
        }

        [Test]
        public void AddTab_SelectTrue_NewTabIsActive()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var alpha = f.Store.Groups.First(g => g.Name == "Alpha");

                Assert.That(f.Store.AddTab(alpha, "Ideas", true), Is.Not.Null);

                var after = f.Store.Groups.First(g => g.Name == "Alpha");
                Assert.That(after.ActiveTab, Is.EqualTo(2));
                Assert.That(after.Active.Name, Is.EqualTo("Ideas"));
                Assert.That(f.Store.PendingSelectCount, Is.EqualTo(0));
            }
        }

        [Test]
        public void AddTab_SelectFalse_KeepsTheSelection()
        {
            // The pin picker's new tab, which pins into the tab it made and leaves the pane alone.
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var alpha = f.Store.Groups.First(g => g.Name == "Alpha");
                f.Store.SetActiveTab(alpha, 1);

                Assert.That(f.Store.AddTab(alpha, "Ideas"), Is.Not.Null);

                var after = f.Store.Groups.First(g => g.Name == "Alpha");
                Assert.That(after.Tabs.Count, Is.EqualTo(3));
                Assert.That(after.ActiveTab, Is.EqualTo(1));
                Assert.That(f.Store.PendingSelectCount, Is.EqualTo(0));
            }
        }

        [Test]
        public void AddTab_SelectTrue_ThenAddPin_LandsInNewTab()
        {
            // GroupAddMenu.Open aims "Add folder" at group.Active, read from the published groups.
            using (var f = new TwoProfiles(action => action()))
            {
                f.Store.ScanOnce();
                var alpha = f.Store.Groups.First(g => g.Name == "Alpha");
                f.Store.PostEdit(s => s.AddTab(alpha, "Ideas", select: true));

                var apollo = MakeFolder(f.Temp, "Apollo");
                var shown = f.Store.Groups.First(g => g.Name == "Alpha");
                f.Store.PostEdit(s => s.AddPins(shown.Active, new[] { apollo }));

                var after = f.Store.Groups.First(g => g.Name == "Alpha");
                Assert.That(after.Tabs[2].Items.Select(i => Path.GetFileName(i.TargetPath)), Is.EqualTo(new[] { "Apollo" }));
                Assert.That(after.Tabs[0].Items, Is.Empty);
                Assert.That(after.Tabs[1].Items, Is.Empty);
            }
        }

        [Test]
        public void AddTab_SelectTrue_ProfileSwitchedBeforeEdit_Refused()
        {
            var queued = new List<System.Action>();
            using (var f = new TwoProfiles(queued.Add))
            {
                f.Store.ScanOnce();
                var alpha = f.Store.Groups[0];
                var before = f.Tree();

                f.Store.PostEdit(s => s.AddTab(alpha, "Ideas", select: true));
                f.SwitchToB();
                queued[0]();
                // A direct call with a group from the old profile is refused the same way.
                Assert.That(f.Store.AddTab(alpha, "Ideas", true), Is.Null);

                Assert.That(f.Tree(), Is.EqualTo(before));
                Assert.That(f.Notes.Count, Is.EqualTo(2));
                Assert.That(f.Store.PendingSelectCount, Is.EqualTo(0));

                f.Store.ScanOnce();
                Assert.That(f.Store.Groups.Single().ActiveTab, Is.EqualTo(0));
            }
        }

        [Test]
        public void AddTab_SelectTrue_SingleTabGroup_SelectsSecondTab()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var gamma = f.Store.Groups.First(g => g.Name == "Gamma");
                Assert.That(gamma.Tabs.Count, Is.EqualTo(1));

                f.Store.AddTab(gamma, "Ideas", true);

                var after = f.Store.Groups.First(g => g.Name == "Gamma");
                Assert.That(after.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "Main", "Ideas" }));
                Assert.That(after.ActiveTab, Is.EqualTo(1));
            }
        }

        [Test]
        public void AddTab_SelectTrue_ImplicitTabGroup_LeavesOnlyTheNewTabSelected()
        {
            // A group folder with no tab folder shows one implicit tab pointing at the group folder.
            // The new tab's folder is then the group's only subfolder, so the implicit tab is gone and
            // the group has one tab, the new one, at index 0.
            using (var f = new TwoProfiles())
            {
                var delta = Path.Combine(f.RootA, "03_Delta");
                Directory.CreateDirectory(delta);
                f.Store.ScanOnce();
                var group = f.Store.Groups.First(g => g.Name == "Delta");
                Assert.That(group.Tabs.Single().FolderPath, Is.EqualTo(delta));

                var created = f.Store.AddTab(group, "Ideas", true);

                var after = f.Store.Groups.First(g => g.Name == "Delta");
                Assert.That(created, Is.Not.Null);
                Assert.That(after.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "Ideas" }));
                Assert.That(after.ActiveTab, Is.EqualTo(0));
                Assert.That(after.Active.FolderPath, Is.EqualTo(Path.Combine(delta, "01_Ideas")));
                Assert.That(f.Store.PendingSelectCount, Is.EqualTo(0));
            }
        }

        [Test]
        public void ScanOnce_AfterAConsumedSelection_KeepsTheNewIndex()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                f.Store.AddTab(f.Store.Groups[0], "Ideas", true);

                f.Store.ScanOnce();

                Assert.That(f.Store.PendingSelectCount, Is.EqualTo(0));
                Assert.That(f.Store.Groups[0].ActiveTab, Is.EqualTo(2));
            }
        }

        [Test]
        public void ScanOnce_NoRequest_RestoresTheSelectionAsBefore()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                f.Store.SetActiveTab(f.Store.Groups[0], 1);

                f.Store.ScanOnce();

                Assert.That(f.Store.Groups[0].ActiveTab, Is.EqualTo(1));
            }
        }

        [Test]
        public void ScanOnce_RequestForAFolderNotThereYet_WaitsForTheScanThatFindsIt()
        {
            // A watcher scan that runs before the new folder exists must not drop the request.
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var third = Path.Combine(f.GroupA, "03_Third");
                f.Store.RequestSelect(third, System.DateTime.MaxValue);   // never expires during the test

                f.Store.ScanOnce();
                Assert.That(f.Store.PendingSelectCount, Is.EqualTo(1));
                Assert.That(f.Store.Groups[0].ActiveTab, Is.EqualTo(0));

                Directory.CreateDirectory(third);
                f.Store.ScanOnce();
                Assert.That(f.Store.PendingSelectCount, Is.EqualTo(0));
                Assert.That(f.Store.Groups[0].ActiveTab, Is.EqualTo(2));
            }
        }

        [Test]
        public void ScanOnce_ExpiredRequest_IsDroppedAndSelectsNothing()
        {
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                f.Store.RequestSelect(Path.Combine(f.GroupA, "02_Second"), System.DateTime.MinValue);   // long expired

                f.Store.ScanOnce();

                Assert.That(f.Store.PendingSelectCount, Is.EqualTo(0));
                Assert.That(f.Store.Groups[0].ActiveTab, Is.EqualTo(0));
            }
        }

        [Test]
        public void RequestSelect_AfterAProfileSwitch_IsRefused()
        {
            // AddTab for profile A records its request only after creating the folder; a switch to B
            // in between must not leave A's folder waiting (#253 review).
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                f.SwitchToB();
                f.Store.RequestSelect(Path.Combine(f.GroupA, "02_Second"), System.DateTime.MaxValue);
                Assert.That(f.Store.PendingSelectCount, Is.EqualTo(0));
            }
        }

        [TestCase(@"C:\Demo\Groups\01_Alpha\02_Ideas", @"C:\Demo\Groups", true)]
        [TestCase(@"c:\demo\groups\01_Alpha", @"C:\Demo\Groups\", true)]
        [TestCase(@"C:\Demo\GroupsOther\01_Alpha", @"C:\Demo\Groups", false)]
        [TestCase(@"C:\Demo\Groups", @"C:\Demo\Groups", false)]
        [TestCase(null, @"C:\Demo\Groups", false)]
        [TestCase(@"C:\Demo\Groups\01_Alpha", null, false)]
        public void IsUnderRoot_MatchesOnlyFoldersInsideTheRoot(string path, string root, bool expected)
        {
            Assert.That(GroupStore.IsUnderRoot(path, root), Is.EqualTo(expected));
        }

        [Test]
        public void Publish_DiscardedScan_CommitsNoSelection()
        {
            // A scan of profile A that found a requested tab, finishing after the switch to B.
            using (var f = new TwoProfiles())
            {
                f.Store.ScanOnce();
                var second = Path.Combine(f.GroupA, "02_Second");
                f.Store.RequestSelect(second, System.DateTime.MaxValue);
                f.SwitchToB();

                var selections = new Dictionary<string, int> { { f.GroupA, 1 } };
                Assert.That(f.Store.Publish(f.RootA, new List<PinnedGroup>(), selections, new List<string> { second }), Is.False);
                Assert.That(f.Store.PendingSelectCount, Is.EqualTo(1));

                // Reload's drop clears the request, so nothing is left to select when A comes back.
                Assert.That(f.Store.DropIfRootChanged(), Is.True);
                Assert.That(f.Store.PendingSelectCount, Is.EqualTo(0));

                f.Settings.Current.GroupsPath = f.RootA;
                f.Store.ScanOnce();
                Assert.That(f.Store.Groups.First(g => g.Name == "Alpha").ActiveTab, Is.EqualTo(0));
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

        // ---- tab auto-sort (#241) ---------------------------------------------
        // Pins here are real shortcuts to a folder inside the test's own temp directory, so loading
        // one never fails and never writes to the log in the system temp folder.

        private static string MainDir(TwoProfiles f) { return Path.Combine(f.GroupA, "01_Main"); }
        private static string SecondDir(TwoProfiles f) { return Path.Combine(f.GroupA, "02_Second"); }

        private static string Touch(string dir, string fileName, System.DateTime? createdUtc = null)
        {
            var path = Path.Combine(dir, fileName);
            ShellLink.Create(path, Path.GetDirectoryName(dir));
            if (createdUtc.HasValue) File.SetCreationTimeUtc(path, createdUtc.Value);
            return path;
        }

        private static void Mark(string dir, string text)
        {
            File.WriteAllText(Path.Combine(dir, PinSorter.MarkerFileName), text);
        }

        private static string[] Names(PinnedTab tab) { return tab.Items.Select(p => p.DisplayName).ToArray(); }

        private static string[] FileNames(string dir)
        {
            return Directory.GetFiles(dir, "*.lnk").Select(Path.GetFileName).OrderBy(n => n).ToArray();
        }

        private static bool HasMarker(string dir) { return File.Exists(Path.Combine(dir, PinSorter.MarkerFileName)); }

        private static PinnedTab MainTab(TwoProfiles f) { f.Store.ScanOnce(); return f.Store.Groups[0].Tabs[0]; }

        [Test]
        public void Scan_WithoutAMarker_ShowsTheStoredOrder()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Zephyr.lnk");
                Touch(MainDir(f), "002_Item 10.lnk");
                Touch(MainDir(f), "003_Item 2.lnk");

                var tab = MainTab(f);

                Assert.That(tab.SortMode, Is.EqualTo(PinSortMode.Manual));
                Assert.That(Names(tab), Is.EqualTo(new[] { "Zephyr", "Item 10", "Item 2" }));
            }
        }

        [Test]
        public void Scan_NameMarker_ShowsNaturalCaseInsensitiveOrder()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Zephyr.lnk");
                Touch(MainDir(f), "002_Item 10.lnk");
                Touch(MainDir(f), "003_apollo.lnk");
                Touch(MainDir(f), "004_Item 2.lnk");
                Mark(MainDir(f), "name");

                var tab = MainTab(f);

                Assert.That(tab.SortMode, Is.EqualTo(PinSortMode.Name));
                Assert.That(Names(tab), Is.EqualTo(new[] { "apollo", "Item 2", "Item 10", "Zephyr" }));
                // The scan never rewrites the stored names.
                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001_Zephyr.lnk", "002_Item 10.lnk", "003_apollo.lnk", "004_Item 2.lnk" }));
            }
        }

        [Test]
        public void Scan_RecentMarker_ShowsNewestFirstByCreationTime()
        {
            using (var f = new TwoProfiles())
            {
                var t = new System.DateTime(2026, 3, 1, 0, 0, 0, System.DateTimeKind.Utc);
                Touch(MainDir(f), "001_Apollo.lnk", t.AddDays(2));
                Touch(MainDir(f), "002_Zephyr.lnk", t);
                Touch(MainDir(f), "003_Item 2.lnk", t.AddDays(5));
                Mark(MainDir(f), "recent");

                var tab = MainTab(f);

                Assert.That(tab.SortMode, Is.EqualTo(PinSortMode.Recent));
                Assert.That(Names(tab), Is.EqualTo(new[] { "Item 2", "Apollo", "Zephyr" }));
            }
        }

        [Test]
        public void Scan_UnknownMarker_ReadsAsManualAndDoesNotThrow()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Zephyr.lnk");
                Touch(MainDir(f), "002_Apollo.lnk");
                Mark(MainDir(f), "sideways");

                var tab = MainTab(f);

                Assert.That(tab.SortMode, Is.EqualTo(PinSortMode.Manual));
                Assert.That(Names(tab), Is.EqualTo(new[] { "Zephyr", "Apollo" }));
            }
        }

        [Test]
        public void CopyGroupFolder_KeepsEachShortcutsCreationTime_SoARecentTabKeepsItsOrder()
        {
            using (var f = new TwoProfiles())
            {
                var t = new System.DateTime(2026, 3, 1, 12, 0, 0, System.DateTimeKind.Utc);
                Touch(MainDir(f), "001_Apollo.lnk", t);
                Touch(MainDir(f), "002_Zephyr.lnk", t.AddDays(3));
                Mark(MainDir(f), "recent");
                var destRoot = Path.Combine(f.Temp.Path, "CopyTarget");

                GroupStore.CopyGroupFolder(f.GroupA, destRoot);

                var copied = Directory.GetDirectories(destRoot).Single();
                var main = Path.Combine(copied, "01_Main");
                Assert.That(File.GetCreationTimeUtc(Path.Combine(main, "001_Apollo.lnk")), Is.EqualTo(t));
                Assert.That(File.GetCreationTimeUtc(Path.Combine(main, "002_Zephyr.lnk")), Is.EqualTo(t.AddDays(3)));
                Assert.That(HasMarker(main), Is.True);
            }
        }

        [Test]
        public void Touch_CreatesAShortcutThatResolvesToItsTargetFolder()
        {
            using (var f = new TwoProfiles())
            {
                var path = Touch(MainDir(f), "001_Apollo.lnk");
                Assert.That(ShellLink.ResolveTarget(path), Is.EqualTo(Path.GetDirectoryName(MainDir(f))).IgnoreCase);
            }
        }

        [Test]
        public void InsertMovedPinAt_WhenTheRenameFails_PutsThePinBackUnderItsOwnName()
        {
            using (var f = new TwoProfiles())
            {
                var original = Touch(SecondDir(f), "001_Mango.lnk");
                var other = Touch(MainDir(f), "001_Apollo.lnk");
                var tmpNew = Path.Combine(MainDir(f), "~new~abc.lnk");
                File.Move(original, tmpNew);

                // An open handle with no sharing makes the rename of Apollo throw, as a lock would.
                using (new FileStream(other, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert.Throws<IOException>(() => GroupStore.InsertMovedPinAt(MainDir(f), tmpNew, original, "Mango", 1));
                }

                Assert.That(File.Exists(original), Is.True);
                Assert.That(File.Exists(tmpNew), Is.False);
                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001_Apollo.lnk" }));
            }
        }

        [Test]
        public void SetPinSort_Name_WritesTheMarkerAndLeavesFileNames()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Zephyr.lnk");
                Touch(MainDir(f), "002_Apollo.lnk");
                var tab = MainTab(f);

                f.Store.SetPinSort(tab, PinSortMode.Name);

                Assert.That(HasMarker(MainDir(f)), Is.True);
                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001_Zephyr.lnk", "002_Apollo.lnk" }));
                var after = f.Store.Groups[0].Tabs[0];
                Assert.That(after.SortMode, Is.EqualTo(PinSortMode.Name));
                Assert.That(Names(after), Is.EqualTo(new[] { "Apollo", "Zephyr" }));
            }
        }

        [Test]
        public void SetPinSort_Manual_FreezesTheDisplayedOrderAndRemovesTheMarker()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Zephyr.lnk");
                Touch(MainDir(f), "002_Item 10.lnk");
                Touch(MainDir(f), "003_Item 2.lnk");
                Mark(MainDir(f), "name");
                var tab = MainTab(f);

                f.Store.SetPinSort(tab, PinSortMode.Manual);

                Assert.That(HasMarker(MainDir(f)), Is.False);
                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001_Item 2.lnk", "002_Item 10.lnk", "003_Zephyr.lnk" }));
                var after = f.Store.Groups[0].Tabs[0];
                Assert.That(after.SortMode, Is.EqualTo(PinSortMode.Manual));
                Assert.That(Names(after), Is.EqualTo(new[] { "Item 2", "Item 10", "Zephyr" }));
            }
        }

        [Test]
        public void SetPinSort_Manual_OnAManualTab_ChangesNothing()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "005_Zephyr.lnk");
                Touch(MainDir(f), "009_Apollo.lnk");
                var tab = MainTab(f);

                f.Store.SetPinSort(tab, PinSortMode.Manual);

                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "005_Zephyr.lnk", "009_Apollo.lnk" }));
            }
        }

        [Test]
        public void ReorderPins_OnASortedTab_RemovesTheMarkerAndWritesTheOrderGiven()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Zephyr.lnk");
                Touch(MainDir(f), "002_Apollo.lnk");
                Mark(MainDir(f), "name");
                var tab = MainTab(f);
                var moved = tab.Items.ToList();   // on screen: Apollo, Zephyr
                moved.Reverse();                   // the person leaves Zephyr first

                f.Store.ReorderPins(tab, moved);

                Assert.That(HasMarker(MainDir(f)), Is.False);
                Assert.That(Names(f.Store.Groups[0].Tabs[0]), Is.EqualTo(new[] { "Zephyr", "Apollo" }));
                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001_Zephyr.lnk", "002_Apollo.lnk" }));
            }
        }

        [Test]
        public void MovePin_OnASortedTab_UsesTheDisplayedOrderAndLeavesTheTabManual()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Zephyr.lnk");
                Touch(MainDir(f), "002_Mango.lnk");
                Touch(MainDir(f), "003_Apollo.lnk");
                Mark(MainDir(f), "name");
                var tab = MainTab(f);   // on screen: Apollo, Mango, Zephyr

                f.Store.MovePin(tab, tab.Items[0], +1);   // Apollo down one

                Assert.That(HasMarker(MainDir(f)), Is.False);
                Assert.That(Names(f.Store.Groups[0].Tabs[0]), Is.EqualTo(new[] { "Mango", "Apollo", "Zephyr" }));
            }
        }

        [Test]
        public void SortPinsOnce_WritesAToZAndLeavesNoMarker()
        {
            using (var f = new TwoProfiles())
            {
                var t = new System.DateTime(2026, 3, 1, 0, 0, 0, System.DateTimeKind.Utc);
                Touch(MainDir(f), "001_Zephyr.lnk", t);
                Touch(MainDir(f), "002_Item 10.lnk", t.AddDays(1));
                Touch(MainDir(f), "003_Item 2.lnk", t.AddDays(2));
                Mark(MainDir(f), "recent");
                var tab = MainTab(f);

                f.Store.SortPinsOnce(tab);

                Assert.That(HasMarker(MainDir(f)), Is.False);
                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001_Item 2.lnk", "002_Item 10.lnk", "003_Zephyr.lnk" }));
                Assert.That(f.Store.Groups[0].Tabs[0].SortMode, Is.EqualTo(PinSortMode.Manual));
            }
        }

        [Test]
        public void SetPinSort_OnAnImplicitEmptyTab_CreatesTheTabFolderWithTheMarker()
        {
            using (var f = new TwoProfiles())
            {
                var empty = Path.Combine(f.RootA, "03_Empty");
                Directory.CreateDirectory(empty);
                f.Store.ScanOnce();
                var tab = f.Store.Groups.First(g => g.Name == "Empty").Tabs[0];
                Assert.That(tab.FolderPath, Is.EqualTo(empty));

                f.Store.SetPinSort(tab, PinSortMode.Name);

                var created = Path.Combine(empty, "01_Empty");
                Assert.That(HasMarker(created), Is.True);
                Assert.That(HasMarker(empty), Is.False);
                Assert.That(f.Store.Groups.First(g => g.Name == "Empty").Tabs[0].SortMode, Is.EqualTo(PinSortMode.Name));
            }
        }

        [Test]
        public void SetPinSort_Manual_OnAnImplicitEmptyTab_CreatesNothing()
        {
            using (var f = new TwoProfiles())
            {
                var empty = Path.Combine(f.RootA, "03_Empty");
                Directory.CreateDirectory(empty);
                f.Store.ScanOnce();
                var tab = f.Store.Groups.First(g => g.Name == "Empty").Tabs[0];
                var before = f.Tree();

                f.Store.SetPinSort(tab, PinSortMode.Manual);

                Assert.That(f.Tree(), Is.EqualTo(before));
            }
        }

        [Test]
        public void SortEdits_AfterAProfileSwitch_ChangeNothing()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Zephyr.lnk");
                Touch(MainDir(f), "002_Apollo.lnk");
                Mark(SecondDir(f), "name");
                f.Store.ScanOnce();
                var main = f.Store.Groups[0].Tabs[0];
                var second = f.Store.Groups[0].Tabs[1];
                var pin = main.Items[0];
                var before = f.Tree();

                f.SwitchToB();
                f.Store.SetPinSort(main, PinSortMode.Recent);
                f.Store.SetPinSort(second, PinSortMode.Manual);
                f.Store.SortPinsOnce(main);
                f.Store.MovePinToTabAt(pin, second, 0);

                Assert.That(f.Tree(), Is.EqualTo(before));
                Assert.That(f.Notes.Count, Is.EqualTo(4));
            }
        }

        [Test]
        public void MoveTabToGroup_KeepsTheSortMode()
        {
            using (var f = new TwoProfiles())
            {
                Mark(SecondDir(f), "recent");
                f.Store.ScanOnce();
                var second = f.Store.Groups[0].Tabs[1];
                var gamma = f.Store.Groups[1];

                f.Store.MoveTabToGroup(second, gamma);

                f.Store.ScanOnce();
                var moved = f.Store.Groups.First(g => g.Name == "Gamma").Tabs.First(t => t.Name == "Second");
                Assert.That(moved.SortMode, Is.EqualTo(PinSortMode.Recent));
            }
        }

        [Test]
        public void MovePinToTabAt_IntoASortedTab_InsertsAtTheLineInScreenOrderAndClearsTheMarker()
        {
            using (var f = new TwoProfiles())
            {
                // Stored order is Zephyr, Apollo; the sorted tab shows Apollo, Zephyr.
                Touch(MainDir(f), "001_Zephyr.lnk");
                Touch(MainDir(f), "002_Apollo.lnk");
                Mark(MainDir(f), "name");
                Touch(SecondDir(f), "001_Mango.lnk");
                f.Store.ScanOnce();
                var main = f.Store.Groups[0].Tabs[0];
                var mango = f.Store.Groups[0].Tabs[1].Items[0];

                f.Store.MovePinToTabAt(mango, main, 1);   // the line between Apollo and Zephyr

                Assert.That(HasMarker(MainDir(f)), Is.False);
                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001_Apollo.lnk", "002_Mango.lnk", "003_Zephyr.lnk" }));
                Assert.That(FileNames(SecondDir(f)), Is.Empty);
            }
        }

        [Test]
        public void MovePinToTab_IntoASortedTab_LandsSortedAndKeepsTheMarker()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Zephyr.lnk");
                Touch(MainDir(f), "002_Apollo.lnk");
                Mark(MainDir(f), "name");
                Touch(SecondDir(f), "001_Mango.lnk");
                f.Store.ScanOnce();
                var main = f.Store.Groups[0].Tabs[0];
                var mango = f.Store.Groups[0].Tabs[1].Items[0];

                f.Store.MovePinToTab(mango, main);

                Assert.That(HasMarker(MainDir(f)), Is.True);
                Assert.That(Names(f.Store.Groups[0].Tabs[0]), Is.EqualTo(new[] { "Apollo", "Mango", "Zephyr" }));
            }
        }

        [Test]
        public void AddPins_IntoASortedTab_LandsSortedAndKeepsTheMarker()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Zephyr.lnk");
                Mark(MainDir(f), "name");
                var tab = MainTab(f);
                var target = Path.Combine(f.Temp.Path, "Apollo");
                Directory.CreateDirectory(target);

                f.Store.AddPins(tab, new[] { target });

                Assert.That(HasMarker(MainDir(f)), Is.True);
                Assert.That(Names(f.Store.Groups[0].Tabs[0]), Is.EqualTo(new[] { "Apollo", "Zephyr" }));
            }
        }

        [Test]
        public void AddPinAtIndex_OnASortedTab_InsertsInScreenOrderAndClearsTheMarker()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Zephyr.lnk");
                Touch(MainDir(f), "002_Apollo.lnk");
                Mark(MainDir(f), "name");
                var tab = MainTab(f);   // on screen: Apollo, Zephyr
                var target = Path.Combine(f.Temp.Path, "Mango");
                Directory.CreateDirectory(target);

                f.Store.AddPinAtIndex(tab, target, 1);

                Assert.That(HasMarker(MainDir(f)), Is.False);
                Assert.That(Names(f.Store.Groups[0].Tabs[0]), Is.EqualTo(new[] { "Apollo", "Mango", "Zephyr" }));
            }
        }

        // ---- pin to top (#301) ------------------------------------------------

        private static bool[] Flags(PinnedTab tab) { return tab.Items.Select(p => p.PinnedToTop).ToArray(); }

        [TestCase("003!_Apollo", 3, true, "Apollo")]
        [TestCase("003_Apollo", 3, false, "Apollo")]
        [TestCase("003_!Apollo", 3, false, "!Apollo")]
        [TestCase("004!_!Apollo", 4, true, "!Apollo")]
        [TestCase("005_3D_Models", 5, false, "3D_Models")]
        [TestCase("Apollo", int.MaxValue, false, "Apollo")]
        [TestCase("12!Apollo", int.MaxValue, false, "12!Apollo")]
        public void SplitPin_ReadsTheOrderTheFlagAndTheName(string raw, int order, bool top, string name)
        {
            int o; bool t; string n;
            GroupStore.SplitPin(raw, out o, out t, out n);
            Assert.That(o, Is.EqualTo(order));
            Assert.That(t, Is.EqualTo(top));
            Assert.That(n, Is.EqualTo(name));
        }

        [TestCase(3, true, "Apollo", "003!_Apollo")]
        [TestCase(3, false, "Apollo", "003_Apollo")]
        [TestCase(12, true, "!Apollo", "012!_!Apollo")]
        public void PinStem_WritesWhatSplitPinReads(int order, bool top, string name, string expected)
        {
            var stem = GroupStore.PinStem(order, top, name);
            Assert.That(stem, Is.EqualTo(expected));
            int o; bool t; string n;
            GroupStore.SplitPin(stem, out o, out t, out n);
            Assert.That(new object[] { o, t, n }, Is.EqualTo(new object[] { order, top, name }));
        }

        [Test]
        public void Scan_AFlaggedFileReadsAsPinnedToTop_AndAnOldNameAsOrdinary()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Apollo.lnk");
                Touch(MainDir(f), "002!_Borealis.lnk");
                var tab = MainTab(f);

                Assert.That(Names(tab), Is.EqualTo(new[] { "Borealis", "Apollo" }));
                Assert.That(Flags(tab), Is.EqualTo(new[] { true, false }));
                Assert.That(tab.Items[0].Order, Is.EqualTo(2));
            }
        }

        [TestCase(null)]
        [TestCase("name")]
        [TestCase("recent")]
        public void SetPinTop_RenamesOnlyTheFlag_KeepsTheMarker_AndShowsThePinFirst(string marker)
        {
            using (var f = new TwoProfiles())
            {
                var t = new System.DateTime(2026, 3, 1, 0, 0, 0, System.DateTimeKind.Utc);
                Touch(MainDir(f), "001_Apollo.lnk", t.AddDays(2));
                Touch(MainDir(f), "002_Borealis.lnk", t.AddDays(1));
                Touch(MainDir(f), "003_Contoso.lnk", t);
                if (marker != null) Mark(MainDir(f), marker);
                var tab = MainTab(f);
                var contoso = tab.Items.Single(p => p.DisplayName == "Contoso");

                f.Store.SetPinTop(contoso, true);

                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001_Apollo.lnk", "002_Borealis.lnk", "003!_Contoso.lnk" }));
                Assert.That(HasMarker(MainDir(f)), Is.EqualTo(marker != null));
                var after = f.Store.Groups[0].Tabs[0];
                Assert.That(after.Items[0].DisplayName, Is.EqualTo("Contoso"));
                Assert.That(after.Items[0].PinnedToTop, Is.True);
            }
        }

        [Test]
        public void SetPinTop_Off_OnAManualTab_PutsThePinBackInItsStoredPlace()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Apollo.lnk");
                Touch(MainDir(f), "002!_Borealis.lnk");
                Touch(MainDir(f), "003_Contoso.lnk");
                var tab = MainTab(f);   // on screen: Borealis, Apollo, Contoso

                f.Store.SetPinTop(tab.Items[0], false);

                Assert.That(Names(f.Store.Groups[0].Tabs[0]), Is.EqualTo(new[] { "Apollo", "Borealis", "Contoso" }));
                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001_Apollo.lnk", "002_Borealis.lnk", "003_Contoso.lnk" }));
            }
        }

        [Test]
        public void ReorderPins_KeepsEveryFlagItIsGiven_AndWritesNoOther()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001!_Apollo.lnk");
                Touch(MainDir(f), "002_Borealis.lnk");
                Touch(MainDir(f), "003_Contoso.lnk");
                var tab = MainTab(f);
                var ordered = new List<PinnedFolder> { tab.Items[0], tab.Items[2], tab.Items[1] };

                f.Store.ReorderPins(tab, ordered);

                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001!_Apollo.lnk", "002_Contoso.lnk", "003_Borealis.lnk" }));
            }
        }

        [Test]
        public void ReorderPins_WithACopyCarryingANewFlag_WritesThatFlag()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001!_Apollo.lnk");
                Touch(MainDir(f), "002_Borealis.lnk");
                var tab = MainTab(f);   // on screen: Apollo*, Borealis
                var ordered = PinSorter.PlaceAtLineInBands(tab.Items, tab.Items[1], 0);   // Borealis into the band

                f.Store.ReorderPins(tab, ordered);

                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001!_Borealis.lnk", "002!_Apollo.lnk" }));
            }
        }

        [Test]
        public void SetPinSort_Manual_OnANameTab_FreezesThePinnedPinFirst()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Apollo.lnk");
                Touch(MainDir(f), "002!_Zephyr.lnk");
                Touch(MainDir(f), "003_Mango.lnk");
                Mark(MainDir(f), "name");
                var tab = MainTab(f);   // on screen: Zephyr*, Apollo, Mango

                f.Store.SetPinSort(tab, PinSortMode.Manual);

                Assert.That(HasMarker(MainDir(f)), Is.False);
                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001!_Zephyr.lnk", "002_Apollo.lnk", "003_Mango.lnk" }));
            }
        }

        [Test]
        public void SortPinsOnce_KeepsTheBandFirstAndSortsEachPartAToZ()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Zephyr.lnk");
                Touch(MainDir(f), "002!_Mango.lnk");
                Touch(MainDir(f), "003_Apollo.lnk");
                Touch(MainDir(f), "004!_Delta.lnk");
                var tab = MainTab(f);

                f.Store.SortPinsOnce(tab);

                Assert.That(Names(f.Store.Groups[0].Tabs[0]), Is.EqualTo(new[] { "Delta", "Mango", "Apollo", "Zephyr" }));
                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001!_Delta.lnk", "002!_Mango.lnk", "003_Apollo.lnk", "004_Zephyr.lnk" }));
            }
        }

        [Test]
        public void AddPinAtIndex_InsideTheBandIsPinned_BelowItIsNot_AndOtherFlagsSurvive()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001!_Apollo.lnk");
                Touch(MainDir(f), "002_Borealis.lnk");
                var mango = Path.Combine(f.Temp.Path, "Mango");
                var delta = Path.Combine(f.Temp.Path, "Delta");
                Directory.CreateDirectory(mango);
                Directory.CreateDirectory(delta);

                f.Store.AddPinAtIndex(MainTab(f), mango, 0);   // above Apollo, inside the band
                f.Store.AddPinAtIndex(MainTab(f), delta, 2);   // the edge between the band and Borealis

                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001!_Mango.lnk", "002!_Apollo.lnk", "003_Delta.lnk", "004_Borealis.lnk" }));
            }
        }

        [Test]
        public void MovePinToTab_ArrivesOrdinary()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Apollo.lnk");
                Touch(SecondDir(f), "001!_Mango.lnk");
                f.Store.ScanOnce();
                var main = f.Store.Groups[0].Tabs[0];
                var mango = f.Store.Groups[0].Tabs[1].Items[0];

                f.Store.MovePinToTab(mango, main);

                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001_Apollo.lnk", "002_Mango.lnk" }));
            }
        }

        [Test]
        public void MovePinToTabAt_IntoTheDestinationsBand_IsPinnedThere()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001!_Apollo.lnk");
                Touch(MainDir(f), "002_Borealis.lnk");
                Touch(SecondDir(f), "001_Mango.lnk");
                f.Store.ScanOnce();
                var main = f.Store.Groups[0].Tabs[0];
                var mango = f.Store.Groups[0].Tabs[1].Items[0];

                f.Store.MovePinToTabAt(mango, main, 0);

                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "001!_Mango.lnk", "002!_Apollo.lnk", "003_Borealis.lnk" }));
            }
        }

        [TestCase(null)]
        [TestCase("name")]
        [TestCase("recent")]
        public void AddPins_TheVerbPath_LandsBelowTheBandWithANumberAboveEveryPinnedPin(string marker)
        {
            using (var f = new TwoProfiles())
            {
                var t = new System.DateTime(2026, 3, 1, 0, 0, 0, System.DateTimeKind.Utc);
                Touch(MainDir(f), "001_Borealis.lnk", t);
                Touch(MainDir(f), "007!_Zephyr.lnk", t);
                if (marker != null) Mark(MainDir(f), marker);
                var target = Path.Combine(f.Temp.Path, "Apollo");
                Directory.CreateDirectory(target);

                f.Store.AddPins(MainTab(f), new[] { target });

                var after = f.Store.Groups[0].Tabs[0];
                Assert.That(after.Items[0].DisplayName, Is.EqualTo("Zephyr"));
                Assert.That(after.Items.Single(p => p.DisplayName == "Apollo").PinnedToTop, Is.False);
                Assert.That(FileNames(MainDir(f)), Does.Contain("008_Apollo.lnk"));
            }
        }

        [Test]
        public void MovePin_NeverCrossesTheEdgeOfTheBand()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001!_Apollo.lnk");
                Touch(MainDir(f), "002_Borealis.lnk");
                var before = FileNames(MainDir(f));
                var tab = MainTab(f);

                f.Store.MovePin(tab, tab.Items[1], -1);   // Move up on the first ordinary pin
                f.Store.MovePin(MainTab(f), MainTab(f).Items[0], +1);   // Move down on the last pinned pin

                Assert.That(FileNames(MainDir(f)), Is.EqualTo(before));
            }
        }

        [Test]
        public void RenamePin_KeepsTheFlag()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "004!_Apollo.lnk");
                var tab = MainTab(f);

                f.Store.RenamePin(tab.Items[0], "Apollo Reports");

                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "004!_Apollo Reports.lnk" }));
            }
        }

        [Test]
        public void CopyGroupFolder_KeepsTheFlags()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001_Apollo.lnk");
                Touch(MainDir(f), "002!_Borealis.lnk");
                var destRoot = Path.Combine(f.Temp.Path, "Copied");
                Directory.CreateDirectory(destRoot);

                GroupStore.CopyGroupFolder(f.GroupA, destRoot);

                var main = Path.Combine(Directory.GetDirectories(destRoot).Single(), "01_Main");
                Assert.That(FileNames(main), Is.EqualTo(new[] { "001_Apollo.lnk", "002!_Borealis.lnk" }));
            }
        }

        [Test]
        public void RemovePin_OfAPinnedPin_LeavesTheOthersAsTheyWere()
        {
            using (var f = new TwoProfiles())
            {
                Touch(MainDir(f), "001!_Apollo.lnk");
                Touch(MainDir(f), "002!_Borealis.lnk");
                Touch(MainDir(f), "003_Contoso.lnk");
                var tab = MainTab(f);

                f.Store.RemovePin(tab.Items[0]);

                Assert.That(FileNames(MainDir(f)), Is.EqualTo(new[] { "002!_Borealis.lnk", "003_Contoso.lnk" }));
            }
        }
    }
}
