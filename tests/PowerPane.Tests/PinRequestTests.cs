using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers how a <c>--pin</c> launch's arguments become a list of paths. Pure string work with
    /// invented paths, so nothing touches the disk.
    /// </summary>
    [TestFixture]
    public class PinRequestTests
    {
        [Test]
        public void ParseArguments_OnePath_ReturnsIt()
        {
            Assert.That(PinRequest.ParseArguments(new[] { "--pin", @"C:\Demo\Projects\Apollo" }),
                Is.EqualTo(new[] { @"C:\Demo\Projects\Apollo" }));
        }

        [Test]
        public void ParseArguments_FlagIsOptional()
        {
            Assert.That(PinRequest.ParseArguments(new[] { @"C:\Demo\Projects\Apollo" }),
                Is.EqualTo(new[] { @"C:\Demo\Projects\Apollo" }));
        }

        [Test]
        public void ParseArguments_SeveralPaths_KeepsTheirOrder()
        {
            var args = new[] { "--pin", @"C:\Demo\Zephyr", @"C:\Demo\Apollo\notes.txt", @"D:\Demo\Borealis" };
            Assert.That(PinRequest.ParseArguments(args),
                Is.EqualTo(new[] { @"C:\Demo\Zephyr", @"C:\Demo\Apollo\notes.txt", @"D:\Demo\Borealis" }));
        }

        [Test]
        public void ParseArguments_NoPathsOrNull_ReturnsEmpty()
        {
            Assert.That(PinRequest.ParseArguments(new string[0]), Is.Empty);
            Assert.That(PinRequest.ParseArguments(new[] { "--pin" }), Is.Empty);
            Assert.That(PinRequest.ParseArguments(null), Is.Empty);
        }

        [Test]
        public void ParseArguments_ValueWithStrayTrailingQuote_IsRepaired()
        {
            // The argument for --pin "C:\" reaches the program as C:"
            Assert.That(PinRequest.ParseArguments(new[] { "--pin", "C:\"" }), Is.EqualTo(new[] { @"C:\" }));
            Assert.That(PinRequest.ParseArguments(new[] { "--pin", "C:\\Demo\\Apollo\"" }),
                Is.EqualTo(new[] { @"C:\Demo\Apollo" }));
        }

        [Test]
        public void ParseArguments_BareDrive_GetsItsBackslash()
        {
            Assert.That(PinRequest.ParseArguments(new[] { "--pin", "D:" }), Is.EqualTo(new[] { @"D:\" }));
        }

        [Test]
        public void ParseArguments_DriveRootWithBackslash_IsKept()
        {
            Assert.That(PinRequest.ParseArguments(new[] { "--pin", @"D:\" }), Is.EqualTo(new[] { @"D:\" }));
        }

        [Test]
        public void ParseArguments_DuplicatesInDifferentCase_AreDropped()
        {
            var args = new[] { "--pin", @"C:\Demo\Apollo", @"c:\demo\APOLLO", @"C:\Demo\Zephyr" };
            Assert.That(PinRequest.ParseArguments(args), Is.EqualTo(new[] { @"C:\Demo\Apollo", @"C:\Demo\Zephyr" }));
        }

        [Test]
        public void ParseArguments_RelativeAndEmptyValues_AreRejected()
        {
            var args = new[] { "--pin", @"Demo\Apollo", "", "   ", @"C:Demo", @"\Demo", "Apollo", @"C:\Demo\Zephyr" };
            Assert.That(PinRequest.ParseArguments(args), Is.EqualTo(new[] { @"C:\Demo\Zephyr" }));
        }

        [Test]
        public void ParseArguments_UncPath_IsAccepted()
        {
            Assert.That(PinRequest.ParseArguments(new[] { "--pin", @"\\demo-nas\Projects\Apollo" }),
                Is.EqualTo(new[] { @"\\demo-nas\Projects\Apollo" }));
        }

        [Test]
        public void ParseArguments_StopsAtTheCap()
        {
            var args = new List<string> { "--pin" };
            for (int i = 0; i < PinRequest.MaxPaths + 25; i++) args.Add(@"C:\Demo\Item" + i);

            var parsed = PinRequest.ParseArguments(args.ToArray());

            Assert.That(parsed.Count, Is.EqualTo(PinRequest.MaxPaths));
            Assert.That(parsed.First(), Is.EqualTo(@"C:\Demo\Item0"));
            Assert.That(parsed.Last(), Is.EqualTo(@"C:\Demo\Item" + (PinRequest.MaxPaths - 1)));
        }

        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase(@"C:\Demo\Apollo", @"C:\Demo\Apollo")]
        [TestCase("C:\\Demo\\Apollo\"", @"C:\Demo\Apollo")]
        [TestCase("\"C:\\Demo\\My Apollo\"", @"C:\Demo\My Apollo")]
        [TestCase("C:\"", @"C:\")]
        [TestCase("D:", @"D:\")]
        [TestCase("d:", @"d:\")]
        [TestCase(@"D:\", @"D:\")]
        [TestCase(@"C:\Demo\Apollo   ", @"C:\Demo\Apollo")]
        [TestCase("   ", "")]
        public void NormalizePath_RepairsWhatTheShellProduces(string raw, string expected)
        {
            Assert.That(PinRequest.NormalizePath(raw), Is.EqualTo(expected));
        }

        [Test]
        public void QuickActionLibrary_PinArguments_StillParsesBackToOnePath()
        {
            // AC-13: "Pin this folder" starts a second copy with these arguments.
            foreach (var folder in new[] { @"C:\Demo\My Apollo", @"C:\Demo\Zephyr", @"D:\", @"C:\Demo\Trailing Space\" })
            {
                var commandLine = QuickActionLibrary.PinArguments(folder);
                var parsed = PinRequest.ParseArguments(SplitLikeWindows(commandLine));
                Assert.That(parsed.Count, Is.EqualTo(1), folder);
                Assert.That(parsed[0], Is.EqualTo(PinRequest.NormalizePath(folder)), folder);
            }
        }

        // The C runtime's rules for a command line, which is what the program receives in e.Args.
        private static string[] SplitLikeWindows(string commandLine)
        {
            var args = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false, any = false;
            for (int i = 0; i < commandLine.Length; i++)
            {
                char c = commandLine[i];
                if (c == '\\')
                {
                    int n = 0;
                    while (i < commandLine.Length && commandLine[i] == '\\') { n++; i++; }
                    if (i < commandLine.Length && commandLine[i] == '"')
                    {
                        sb.Append('\\', n / 2);
                        any = true;
                        if (n % 2 == 1) { sb.Append('"'); continue; }
                        i--; continue; // an even run leaves the quote to toggle quoting
                    }
                    sb.Append('\\', n); any = true; i--; continue;
                }
                if (c == '"') { inQuotes = !inQuotes; any = true; continue; }
                if (!inQuotes && (c == ' ' || c == '\t'))
                {
                    if (any) { args.Add(sb.ToString()); sb.Clear(); any = false; }
                    continue;
                }
                sb.Append(c); any = true;
            }
            if (any) args.Add(sb.ToString());
            return args.ToArray();
        }
    }
}
