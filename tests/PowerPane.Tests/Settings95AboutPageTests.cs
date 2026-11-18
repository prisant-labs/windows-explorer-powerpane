using System;
using NUnit.Framework;
using PowerPane.Services;
using PowerPane.UI.Settings.Pages;

namespace PowerPane.Tests
{
    /// <summary>Exercises AboutPage's testable decisions with no <see cref="UpdateService"/> call
    /// and no window, matching the old footer's own four status branches (SettingsPanel.xaml.cs's
    /// BuildFooter, read at commit 068301b) and the fork's own repository URLs.</summary>
    [TestFixture]
    public class Settings95AboutPageTests
    {
        [Test]
        public void StatusText_NullResult_ReportsFailure()
        {
            Assert.That(AboutPage.StatusText(null), Is.EqualTo("Update check failed"));
        }

        [Test]
        public void StatusText_ResultWithError_ReportsFailure()
        {
            var r = new UpdateCheckResult { Error = "timed out" };
            Assert.That(AboutPage.StatusText(r), Is.EqualTo("Update check failed"));
        }

        [Test]
        public void StatusText_NoUpdateAvailable_ReportsUpToDate()
        {
            var r = new UpdateCheckResult { Available = false };
            Assert.That(AboutPage.StatusText(r), Is.EqualTo("Up to date"));
        }

        [Test]
        public void StatusText_UpdateAvailable_NamesTheVersion()
        {
            var r = new UpdateCheckResult { Available = true, Latest = new Version(3, 11, 0, 1) };
            Assert.That(AboutPage.StatusText(r), Is.EqualTo("Version 3.11.0.1 is available"));
        }

        [Test]
        public void ShowDownload_OnlyWhenAvailableAndNoError()
        {
            Assert.That(AboutPage.ShowDownload(null), Is.False);
            Assert.That(AboutPage.ShowDownload(new UpdateCheckResult { Available = true, Error = "x" }), Is.False);
            Assert.That(AboutPage.ShowDownload(new UpdateCheckResult { Available = false }), Is.False);
            Assert.That(AboutPage.ShowDownload(new UpdateCheckResult { Available = true }), Is.True);
        }

        [Test]
        public void DownloadLabel_NamesTheVersion()
        {
            Assert.That(AboutPage.DownloadLabel(new Version(3, 11, 0, 1)), Is.EqualTo("Download 3.11.0.1"));
        }

        [Test]
        public void Links_AllPointAtTheForksOwnRepository_NotUpstream()
        {
            foreach (var link in AboutPage.Links)
            {
                Assert.That(link.Url, Does.StartWith("https://github.com/prisant-labs/windows-explorer-powerpane"),
                    "a fork build must never point at upstream's repository, issues, or release notes");
                Assert.That(link.Title, Is.Not.Null.And.Not.Empty);
                Assert.That(link.AccessibleName, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        public void Links_HasSourceChangelogAndIssueTracker()
        {
            var titles = Array.ConvertAll(AboutPage.Links, l => l.Title);
            Assert.That(titles, Has.Some.Contains("Source code"));
            Assert.That(titles, Has.Some.Contains("Changelog"));
            Assert.That(titles, Has.Some.Contains("Report an issue"));
        }

        [Test]
        public void SupportAuthorLink_PointsAtTheOriginalAuthorsDonationPage()
        {
            // The one deliberate exception to the rule above (#266): it credits QuickPane's author.
            Assert.That(AboutPage.SupportAuthorUrl, Is.EqualTo("https://plexpixel.com/donate"));
            Assert.That(AboutPage.SupportAuthorTitle, Is.EqualTo("Support QuickPane's author"));
            Assert.That(Array.ConvertAll(AboutPage.Links, l => l.Url), Has.None.EqualTo(AboutPage.SupportAuthorUrl),
                "the support link has its own card, so the Links card stays on this repository");
        }

        [Test]
        public void SearchEntries_CoverUpdateAndVersionAndLinks()
        {
            var page = new AboutPage();
            var entries = new System.Collections.Generic.List<string>();
            foreach (var e in page.SearchEntries)
            {
                Assert.That(e.PageId, Is.EqualTo("about"));
                entries.Add(e.Title);
            }

            Assert.That(entries, Has.Some.Contains("Check for updates"));
            Assert.That(entries, Has.Some.Contains("licence"));
            Assert.That(entries, Has.Some.Contains("Source code"));
            Assert.That(entries, Has.Some.Contains("Support QuickPane's author"));
        }

        [Test]
        public void ThirdPartyNotice_NamesTheFileThatShipsWithTheApp()
        {
            // The card once named PowerPane/Themes/Fluent/NOTICE.md, a path only a source clone has.
            // package.ps1 and Installer.iss put THIRD-PARTY-NOTICES.txt beside the exe (#278).
            Assert.That(AboutPage.ThirdPartyNoticeText, Does.Contain("THIRD-PARTY-NOTICES.txt"));
            Assert.That(AboutPage.ThirdPartyNoticeText, Does.Not.Contain("PowerPane/Themes/Fluent"));
        }
    }
}
