using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace QuickPane.Services
{
    /// <summary>What a custom command (#190) turns into: the program to start and its argument string.
    /// <see cref="Error"/> is set instead when the command must not or cannot run.</summary>
    internal sealed class CustomCommandLine
    {
        public string Program;
        public string Arguments;
        public string Error;
    }

    /// <summary>Parses and expands a custom command's template, such as <c>code {path}</c> (#190).
    /// A template is the user's own text, but the values put into it are folder and file names, which
    /// can hold anything Windows allows: spaces, quotes, ampersands, percent signs. The one rule that
    /// keeps a name from becoming part of a command: the template is split into a program and arguments
    /// first, and values go in afterwards, each whole argument quoted by
    /// <see cref="QuickActionLibrary.QuoteArgument"/>. No value ever passes through the splitter, so
    /// quotes the user typed around a placeholder are harmless, and nothing runs through cmd /c.
    ///
    /// That protects the command line, not the program. Some programs read an argument as code, so a
    /// placeholder is refused for those (see <see cref="ShellRefusal"/>).
    ///
    /// Every method here is pure, except <see cref="ResolveProgram"/> and <see cref="Build"/>, which
    /// read the disk through a lookup the caller passes in.</summary>
    internal static class CustomCommandTemplate
    {
        public const string IdPrefix = "custom:";

        /// <summary>CreateProcess fails past 32,767 characters, and a large selection can get there.
        /// Refusing is honest: dropping the end of a selection would act on the wrong set of files.</summary>
        internal const int MaxCommandLineLength = 32000;

        public static bool IsCustomId(string id)
        {
            return id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal);
        }

        // ---- splitting ----------------------------------------------------------------------

        /// <summary>Splits on whitespace outside double quotes, and drops the quotes. A backslash is an
        /// ordinary character, because a template mostly holds Windows paths.</summary>
        internal static bool TrySplit(string template, out List<string> tokens, out string error)
        {
            tokens = new List<string>();
            error = null;
            var current = new StringBuilder();
            bool inQuotes = false, inToken = false;
            foreach (char c in template ?? string.Empty)
            {
                if (c == '"') { inQuotes = !inQuotes; inToken = true; continue; }
                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (inToken) { tokens.Add(current.ToString()); current.Clear(); inToken = false; }
                    continue;
                }
                current.Append(c);
                inToken = true;
            }
            if (inQuotes) { error = "A double quote has no matching closing quote."; return false; }
            if (inToken) tokens.Add(current.ToString());
            if (tokens.Count == 0 || tokens[0].Length == 0) { error = "Enter a program to run."; return false; }
            return true;
        }

        // ---- placeholders -------------------------------------------------------------------

        private static bool IsKnownPlaceholder(string name)
        {
            return name == "path" || name == "paths" || name == "dir" || name == "name";
        }

        /// <summary>Walks one token. Calls <paramref name="onPlaceholder"/> for each {name} and appends
        /// what it returns. {{ and }} are literal braces. Returns false, with a message, for a brace
        /// that opens nothing or a name that is not a placeholder, because a typo that ran with the
        /// literal text would be much harder to notice.</summary>
        private static bool Scan(string token, Func<string, string> onPlaceholder, StringBuilder output, out string error)
        {
            error = null;
            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                if (c == '}')
                {
                    if (i + 1 < token.Length && token[i + 1] == '}') i++;
                    output.Append('}');
                    continue;
                }
                if (c != '{') { output.Append(c); continue; }
                if (i + 1 < token.Length && token[i + 1] == '{') { output.Append('{'); i++; continue; }

                int close = token.IndexOf('}', i + 1);
                if (close < 0) { error = "A { has no matching }. Write {{ for a literal brace."; return false; }
                string name = token.Substring(i + 1, close - i - 1);
                if (!IsKnownPlaceholder(name))
                {
                    error = "{" + name + "} is not a placeholder. Use {path}, {paths}, {dir}, or {name}.";
                    return false;
                }
                output.Append(onPlaceholder(name));
                i = close;
            }
            return true;
        }

        private static HashSet<string> PlaceholdersIn(string token, out string error)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            Scan(token, n => { found.Add(n); return string.Empty; }, new StringBuilder(), out error);
            return found;
        }

        /// <summary>True when the template names anything that depends on what is being acted on, as
        /// opposed to where. A caller uses this to skip reading the Explorer selection, which is a
        /// cross-process call, for a template that would not use it.</summary>
        public static bool UsesTargets(string template)
        {
            List<string> tokens;
            string error;
            if (!TrySplit(template, out tokens, out error)) return false;
            foreach (var token in tokens.Skip(1))
            {
                var names = PlaceholdersIn(token, out error);
                if (names.Contains("path") || names.Contains("paths") || names.Contains("name")) return true;
            }
            return false;
        }

        // ---- validation ---------------------------------------------------------------------

        /// <summary>Null when the template can run, or else the reason it cannot, in words for the
        /// Settings page. Reads no disk, so the page can call it on every keystroke.</summary>
        public static string Validate(string template)
        {
            List<string> tokens;
            string error;
            if (!TrySplit(template, out tokens, out error)) return error;

            var inProgram = PlaceholdersIn(tokens[0], out error);
            if (error != null) return error;
            if (inProgram.Count > 0)
                return "The program must be fixed text. A placeholder there would run whatever a folder is named.";

            bool anyPlaceholder = false;
            foreach (var token in tokens.Skip(1))
            {
                var names = PlaceholdersIn(token, out error);
                if (error != null) return error;
                if (names.Count > 0) anyPlaceholder = true;
                if (names.Contains("paths") && token != "{paths}")
                    return "{paths} must stand alone, because it becomes several arguments.";
            }
            return anyPlaceholder ? ShellRefusal(tokens) : null;
        }

        /// <summary>The reason not to hand a file name to this program, or null. Each of these reads an
        /// argument as code or splits it on its own separators, so correct quoting does not help: a
        /// folder named "a&amp;calc" would run calc through cmd or a batch file. A plain program with no
        /// placeholder is fine, since every word of its command line is the user's own, and it still
        /// starts in the folder through its working directory.</summary>
        internal static string ShellRefusal(IList<string> tokens)
        {
            string program = tokens[0];
            string ext = SafeExtension(program);
            string stem = SafeStem(program);

            if (ext == ".bat" || ext == ".cmd")
                return "A batch file would read a folder name as commands. Point at the program it starts, or remove the placeholder.";
            if (stem == "cmd" || stem == "command")
                return "Command Prompt would read a folder name as commands. Start the program directly, or remove the placeholder.";
            if (stem == "wsl")
                return "WSL hands its arguments to a shell, which would read a folder name as code. Commands start in the folder, so remove the placeholder.";
            if (stem == "wt")
                return "Windows Terminal reads a ; in a folder name as its own separator. Commands start in the folder, so use: wt -d .";

            var args = tokens.Skip(1).ToList();
            if (stem == "powershell" || stem == "pwsh")
            {
                // -File takes the script's arguments literally. Everything else re-parses them as script.
                bool hasFile = args.Any(a => a.Equals("-File", StringComparison.OrdinalIgnoreCase) || a.Equals("-f", StringComparison.OrdinalIgnoreCase));
                if (!hasFile) return "PowerShell would read a folder name as script. Use -File with a script, or remove the placeholder.";
            }
            if (stem == "bash" || stem == "sh" || stem == "zsh")
            {
                bool hasCommand = args.Any(a => a.StartsWith("-", StringComparison.Ordinal) && !a.StartsWith("--", StringComparison.Ordinal) && a.IndexOf('c') >= 0);
                if (hasCommand) return "A shell given -c would read a folder name as code. Pass a script file instead, or remove the placeholder.";
            }
            return null;
        }

        private static string SafeExtension(string program)
        {
            try { return (Path.GetExtension(program) ?? string.Empty).ToLowerInvariant(); }
            catch (ArgumentException) { return string.Empty; }
        }

        private static string SafeStem(string program)
        {
            try { return (Path.GetFileNameWithoutExtension(program) ?? string.Empty).ToLowerInvariant(); }
            catch (ArgumentException) { return string.Empty; }
        }

        // ---- expansion ----------------------------------------------------------------------

        /// <summary>The argument string for a valid template. <paramref name="targets"/> is what the
        /// command acts on, never empty, and <paramref name="dir"/> is the folder on screen.</summary>
        internal static string BuildArguments(IList<string> tokens, IList<string> targets, string dir)
        {
            var args = new List<string>();
            foreach (var token in tokens.Skip(1))
            {
                if (token == "{paths}")
                {
                    args.AddRange(targets.Select(QuickActionLibrary.QuoteArgument));
                    continue;
                }
                var value = new StringBuilder();
                string error;
                Scan(token, n =>
                {
                    switch (n)
                    {
                        case "path": return targets[0];
                        case "dir": return dir;
                        case "name": return QuickActionLibrary.Leaf(targets[0]);
                        default: return string.Empty;
                    }
                }, value, out error);
                args.Add(QuickActionLibrary.QuoteArgument(value.ToString()));
            }
            return string.Join(" ", args);
        }

        /// <summary>What the template would run for an example, for the Settings page's live preview.
        /// Null while the template is not valid.</summary>
        public static string Preview(string template, IList<string> targets, string dir)
        {
            if (Validate(template) != null) return null;
            List<string> tokens;
            string error;
            if (!TrySplit(template, out tokens, out error)) return null;
            string arguments = BuildArguments(tokens, targets, dir);
            string program = QuickActionLibrary.QuoteArgument(tokens[0]);
            return arguments.Length > 0 ? program + " " + arguments : program;
        }

        // ---- the program --------------------------------------------------------------------

        /// <summary>The full path of the program to start, or null. An absolute path must exist, and a
        /// bare name comes from PATH. Resolving it here matters: CreateProcess given a bare name also
        /// looks in the current directory, and a relative path would be read against wherever the
        /// command runs, so either one could start a program planted in the folder being acted on.
        /// Environment variables expand in the program only, never in a value.</summary>
        internal static string ResolveProgram(string programToken, Func<string, string> findOnPath)
        {
            string expanded = Environment.ExpandEnvironmentVariables(programToken ?? string.Empty);
            if (expanded.Length == 0) return null;
            try
            {
                if (Path.IsPathRooted(expanded)) return File.Exists(expanded) ? expanded : null;
            }
            catch (ArgumentException) { return null; }
            if (expanded.IndexOfAny(new[] { '\\', '/' }) >= 0) return null;
            return findOnPath(expanded);
        }

        /// <summary>A template resolved as far as it can be for this machine: the program, the
        /// arguments, or the reason it cannot run. A bare name such as "code" can turn out to be a
        /// batch file only here, once PATH has answered, so that refusal happens at this point too.
        /// VS Code's own shim is the one case with a known real program behind it.</summary>
        public static CustomCommandLine Build(string template, IList<string> targets, string dir, Func<string, string> findOnPath)
        {
            var result = new CustomCommandLine();
            result.Error = Validate(template);
            if (result.Error != null) return result;

            List<string> tokens;
            string error;
            if (!TrySplit(template, out tokens, out error)) { result.Error = error; return result; }

            string program = ResolveProgram(tokens[0], findOnPath);
            if (program == null)
            {
                result.Error = "Program not found: " + tokens[0];
                return result;
            }
            string codeExe = QuickActionLibrary.ResolveCodeExe(program);
            if (codeExe != null) program = codeExe;

            string arguments = BuildArguments(tokens, targets, dir);
            bool hasPlaceholder = tokens.Skip(1).Any(t =>
            {
                string e;
                return PlaceholdersIn(t, out e).Count > 0;
            });
            string ext = SafeExtension(program);
            if (hasPlaceholder && (ext == ".bat" || ext == ".cmd"))
            {
                result.Error = tokens[0] + " is a batch file, which would read a folder name as commands.";
                return result;
            }
            if (program.Length + arguments.Length + 3 > MaxCommandLineLength)
            {
                result.Error = "Too many items for one command.";
                return result;
            }

            result.Program = program;
            result.Arguments = arguments;
            return result;
        }

        /// <summary>A note for the Settings page about a template that is valid but will not run on this
        /// machine, or null. Reads the disk through PATH, so the caller runs it off the UI thread.</summary>
        public static string ProgramWarning(string template, Func<string, string> findOnPath)
        {
            var line = Build(template, new[] { @"C:\Demo\Projects\Apollo" }, @"C:\Demo\Projects\Apollo", findOnPath);
            return line.Error;
        }
    }
}
