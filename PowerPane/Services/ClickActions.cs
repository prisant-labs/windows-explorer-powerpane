using System;
using System.Collections.Generic;
using System.Windows.Input;
using PowerPane.Models;

namespace PowerPane.Services
{
    /// <summary>A mouse gesture on a pane row that the user can give an action to (#269).</summary>
    internal enum ClickGesture { Left, Middle, Ctrl, Shift, CtrlShift }

    /// <summary>What a click on a pane row ends up doing, once the stored choice, the kind of row,
    /// and the pane's host have all been taken into account.</summary>
    internal enum ClickOutcome { Nothing, OpenHere, OpenNewWindow, OpenParent, OpenFile, OpenContainingFolder, CopyPath, QuickAction }

    /// <summary>Whether a button release on a row completes a click that began on that row (#269). A
    /// click can now run a quick action or a custom command, so a release whose press began elsewhere
    /// (in blank pane space, or on another row, then dragged over this one) must do nothing. The row
    /// reports presses and releases, and cancels on entering or leaving, because WPF raises MouseEnter
    /// for a pointer that arrives with a button already down. No WPF types beyond the button enum, so
    /// the rule is unit-testable.</summary>
    internal sealed class RowPress
    {
        private bool _left;
        private bool _middle;

        public void Down(MouseButton button)
        {
            if (button == MouseButton.Left) _left = true;
            else if (button == MouseButton.Middle) _middle = true;
        }

        /// <summary>Forgets any press, for the pointer entering or leaving the row.</summary>
        public void Cancel() { _left = false; _middle = false; }

        /// <summary>True when this release completes a press that began on the row. Either way the
        /// press is used up, so a second release cannot act on the same press.</summary>
        public bool Up(MouseButton button)
        {
            bool pressed = false;
            if (button == MouseButton.Left) { pressed = _left; _left = false; }
            else if (button == MouseButton.Middle) { pressed = _middle; _middle = false; }
            return pressed;
        }
    }

    /// <summary>
    /// The pure decisions behind click actions (#269): which gesture a button and modifier keys make,
    /// which action the user chose for it, and what that action means on a given row. Nothing here
    /// touches a window, the clipboard, or a process, so every decision is unit-testable; the row in
    /// <c>FolderTreeNode</c> carries out the outcome.
    ///
    /// The defaults keep the click behaviour from before this setting existed. Left click opens, the
    /// three modifier gestures are "Same as left click" (a left click ignored modifiers, so Ctrl and
    /// Shift clicks opened too), and middle click does nothing, as it did. Choices are stored as
    /// string ids rather than enum numbers, so a later action can be added without shifting anyone's
    /// stored choice.
    /// </summary>
    internal static class ClickActions
    {
        public const string Open = "open";
        public const string NewWindow = "new-window";
        public const string OpenParent = "open-parent";
        public const string OpenContainingFolder = "open-folder";
        public const string CopyPath = "copy-path";
        public const string None = "none";
        public const string SameAsLeft = "same-as-left";

        /// <summary>Prefix of an action that runs a quick action, followed by its id, e.g.
        /// "quick:open-terminal" or a custom command's "quick:custom-...".</summary>
        public const string QuickPrefix = "quick:";

        /// <summary>The Copy path quick action, which a folder click saved as the old built-in
        /// <see cref="CopyPath"/> becomes when settings load (#282).</summary>
        public const string QuickCopyPath = QuickPrefix + "copy-path";

        /// <summary>Every gesture, in the order Settings lists them.</summary>
        public static readonly ClickGesture[] Gestures =
            { ClickGesture.Left, ClickGesture.Middle, ClickGesture.Ctrl, ClickGesture.Shift, ClickGesture.CtrlShift };

        public static string GestureId(ClickGesture g)
        {
            switch (g)
            {
                case ClickGesture.Middle: return "middle";
                case ClickGesture.Ctrl: return "ctrl";
                case ClickGesture.Shift: return "shift";
                case ClickGesture.CtrlShift: return "ctrl-shift";
                default: return "left";
            }
        }

        public static bool TryParseGesture(string id, out ClickGesture gesture)
        {
            foreach (var g in Gestures)
            {
                if (string.Equals(GestureId(g), id, StringComparison.Ordinal)) { gesture = g; return true; }
            }
            gesture = ClickGesture.Left;
            return false;
        }

        public static string GestureLabel(ClickGesture g)
        {
            switch (g)
            {
                case ClickGesture.Middle: return "Middle click";
                case ClickGesture.Ctrl: return "Ctrl + click";
                case ClickGesture.Shift: return "Shift + click";
                case ClickGesture.CtrlShift: return "Ctrl + Shift + click";
                default: return "Left click";
            }
        }

        /// <summary>The gesture a released button makes with the modifiers held, or null for a button
        /// that has no action (right, which opens the menu, and the side buttons). Alt and the Windows
        /// key are ignored, exactly as a left click ignored every modifier before this setting, so
        /// Alt + click still opens; Alt is left out on purpose (#269), because in inside mode the pane
        /// shares Explorer's input queue and Alt can reach Explorer's access keys. A modified middle
        /// click is still a middle click.</summary>
        public static ClickGesture? GestureFor(MouseButton button, ModifierKeys modifiers)
        {
            if (button == MouseButton.Middle) return ClickGesture.Middle;
            if (button != MouseButton.Left) return null;
            bool ctrl = (modifiers & ModifierKeys.Control) != 0;
            bool shift = (modifiers & ModifierKeys.Shift) != 0;
            if (ctrl && shift) return ClickGesture.CtrlShift;
            if (ctrl) return ClickGesture.Ctrl;
            if (shift) return ClickGesture.Shift;
            return ClickGesture.Left;
        }

        /// <summary>The action a gesture has when nothing is stored for it.</summary>
        public static string DefaultFor(ClickGesture g)
        {
            if (g == ClickGesture.Left) return Open;
            if (g == ClickGesture.Middle) return None;
            return SameAsLeft;
        }

        /// <summary>Whether <paramref name="actionId"/> may be stored for this gesture and kind of row.
        /// A quick action is accepted on a folder row when its id is a real action, built-in or
        /// custom; whether it is switched on is decided at click time, so turning an action off and on
        /// again keeps the click bound to it.</summary>
        public static bool IsValid(AppSettings settings, ClickGesture g, bool forFile, string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return false;
            if (actionId == SameAsLeft) return g != ClickGesture.Left;
            if (actionId == None) return g != ClickGesture.Left;   // a left click always does something
            if (actionId == Open) return true;
            // A folder click copies its path through the Copy path quick action, which Settings
            // already listed beside a built-in Copy path that did the same thing (#282).
            if (forFile) return actionId == OpenContainingFolder || actionId == CopyPath;
            if (actionId == NewWindow || actionId == OpenParent) return true;
            if (actionId.StartsWith(QuickPrefix, StringComparison.Ordinal))
                return QuickActionLibrary.Resolve(settings, actionId.Substring(QuickPrefix.Length)) != null;
            return false;
        }

        /// <summary>The stored choice for a gesture, or its default when nothing valid is stored.</summary>
        public static string Stored(AppSettings settings, ClickGesture g, bool forFile)
        {
            var entry = Find(settings, g);
            var id = entry == null ? null : (forFile ? entry.File : entry.Folder);
            return IsValid(settings, g, forFile, id) ? id : DefaultFor(g);
        }

        /// <summary>The action a gesture runs, with "Same as left click" followed through to the left
        /// click's own choice.</summary>
        public static string Resolve(AppSettings settings, ClickGesture g, bool forFile)
        {
            var id = Stored(settings, g, forFile);
            return id == SameAsLeft ? Stored(settings, ClickGesture.Left, forFile) : id;
        }

        /// <summary>Stores a choice. Storing a gesture's default removes its value, and an entry left
        /// with no values at all is removed, so a settings file only names the clicks someone changed.</summary>
        public static void Set(AppSettings settings, ClickGesture g, bool forFile, string actionId)
        {
            if (settings == null || !IsValid(settings, g, forFile, actionId)) return;
            if (settings.ClickActions == null) settings.ClickActions = new List<ClickActionSetting>();
            var entry = Find(settings, g);
            if (entry == null)
            {
                entry = new ClickActionSetting { Gesture = GestureId(g) };
                settings.ClickActions.Add(entry);
            }
            var value = actionId == DefaultFor(g) ? null : actionId;
            if (forFile) entry.File = value; else entry.Folder = value;
            if (entry.Folder == null && entry.File == null) settings.ClickActions.Remove(entry);
        }

        /// <summary>Drops an entry for an unknown or repeated gesture and any value that is not valid
        /// for it, such as an id from a newer release or a quick action that no longer exists. A null
        /// list stays null, so a settings file that never set a click keeps no key for it.</summary>
        public static void Normalize(AppSettings settings)
        {
            if (settings == null || settings.ClickActions == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cleaned = new List<ClickActionSetting>();
            foreach (var e in settings.ClickActions)
            {
                ClickGesture g;
                if (e == null || !TryParseGesture(e.Gesture, out g) || !seen.Add(e.Gesture)) continue;
                // 4.2.0 and 4.2.1 offered a built-in Copy path for folders; keep such a click copying.
                if (e.Folder == CopyPath) e.Folder = QuickCopyPath;
                if (!IsValid(settings, g, false, e.Folder) || e.Folder == DefaultFor(g)) e.Folder = null;
                if (!IsValid(settings, g, true, e.File) || e.File == DefaultFor(g)) e.File = null;
                if (e.Folder != null || e.File != null) cleaned.Add(e);
            }
            settings.ClickActions = cleaned.Count > 0 ? cleaned : null;
        }

        /// <summary>What a click does on a given row. A folder that does not exist does nothing, as
        /// before this setting; only the old built-in Copy path copied it, and a folder click no longer
        /// offers that (#282). A file row tries to open even when
        /// its target looks missing, as it always has: the shell then reports the problem. Inside a
        /// file dialog's pane a new window would leave the dialog, so it opens the folder in the dialog
        /// instead, the same as Open here.</summary>
        public static ClickOutcome OutcomeFor(string actionId, bool isFile, bool exists, bool inDialog)
        {
            if (actionId == CopyPath) return ClickOutcome.CopyPath;
            if (isFile)
            {
                if (actionId == Open) return ClickOutcome.OpenFile;
                if (actionId == OpenContainingFolder) return ClickOutcome.OpenContainingFolder;
                return ClickOutcome.Nothing;
            }
            if (!exists) return ClickOutcome.Nothing;
            if (actionId == Open) return ClickOutcome.OpenHere;
            if (actionId == NewWindow) return inDialog ? ClickOutcome.OpenHere : ClickOutcome.OpenNewWindow;
            if (actionId == OpenParent) return ClickOutcome.OpenParent;
            if (actionId != null && actionId.StartsWith(QuickPrefix, StringComparison.Ordinal)) return ClickOutcome.QuickAction;
            return ClickOutcome.Nothing;
        }

        /// <summary>The quick action a click may run right now, or null with the reason in
        /// <paramref name="why"/>. Uses the same rules as a pinned row's Quick actions submenu: the
        /// Quick actions section is on, the action is on, and it applies to a pinned row. A custom
        /// command that a restored backup turned off therefore does not run from a click either.
        /// Whether the action's program is installed is the caller's check, because it needs the
        /// machine.</summary>
        public static QuickActionDefinition RunnableQuickAction(AppSettings settings, string actionId, out string why)
        {
            why = null;
            if (settings == null || actionId == null || !actionId.StartsWith(QuickPrefix, StringComparison.Ordinal))
            {
                why = "it is not a quick action";
                return null;
            }
            var id = actionId.Substring(QuickPrefix.Length);
            var def = QuickActionLibrary.Resolve(settings, id);
            if (def == null) { why = "the quick action no longer exists"; return null; }
            if (!QuickActionLibrary.AppliesToPinnedRow(def)) { why = "that quick action does not apply to a folder row"; return null; }

            bool sectionOn = false;
            if (settings.Sections != null)
                foreach (var s in settings.Sections)
                    if (s != null && s.Type == "quickactions") { sectionOn = s.Visible; break; }
            if (!sectionOn) { why = "the Quick actions section is off"; return null; }

            bool enabled = false;
            if (settings.QuickActions != null)
                foreach (var a in settings.QuickActions)
                    if (a != null && string.Equals(a.Id, id, StringComparison.Ordinal)) { enabled = a.Enabled; break; }
            if (!enabled) { why = "the quick action \"" + def.Name + "\" is off"; return null; }
            return def;
        }

        /// <summary>The choices Settings offers for a gesture, as (id, label) pairs in display order.
        /// A folder's list adds every quick action that applies to a pinned row, plus the stored one
        /// if it is not otherwise listed, so the box never shows a different choice from the one that
        /// is saved.</summary>
        public static List<KeyValuePair<string, string>> Options(AppSettings settings, ClickGesture g, bool forFile)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (g != ClickGesture.Left) list.Add(Pair(SameAsLeft, "Same as left click"));
            if (forFile)
            {
                list.Add(Pair(Open, "Open"));
                list.Add(Pair(OpenContainingFolder, "Open containing folder"));
                list.Add(Pair(CopyPath, "Copy path"));
            }
            else
            {
                list.Add(Pair(Open, "Open here"));
                list.Add(Pair(NewWindow, "Open in new window"));
                list.Add(Pair(OpenParent, "Open parent folder"));
                if (settings != null && settings.QuickActions != null)
                {
                    foreach (var a in settings.QuickActions)
                    {
                        if (a == null) continue;
                        var def = QuickActionLibrary.Resolve(settings, a.Id);
                        if (def == null || !QuickActionLibrary.AppliesToPinnedRow(def)) continue;
                        list.Add(Pair(QuickPrefix + def.Id, "Quick action: " + def.Name + (a.Enabled ? "" : " (off)")));
                    }
                }
            }
            if (g != ClickGesture.Left) list.Add(Pair(None, "Do nothing"));
            return list;
        }

        private static KeyValuePair<string, string> Pair(string id, string label)
        {
            return new KeyValuePair<string, string>(id, label);
        }

        private static ClickActionSetting Find(AppSettings settings, ClickGesture g)
        {
            if (settings == null || settings.ClickActions == null) return null;
            var id = GestureId(g);
            foreach (var e in settings.ClickActions)
                if (e != null && string.Equals(e.Gesture, id, StringComparison.Ordinal)) return e;
            return null;
        }
    }
}
