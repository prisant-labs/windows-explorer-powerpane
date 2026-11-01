using System;
using System.IO;
using System.Linq;
using System.Text;

namespace QuickPane.Services
{
    /// <summary>What a quick action does with the folder it runs against (quick actions, first version):
    /// copies some form of its path to the clipboard, adds something new inside it, or starts an
    /// external program there. Only picks the pane row's icon glyph; QuickActionRunner is what
    /// actually runs an action, keyed on its Id.</summary>
    internal enum QuickActionKind { CopyPath, NewItem, OpenTool }

    /// <summary>One built-in quick action: a stable <see cref="Id"/> that settings.json stores
    /// (QuickActionSetting carries only this), and the display <see cref="Name"/> and one-line
    /// <see cref="Description"/> the Quick actions settings page and the pane's tooltip show. A later
    /// release can reword either without migrating anyone's settings.</summary>
    internal sealed class QuickActionDefinition
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public QuickActionKind Kind { get; private set; }

        public QuickActionDefinition(string id, string name, string description, QuickActionKind kind)
        {
            Id = id;
            Name = name;
            Description = description;
            Kind = kind;
        }
    }

    /// <summary>The fixed library of built-in quick actions (quick actions, first version), in their
    /// default pane order, plus the pure path-formatting, quoting, and unique-naming helpers every
    /// action needs. Nothing here touches the clipboard, disk, or a process -- that is
    /// QuickActionRunner -- so every method here is unit-testable with no temp directory beyond the
    /// two that genuinely read one (UniqueFileName, UniqueFolderName), and no window at all.</summary>
    internal static class QuickActionLibrary
    {
        public static readonly QuickActionDefinition[] All =
        {
            new QuickActionDefinition("copy-path", "Copy path",
                "The folder's path, exactly as Windows shows it.", QuickActionKind.CopyPath),
            new QuickActionDefinition("copy-path-quoted", "Copy path in quotes",
                "The path wrapped in \"double quotes\", ready to paste into a command line.", QuickActionKind.CopyPath),
            new QuickActionDefinition("copy-path-forward", "Copy path with forward slashes",
                "The path with every backslash changed to a forward slash.", QuickActionKind.CopyPath),
            new QuickActionDefinition("copy-path-wsl", "Copy WSL path",
                "The path as WSL would read it, such as /mnt/c/Demo. Does nothing for a network path.", QuickActionKind.CopyPath),
            new QuickActionDefinition("copy-markdown-link", "Copy Markdown link",
                "A Markdown link to the folder, such as [Demo](<C:/Demo>).", QuickActionKind.CopyPath),
            new QuickActionDefinition("new-text-file", "New text file",
                "Adds \"New Text Document.txt\" to the folder, never overwriting one already there.", QuickActionKind.NewItem),
            new QuickActionDefinition("new-markdown-file", "New Markdown file",
                "Adds a dated Markdown file with a \"# Untitled\" heading.", QuickActionKind.NewItem),
            new QuickActionDefinition("new-dated-folder", "New folder with today's date",
                "Adds a folder named with today's date.", QuickActionKind.NewItem),
            new QuickActionDefinition("open-terminal", "Open in Terminal",
                "Opens Windows Terminal here, or PowerShell if Terminal is not installed.", QuickActionKind.OpenTool),
            new QuickActionDefinition("open-vscode", "Open in Visual Studio Code",
                "Opens the folder in VS Code. Hidden when \"code\" is not on PATH.", QuickActionKind.OpenTool),
        };

        public static QuickActionDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var d in All)
                if (string.Equals(d.Id, id, StringComparison.Ordinal)) return d;
            return null;
        }

        // ---- path formats ---------------------------------------------------

        public static string ToQuoted(string path) { return "\"" + path + "\""; }

        public static string ToForwardSlashes(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        /// <summary>True for a UNC path (\\server\share\...), which has no /mnt/&lt;drive&gt; form
        /// under WSL.</summary>
        public static bool IsUncPath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.StartsWith(@"\\", StringComparison.Ordinal);
        }

        /// <summary>"C:\Demo\Projects" -&gt; "/mnt/c/Demo/Projects". Null for a UNC path or a path
        /// with no drive letter, since WSL has no fixed mapping for either; the caller treats null as
        /// "nothing to copy" rather than guessing at one.</summary>
        public static string ToWslPath(string path)
        {
            if (string.IsNullOrEmpty(path) || IsUncPath(path)) return null;
            if (path.Length < 2 || path[1] != ':') return null;
            char drive = char.ToLowerInvariant(path[0]);
            if (drive < 'a' || drive > 'z') return null;

            string rest = path.Substring(2).Replace('\\', '/');
            if (!rest.StartsWith("/", StringComparison.Ordinal)) rest = "/" + rest;
            return "/mnt/" + drive + rest;
        }

        /// <summary>"[Apollo](&lt;C:/Demo/Projects/Apollo&gt;)". The angle brackets are CommonMark's
        /// own escape for a link destination that contains a space, so a folder name with a space
        /// needs no escaping of its own.</summary>
        public static string ToMarkdownLink(string path)
        {
            return "[" + Leaf(path) + "](<" + ToForwardSlashes(path) + ">)";
        }

        /// <summary>The last path segment, with no trailing separator. "C:\Demo\Apollo" and
        /// "C:\Demo\Apollo\" both give "Apollo"; "C:\" gives "C:\" itself, since a drive root has no
        /// segment under it to peel off.</summary>
        public static string Leaf(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var trimmed = path.TrimEnd('\\', '/');
            if (trimmed.Length == 0) return path;
            int i = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            // No separator left after trimming means there was nothing but a drive root under the
            // trailing slash (e.g. "C:\"), which has no segment to peel off; the untrimmed path is
            // the most honest answer, not the drive letter with its own marker stripped off.
            return i >= 0 ? trimmed.Substring(i + 1) : path;
        }

        // ---- process-argument quoting ---------------------------------------

        /// <summary>Quotes one value the way CommandLineToArgvW parses a command line back apart, so
        /// a path with a space, a quote, an ampersand, a caret, a percent sign, or a trailing
        /// backslash survives as a single argument. Plain double quotes alone are not enough:
        /// CommandLineToArgvW treats a backslash specially only when it precedes a quote, so a
        /// trailing backslash right before the closing quote would otherwise escape that quote and
        /// run the argument into whatever follows it. Every quick action that starts a process goes
        /// through this one helper instead of composing its own quoting (quick actions, first version).
        /// Unaffected by &amp;, ^, and % -- those are cmd.exe's own metacharacters, and every quick
        /// action starts its process directly through ProcessStartInfo, never through cmd /c, so
        /// cmd.exe never sees this string at all.</summary>
        public static string QuoteArgument(string value)
        {
            if (value == null) value = string.Empty;
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return value;

            var sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                    continue;
                }
                if (backslashes > 0) { sb.Append('\\', backslashes); backslashes = 0; }
                sb.Append(c);
            }
            // A run of backslashes right before the closing quote must be doubled, or the first of the
            // pair would escape that quote instead of ending the argument.
            if (backslashes > 0) sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>The argument string for "Open in Terminal": "-d &lt;path&gt;" for wt.exe, quoted
        /// through <see cref="QuoteArgument"/> like every other process this file starts, or a
        /// PowerShell fallback that must quote the path a different way. PowerShell's own -Command
        /// parses the rest of the line as script text, not as a plain argv, so CommandLineToArgvW
        /// quoting does not protect a folder name containing "$" (e.g. "$RECYCLE.BIN") from being read
        /// as a variable reference; wrapping it in PowerShell's single-quoted string literal does,
        /// since nothing expands inside one except a doubled single quote.</summary>
        public static string TerminalArguments(string folder, bool haveWt)
        {
            if (haveWt) return "-d " + QuoteArgument(folder);
            string psLiteral = "'" + (folder ?? string.Empty).Replace("'", "''") + "'";
            return "-NoExit -Command Set-Location -LiteralPath " + psLiteral;
        }

        // ---- unique naming ---------------------------------------------------

        /// <summary>A file path inside <paramref name="directory"/> that does not exist yet: the
        /// plain name first, then " (2)", " (3)", and so on before the extension, the same numbering
        /// Explorer itself uses for a second copy. Never returns a path that already exists as either
        /// a file or a folder, so a quick action can never overwrite something it did not create
        /// (quick actions, first version).</summary>
        public static string UniqueFileName(string directory, string baseName, string extension)
        {
            string candidate = Path.Combine(directory, baseName + extension);
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            for (int n = 2; ; n++)
            {
                candidate = Path.Combine(directory, baseName + " (" + n + ")" + extension);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
        }

        /// <summary>Same idea as <see cref="UniqueFileName"/>, for a folder name with no extension.</summary>
        public static string UniqueFolderName(string directory, string baseName)
        {
            return UniqueFileName(directory, baseName, string.Empty);
        }

        // ---- PATH lookup -------------------------------------------------------

        /// <summary>Searches every directory on PATH (and, when <paramref name="command"/> carries no
        /// extension of its own, every extension on PATHEXT) for a program, the same resolution order
        /// CreateProcess itself uses, and returns the first match's full path or null. Pure and
        /// synchronous: a caller that must not block the UI thread goes through WorkQueue itself, the
        /// way <see cref="BeginCodeCheck"/> does below.</summary>
        public static string FindOnPath(string command)
        {
            if (string.IsNullOrEmpty(command)) return null;
            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var dirs = pathVar.Split(';').Where(d => !string.IsNullOrWhiteSpace(d));

            bool hasExtension = Path.HasExtension(command);
            string[] exts = hasExtension
                ? new[] { string.Empty }
                : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';');

            foreach (var dir in dirs)
            {
                foreach (var ext in exts)
                {
                    string candidate;
                    try { candidate = Path.Combine(dir.Trim(), command + ext); }
                    catch (ArgumentException) { continue; } // a malformed PATH entry; left alone
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        // ---- VS Code's real executable, not its PATH shim ----------------------

        /// <summary>VS Code's PATH entry is "code.cmd", a batch file, and CreateProcess cannot launch
        /// one directly (only ShellExecute can, by handing it to cmd.exe through the .cmd file
        /// association) -- and every quick action that starts a process is required to go through
        /// ProcessStartInfo directly, never through cmd /c, because cmd.exe's own metacharacters
        /// (&amp;, ^, %) are not what QuoteArgument guards against. The installer always places the
        /// real "Code.exe" one folder above the "bin" folder that holds code.cmd, so this walks there
        /// instead of ever invoking the shim. Pure given its input, and the input itself (whatever
        /// FindOnPath("code") returns) is also deterministic for a given PATH, so <see cref="FindCodeExe"/>
        /// below is what callers that care about the real machine's PATH use; this one is what a test
        /// exercises with a path it built itself.</summary>
        internal static string ResolveCodeExe(string codeCmdPath)
        {
            if (string.IsNullOrEmpty(codeCmdPath)) return null;
            if (!string.Equals(Path.GetFileName(codeCmdPath), "code.cmd", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                string bin = Path.GetDirectoryName(codeCmdPath);                  // ...\Microsoft VS Code\bin
                string root = bin != null ? Path.GetDirectoryName(bin) : null;     // ...\Microsoft VS Code
                if (string.IsNullOrEmpty(root)) return null;
                string exe = Path.Combine(root, "Code.exe");
                return File.Exists(exe) ? exe : null;
            }
            catch (ArgumentException) { return null; } // a malformed path; treated as "not found"
        }

        /// <summary>"code" resolved all the way to a real, directly launchable Code.exe, or null when
        /// "code" is not on PATH at all or its PATH entry is not the expected code.cmd shim next to an
        /// install root. Both the pane row's visibility and the action that runs it treat null the
        /// same way: VS Code is not available.</summary>
        public static string FindCodeExe()
        {
            return ResolveCodeExe(FindOnPath("code"));
        }

        // ---- "code" on PATH, checked once off the UI thread --------------------

        private static readonly object Gate = new object();
        private static bool _codeCheckStarted;
        private static bool _codeChecked;
        private static bool _codeOnPath;

        /// <summary>Raised on the UI thread once <see cref="BeginCodeCheck"/>'s background probe has
        /// an answer, so a pane already built can show or hide "Open in Visual Studio Code"
        /// accordingly.</summary>
        public static event Action CodeAvailabilityChanged;

        public static bool CodeCheckDone { get { lock (Gate) return _codeChecked; } }
        public static bool CodeOnPath { get { lock (Gate) return _codeOnPath; } }

        /// <summary>Starts the PATH probe for "code" on the worker, unless it has already started.
        /// Safe to call from every pane build and every pinned-row menu build; only the first call
        /// does any work.</summary>
        public static void BeginCodeCheck()
        {
            lock (Gate)
            {
                if (_codeCheckStarted) return;
                _codeCheckStarted = true;
            }
            WorkQueue.Post(() =>
            {
                bool found = FindCodeExe() != null;
                lock (Gate) { _codeOnPath = found; _codeChecked = true; }
                var h = CodeAvailabilityChanged;
                if (h != null) WorkQueue.PostUI(h);
            });
        }

        /// <summary>Test-only: lets a test re-arm the check instead of trusting whatever a previous
        /// test, or the real machine this build happens to run on, already decided.</summary>
        internal static void ResetCodeCheckForTests()
        {
            lock (Gate) { _codeCheckStarted = false; _codeChecked = false; _codeOnPath = false; }
        }
    }
}
