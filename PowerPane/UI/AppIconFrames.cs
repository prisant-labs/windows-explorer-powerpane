using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PowerPane.Services;

namespace PowerPane.UI
{
    /// <summary>
    /// The app icon drawn at a given size from the frames <c>powerpane.ico</c> already holds (16, 24,
    /// 32, 48, and larger), rather than one frame scaled to fit. The Settings window used to take its
    /// icon from <c>Icon.ExtractAssociatedIcon</c>, which returns the 32 px frame, and its 16 px title
    /// row drew that frame at half size, so the icon looked blurred next to the pane header's (#271).
    /// </summary>
    internal static class AppIconFrames
    {
        // Built inside Decoder's try, never as a static field: "pack:" is only a known scheme once WPF
        // has registered it, and a static initializer that threw would break every later call.
        private const string IconPackUri = "pack://application:,,,/PowerPane;component/powerpane.ico";
        private static BitmapDecoder _decoder;

        /// <summary>The index of the frame to draw at <paramref name="targetPx"/> device pixels: the
        /// smallest frame at least that wide, or the largest frame when none is. Scaling a frame down
        /// a little stays sharp, and scaling one up blurs, which is why a smaller frame never wins.
        /// -1 for an empty list.</summary>
        internal static int PickIndex(IList<int> widths, int targetPx)
        {
            if (widths == null || widths.Count == 0) return -1;
            int best = -1;
            int largest = 0;
            for (int i = 0; i < widths.Count; i++)
            {
                if (widths[i] > widths[largest]) largest = i;
                if (widths[i] >= targetPx && (best < 0 || widths[i] < widths[best])) best = i;
            }
            return best >= 0 ? best : largest;
        }

        /// <summary>The icon's whole frame set, for a window's own icon: WPF picks the small and large
        /// frames it needs for the caption, the taskbar, and Alt+Tab from it. Null if the resource
        /// cannot be read.</summary>
        public static ImageSource ForWindow()
        {
            var d = Decoder();
            return d != null && d.Frames.Count > 0 ? d.Frames[0] : null;
        }

        /// <summary>The frame to draw at <paramref name="sizeDip"/> device-independent pixels on a
        /// display with <paramref name="pixelsPerDip"/> pixels to the DIP. Null if the resource cannot
        /// be read.</summary>
        public static ImageSource At(double sizeDip, double pixelsPerDip)
        {
            var d = Decoder();
            if (d == null || d.Frames.Count == 0) return null;
            var widths = new List<int>(d.Frames.Count);
            foreach (var f in d.Frames) widths.Add(f.PixelWidth);
            int i = PickIndex(widths, (int)Math.Ceiling(sizeDip * (pixelsPerDip > 0 ? pixelsPerDip : 1)));
            return i >= 0 ? d.Frames[i] : null;
        }

        private static BitmapDecoder Decoder()
        {
            if (_decoder != null) return _decoder;
            try
            {
                _decoder = BitmapDecoder.Create(new Uri(IconPackUri), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            catch (Exception ex) { Log.Error("read powerpane.ico", ex); }
            return _decoder;
        }
    }
}
