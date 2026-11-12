using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
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
        // ---- selection actions (#191) ----------------------------------------------------------

        private static readonly string[] Selected = { @"C:\Demo\My File.txt", @"C:\Demo\Sub" };

        [TestCase("copy-selected-names", "My File.txt|Sub")]
        [TestCase("copy-selected-paths", @"C:\Demo\My File.txt|C:\Demo\Sub")]
        [TestCase("copy-selected-quoted", "\"C:\\Demo\\My File.txt\"|\"C:\\Demo\\Sub\"")]
        [TestCase("copy-selected-forward", "C:/Demo/My File.txt|C:/Demo/Sub")]
        [TestCase("copy-selected-wsl", "/mnt/c/Demo/My File.txt|/mnt/c/Demo/Sub")]
        [TestCase("copy-selected-urls", "file:///C:/Demo/My%20File.txt|file:///C:/Demo/Sub")]
        [TestCase("copy-selected-markdown", "[My File.txt](<C:/Demo/My File.txt>)|[Sub](<C:/Demo/Sub>)")]
        public void SelectionTextFor_FormatsEachItemOnItsOwnLine(string id, string expected)
        {
            Assert.That(QuickActionRunner.SelectionTextFor(id, Selected),
                Is.EqualTo(expected.Replace("|", System.Environment.NewLine)));
        }

        [Test]
        public void SelectionTextFor_SkipsAnItemWithNoSuchForm()
        {
            Assert.That(QuickActionRunner.SelectionTextFor("copy-selected-wsl", new[] { @"\\server\share\a", @"C:\Demo\b" }),
                Is.EqualTo("/mnt/c/Demo/b"));
            Assert.That(QuickActionRunner.SelectionTextFor("copy-selected-wsl", new[] { @"\\server\share\a" }), Is.Null);
        }

        [Test]
        public void SelectionProblem_OnlyAReadThatWorkedLetsAnActionRun()
        {
            Assert.That(QuickActionRunner.SelectionProblem(PowerPane.Interop.SelectionReadStatus.Ok), Is.Null);
            Assert.That(QuickActionRunner.SelectionProblem(PowerPane.Interop.SelectionReadStatus.TooMany), Does.Contain("Too many"));
            Assert.That(QuickActionRunner.SelectionProblem(PowerPane.Interop.SelectionReadStatus.Failed), Does.Contain("Could not read"));
        }

        [TestCase(null)]
        [TestCase("empty")]
        public void Run_SelectionActionWithNothingSelected_SaysSo(string selection)
        {
            var def = QuickActionLibrary.Find("copy-selected-paths");
            Assert.That(QuickActionRunner.Run(def, @"C:\Demo", selection == null ? null : new string[0]),
                Is.EqualTo(QuickActionRunner.NothingSelected));
        }

        [TestCase("copy-selected-paths", false)]
        [TestCase("open-selected-vscode", false)]
        [TestCase("copy-path", true)]
        public void AppliesToPinnedRow_ExcludesSelectionActions(string id, bool expected)
        {
            Assert.That(QuickActionLibrary.AppliesToPinnedRow(QuickActionLibrary.Find(id)), Is.EqualTo(expected));
        }

        [Test]
        public void UsesSelection_OnlyForActionsThatReadIt()
        {
            Assert.That(QuickActionLibrary.UsesSelection(QuickActionLibrary.Find("copy-selected-paths")), Is.True);
            Assert.That(QuickActionLibrary.UsesSelection(QuickActionLibrary.Find("copy-path")), Is.False);

            var usesPath = new QuickActionDefinition("custom:a", "A", "tool {path}", QuickActionKind.Custom, false);
            var usesDir = new QuickActionDefinition("custom:b", "B", "tool {dir}", QuickActionKind.Custom, false);
            Assert.That(QuickActionLibrary.UsesSelection(usesPath), Is.True);
            Assert.That(QuickActionLibrary.UsesSelection(usesDir), Is.False);
        }

        [TestCase(@"C:\Demo\a.txt", true)]
        [TestCase(@"\\server\share\a.txt", true)]
        [TestCase("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsFileSystemPath_RejectsShellOnlyItems(string path, bool expected)
        {
            Assert.That(PowerPane.Interop.ExplorerNavigator.IsFileSystemPath(path), Is.EqualTo(expected));
        }

        // ---- ClipboardTextFor (pure) ----------------------------------------------------------

        [TestCase("copy-folder-name", @"C:\Demo\Apollo", "Apollo")]
        [TestCase("copy-parent-path", @"C:\Demo\Apollo", @"C:\Demo")]
        [TestCase("copy-file-url", @"C:\Demo\Apollo", "file:///C:/Demo/Apollo")]
        [TestCase("copy-cd-powershell", @"C:\Demo\Apollo", @"Set-Location -LiteralPath 'C:\Demo\Apollo'")]
        public void ClipboardTextFor_NewFormats(string id, string folder, string expected)
        {
            Assert.That(QuickActionRunner.ClipboardTextFor(id, folder), Is.EqualTo(expected));
        }

        [Test]
        public void ClipboardTextFor_ParentOfADriveRoot_IsNothingToCopy()
        {
            Assert.That(QuickActionRunner.ClipboardTextFor("copy-parent-path", @"C:\"), Is.Null);
        }

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
