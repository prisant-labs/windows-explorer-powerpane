using System;

namespace PowerPane.UI
{
    /// <summary>A rectangle in device pixels. Kept local to the placement code so a test needs no
    /// native RECT.</summary>
    internal struct PixelRect
    {
        public readonly int Left, Top, Right, Bottom;

        public PixelRect(int left, int top, int right, int bottom)
        {
            Left = left; Top = top; Right = right; Bottom = bottom;
        }

        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }

        public static PixelRect FromSize(int left, int top, int width, int height)
        {
            return new PixelRect(left, top, left + width, top + height);
        }

        public override string ToString()
        {
            return "(" + Left + "," + Top + ")-(" + Right + "," + Bottom + ")";
        }
    }

    /// <summary>
    /// Where a name prompt goes, worked out in device pixels so WPF's rounding of device-independent
    /// units cannot move it when the pane is on a monitor with a different scale. The old prompt used
    /// WindowStartupLocation.CenterScreen, which centres on one screen whatever monitor the pane is on.
    /// </summary>
    internal static class PromptPlacement
    {
        /// <summary>Pixels for a size in device-independent units at the given DPI (96 is 100 percent).</summary>
        internal static int ScaleDip(double dip, int dpi)
        {
            if (dpi <= 0) dpi = 96;
            return (int)Math.Round(dip * dpi / 96.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>Position of a prompt of the given size, as a rectangle. It lines up with the anchor's
        /// left edge and sits <paramref name="gap"/> below the anchor, flips above the anchor when the
        /// work area has no room below, and is clamped into the work area. A prompt larger than the work
        /// area on an axis is pinned to the work area's origin on that axis.</summary>
        internal static PixelRect Compute(PixelRect anchor, int width, int height, PixelRect work, int gap)
        {
            if (gap < 0) gap = 0;

            int left = anchor.Left;
            if (width >= work.Width) left = work.Left;
            else
            {
                if (left + width > work.Right) left = work.Right - width;
                if (left < work.Left) left = work.Left;
            }

            int top = anchor.Bottom + gap;
            if (height >= work.Height) top = work.Top;
            else
            {
                if (top + height > work.Bottom)
                {
                    int above = anchor.Top - gap - height;
                    top = above >= work.Top ? above : work.Bottom - height;
                }
                if (top < work.Top) top = work.Top;
            }

            return PixelRect.FromSize(left, top, width, height);
        }
    }
}
