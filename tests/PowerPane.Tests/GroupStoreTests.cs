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
        // Pins here are empty .lnk files: LoadPins reads their names and creation times, and an
        // empty file resolves to no target, which the scan accepts.

        private static string MainDir(TwoProfiles f) { return Path.Combine(f.GroupA, "01_Main"); }
        private static string SecondDir(TwoProfiles f) { return Path.Combine(f.GroupA, "02_Second"); }

        private static string Touch(string dir, string fileName, System.DateTime? createdUtc = null)
        {
            var path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, new byte[0]);
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
        public void ShellLinkResolveTarget_OnAnEmptyShortcutFile_DoesNotThrow()
        {
            using (var f = new TwoProfiles())
            {
                var path = Touch(MainDir(f), "001_Empty.lnk");
                string result = "unset";
                Assert.DoesNotThrow(() => result = ShellLink.ResolveTarget(path));
                Assert.That(string.IsNullOrEmpty(result), Is.True);
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
    }
}
