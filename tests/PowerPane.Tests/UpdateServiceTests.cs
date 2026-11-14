using System;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>Pins TryParseTag's contract: the version can sit anywhere in the tag string, and a
    /// tag with no recognizable version returns false rather than throwing. Also pins Display, which
    /// must keep a fork build's fourth part.</summary>
    [TestFixture]
    public class UpdateServiceTests
    {
        [Test]
        public void TryParseTag_PlainVersionTag_Parses()
        {
            Version v;
            bool ok = UpdateService.TryParseTag("v3.9.0", out v);

            Assert.That(ok, Is.True);
            Assert.That(v, Is.EqualTo(new Version(3, 9, 0)));
        }

        [Test]
        public void TryParseTag_VersionBuriedInInstallerName_Parses()
        {
            Version v;
            bool ok = UpdateService.TryParseTag("QuickPaneSetup-3.7.0-release", out v);

            Assert.That(ok, Is.True);
            Assert.That(v, Is.EqualTo(new Version(3, 7, 0)));
        }

        [Test]
        public void TryParseTag_Junk_Fails()
        {
            Version v;
            Assert.That(UpdateService.TryParseTag("junk", out v), Is.False);
        }

        [Test]
        public void TryParseTag_Empty_Fails()
        {
            Version v;
            Assert.That(UpdateService.TryParseTag("", out v), Is.False);
        }

        [Test]
        public void TryParseTag_Null_Fails()
        {
            Version v;
            Assert.That(UpdateService.TryParseTag(null, out v), Is.False);
        }

        [Test]
        public void TryParseTag_ForkTag_KeepsFourthPart()
        {
            Version v;
            bool ok = UpdateService.TryParseTag("v3.10.0.1", out v);

            Assert.That(ok, Is.True);
            Assert.That(v, Is.EqualTo(new Version(3, 10, 0, 1)));
        }

        [Test]
        public void ForkBuild_IsNewerThanTheUpstreamReleaseItForked()
        {
            // An upstream tag parses with no revision (-1), so a fork build on top of it must not see
            // that release as an update.
            Assert.That(new Version(3, 10, 0, 1) > new Version(3, 10, 0), Is.True);
        }

        [TestCase(3, 10, 0, -1, "3.10.0")]
        [TestCase(3, 10, 0, 0, "3.10.0")]
        [TestCase(3, 10, 0, 1, "3.10.0.1")]
        [TestCase(3, 11, -1, -1, "3.11")]
        public void Display_SpellsTheVersionLikeItsTag(int major, int minor, int build, int revision, string expected)
        {
            Version v = build < 0 ? new Version(major, minor)
                : revision < 0 ? new Version(major, minor, build)
                : new Version(major, minor, build, revision);

            Assert.That(UpdateService.Display(v), Is.EqualTo(expected));
        }

        [Test]
        public void Display_Null_IsEmpty()
        {
            Assert.That(UpdateService.Display(null), Is.EqualTo(""));
        }

        // ---- comparing a tag with the running program -----------------------------------------------

        [TestCase("v4.1.0", "4.0.0.1", true, Description = "the first Semantic Versioning release reaches 4.0.0.1")]
        [TestCase("v4.1.0", "4.1.0.0", false, Description = "a program never offers itself as an update")]
        [TestCase("v4.1.1", "4.1.0.0", true)]
        [TestCase("v4.1.0", "4.1.1.0", false)]
        [TestCase("v5.0.0", "4.9.9.0", true)]
        [TestCase("v4.0.0.1", "4.1.0.0", false, Description = "an older four-part tag is not newer")]
        [TestCase("v4.0.0.2", "4.0.0.1", true)]
        public void IsNewer_ComparesThreeAndFourPartVersions(string tag, string current, bool expected)
        {
            System.Version latest;
            Assert.That(UpdateService.TryParseTag(tag, out latest), Is.True);
            Assert.That(UpdateService.IsNewer(latest, new System.Version(current)), Is.EqualTo(expected));
        }

        [Test]
        public void Display_SpellsAVersionAsItsTagDoes()
        {
            Assert.That(UpdateService.Display(new System.Version(4, 1, 0, 0)), Is.EqualTo("4.1.0"));
            Assert.That(UpdateService.Display(new System.Version(4, 0, 0, 1)), Is.EqualTo("4.0.0.1"));
        }
    }
}
