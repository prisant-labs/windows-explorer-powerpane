using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NUnit.Framework;
using QuickPane.Explorer;
using QuickPane.Models;
using QuickPane.Services;
using QuickPane.UI.Settings;
using QuickPane.UI.Settings.Pages;

namespace QuickPane.Tests
{
    /// <summary>Builds every Settings page, the Groups profile sub-page, and the parts that only
    /// appear once background work returns, with no window (#120). The 3.11.0.0 Pane page threw
    /// on every build, and CI never saw it, because no test built a page.
    ///
    /// Each page gets a fake <see cref="ISettingsShell"/>, and SettingsStore and GroupStore point
    /// at a temporary directory, including every profile's groups folder. Two seams keep the
    /// pages off the real machine: About's update check never calls GitHub, and SSH's install
    /// check never probes the disk. WorkQueue.PostUI drops work when no Application exists, so
    /// the group rows and the SSH setup cards are built through their own internal entry points
    /// rather than through the callbacks that would add them in the real window.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class SettingsPageBuildTests
    {
        private sealed class FakeShell : ISettingsShell
        {
            public readonly List<Tuple<string, Func<SettingsPageContext, UIElement>>> SubPages =
                new List<Tuple<string, Func<SettingsPageContext, UIElement>>>();
            public int Rebuilds;

            public int RenderGeneration { get { return 1; } }

            public void OpenSubPage(string title, Func<SettingsPageContext, UIElement> build)
            {
                SubPages.Add(Tuple.Create(title, build));
            }

            public void Rebuild() { Rebuilds++; }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly TempDir Temp = new TempDir();
            public readonly SettingsStore Settings;
            public readonly GroupStore Groups;
            public readonly SettingsCards Cards = new SettingsCards();
            public readonly FakeShell Shell = new FakeShell();
            public readonly string WorkRoot, HomeRoot;

            public Fixture()
            {
                WorkRoot = Path.Combine(Temp.Path, "WorkGroups");
                HomeRoot = Path.Combine(Temp.Path, "HomeGroups");
                Directory.CreateDirectory(Path.Combine(WorkRoot, "01_Projects", "01_Main"));
                Directory.CreateDirectory(Path.Combine(WorkRoot, "02_Reference", "01_Main"));
                Directory.CreateDirectory(HomeRoot);

                Settings = new SettingsStore(Path.Combine(Temp.Path, "Settings"));
                Settings.Load();
                // The default groups path is the real %APPDATA%\QuickPane\Groups, which
                // GroupsPage lists in the background as soon as it builds.
                var s = Settings.Current;
                s.GroupsPath = WorkRoot;
                s.Profiles = new List<Profile>
                {
                    new Profile { Name = "Work", GroupsPath = WorkRoot },
                    new Profile { Name = "Home", GroupsPath = HomeRoot },
                };
                s.ActiveProfileIndex = 0;
                s.SshProfiles = new List<SshProfile>
                {
                    new SshProfile { Name = "Demo server", Hostname = "demo.example.com" },
                };

                Groups = new GroupStore(Settings, info => { });
            }

            public SettingsPageContext Context()
            {
                Cards.BeginPage();   // as SettingsWindow.RenderCurrentPage does before every build
                return new SettingsPageContext(Settings, Groups, Cards, Shell, 1);
            }

            public List<SettingsPage> Pages(bool sshInstalled = true)
            {
                var pages = SettingsWindow.BuildPages();
                foreach (var about in pages.OfType<AboutPage>()) about.CheckForUpdates = done => { };
                foreach (var ssh in pages.OfType<SshPage>()) ssh.SetInstallState(sshInstalled);
                return pages;
            }

            public SettingsPage Page(string id, bool sshInstalled = true)
            {
                return Pages(sshInstalled).Single(p => p.Id == id);
            }

            public void Dispose() { Temp.Dispose(); }
        }

        private static IEnumerable<string> PageIds()
        {
            return SettingsWindow.BuildPages().Select(p => p.Id);
        }

        // ---- every page ------------------------------------------------------

        [TestCaseSource(nameof(PageIds))]
        public void Page_Builds(string id)
        {
            using (var f = new Fixture())
            {
                var page = f.Page(id);
                UIElement body = null;
                Assert.DoesNotThrow(() => body = page.Build(f.Context()));
                Assert.That(body, Is.Not.Null);
            }
        }

        [TestCaseSource(nameof(PageIds))]
        public void Page_BuildsTwice(string id)
        {
            // The window builds a page again on every visit and every Rebuild, with the same page
            // instance, so state a page keeps between builds must survive a second one.
            using (var f = new Fixture())
            {
                var page = f.Page(id);
                page.Build(f.Context());
                Assert.DoesNotThrow(() => page.Build(f.Context()));
            }
        }

        [TestCaseSource(nameof(PageIds))]
        public void Page_EveryInteractiveControlHasAnAccessibleName(string id)
        {
            using (var f = new Fixture())
            {
                var body = f.Page(id).Build(f.Context());
                var unnamed = UnnamedControls(body);
                Assert.That(unnamed, Is.Empty, "controls with no accessible name on the " + id + " page");
            }
        }

        // ---- every search entry lands on a real control (#164) --------------

        // Card ids a page's search index legitimately points at with nothing to focus (mirrors the
        // null-CardId entries already in SearchEntries, e.g. About's "App version and licence" --
        // those are skipped below without needing an entry here, since their CardId is null). A
        // genuine miss, a non-null CardId no builder ever registered, gets a one-line Register call
        // in that page's Build() instead of being added to this set.
        private static readonly HashSet<string> NoFocusTargetAllowed = new HashSet<string>();

        [TestCaseSource(nameof(PageIds))]
        public void Page_EverySearchEntryCardId_RegistersAFocusTarget(string id)
        {
            using (var f = new Fixture())
            {
                var page = f.Page(id);
                page.Build(f.Context());   // Context() calls Cards.BeginPage() first, as the window does

                var missing = page.SearchEntries
                    .Where(e => e.CardId != null && !NoFocusTargetAllowed.Contains(e.CardId))
                    .Select(e => e.CardId)
                    .Where(cardId => !f.Cards.HasFocusTarget(cardId))
                    .ToList();
                Assert.That(missing, Is.Empty, "search entries on the " + id + " page with no registered focus target");
            }
        }

        [Test]
        public void PanePage_DisconnectedSavedDisplay_ComboSelectsItsOwnEntry()
        {
            // An invented id no real monitor from MonitorPicker.DescribeMonitors() can ever match,
            // so this is deterministic regardless of how many displays this machine actually has
            // (#162) -- unlike a "still connected" case, which would need a real monitor.
            using (var f = new Fixture())
            {
                f.Settings.Current.DesktopDockDisplay = "demo-not-connected-id";
                var body = f.Page("pane").Build(f.Context());
                var combo = Descendants(body).OfType<ComboBox>()
                    .Single(c => AutomationProperties.GetName(c) == "Which display the desktop dock sits on");

                var selected = combo.SelectedItem as ComboBoxItem;
                Assert.That(selected, Is.Not.Null);
                Assert.That((string)selected.Tag, Is.EqualTo("demo-not-connected-id"));

                // "Primary display" is still offered, and it is a different (unselected) item --
                // before #162 it looked already selected, so picking it again saved nothing.
                var primaryItem = combo.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag == MonitorPicker.PrimaryDisplayId);
                Assert.That(primaryItem, Is.Not.SameAs(selected));
            }
        }

        // ---- Groups profile sub-page ---------------------------------------

        [TestCase("Work")]
        [TestCase("Home")]
        public void GroupsPage_ProfileCard_OpensASubPageThatBuilds(string profileName)
        {
            using (var f = new Fixture())
            {
                var body = f.Page("groups").Build(f.Context());
                var card = Descendants(body).OfType<Button>()
                    .Single(b => AutomationProperties.GetName(b) == "Open profile " + profileName);

                card.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.That(f.Shell.SubPages, Has.Count.EqualTo(1));
                Assert.That(f.Shell.SubPages[0].Item1, Is.EqualTo(profileName));
                UIElement sub = null;
                Assert.DoesNotThrow(() => sub = f.Shell.SubPages[0].Item2(f.Context()));
                Assert.That(sub, Is.Not.Null);
                Assert.That(UnnamedControls(sub), Is.Empty, "controls with no accessible name on the " + profileName + " sub-page");
            }
        }

        [Test]
        public void GroupsPage_GroupRows_Build()
        {
            using (var f = new Fixture())
            {
                var page = (GroupsPage)f.Page("groups");
                var profile = f.Settings.Current.Profiles[0];
                var groups = GroupStore.ListGroups(f.WorkRoot);
                Assert.That(groups, Has.Count.EqualTo(2), "fixture should list two groups");

                FrameworkElement rows = null;
                Assert.DoesNotThrow(() => rows = page.BuildGroupRows(f.Context(), 0, profile, groups));
                Assert.That(rows, Is.Not.Null);
                Assert.That(UnnamedControls(rows), Is.Empty, "controls with no accessible name in the group rows");
            }
        }

        [Test]
        public void GroupsPage_GroupRows_BuildWithNoGroups()
        {
            using (var f = new Fixture())
            {
                var page = (GroupsPage)f.Page("groups");
                var profile = f.Settings.Current.Profiles[1];
                Assert.DoesNotThrow(() => page.BuildGroupRows(f.Context(), 1, profile, new List<Tuple<string, string>>()));
            }
        }

        // ---- states reached only after background work ----------------------

        [Test]
        public void SshPage_WithComponentsMissing_BuildsTheSetupCards()
        {
            using (var f = new Fixture())
            {
                var body = f.Page("ssh", sshInstalled: false).Build(f.Context());
                Assert.That(Texts(body), Has.Some.EqualTo("Remote drives need two free components"));
                Assert.That(UnnamedControls(body), Is.Empty, "controls with no accessible name on the SSH setup cards");
            }
        }

        [Test]
        public void AboutPage_EveryUpdateResult_UpdatesTheCard()
        {
            var results = new[]
            {
                null,
                new UpdateCheckResult { Error = "The demo server did not answer." },
                new UpdateCheckResult { Available = false },
                new UpdateCheckResult { Available = true, Latest = new Version(99, 0, 0, 0), DownloadUrl = "https://example.com/QuickPane-99.0.0.0.exe" },
            };
            foreach (var result in results)
            {
                using (var f = new Fixture())
                {
                    var page = (AboutPage)f.Page("about");
                    page.CheckForUpdates = done => done(result);
                    UIElement body = null;
                    Assert.DoesNotThrow(() => body = page.Build(f.Context()));
                    Assert.That(Texts(body), Has.Some.EqualTo(AboutPage.StatusText(result)));
                }
            }
        }

        // ---- tree walking ----------------------------------------------------

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                    stack.Push(child);
            }
        }

        // ---- what's new (#208) -------------------------------------------------

        [Test]
        public void AboutPage_ShowsEveryWhatsNewLine_AboveTheLicence()
        {
            using (var f = new Fixture())
            {
                var body = (DependencyObject)f.Page("about").Build(f.Context());
                var texts = Texts(body).ToList();
                Assert.That(texts, Has.Some.EqualTo(AboutPage.WhatsNewTitle()));
                foreach (var item in WhatsNew.Items)
                {
                    Assert.That(texts, Has.Some.EqualTo(item.Title));
                    Assert.That(texts, Has.Some.EqualTo(item.Summary));
                }

                // The panel holds the cards in order, and each card's own texts come from a walk of it.
                var cards = ((Panel)body).Children.Cast<DependencyObject>().ToList();
                int whatsNew = cards.FindIndex(c => Texts(c).Contains(AboutPage.WhatsNewTitle()));
                int licence = cards.FindIndex(c => Texts(c).Contains("Licence"));
                Assert.That(whatsNew, Is.GreaterThanOrEqualTo(0));
                Assert.That(licence, Is.GreaterThan(whatsNew));
            }
        }

        // ---- info buttons (#205) ----------------------------------------------

        private static Button InfoButtonFor(DependencyObject root, string title)
        {
            return Descendants(root).OfType<Button>()
                .FirstOrDefault(b => AutomationProperties.GetName(b) == "About " + title);
        }

        private static string TipText(Button button)
        {
            return ((TextBlock)((ToolTip)button.ToolTip).Content).Text;
        }

        [Test]
        public void SectionsPage_EverySectionHasAnInfoButtonWithItsDescription()
        {
            using (var f = new Fixture())
            {
                var body = (DependencyObject)f.Page("sections").Build(f.Context());
                foreach (var section in f.Settings.Current.Sections)
                {
                    string title = SectionsPage.Friendly(section.Type);
                    var button = InfoButtonFor(body, title);
                    Assert.That(button, Is.Not.Null, "no info button for " + title);
                    Assert.That(TipText(button), Is.EqualTo(SectionsPage.Describe(section.Type)));
                    Assert.That(AutomationProperties.GetHelpText(button), Is.EqualTo(SectionsPage.Describe(section.Type)));
                }
            }
        }

        [Test]
        public void QuickActionsPage_BuiltInRow_MovesItsDescriptionIntoTheInfoButton()
        {
            using (var f = new Fixture())
            {
                var body = (DependencyObject)f.Page("quickactions").Build(f.Context());
                foreach (var def in QuickActionLibrary.All)
                {
                    var button = InfoButtonFor(body, def.Name);
                    Assert.That(button, Is.Not.Null, "no info button for " + def.Name);
                    Assert.That(TipText(button), Is.EqualTo(def.Description));
                    // A ToolTip is no logical child of its button, so this walk sees only the row.
                    Assert.That(Texts(body), Has.None.EqualTo(def.Description),
                        "the description should appear only in the tooltip, for " + def.Name);
                }
            }
        }

        [Test]
        public void QuickActionsPage_CustomCommandRow_KeepsItsTemplateOnScreen()
        {
            using (var f = new Fixture())
            {
                QuickActionsPage.AddCustomCommand(f.Settings.Current, "Demo command", "demo-tool {path}");
                var body = (DependencyObject)f.Page("quickactions").Build(f.Context());
                Assert.That(InfoButtonFor(body, "Demo command"), Is.Null);
                Assert.That(Texts(body), Has.Some.EqualTo("demo-tool {path}"));
            }
        }

        private static IEnumerable<string> Texts(DependencyObject root)
        {
            return Descendants(root).OfType<TextBlock>().Select(t => t.Text);
        }

        /// <summary>Every control a keyboard or screen-reader user can operate whose automation
        /// peer reports no name. The peer is what Narrator and the sandbox jobs' click-by-name
        /// read, so a button whose only content is text still counts as named.</summary>
        private static List<string> UnnamedControls(DependencyObject root)
        {
            var unnamed = new List<string>();
            foreach (var el in Descendants(root).OfType<Control>())
            {
                if (!(el is ButtonBase || el is TextBoxBase || el is PasswordBox || el is ComboBox || el is Slider))
                    continue;
                var peer = UIElementAutomationPeer.CreatePeerForElement(el);
                string name = peer != null ? peer.GetName() : AutomationProperties.GetName(el);
                if (string.IsNullOrWhiteSpace(name))
                    unnamed.Add(el.GetType().Name + (el is ContentControl && ((ContentControl)el).Content is string ? " \"" + ((ContentControl)el).Content + "\"" : ""));
            }
            return unnamed;
        }
    }
}
