using System;
using System.Collections.Generic;
using System.IO;

namespace PowerPane.Services
{
    /// <summary>The last few folders each Explorer window showed, so a copy action can tell a selection
    /// the user made from one Explorer made for them. Going up a level, or Back to a parent, selects the
    /// folder just left; a "Copy path" that preferred the selection would copy that subfolder instead of
    /// the folder on screen (#191).
    ///
    /// NavigationListener records every folder change as Explorer reports it, so a visit of any length
    /// is here; a two-second poll alone missed an in-and-back visit, which a Codex review found and
    /// Windows Sandbox reproduced. RecentTracker's poll and each quick action click still record as a
    /// second source. The history is per Explorer window, not per tab, because the listener cannot read
    /// a tab's handle from its apartment, so it keeps <see cref="Depth"/> folders: enough for a tab left
    /// in a subfolder while another tab of the same window moves a few times. Keyed by window handle
    /// and capped, so a reused handle at worst carries old paths that also have to pass
    /// IsUpSelection's parent test.</summary>
    internal static class WindowFolderHistory
    {
        private const int MaxWindows = 32;
        internal const int Depth = 6;
        private static readonly object Gate = new object();
        private static readonly LruCache<IntPtr, string[]> Seen = new LruCache<IntPtr, string[]>(MaxWindows);

        /// <summary>Records a folder as the window's newest. Called from COM worker threads, the work
        /// lane, and RecentTracker, so everything happens under the lock and nothing here blocks.</summary>
        public static void Record(IntPtr window, string folder)
        {
            if (window == IntPtr.Zero || string.IsNullOrEmpty(folder)) return;
            lock (Gate)
            {
                string[] last;
                if (!Seen.TryGetValue(window, out last)) last = new string[0];
                if (last.Length > 0 && SamePath(last[0], folder)) return;
                Seen.Set(window, Push(last, folder));
            }
        }

        /// <summary>The history with <paramref name="folder"/> in front, the oldest dropped past
        /// <see cref="Depth"/>. A folder seen again moves up rather than taking a second place.</summary>
        internal static string[] Push(string[] last, string folder)
        {
            var next = new List<string>(Depth) { folder };
            foreach (var f in last)
            {
                if (next.Count == Depth) break;
                if (!SamePath(f, folder)) next.Add(f);
            }
            return next.ToArray();
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
        /// up: exactly one item, directly inside <paramref name="folder"/>, when the window's newest
        /// recorded navigation is to <paramref name="folder"/> and the one before it was that item or a
        /// folder inside it. "Inside it" covers going up two levels at once. A deliberate click on that
        /// same folder right after the move looks identical and is also treated as no selection; any
        /// other item, two items, or a file is never touched.
        ///
        /// Only the latest move counts, because the history is kept per window, not per tab. Matching
        /// any recent folder dropped a deliberate selection in tab B of a folder that tab A had visited
        /// (Codex review, 2026-11-13). A tab switch records nothing, so after one the newest entry is the
        /// other tab's folder and this is false. The cost: going up in tab A after navigating in tab B
        /// no longer catches Explorer's selection, and Copy path copies the selected folder, as it did
        /// before this guard existed.</summary>
        internal static bool IsUpSelection(string folder, IList<string> selection, IList<string> recentFolders)
        {
            if (string.IsNullOrEmpty(folder) || selection == null || selection.Count != 1) return false;
            if (recentFolders == null || recentFolders.Count < 2 || !SamePath(recentFolders[0], folder)) return false;
            string item = Normalize(selection[0]);
            if (item == null || !SamePath(ParentOf(item), folder)) return false;
            string left = Normalize(recentFolders[1]);
            return left != null &&
                   (string.Equals(left, item, StringComparison.OrdinalIgnoreCase) ||
                    left.StartsWith(item + @"\", StringComparison.OrdinalIgnoreCase));
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
