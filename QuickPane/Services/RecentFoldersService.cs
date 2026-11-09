using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using QuickPane.Models;

namespace QuickPane.Services
{
    /// <summary>
    /// Surfaces recently used items from %APPDATA%\Microsoft\Windows\Recent plus folders navigated
    /// through QuickPane. All scanning (shortcut resolution, existence checks) runs on the background
    /// worker: the Recent folder routinely holds hundreds of .lnk files, some pointing at dead
    /// network targets, and resolving those on the UI thread stalled the input queue shared with
    /// Explorer. Shortcuts are resolved newest-first and resolution stops once the list is full, so
    /// the cost is bounded by the visible count rather than the folder size. RecentsChanged is always
    /// raised on the UI thread.
    /// </summary>
    public sealed class RecentFoldersService : IDisposable
    {
        public event Action RecentsChanged;

        public IReadOnlyList<PinnedFolder> Items { get { return _items; } }

        private readonly SettingsStore _settings;
        private readonly string _recentDir;
        private readonly string _appRecentFile;
        private readonly List<string> _appRecent = new List<string>(); // folders navigated via QuickPane
        private List<PinnedFolder> _items = new List<PinnedFolder>();
        private FileSystemWatcher _watcher;
        private Timer _debounce;
        private readonly object _gate = new object();
        private int _rescanQueued; // coalesces queued background rescans

        public RecentFoldersService(SettingsStore settings)
        {
            _settings = settings;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _recentDir = Path.Combine(appData, @"Microsoft\Windows\Recent");
            _appRecentFile = Path.Combine(appData, @"QuickPane\recent.txt");
        }

        /// <summary>Record a folder the user just opened through QuickPane. Safe to call from any
        /// thread; the existence check and file write happen on the worker.</summary>
        public void RecordNavigation(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            WorkQueue.Post(() => RecordCore(path));
        }

        private void RecordCore(string path)
        {
            try
            {
                // A hidden folder is never written to recent.txt, not merely filtered on the way out.
                if (IsHidden(path, _settings.Current.RecentHiddenFolders)) return;
                if (!Directory.Exists(path)) return;
                bool changed;
                lock (_gate)
                {
                    if (_appRecent.Count > 0 && string.Equals(_appRecent[0], path, StringComparison.OrdinalIgnoreCase))
                    {
                        changed = false; // already the most recent; nothing to do
                    }
                    else
                    {
                        _appRecent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                        _appRecent.Insert(0, path);
                        while (_appRecent.Count > 50) _appRecent.RemoveAt(_appRecent.Count - 1);
                        SaveAppRecent();
                        changed = true;
                    }
                }
                if (changed) RescanCore();
            }
            catch (Exception ex) { Log.Error("record navigation", ex); }
        }

        private void LoadAppRecent()
        {
            try
            {
                if (!File.Exists(_appRecentFile)) return;
                lock (_gate)
                {
                    _appRecent.Clear();
                    foreach (var line in File.ReadAllLines(_appRecentFile))
                        if (!string.IsNullOrWhiteSpace(line)) _appRecent.Add(line.Trim());
                }
            }
            catch (Exception ex) { Log.Error("load app recent", ex); }
        }

        private void SaveAppRecent()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_appRecentFile));
                File.WriteAllLines(_appRecentFile, _appRecent);
            }
            catch (Exception ex) { Log.Error("save app recent", ex); }
        }

        public void Start()
        {
            OnSettingsChanged(null, EventArgs.Empty);   // records the starting key, so the first save compares against it
            _settings.Changed += OnSettingsChanged;
            WorkQueue.Post(() =>
            {
                LoadAppRecent();
                RescanCore();
            });
            try
            {
                if (!Directory.Exists(_recentDir)) return;
                _watcher = new FileSystemWatcher(_recentDir, "*.lnk")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };
                _watcher.Created += OnFsEvent;
                _watcher.Changed += OnFsEvent;
                _watcher.Deleted += OnFsEvent;
                _watcher.Renamed += OnFsEvent;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Log.Error("recents watcher failed", ex);
            }
        }

        /// <summary>Queue a rescan on the worker. Multiple queued requests coalesce into one.</summary>
        public void Rescan()
        {
            if (Interlocked.Exchange(ref _rescanQueued, 1) == 1) return;
            WorkQueue.Post(() =>
            {
                Interlocked.Exchange(ref _rescanQueued, 0);
                RescanCore();
            });
        }

        // Runs on the worker only.
        private void RescanCore()
        {
            var result = new List<PinnedFolder>();
            try
            {
                var current = _settings.Current;
                int cap = current.RecentsMaxCount;
                var hidden = current.RecentHiddenFolders;
                long clearedTicks = current.RecentClearedTicks;

                // A folder hidden after it was recorded leaves recent.txt here, so it is gone from
                // disk too, not just from the pane.
                List<string> appSnapshot;
                lock (_gate)
                {
                    if (_appRecent.RemoveAll(p => IsHidden(p, hidden)) > 0) SaveAppRecent();
                    appSnapshot = _appRecent.ToList();
                }
                var appItems = new List<PinnedFolder>();
                foreach (var p in appSnapshot)
                {
                    if (!SafeDirExists(p)) continue;
                    appItems.Add(new PinnedFolder { DisplayName = Leaf(p), TargetPath = p, LinkPath = null, Exists = true, IsFile = false });
                    if (appItems.Count >= cap) break;
                }

                var recentItems = new List<PinnedFolder>();
                if (Directory.Exists(_recentDir))
                {
                    // Newest first by link write time (cheap metadata), then resolve only until the
                    // list is full. This keeps a 500-link Recent folder from costing 500 COM loads.
                    var byTime = new List<Tuple<DateTime, string>>();
                    foreach (var lnk in Directory.GetFiles(_recentDir, "*.lnk"))
                    {
                        DateTime when;
                        try { when = File.GetLastWriteTimeUtc(lnk); }
                        catch { when = DateTime.MinValue; }
                        // Checked before resolving, so a cleared list also costs no COM loads.
                        if (ClearedBefore(when, clearedTicks)) continue;
                        byTime.Add(Tuple.Create(when, lnk));
                    }
                    byTime.Sort((a, b) => b.Item1.CompareTo(a.Item1));

                    int budget = Math.Max(cap * 3, 60); // resolution attempts, not results
                    foreach (var entry in byTime)
                    {
                        if (recentItems.Count >= cap || budget-- <= 0) break;

                        string target;
                        try { target = ShellLink.ResolveTarget(entry.Item2); }
                        catch { continue; }
                        if (string.IsNullOrEmpty(target)) continue;
                        if (IsHidden(target, hidden)) continue;

                        bool isDir = false, isFile = false;
                        try { isDir = Directory.Exists(target); if (!isDir) isFile = File.Exists(target); }
                        catch { continue; }
                        if (!isDir && !isFile) continue;

                        recentItems.Add(new PinnedFolder
                        {
                            DisplayName = Leaf(target),
                            TargetPath = target,
                            LinkPath = entry.Item2,
                            Exists = true,
                            IsFile = isFile
                        });
                    }
                }

                // Folders opened through QuickPane come first, then the Windows Recent items.
                result = appItems.Concat(recentItems)
                    .GroupBy(p => p.TargetPath, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Take(cap)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Error("recents rescan failed", ex);
            }

            lock (_gate) { _items = result; }
            WorkQueue.PostUI(() => { RecentsChanged?.Invoke(); });
        }

        public void RemoveFromRecents(PinnedFolder item)
        {
            WorkQueue.Post(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(item.LinkPath) && File.Exists(item.LinkPath)) File.Delete(item.LinkPath);
                    lock (_gate)
                    {
                        if (_appRecent.RemoveAll(p => string.Equals(p, item.TargetPath, StringComparison.OrdinalIgnoreCase)) > 0)
                            SaveAppRecent();
                    }
                    RescanCore();
                }
                catch (Exception ex) { Log.Error("remove recent failed", ex); }
            });
        }

        // ---- hidden folders and Clear Recent (#224) ------------------------------------------

        /// <summary>Hide a folder, and everything inside it, from Recent for good. Runs on the UI
        /// thread: it replaces the settings list rather than editing it, because the scan on the work
        /// lane may be reading the old one. The settings change then rescans.</summary>
        public void HideFolder(string folder)
        {
            var current = _settings.Current;
            var updated = WithHidden(current.RecentHiddenFolders, folder);
            if (updated == null) return;
            current.RecentHiddenFolders = updated;
            _settings.Save();
        }

        /// <summary>Show a hidden folder in Recent again. UI thread, like <see cref="HideFolder"/>.</summary>
        public void UnhideFolder(string folder)
        {
            var current = _settings.Current;
            var list = current.RecentHiddenFolders;
            if (list == null) return;
            string key = FolderKey(folder);
            var updated = list.Where(h => !string.Equals(FolderKey(h), key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (updated.Count == list.Count) return;
            current.RecentHiddenFolders = updated.Count == 0 ? null : updated;
            _settings.Save();
        }

        /// <summary>Empty QuickPane's own list, and hide the shortcuts Windows' Recent folder holds right
        /// now without deleting them. Those shortcuts are Windows' own Recent list and Quick Access too,
        /// and clearing QuickPane must not clear Windows. UI thread; the file write runs on the lane.</summary>
        public void ClearRecent()
        {
            _settings.Current.RecentClearedTicks = DateTime.UtcNow.Ticks;
            _settings.Save();
            WorkQueue.Post(() =>
            {
                try
                {
                    lock (_gate) { _appRecent.Clear(); SaveAppRecent(); }
                    RescanCore();
                }
                catch (Exception ex) { Log.Error("clear recent failed", ex); }
            });
        }

        /// <summary>The folder as a comparison key: backslashes only, no surrounding spaces, and
        /// exactly one trailing backslash, so "C:\Demo\Private" and "C:\Demo\Private\" are one folder
        /// and a drive root keeps its own. Null for nothing usable.</summary>
        internal static string FolderKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var p = path.Trim().Replace('/', '\\').TrimEnd('\\');
            return p.Length == 0 ? null : p + "\\";
        }

        /// <summary>Whether a recent item sits in a hidden folder or is one. The trailing backslash in
        /// both keys is what keeps "C:\Demo\Private" from also hiding "C:\Demo\PrivateStuff". Letter
        /// case is ignored, the way Windows compares paths.</summary>
        internal static bool IsHidden(string path, IEnumerable<string> hiddenFolders)
        {
            if (hiddenFolders == null) return false;
            string key = FolderKey(path);
            if (key == null) return false;
            foreach (var h in hiddenFolders)
            {
                string hk = FolderKey(h);
                if (hk != null && key.StartsWith(hk, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Whether a Windows Recent shortcut was written at or before the last Clear Recent.
        /// Zero means Clear Recent was never pressed.</summary>
        internal static bool ClearedBefore(DateTime shortcutWrittenUtc, long clearedTicks)
        {
            return clearedTicks > 0 && shortcutWrittenUtc.Ticks <= clearedTicks;
        }

        /// <summary>A new list with the folder added, or null when it adds nothing: the folder is
        /// empty, or it is already listed. A folder already inside a listed one is not added either,
        /// because it is hidden already.</summary>
        internal static List<string> WithHidden(IList<string> current, string folder)
        {
            string key = FolderKey(folder);
            if (key == null) return null;
            if (IsHidden(folder, current)) return null;
            var updated = current == null ? new List<string>() : current.ToList();
            updated.Add(key.Length > 3 ? key.TrimEnd('\\') : key);   // "C:\" keeps its backslash
            return updated;
        }

        private string _settingsKey;

        // Rescan when anything Recent reads from settings changes: the hidden list, Clear Recent, or
        // the count. Other saves, such as a width drag, change none of them and cost nothing here.
        private void OnSettingsChanged(object sender, EventArgs e)
        {
            var c = _settings.Current;
            string key = c.RecentsMaxCount + "|" + c.RecentClearedTicks + "|" +
                (c.RecentHiddenFolders == null ? string.Empty : string.Join("|", c.RecentHiddenFolders));
            if (key == _settingsKey) return;
            bool first = _settingsKey == null;
            _settingsKey = key;
            if (!first) Rescan();
        }

        private static bool SafeDirExists(string path)
        {
            try { return Directory.Exists(path); } catch { return false; }
        }

        private static string Leaf(string path)
        {
            try
            {
                var trimmed = path.TrimEnd('\\', '/');
                var leaf = Path.GetFileName(trimmed);
                return string.IsNullOrEmpty(leaf) ? trimmed : leaf;
            }
            catch { return path; }
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            if (_debounce == null)
                _debounce = new Timer(_ => Rescan(), null, Timeout.Infinite, Timeout.Infinite);
            _debounce.Change(300, Timeout.Infinite);
        }

        public void Dispose()
        {
            try { _settings.Changed -= OnSettingsChanged; } catch { }
            try { _watcher?.Dispose(); } catch { }
            try { _debounce?.Dispose(); } catch { }
        }
    }
}
