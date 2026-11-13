using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PowerPane.Services
{
    /// <summary>Explorer's View > Show options, read and flipped for the toggle quick actions. They are
    /// per-user settings that every Explorer window shares, so a toggle changes all of them.
    ///
    /// How each one is applied was settled in Windows Sandbox: SHGetSetSettings, the supported call,
    /// writes hidden items, file name extensions and item check boxes to the registry and open windows
    /// change at once. Compact view has no such call; writing UseCompactMode and announcing a
    /// "ShellState" setting change also changes open windows at once. The navigation pane lives in a
    /// binary layout value that a window reads only when it opens a folder, so an open window keeps
    /// its tree until it moves to another folder, and new windows and tabs follow at once. Refreshing
    /// or reopening the same folder does not apply it.
    ///
    /// Reads are registry reads, cheap enough to refresh a row's check mark. A toggle broadcasts a
    /// setting change to every top-level window, so it runs on the work lane, never the UI thread.</summary>
    internal static class ExplorerViewOptions
    {
        public const string HiddenItems = "toggle-hidden-items";
        public const string Extensions = "toggle-extensions";
        public const string CheckBoxes = "toggle-check-boxes";
        public const string CompactView = "toggle-compact-view";
        public const string NavigationPane = "toggle-navigation-pane";

        private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string SizerKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Modules\GlobalSettings\Sizer";
        private const string SizerValue = "PageSpaceControlSizer";

        /// <summary>Byte 4 of PageSpaceControlSizer is 1 when the navigation pane shows and 0 when not.
        /// The rest is the pane's width and layout, kept as found.</summary>
        internal const int NavPaneByte = 4;

        // Explorer writes this when the pane has never been changed: shown, at its default width.
        private static readonly byte[] DefaultSizer = { 0xA0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0x5E, 1, 0, 0 };

        /// <summary>Raised on the work lane after a toggle changes a setting, so the rows can re-read.</summary>
        public static event Action Changed;

        public static bool IsViewOption(string actionId)
        {
            switch (actionId)
            {
                case HiddenItems: case Extensions: case CheckBoxes: case CompactView: case NavigationPane: return true;
                default: return false;
            }
        }

        /// <summary>Whether the option is on now, or null when it could not be read.</summary>
        public static bool? IsOn(string actionId)
        {
            try
            {
                if (actionId == NavigationPane)
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(SizerKey))
                        return NavPaneShown(key == null ? null : key.GetValue(SizerValue) as byte[]);
                }
                using (var key = Registry.CurrentUser.OpenSubKey(AdvancedKey))
                {
                    switch (actionId)
                    {
                        case HiddenItems: return StateFromAdvanced(actionId, key == null ? null : key.GetValue("Hidden"));
                        case Extensions: return StateFromAdvanced(actionId, key == null ? null : key.GetValue("HideFileExt"));
                        case CheckBoxes: return StateFromAdvanced(actionId, key == null ? null : key.GetValue("AutoCheckSelect"));
                        case CompactView: return StateFromAdvanced(actionId, key == null ? null : key.GetValue("UseCompactMode"));
                        default: return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("read Explorer view option '" + actionId + "'", ex);
                return null;
            }
        }

        /// <summary>What an Advanced value means for each option. A missing value is Explorer's default:
        /// hidden items not shown, extensions hidden, no check boxes, and not compact.</summary>
        internal static bool StateFromAdvanced(string actionId, object value)
        {
            int? v = value is int ? (int?)(int)value : null;
            switch (actionId)
            {
                case HiddenItems: return v == 1;            // 1 shows, 2 hides
                case Extensions: return v.HasValue && v.Value == 0;  // HideFileExt: 0 shows
                case CheckBoxes: return v == 1;
                case CompactView: return v == 1;
                default: return false;
            }
        }

        internal static bool NavPaneShown(byte[] sizer)
        {
            if (sizer == null || sizer.Length <= NavPaneByte) return true;
            return sizer[NavPaneByte] != 0;
        }

        /// <summary>A copy of the layout value with the navigation pane set on or off, and every other
        /// byte kept. A missing or too-short value starts from Explorer's own default.</summary>
        internal static byte[] WithNavPane(byte[] sizer, bool on)
        {
            byte[] copy = sizer != null && sizer.Length > NavPaneByte ? (byte[])sizer.Clone() : (byte[])DefaultSizer.Clone();
            copy[NavPaneByte] = on ? (byte)1 : (byte)0;
            return copy;
        }

        /// <summary>Flips the option. Returns a short message for the row when it could not, or null.</summary>
        public static string Toggle(string actionId)
        {
            bool? now = IsOn(actionId);
            if (now == null) return "Could not read the setting";
            bool on = !now.Value;
            try
            {
                switch (actionId)
                {
                    case HiddenItems: SetShellState(SSF_SHOWALLOBJECTS, on); break;
                    case Extensions: SetShellState(SSF_SHOWEXTENSIONS, on); break;
                    case CheckBoxes: SetShellState(SSF_AUTOCHECKSELECT, on); break;
                    case CompactView:
                        using (var key = Registry.CurrentUser.CreateSubKey(AdvancedKey))
                            key.SetValue("UseCompactMode", on ? 1 : 0, RegistryValueKind.DWord);
                        BroadcastShellState();
                        break;
                    case NavigationPane:
                        using (var key = Registry.CurrentUser.CreateSubKey(SizerKey))
                            key.SetValue(SizerValue, WithNavPane(key.GetValue(SizerValue) as byte[], on), RegistryValueKind.Binary);
                        BroadcastShellState();
                        break;
                    default: return null;
                }
                Log.Info("Explorer view option '" + actionId + "' turned " + (on ? "on" : "off") + ".");
            }
            catch (Exception ex)
            {
                Log.Error("toggle Explorer view option '" + actionId + "'", ex);
                return "Could not change the setting";
            }
            try { Changed?.Invoke(); } catch (Exception ex) { Log.Error("view option changed handler", ex); }
            return null;
        }

        // ---- SHGetSetSettings ----------------------------------------------------------------------

        private const uint SSF_SHOWALLOBJECTS = 0x00000001;
        private const uint SSF_SHOWEXTENSIONS = 0x00000002;
        private const uint SSF_AUTOCHECKSELECT = 0x00800000;

        /// <summary>SHELLSTATE is two C bitfields around six 4-byte fields: fShowAllObjects is bit 0 and
        /// fShowExtensions bit 1 of the DWORD at offset 0, and fAutoCheckSelect is bit 3 of the DWORD at
        /// offset 28. Only the fields named in the mask are read on a set, so the rest stay zero.</summary>
        internal const int ShellStateSize = 64;
        internal const int SecondFlagsOffset = 28;

        internal static void EncodeShellState(byte[] buffer, uint mask, bool on)
        {
            Array.Clear(buffer, 0, buffer.Length);
            int first = 0, second = 0;
            if (on && (mask & SSF_SHOWALLOBJECTS) != 0) first |= 1;
            if (on && (mask & SSF_SHOWEXTENSIONS) != 0) first |= 2;
            if (on && (mask & SSF_AUTOCHECKSELECT) != 0) second |= 8;
            BitConverter.GetBytes(first).CopyTo(buffer, 0);
            BitConverter.GetBytes(second).CopyTo(buffer, SecondFlagsOffset);
        }

        [DllImport("shell32.dll")]
        private static extern void SHGetSetSettings(IntPtr state, uint mask, bool set);

        private static void SetShellState(uint mask, bool on)
        {
            var buffer = new byte[ShellStateSize];
            EncodeShellState(buffer, mask, on);
            IntPtr p = Marshal.AllocHGlobal(ShellStateSize);
            try
            {
                Marshal.Copy(buffer, 0, p, ShellStateSize);
                SHGetSetSettings(p, mask, true);
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
            uint flags, uint timeout, out IntPtr result);

        /// <summary>Tells every window the shell settings changed, which is what makes open Explorer
        /// windows re-read compact view. A hung window costs at most the timeout, on the work lane.</summary>
        private static void BroadcastShellState()
        {
            IntPtr result;
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "ShellState", SMTO_ABORTIFHUNG, 1000, out result);
        }
    }
}
