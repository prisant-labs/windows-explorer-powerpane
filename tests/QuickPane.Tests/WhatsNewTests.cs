using NUnit.Framework;
using QuickPane.Services;
using QuickPane.UI.Settings.Pages;

namespace QuickPane.Tests
{
    /// <summary>#208: the About page's summary is written by hand at each release, so these tests are
    /// what stop a release from shipping the last release's text.</summary>
    [TestFixture]
    public class WhatsNewTests
    {
        // When this fails after a version bump, the fix is to rewrite WhatsNew.cs for the new version,
        // not to change only its Version.
        [Test]
        public void Version_MatchesTheAssembly()
        {
            string assembly = typeof(WhatsNew).Assembly.GetName().Version.ToString();
            Assert.That(WhatsNew.Version, Is.EqualTo(assembly),
                "WhatsNew.cs still describes another version. See the release steps in docs/DEVELOPMENT.md.");
        }

        [Test]
        public void Items_AreAShortListWithNoEmptyText()
        {
            Assert.That(WhatsNew.Items.Length, Is.InRange(1, 6));
            foreach (var item in WhatsNew.Items)
            {
                Assert.That(item.Title, Is.Not.Null.And.Not.Empty);
                Assert.That(item.Summary, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        public void NotesLink_NamesThisVersionsTagInThisRepository()
        {
            Assert.That(AboutPage.ThisVersionNotesUrl, Is.EqualTo(
                "https://github.com/prisant-labs/windows-explorer-quickpane/releases/tag/v" + WhatsNew.Version));
        }

        [Test]
        public void Title_UsesTheSameFormAsTheAboutHeader()
        {
            Assert.That(AboutPage.WhatsNewTitle(), Is.EqualTo("What's new in " + UpdateService.Display(UpdateService.Current)));
        }
    }
}
