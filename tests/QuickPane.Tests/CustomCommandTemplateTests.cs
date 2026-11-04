using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using QuickPane.Services;

namespace QuickPane.Tests
{
    /// <summary>The safety of a custom command (#190) rests on values never being parsed as part of the
    /// command. These tests read each expanded command line back through CommandLineToArgvW, the parser
    /// a started program uses, so "stayed one argument" is checked rather than assumed.</summary>
    [TestFixture]
    public class CustomCommandTemplateTests
    {
        private const string Folder = @"C:\Demo\Projects\Apollo";

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmd, out int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr mem);

        /// <summary>Parses an argument string as a started program would, after its own name.</summary>
        private static List<string> Parse(string arguments)
        {
            int count;
            IntPtr argv = CommandLineToArgvW("prog.exe " + arguments, out count);
            Assert.That(argv, Is.Not.EqualTo(IntPtr.Zero));
            try
            {
                var result = new List<string>();
                for (int i = 1; i < count; i++)
                    result.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)));
                return result;
            }
            finally { LocalFree(argv); }
        }

        private static CustomCommandLine Build(string template, params string[] targets)
        {
            return CustomCommandTemplate.Build(template, targets.Length > 0 ? targets : new[] { Folder }, Folder, FoundOnPath);
        }

        private static string FoundOnPath(string name) { return @"C:\Tools\" + name; }

        // ---- every hostile name stays one argument ------------------------------------------

        [TestCase(@"C:\Demo\My Apollo")]
        [TestCase(@"C:\Demo\R&D notes")]
        [TestCase(@"C:\Demo\a&calc")]
        [TestCase(@"C:\Demo\100%PATH%")]
        [TestCase(@"C:\Demo\caret^here")]
        [TestCase(@"C:\Demo\x;calc")]
        [TestCase(@"C:\Demo\it's")]
        [TestCase(@"C:\Demo\$RECYCLE.BIN")]
        [TestCase(@"C:\Demo\Trailing Space\")]
        [TestCase(@"C:\")]
        [TestCase(@"\\server\share\My Apollo")]
        [TestCase("C:\\Demo\\quo\"te")]   // not a legal Windows name, and it must still not break out
        public void Path_StaysOneArgument(string path)
        {
            var line = Build("tool {path}", path);
            Assert.That(line.Error, Is.Null);
            Assert.That(Parse(line.Arguments), Is.EqualTo(new[] { path }));
        }

        [Test]
        public void QuotesAroundAPlaceholder_AreHarmless()
        {
            var line = Build("tool \"{path}\"", @"C:\Demo\My Apollo");
            Assert.That(Parse(line.Arguments), Is.EqualTo(new[] { @"C:\Demo\My Apollo" }));
        }

        [Test]
        public void PlaceholderInsideAnArgument_StaysInThatArgument()
        {
            var line = Build("tool --cd={dir} --title=\"Open {name}\"", @"C:\Demo\My Apollo");
            Assert.That(Parse(line.Arguments), Is.EqualTo(new[] { "--cd=" + Folder, "--title=Open My Apollo" }));
        }

        [Test]
        public void Paths_BecomesOneArgumentPerTarget()
        {
            var line = Build("tool --all {paths} --end", @"C:\Demo\a b.txt", @"C:\Demo\c&d.txt");
            Assert.That(Parse(line.Arguments), Is.EqualTo(new[] { "--all", @"C:\Demo\a b.txt", @"C:\Demo\c&d.txt", "--end" }));
        }

        [Test]
        public void PathAndName_UseTheFirstTarget()
        {
            var line = Build("tool {path} {name}", @"C:\Demo\one.txt", @"C:\Demo\two.txt");
            Assert.That(Parse(line.Arguments), Is.EqualTo(new[] { @"C:\Demo\one.txt", "one.txt" }));
        }

        [Test]
        public void Dir_IsTheFolderEvenWhenTargetsAreElsewhere()
        {
            var line = CustomCommandTemplate.Build("tool {dir}", new[] { @"D:\Other\file.txt" }, Folder, FoundOnPath);
            Assert.That(Parse(line.Arguments), Is.EqualTo(new[] { Folder }));
        }

        [Test]
        public void DoubledBraces_AreLiteral()
        {
            var line = Build("tool {{x}} {{{name}}}", @"C:\Demo\one");
            Assert.That(Parse(line.Arguments), Is.EqualTo(new[] { "{x}", "{one}" }));
        }

        [Test]
        public void AValueThatLooksLikeAPlaceholder_IsNotExpandedAgain()
        {
            var line = Build("tool {path} {dir}", @"C:\Demo\{dir}");
            Assert.That(Parse(line.Arguments), Is.EqualTo(new[] { @"C:\Demo\{dir}", Folder }));
        }

        [Test]
        public void NoPlaceholders_LeavesArgumentsAsTyped()
        {
            var line = Build("tool --flag \"two words\"");
            Assert.That(Parse(line.Arguments), Is.EqualTo(new[] { "--flag", "two words" }));
        }

        [Test]
        public void BareProgram_HasNoArguments()
        {
            Assert.That(Build("tool").Arguments, Is.EqualTo(string.Empty));
        }

        // ---- validation ---------------------------------------------------------------------

        [TestCase("code {path}")]
        [TestCase("\"C:\\Program Files\\Git\\git-bash.exe\" --cd={dir}")]
        [TestCase("tool {paths}")]
        [TestCase("pwsh -File C:\\Demo\\go.ps1 {path}")]
        [TestCase("powershell -f C:\\Demo\\go.ps1 {path}")]
        [TestCase("bash script.sh {path}")]
        [TestCase("wt -d .")]
        [TestCase("cmd /k echo hello")]
        [TestCase("tool.cmd --flag")]
        [TestCase("pwsh -NoExit")]
        public void Validate_Accepts(string template)
        {
            Assert.That(CustomCommandTemplate.Validate(template), Is.Null);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\"\" {path}")]
        [TestCase("tool \"unclosed")]
        [TestCase("tool {nope}")]
        [TestCase("tool {path")]
        [TestCase("{path}")]                 // a folder name must never be the program
        [TestCase("C:\\{name}\\tool.exe")]
        [TestCase("tool --x={paths}")]       // {paths} is several arguments, so it cannot sit inside one
        [TestCase("cmd /c echo {path}")]
        [TestCase("C:\\Windows\\System32\\cmd.exe /c {path}")]
        [TestCase("tool.bat {path}")]
        [TestCase("tool.cmd {name}")]
        [TestCase("powershell -Command {path}")]
        [TestCase("pwsh -c {path}")]
        [TestCase("pwsh {path}")]
        [TestCase("wsl ls {path}")]
        [TestCase("wt -d {dir}")]
        [TestCase("bash -c {path}")]
        [TestCase("bash -lc {path}")]
        public void Validate_Refuses(string template)
        {
            Assert.That(CustomCommandTemplate.Validate(template), Is.Not.Null);
        }

        [TestCase("code {path}", true)]
        [TestCase("code {paths}", true)]
        [TestCase("code {name}", true)]
        [TestCase("code {dir}", false)]
        [TestCase("code", false)]
        public void UsesTargets_ReportsWhetherTheSelectionMatters(string template, bool expected)
        {
            Assert.That(CustomCommandTemplate.UsesTargets(template), Is.EqualTo(expected));
        }

        // ---- finding the program ------------------------------------------------------------

        [Test]
        public void ResolveProgram_RelativePath_IsRefused()
        {
            Assert.That(CustomCommandTemplate.ResolveProgram(@".\tool.exe", FoundOnPath), Is.Null);
            Assert.That(CustomCommandTemplate.ResolveProgram(@"sub\tool.exe", FoundOnPath), Is.Null);
        }

        [Test]
        public void ResolveProgram_BareName_ComesFromPathOnly()
        {
            Assert.That(CustomCommandTemplate.ResolveProgram("tool", FoundOnPath), Is.EqualTo(@"C:\Tools\tool"));
            Assert.That(CustomCommandTemplate.ResolveProgram("tool", n => null), Is.Null);
        }

        [Test]
        public void ResolveProgram_MissingAbsolutePath_IsNull()
        {
            Assert.That(CustomCommandTemplate.ResolveProgram(@"C:\Demo\NoSuchProgram-190.exe", FoundOnPath), Is.Null);
        }

        [Test]
        public void Build_ProgramNotFound_SaysWhich()
        {
            var line = CustomCommandTemplate.Build("nosuchtool {path}", new[] { Folder }, Folder, n => null);
            Assert.That(line.Error, Does.Contain("nosuchtool"));
        }

        [Test]
        public void Build_BareNameThatIsABatchFile_IsRefusedWithAPlaceholder()
        {
            var line = CustomCommandTemplate.Build("mytool {path}", new[] { Folder }, Folder, n => @"C:\Tools\mytool.cmd");
            Assert.That(line.Error, Does.Contain("batch file"));
        }

        [Test]
        public void Build_BareNameThatIsABatchFile_RunsWithoutAPlaceholder()
        {
            var line = CustomCommandTemplate.Build("mytool --flag", new[] { Folder }, Folder, n => @"C:\Tools\mytool.cmd");
            Assert.That(line.Error, Is.Null);
            Assert.That(line.Program, Is.EqualTo(@"C:\Tools\mytool.cmd"));
        }

        [Test]
        public void Build_ACommandLineTooLongToStart_IsRefused()
        {
            var targets = new List<string>();
            for (int i = 0; i < 2000; i++) targets.Add(@"C:\Demo\Projects\Apollo\some-long-file-name-" + i + ".txt");
            var line = CustomCommandTemplate.Build("tool {paths}", targets, Folder, FoundOnPath);
            Assert.That(line.Error, Does.Contain("Too many"));
        }

        [Test]
        public void Preview_ShowsTheQuotedCommand_AndIsNullWhenInvalid()
        {
            Assert.That(CustomCommandTemplate.Preview("code {path}", new[] { @"C:\Demo\My Apollo" }, Folder),
                Is.EqualTo("code \"C:\\Demo\\My Apollo\""));
            Assert.That(CustomCommandTemplate.Preview("cmd /c {path}", new[] { Folder }, Folder), Is.Null);
        }

        [TestCase("custom:abc", true)]
        [TestCase("copy-path", false)]
        [TestCase(null, false)]
        public void IsCustomId(string id, bool expected)
        {
            Assert.That(CustomCommandTemplate.IsCustomId(id), Is.EqualTo(expected));
        }
    }
}
