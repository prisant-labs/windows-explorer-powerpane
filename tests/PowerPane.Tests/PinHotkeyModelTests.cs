using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;
using PowerPane.UI.Settings.Pages;

namespace PowerPane.Tests
{
    /// <summary>
    /// The rules for pin shortcuts (#299). The folders are invented, and only the settings round trip
    /// touches the disk, in its own temporary folder.
    /// </summary>
    [TestFixture]
    public class PinHotkeyModelTests
    {
        private const string Apollo = @"C:\Demo\Projects\Apollo";
        private const string Borealis = @"C:\Demo\Projects\Borealis";
        private const string Contoso = @"C:\Demo\Projects\Contoso";

        private static List<PinHotkeyEntry> Entries(params object[] slotTarget)
        {
            var list = new List<PinHotkeyEntry>();
            for (int i = 0; i < slotTarget.Length; i += 2)
                list.Add(new PinHotkeyEntry { Slot = (int)slotTarget[i], Target = (string)slotTarget[i + 1] });
            return list;
        }

        private static string Show(IEnumerable<PinHotkeyEntry> list)
        {
            return list == null ? "(none)" : string.Join(", ", list.Select(e => e.Slot + "=" + Path.GetFileName(e.Target)));
        }

        [Test]
        public void SlotSpec_SpellsTheDigitSoTheParserReadsTheDigitKey()
        {
            Assert.That(PinHotkeyModel.SlotSpec("ctrl+alt", 3), Is.EqualTo("ctrl+alt+D3"));
            uint mods, vk;
            Assert.That(HotkeyService.TryParse(PinHotkeyModel.SlotSpec("ctrl+alt", 3), out mods, out vk), Is.True);
            Assert.That(vk, Is.EqualTo(0x33u), "the 3 key, not Key value 3");
            Assert.That(mods, Is.EqualTo(0x0003u), "ctrl and alt");
        }

        [TestCase(0)]
        [TestCase(10)]
        [TestCase(-1)]
        public void SlotSpec_ANumberOutOfRange_IsNull(int slot)
        {
            Assert.That(PinHotkeyModel.SlotSpec("ctrl+alt", slot), Is.Null);
        }

        [Test]
        public void SlotLabel_ReadsAsAPersonWritesIt()
        {
            Assert.That(PinHotkeyModel.SlotLabel("ctrl+alt", 1), Is.EqualTo("Ctrl+Alt+1"));
            Assert.That(PinHotkeyModel.SlotLabel("ctrl+shift", 9), Is.EqualTo("Ctrl+Shift+9"));
        }

        [TestCase("ctrl+alt", "ctrl+alt")]
        [TestCase(" Alt + Ctrl ", "ctrl+alt")]
        [TestCase("control+shift", "ctrl+shift")]
        [TestCase("ctrl+ctrl+alt", "ctrl+alt")]
        [TestCase("win+alt", "alt+win")]
        [TestCase("", null)]
        [TestCase("Q", null)]
        [TestCase("ctrl+alt+Q", null)]
        [TestCase("ctrl+alt+1", null)]
        [TestCase("+", null)]
        public void ValidateModifiers_KeepsModifiersAloneInOneOrder(string input, string expected)
        {
            Assert.That(PinHotkeyModel.ValidateModifiers(input), Is.EqualTo(expected));
        }

        [Test]
        public void Assign_AddsMovesAndReplaces()
        {
            var one = PinHotkeyModel.Assign(null, 1, Apollo);
            Assert.That(Show(one), Is.EqualTo("1=Apollo"));

            var moved = PinHotkeyModel.Assign(one, 1, Borealis);
            Assert.That(Show(moved), Is.EqualTo("1=Borealis"), "the number moves; Apollo holds nothing");

            var replaced = PinHotkeyModel.Assign(Entries(1, Apollo, 3, Contoso), 2, Apollo);
            Assert.That(Show(replaced), Is.EqualTo("2=Apollo, 3=Contoso"), "a folder has one number");
        }

        [Test]
        public void Assign_ComparesFoldersByPathKey()
        {
            var list = PinHotkeyModel.Assign(Entries(1, @"C:\Demo\Projects\Apollo\"), 2, "c:/demo/projects/apollo");
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list[0].Slot, Is.EqualTo(2));
            Assert.That(PinHotkeyModel.SlotOf(list, Apollo), Is.EqualTo(2));
        }

        [Test]
        public void Assign_DoesNotChangeTheListItWasGiven()
        {
            var original = Entries(1, Apollo);
            PinHotkeyModel.Assign(original, 1, Borealis);
            Assert.That(Show(original), Is.EqualTo("1=Apollo"));
        }

        [Test]
        public void Unassign_RemovesOnlyThatNumber_AndAnEmptyListIsNull()
        {
            Assert.That(Show(PinHotkeyModel.Unassign(Entries(1, Apollo, 2, Borealis), 1)), Is.EqualTo("2=Borealis"));
            Assert.That(Show(PinHotkeyModel.Unassign(Entries(1, Apollo), 7)), Is.EqualTo("1=Apollo"));
            Assert.That(PinHotkeyModel.Unassign(Entries(1, Apollo), 1), Is.Null);
        }

        [Test]
        public void Resolve_OpensOnlyAFolderThatIsStillPinned()
        {
            var list = Entries(1, Apollo, 2, Borealis);
            var pins = new[] { @"c:\demo\projects\apollo\", Contoso };
            Assert.That(PinHotkeyModel.Resolve(list, 1, pins), Is.EqualTo(Apollo));
            Assert.That(PinHotkeyModel.Resolve(list, 2, pins), Is.Null, "Borealis is no longer pinned");
            Assert.That(PinHotkeyModel.Resolve(list, 3, pins), Is.Null);
            Assert.That(PinHotkeyModel.Resolve(Entries(4, ""), 4, new[] { "" }), Is.Null);
        }

        [Test]
        public void Resolve_IgnoresTheOrderOfThePins_SoAReorderLosesNoKey()
        {
            var list = Entries(1, Apollo);
            var before = new[] { Apollo, Borealis, Contoso };
            var after = new[] { Contoso, Apollo, Borealis };
            Assert.That(PinHotkeyModel.Resolve(list, 1, after), Is.EqualTo(PinHotkeyModel.Resolve(list, 1, before)));
        }

        [Test]
        public void Orphans_AreTheEntriesNoPinPointsAt()
        {
            var list = Entries(2, Borealis, 1, Apollo, 3, Contoso);
            Assert.That(Show(PinHotkeyModel.Orphans(list, new[] { Apollo })), Is.EqualTo("2=Borealis, 3=Contoso"));
        }

        [Test]
        public void MigrateTarget_MovesTheKeyAndNothingElse()
        {
            var list = Entries(1, Apollo, 2, Borealis);
            Assert.That(Show(PinHotkeyModel.MigrateTarget(list, Apollo, Contoso)), Is.EqualTo("1=Contoso, 2=Borealis"));
            Assert.That(Show(PinHotkeyModel.MigrateTarget(list, Contoso, Apollo)), Is.EqualTo("1=Apollo, 2=Borealis"));
        }

        [Test]
        public void FolderPinTargets_LeavesOutFilePinsAndEmptyTargets()
        {
            var groups = new[]
            {
                new PinnedGroup
                {
                    Tabs = new List<PinnedTab>
                    {
                        new PinnedTab { Items = new List<PinnedFolder>
                        {
                            new PinnedFolder { TargetPath = Apollo },
                            new PinnedFolder { TargetPath = @"C:\Demo\Projects\notes.txt", IsFile = true },
                            new PinnedFolder { TargetPath = "" }
                        } },
                        new PinnedTab { Items = new List<PinnedFolder> { new PinnedFolder { TargetPath = Borealis } } }
                    }
                }
            };
            Assert.That(PinHotkeyModel.FolderPinTargets(groups), Is.EqualTo(new[] { Apollo, Borealis }));
        }

        [Test]
        public void KeyboardPage_PinPrefix_KeepsAValidPrefixAndRevertsTheRest()
        {
            bool accepted;
            Assert.That(KeyboardPage.ValidatePinPrefixText("Shift+Ctrl", "ctrl+alt", out accepted), Is.EqualTo("ctrl+shift"));
            Assert.That(accepted, Is.True);
            Assert.That(KeyboardPage.ValidatePinPrefixText("ctrl+alt+P", "ctrl+alt", out accepted), Is.EqualTo("ctrl+alt"));
            Assert.That(accepted, Is.False);
        }

        [Test]
        public void KeyboardPage_PinShortcutNote_SaysWhyAKeyDoesNothing()
        {
            Assert.That(KeyboardPage.PinShortcutNote(false, false), Is.Null);
            Assert.That(KeyboardPage.PinShortcutNote(true, true), Is.EqualTo("no longer pinned"));
            Assert.That(KeyboardPage.PinShortcutNote(false, true), Does.Contain("Windows refused"));
        }

        // ---- settings round trip ---------------------------------------------

        private static AppSettings RoundTrip(AppSettings s, out string json)
        {
            var ser = new DataContractJsonSerializer(typeof(AppSettings));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, s);
                json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                ms.Position = 0;
                return (AppSettings)ser.ReadObject(ms);
            }
        }

        [Test]
        public void Settings_TheFeatureIsOffAndAProfileWithNoKeysWritesNothing()
        {
            var s = AppSettings.CreateDefault();
            s.Profiles = new List<Profile> { new Profile { Name = "Work" } };
            string json;
            RoundTrip(s, out json);
            Assert.That(s.PinHotkeys, Is.False);
            Assert.That(json, Does.Not.Contain("\"pinHotkeys\""));
        }

        [Test]
        public void Settings_KeysAndTheSwitchSurviveARoundTrip()
        {
            var s = AppSettings.CreateDefault();
            s.PinHotkeys = true;
            s.PinHotkeyModifiers = "ctrl+shift";
            s.Profiles = new List<Profile> { new Profile { Name = "Work", PinHotkeys = Entries(1, Apollo) } };
            string json;
            var back = RoundTrip(s, out json);
            Assert.That(back.PinHotkeys, Is.True);
            Assert.That(back.PinHotkeyModifiers, Is.EqualTo("ctrl+shift"));
            Assert.That(Show(back.Profiles[0].PinHotkeys), Is.EqualTo("1=Apollo"));
        }

        [Test]
        public void Settings_AnOlderFileLoadsWithTheFeatureOffAndTheDefaultPrefix()
        {
            using (var temp = new TempDir())
            {
                File.WriteAllText(Path.Combine(temp.Path, "settings.json"),
                    "{\"groupsPath\":\"C:\\\\Demo\\\\Groups\",\"profiles\":[{\"name\":\"Work\",\"groupsPath\":\"C:\\\\Demo\\\\Groups\"}]}");
                var store = new SettingsStore(temp.Path);
                store.Load();
                Assert.That(store.Current.PinHotkeys, Is.False);
                Assert.That(store.Current.PinHotkeyModifiers, Is.EqualTo(AppSettings.DefaultPinHotkeyModifiers));
                Assert.That(store.Current.Profiles[0].PinHotkeys, Is.Null);
            }
        }

        [Test]
        public void Settings_AnUnreadablePrefixInTheFileFallsBackToTheDefault()
        {
            using (var temp = new TempDir())
            {
                File.WriteAllText(Path.Combine(temp.Path, "settings.json"),
                    "{\"groupsPath\":\"C:\\\\Demo\\\\Groups\",\"pinHotkeyModifiers\":\"ctrl+alt+Q\"}");
                var store = new SettingsStore(temp.Path);
                store.Load();
                Assert.That(store.Current.PinHotkeyModifiers, Is.EqualTo(AppSettings.DefaultPinHotkeyModifiers));
            }
        }
    }
}
