using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuickPane.Models;
using QuickPane.UI;

namespace QuickPane.Tests
{
    /// <summary>Pins when the pane shows its profile row: only with a second profile to switch to, and
    /// never when the setting is off or the pane is collapsed to its strip.</summary>
    [TestFixture]
    public class SidebarControlTests
    {
        [Test]
        public void ProfileRow_OneProfile_IsHidden()
        {
            Assert.That(SidebarControl.ShouldShowProfileTabs(true, false, 1), Is.False);
        }

        [Test]
        public void ProfileRow_NoProfiles_IsHidden()
        {
            Assert.That(SidebarControl.ShouldShowProfileTabs(true, false, 0), Is.False);
        }

        [TestCase(2)]
        [TestCase(5)]
        public void ProfileRow_TwoOrMoreProfiles_Shows(int count)
        {
            Assert.That(SidebarControl.ShouldShowProfileTabs(true, false, count), Is.True);
        }

        [Test]
        public void ProfileRow_SettingOff_IsHiddenWhateverTheCount()
        {
            Assert.That(SidebarControl.ShouldShowProfileTabs(false, false, 3), Is.False);
        }

        [Test]
        public void ProfileRow_PaneCollapsed_IsHidden()
        {
            Assert.That(SidebarControl.ShouldShowProfileTabs(true, true, 3), Is.False);
        }

        // ---- PaneShapeKey (#51) ----------------------------------------------

        private static AppSettings MakeSettings()
        {
            var s = AppSettings.CreateDefault();
            s.Profiles = new List<Profile>
            {
                new Profile { Name = "Profile 1", GroupsPath = @"C:\Demo\Groups", RecentsMaxCount = 15, SidebarWidthPx = 220 }
            };
            s.SshProfiles = new List<SshProfile>
            {
                new SshProfile { Name = "Home", Hostname = "home.example", DriveLetter = "S:" }
            };
            // Populates QuickActions (null straight out of CreateDefault) with every built-in, so the
            // Quick actions tests below have something to toggle and reorder. Every other field above
            // is already a valid, normalized value, so this changes nothing the other PaneShapeKey
            // tests check.
            s.Normalize();
            return s;
        }

        [Test]
        public void PaneShapeKey_WidthChange_KeepsTheSameKey()
        {
            var a = MakeSettings();
            var b = MakeSettings();
            b.SidebarWidthPx = 300;
            b.Profiles[0].SidebarWidthPx = 300;

            Assert.That(SidebarControl.PaneShapeKey(b), Is.EqualTo(SidebarControl.PaneShapeKey(a)));
        }

        [Test]
        public void PaneShapeKey_RecentsMaxCountChange_KeepsTheSameKey()
        {
            var a = MakeSettings();
            var b = MakeSettings();
            b.RecentsMaxCount = 40;
            b.Profiles[0].RecentsMaxCount = 40;

            Assert.That(SidebarControl.PaneShapeKey(b), Is.EqualTo(SidebarControl.PaneShapeKey(a)));
        }

        [Test]
        public void PaneShapeKey_SectionTitleChange_ChangesTheKey()
        {
            var a = MakeSettings();
            var b = MakeSettings();
            b.Sections.First(x => x.Type == "groups").Title = "Pinned";

            Assert.That(SidebarControl.PaneShapeKey(b), Is.Not.EqualTo(SidebarControl.PaneShapeKey(a)));
        }

        [Test]
        public void PaneShapeKey_SectionOrderChange_ChangesTheKey()
        {
            var a = MakeSettings();
            var b = MakeSettings();
            b.Sections.First(x => x.Type == "groups").Order = 99;

            Assert.That(SidebarControl.PaneShapeKey(b), Is.Not.EqualTo(SidebarControl.PaneShapeKey(a)));
        }

        [Test]
        public void PaneShapeKey_SectionVisibilityChange_ChangesTheKey()
        {
            var a = MakeSettings();
            var b = MakeSettings();
            b.Sections.First(x => x.Type == "network").Visible = false;

            Assert.That(SidebarControl.PaneShapeKey(b), Is.Not.EqualTo(SidebarControl.PaneShapeKey(a)));
        }

        [Test]
        public void PaneShapeKey_ActiveProfileChange_ChangesTheKey()
        {
            var a = MakeSettings();
            var b = MakeSettings();
            b.Profiles.Add(new Profile { Name = "Profile 2", GroupsPath = @"C:\Demo\Groups2" });
            a.Profiles.Add(new Profile { Name = "Profile 2", GroupsPath = @"C:\Demo\Groups2" });
            b.ActiveProfileIndex = 1;

            Assert.That(SidebarControl.PaneShapeKey(b), Is.Not.EqualTo(SidebarControl.PaneShapeKey(a)));
        }

        [Test]
        public void PaneShapeKey_SshNameChange_ChangesTheKey()
        {
            var a = MakeSettings();
            var b = MakeSettings();
            b.SshProfiles[0].Name = "Office";

            Assert.That(SidebarControl.PaneShapeKey(b), Is.Not.EqualTo(SidebarControl.PaneShapeKey(a)));
        }

        [Test]
        public void PaneShapeKey_SshHostnameChange_KeepsTheSameKey()
        {
            var a = MakeSettings();
            var b = MakeSettings();
            b.SshProfiles[0].Hostname = "office.example";

            Assert.That(SidebarControl.PaneShapeKey(b), Is.EqualTo(SidebarControl.PaneShapeKey(a)));
        }

        // ---- property-coverage: every settable property is accounted for ----

        // "Key" fields change which sections a pane shows, their order or title, or the profile tab
        // row; "not rendered" fields are read elsewhere (a hotkey, a dock window, an SSH credential)
        // or only once a section is already built. A new AppSettings/Profile/SshProfile/SectionSetting
        // property that lands in neither list fails this test, so it gets a deliberate answer instead
        // of silently leaving every pane stale (#51).
        private static readonly string[] AppSettingsKeyFields =
        {
            "Sections", "RecentAppsSource", "SshProfiles", "Profiles", "ActiveProfileIndex",
            "ShowProfileTabs", "ProfileTabsAutoHide", "GroupsPath", "KeyboardNav", "Mode", "DialogMode",
            "QuickActions"
        };

        private static readonly string[] AppSettingsNotRendered =
        {
            "KeyboardNavHotkey", "RecentsMaxCount", "SidebarWidthPx",
            "DesktopDock", "DesktopDockAutoHide", "DesktopDockAllDesktops", "DesktopDockDisplay", "DesktopDockEdge",
            "ActiveProfile", // derived from Profiles + ActiveProfileIndex, both already in the key
            "PaneColor" // live through SidebarBackground's DynamicResource binding, no rebuild (#134)
        };

        private static readonly string[] ProfileKeyFields = { "Name", "GroupsPath" };
        private static readonly string[] ProfileNotRendered = { "RecentsMaxCount", "SidebarWidthPx" };

        private static readonly string[] SshProfileKeyFields = { "Name", "DriveLetter" };
        private static readonly string[] SshProfileNotRendered =
        {
            "Hostname", "Port", "Username", "AuthMethod", "PrivateKeyPath", "Password",
            "RemotePath", "KeepAliveInterval", "KeepAliveCountMax", "Reconnect"
        };

        private static readonly string[] SectionSettingKeyFields = { "Type", "Visible", "Order", "Title" };
        private static readonly string[] SectionSettingNotRendered = new string[0];

        private static readonly string[] QuickActionSettingKeyFields = { "Id", "Enabled" };
        private static readonly string[] QuickActionSettingNotRendered = new string[0];

        private static void AssertEveryPropertyIsAccountedFor(Type t, string[] keyFields, string[] notRendered)
        {
            var names = t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();

            foreach (var name in names)
            {
                bool inKey = keyFields.Contains(name);
                bool inNotRendered = notRendered.Contains(name);
                Assert.That(inKey ^ inNotRendered, Is.True,
                    t.Name + "." + name + " must be in exactly one of PaneShapeKey's key-field or not-rendered lists.");
            }

            // Catches a renamed or removed property left behind in either list.
            foreach (var name in keyFields.Concat(notRendered))
                Assert.That(names.Contains(name), Is.True, t.Name + " has no public property named \"" + name + "\".");
        }

        [Test]
        public void PaneShapeKey_AppSettingsProperties_AreAllAccountedFor()
        {
            AssertEveryPropertyIsAccountedFor(typeof(AppSettings), AppSettingsKeyFields, AppSettingsNotRendered);
        }

        [Test]
        public void PaneShapeKey_ProfileProperties_AreAllAccountedFor()
        {
            AssertEveryPropertyIsAccountedFor(typeof(Profile), ProfileKeyFields, ProfileNotRendered);
        }

        [Test]
        public void PaneShapeKey_SshProfileProperties_AreAllAccountedFor()
        {
            AssertEveryPropertyIsAccountedFor(typeof(SshProfile), SshProfileKeyFields, SshProfileNotRendered);
        }

        [Test]
        public void PaneShapeKey_SectionSettingProperties_AreAllAccountedFor()
        {
            AssertEveryPropertyIsAccountedFor(typeof(SectionSetting), SectionSettingKeyFields, SectionSettingNotRendered);
        }

        [Test]
        public void PaneShapeKey_QuickActionSettingProperties_AreAllAccountedFor()
        {
            AssertEveryPropertyIsAccountedFor(typeof(QuickActionSetting), QuickActionSettingKeyFields, QuickActionSettingNotRendered);
        }

        // ---- PaneShapeKey: the Quick actions list itself (quick actions, first version) --------------

        // The action list only matters once the section itself is visible (PaneShapeKey skips it
        // otherwise, so toggling an action behind an off switch never rebuilds every open pane for
        // nothing), so each of these turns it on first.
        private static void ShowQuickActions(AppSettings s)
        {
            s.Sections.First(x => x.Type == "quickactions").Visible = true;
        }

        [Test]
        public void PaneShapeKey_QuickActionToggled_ChangesTheKey()
        {
            var a = MakeSettings();
            var b = MakeSettings();
            ShowQuickActions(a);
            ShowQuickActions(b);
            b.QuickActions[0].Enabled = !b.QuickActions[0].Enabled;

            Assert.That(SidebarControl.PaneShapeKey(b), Is.Not.EqualTo(SidebarControl.PaneShapeKey(a)));
        }

        [Test]
        public void PaneShapeKey_QuickActionsReordered_ChangesTheKey()
        {
            var a = MakeSettings();
            var b = MakeSettings();
            ShowQuickActions(a);
            ShowQuickActions(b);
            var moved = b.QuickActions[0];
            b.QuickActions.RemoveAt(0);
            b.QuickActions.Add(moved);

            Assert.That(SidebarControl.PaneShapeKey(b), Is.Not.EqualTo(SidebarControl.PaneShapeKey(a)));
        }

        [Test]
        public void PaneShapeKey_QuickActionToggled_WhileSectionIsHidden_KeepsTheSameKey()
        {
            // The switch (Sections["quickactions"].Visible) is off by default in MakeSettings, so
            // nothing on screen reads the action list yet.
            var a = MakeSettings();
            var b = MakeSettings();
            b.QuickActions[0].Enabled = !b.QuickActions[0].Enabled;

            Assert.That(SidebarControl.PaneShapeKey(b), Is.EqualTo(SidebarControl.PaneShapeKey(a)));
        }

        [Test]
        public void PaneShapeKey_QuickActionsSectionVisibilityChange_ChangesTheKey()
        {
            var a = MakeSettings();
            var b = MakeSettings();
            b.Sections.First(x => x.Type == "quickactions").Visible = true;

            Assert.That(SidebarControl.PaneShapeKey(b), Is.Not.EqualTo(SidebarControl.PaneShapeKey(a)));
        }
    }
}
