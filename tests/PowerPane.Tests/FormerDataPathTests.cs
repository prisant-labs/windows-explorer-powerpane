using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>Covers the mapping from QuickPane's old data folder to PowerPane's (rename decision D6).
    /// A settings file copied from %APPDATA%\QuickPane, or a backup QuickPane made, stores groupsPath
    /// under the old folder. If the mapping misses one, the pane reads a folder that may be gone and
    /// looks empty; if it maps too much, it moves a path the user chose on purpose.</summary>
    [TestFixture]
    public class FormerDataPathTests
    {
        private const string AppData = @"C:\Demo\AppData\Roaming";

        [TestCase(@"%APPDATA%\QuickPane\Groups", @"%APPDATA%\PowerPane\Groups")]
        [TestCase(@"%APPDATA%\QuickPane\Profiles\Work\Groups", @"%APPDATA%\PowerPane\Profiles\Work\Groups")]
        [TestCase(@"%appdata%\quickpane\groups", @"%appdata%\PowerPane\groups")]
        [TestCase(@"%APPDATA%\QuickPane", @"%APPDATA%\PowerPane")]
        [TestCase(@"C:\Demo\AppData\Roaming\QuickPane\Groups", @"C:\Demo\AppData\Roaming\PowerPane\Groups")]
        [TestCase(@"c:\demo\appdata\roaming\QuickPane/Groups", @"c:\demo\appdata\roaming\PowerPane/Groups")]
        public void MapFormerDataPath_MovesPathsInsideTheOldDataFolder(string stored, string expected)
        {
            Assert.That(AppSettings.MapFormerDataPath(stored, AppData), Is.EqualTo(expected));
        }

        [TestCase(@"D:\Demo\Pins")]
        [TestCase(@"%APPDATA%\PowerPane\Groups")]
        [TestCase(@"%APPDATA%\QuickPaneArchive\Groups")]
        [TestCase(@"C:\Demo\QuickPane\Groups")]
        [TestCase(@"\\demo-server\share\QuickPane\Groups")]
        [TestCase("")]
        [TestCase(null)]
        public void MapFormerDataPath_LeavesEveryOtherPathAlone(string stored)
        {
            Assert.That(AppSettings.MapFormerDataPath(stored, AppData), Is.EqualTo(stored));
        }

        [Test]
        public void MapFormerDataPath_WithNoAppDataFolder_StillMapsTheStoredForm()
        {
            Assert.That(AppSettings.MapFormerDataPath(@"%APPDATA%\QuickPane\Groups", null),
                Is.EqualTo(@"%APPDATA%\PowerPane\Groups"));
        }

        [Test]
        public void Normalize_MapsTheTopLevelAndEveryProfile_ButNotACustomPath()
        {
            var s = new AppSettings();
            s.GroupsPath = @"%APPDATA%\QuickPane\Groups";
            s.Profiles = new List<Profile>();
            s.Profiles.Add(new Profile { Name = "Home", GroupsPath = @"%APPDATA%\QuickPane\Groups" });
            s.Profiles.Add(new Profile { Name = "Work", GroupsPath = @"%APPDATA%\QuickPane\Profiles\Work\Groups" });
            s.Profiles.Add(new Profile { Name = "Shared", GroupsPath = @"D:\Demo\Pins" });

            s.Normalize();

            Assert.That(s.GroupsPath, Is.EqualTo(@"%APPDATA%\PowerPane\Groups"));
            Assert.That(s.Profiles.Select(p => p.GroupsPath), Is.EqualTo(new[]
            {
                @"%APPDATA%\PowerPane\Groups",
                @"%APPDATA%\PowerPane\Profiles\Work\Groups",
                @"D:\Demo\Pins",
            }));
        }

        [Test]
        public void ImportFrom_AnOldQuickPaneBackup_PointsAtThePowerPaneFolder()
        {
            using (var temp = new TempDir())
            {
                // Written by hand, as QuickPane's Back up would have written it. Going through this
                // build's own ExportTo would normalize the paths on the way out and prove nothing.
                string backup = Path.Combine(temp.Path, "backup.json");
                File.WriteAllText(backup,
                    "{\"groupsPath\":\"%APPDATA%\\\\QuickPane\\\\Groups\"," +
                    "\"profiles\":[" +
                    "{\"name\":\"Home\",\"groupsPath\":\"%APPDATA%\\\\QuickPane\\\\Groups\"}," +
                    "{\"name\":\"Shared\",\"groupsPath\":\"D:\\\\Demo\\\\Pins\"}]," +
                    "\"activeProfile\":0}");

                var target = new SettingsStore(Path.Combine(temp.Path, "target"));
                target.Load();
                Assert.That(target.ImportFrom(backup), Is.True);
                target.Flush();

                Assert.That(target.Current.Profiles.Select(p => p.GroupsPath), Is.EqualTo(new[]
                {
                    @"%APPDATA%\PowerPane\Groups",
                    @"D:\Demo\Pins",
                }));
                Assert.That(target.ExpandedGroupsPath, Does.Not.Contain(AppSettings.FormerDataFolderName));
            }
        }

        /// <summary>The main path of the move: QuickPane's settings.json copied as is into the new data
        /// folder, then read at startup. The release notes promise this finds the pins.</summary>
        [Test]
        public void Load_ACopiedQuickPaneSettingsFile_PointsAtThePowerPaneFolder()
        {
            using (var temp = new TempDir())
            {
                File.WriteAllText(Path.Combine(temp.Path, "settings.json"),
                    "{\"groupsPath\":\"%APPDATA%\\\\QuickPane\\\\Groups\"," +
                    "\"profiles\":[" +
                    "{\"name\":\"Home\",\"groupsPath\":\"%APPDATA%\\\\QuickPane\\\\Groups\"}," +
                    "{\"name\":\"Work\",\"groupsPath\":\"%APPDATA%\\\\QuickPane\\\\Profiles\\\\Work\\\\Groups\"}]," +
                    "\"activeProfile\":1}");

                var store = new SettingsStore(temp.Path);
                store.Load();

                Assert.That(store.Current.Profiles.Select(p => p.GroupsPath), Is.EqualTo(new[]
                {
                    @"%APPDATA%\PowerPane\Groups",
                    @"%APPDATA%\PowerPane\Profiles\Work\Groups",
                }));
                Assert.That(store.Current.GroupsPath, Is.EqualTo(@"%APPDATA%\PowerPane\Profiles\Work\Groups"),
                    "the active profile's folder is the one the pane reads");
            }
        }

        [TestCase(@"group:C:\Demo\AppData\Roaming\QuickPane\Groups\10_Work", @"group:C:\Demo\AppData\Roaming\PowerPane\Groups\10_Work")]
        [TestCase(@"tree:C:\Demo\AppData\Roaming\QuickPane\Groups", @"tree:C:\Demo\AppData\Roaming\PowerPane\Groups")]
        [TestCase(@"group:D:\Demo\Pins\10_Work", @"group:D:\Demo\Pins\10_Work")]
        [TestCase(@"tree:C:\Demo\Projects", @"tree:C:\Demo\Projects")]
        [TestCase("section:computer", "section:computer")]
        [TestCase("", "")]
        public void UiStateKeys_InsideTheOldDataFolder_MoveToTheNewOne(string stored, string expected)
        {
            Assert.That(UiState.MapFormerKey(stored, AppData), Is.EqualTo(expected));
        }
    }
}
