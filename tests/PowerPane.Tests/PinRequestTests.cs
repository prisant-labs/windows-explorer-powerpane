using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmd, out int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr mem);

        // The parser a started program's arguments come from, after the program name.
        private static string[] SplitLikeWindows(string commandLine)
        {
            int count;
            IntPtr argv = CommandLineToArgvW("prog.exe " + commandLine, out count);
            Assert.That(argv, Is.Not.EqualTo(IntPtr.Zero));
            try
            {
                var result = new List<string>();
                for (int i = 1; i < count; i++)
                    result.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)));
                return result.ToArray();
            }
            finally { LocalFree(argv); }
        }
    }
}
