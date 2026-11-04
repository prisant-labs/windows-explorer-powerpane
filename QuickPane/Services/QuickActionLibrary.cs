using System;
using System.IO;
using System.Linq;
using System.Text;
using QuickPane.Models;

namespace QuickPane.Services
{
    /// <summary>What a quick action does with the folder it runs against (quick actions, first version):
    /// copies some form of its path to the clipboard, adds something new inside it, or starts an
    /// external program there. Only picks the pane row's icon glyph; QuickActionRunner is what
    /// actually runs an action, keyed on its Id. FolderAction is anything else done to the folder
    /// itself, today only pinning it. Selection acts on what is selected in Explorer (#191), and
    /// Custom is a command the user wrote (#190).</summary>
    internal enum QuickActionKind { CopyPath, NewItem, OpenTool, FolderAction, Selection, Custom }

    /// <summary>One built-in quick action: a stable <see cref="Id"/> that settings.json stores
    /// (QuickActionSetting carries only this), and the display <see cref="Name"/> and one-line
    /// <see cref="Description"/> the Quick actions settings page and the pane's tooltip show. A later
    /// release can reword either without migrating anyone's settings. <see cref="DefaultEnabled"/> is
    /// the state the action starts in, both for someone whose settings predate it and after Reset to
    /// default. An action added after the first ten must leave it false, or every release would grow
    /// the section of everyone already using it.</summary>
    internal sealed class QuickActionDefinition
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public QuickActionKind Kind { get; private set; }
        public bool DefaultEnabled { get; private set; }

        /// <summary>A custom command's template, which is also what its row's tooltip shows. Null for a
        /// built-in action.</summary>
        public string Template { get { return Kind == QuickActionKind.Custom ? Description : null; } }

        public QuickActionDefinition(string id, string name, string description, QuickActionKind kind, bool defaultEnabled)
        {
            Id = id;
            Name = name;
            Description = description;
            Kind = kind;
            DefaultEnabled = defaultEnabled;
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
                "The folder's path, exactly as Windows shows it.", QuickActionKind.CopyPath, true),
            new QuickActionDefinition("copy-path-quoted", "Copy path in quotes",
                "The path wrapped in \"double quotes\", ready to paste into a command line.", QuickActionKind.CopyPath, true),
            new QuickActionDefinition("copy-path-forward", "Copy path with forward slashes",
                "The path with every backslash changed to a forward slash.", QuickActionKind.CopyPath, true),
            new QuickActionDefinition("copy-path-wsl", "Copy WSL path",
                "The path as WSL would read it, such as /mnt/c/Demo. Does nothing for a network path.", QuickActionKind.CopyPath, true),
            new QuickActionDefinition("copy-markdown-link", "Copy Markdown link",
                "A Markdown link to the folder, such as [Demo](<C:/Demo>).", QuickActionKind.CopyPath, true),
            new QuickActionDefinition("copy-folder-name", "Copy folder name",
                "Only the folder's own name, such as Apollo.", QuickActionKind.CopyPath, false),
            new QuickActionDefinition("copy-parent-path", "Copy parent folder path",
                "The path of the folder that holds this one. Does nothing at a drive root.", QuickActionKind.CopyPath, false),
            new QuickActionDefinition("copy-file-url", "Copy as file URL",
                "The path as a file:// URL, with special characters encoded.", QuickActionKind.CopyPath, false),
            new QuickActionDefinition("copy-cd-powershell", "Copy PowerShell change-directory command",
                "A Set-Location command for this folder, ready to paste into PowerShell.", QuickActionKind.CopyPath, false),
            new QuickActionDefinition("new-text-file", "New text file",
                "Adds \"New Text Document.txt\" to the folder, never overwriting one already there.", QuickActionKind.NewItem, true),
            new QuickActionDefinition("new-markdown-file", "New Markdown file",
                "Adds a dated Markdown file with a \"# Untitled\" heading.", QuickActionKind.NewItem, true),
            new QuickActionDefinition("new-dated-folder", "New folder with today's date",
                "Adds a folder named with today's date.", QuickActionKind.NewItem, true),
            new QuickActionDefinition("new-powershell-script", "New PowerShell script",
                "Adds \"script.ps1\" to the folder, never overwriting one already there.", QuickActionKind.NewItem, false),
            new QuickActionDefinition("new-readme", "New README",
                "Adds \"README.md\" with the folder's name as its heading, never overwriting one already there.", QuickActionKind.NewItem, false),
            new QuickActionDefinition("new-gitignore", "New .gitignore",
                "Adds a \".gitignore\" with common entries, never overwriting one already there.", QuickActionKind.NewItem, false),
            new QuickActionDefinition("open-terminal", "Open in Terminal",
                "Opens Windows Terminal here, or PowerShell if Terminal is not installed.", QuickActionKind.OpenTool, true),
            new QuickActionDefinition("open-vscode", "Open in Visual Studio Code",
                "Opens the folder in VS Code. Hidden when \"code\" is not on PATH.", QuickActionKind.OpenTool, true),
            new QuickActionDefinition("open-powershell", "Open PowerShell here",
                "Opens PowerShell in the folder: PowerShell 7 if it is installed, or else Windows PowerShell.", QuickActionKind.OpenTool, false),
            new QuickActionDefinition("open-wsl", "Open WSL here",
                "Opens your default WSL distribution in the folder. Hidden when no distribution is installed.", QuickActionKind.OpenTool, false),
            new QuickActionDefinition("pin-folder", "Pin this folder",
                "Opens the group picker to pin the folder, the same as the Explorer right-click command.", QuickActionKind.FolderAction, false),
            // Separate rows rather than "Copy path" preferring the selection: Explorer selects the
            // folder you came from when you go up a level, so that row would quietly copy the wrong
            // path after an ordinary navigation (#191).
            new QuickActionDefinition("copy-selected-names", "Copy selected names",
                "The names of the items selected in Explorer, one per line.", QuickActionKind.Selection, false),
            new QuickActionDefinition("copy-selected-paths", "Copy selected paths",
                "The full paths of the selected items, one per line.", QuickActionKind.Selection, false),
            new QuickActionDefinition("copy-selected-quoted", "Copy selected paths in quotes",
                "The selected paths in \"double quotes\", one per line.", QuickActionKind.Selection, false),
            new QuickActionDefinition("copy-selected-forward", "Copy selected paths with forward slashes",
                "The selected paths with forward slashes, one per line.", QuickActionKind.Selection, false),
            new QuickActionDefinition("copy-selected-wsl", "Copy selected WSL paths",
                "The selected paths as WSL would read them, one per line. Skips network paths.", QuickActionKind.Selection, false),
            new QuickActionDefinition("copy-selected-urls", "Copy selected as file URLs",
                "The selected paths as file:// URLs, one per line.", QuickActionKind.Selection, false),
            new QuickActionDefinition("copy-selected-markdown", "Copy selected as Markdown links",
                "A Markdown link to each selected item, one per line.", QuickActionKind.Selection, false),
            new QuickActionDefinition("open-selected-vscode", "Open selection in Visual Studio Code",
                "Opens the selected items in VS Code. Hidden when \"code\" is not on PATH.", QuickActionKind.Selection, false),
        };

        public static QuickActionDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var d in All)
                if (string.Equals(d.Id, id, StringComparison.Ordinal)) return d;
            return null;
        }

        /// <summary><see cref="Find"/>, plus the custom commands in <paramref name="settings"/>. Anything
        /// that reads AppSettings.QuickActions uses this one, because an entry there can be either.</summary>
        public static QuickActionDefinition Resolve(AppSettings settings, string id)
        {
            var builtIn = Find(id);
            if (builtIn != null || !CustomCommandTemplate.IsCustomId(id) || settings == null || settings.CustomCommands == null)
                return builtIn;
            foreach (var c in settings.CustomCommands)
                if (string.Equals(c.Id, id, StringComparison.Ordinal))
                    return new QuickActionDefinition(c.Id, c.Name, c.Template, QuickActionKind.Custom, false);
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

        /// <summary>"C:\Demo\Apollo" -&gt; "C:\Demo". Null at a drive root or a share root, which have
        /// nothing above them. The trailing separator comes off first, because
        /// Path.GetDirectoryName would otherwise read "C:\Demo\Apollo\" as "C:\Demo\Apollo".</summary>
        public static string ParentPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try { return Path.GetDirectoryName(path.TrimEnd('\\', '/')); }
            catch (ArgumentException) { return null; }
        }

        /// <summary>"C:\Demo\My Apollo" -&gt; "file:///C:/Demo/My%20Apollo", and "\\server\share" -&gt;
        /// "file://server/share". Each segment is encoded on its own instead of going through
        /// new Uri(path).AbsoluteUri, because a path is not a URL: a "#" or "%" in a folder name is an
        /// ordinary character there, and must come out encoded rather than start a fragment or read as
        /// an escape.</summary>
        public static string ToFileUrl(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            bool unc = IsUncPath(path);
            var segments = path.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var encoded = segments.Select((s, i) =>
                !unc && i == 0 && s.Length == 2 && s[1] == ':' ? s : Uri.EscapeDataString(s));
            string joined = string.Join("/", encoded);
            if (unc) return "file://" + joined;
            // A bare drive root needs its slash: "file:///C:" does not name a folder.
            return "file:///" + joined + (segments.Length == 1 ? "/" : string.Empty);
        }

        /// <summary>A Set-Location command for the folder. A single-quoted string is the one PowerShell
        /// form that expands nothing, so "$RECYCLE.BIN" stays literal, and doubling a quote is its only
        /// escape. PowerShell also treats U+2018 to U+201B as single quotes, so those are doubled too:
        /// left alone, one of them would close the string early when the command is pasted.</summary>
        public static string ToPowerShellChangeDirectory(string path)
        {
            var sb = new StringBuilder("Set-Location -LiteralPath '");
            foreach (char c in path ?? string.Empty)
            {
                sb.Append(c);
                if (c == '\'' || (c >= '\u2018' && c <= '\u201B')) sb.Append(c);
            }
            return sb.Append('\'').ToString();
        }

        // ---- text for new files ------------------------------------------------

        public static string ReadmeContent(string folder)
        {
            return "# " + Leaf(folder) + Environment.NewLine;
        }

        public static string PowerShellScriptContent()
        {
            return "Set-StrictMode -Version Latest" + Environment.NewLine + "$ErrorActionPreference = 'Stop'" + Environment.NewLine;
        }

        /// <summary>A short built-in list, never a downloaded one: the app does not write out content
        /// it fetched.</summary>
        public static string GitignoreContent()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "# Operating system files", "Thumbs.db", "desktop.ini", ".DS_Store", "",
                "# Build output", "bin/", "obj/", "build/", "dist/", "",
                "# Dependencies and caches", "node_modules/", "__pycache__/", "",
                "# Logs", "*.log", "",
            });
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

        /// <summary>The argument string for "Open in Terminal": neither branch puts the folder in the
        /// argument string at all -- the caller places the new process directly in that folder through
        /// <see cref="System.Diagnostics.ProcessStartInfo.WorkingDirectory"/> instead, which the OS
        /// sets before the process's own command-line parsing ever runs. Two earlier approaches both
        /// let a folder name reach a second parser once the argument got there: wt.exe treats an
        /// unescaped ";" in "-d &lt;path&gt;" as its own tab/pane separator (a folder such as
        /// "C:\x;calc" could start a second tab), and PowerShell's "-Command" re-parses its remainder
        /// as script text, not argv -- so even a correctly argv-quoted path let "$RECYCLE.BIN" be read
        /// as a variable, and let a folder name holding one of PowerShell's non-ASCII single-quote
        /// look-alikes (U+2018-U+201B, which PowerShell also treats as a quote character) close the
        /// literal early and run whatever followed. "-d ." tells wt.exe to use its own process's
        /// current directory -- the same pattern Microsoft's own "Open in Windows Terminal here" shell
        /// extension uses -- and "-NoExit" alone starts PowerShell interactively in whatever directory
        /// CreateProcess already placed it in, with no script text to parse at all.</summary>
        public static string TerminalArguments(bool haveWt)
        {
            return haveWt ? "-d ." : "-NoExit";
        }

        /// <summary>The argument string that starts QuickPane's pin mode for a folder.</summary>
        public static string PinArguments(string folder)
        {
            return "--pin " + QuoteArgument(folder);
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

        // ---- where an action may appear -----------------------------------------

        /// <summary>Whether an action belongs in a pinned row's Quick actions submenu. "Pin this folder"
        /// does not, because that row is a pin already. A selection action does not either, because a
        /// pinned row is not looking at any Explorer window's selection.</summary>
        public static bool AppliesToPinnedRow(QuickActionDefinition def)
        {
            return def != null && def.Id != "pin-folder" && def.Kind != QuickActionKind.Selection;
        }

        /// <summary>Whether running this action needs the Explorer selection read first. A custom command
        /// that uses only {dir}, or no placeholder, skips the cross-process call.</summary>
        public static bool UsesSelection(QuickActionDefinition def)
        {
            if (def == null) return false;
            return def.Kind == QuickActionKind.Selection
                || (def.Kind == QuickActionKind.Custom && CustomCommandTemplate.UsesTargets(def.Template));
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
