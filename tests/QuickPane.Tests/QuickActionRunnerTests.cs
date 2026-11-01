using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using QuickPane.Services;

namespace QuickPane.Tests
{
    /// <summary>Covers QuickActionRunner's pure clipboard-text computation and its new-item side
    /// effects (quick actions, first version), all against a temp directory. The open-tool actions
    /// (Open in Terminal, Open in Visual Studio Code) start a real process and are deliberately left
    /// untested here: the spec's testable core is path formatting, quoting, and unique naming, all
    /// already covered in QuickActionLibraryTests, and starting a process from a test would be
    /// flaky and would not stay inside this test's own temp directory.</summary>
    [TestFixture]
    public class QuickActionRunnerTests
    {
        // ---- ClipboardTextFor (pure) ----------------------------------------------------------

        [Test]
        public void ClipboardTextFor_CopyPath_ReturnsThePlainFolder()
        {
            Assert.That(QuickActionRunner.ClipboardTextFor("copy-path", @"C:\Demo\Apollo"),
                Is.EqualTo(@"C:\Demo\Apollo"));
        }

        [Test]
        public void ClipboardTextFor_CopyPathQuoted_WrapsInQuotes()
        {
            Assert.That(QuickActionRunner.ClipboardTextFor("copy-path-quoted", @"C:\Demo\Apollo"),
                Is.EqualTo("\"C:\\Demo\\Apollo\""));
        }

        [Test]
        public void ClipboardTextFor_CopyPathForward_UsesForwardSlashes()
        {
            Assert.That(QuickActionRunner.ClipboardTextFor("copy-path-forward", @"C:\Demo\Apollo"),
                Is.EqualTo("C:/Demo/Apollo"));
        }

        [Test]
        public void ClipboardTextFor_CopyPathWsl_MapsUnderMntDrive()
        {
            Assert.That(QuickActionRunner.ClipboardTextFor("copy-path-wsl", @"C:\Demo\Apollo"),
                Is.EqualTo("/mnt/c/Demo/Apollo"));
        }

        [Test]
        public void ClipboardTextFor_CopyPathWsl_UncFolder_ReturnsNull()
        {
            Assert.That(QuickActionRunner.ClipboardTextFor("copy-path-wsl", @"\\server\share\Apollo"), Is.Null);
        }

        [Test]
        public void ClipboardTextFor_CopyMarkdownLink_ReturnsTheLinkForm()
        {
            Assert.That(QuickActionRunner.ClipboardTextFor("copy-markdown-link", @"C:\Demo\Apollo"),
                Is.EqualTo("[Apollo](<C:/Demo/Apollo>)"));
        }

        [Test]
        public void ClipboardTextFor_NotACopyAction_ReturnsNull()
        {
            Assert.That(QuickActionRunner.ClipboardTextFor("new-text-file", @"C:\Demo\Apollo"), Is.Null);
            Assert.That(QuickActionRunner.ClipboardTextFor("unknown-id", @"C:\Demo\Apollo"), Is.Null);
        }

        // ---- new-item side effects, in a temp directory ----------------------------------------

        [Test]
        public void Run_NewTextFile_CreatesNewTextDocument()
        {
            using (var temp = new TempDir())
            {
                QuickActionRunner.Run("new-text-file", temp.Path);
                Assert.That(File.Exists(Path.Combine(temp.Path, "New Text Document.txt")), Is.True);
            }
        }

        [Test]
        public void Run_NewTextFile_TwiceNeverOverwrites()
        {
            using (var temp = new TempDir())
            {
                File.WriteAllText(Path.Combine(temp.Path, "New Text Document.txt"), "do not touch");
                QuickActionRunner.Run("new-text-file", temp.Path);

                Assert.That(File.ReadAllText(Path.Combine(temp.Path, "New Text Document.txt")), Is.EqualTo("do not touch"));
                Assert.That(File.Exists(Path.Combine(temp.Path, "New Text Document (2).txt")), Is.True);
            }
        }

        [Test]
        public void Run_NewMarkdownFile_CreatesADatedFileWithAnUntitledHeading()
        {
            using (var temp = new TempDir())
            {
                QuickActionRunner.Run("new-markdown-file", temp.Path);

                // InvariantCulture to match production: the current thread's culture (e.g. Thai
                // Buddhist, th-TH) must not be able to put a different year in either side.
                string expectedName = System.DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-untitled.md";
                string path = Path.Combine(temp.Path, expectedName);
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.ReadAllText(path), Does.Contain("# Untitled"));
            }
        }

        [Test]
        public void Run_NewDatedFolder_CreatesATodayNamedFolder()
        {
            using (var temp = new TempDir())
            {
                QuickActionRunner.Run("new-dated-folder", temp.Path);

                string expectedName = System.DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                Assert.That(Directory.Exists(Path.Combine(temp.Path, expectedName)), Is.True);
            }
        }

        [Test]
        public void Run_NewDatedFolder_TwiceAddsASuffixRatherThanReusingIt()
        {
            using (var temp = new TempDir())
            {
                QuickActionRunner.Run("new-dated-folder", temp.Path);
                QuickActionRunner.Run("new-dated-folder", temp.Path);

                string baseName = System.DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                Assert.That(Directory.Exists(Path.Combine(temp.Path, baseName + " (2)")), Is.True);
            }
        }

        // ---- race safety: UniqueFileName/UniqueFolderName only check a name is free; nothing holds
        // it reserved, so a single existence check followed by an unconditional write could overwrite
        // whatever a second, near-simultaneous call had just created there. These race multiple real
        // calls against the very same folder, on real OS threads, rather than trying to simulate the
        // race deterministically -- the only way to actually exercise the FileMode.CreateNew retry.

        [Test]
        public void Run_NewTextFile_ConcurrentCallsNeverOverwriteEachOther()
        {
            using (var temp = new TempDir())
            {
                const int n = 8;
                var tasks = new Task[n];
                for (int i = 0; i < n; i++) tasks[i] = Task.Run(() => QuickActionRunner.Run("new-text-file", temp.Path));
                Task.WaitAll(tasks);

                var created = Directory.GetFiles(temp.Path, "New Text Document*.txt");
                Assert.That(created, Has.Length.EqualTo(n)); // one file per call; none silently lost to another
            }
        }

        [Test]
        public void Run_NewDatedFolder_ConcurrentCallsNeverCollapseIntoFewerFolders()
        {
            using (var temp = new TempDir())
            {
                const int n = 8;
                var tasks = new Task[n];
                for (int i = 0; i < n; i++) tasks[i] = Task.Run(() => QuickActionRunner.Run("new-dated-folder", temp.Path));
                Task.WaitAll(tasks);

                Assert.That(Directory.GetDirectories(temp.Path), Has.Length.EqualTo(n));
            }
        }

        // ---- safety: a missing or unknown target never throws and never creates anything --------

        [Test]
        public void Run_NullFolder_DoesNothing()
        {
            Assert.DoesNotThrow(() => QuickActionRunner.Run("new-text-file", null));
        }

        [Test]
        public void Run_FolderDoesNotExist_DoesNothing()
        {
            using (var temp = new TempDir())
            {
                string gone = Path.Combine(temp.Path, "does-not-exist");
                Assert.DoesNotThrow(() => QuickActionRunner.Run("new-text-file", gone));
                Assert.That(Directory.Exists(gone), Is.False);
            }
        }

        [Test]
        public void Run_UnknownActionId_DoesNotThrow()
        {
            using (var temp = new TempDir())
                Assert.DoesNotThrow(() => QuickActionRunner.Run("not-a-real-action", temp.Path));
        }
    }
}
