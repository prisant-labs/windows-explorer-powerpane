using System.IO;
using System.Text;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers AppendOnlyFile, the handle the log keeps open across lines instead of opening the file
    /// for every line (#46). Every file lives in the test's own temp folder.
    /// </summary>
    [TestFixture]
    public class AppendOnlyFileTests
    {
        // The log stays open while these read it, as an editor would, so read with full sharing.
        private static string ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        [Test]
        public void AppendLine_WritesEachLineWhileOpen()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "debug.log");
                using (var log = new AppendOnlyFile(path))
                {
                    log.AppendLine("first");
                    log.AppendLine("second");
                    // Flushed per line, so the text is on disk before the handle closes.
                    Assert.That(ReadShared(path), Is.EqualTo("first\r\nsecond\r\n"));
                }
            }
        }

        [Test]
        public void AppendLine_KeepsExistingText()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "events.log");
                File.WriteAllText(path, "earlier session\r\n");
                using (var log = new AppendOnlyFile(path)) log.AppendLine("this session");
                Assert.That(File.ReadAllText(path), Is.EqualTo("earlier session\r\nthis session\r\n"));
            }
        }

        [Test]
        public void AppendLine_TwoHandles_BothLand()
        {
            // The watchdog process appends to the same journal. Append-only rights put each write at
            // the current end, so neither handle overwrites the other's lines.
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "events.log");
                using (var a = new AppendOnlyFile(path))
                using (var b = new AppendOnlyFile(path))
                {
                    a.AppendLine("a1");
                    b.AppendLine("b1");
                    a.AppendLine("a2");
                }
                Assert.That(File.ReadAllText(path), Is.EqualTo("a1\r\nb1\r\na2\r\n"));
            }
        }

        [Test]
        public void AppendLine_FileDeletedWhileOpen_IsCreatedAgain()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "debug.log");
                using (var log = new AppendOnlyFile(path, recheckMs: 0))
                {
                    log.AppendLine("before");
                    File.Delete(path);
                    log.AppendLine("after");
                    Assert.That(ReadShared(path), Is.EqualTo("after\r\n"));
                }
            }
        }

        [Test]
        public void AppendLine_FileRolledWhileOpen_FollowsTheName()
        {
            // A second PowerPane process rolls the log at startup: debug.log becomes debug.log.1 and
            // that process starts a new debug.log. This handle must not keep writing into the archive.
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "debug.log");
                using (var log = new AppendOnlyFile(path, recheckMs: 0))
                {
                    log.AppendLine("before");
                    File.Move(path, path + ".1");
                    using (var other = new AppendOnlyFile(path)) other.AppendLine("second copy");
                    log.AppendLine("after");
                    Assert.That(ReadShared(path), Is.EqualTo("second copy\r\nafter\r\n"));
                    Assert.That(ReadShared(path + ".1"), Is.EqualTo("before\r\n"));
                }
            }
        }

        [Test]
        public void AppendLine_DoesNotWriteAByteOrderMark()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "debug.log");
                using (var log = new AppendOnlyFile(path)) log.AppendLine("x");
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { (byte)'x', 13, 10 }));
            }
        }
    }
}
