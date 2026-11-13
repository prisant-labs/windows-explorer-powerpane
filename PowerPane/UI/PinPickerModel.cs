using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PowerPane.Models;

namespace PowerPane.UI
{
    /// <summary>Which group and tab the picker has selected. A GroupIndex of <see cref="NewIndex"/>
    /// means "New group...", which implies a new first tab. A TabIndex of <see cref="NewIndex"/> means
    /// "New tab..." inside the chosen group.</summary>
    internal sealed class PinChoice
    {
        public const int NewIndex = -1;

        public int GroupIndex { get; set; }
        public int TabIndex { get; set; }

        public bool IsNewGroup { get { return GroupIndex == NewIndex; } }
        public bool IsNewTab { get { return !IsNewGroup && TabIndex == NewIndex; } }
    }

    /// <summary>The last group and tab the user pinned into, as folder paths. Stored in a two-line text
    /// file, because UiState holds booleans only. Every failure reads as "nothing remembered".</summary>
    internal sealed class PinMemory
    {
        public string GroupFolder { get; set; }
        public string TabFolder { get; set; }

        /// <summary>%APPDATA%\PowerPane\pin-picker.txt.</summary>
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PowerPane", "pin-picker.txt");
            }
        }

        /// <summary>Reads the file. A missing, empty, short, or garbled file gives an empty memory, never
        /// null and never an exception.</summary>
        public static PinMemory Load(string file)
        {
            var memory = new PinMemory();
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return memory;
                var lines = File.ReadAllText(file, Encoding.UTF8).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length >= 1) memory.GroupFolder = Clean(lines[0]);
                if (lines.Length >= 2) memory.TabFolder = Clean(lines[1]);
                if (memory.GroupFolder == null) memory.TabFolder = null;   // a tab means nothing without its group
            }
            catch (Exception)
            {
                return new PinMemory();
            }
            return memory;
        }

        /// <summary>Writes the two lines, creating the folder if needed. Returns false when the write
        /// failed. Remembering is a convenience, so the caller ignores the result.</summary>
        public bool Save(string file)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || string.IsNullOrWhiteSpace(GroupFolder)) return false;
                var dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(file, GroupFolder + "\r\n" + (TabFolder ?? string.Empty) + "\r\n", new UTF8Encoding(false));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // A line that is not a plausible path (control characters, characters Windows forbids) is
        // treated as garbled.
        private static string Clean(string line)
        {
            var s = (line ?? string.Empty).Trim();
            if (s.Length == 0) return null;
            if (s.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            return s;
        }
    }

    /// <summary>The picker's pure decisions: which entry starts selected, how the items read in the
    /// header, and what a blank name becomes. Kept apart from the window so they are unit-tested.</summary>
    internal static class PinPickerModel
    {
        public const string DefaultGroupName = "Group";
        public const string DefaultTabName = "Tab";

        private const int NamesShown = 3;

        /// <summary>The remembered group and tab by folder path when both still exist. A remembered group
        /// that no longer exists, or a remembered tab that is gone from it, falls back to the first group
        /// or first tab. With no groups at all it is "New group...".</summary>
        internal static PinChoice DefaultChoice(IList<PinnedGroup> groups, PinMemory last)
        {
            if (groups == null || groups.Count == 0)
                return new PinChoice { GroupIndex = PinChoice.NewIndex, TabIndex = 0 };

            int g = 0;
            if (last != null && !string.IsNullOrEmpty(last.GroupFolder))
            {
                int found = IndexOfGroup(groups, last.GroupFolder);
                if (found >= 0) g = found;
            }

            var tabs = groups[g] == null ? null : groups[g].Tabs;
            if (tabs == null || tabs.Count == 0)
                return new PinChoice { GroupIndex = g, TabIndex = PinChoice.NewIndex };

            int t = 0;
            if (last != null && !string.IsNullOrEmpty(last.TabFolder) && g == IndexOfGroup(groups, last.GroupFolder))
            {
                for (int i = 0; i < tabs.Count; i++)
                {
                    if (SamePath(tabs[i].FolderPath, last.TabFolder)) { t = i; break; }
                }
            }
            return new PinChoice { GroupIndex = g, TabIndex = t };
        }

        /// <summary>One item reads as its full path. Several read as "N items" followed by up to three
        /// names, with "and M more" when there are further ones. No items reads "No items".</summary>
        internal static string ItemSummary(IList<string> paths)
        {
            if (paths == null || paths.Count == 0) return "No items";
            if (paths.Count == 1) return paths[0];

            var names = new List<string>();
            for (int i = 0; i < paths.Count && i < NamesShown; i++) names.Add(Leaf(paths[i]));
            string text = paths.Count + " items: " + string.Join(", ", names);
            if (paths.Count > NamesShown) text += " and " + (paths.Count - NamesShown) + " more";
            return text;
        }

        /// <summary>The typed name, trimmed, or the fallback when it is empty or only whitespace.</summary>
        internal static string ResolveName(string typed, string fallback)
        {
            return string.IsNullOrWhiteSpace(typed) ? fallback : typed.Trim();
        }

        private static string Leaf(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            var trimmed = path.TrimEnd('\\', '/');
            var name = trimmed.Length == 0 ? string.Empty : Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? path : name;   // a drive root has no leaf: show it whole
        }

        private static int IndexOfGroup(IList<PinnedGroup> groups, string folder)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null && SamePath(groups[i].FolderPath, folder)) return i;
            }
            return -1;
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
