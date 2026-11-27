using System;
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
    /// Duplicating a profile (#300): the name offered, the refused name, the whole-folder copy and
    /// the new profile's values. Every file stays in a temporary folder, and no test copies into the
    /// real profiles folder.
    /// </summary>
    [TestFixture]
    public class DuplicateProfileTests
    {
        // ---- the name offered and the name refused ------------------------------

        [Test]
        public void CopyName_OffersCopy_ThenNumbersIt_IgnoringCase()
        {
            Assert.That(SettingsStore.CopyName("Work", new[] { "Work", "Home" }), Is.EqualTo("Work copy"));
            Assert.That(SettingsStore.CopyName("Work", new[] { "Work", "work COPY" }), Is.EqualTo("Work copy 2"));
            Assert.That(SettingsStore.CopyName("Work", new[] { "Work", "Work copy", "Work copy 2" }), Is.EqualTo("Work copy 3"));
            Assert.That(SettingsStore.CopyName("  Work ", null), Is.EqualTo("Work copy"));
        }

        [Test]
        public void ProfileNameTaken_MatchesIgnoringCaseAndOuterSpaces()
        {
            var profiles = new List<Profile> { new Profile { Name = "Work" }, new Profile { Name = "Home" } };
            Assert.That(SettingsStore.ProfileNameTaken(" work ", profiles), Is.True);
            Assert.That(SettingsStore.ProfileNameTaken("Work copy", profiles), Is.False);
            Assert.That(SettingsStore.ProfileNameTaken("", profiles), Is.False);
        }

        // ---- the whole-folder copy ---------------------------------------------

        private static string Shortcut(string dir, string name, DateTime created)
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name);
            ShellLink.Create(path, dir);
            File.SetCreationTimeUtc(path, created);
            return path;
        }

        private static string[] Tree(string root)
        {
            return Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .Select(p => p.Substring(root.Length)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        [Test]
        public void CopyGroupsRoot_CopiesEveryGroupTabPinAndMarker_AndKeepsTheSource()
        {
            using (var temp = new TempDir())
            {
                var src = Path.Combine(temp.Path, "Work", "Groups");
                var t = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
                Shortcut(Path.Combine(src, "01_Projects", "01_Apollo"), "001_Reports.lnk", t);
                Shortcut(Path.Combine(src, "01_Projects", "02_Borealis"), "002!_Plans.lnk", t.AddDays(2));
                File.WriteAllText(Path.Combine(src, "01_Projects", "02_Borealis", PinSorter.MarkerFileName), "recent");
                Shortcut(Path.Combine(src, "02_Reference"), "001_Contoso.lnk", t);
                Directory.CreateDirectory(Path.Combine(src, "03_Archive"));
                var before = Tree(src);
                var dst = Path.Combine(temp.Path, "Work copy", "Groups");

                Assert.That(GroupStore.CopyGroupsRoot(src, dst), Is.True);

                Assert.That(Tree(dst), Is.EqualTo(before));
                Assert.That(Tree(src), Is.EqualTo(before));
                Assert.That(File.GetCreationTimeUtc(Path.Combine(dst, "01_Projects", "02_Borealis", "002!_Plans.lnk")), Is.EqualTo(t.AddDays(2)));
                Assert.That(File.ReadAllText(Path.Combine(dst, "01_Projects", "02_Borealis", PinSorter.MarkerFileName)), Is.EqualTo("recent"));
            }
        }

        [Test]
        public void CopyGroupsRoot_ADestinationThatExists_IsRefusedAndLeftAlone()
        {
            using (var temp = new TempDir())
            {
                var src = Path.Combine(temp.Path, "Work", "Groups");
                Shortcut(Path.Combine(src, "01_Projects"), "001_Apollo.lnk", DateTime.UtcNow);
                var dst = Path.Combine(temp.Path, "Old", "Groups");
                Shortcut(Path.Combine(dst, "01_Leftover"), "001_Mango.lnk", DateTime.UtcNow);
                var before = Tree(dst);

                Assert.That(GroupStore.CopyGroupsRoot(src, dst), Is.False);
                Assert.That(Tree(dst), Is.EqualTo(before));
            }
        }

        [Test]
        public void CopyGroupsRoot_ADestinationInsideTheSource_IsRefusedWithoutRecursing()
        {
            using (var temp = new TempDir())
            {
                var src = Path.Combine(temp.Path, "Groups");
                Shortcut(Path.Combine(src, "01_Projects"), "001_Apollo.lnk", DateTime.UtcNow);
                var before = Tree(src);

                Assert.That(GroupStore.CopyGroupsRoot(src, Path.Combine(src, "Inner")), Is.False);
                Assert.That(GroupStore.CopyGroupsRoot(src, src + @"\"), Is.False);
                Assert.That(Tree(src), Is.EqualTo(before));
            }
        }

        [Test]
        public void CopyGroupsRoot_ASiblingThatSharesAPrefix_IsAllowed()
        {
            using (var temp = new TempDir())
            {
                var src = Path.Combine(temp.Path, "Groups");
                Shortcut(Path.Combine(src, "01_Projects"), "001_Apollo.lnk", DateTime.UtcNow);

                Assert.That(GroupStore.CopyGroupsRoot(src, Path.Combine(temp.Path, "Groups2")), Is.True);
                Assert.That(File.Exists(Path.Combine(temp.Path, "Groups2", "01_Projects", "001_Apollo.lnk")), Is.True);
            }
        }

        [Test]
        public void CopyGroupsRoot_AMissingSource_MakesAnEmptyProfile()
        {
            using (var temp = new TempDir())
            {
                var dst = Path.Combine(temp.Path, "Copy", "Groups");
                Assert.That(GroupStore.CopyGroupsRoot(Path.Combine(temp.Path, "Never"), dst), Is.True);
                Assert.That(Directory.Exists(dst), Is.True);
                Assert.That(Directory.GetFileSystemEntries(dst), Is.Empty);
            }
        }

        [Test]
        public void CopyGroupsRoot_AFailurePartWay_RemovesThePartialCopy()
        {
            using (var temp = new TempDir())
            {
                var src = Path.Combine(temp.Path, "Work", "Groups");
                Shortcut(Path.Combine(src, "01_Projects"), "001_Apollo.lnk", DateTime.UtcNow);
                var locked = Shortcut(Path.Combine(src, "02_Reference"), "001_Contoso.lnk", DateTime.UtcNow);
                var dst = Path.Combine(temp.Path, "Copy", "Groups");

                // An open handle that shares nothing makes the copy of this one file fail.
                using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert.That(GroupStore.CopyGroupsRoot(src, dst), Is.False);
                }

                Assert.That(Directory.Exists(dst), Is.False);
                Assert.That(Directory.GetFileSystemEntries(Path.Combine(temp.Path, "Copy")), Is.Empty, "no staging folder is left");
                Assert.That(File.Exists(locked), Is.True);
            }
        }

        [Test]
        public void CopyGroupsRoot_ASourceThatIsAFile_IsRefused()
        {
            using (var temp = new TempDir())
            {
                var src = Path.Combine(temp.Path, "Groups");
                File.WriteAllText(src, "not a folder");
                var dst = Path.Combine(temp.Path, "Copy", "Groups");

                Assert.That(GroupStore.CopyGroupsRoot(src, dst), Is.False);
                Assert.That(Directory.Exists(dst), Is.False);
            }
        }

        [TestCase(@"C:\Demo\Groups", @"C:\Demo\Groups", true)]
        [TestCase(@"c:\demo\groups\", @"C:\Demo\Groups", true)]
        [TestCase(@"C:\Demo\Groups\Inner", @"C:\Demo\Groups\", true)]
        [TestCase(@"C:\Demo\Groups2", @"C:\Demo\Groups", false)]
        [TestCase(@"C:\Demo", @"C:\Demo\Groups", false)]
        [TestCase("", @"C:\Demo\Groups", false)]
        public void IsInsideOrSame_ComparesOnSeparatorBoundaries(string candidate, string root, bool expected)
        {
            Assert.That(GroupStore.IsInsideOrSame(candidate, root), Is.EqualTo(expected));
        }

        // ---- the new profile ---------------------------------------------------

        private static SettingsStore TwoProfileStore(TempDir temp, int active = 0)
        {
            var config = AppSettings.CreateDefault();
            config.Profiles = new List<Profile>
            {
                new Profile { Name = "Work", GroupsPath = Path.Combine(temp.Path, "Work"), RecentsMaxCount = 25, SidebarWidthPx = 300, HiddenFromRow = true },
                new Profile { Name = "Home", GroupsPath = Path.Combine(temp.Path, "Home"), RecentsMaxCount = 10, SidebarWidthPx = 200 }
            };
            config.ActiveProfileIndex = active;
            var a = config.Profiles[active];
            config.GroupsPath = a.GroupsPath;
            config.RecentsMaxCount = a.RecentsMaxCount;
            config.SidebarWidthPx = a.SidebarWidthPx;
            using (var fs = File.Create(Path.Combine(temp.Path, "settings.json")))
                new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(fs, config);
            var store = new SettingsStore(temp.Path) { ProfileFolderExists = path => false };
            store.Load();
            return store;
        }

        private const string CopyPath = @"%APPDATA%\PowerPane\Profiles\Work copy\Groups";

        [Test]
        public void AddProfileCopy_AppendsAShownProfileWithTheSourcesValues_AndKeepsTheActiveProfile()
        {
            using (var temp = new TempDir())
            {
                var store = TwoProfileStore(temp, active: 1);
                int raised = 0;
                store.ProfilesChanged += (s, e) => raised++;

                var request = store.BeginProfileCopy(0, "Work copy");
                var added = store.AddProfileCopy(request, CopyPath);

                Assert.That(added, Is.Not.Null);
                Assert.That(store.Current.Profiles.Last(), Is.SameAs(added));
                Assert.That(added.Name, Is.EqualTo("Work copy"));
                Assert.That(added.GroupsPath, Is.EqualTo(CopyPath));
                Assert.That(added.RecentsMaxCount, Is.EqualTo(25));
                Assert.That(added.SidebarWidthPx, Is.EqualTo(300));
                Assert.That(added.HiddenFromRow, Is.False, "the source is hidden, the copy starts shown");
                Assert.That(store.Current.ActiveProfileIndex, Is.EqualTo(1));
                Assert.That(store.Current.GroupsPath, Is.EqualTo(Path.Combine(temp.Path, "Home")));
                Assert.That(raised, Is.EqualTo(1));
            }
        }

        [Test]
        public void BeginProfileCopy_OfTheActiveProfile_UsesItsLiveFolder_AndTheCopyItsLiveValues()
        {
            using (var temp = new TempDir())
            {
                var store = TwoProfileStore(temp, active: 0);
                // Live edits sit in the top-level fields until the next sync.
                store.Current.RecentsMaxCount = 40;
                store.Current.SidebarWidthPx = 350;
                store.Current.GroupsPath = Path.Combine(temp.Path, "Moved");

                var request = store.BeginProfileCopy(0, "Work copy");
                Assert.That(request.SourceRoot, Is.EqualTo(Path.Combine(temp.Path, "Moved")));
                Assert.That(request.UsedGroupsPaths, Does.Contain(Path.Combine(temp.Path, "Moved")));
                Assert.That(request.FolderBase, Is.EqualTo("Work copy"));

                var added = store.AddProfileCopy(request, CopyPath);
                Assert.That(added.RecentsMaxCount, Is.EqualTo(40));
                Assert.That(added.SidebarWidthPx, Is.EqualTo(350));
            }
        }

        [Test]
        public void AddProfileCopy_WhenTheSourceWasRemoved_AddsNothing()
        {
            using (var temp = new TempDir())
            {
                var store = TwoProfileStore(temp, active: 1);
                var request = store.BeginProfileCopy(0, "Work copy");
                store.RemoveProfile(0);

                Assert.That(store.AddProfileCopy(request, CopyPath), Is.Null);
                Assert.That(store.Current.Profiles.Select(p => p.Name), Is.EqualTo(new[] { "Home" }));
            }
        }

        [Test]
        public void AddProfileCopy_WhenAnotherProfileTookTheFolderMeanwhile_AddsNothing()
        {
            using (var temp = new TempDir())
            {
                var store = TwoProfileStore(temp, active: 1);
                var request = store.BeginProfileCopy(0, "Work copy");
                store.Current.Profiles.Add(new Profile { Name = "Work copy?", GroupsPath = CopyPath.ToLowerInvariant() });

                Assert.That(store.AddProfileCopy(request, CopyPath), Is.Null);
                Assert.That(store.Current.Profiles.Count, Is.EqualTo(3));
            }
        }

        [Test]
        public void BeginProfileCopy_AnOutOfRangeIndexOrABlankName_GivesNull()
        {
            using (var temp = new TempDir())
            {
                var store = TwoProfileStore(temp);
                Assert.That(store.BeginProfileCopy(5, "Work copy"), Is.Null);
                Assert.That(store.BeginProfileCopy(0, "  "), Is.Null);
            }
        }

        [Test]
        public void AddProfileCopy_SurvivesASaveAndAReload()
        {
            using (var temp = new TempDir())
            {
                var store = TwoProfileStore(temp, active: 1);
                store.AddProfileCopy(store.BeginProfileCopy(0, "Work copy"), CopyPath);
                store.Flush();

                var again = new SettingsStore(temp.Path);
                again.Load();
                var copy = again.Current.Profiles.Last();
                Assert.That(copy.Name, Is.EqualTo("Work copy"));
                Assert.That(copy.GroupsPath, Is.EqualTo(CopyPath));
                Assert.That(copy.RecentsMaxCount, Is.EqualTo(25));
                Assert.That(again.Current.ActiveProfileIndex, Is.EqualTo(1));
            }
        }
    }
}
