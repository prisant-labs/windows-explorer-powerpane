using System;
using System.Collections.Generic;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.UI
{
    /// <summary>What an "Add" entry adds.</summary>
    internal enum AddKind { Tab, GroupBelow, Folder, File }

    /// <summary>Which "Add" entries a menu shows.</summary>
    [Flags]
    internal enum AddKinds
    {
        None = 0,
        Tab = 1,
        GroupBelow = 2,
        Folder = 4,
        File = 8,
        FolderAndFile = Folder | File,
        All = Tab | GroupBelow | Folder | File
    }

    /// <summary>One "Add" entry's text. <see cref="Text"/> is the plain words, for an automation name.
    /// <see cref="Header"/> is the same words with every underscore doubled, for a MenuItem header,
    /// because WPF reads a single underscore in a header as an access key and drops it.</summary>
    internal sealed class AddHeader
    {
        public AddKind Kind { get; private set; }
        public string Text { get; private set; }
        public string Header { get; private set; }

        public AddHeader(AddKind kind, string text)
        {
            Kind = kind;
            Text = text;
            Header = AddMenuLabels.EscapeAccessKeys(text);
        }
    }

    /// <summary>
    /// The words on the "+" menu, the right-click menus' Add entries, and the name prompts (#243). Pure
    /// strings, so a test needs no window. Which name stands for a group is chosen in
    /// <see cref="GroupLabel"/> alone, because the maintainer may change it later.
    /// </summary>
    internal static class AddMenuLabels
    {
        /// <summary>Longest name that appears in a label. A longer name is shortened in the middle.</summary>
        internal const int MaxName = 32;

        /// <summary>Shorten <paramref name="text"/> to at most <paramref name="max"/> characters by
        /// replacing the middle with "...". Null becomes an empty string.</summary>
        internal static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (max < 4 || text.Length <= max) return text.Length <= max ? text : text.Substring(0, max);
            int keep = max - 3;
            int head = (keep + 1) / 2;
            int tail = keep - head;
            return text.Substring(0, head) + "..." + text.Substring(text.Length - tail);
        }

        /// <summary>Double every underscore, so a MenuItem header shows it instead of treating it as an
        /// access key.</summary>
        internal static string EscapeAccessKeys(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : text.Replace("_", "__");
        }

        /// <summary>The name that stands for a group: its own name, which is its folder's name without
        /// the order prefix. A group with no name of its own falls back to the joined tab names.</summary>
        internal static string GroupLabel(PinnedGroup group)
        {
            if (group == null) return string.Empty;
            string label = !string.IsNullOrWhiteSpace(group.Name) ? group.Name : GroupStore.DisplayName(group);
            return Shorten(label, MaxName);
        }

        /// <summary>The words after "Add folder to" and "Add file to": the group's label for a group with
        /// one tab, and the tab's own name for a group with several, so the target is the tab that
        /// receives the pin.</summary>
        internal static string TabTarget(PinnedGroup group, PinnedTab tab)
        {
            if (group != null && group.Tabs.Count > 1 && tab != null && !string.IsNullOrWhiteSpace(tab.Name))
                return "tab \"" + Shorten(tab.Name, MaxName) + "\"";
            return "\"" + GroupLabel(group) + "\"";
        }

        /// <summary>The entries for the requested kinds, in menu order: Add tab, Add group below, Add
        /// folder, Add file. <paramref name="target"/> is the tab a folder or a file goes into, and
        /// defaults to the group's active tab.</summary>
        internal static List<AddHeader> Headers(PinnedGroup group, PinnedTab target, AddKinds kinds)
        {
            var list = new List<AddHeader>();
            if (group == null) return list;
            if (target == null) target = group.Active;

            string label = GroupLabel(group);
            if ((kinds & AddKinds.Tab) != 0)
                list.Add(new AddHeader(AddKind.Tab, "Add tab to \"" + label + "\"..."));
            if ((kinds & AddKinds.GroupBelow) != 0)
                list.Add(new AddHeader(AddKind.GroupBelow, "Add group below \"" + label + "\"..."));
            if ((kinds & AddKinds.Folder) != 0)
                list.Add(new AddHeader(AddKind.Folder, "Add folder to " + TabTarget(group, target) + "..."));
            if ((kinds & AddKinds.File) != 0)
                list.Add(new AddHeader(AddKind.File, "Add file to " + TabTarget(group, target) + "..."));
            return list;
        }

        /// <summary>The "+" button's tooltip and automation name. It names the group, so each group's
        /// button can be told apart.</summary>
        internal static string PlusName(PinnedGroup group)
        {
            return "Add to \"" + GroupLabel(group) + "\"";
        }

        /// <summary>Heading of the prompt that names a new tab.</summary>
        internal static string NewTabTitle(PinnedGroup group)
        {
            return "New tab in \"" + GroupLabel(group) + "\"";
        }

        /// <summary>Heading of the prompt that names a new group placed below another.</summary>
        internal static string NewGroupBelowTitle(PinnedGroup group)
        {
            return "New group below \"" + GroupLabel(group) + "\"";
        }
    }
}
