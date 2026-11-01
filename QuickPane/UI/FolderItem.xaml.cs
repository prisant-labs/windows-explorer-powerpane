using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickPane.Services;
using NM = QuickPane.Interop.NativeMethods;

namespace QuickPane.UI
{
    /// <summary>One folder row, shared by the Groups and Recent sections. Shows the real shell icon
    /// for the target so custom folder icons are respected, dims broken targets without deleting them.</summary>
    public partial class FolderItem : UserControl
    {
        public string DisplayName { get; private set; }
        public string TargetPath { get; private set; }
        public bool IsBroken { get; private set; }

        public event Action Clicked;

        public FolderItem()
        {
            InitializeComponent();
            MouseEnter += (s, e) => Root.Background = Brush("ItemHoverBackground");
            MouseLeave += (s, e) => Root.Background = Brushes.Transparent;
            MouseLeftButtonUp += OnLeftClick;

            // Reachable by keyboard: arrows move between rows and Enter opens the one in focus. Rows are
            // only in the tab order when keyboard navigation is switched on, so nothing changes for
            // anyone who leaves it off.
            Focusable = true;
            KeyDown += (s, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter && e.Key != System.Windows.Input.Key.Space) return;
                var h = Clicked;
                if (h != null) { h(); e.Handled = true; }
            };
            GotKeyboardFocus += (s, e) => Root.Background = UiHelpers.AppBrush("ItemHoverBackground");
            LostKeyboardFocus += (s, e) => Root.Background = System.Windows.Media.Brushes.Transparent;
        }

        /// <summary>The text this row shows, which is what the pane's search box filters on.</summary>
        public string LabelText { get; private set; }

        public void Bind(string display, string targetPath, bool exists, bool isFile = false)
        {
            LabelText = display;
            DisplayName = display;
            TargetPath = targetPath;
            IsBroken = !exists;
            Label.Text = display;
            ToolTip = targetPath;

            IconHelper.Bind(Icon, targetPath, exists, isFile);

            if (IsBroken)
            {
                Root.Opacity = 0.5;
                Label.Foreground = Brush("BrokenLinkColor");
                ToolTip = targetPath + "  (target not found)";
            }
        }

        private void OnLeftClick(object sender, MouseButtonEventArgs e)
        {
            var h = Clicked;
            if (h != null) h();
        }

        private Brush Brush(string key)
        {
            var b = TryFindResource(key) as Brush;
            return b ?? Brushes.Transparent;
        }
    }

    /// <summary>
    /// Pulls 16x16 shell icons and converts them to frozen WPF image sources. A cached or placeholder
    /// icon (extension or generic folder) is always shown at once; real shell icon extraction touches
    /// the target's volume, so it never runs on the UI thread, not even for a path on a local fixed
    /// drive, because a fixed drive is not a speed guarantee: custom icon handlers, cloud placeholders,
    /// junctions, and slow disks can all block it (prisant-labs/windows-explorer-quickpane#76). A
    /// bounded drain task does the real extraction on the general worker and delivers the result
    /// straight to every row still waiting on that path, instead of raising a refresh that makes whole
    /// sections rebuild and re-ask for everything the cache has since evicted (#78). The cache is
    /// shared by every sidebar and is thread-safe because the worker fills it too.
    /// </summary>
    internal static class IconHelper
    {
        private static readonly object Gate = new object();
        private const int MaxCache = 600;
        private const int BatchSize = 16;
        private static readonly LruCache<string, ImageSource> Cache =
            new LruCache<string, ImageSource>(MaxCache, StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> FastRoots =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly IconRequests<Image> Requests = new IconRequests<Image>();
        private static readonly Queue<IconJob> Jobs = new Queue<IconJob>();
        private static readonly Dictionary<int, ImageSource> SpecialFolderCache = new Dictionary<int, ImageSource>();
        private static bool _draining;
        private static ImageSource _genericFolder;

        // A queued real-icon extraction. Ready is null when the caller already knows the target
        // exists (a fixed-drive path, or an app icon); otherwise it is the alive-probe gate a slow
        // volume keeps, unchanged from before this file was rewritten.
        private sealed class IconJob
        {
            public string Key;
            public Func<ImageSource> Extract;
            public Func<bool> Ready;
        }

        /// <summary>Shows a cached icon or an instant placeholder, and queues the real shell icon for a
        /// path that is not cached yet. Shared by every tree row and pinned-folder row, so an
        /// extraction two rows both asked for runs once and reaches both (#76, #78).</summary>
        public static void Bind(Image target, string path, bool exists, bool isFile)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) { target.Source = GenericFolder(); return; }

                string key = (isFile ? "f:" : "d:") + path;
                ImageSource cached;
                lock (Gate) { if (Cache.TryGetValue(key, out cached)) { target.Source = cached; return; } }

                if (!exists)
                {
                    // Broken target: icon by extension for files, plain folder otherwise. No disk hit.
                    target.Source = isFile ? ExtensionIcon(path) : GenericFolder();
                    return;
                }

                target.Source = isFile ? ExtensionIcon(path) : GenericFolder();

                // A fixed drive is not a speed guarantee (#76), so even a "fast" root is extracted in
                // the background; the caller already knows the target exists, so unlike a slow volume
                // it skips the alive-probe gate below.
                bool fast = IsFastPath(path);
                QueueExtract(key, target, () => Extract(path, useAttributes: false),
                    fast ? (Func<bool>)null : (Func<bool>)(() => PathStatus.ConfirmedAlive(path)));
            }
            catch (Exception ex)
            {
                Log.Error("Bind failed for '" + path + "'", ex);
            }
        }

        /// <summary>Same idea as <see cref="Bind"/> for an application's own icon: the Start menu
        /// shortcut's chosen icon, falling back to the exe's own icon. The placeholder is the exe's
        /// extension icon.</summary>
        public static void BindApp(Image target, string exePath)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath)) { target.Source = GenericFolder(); return; }

                target.Source = ExtensionIcon(exePath);

                var pick = AppShortcuts.IconFor(exePath);
                string key = pick != null ? "a:" + pick.File + "," + pick.Index : "f:" + exePath;

                ImageSource cached;
                lock (Gate) { if (Cache.TryGetValue(key, out cached)) { target.Source = cached; return; } }

                var path = exePath;
                var picked = pick;
                // The exe's own icon when the Start menu's choice cannot be read, as before. An exe on
                // a share keeps the same alive-probe gate as any other slow path, so a dead share
                // never holds the general lane for the network timeout.
                QueueExtract(key, target,
                    () => (picked != null ? ExtractAt(picked.File, picked.Index) : null) ?? Extract(path, useAttributes: false),
                    IsFastPath(path) ? (Func<bool>)null : (Func<bool>)(() => PathStatus.ConfirmedAlive(path)));
            }
            catch (Exception ex)
            {
                Log.Error("BindApp failed for '" + exePath + "'", ex);
            }
        }

        // Queues one extraction per key no matter how many rows ask for it, and starts the drain task
        // if it is not already running.
        private static void QueueExtract(string key, Image target, Func<ImageSource> extract, Func<bool> ready)
        {
            bool first;
            lock (Gate)
            {
                first = Requests.Add(key, target);
                if (!first) return; // another row already queued this extraction; it reaches this one too
                Jobs.Enqueue(new IconJob { Key = key, Extract = extract, Ready = ready });
                if (_draining) return;
                _draining = true;
            }
            WorkQueue.Post(DrainBatch);
        }

        // Extracts up to BatchSize real icons, then reposts itself if more are queued, so other
        // general-lane work (scans, probes, drops) gets a turn between batches instead of one task
        // holding the lane until every pending icon is done (#76).
        private static void DrainBatch()
        {
            var batch = new List<IconJob>(BatchSize);
            lock (Gate)
            {
                for (int i = 0; i < BatchSize && Jobs.Count > 0; i++) batch.Add(Jobs.Dequeue());
            }

            var updates = new List<KeyValuePair<Image, ImageSource>>();
            try
            {
                foreach (var job in batch)
                {
                    ImageSource img = null;
                    try
                    {
                        // Not ready yet (a slow volume whose probe has not confirmed it answers): leave
                        // the placeholder. The rebuild that follows the probe's Changed event asks
                        // again, so nothing here needs to raise a refresh of its own (#78).
                        if (job.Ready == null || job.Ready()) img = job.Extract();
                    }
                    catch (Exception ex) { Log.Error("icon extract '" + job.Key + "'", ex); }

                    List<Image> targets;
                    lock (Gate) { targets = Requests.Complete(job.Key); }
                    if (img != null)
                    {
                        Put(job.Key, img);
                        foreach (var t in targets) updates.Add(new KeyValuePair<Image, ImageSource>(t, img));
                    }
                }
            }
            finally
            {
                // Always settle _draining, even if something above threw: left set, it would stop
                // every later icon from ever being queued for extraction.
                bool more;
                lock (Gate) { more = Jobs.Count > 0; _draining = more; }
                if (more) WorkQueue.Post(DrainBatch);
            }

            if (updates.Count > 0)
            {
                WorkQueue.PostUI(() =>
                {
                    foreach (var u in updates) u.Key.Source = u.Value;
                });
            }
        }

        /// <summary>One numbered icon out of a file.</summary>
        private static ImageSource ExtractAt(string file, int index)
        {
            var large = new IntPtr[1];
            var small = new IntPtr[1];
            int got = NM.ExtractIconEx(file, index, large, small, 1);
            if (got <= 0) return null;
            try
            {
                IntPtr h = small[0] != IntPtr.Zero ? small[0] : large[0];
                if (h == IntPtr.Zero) return null;
                var img = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                img.Freeze();
                return img;
            }
            catch (Exception ex) { Log.Error("read icon " + index + " of '" + file + "'", ex); return null; }
            finally
            {
                if (large[0] != IntPtr.Zero) NM.DestroyIcon(large[0]);
                if (small[0] != IntPtr.Zero) NM.DestroyIcon(small[0]);
            }
        }

        private static void Put(string key, ImageSource img)
        {
            lock (Gate)
            {
                // Full: the icon used longest ago goes, rather than all of them (#47).
                Cache.Set(key, img);
            }
        }

        // Fixed local drives answer icon queries in microseconds; everything else can block.
        private static bool IsFastPath(string path)
        {
            try
            {
                if (path.StartsWith("\\\\", StringComparison.Ordinal)) return false;
                var root = System.IO.Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root)) return false;
                lock (Gate)
                {
                    bool fast;
                    if (FastRoots.TryGetValue(root, out fast)) return fast;
                }
                bool result = new System.IO.DriveInfo(root).DriveType == System.IO.DriveType.Fixed;
                lock (Gate) { FastRoots[root] = result; }
                return result;
            }
            catch { return false; }
        }

        /// <summary>The instant icon for a program's row, for a caller that binds the real one itself
        /// with <see cref="BindApp"/>. Passing this as a row's override icon stops the row from also
        /// queueing the exe's own icon, which could land after the Start menu's choice and replace it.</summary>
        public static ImageSource AppPlaceholder(string exePath)
        {
            return string.IsNullOrEmpty(exePath) ? GenericFolder() : ExtensionIcon(exePath);
        }

        // Cached by extension rather than by path, because every ".txt" shares the same icon and this
        // used to re-extract it for every row (#76).
        private static ImageSource ExtensionIcon(string path)
        {
            string ext;
            try { ext = System.IO.Path.GetExtension(path) ?? string.Empty; }
            catch { ext = string.Empty; }
            string key = "x:" + ext.ToLowerInvariant();

            ImageSource cached;
            lock (Gate) { if (Cache.TryGetValue(key, out cached)) return cached; }

            var img = Extract(path, useAttributes: true, fileAttr: 0x80 /* FILE_ATTRIBUTE_NORMAL */);
            if (img != null) Put(key, img);
            return img;
        }

        // The real shell icon for a special folder (e.g. Network), via its PIDL. Cached by CSIDL: the
        // set of special folders a pane ever asks for is fixed (This PC, Network), so a plain
        // dictionary is enough and this never needed an LruCache (#76).
        public static ImageSource GetSpecialFolderIcon(int csidl)
        {
            lock (Gate)
            {
                ImageSource cached;
                if (SpecialFolderCache.TryGetValue(csidl, out cached)) return cached;
            }
            try
            {
                IntPtr pidl;
                if (NM.SHGetSpecialFolderLocation(IntPtr.Zero, csidl, out pidl) != 0 || pidl == IntPtr.Zero)
                    return null;
                try
                {
                    var info = new NM.SHFILEINFO();
                    var res = NM.SHGetFileInfoPidl(pidl, 0, ref info,
                        (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NM.SHFILEINFO)),
                        NM.SHGFI_ICON | NM.SHGFI_SMALLICON | NM.SHGFI_PIDL);
                    if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
                    try
                    {
                        var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                        lock (Gate) { SpecialFolderCache[csidl] = src; }
                        return src;
                    }
                    finally { NM.DestroyIcon(info.hIcon); }
                }
                finally { NM.ILFreePidl(pidl); }
            }
            catch (Exception ex) { Log.Error("special folder icon", ex); return null; }
        }

        private static ImageSource GenericFolder()
        {
            lock (Gate)
            {
                if (_genericFolder == null)
                    _genericFolder = Extract("folder", useAttributes: true);
                return _genericFolder;
            }
        }

        private static ImageSource Extract(string path, bool useAttributes, uint fileAttr = NM.FILE_ATTRIBUTE_DIRECTORY)
        {
            var info = new NM.SHFILEINFO();
            uint flags = NM.SHGFI_ICON | NM.SHGFI_SMALLICON;
            uint attr = 0;
            if (useAttributes)
            {
                flags |= NM.SHGFI_USEFILEATTRIBUTES;
                attr = fileAttr;
            }

            IntPtr res = NM.SHGetFileInfo(path, attr, ref info,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NM.SHFILEINFO)), flags);
            if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                NM.DestroyIcon(info.hIcon);
            }
        }
    }
}
