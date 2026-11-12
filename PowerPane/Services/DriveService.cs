using System;
using System.Collections.Generic;
using System.IO;
using PowerPane.Services;

namespace PowerPane.Services
{
    public sealed class DriveItem
    {
        public string Letter { get; set; }      // "C:"
        public string RootPath { get; set; }     // "C:\"
        public string Label { get; set; }        // volume label or a sensible default
        public bool IsNetwork { get; set; }
        public bool IsRemovable { get; set; }
        public bool Ready { get; set; }
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }

        public double UsedFraction
        {
            get
            {
                if (TotalBytes <= 0) return 0;
                var used = (double)(TotalBytes - FreeBytes) / TotalBytes;
                return used < 0 ? 0 : (used > 1 ? 1 : used);
            }
        }
    }

    /// <summary>
    /// Snapshots fixed, removable, and network drives for the Computer section. IsReady, volume
    /// labels, and sizes are queried in full only on the background worker: a mapped network drive
    /// whose server is gone blocks those calls for many seconds, and this used to run on the UI
    /// thread on every pane rebuild. A fixed local drive used to be queried in full on the quick pass
    /// too, on the assumption that it always answers fast; it is not a speed guarantee (custom icon
    /// handlers, cloud placeholders, junctions, and slow disks can all block it), so the quick pass
    /// now reads only DriveType for every drive and the worker fills in the rest
    /// (prisant-labs/windows-explorer-powerpane#76). The synchronous path returns the cached snapshot
    /// and kicks a refresh.
    /// </summary>
    public sealed class DriveService
    {
        /// <summary>Raised on the UI thread when a background refresh replaced the snapshot.</summary>
        public event Action Refreshed;

        private volatile List<DriveItem> _cache;
        private readonly object _cacheGate = new object();
        private int _refreshing;

        public List<DriveItem> GetDrives()
        {
            var c = _cache;
            if (c == null)
            {
                c = Snapshot(fullDetail: false); // drive types only; instant
                // Startup's RefreshAsync may have written the full snapshot while this one was being
                // taken. Keep whichever landed first, so the quick one never overwrites real labels,
                // sizes, and readiness (#76).
                lock (_cacheGate)
                {
                    if (_cache == null) _cache = c;
                    else c = _cache;
                }
                RefreshAsync();
            }
            return c;
        }

        /// <summary>Re-query everything (including slow volumes) on the worker.</summary>
        public void RefreshAsync()
        {
            if (System.Threading.Interlocked.Exchange(ref _refreshing, 1) == 1) return;
            WorkQueue.Post(() =>
            {
                try
                {
                    var list = Snapshot(fullDetail: true);
                    lock (_cacheGate) { _cache = list; }
                    WorkQueue.PostUI(() => { Refreshed?.Invoke(); });
                }
                finally { System.Threading.Interlocked.Exchange(ref _refreshing, 0); }
            });
        }

        private static List<DriveItem> Snapshot(bool fullDetail)
        {
            var list = new List<DriveItem>();
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch (Exception ex) { Log.Error("GetDrives failed", ex); return list; }

            foreach (var d in drives)
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed &&
                        d.DriveType != DriveType.Removable &&
                        d.DriveType != DriveType.Network)
                        continue;

                    var item = new DriveItem
                    {
                        Letter = d.Name.TrimEnd('\\'),
                        RootPath = d.Name,
                        IsNetwork = d.DriveType == DriveType.Network,
                        IsRemovable = d.DriveType == DriveType.Removable
                    };

                    // IsReady / labels / sizes can block on any volume, fixed drives included, so the
                    // quick pass leaves them at optimistic defaults and the worker fills them in.
                    bool queryDetail = QueryDetail(fullDetail, d.DriveType);
                    if (queryDetail && d.IsReady)
                    {
                        item.Ready = true;
                        item.Label = string.IsNullOrWhiteSpace(d.VolumeLabel)
                            ? DefaultLabel(d.DriveType)
                            : d.VolumeLabel;
                        item.TotalBytes = d.TotalSize;
                        item.FreeBytes = d.TotalFreeSpace;
                    }
                    else
                    {
                        item.Ready = !queryDetail; // assume alive until the worker says otherwise
                        item.Label = DefaultLabel(d.DriveType);
                    }

                    list.Add(item);
                }
                catch (Exception ex)
                {
                    Log.Error("drive read failed for " + d.Name, ex);
                }
            }
            return list;
        }

        // The quick pass's decision of whether to read IsReady/label/size for a drive. Pulled out so
        // a test can pin it down: it is always just fullDetail now, since a fixed drive is no longer
        // treated as a speed guarantee (#76). driveType is kept as a parameter so a future exception
        // (a drive type that genuinely never blocks) has somewhere to go without changing every caller.
        internal static bool QueryDetail(bool full, DriveType driveType)
        {
            return full;
        }

        private static string DefaultLabel(DriveType type)
        {
            switch (type)
            {
                case DriveType.Removable: return "Removable Disk";
                case DriveType.Network: return "Network Drive";
                default: return "Local Disk";
            }
        }
    }
}
