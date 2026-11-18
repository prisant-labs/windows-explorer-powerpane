using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Input;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>Covers the pure click-action decisions (#269): the gesture a button and modifiers
    /// make, the defaults that keep the click behaviour from before the setting, storing and
    /// normalizing choices, what an action means on a given row, and when a quick action may run.</summary>
    [TestFixture]
    public class ClickActionsTests
    {
        private static AppSettings Fresh()
        {
            var s = AppSettings.CreateDefault();
            s.Normalize();
            return s;
        }

        // ---- gestures ------------------------------------------------------------

        [TestCase(ModifierKeys.None, "Left")]
        [TestCase(ModifierKeys.Control, "Ctrl")]
        [TestCase(ModifierKeys.Shift, "Shift")]
        [TestCase(ModifierKeys.Control | ModifierKeys.Shift, "CtrlShift")]
        [TestCase(ModifierKeys.Alt, "Left")]
        [TestCase(ModifierKeys.Windows, "Left")]
        [TestCase(ModifierKeys.Alt | ModifierKeys.Control, "Ctrl")]
        public void GestureFor_LeftButton_ReadsCtrlAndShiftOnly(ModifierKeys mods, string expected)
        {
            Assert.That(ClickActions.GestureFor(MouseButton.Left, mods).ToString(), Is.EqualTo(expected));
        }

        [Test]
        public void GestureFor_MiddleButton_IsMiddleWhateverIsHeld()
        {
            Assert.That(ClickActions.GestureFor(MouseButton.Middle, ModifierKeys.None), Is.EqualTo(ClickGesture.Middle));
            Assert.That(ClickActions.GestureFor(MouseButton.Middle, ModifierKeys.Control), Is.EqualTo(ClickGesture.Middle));
        }

        [TestCase(MouseButton.Right)]
        [TestCase(MouseButton.XButton1)]
        [TestCase(MouseButton.XButton2)]
        public void GestureFor_OtherButtons_HaveNoAction(MouseButton button)
        {
            Assert.That(ClickActions.GestureFor(button, ModifierKeys.None), Is.Null);
        }

        [Test]
        public void GestureIds_RoundTrip()
        {
            foreach (var g in ClickActions.Gestures)
            {
                ClickGesture parsed;
                Assert.That(ClickActions.TryParseGesture(ClickActions.GestureId(g), out parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(g));
            }
            ClickGesture unused;
            Assert.That(ClickActions.TryParseGesture("alt", out unused), Is.False);
        }

        // ---- defaults keep today's clicks ----------------------------------------

        [Test]
        public void Defaults_EveryLeftGestureOpens_AndMiddleDoesNothing()
        {
            var s = Fresh();
            foreach (var forFile in new[] { false, true })
            {
                Assert.That(ClickActions.Resolve(s, ClickGesture.Left, forFile), Is.EqualTo(ClickActions.Open));
                Assert.That(ClickActions.Resolve(s, ClickGesture.Ctrl, forFile), Is.EqualTo(ClickActions.Open));
                Assert.That(ClickActions.Resolve(s, ClickGesture.Shift, forFile), Is.EqualTo(ClickActions.Open));
                Assert.That(ClickActions.Resolve(s, ClickGesture.CtrlShift, forFile), Is.EqualTo(ClickActions.Open));
                Assert.That(ClickActions.Resolve(s, ClickGesture.Middle, forFile), Is.EqualTo(ClickActions.None));
            }
            Assert.That(s.ClickActions, Is.Null, "a fresh settings file stores no click choices");
        }

        [Test]
        public void SameAsLeft_FollowsTheLeftClickChoice()
        {
            var s = Fresh();
            ClickActions.Set(s, ClickGesture.Left, false, ClickActions.NewWindow);
            Assert.That(ClickActions.Resolve(s, ClickGesture.Shift, false), Is.EqualTo(ClickActions.NewWindow));
            Assert.That(ClickActions.Resolve(s, ClickGesture.Shift, true), Is.EqualTo(ClickActions.Open), "file rows have their own left click");
        }

        // ---- storing ---------------------------------------------------------------

        [Test]
        public void Set_StoresOnlyChangedGestures_AndRemovesAnEntryResetToDefault()
        {
            var s = Fresh();
            ClickActions.Set(s, ClickGesture.Middle, false, ClickActions.NewWindow);
            Assert.That(s.ClickActions.Count, Is.EqualTo(1));
            Assert.That(s.ClickActions[0].Gesture, Is.EqualTo("middle"));
            Assert.That(s.ClickActions[0].Folder, Is.EqualTo(ClickActions.NewWindow));
            Assert.That(s.ClickActions[0].File, Is.Null);

            ClickActions.Set(s, ClickGesture.Middle, false, ClickActions.None);
            Assert.That(s.ClickActions, Is.Empty);
        }

        [Test]
        public void Set_RefusesNothingForLeftClick_AndFolderOnlyActionsForFiles()
        {
            var s = Fresh();
            ClickActions.Set(s, ClickGesture.Left, false, ClickActions.None);
            ClickActions.Set(s, ClickGesture.Left, false, ClickActions.SameAsLeft);
            ClickActions.Set(s, ClickGesture.Ctrl, true, ClickActions.NewWindow);
            ClickActions.Set(s, ClickGesture.Ctrl, true, ClickActions.QuickPrefix + "open-terminal");
            Assert.That(s.ClickActions, Is.Null.Or.Empty);
            Assert.That(ClickActions.Resolve(s, ClickGesture.Left, false), Is.EqualTo(ClickActions.Open));
        }

        [Test]
        public void Options_LeftClickOffersNeitherNothingNorSameAsLeft()
        {
            var s = Fresh();
            var left = ClickActions.Options(s, ClickGesture.Left, false).Select(p => p.Key).ToList();
            Assert.That(left, Does.Not.Contain(ClickActions.None));
            Assert.That(left, Does.Not.Contain(ClickActions.SameAsLeft));
            var middle = ClickActions.Options(s, ClickGesture.Middle, false).Select(p => p.Key).ToList();
            Assert.That(middle, Does.Contain(ClickActions.None));
            Assert.That(middle, Does.Contain(ClickActions.SameAsLeft));
        }

        [Test]
        public void Options_EveryOfferedChoiceIsValid_AndTheStoredOneIsListed()
        {
            var s = Fresh();
            foreach (var g in ClickActions.Gestures)
                foreach (var forFile in new[] { false, true })
                {
                    var keys = ClickActions.Options(s, g, forFile).Select(p => p.Key).ToList();
                    Assert.That(keys, Does.Contain(ClickActions.Stored(s, g, forFile)));
                    foreach (var k in keys) Assert.That(ClickActions.IsValid(s, g, forFile, k), Is.True, k);
                }
        }

        [Test]
        public void Options_FolderListsQuickActionsThatApplyToRows_AndMarksOffOnes()
        {
            var s = Fresh();
            var folder = ClickActions.Options(s, ClickGesture.Middle, false);
            Assert.That(folder.Any(p => p.Key == ClickActions.QuickPrefix + "copy-path"), Is.True);
            Assert.That(folder.Any(p => p.Key == ClickActions.QuickPrefix + "pin-folder"), Is.False, "a pin is already pinned");
            var file = ClickActions.Options(s, ClickGesture.Middle, true);
            Assert.That(file.Any(p => p.Key.StartsWith(ClickActions.QuickPrefix)), Is.False);

            var off = s.QuickActions.First(a => !a.Enabled && QuickActionLibrary.AppliesToPinnedRow(QuickActionLibrary.Resolve(s, a.Id)));
            Assert.That(folder.Single(p => p.Key == ClickActions.QuickPrefix + off.Id).Value, Does.EndWith("(off)"));
        }

        // ---- normalizing a loaded file -------------------------------------------

        [Test]
        public void Normalize_DropsUnknownGesturesActionsAndRepeats()
        {
            var s = AppSettings.CreateDefault();
            s.ClickActions = new List<ClickActionSetting>
            {
                new ClickActionSetting { Gesture = "middle", Folder = ClickActions.NewWindow, File = "launch-rockets" },
                new ClickActionSetting { Gesture = "middle", Folder = ClickActions.CopyPath },
                new ClickActionSetting { Gesture = "alt", Folder = ClickActions.CopyPath },
                new ClickActionSetting { Gesture = "left", Folder = ClickActions.None, File = ClickActions.Open },
                new ClickActionSetting { Gesture = "ctrl", Folder = ClickActions.QuickPrefix + "no-such-action" },
                null,
            };
            s.Normalize();

            Assert.That(s.ClickActions.Count, Is.EqualTo(1));
            Assert.That(s.ClickActions[0].Gesture, Is.EqualTo("middle"));
            Assert.That(s.ClickActions[0].Folder, Is.EqualTo(ClickActions.NewWindow));
            Assert.That(s.ClickActions[0].File, Is.Null);
        }

        // #282: Settings listed a built-in Copy path beside the Copy path quick action for folders.

        [Test]
        public void Normalize_FolderCopyPathFrom420_BecomesTheCopyPathQuickAction_FileCopyPathStays()
        {
            var s = Fresh();
            s.ClickActions = new List<ClickActionSetting>
            {
                new ClickActionSetting { Gesture = "middle", Folder = ClickActions.CopyPath, File = ClickActions.CopyPath },
            };
            s.Normalize();
            Assert.That(ClickActions.Resolve(s, ClickGesture.Middle, false), Is.EqualTo(ClickActions.QuickCopyPath));
            Assert.That(ClickActions.Resolve(s, ClickGesture.Middle, true), Is.EqualTo(ClickActions.CopyPath));
        }

        [Test]
        public void Options_FolderOffersOnlyTheQuickActionCopyPath_FileKeepsCopyPath()
        {
            var s = Fresh();
            var folder = ClickActions.Options(s, ClickGesture.Middle, false).Select(p => p.Key).ToList();
            Assert.That(folder, Does.Not.Contain(ClickActions.CopyPath));
            Assert.That(folder, Does.Contain(ClickActions.QuickCopyPath));
            Assert.That(ClickActions.IsValid(s, ClickGesture.Middle, false, ClickActions.CopyPath), Is.False);
            var file = ClickActions.Options(s, ClickGesture.Middle, true).Select(p => p.Key).ToList();
            Assert.That(file, Does.Contain(ClickActions.CopyPath));
        }

        [Test]
        public void Normalize_KeepsAQuickActionThatIsOff_SoTurningItOnAgainRestoresTheClick()
        {
            var s = Fresh();
            var off = s.QuickActions.First(a => !a.Enabled);
            ClickActions.Set(s, ClickGesture.Middle, false, ClickActions.QuickPrefix + off.Id);
            s.Normalize();
            Assert.That(ClickActions.Resolve(s, ClickGesture.Middle, false), Is.EqualTo(ClickActions.QuickPrefix + off.Id));
        }

        [Test]
        public void Normalize_DropsAClickBoundToADeletedCustomCommand()
        {
            var s = Fresh();
            var id = CustomCommandTemplate.IdPrefix + "demo";
            s.CustomCommands.Add(new CustomCommand { Id = id, Name = "Demo", Template = "notepad.exe {dir}" });
            s.Normalize();
            ClickActions.Set(s, ClickGesture.Middle, false, ClickActions.QuickPrefix + id);
            Assert.That(ClickActions.Resolve(s, ClickGesture.Middle, false), Is.EqualTo(ClickActions.QuickPrefix + id));

            s.CustomCommands.Clear();
            s.Normalize();
            Assert.That(s.ClickActions, Is.Null);
            Assert.That(ClickActions.Resolve(s, ClickGesture.Middle, false), Is.EqualTo(ClickActions.None));
        }

        [Test]
        public void SettingsJson_WithoutClickActions_LoadsWithDefaults_AndSavesNoKey()
        {
            var s = Fresh();
            var json = Serialize(s);
            Assert.That(json, Does.Not.Contain("clickActions"));

            ClickActions.Set(s, ClickGesture.Middle, false, ClickActions.QuickCopyPath);
            json = Serialize(s);
            Assert.That(json, Does.Contain("\"clickActions\""));
            var back = Deserialize(json);
            back.Normalize();
            Assert.That(ClickActions.Resolve(back, ClickGesture.Middle, false), Is.EqualTo(ClickActions.QuickCopyPath));
            Assert.That(ClickActions.Resolve(back, ClickGesture.Middle, true), Is.EqualTo(ClickActions.None));
        }

        private static string Serialize(AppSettings s)
        {
            var ser = new DataContractJsonSerializer(typeof(AppSettings));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, s);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static AppSettings Deserialize(string json)
        {
            var ser = new DataContractJsonSerializer(typeof(AppSettings));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (AppSettings)ser.ReadObject(ms);
        }

        // ---- what an action means on a row ----------------------------------------

        [TestCase(ClickActions.Open, "OpenHere")]
        [TestCase(ClickActions.NewWindow, "OpenNewWindow")]
        [TestCase(ClickActions.OpenParent, "OpenParent")]
        [TestCase(ClickActions.CopyPath, "CopyPath")]
        [TestCase(ClickActions.None, "Nothing")]
        [TestCase("quick:open-terminal", "QuickAction")]
        public void OutcomeFor_FolderInExplorer(string action, string expected)
        {
            Assert.That(ClickActions.OutcomeFor(action, false, true, false).ToString(), Is.EqualTo(expected));
        }

        [Test]
        public void OutcomeFor_NewWindowInADialog_OpensInTheDialog()
        {
            Assert.That(ClickActions.OutcomeFor(ClickActions.NewWindow, false, true, true), Is.EqualTo(ClickOutcome.OpenHere));
        }

        [TestCase(ClickActions.Open, "Nothing")]
        [TestCase(ClickActions.NewWindow, "Nothing")]
        [TestCase(ClickActions.OpenParent, "Nothing")]
        [TestCase("quick:open-terminal", "Nothing")]
        [TestCase(ClickActions.CopyPath, "CopyPath")]
        public void OutcomeFor_MissingFolder_OnlyCopiesItsPath(string action, string expected)
        {
            Assert.That(ClickActions.OutcomeFor(action, false, false, false).ToString(), Is.EqualTo(expected));
        }

        [TestCase(ClickActions.Open, "OpenFile")]
        [TestCase(ClickActions.OpenContainingFolder, "OpenContainingFolder")]
        [TestCase(ClickActions.CopyPath, "CopyPath")]
        [TestCase(ClickActions.NewWindow, "Nothing")]
        [TestCase(ClickActions.None, "Nothing")]
        public void OutcomeFor_File(string action, string expected)
        {
            Assert.That(ClickActions.OutcomeFor(action, true, true, false).ToString(), Is.EqualTo(expected));
        }

        [Test]
        public void OutcomeFor_MissingFile_StillTriesToOpen_AsBefore()
        {
            Assert.That(ClickActions.OutcomeFor(ClickActions.Open, true, false, false), Is.EqualTo(ClickOutcome.OpenFile));
        }

        // ---- a release counts only after a press on the same row --------------------

        [TestCase(MouseButton.Left)]
        [TestCase(MouseButton.Middle)]
        public void RowPress_PressThenRelease_IsAClick(MouseButton button)
        {
            var p = new RowPress();
            p.Down(button);
            Assert.That(p.Up(button), Is.True);
        }

        [TestCase(MouseButton.Left)]
        [TestCase(MouseButton.Middle)]
        public void RowPress_ReleaseWithNoPressOnTheRow_IsNotAClick(MouseButton button)
        {
            Assert.That(new RowPress().Up(button), Is.False);
        }

        [Test]
        public void RowPress_PressElsewhereThenEnterAndRelease_IsNotAClick()
        {
            // A press left over from an earlier visit is forgotten when the pointer comes back.
            var p = new RowPress();
            p.Down(MouseButton.Left);
            p.Cancel();   // left the row
            p.Cancel();   // came back with the button still down
            Assert.That(p.Up(MouseButton.Left), Is.False);
        }

        [Test]
        public void RowPress_OneReleasePerPress_AndButtonsAreSeparate()
        {
            var p = new RowPress();
            p.Down(MouseButton.Middle);
            Assert.That(p.Up(MouseButton.Left), Is.False, "a left release does not complete a middle press");
            Assert.That(p.Up(MouseButton.Middle), Is.True);
            Assert.That(p.Up(MouseButton.Middle), Is.False, "the press is used up");
        }

        [Test]
        public void RowPress_RightButton_NeverCounts()
        {
            var p = new RowPress();
            p.Down(MouseButton.Right);
            Assert.That(p.Up(MouseButton.Right), Is.False);
        }

        // ---- when a quick action may run ------------------------------------------

        [Test]
        public void RunnableQuickAction_NeedsTheSectionAndTheActionOn()
        {
            var s = Fresh();
            var id = "copy-path";
            string why;

            s.Sections.Single(x => x.Type == "quickactions").Visible = false;
            Assert.That(ClickActions.RunnableQuickAction(s, ClickActions.QuickPrefix + id, out why), Is.Null);
            Assert.That(why, Does.Contain("section"));

            s.Sections.Single(x => x.Type == "quickactions").Visible = true;
            s.QuickActions.Single(a => a.Id == id).Enabled = false;
            Assert.That(ClickActions.RunnableQuickAction(s, ClickActions.QuickPrefix + id, out why), Is.Null);
            Assert.That(why, Does.Contain("off"));

            s.QuickActions.Single(a => a.Id == id).Enabled = true;
            Assert.That(ClickActions.RunnableQuickAction(s, ClickActions.QuickPrefix + id, out why).Id, Is.EqualTo(id));
        }

        [Test]
        public void RunnableQuickAction_RefusesOneThatDoesNotApplyToARow()
        {
            var s = Fresh();
            s.Sections.Single(x => x.Type == "quickactions").Visible = true;
            foreach (var a in s.QuickActions) a.Enabled = true;
            string why;
            Assert.That(ClickActions.RunnableQuickAction(s, ClickActions.QuickPrefix + "pin-folder", out why), Is.Null);
        }

        [Test]
        public void RunnableQuickAction_RefusesACustomCommandARestoredBackupTurnedOff()
        {
            var s = Fresh();
            var id = CustomCommandTemplate.IdPrefix + "demo";
            s.CustomCommands.Add(new CustomCommand { Id = id, Name = "Demo", Template = "notepad.exe {dir}" });
            s.Normalize();
            s.Sections.Single(x => x.Type == "quickactions").Visible = true;
            s.QuickActions.Single(a => a.Id == id).Enabled = true;
            string why;
            Assert.That(ClickActions.RunnableQuickAction(s, ClickActions.QuickPrefix + id, out why), Is.Not.Null);

            s.TurnOffCustomCommands();
            Assert.That(ClickActions.RunnableQuickAction(s, ClickActions.QuickPrefix + id, out why), Is.Null);
        }
    }
}
