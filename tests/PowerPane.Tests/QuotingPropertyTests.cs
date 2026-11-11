using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>Property tests for the custom-command injection guard (#190). The hand-picked cases in
    /// <see cref="CustomCommandTemplateTests"/> and <see cref="QuickActionLibraryTests"/> name the
    /// attacks someone thought of. These generate thousands of awkward names and check the three rules
    /// that make a name harmless: it round-trips through CommandLineToArgvW as one argument, a
    /// substituted template never grows extra arguments, and a shell is refused however it is spelled.</summary>
    [TestFixture]
    public class QuotingPropertyTests
    {
        private const int Cases = 10000;

        private static Config Cfg { get { return Config.QuickThrowOnFailure.WithMaxTest(Cases); } }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmd, out int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr mem);

        /// <summary>Parses a command line the way a started program does. Returns every argument after
        /// the program name, so the program token's different parsing rules are never under test.</summary>
        private static List<string> ParseAfterProgram(string arguments)
        {
            int count;
            IntPtr argv = CommandLineToArgvW("x.exe " + arguments, out count);
            if (argv == IntPtr.Zero) throw new InvalidOperationException("CommandLineToArgvW failed for: " + arguments);
            try
            {
                var result = new List<string>();
                for (int i = 1; i < count; i++)
                    result.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)));
                return result;
            }
            finally { LocalFree(argv); }
        }

        // ---- generators ---------------------------------------------------------------------

        // Awkward fragments, picked often so quotes and backslashes land next to each other.
        private static readonly string[] Pieces =
        {
            "\"", "\\", "\\\\", "\\\\\\", "\\\"", "\\\\\"", " ", "\t", "&", "^", "%", "!", "|", "<", ">",
            ";", ",", "(", ")", "'", "{", "}", "%PATH%", "a", "Z", "0", ".", "-", "_", ":", "/",
            "\u00e9", "\u4e2d\u6587", "\u0416", "\u200d", "\ud83d\ude00", "\u00a0", "\r\n",
        };

        private static string Join(IEnumerable<string> parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts) sb.Append(p);
            return sb.ToString();
        }

        /// <summary>Removes one piece at a time, so a failure shrinks to a short value. Works on UTF-16
        /// chars, but never splits a surrogate pair.</summary>
        private static IEnumerable<string> ShrinkString(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                int len = char.IsHighSurrogate(s[i]) && i + 1 < s.Length ? 2 : 1;
                yield return s.Remove(i, len);
                if (len == 2) i++;
            }
        }

        private static Arbitrary<string> AwkwardStrings()
        {
            var fromPieces = Gen.ListOf(Gen.Elements(Pieces)).Select(l => Join(l));
            var fromBackslashRuns = Gen.Choose(0, 6).SelectMany(n =>
                Gen.Elements("\"", "a", " ", "").Select(tail => new string('\\', n) + tail));
            var fromAnyChars = Gen.ListOf(Gen.Choose(1, 0x2fff)
                    .Where(c => c < 0xd800 || c > 0xdfff)
                    .Select(c => ((char)c).ToString()))
                .Select(l => Join(l));
            var empty = Gen.Constant(string.Empty);
            // The value, never a NUL: a string ends at a NUL in every Windows command line, so a file
            // name cannot hold one either (NTFS refuses it). Nothing here generates char 0.
            var gen = Gen.OneOf(fromPieces, fromPieces, fromPieces, fromBackslashRuns, fromAnyChars, empty);
            return Arb.From(gen, ShrinkString);
        }

        // ---- 1. round trip ------------------------------------------------------------------

        [Test]
        public void QuoteArgument_RoundTripsThroughCommandLineToArgvW_AsOneArgument()
        {
            var prop = Prop.ForAll(AwkwardStrings(), value =>
            {
                var argv = ParseAfterProgram(QuickActionLibrary.QuoteArgument(value));
                return argv.Count == 1 && argv[0] == value;
            });
            Check.One(Cfg, prop);
        }

        // ---- 2. a template never gains arguments --------------------------------------------

        private static readonly string[] TokenPool =
        {
            "--flag", "-x", "{path}", "{dir}", "{name}", "{paths}", "pre{path}post", "--file={path}",
            "{dir}\\{name}", "{{literal}}", "\"two words\"", "\"a {path} b\"", "plain", "\"{name} {dir}\"",
            "\"with\\\"quote\"", "{path}{path}", "\"\"",
        };

        [Test]
        public void BuildArguments_NeverYieldsMoreArgumentsThanTheTemplateHasTokens()
        {
            var templates = Gen.ListOf(Gen.Elements(TokenPool))
                .Select(l => "tool.exe " + string.Join(" ", l));
            // At least one target, as BuildArguments requires ("never empty").
            var targets = Gen.NonEmptyListOf(AwkwardStrings().Generator).Select(l => l.ToList());
            var arb = Arb.From(templates.SelectMany(a => targets.SelectMany(b => AwkwardStrings().Generator.Select(c => Tuple.Create(a, b, c)))), t => Enumerable.Empty<Tuple<string, List<string>, string>>());

            var prop = Prop.ForAll(arb, t =>
            {
                string template = t.Item1;
                List<string> targetList = t.Item2;
                string dir = t.Item3;

                List<string> tokens;
                string error;
                if (!CustomCommandTemplate.TrySplit(template, out tokens, out error)) return true;
                // Only templates the Settings page would accept. The generator can build {paths} inside
                // a longer token only if it were in the pool, and it is not, so this is just a guard.
                if (CustomCommandTemplate.Validate(template) != null) return true;

                string built = CustomCommandTemplate.BuildArguments(tokens, targetList, dir);
                var argv = ParseAfterProgram(built);

                // {paths} is the one placeholder that expands on purpose, to one argument per target.
                int expected = tokens.Skip(1).Sum(tok => tok == "{paths}" ? targetList.Count : 1);
                if (argv.Count != expected) return false;

                // Where the expected value is exact, check it too, not only the count.
                int at = 0;
                foreach (var tok in tokens.Skip(1))
                {
                    if (tok == "{paths}")
                    {
                        foreach (var target in targetList)
                            if (argv[at++] != target) return false;
                        continue;
                    }
                    string want = tok == "{path}" ? targetList[0]
                                : tok == "{dir}" ? dir
                                : tok == "{name}" ? QuickActionLibrary.Leaf(targetList[0])
                                : null;
                    if (want != null && argv[at] != want) return false;
                    at++;
                }
                return true;
            });
            Check.One(Cfg, prop);
        }

        // ---- 3. shells are refused in every spelling ----------------------------------------

        // Programs that ShellRefusal refuses with no other argument. The stem is matched
        // case-insensitively after the directory and extension are removed.
        private static readonly string[] RefusedBare = { "cmd", "command", "wsl", "wt", "powershell", "pwsh" };
        private static readonly string[] RefusedBatch = { "build.bat", "run.cmd", "x.BAT", "setup.Cmd" };
        private static readonly string[] TrailingNoise = { string.Empty, string.Empty, ".", "..", " ", ". .", " . " };
        private static readonly string[] Directories =
        {
            string.Empty, @"C:\Demo\Tools\", @"C:\Demo\Tools With Space\", "C:/Demo/Tools/", @"D:\a\b\c\d\", @"\\server\share\",
        };

        private static string RandomCase(string s, Random rng)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(rng.Next(2) == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            return sb.ToString();
        }

        [Test]
        public void ShellRefusal_RefusesEveryListedProgramInAnyCaseAndPathForm()
        {
            var gen = Gen.Choose(0, int.MaxValue).Select(seed =>
            {
                var rng = new Random(seed);
                string name = rng.Next(5) == 0
                    ? RefusedBatch[rng.Next(RefusedBatch.Length)]
                    : RefusedBare[rng.Next(RefusedBare.Length)] + (rng.Next(2) == 0 ? ".exe" : string.Empty);
                name = RandomCase(name, rng);
                // Windows drops trailing dots and spaces when it opens a file, so "cmd.exe." still
                // runs Command Prompt (#222).
                string program = Directories[rng.Next(Directories.Length)] + name + TrailingNoise[rng.Next(TrailingNoise.Length)];
                return program;
            });
            var arb = Arb.From(gen);

            var prop = Prop.ForAll(arb, program =>
            {
                // The tokens the splitter would hand over (it strips quotes), and the same program as
                // typed, with quotes, in a template: both must be refused.
                string direct = CustomCommandTemplate.ShellRefusal(new[] { program, "{path}" });
                string typedBare = CustomCommandTemplate.Validate(program.Contains(" ") ? "\"" + program + "\" {path}" : program + " {path}");
                string typedQuoted = CustomCommandTemplate.Validate("\"" + program + "\" {path}");

                // PowerShell is refused only without -File, and these templates have none.
                return direct != null && typedBare != null && typedQuoted != null;
            });
            Check.One(Cfg, prop);
        }

        [Test]
        public void ShellRefusal_RefusesBashStyleShellsGivenDashC_InAnyCaseAndPathForm()
        {
            var shells = new[] { "bash", "sh", "zsh" };
            var flags = new[] { "-c", "-lc", "-ic", "-xc" };
            var gen = Gen.Choose(0, int.MaxValue).Select(seed =>
            {
                var rng = new Random(seed);
                string name = RandomCase(shells[rng.Next(shells.Length)], rng) + (rng.Next(2) == 0 ? ".exe" : string.Empty);
                return Tuple.Create(Directories[rng.Next(Directories.Length)] + name, flags[rng.Next(flags.Length)]);
            });

            var prop = Prop.ForAll(Arb.From(gen), t =>
                CustomCommandTemplate.ShellRefusal(new[] { t.Item1, t.Item2, "{path}" }) != null
                && CustomCommandTemplate.Validate("\"" + t.Item1 + "\" " + t.Item2 + " {path}") != null);
            Check.One(Cfg, prop);
        }
    }
}
