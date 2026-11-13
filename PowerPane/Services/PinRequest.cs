using System;
using System.Collections.Generic;

namespace PowerPane.Services
{
    /// <summary>
    /// Reads the paths that the "Add to PowerPane" verb (and the "Pin this folder" quick action)
    /// hand to a <c>--pin</c> launch. Pure string work, so it is unit-tested without Explorer.
    /// </summary>
    internal static class PinRequest
    {
        /// <summary>The most paths one launch accepts. A single multi-selection never needs more, and
        /// the cap keeps a runaway command line from queuing thousands of shortcuts.</summary>
        public const int MaxPaths = 500;

        private const string PinFlag = "--pin";

        /// <summary>Every argument after <c>--pin</c> is a path. The leading flag itself is skipped when
        /// present, so a caller can pass the whole argument array or only the part after the flag.
        /// Each value is normalized, an empty or relative value is dropped, a duplicate that differs only
        /// in case is dropped, and the list stops at <see cref="MaxPaths"/>. The order of the arguments
        /// is kept.</summary>
        internal static List<string> ParseArguments(string[] args)
        {
            var result = new List<string>();
            if (args == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int start = 0;
            if (args.Length > 0 && string.Equals(args[0], PinFlag, StringComparison.OrdinalIgnoreCase)) start = 1;

            for (int i = start; i < args.Length; i++)
            {
                if (result.Count >= MaxPaths) break;
                string path = NormalizePath(args[i]);
                if (path.Length == 0 || !IsFullPath(path)) continue;
                if (seen.Add(path)) result.Add(path);
            }
            return result;
        }

        /// <summary>Repairs the shapes the shell and the command-line parser produce. With the command
        /// <c>--pin "%V"</c> and a value of <c>C:\</c> the argument reads <c>"C:\"</c>, and the parser
        /// treats <c>\"</c> as an escaped quote, so the program sees <c>C:"</c>. This strips stray quotes
        /// and trailing spaces, then turns a bare drive such as <c>D:</c> into <c>D:\</c>. A null value
        /// gives an empty string.</summary>
        internal static string NormalizePath(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            string s = raw.Trim().Trim('"').Trim();
            if (s.Length == 2 && s[1] == ':' && IsDriveLetter(s[0])) s += "\\";
            return s;
        }

        // A path the picker can pin: a drive path with a backslash after the colon, or a UNC path. A
        // value such as "Demo\Apollo" or "C:Demo" depends on a current directory this process never
        // set deliberately, so it is refused.
        private static bool IsFullPath(string s)
        {
            if (s.Length >= 3 && IsDriveLetter(s[0]) && s[1] == ':' && (s[2] == '\\' || s[2] == '/')) return true;
            return s.Length >= 3 && s[0] == '\\' && s[1] == '\\' && s[2] != '\\';
        }

        private static bool IsDriveLetter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }
    }
}
