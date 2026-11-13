using System;
using System.Collections.Generic;
using System.IO;

namespace PowerPane.Services
{
    /// <summary>The last two folders each Explorer window was seen showing, so a copy action can tell
    /// a selection the user made from one Explorer made for them. Going up a level, or Back to a parent,
    /// selects the folder just left; a "Copy path" that preferred the selection would copy that
    /// subfolder instead of the folder on screen (#191).
    ///
    /// RecentTracker records here whenever it reads a window's folder, and a quick action records the
    /// folder it read when clicked. The tracker polls every two seconds, so at click time the newest
    /// entry may still be the folder the user just left; <see cref="IsUpSelection"/> is written to work
    /// whether or not the tracker has caught up. Keyed by window handle and capped, so a reused handle
    /// at worst carries two old paths that also have to pass IsUpSelection's parent test.</summary>
    internal static class WindowFolderHistory
    {
        private const int MaxWindows = 32;
        private static readonly object Gate = new object();
        private static readonly LruCache<IntPtr, string[]> Seen = new LruCache<IntPtr, string[]>(MaxWindows);

        public static void Record(IntPtr window, string folder)
        {
            if (window == IntPtr.Zero || string.IsNullOrEmpty(folder)) return;
            lock (Gate)
            {
                string[] last;
                if (Seen.TryGetValue(window, out last) && last.Length > 0 && SamePath(last[0], folder)) return;
                Seen.Set(window, last != null && last.Length > 0 ? new[] { folder, last[0] } : new[] { folder });
            }
        }

        public static IList<string> Recent(IntPtr window)
        {
            lock (Gate)
            {
                string[] last;
                return Seen.TryGetValue(window, out last) ? (IList<string>)(string[])last.Clone() : new string[0];
            }
        }

        /// <summary>True when <paramref name="selection"/> is what Explorer selects by itself after a move
        /// up: exactly one item, directly inside <paramref name="folder"/>, that is one of the window's
        /// recent folders or holds one of them. "Holds" covers going up two levels faster than the
        /// tracker polls. A deliberate click on that same folder looks identical and is also treated as
        /// no selection; any other item, two items, or a file is never touched.</summary>
        internal static bool IsUpSelection(string folder, IList<string> selection, IList<string> recentFolders)
        {
            if (string.IsNullOrEmpty(folder) || selection == null || selection.Count != 1 || recentFolders == null) return false;
            string item = Normalize(selection[0]);
            if (item == null || !SamePath(ParentOf(item), folder)) return false;
            foreach (var recent in recentFolders)
            {
                string r = Normalize(recent);
                if (r == null) continue;
                if (string.Equals(r, item, StringComparison.OrdinalIgnoreCase)) return true;
                if (r.StartsWith(item + @"\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static bool SamePath(string a, string b)
        {
            string x = Normalize(a), y = Normalize(b);
            return x != null && y != null && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The path with no trailing backslash, so "C:\" and "C:" compare equal, as do "C:\Demo\"
        /// and "C:\Demo". Null for an empty path.</summary>
        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string trimmed = path.TrimEnd('\\', '/');
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static string ParentOf(string normalized)
        {
            try { return Path.GetDirectoryName(normalized); }
            catch (ArgumentException) { return null; }
            catch (PathTooLongException) { return null; }
        }
    }
}
