using System.Windows;

namespace PowerPane.UI
{
    /// <summary>Where the "Add to PowerPane" picker opens. Pure arithmetic in device pixels, so it is
    /// unit-tested and the window can run it again whenever its height changes.</summary>
    internal static class PickerPlacement
    {
        /// <summary>The top-left corner for a window of <paramref name="windowPx"/> that opens near the
        /// pointer and stays fully inside <paramref name="workAreaPx"/>, the work area of the monitor
        /// the pointer is on. The corner starts <paramref name="offsetPx"/> up and to the left of the
        /// pointer, so the pointer lands just inside the window, as the old picker did. The window is
        /// then pushed back in from the right and bottom edges, and last from the left and top, so a
        /// window larger than the work area keeps its top-left corner visible.</summary>
        internal static Point Place(Point pointerPx, Size windowPx, Rect workAreaPx, int offsetPx)
        {
            double x = pointerPx.X - offsetPx;
            double y = pointerPx.Y - offsetPx;

            if (x + windowPx.Width > workAreaPx.Right) x = workAreaPx.Right - windowPx.Width;
            if (y + windowPx.Height > workAreaPx.Bottom) y = workAreaPx.Bottom - windowPx.Height;
            if (x < workAreaPx.Left) x = workAreaPx.Left;
            if (y < workAreaPx.Top) y = workAreaPx.Top;

            return new Point(x, y);
        }
    }
}
