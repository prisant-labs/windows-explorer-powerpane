using System.IO;
using System.Linq;
using NUnit.Framework;
using QuickPane.Services;

namespace QuickPane.Tests
{
    /// <summary>Covers QuickActionLibrary's pure path-format, quoting, and unique-naming helpers
    /// (quick actions, first version). None of these touch the clipboard, start a process, or need a
    /// window; UniqueFileName/UniqueFolderName are the only two that read a temp directory.</summary>
    [TestFixture]
    public class QuickActionLibraryTests
    {
        // ---- path formats, including a drive root, a UNC path, and a trailing backslash ---------

        [TestCase(@"C:\Demo\Projects\Apollo")]
        [TestCase(@"C:\")]
        [TestCase(@"\\server\share\Apollo")]
        [TestCase(@"C:\Demo\Projects\Apollo\")]
        public void ToQuoted_WrapsInDoubleQuotes(string path)
        {
            Assert.That(QuickActionLibrary.ToQuoted(path), Is.EqualTo("\"" + path + "\""));
        }

        [TestCase(@"C:\Demo\Projects\Apollo", "C:/Demo/Projects/Apollo")]
        [TestCase(@"C:\", "C:/")]
        [TestCase(@"\\server\share\Apollo", "//server/share/Apollo")]
        [TestCase(@"C:\Demo\Projects\Apollo\", "C:/Demo/Projects/Apollo/")]
        public void ToForwardSlashes_ReplacesEveryBackslash(string path, string expected)
        {
            Assert.That(QuickActionLibrary.ToForwardSlashes(path), Is.EqualTo(expected));
        }

        [TestCase(@"C:\Demo\Projects\Apollo", "/mnt/c/Demo/Projects/Apollo")]
        [TestCase(@"c:\Demo", "/mnt/c/Demo")]
        [TestCase(@"C:\", "/mnt/c/")]
        public void ToWslPath_DriveLetterPaths_MapUnderMntDrive(string path, string expected)
        {
            Assert.That(QuickActionLibrary.ToWslPath(path), Is.EqualTo(expected));
        }

        [Test]
        public void ToWslPath_UncPath_ReturnsNull()
        {
            Assert.That(QuickActionLibrary.ToWslPath(@"\\server\share\Apollo"), Is.Null);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("relative\\path")]
        public void ToWslPath_NoDriveLetter_ReturnsNull(string path)
        {
            Assert.That(QuickActionLibrary.ToWslPath(path), Is.Null);
        }

        [Test]
        public void IsUncPath_RecognizesLeadingDoubleBackslash()
        {
            Assert.That(QuickActionLibrary.IsUncPath(@"\\server\share"), Is.True);
            Assert.That(QuickActionLibrary.IsUncPath(@"C:\Demo"), Is.False);
            Assert.That(QuickActionLibrary.IsUncPath(null), Is.False);
        }

        [Test]
        public void ToMarkdownLink_UsesLeafNameAndForwardSlashesAndAngleBrackets()
        {
            Assert.That(QuickActionLibrary.ToMarkdownLink(@"C:\Demo\Projects\Apollo"),
                Is.EqualTo("[Apollo](<C:/Demo/Projects/Apollo>)"));
        }

        [Test]
        public void ToMarkdownLink_TrailingBackslash_StillFindsTheLeaf()
        {
            Assert.That(QuickActionLibrary.ToMarkdownLink(@"C:\Demo\Projects\Apollo\"),
                Is.EqualTo("[Apollo](<C:/Demo/Projects/Apollo/>)"));
        }

        [TestCase(@"C:\Demo\Projects\Apollo", "Apollo")]
        [TestCase(@"C:\Demo\Projects\Apollo\", "Apollo")]
        [TestCase(@"C:\", @"C:\")]
        public void Leaf_ReturnsTheLastSegment(string path, string expected)
        {
            Assert.That(QuickActionLibrary.Leaf(path), Is.EqualTo(expected));
        }

        // ---- the quoting helper, against the tricky characters the spec calls out ---------------

        [Test]
        public void QuoteArgument_PlainValue_IsReturnedUnquoted()
        {
            Assert.That(QuickActionLibrary.QuoteArgument(@"C:\Demo\Apollo"), Is.EqualTo(@"C:\Demo\Apollo"));
        }

        [Test]
        public void QuoteArgument_Space_IsQuoted()
        {
            Assert.That(QuickActionLibrary.QuoteArgument(@"C:\Demo Projects\Apollo"),
                Is.EqualTo("\"" + @"C:\Demo Projects\Apollo" + "\""));
        }

        [Test]
        public void QuoteArgument_Ampersand_StaysInsideOneArgument()
        {
            var quoted = QuickActionLibrary.QuoteArgument(@"C:\Demo\R&D notes");
            Assert.That(quoted, Is.EqualTo("\"" + @"C:\Demo\R&D notes" + "\""));
            Assert.That(RoundTripsAsOneArgument(quoted, @"C:\Demo\R&D notes"), Is.True);
        }

        [Test]
        public void QuoteArgument_Caret_StaysInsideOneArgument()
        {
            var quoted = QuickActionLibrary.QuoteArgument(@"C:\Demo\Weird^Name");
            Assert.That(RoundTripsAsOneArgument(quoted, @"C:\Demo\Weird^Name"), Is.True);
        }

        [Test]
        public void QuoteArgument_PercentSign_StaysInsideOneArgument()
        {
            var quoted = QuickActionLibrary.QuoteArgument(@"C:\Demo\100%Done");
            Assert.That(RoundTripsAsOneArgument(quoted, @"C:\Demo\100%Done"), Is.True);
        }

        [Test]
        public void QuoteArgument_EmbeddedQuote_IsEscapedAndRoundTrips()
        {
            var value = "C:\\Demo\\\"Quoted\" Folder";
            var quoted = QuickActionLibrary.QuoteArgument(value);
            Assert.That(RoundTripsAsOneArgument(quoted, value), Is.True);
        }

        [Test]
        public void QuoteArgument_TrailingBackslash_DoesNotEscapeTheClosingQuote()
        {
            // The space forces quoting; without it this value round-trips unquoted and the trailing
            // backslash never meets a closing quote to escape in the first place.
            var value = @"C:\Demo Projects\Apollo\";
            var quoted = QuickActionLibrary.QuoteArgument(value);
            // A naive '"' + value + '"' would let the trailing backslash escape the closing quote and
            // run the argument into whatever followed it on the command line.
            Assert.That(quoted, Is.EqualTo("\"C:\\Demo Projects\\Apollo\\\\\""));
            Assert.That(RoundTripsAsOneArgument(quoted, value), Is.True);
        }

        [Test]
        public void QuoteArgument_InteriorBackslashesBeforeAQuote_AreDoubled()
        {
            // Two backslashes then a literal quote: CommandLineToArgvW needs 2*2+1 backslashes ahead
            // of the escaped quote to read back "\\\"" unchanged.
            var value = "a\\\\\"b";
            var quoted = QuickActionLibrary.QuoteArgument(value);
            Assert.That(RoundTripsAsOneArgument(quoted, value), Is.True);
        }

        [Test]
        public void QuoteArgument_NullValue_QuotesAsEmpty()
        {
            Assert.That(QuickActionLibrary.QuoteArgument(null), Is.EqualTo("\"\""));
        }

        /// <summary>True when parsing "cmd.exe <quoted> after" with Windows' CommandLineToArgvW-style
        /// rules gives back exactly <paramref name="expected"/> as the single middle token -- the
        /// same guarantee QuoteArgument exists to make for a process's real argv, reimplemented here
        /// only for the test (not shared production code), so the test cannot pass merely because it
        /// shares a bug with the method it is checking.</summary>
        private static bool RoundTripsAsOneArgument(string quotedArg, string expected)
        {
            var tokens = SplitCommandLineLikeWindows("ignored.exe " + quotedArg + " after");
            return tokens.Count == 3 && tokens[1] == expected;
        }

        private static System.Collections.Generic.List<string> SplitCommandLineLikeWindows(string commandLine)
        {
            var args = new System.Collections.Generic.List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            int i = 0;
            while (i < commandLine.Length)
            {
                char c = commandLine[i];
                if (c == '\\')
                {
                    int backslashes = 0;
                    while (i < commandLine.Length && commandLine[i] == '\\') { backslashes++; i++; }
                    if (i < commandLine.Length && commandLine[i] == '"')
                    {
                        current.Append('\\', backslashes / 2);
                        if (backslashes % 2 == 1) { current.Append('"'); i++; }
                        else { inQuotes = !inQuotes; i++; }
                    }
                    else
                    {
                        current.Append('\\', backslashes);
                    }
                    continue;
                }
                if (c == '"') { inQuotes = !inQuotes; i++; continue; }
                if (c == ' ' && !inQuotes)
                {
                    if (current.Length > 0) { args.Add(current.ToString()); current.Clear(); }
                    i++;
                    continue;
                }
                current.Append(c);
                i++;
            }
            if (current.Length > 0) args.Add(current.ToString());
            return args;
        }

        // ---- unique naming, in a temp directory, never overwriting ------------------------------

        [Test]
        public void UniqueFileName_NameNotTaken_ReturnsThePlainName()
        {
            using (var temp = new TempDir())
            {
                var path = QuickActionLibrary.UniqueFileName(temp.Path, "New Text Document", ".txt");
                Assert.That(path, Is.EqualTo(Path.Combine(temp.Path, "New Text Document.txt")));
            }
        }

        [Test]
        public void UniqueFileName_NameTaken_AddsNumericSuffix()
        {
            using (var temp = new TempDir())
            {
                File.WriteAllText(Path.Combine(temp.Path, "New Text Document.txt"), "existing");
                var path = QuickActionLibrary.UniqueFileName(temp.Path, "New Text Document", ".txt");
                Assert.That(path, Is.EqualTo(Path.Combine(temp.Path, "New Text Document (2).txt")));
            }
        }

        [Test]
        public void UniqueFileName_SeveralTaken_KeepsIncrementing()
        {
            using (var temp = new TempDir())
            {
                File.WriteAllText(Path.Combine(temp.Path, "New Text Document.txt"), "a");
                File.WriteAllText(Path.Combine(temp.Path, "New Text Document (2).txt"), "b");
                var path = QuickActionLibrary.UniqueFileName(temp.Path, "New Text Document", ".txt");
                Assert.That(path, Is.EqualTo(Path.Combine(temp.Path, "New Text Document (3).txt")));
            }
        }

        [Test]
        public void UniqueFileName_NeverReturnsAnExistingPath()
        {
            using (var temp = new TempDir())
            {
                File.WriteAllText(Path.Combine(temp.Path, "notes.md"), "a");
                var path = QuickActionLibrary.UniqueFileName(temp.Path, "notes", ".md");
                Assert.That(File.Exists(path), Is.False);
            }
        }

        [Test]
        public void UniqueFileName_FolderWithTheCandidateNameAlsoCountsAsTaken()
        {
            using (var temp = new TempDir())
            {
                Directory.CreateDirectory(Path.Combine(temp.Path, "2026-11-01"));
                var path = QuickActionLibrary.UniqueFolderName(temp.Path, "2026-11-01");
                Assert.That(path, Is.EqualTo(Path.Combine(temp.Path, "2026-11-01 (2)")));
            }
        }

        [Test]
        public void UniqueFolderName_NameNotTaken_ReturnsThePlainName()
        {
            using (var temp = new TempDir())
            {
                var path = QuickActionLibrary.UniqueFolderName(temp.Path, "2026-11-01");
                Assert.That(path, Is.EqualTo(Path.Combine(temp.Path, "2026-11-01")));
            }
        }

        // ---- the library itself -------------------------------------------------------------

        [Test]
        public void All_IdsAreUniqueAndStable()
        {
            var ids = QuickActionLibrary.All.Select(d => d.Id).ToList();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
        }

        [Test]
        public void Find_KnownId_ReturnsItsDefinition()
        {
            Assert.That(QuickActionLibrary.Find("copy-path").Name, Is.EqualTo("Copy path"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not-a-real-action")]
        public void Find_UnknownOrBlankId_ReturnsNull(string id)
        {
            Assert.That(QuickActionLibrary.Find(id), Is.Null);
        }

        // ---- TerminalArguments: wt.exe vs. the PowerShell fallback's own quoting rules ----------

        [Test]
        public void TerminalArguments_WithWt_UsesDashDAndQuoteArgument()
        {
            Assert.That(QuickActionLibrary.TerminalArguments(@"C:\Demo\R&D notes", haveWt: true),
                Is.EqualTo("-d " + QuickActionLibrary.QuoteArgument(@"C:\Demo\R&D notes")));
        }

        [Test]
        public void TerminalArguments_WithoutWt_WrapsThePathInPowerShellSingleQuotes()
        {
            Assert.That(QuickActionLibrary.TerminalArguments(@"C:\Demo\Apollo", haveWt: false),
                Is.EqualTo("-NoExit -Command Set-Location -LiteralPath 'C:\\Demo\\Apollo'"));
        }

        [Test]
        public void TerminalArguments_WithoutWt_DollarSignIsNotExpandable()
        {
            // CommandLineToArgvW-style quoting (QuoteArgument) protects argv parsing, not
            // PowerShell's -Command script parsing: a double-quoted "$RECYCLE.BIN" would still have
            // $RECYCLE read as a variable. Single quotes are PowerShell's own literal string, so $
            // inside one is just a character.
            var args = QuickActionLibrary.TerminalArguments(@"C:\$RECYCLE.BIN", haveWt: false);
            Assert.That(args, Is.EqualTo("-NoExit -Command Set-Location -LiteralPath 'C:\\$RECYCLE.BIN'"));
            Assert.That(args, Does.Not.Contain("\""));
        }

        [Test]
        public void TerminalArguments_WithoutWt_EmbeddedSingleQuoteIsDoubled()
        {
            var args = QuickActionLibrary.TerminalArguments(@"C:\Demo\O'Brien", haveWt: false);
            Assert.That(args, Is.EqualTo("-NoExit -Command Set-Location -LiteralPath 'C:\\Demo\\O''Brien'"));
        }

        // ---- ResolveCodeExe: the real Code.exe beside code.cmd's install root ------------------

        [Test]
        public void ResolveCodeExe_CodeExeBesideInstallRoot_ReturnsItsPath()
        {
            using (var temp = new TempDir())
            {
                string bin = Path.Combine(temp.Path, "Microsoft VS Code", "bin");
                Directory.CreateDirectory(bin);
                string codeCmd = Path.Combine(bin, "code.cmd");
                File.WriteAllText(codeCmd, "@echo off");
                string codeExe = Path.Combine(temp.Path, "Microsoft VS Code", "Code.exe");
                File.WriteAllText(codeExe, "not a real exe, just needs to exist");

                Assert.That(QuickActionLibrary.ResolveCodeExe(codeCmd), Is.EqualTo(codeExe));
            }
        }

        [Test]
        public void ResolveCodeExe_CodeExeMissing_ReturnsNull()
        {
            using (var temp = new TempDir())
            {
                string bin = Path.Combine(temp.Path, "Microsoft VS Code", "bin");
                Directory.CreateDirectory(bin);
                string codeCmd = Path.Combine(bin, "code.cmd");
                File.WriteAllText(codeCmd, "@echo off");

                Assert.That(QuickActionLibrary.ResolveCodeExe(codeCmd), Is.Null);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        public void ResolveCodeExe_NullOrEmptyPath_ReturnsNull(string path)
        {
            Assert.That(QuickActionLibrary.ResolveCodeExe(path), Is.Null);
        }

        [Test]
        public void ResolveCodeExe_NotNamedCodeCmd_ReturnsNull()
        {
            using (var temp = new TempDir())
            {
                string notCode = Path.Combine(temp.Path, "notepad.exe");
                File.WriteAllText(notCode, "x");
                Assert.That(QuickActionLibrary.ResolveCodeExe(notCode), Is.Null);
            }
        }
    }
}
