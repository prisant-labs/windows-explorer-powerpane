using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// Exercises SettingsStore entirely inside a throwaway temp directory, using the internal
    /// (string dir) constructor added for tests. Never uses the public default constructor, which
    /// hard-codes the real %APPDATA%\PowerPane. GroupsPath and the one profile's GroupsPath both point
    /// inside the temp dir, because LoadActiveToTop() copies the active profile's path over the
    /// top-level one and a later GroupStore scan would create that folder.
    /// </summary>
    [TestFixture]
    public class SettingsStoreTests
    {
        private static void WriteSettingsFile(string dir, string groupsPath)
        {
            var settings = AppSettings.CreateDefault();
            settings.GroupsPath = groupsPath;
            settings.Profiles = new List<Profile>
            {
                new Profile { Name = "Profile 1", GroupsPath = groupsPath, RecentsMaxCount = 15, SidebarWidthPx = 220 }
            };

            var file = Path.Combine(dir, "settings.json");
            using (var fs = File.Create(file))
            {
                var ser = new DataContractJsonSerializer(typeof(AppSettings));
                ser.WriteObject(fs, settings);
            }
        }

        [Test]
        public void Load_ReadsGroupsPathFromTheActiveProfile()
        {
            using (var temp = new TempDir())
            {
                var groupsPath = Path.Combine(temp.Path, "Groups");
                WriteSettingsFile(temp.Path, groupsPath);

                var store = new SettingsStore(temp.Path);
                store.Load();

                Assert.That(store.Current.GroupsPath, Is.EqualTo(groupsPath));
                // Load runs on the UI thread, so it must not touch the groups folder, which can be on a
                // network share (#70). GroupStore creates it on the worker.
                Assert.That(Directory.Exists(groupsPath), Is.False);
            }
        }

        [Test]
        public void SaveThenLoad_RoundTripsAChangedValue()
        {
            using (var temp = new TempDir())
            {
                var groupsPath = Path.Combine(temp.Path, "Groups");
                WriteSettingsFile(temp.Path, groupsPath);

                var store = new SettingsStore(temp.Path);
                store.Load();
                store.Current.RecentsMaxCount = 33;
                store.Save();

                var reloaded = new SettingsStore(temp.Path);
                reloaded.Load();

                Assert.That(reloaded.Current.RecentsMaxCount, Is.EqualTo(33));
            }
        }
    }
}
