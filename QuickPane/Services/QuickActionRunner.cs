using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace QuickPane.Services
{
    /// <summary>Runs one quick action (quick actions, first version) against a folder. Called only from a
    /// delegate already posted to <see cref="WorkQueue"/>: the pane's Quick actions section resolves
    /// "the folder the host Explorer window shows" through a cross-process Shell COM call first, and
    /// a pinned row's "Quick actions" submenu already knows its own folder synchronously but still
    /// posts here, because the existence check and every action's own disk or process work must stay
    /// off the UI thread the same way. The one exception is the clipboard write a copy action makes:
    /// that hops back to the UI thread, because Clipboard.SetText needs an STA thread with a message
    /// pump, which the general WorkQueue lane -- an STA thread created only to satisfy the shell COM
    /// calls other actions and sections use -- does not have.</summary>
    internal static class QuickActionRunner
    {
        public static void Run(string actionId, string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                Log.Info("quick action '" + actionId + "': the current folder is not known.");
                return;
            }
            if (!Directory.Exists(folder))
            {
                Log.Info("quick action '" + actionId + "': '" + folder + "' no longer exists.");
                return;
            }

            var def = QuickActionLibrary.Find(actionId);
            if (def == null) { Log.Info("quick action: unknown id '" + actionId + "'."); return; }

            if (def.Kind == QuickActionKind.CopyPath)
            {
                string text = ClipboardTextFor(actionId, folder);
                if (text == null)
                {
                    Log.Info("quick action '" + actionId + "': nothing to copy for '" + folder + "'.");
                    return;
                }
                WorkQueue.PostUI(() => CopyToClipboard(text));
                return;
            }

            RunSideEffect(actionId, folder);
        }

        /// <summary>The text a copy action puts on the clipboard for <paramref name="folder"/>, or
        /// null when this id/folder combination has nothing sensible to copy -- today, only a WSL
        /// path request against a UNC folder or a folder with no drive letter.</summary>
        internal static string ClipboardTextFor(string actionId, string folder)
        {
            switch (actionId)
            {
                case "copy-path": return folder;
                case "copy-path-quoted": return QuickActionLibrary.ToQuoted(folder);
                case "copy-path-forward": return QuickActionLibrary.ToForwardSlashes(folder);
                case "copy-path-wsl": return QuickActionLibrary.ToWslPath(folder);
                case "copy-markdown-link": return QuickActionLibrary.ToMarkdownLink(folder);
                case "copy-folder-name": return QuickActionLibrary.Leaf(folder);
                case "copy-parent-path": return QuickActionLibrary.ParentPath(folder);
                case "copy-file-url": return QuickActionLibrary.ToFileUrl(folder);
                case "copy-cd-powershell": return QuickActionLibrary.ToPowerShellChangeDirectory(folder);
                default: return null;
            }
        }

        private static void CopyToClipboard(string text)
        {
            try { Clipboard.SetText(text); }
            catch (ExternalException ex) { Log.Error("quick action clipboard write (clipboard busy)", ex); }
            catch (Exception ex) { Log.Error("quick action clipboard write", ex); }
        }

        // ---- new-item and open-tool actions: disk and process work, already off the UI thread ----

        private static void RunSideEffect(string actionId, string folder)
        {
            switch (actionId)
            {
                case "new-text-file": NewTextFile(folder); break;
                case "new-markdown-file": NewMarkdownFile(folder); break;
                case "new-dated-folder": NewDatedFolder(folder); break;
                case "new-powershell-script": NewFile(actionId, folder, "script", ".ps1", QuickActionLibrary.PowerShellScriptContent()); break;
                case "new-readme": NewFile(actionId, folder, "README", ".md", QuickActionLibrary.ReadmeContent(folder)); break;
                // The whole name goes in baseName, so a second copy reads ".gitignore (2)" and never
                // " (2).gitignore", which Git would not recognize either way but which reads worse.
                case "new-gitignore": NewFile(actionId, folder, ".gitignore", string.Empty, QuickActionLibrary.GitignoreContent()); break;
                case "open-terminal": OpenTerminal(folder); break;
                case "open-vscode": OpenVsCode(folder); break;
                case "open-powershell": OpenPowerShell(folder); break;
                case "open-wsl": OpenWsl(folder); break;
                case "pin-folder": PinFolder(folder); break;
                default: Log.Info("quick action: unknown id '" + actionId + "'."); break;
            }
        }

        // UniqueFileName/UniqueFolderName only check whether a name is free; nothing holds that name
        // reserved between the check and the create call below it, so two quick clicks (or a second
        // process) can both pick the same candidate. A capped retry loop, not a single attempt, is
        // what actually keeps the "never overwrite" promise under that race.
        private const int MaxUniqueNameAttempts = 100;

        private static void NewTextFile(string folder)
        {
            try
            {
                string path;
                if (TryCreateUniqueFile(folder, "New Text Document", ".txt", string.Empty, out path))
                    Log.Info("quick action 'new-text-file': created '" + path + "'.");
                else
                    Log.Error("quick action 'new-text-file': could not find a free name in '" + folder + "'.");
            }
            catch (Exception ex) { Log.Error("quick action 'new-text-file'", ex); }
        }

        private static void NewMarkdownFile(string folder)
        {
            try
            {
                // InvariantCulture, not the current thread's culture: a non-Gregorian calendar (e.g.
                // Thai Buddhist, th-TH) would otherwise put a different year in the name.
                string baseName = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-untitled";
                string path;
                if (TryCreateUniqueFile(folder, baseName, ".md", "# Untitled" + Environment.NewLine, out path))
                    Log.Info("quick action 'new-markdown-file': created '" + path + "'.");
                else
                    Log.Error("quick action 'new-markdown-file': could not find a free name in '" + folder + "'.");
            }
            catch (Exception ex) { Log.Error("quick action 'new-markdown-file'", ex); }
        }

        private static void NewFile(string actionId, string folder, string baseName, string extension, string content)
        {
            try
            {
                string path;
                if (TryCreateUniqueFile(folder, baseName, extension, content, out path))
                    Log.Info("quick action '" + actionId + "': created '" + path + "'.");
                else
                    Log.Error("quick action '" + actionId + "': could not find a free name in '" + folder + "'.");
            }
            catch (Exception ex) { Log.Error("quick action '" + actionId + "'", ex); }
        }

        /// <summary>Creates a file holding <paramref name="content"/> under a name
        /// <see cref="QuickActionLibrary.UniqueFileName"/> reports free, retrying with the next
        /// candidate (up to <see cref="MaxUniqueNameAttempts"/>) whenever <see cref="FileMode.CreateNew"/>
        /// finds that name already taken -- which only an exclusive create-or-fail open can detect
        /// reliably, since a plain existence check followed by <see cref="File.WriteAllText"/> would
        /// silently overwrite whatever had just appeared there.</summary>
        private static bool TryCreateUniqueFile(string folder, string baseName, string extension, string content, out string createdPath)
        {
            for (int attempt = 0; attempt < MaxUniqueNameAttempts; attempt++)
            {
                string path = QuickActionLibrary.UniqueFileName(folder, baseName, extension);
                try
                {
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    using (var writer = new StreamWriter(stream))
                        writer.Write(content);
                    createdPath = path;
                    return true;
                }
                catch (IOException)
                {
                    // Somebody else (another click, another process) created this exact name between
                    // UniqueFileName's check and here; ask it for the next candidate instead of
                    // overwriting what just appeared.
                }
            }
            createdPath = null;
            return false;
        }

        private static void NewDatedFolder(string folder)
        {
            try
            {
                // InvariantCulture for the same reason as NewMarkdownFile above.
                string baseName = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                for (int attempt = 0; attempt < MaxUniqueNameAttempts; attempt++)
                {
                    string path = QuickActionLibrary.UniqueFolderName(folder, baseName);
                    if (TryCreateDirectoryExclusive(path))
                    {
                        Log.Info("quick action 'new-dated-folder': created '" + path + "'.");
                        return;
                    }
                    // Somebody else (another click, another process) created this exact name between
                    // UniqueFolderName's check and here; ask it for the next candidate instead of
                    // reusing what just appeared.
                }
                Log.Error("quick action 'new-dated-folder': could not find a free name in '" + folder + "'.");
            }
            catch (Exception ex) { Log.Error("quick action 'new-dated-folder'", ex); }
        }

        // Directory.CreateDirectory cannot tell "I created it" apart from "it was already there" --
        // it succeeds silently either way, which is exactly how two near-simultaneous calls can both
        // "succeed" on the identical path and collapse into one folder. The underlying Win32
        // CreateDirectoryW does distinguish the two cases (ERROR_ALREADY_EXISTS the second time), so
        // this P/Invoke, not .NET's own wrapper, is what the retry loop above can actually trust.
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateDirectoryW(string lpPathName, IntPtr lpSecurityAttributes);

        private const int ErrorAlreadyExists = 183;

        /// <summary>True only if this call is the one that actually created <paramref name="path"/>;
        /// false (never throwing for this one reason) if it already existed, so the caller can treat
        /// that as "somebody else just took this name" and try the next candidate.</summary>
        private static bool TryCreateDirectoryExclusive(string path)
        {
            if (CreateDirectoryW(path, IntPtr.Zero)) return true;
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorAlreadyExists) return false;
            throw new IOException("Could not create '" + path + "' (Win32 error " + error + ").");
        }

        /// <summary>wt.exe when it is on PATH, else powershell.exe, started directly through
        /// ProcessStartInfo with its own argument list -- never "cmd /c" -- so a folder name holding
        /// an ampersand or a caret cannot start a second command (quick actions, first version). The
        /// folder never appears in the argument string at all (see TerminalArguments): WorkingDirectory
        /// alone places the new process there, which is what keeps a ";" (wt.exe's own tab separator)
        /// or a PowerShell quote character from ever reaching either program's own parser. Never
        /// elevated: ProcessStartInfo.UseShellExecute defaults false and nothing here sets Verb.</summary>
        private static void OpenTerminal(string folder)
        {
            string wt = QuickActionLibrary.FindOnPath("wt.exe");
            try
            {
                if (wt != null)
                {
                    Process.Start(new ProcessStartInfo(wt)
                    {
                        Arguments = QuickActionLibrary.TerminalArguments(haveWt: true),
                        WorkingDirectory = folder,
                        UseShellExecute = false
                    });
                    Log.Info("quick action 'open-terminal': started Windows Terminal in '" + folder + "'.");
                    return;
                }
                Process.Start(new ProcessStartInfo("powershell.exe")
                {
                    Arguments = QuickActionLibrary.TerminalArguments(haveWt: false),
                    WorkingDirectory = folder,
                    UseShellExecute = false
                });
                Log.Info("quick action 'open-terminal': wt.exe not on PATH; started PowerShell in '" + folder + "'.");
            }
            catch (Exception ex) { Log.Error("quick action 'open-terminal'", ex); }
        }

        /// <summary>PowerShell 7 when it is on PATH, or else Windows PowerShell. Same rule as
        /// <see cref="OpenTerminal"/>: only the working directory carries the folder.</summary>
        private static void OpenPowerShell(string folder)
        {
            string program = QuickActionLibrary.FindOnPath("pwsh.exe") ?? "powershell.exe";
            try
            {
                Process.Start(new ProcessStartInfo(program)
                {
                    Arguments = QuickActionLibrary.TerminalArguments(haveWt: false),
                    WorkingDirectory = folder,
                    UseShellExecute = false
                });
                Log.Info("quick action 'open-powershell': started '" + program + "' in '" + folder + "'.");
            }
            catch (Exception ex) { Log.Error("quick action 'open-powershell'", ex); }
        }

        /// <summary>wsl.exe with no arguments opens the default distribution's shell in the current
        /// directory, which it translates itself, so no folder name reaches an argument here either.</summary>
        private static void OpenWsl(string folder)
        {
            string wsl = QuickActionLibrary.FindOnPath("wsl.exe");
            if (wsl == null)
            {
                Log.Info("quick action 'open-wsl': wsl.exe is not on PATH.");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(wsl) { WorkingDirectory = folder, UseShellExecute = false });
                Log.Info("quick action 'open-wsl': started WSL in '" + folder + "'.");
            }
            catch (Exception ex) { Log.Error("quick action 'open-wsl'", ex); }
        }

        /// <summary>Starts a second copy of QuickPane in its existing pin mode, the same flow as the
        /// Explorer right-click command. That mode runs before the single-instance check and exits when
        /// its picker closes, and the running copy's folder watcher picks up the new pin. Running the
        /// picker in its own process also keeps it off this process's UI thread, which in inside mode
        /// shares Explorer's input queue while a modal choice is open.</summary>
        private static void PinFolder(string folder)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Log.ExePath())
                {
                    Arguments = QuickActionLibrary.PinArguments(folder),
                    UseShellExecute = false
                });
                Log.Info("quick action 'pin-folder': opened the group picker for '" + folder + "'.");
            }
            catch (Exception ex) { Log.Error("quick action 'pin-folder'", ex); }
        }

        private static void OpenVsCode(string folder)
        {
            // FindCodeExe walks from "code"'s PATH entry (code.cmd, a batch file) to the real Code.exe
            // beside its install root, specifically so this can launch a real executable directly
            // through ProcessStartInfo rather than through code.cmd -- which would mean cmd.exe
            // parsing the argument string, metacharacters and all, no matter how the .cmd is invoked.
            string codeExe = QuickActionLibrary.FindCodeExe();
            if (codeExe == null)
            {
                Log.Info("quick action 'open-vscode': 'code' is not on PATH, or its PATH entry is not the expected code.cmd shim.");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(codeExe)
                {
                    Arguments = QuickActionLibrary.QuoteArgument(folder),
                    WorkingDirectory = folder,
                    UseShellExecute = false
                });
                Log.Info("quick action 'open-vscode': opened '" + folder + "'.");
            }
            catch (Exception ex) { Log.Error("quick action 'open-vscode'", ex); }
        }
    }
}
