using System;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.Interop
{
    /// <summary>
    /// Tells a border resize from a title-bar move when Windows starts dragging a window. Both raise
    /// EVENT_SYSTEM_MOVESIZESTART, but only a resize makes Explorer and file dialogs lay themselves out
    /// again, so only a resize should take the pane out of the way.
    /// </summary>
    internal static class ResizeBorder
    {
        /// <summary>True when the cursor is on hwnd's resize border right now, which at the start of a
        /// drag means the drag is a resize. A keyboard resize starts with the cursor elsewhere, so callers
        /// also need a fallback, such as the first size change.</summary>
        public static bool CursorOn(IntPtr hwnd)
        {
            NM.RECT wr; NM.POINT cursor;
            return NM.GetWindowRect(hwnd, out wr) && NM.GetCursorPos(out cursor) &&
                   Contains(wr, cursor, BandFor(hwnd));
        }

        // How far from the window's edge a press still counts as grabbing the border: Windows 11's
        // resize area is the invisible 7-pixel frame outside the visible edge plus the edge itself.
        private static int BandFor(IntPtr hwnd) { return 8 * NM.DpiForWindow(hwnd) / 96; }

        /// <summary>True when the cursor sits within band pixels of any edge of the window rectangle.</summary>
        internal static bool Contains(NM.RECT window, NM.POINT cursor, int band)
        {
            if (cursor.X < window.Left || cursor.X >= window.Right || cursor.Y < window.Top || cursor.Y >= window.Bottom)
                return false;
            return cursor.X < window.Left + band || cursor.X >= window.Right - band ||
                   cursor.Y < window.Top + band || cursor.Y >= window.Bottom - band;
        }
    }
}
