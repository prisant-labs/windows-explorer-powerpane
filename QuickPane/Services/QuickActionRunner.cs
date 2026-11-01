using System;
using System.Diagnostics;
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
                case "open-terminal": OpenTerminal(folder); break;
                case "open-vscode": OpenVsCode(folder); break;
                default: Log.Info("quick action: unknown id '" + actionId + "'."); break;
            }
        }

        private static void NewTextFile(string folder)
        {
            try
            {
                var path = QuickActionLibrary.UniqueFileName(folder, "New Text Document", ".txt");
                File.WriteAllText(path, string.Empty);
                Log.Info("quick action 'new-text-file': created '" + path + "'.");
            }
            catch (Exception ex) { Log.Error("quick action 'new-text-file'", ex); }
        }

        private static void NewMarkdownFile(string folder)
        {
            try
            {
                string baseName = DateTime.Now.ToString("yyyy-MM-dd") + "-untitled";
                var path = QuickActionLibrary.UniqueFileName(folder, baseName, ".md");
                File.WriteAllText(path, "# Untitled" + Environment.NewLine);
                Log.Info("quick action 'new-markdown-file': created '" + path + "'.");
            }
            catch (Exception ex) { Log.Error("quick action 'new-markdown-file'", ex); }
        }

        private static void NewDatedFolder(string folder)
        {
            try
            {
                var path = QuickActionLibrary.UniqueFolderName(folder, DateTime.Now.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(path);
                Log.Info("quick action 'new-dated-folder': created '" + path + "'.");
            }
            catch (Exception ex) { Log.Error("quick action 'new-dated-folder'", ex); }
        }

        /// <summary>wt.exe when it is on PATH, else powershell.exe, started directly through
        /// ProcessStartInfo with its own argument list -- never "cmd /c" -- so a folder name holding
        /// an ampersand or a caret cannot start a second command (quick actions, first version). Never
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
                        Arguments = QuickActionLibrary.TerminalArguments(folder, haveWt: true),
                        WorkingDirectory = folder,
                        UseShellExecute = false
                    });
                    Log.Info("quick action 'open-terminal': started Windows Terminal in '" + folder + "'.");
                    return;
                }
                Process.Start(new ProcessStartInfo("powershell.exe")
                {
                    Arguments = QuickActionLibrary.TerminalArguments(folder, haveWt: false),
                    WorkingDirectory = folder,
                    UseShellExecute = false
                });
                Log.Info("quick action 'open-terminal': wt.exe not on PATH; started PowerShell in '" + folder + "'.");
            }
            catch (Exception ex) { Log.Error("quick action 'open-terminal'", ex); }
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
