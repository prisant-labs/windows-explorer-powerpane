using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>Pins that a new profile never moves into another profile's groups folder (#303).
    /// The folder came from the name alone, so the same name twice, a name differing only in case,
    /// the name of a removed profile, or a renamed profile's old name all shared one folder.</summary>
    [TestFixture]
    public class ProfileFolderTests
    {
        private const string Root = @"%APPDATA%\PowerPane\Profiles\";

        private static string Groups(string folder)
        {
            return Root + folder + @"\Groups";
        }

        private static readonly Func<string, bool> NothingOnDisk = path => false;

        [Test]
        public void Choose_FreeName_KeepsIt()
        {
            Assert.That(SettingsStore.ChooseProfileFolder("Work", new[] { Groups("Home") }, NothingOnDisk),
                Is.EqualTo("Work"));
        }

        [Test]
        public void Choose_NameInUse_AddsANumber()
        {
            Assert.That(SettingsStore.ChooseProfileFolder("Work", new[] { Groups("Work") }, NothingOnDisk),
                Is.EqualTo("Work 2"));
        }

        [Test]
        public void Choose_NumberedNamesInUse_TakesTheNextFree()
        {
            var used = new[] { Groups("Work"), Groups("Work 2"), Groups("Work 3") };
            Assert.That(SettingsStore.ChooseProfileFolder("Work", used, NothingOnDisk), Is.EqualTo("Work 4"));
        }

        [Test]
        public void Choose_DifferentCase_CountsAsTaken()
        {
            Assert.That(SettingsStore.ChooseProfileFolder("work", new[] { Groups("Work") }, NothingOnDisk),
                Is.EqualTo("work 2"));
        }

        [Test]
        public void Choose_ExpandedOrSlashedSpelling_CountsAsTaken()
        {
            // A path saved expanded, with forward slashes or a trailing backslash is the same folder.
            string expanded = Environment.ExpandEnvironmentVariables(Groups("Work")).Replace('\\', '/') + "/";
            Assert.That(SettingsStore.ChooseProfileFolder("Work", new[] { expanded }, NothingOnDisk),
                Is.EqualTo("Work 2"));
        }

        [Test]
        public void Choose_FolderLeftOnDisk_CountsAsTaken()
        {
            // A removed profile leaves its folder behind, and reusing it would bring its groups back.
            string leftBehind = Environment.ExpandEnvironmentVariables(Root + "Work");
            Func<string, bool> exists = path => string.Equals(path, leftBehind, StringComparison.OrdinalIgnoreCase);
            Assert.That(SettingsStore.ChooseProfileFolder("Work", new string[0], exists), Is.EqualTo("Work 2"));
        }

        [Test]
        public void Choose_OnlyASimilarPrefixInUse_IsFree()
        {
            // "Work 2" in use must not block "Work", and "Workshop" must not block "Work".
            var used = new[] { Groups("Work 2"), Groups("Workshop") };
            Assert.That(SettingsStore.ChooseProfileFolder("Work", used, NothingOnDisk), Is.EqualTo("Work"));
        }

        [Test]
        public void Choose_CustomPathsAndBlanks_AreIgnored()
        {
            var used = new[] { null, "", @"C:\Demo\Groups", Groups("Home") };
            Assert.That(SettingsStore.ChooseProfileFolder("Work", used, NothingOnDisk), Is.EqualTo("Work"));
        }

        [Test]
        public void Choose_NullChecks_AreSafe()
        {
            Assert.That(SettingsStore.ChooseProfileFolder("Work", null, null), Is.EqualTo("Work"));
        }

        // ---- through the store ----------------------------------------------------

        private static SettingsStore StoreWith(TempDir temp, params Profile[] profiles)
        {
            var settings = AppSettings.CreateDefault();
            settings.Profiles = new List<Profile>(profiles);
            settings.ActiveProfileIndex = 0;
            settings.GroupsPath = profiles[0].GroupsPath;
            using (var fs = File.Create(Path.Combine(temp.Path, "settings.json")))
                new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(fs, settings);
            var store = new SettingsStore(temp.Path);
            store.Load();
            store.ProfileFolderExists = NothingOnDisk; // never look in the real AppData folder
            return store;
        }

        [Test]
        public void Store_SameNameTwice_GetsItsOwnFolder()
        {
            using (var temp = new TempDir())
            {
                var store = StoreWith(temp, new Profile { Name = "Work", GroupsPath = Groups("Work") });
                var added = store.AddProfile("Work");
                Assert.That(added.Name, Is.EqualTo("Work"));
                Assert.That(added.GroupsPath, Is.EqualTo(Groups("Work 2")));
            }
        }

        [Test]
        public void Store_RenamedProfilesOldName_GetsItsOwnFolder()
        {
            using (var temp = new TempDir())
            {
                // "Work" was renamed to "Home" and kept its folder.
                var store = StoreWith(temp, new Profile { Name = "Home", GroupsPath = Groups("Work") });
                Assert.That(store.AddProfile("Work").GroupsPath, Is.EqualTo(Groups("Work 2")));
            }
        }

        [Test]
        public void Store_NameWithDroppedCharacters_GetsItsOwnFolder()
        {
            using (var temp = new TempDir())
            {
                var store = StoreWith(temp, new Profile { Name = "Work", GroupsPath = Groups("Work") });
                var a = store.AddProfile("Work?");
                var b = store.AddProfile("Work.");
                Assert.That(a.Name, Is.EqualTo("Work?"));
                Assert.That(a.GroupsPath, Is.EqualTo(Groups("Work 2")));
                Assert.That(b.GroupsPath, Is.EqualTo(Groups("Work 3")));
            }
        }

        [Test]
        public void Store_ActiveProfilesUnsyncedPath_CountsAsTaken()
        {
            using (var temp = new TempDir())
            {
                var store = StoreWith(temp, new Profile { Name = "Main", GroupsPath = Groups("Main") });
                store.Current.GroupsPath = Groups("Work"); // edited live, not yet copied into the profile
                Assert.That(store.AddProfile("Work").GroupsPath, Is.EqualTo(Groups("Work 2")));
            }
        }

        [Test]
        public void Store_AddedFolders_SurviveAReload()
        {
            using (var temp = new TempDir())
            {
                var store = StoreWith(temp, new Profile { Name = "Work", GroupsPath = Groups("Work") });
                store.AddProfile("Work");
                store.Flush();
                var reloaded = new SettingsStore(temp.Path);
                reloaded.Load();
                Assert.That(reloaded.Current.Profiles[0].GroupsPath, Is.EqualTo(Groups("Work")));
                Assert.That(reloaded.Current.Profiles[1].GroupsPath, Is.EqualTo(Groups("Work 2")));
            }
        }
    }
}
