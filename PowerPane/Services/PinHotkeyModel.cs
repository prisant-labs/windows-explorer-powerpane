using System;
using System.Collections.Generic;
using System.Linq;
using PowerPane.Models;
using PowerPane.UI;

namespace PowerPane.Services
{
    /// <summary>
    /// The rules for pin shortcuts (#299), with no UI and no disk. A shortcut is a modifier prefix
    /// plus a number from 1 to 9, and each number opens one pinned folder. The key belongs to the
    /// folder the pin points at, so a reorder, a rename or a drag to another tab or group, which all
    /// rename or move the shortcut file, never loses it. Only Edit path changes the folder, and
    /// <see cref="MigrateTarget"/> carries the key along. Folders are compared with
    /// <see cref="PinPickerModel.PathKey"/>, the comparison the duplicate-pin check uses.
    /// </summary>
    internal static class PinHotkeyModel
    {
        public const int FirstSlot = 1;
        public const int LastSlot = 9;

        /// <summary>The full shortcut for a number, such as "ctrl+alt+D3", or null for a number out
        /// of range. The digit is spelled D3 because <see cref="HotkeyService.TryParse"/> reads a bare
        /// "3" as the numeric value of a Key, which is not the 3 key.</summary>
        public static string SlotSpec(string modifiers, int slot)
        {
            if (slot < FirstSlot || slot > LastSlot || string.IsNullOrWhiteSpace(modifiers)) return null;
            return modifiers.Trim() + "+D" + slot;
        }

        /// <summary>The label a person reads for a number, such as "Ctrl+Alt+3".</summary>
        public static string SlotLabel(string modifiers, int slot)
        {
            var parts = (modifiers ?? string.Empty).Split('+')
                .Select(p => p.Trim()).Where(p => p.Length > 0)
                .Select(p => p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant());
            return string.Join("+", parts.Concat(new[] { slot.ToString() }));
        }

        /// <summary>The prefix in its stored form, such as "ctrl+alt", or null when it is not one or
        /// more modifiers alone. A key in the prefix, or a bare prefix, is refused, because the number
        /// is the key.</summary>
        public static string ValidateModifiers(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var seen = new List<string>();
            foreach (var raw in text.Split('+'))
            {
                var part = raw.Trim().ToLowerInvariant();
                if (part.Length == 0) continue;
                if (part == "control") part = "ctrl";
                if (part == "windows") part = "win";
                if (part != "ctrl" && part != "alt" && part != "shift" && part != "win") return null;
                if (!seen.Contains(part)) seen.Add(part);
            }
            if (seen.Count == 0) return null;
            var ordered = new[] { "ctrl", "alt", "shift", "win" }.Where(seen.Contains);
            var prefix = string.Join("+", ordered);
            uint mods, vk;
            return HotkeyService.TryParse(SlotSpec(prefix, 1), out mods, out vk) ? prefix : null;
        }

        private static bool Same(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return PinPickerModel.PathKey(a) == PinPickerModel.PathKey(b);
        }

        private static List<PinHotkeyEntry> Copy(IEnumerable<PinHotkeyEntry> entries)
        {
            return (entries ?? Enumerable.Empty<PinHotkeyEntry>())
                .Where(e => e != null && e.Slot >= FirstSlot && e.Slot <= LastSlot && !string.IsNullOrWhiteSpace(e.Target))
                .Select(e => new PinHotkeyEntry { Slot = e.Slot, Target = e.Target }).ToList();
        }

        /// <summary>The list as it is stored: sorted by number, or null when empty, so an empty list
        /// stays out of the settings file.</summary>
        private static List<PinHotkeyEntry> Stored(List<PinHotkeyEntry> list)
        {
            if (list.Count == 0) return null;
            return list.OrderBy(e => e.Slot).ToList();
        }

        /// <summary>Gives <paramref name="slot"/> to <paramref name="target"/>. Any folder that held
        /// the number loses it, and the folder's old number, if it had one, is freed, so a number
        /// means one folder and a folder has one number.</summary>
        public static List<PinHotkeyEntry> Assign(IEnumerable<PinHotkeyEntry> entries, int slot, string target)
        {
            var list = Copy(entries);
            if (slot < FirstSlot || slot > LastSlot || string.IsNullOrWhiteSpace(target)) return Stored(list);
            list.RemoveAll(e => e.Slot == slot || Same(e.Target, target));
            list.Add(new PinHotkeyEntry { Slot = slot, Target = target });
            return Stored(list);
        }

        /// <summary>Frees one number. An unknown number changes nothing.</summary>
        public static List<PinHotkeyEntry> Unassign(IEnumerable<PinHotkeyEntry> entries, int slot)
        {
            var list = Copy(entries);
            list.RemoveAll(e => e.Slot == slot);
            return Stored(list);
        }

        /// <summary>The number a folder holds, or 0 for none.</summary>
        public static int SlotOf(IEnumerable<PinHotkeyEntry> entries, string target)
        {
            var hit = Copy(entries).FirstOrDefault(e => Same(e.Target, target));
            return hit == null ? 0 : hit.Slot;
        }

        /// <summary>The folder a number holds, whether or not it is still pinned, or null.</summary>
        public static string TargetOf(IEnumerable<PinHotkeyEntry> entries, int slot)
        {
            var hit = Copy(entries).FirstOrDefault(e => e.Slot == slot);
            return hit == null ? null : hit.Target;
        }

        /// <summary>The folder a pressed number opens: its stored folder, but only while one of
        /// <paramref name="pinTargets"/> still points there. Null otherwise, so a key whose pin is
        /// gone does nothing.</summary>
        public static string Resolve(IEnumerable<PinHotkeyEntry> entries, int slot, IEnumerable<string> pinTargets)
        {
            var target = TargetOf(entries, slot);
            if (target == null) return null;
            foreach (var t in pinTargets ?? Enumerable.Empty<string>())
                if (Same(t, target)) return target;
            return null;
        }

        /// <summary>The entries whose folder no pin points at any more. Settings lists them as no
        /// longer pinned instead of dropping them, because a pin can vanish for a moment, such as
        /// while a synced folder catches up.</summary>
        public static List<PinHotkeyEntry> Orphans(IEnumerable<PinHotkeyEntry> entries, IEnumerable<string> pinTargets)
        {
            var targets = (pinTargets ?? Enumerable.Empty<string>()).ToList();
            return Copy(entries).Where(e => !targets.Any(t => Same(t, e.Target))).OrderBy(e => e.Slot).ToList();
        }

        /// <summary>Moves a key from a pin's old folder to its new one, after Edit path. Nothing else
        /// changes, and an old folder with no key changes nothing.</summary>
        public static List<PinHotkeyEntry> MigrateTarget(IEnumerable<PinHotkeyEntry> entries, string oldTarget, string newTarget)
        {
            var list = Copy(entries);
            int slot = SlotOf(list, oldTarget);
            if (slot == 0 || string.IsNullOrWhiteSpace(newTarget)) return Stored(list);
            return Assign(list, slot, newTarget);
        }

        /// <summary>The folders the given groups pin, folder pins only. File pins get no key.</summary>
        public static List<string> FolderPinTargets(IEnumerable<PinnedGroup> groups)
        {
            var result = new List<string>();
            foreach (var g in groups ?? Enumerable.Empty<PinnedGroup>())
                foreach (var t in g.Tabs ?? new List<PinnedTab>())
                    foreach (var p in t.Items ?? new List<PinnedFolder>())
                        if (p != null && !p.IsFile && !string.IsNullOrWhiteSpace(p.TargetPath)) result.Add(p.TargetPath);
            return result;
        }
    }
}
