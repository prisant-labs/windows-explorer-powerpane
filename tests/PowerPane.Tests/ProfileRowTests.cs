using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>Pins which profiles get a chip in the pane's profile row once a profile can be hidden
    /// from it (#6): hidden profiles drop out, the active profile always stays, and the row shows only
    /// when two or more chips would be drawn. Also pins that the new field leaves old settings files
    /// reading as "shown".</summary>
    [TestFixture]
    public class ProfileRowTests
    {
        private static List<Profile> Profiles(params bool[] hidden)
        {
            var list = new List<Profile>();
            for (int i = 0; i < hidden.Length; i++)
                list.Add(new Profile { Name = "P" + i, GroupsPath = @"C:\Demo\Groups" + i, HiddenFromRow = hidden[i] });
            return list;
        }

        [Test]
        public void Chips_NoneHidden_ListsEveryProfileInOrder()
        {
            Assert.That(SidebarControl.ProfileChipIndexes(Profiles(false, false, false), 0),
                Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void Chips_HiddenInactiveProfile_IsLeftOut()
        {
            Assert.That(SidebarControl.ProfileChipIndexes(Profiles(false, true, false), 0),
                Is.EqualTo(new[] { 0, 2 }));
        }

        [Test]
        public void Chips_HiddenActiveProfile_KeepsItsChip()
        {
            Assert.That(SidebarControl.ProfileChipIndexes(Profiles(false, true, true), 1),
                Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Chips_NullListOrNullEntry_IsSafe()
        {
            Assert.That(SidebarControl.ProfileChipIndexes(null, 0), Is.Empty);
            var list = Profiles(false, false);
            list.Insert(1, null);
            Assert.That(SidebarControl.ProfileChipIndexes(list, 0), Is.EqualTo(new[] { 0, 2 }));
        }

        [Test]
        public void Row_EveryOtherProfileHidden_IsHidden()
        {
            // Three profiles, but only the active one would get a chip: a lone chip switches nothing.
            int chips = SidebarControl.ProfileChipIndexes(Profiles(false, true, true), 0).Count;
            Assert.That(SidebarControl.ShouldShowProfileTabs(true, false, chips), Is.False);
        }

        [Test]
        public void Row_TwoChipsLeft_Shows()
        {
            int chips = SidebarControl.ProfileChipIndexes(Profiles(false, true, false), 0).Count;
            Assert.That(SidebarControl.ShouldShowProfileTabs(true, false, chips), Is.True);
        }

        [Test]
        public void PaneShapeKey_HidingAProfile_ChangesTheKey()
        {
            // The row is drawn during a rebuild, and a settings change that keeps the same key skips
            // the rebuild (#51), so the switch would do nothing until something else rebuilt the pane.
            var a = AppSettings.CreateDefault();
            a.Profiles = Profiles(false, false);
            var b = AppSettings.CreateDefault();
            b.Profiles = Profiles(false, true);

            Assert.That(SidebarControl.PaneShapeKey(b), Is.Not.EqualTo(SidebarControl.PaneShapeKey(a)));
        }

        // ---- settings file ----------------------------------------------------

        private static Profile Read(string json)
        {
            var ser = new DataContractJsonSerializer(typeof(Profile));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (Profile)ser.ReadObject(ms);
        }

        private static string Write(Profile p)
        {
            var ser = new DataContractJsonSerializer(typeof(Profile));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, p);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        [Test]
        public void File_WrittenBeforeTheField_ReadsAsShown()
        {
            var p = Read("{\"name\":\"Work\",\"groupsPath\":\"C:\\\\Demo\\\\Groups\",\"recentsMaxCount\":15,\"sidebarWidthPx\":220}");
            Assert.That(p.HiddenFromRow, Is.False);
        }

        [Test]
        public void File_ShownProfile_WritesNoField()
        {
            // Leaving the field out for the default keeps files unchanged for anyone who never hides a
            // profile.
            Assert.That(Write(new Profile { Name = "Work" }), Does.Not.Contain("hiddenFromRow"));
        }

        [Test]
        public void File_HiddenProfile_RoundTrips()
        {
            var json = Write(new Profile { Name = "Work", HiddenFromRow = true });
            Assert.That(json, Does.Contain("\"hiddenFromRow\":true"));
            Assert.That(Read(json).HiddenFromRow, Is.True);
        }

        // ---- removing a profile keeps the user where they were -------------------

        [TestCase(1, 0, 2, 0)] // removing an earlier profile: the active one moves down with it
        [TestCase(1, 2, 2, 1)] // removing a later profile: no change
        [TestCase(1, 1, 2, 1)] // removing the active profile: the next one takes its place
        [TestCase(2, 2, 2, 1)] // removing the active, last profile: the new last one
        [TestCase(0, 0, 1, 0)]
        [TestCase(5, 0, 2, 1)] // an index already out of range is clamped
        public void ActiveIndexAfterRemove_FollowsTheActiveProfile(int active, int removed, int countAfter, int expected)
        {
            Assert.That(SettingsStore.ActiveIndexAfterRemove(active, removed, countAfter), Is.EqualTo(expected));
        }

        [Test]
        public void Store_RemoveEarlierProfile_KeepsTheActiveProfileAndItsChip()
        {
            using (var temp = new TempDir())
            {
                var settings = AppSettings.CreateDefault();
                settings.Profiles = new List<Profile>
                {
                    new Profile { Name = "A", GroupsPath = Path.Combine(temp.Path, "A") },
                    new Profile { Name = "B", GroupsPath = Path.Combine(temp.Path, "B"), HiddenFromRow = true },
                    new Profile { Name = "C", GroupsPath = Path.Combine(temp.Path, "C") }
                };
                settings.ActiveProfileIndex = 1;
                settings.GroupsPath = Path.Combine(temp.Path, "B");
                using (var fs = File.Create(Path.Combine(temp.Path, "settings.json")))
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(fs, settings);

                var store = new SettingsStore(temp.Path);
                store.Load();
                store.Current.RecentsMaxCount = 27; // a live edit to B not yet copied into its profile
                store.RemoveProfile(0);
                store.Flush();

                var reloaded = new SettingsStore(temp.Path);
                reloaded.Load();
                var s = reloaded.Current;
                Assert.That(s.Profiles[s.ActiveProfileIndex].Name, Is.EqualTo("B"));
                Assert.That(s.GroupsPath, Is.EqualTo(Path.Combine(temp.Path, "B")));
                Assert.That(s.Profiles[s.ActiveProfileIndex].RecentsMaxCount, Is.EqualTo(27));
                Assert.That(SidebarControl.ProfileChipIndexes(s.Profiles, s.ActiveProfileIndex), Is.EqualTo(new[] { 0, 1 }));
            }
        }

        [Test]
        public void Store_SetProfileHiddenFromRow_SavesAndReloads()
        {
            using (var temp = new TempDir())
            {
                var settings = AppSettings.CreateDefault();
                settings.GroupsPath = Path.Combine(temp.Path, "A");
                settings.Profiles = new List<Profile>
                {
                    new Profile { Name = "A", GroupsPath = Path.Combine(temp.Path, "A") },
                    new Profile { Name = "B", GroupsPath = Path.Combine(temp.Path, "B") }
                };
                using (var fs = File.Create(Path.Combine(temp.Path, "settings.json")))
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(fs, settings);

                var store = new SettingsStore(temp.Path);
                store.Load();
                store.SetProfileHiddenFromRow(1, true);
                store.SetProfileHiddenFromRow(5, true); // out of range: ignored
                store.Flush();

                var reloaded = new SettingsStore(temp.Path);
                reloaded.Load();
                Assert.That(reloaded.Current.Profiles[0].HiddenFromRow, Is.False);
                Assert.That(reloaded.Current.Profiles[1].HiddenFromRow, Is.True);
            }
        }
    }
}
