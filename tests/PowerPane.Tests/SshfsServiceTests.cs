using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// Pins BuildCommandLine's quoting contract: an argument with a space or an embedded quote comes
    /// back as the same single argument after Windows re-splits the command line, never as two. This
    /// guards against an SSH connection name injecting an extra -o option by closing its own quote
    /// early.
    /// </summary>
    [TestFixture]
    public class SshfsServiceTests
    {
        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        // CommandLineToArgvW parses argv[0] (the program name) under different rules than the rest of
        // the command line, so a dummy program token is prepended and dropped here, leaving only the
        // arguments BuildCommandLine actually produced.
        private static string[] ParseCommandLine(string commandLine)
        {
            int argc;
            IntPtr argv = CommandLineToArgvW("x " + commandLine, out argc);
            if (argv == IntPtr.Zero) throw new InvalidOperationException("CommandLineToArgvW failed");
            try
            {
                var result = new string[argc - 1];
                for (int i = 1; i < argc; i++)
                {
                    IntPtr p = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                    result[i - 1] = Marshal.PtrToStringUni(p);
                }
                return result;
            }
            finally { LocalFree(argv); }
        }

        [Test]
        public void BuildCommandLine_PlainArguments_RoundTrip()
        {
            var args = new List<string> { "-p", "22", "user@host:/", "S:" };
            var cmd = SshfsService.BuildCommandLine(args);
            Assert.That(ParseCommandLine(cmd), Is.EqualTo(args.ToArray()));
        }

        [Test]
        public void BuildCommandLine_ArgumentWithSpace_StaysOneArgument()
        {
            var args = new List<string> { "-o", @"IdentityFile=C:\Demo\Keys\Test User\id_ed25519" };
            var cmd = SshfsService.BuildCommandLine(args);
            Assert.That(ParseCommandLine(cmd), Is.EqualTo(args.ToArray()));
        }

        [Test]
        public void BuildCommandLine_ArgumentWithEmbeddedQuote_EscapesAndRoundTrips()
        {
            var args = new List<string> { "volname=Bob\"s Server" };
            var cmd = SshfsService.BuildCommandLine(args);
            Assert.That(ParseCommandLine(cmd), Is.EqualTo(args.ToArray()));
        }

        [Test]
        public void BuildCommandLine_ConnectionNameCannotInjectExtraOption()
        {
            // A connection name crafted to look like it closes its quote and opens a new
            // -o option must still come back as one argument, not split into "-o" and a value.
            var hostile = new List<string> { "myconn\" -o ProxyCommand=calc.exe" };
            var cmd = SshfsService.BuildCommandLine(hostile);
            var parsed = ParseCommandLine(cmd);

            Assert.That(parsed.Length, Is.EqualTo(1));
            Assert.That(parsed[0], Is.EqualTo(hostile[0]));
        }

        [Test]
        public void BuildCommandLine_TrailingBackslashesBeforeClosingQuote_Survive()
        {
            // The argument needs a space to force quoting at all, and a trailing backslash to
            // exercise the doubling that keeps that backslash from escaping the closing quote.
            var args = new List<string> { @"C:\Demo\Test User\" };
            var cmd = SshfsService.BuildCommandLine(args);
            Assert.That(ParseCommandLine(cmd), Is.EqualTo(args.ToArray()));
        }
    }
}
