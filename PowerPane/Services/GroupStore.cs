using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using PowerPane.Models;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.Services
{
    /// <summary>
    /// Reads and writes the groups hierarchy. A group is a folder NN_Name. Inside it, each tab is a
    /// subfolder TT_TabName, and a tab's pins are NNN_Name.lnk files in that subfolder. A legacy group
    /// that still holds loose .lnk files is migrated to a single tab on first scan, so a one-tab group
    /// looks exactly like a plain group while groups with several tabs show a tab row.
    /// </summary>
    public sealed class GroupStore : IDisposable
    {
        public event Action GroupsChanged;

        public IReadOnlyList<PinnedGroup> Groups { get { return _groups; } }

        private static readonly Regex Prefix = new Regex(@"^(\d+)_(.*)$", RegexOptions.Compiled);

        private readonly SettingsStore _settings;
        private readonly Dictionary<string, int> _activeTabs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private List<PinnedGroup> _groups = new List<PinnedGroup>();
        private string _publishedRoot;   // the groups folder _groups was read from; null before the first scan
        private FileSystemWatcher _watcher;
        private Timer _debounce;
        private readonly object _gate = new object();
        private readonly CoalescedPost _rescan;

        private readonly Action<string> _info;
        private readonly Action<Action> _post;

        public GroupStore(SettingsStore settings) : this(settings, null, null) { }

        /// <summary>info receives notes such as a refused stale edit. It defaults to the log; tests pass
        /// their own so those notes stay out of it.</summary>
        internal GroupStore(SettingsStore settings, Action<string> info) : this(settings, info, null) { }

        /// <summary>post is the seam PostEdit runs a posted edit through. It defaults to the worker
        /// lane; a test passes a synchronous or a queuing fake so it can run a posted edit on its own
        /// schedule (prisant-labs/windows-explorer-powerpane#75).</summary>
        internal GroupStore(SettingsStore settings, Action<string> info, Action<Action> post)
        {
            _settings = settings;
            _info = info ?? Log.Info;
            _post = post ?? WorkQueue.Post;
            // A field initializer cannot call an instance method (CS0236), so this is built here
            // instead of alongside _debounce and _gate above.
            _rescan = new CoalescedPost(() => { ScanOnce(); RaiseChanged(); }, WorkQueue.Post);
        }

        public string Root { get { return _settings.ExpandedGroupsPath; } }

        /// <summary>Run edit on the worker as one operation with its scan, so a UI handler that
        /// renames, reorders, or pins something never waits on the scan lock or the shortcut COM
        /// calls a full ScanOnce makes for every pin (#75). Root is captured here, on the caller's
        /// thread; if a profile switch changes it before the posted action runs, the edit is refused
        /// the same way a stale group, tab, or pin already is, through the same _info note. Never
        /// post a lambda that touches a section or a control: closures here capture only data and the
        /// store itself (#69).</summary>
        public void PostEdit(Action<GroupStore> edit)
        {
            if (edit == null) return;
            var root = Root;
            _post(() =>
            {
                if (!SameRoot(root, Root))
                {
                    _info("ignored an edit outside the current groups folder; the profile changed under it");
                    return;
                }
                try { edit(this); }
                catch (Exception ex) { Log.Error("posted group edit failed", ex); }
            });
        }

        // The first scan and a reload run on the worker. Both walk the groups folder and read every
        // pin's .lnk through COM, and on the UI thread a groups folder on a slow or unreachable share
        // froze startup, before the tray icon existed, for the network timeout
        // (prisant-labs/windows-explorer-powerpane#42). Panes show an empty Groups section until
        // GroupsChanged arrives and they rebuild.
        public void Start()
        {
            WorkQueue.Post(() =>
            {
                // A scan discarded because the profile changed while it ran leaves the list empty, and
                // seeding then would write the default group into the new profile's folder.
                if (ScanOnce()) SeedDefaultsIfEmpty();
                InstallWatcher();
                RaiseChanged();
            });
        }

        // Called on the UI thread when the groups folder setting changes, usually a profile switch, and
        // after edits made outside the store, such as a pin's new path.
        public void Reload()
        {
            if (DropIfRootChanged()) RaiseChanged();
            WorkQueue.Post(() =>
            {
                InstallWatcher();
                ScanOnce();
                RaiseChanged();
            });
        }

        /// <summary>Drop the published groups when they were read from another groups folder, and
        /// report whether it did. After a profile switch, panes rebuild for the new profile straight
        /// away, and an edit on a group still showing from the old profile wrote into the old profile's
        /// folder, and a reorder moved its folders into the new one
        /// (prisant-labs/windows-explorer-powerpane#68). A reload of the same folder keeps them, so the
        /// Groups section does not go blank for the length of the scan.</summary>
        internal bool DropIfRootChanged()
        {
            lock (_gate)
            {
                if (SameRoot(_publishedRoot, Root)) return false;
                _groups = new List<PinnedGroup>();
                _publishedRoot = null;
                return true;
            }
        }

        // GroupsChanged always fires on the UI thread, because scans can complete on the
        // FileSystemWatcher debounce (worker) and every subscriber is a WPF control.
        private void RaiseChanged()
        {
            WorkQueue.PostUI(() => { GroupsChanged?.Invoke(); });
        }

        internal void SeedDefaultsIfEmpty()
        {
            try
            {
                // Only an empty list read from the current folder means an empty profile. After a switch
                // the list is empty because it was dropped, and seeding would write into the new profile.
                lock (_gate) { if (_groups.Count > 0 || !SameRoot(_publishedRoot, Root)) return; }
                var group = CreateGroup("Library");
                if (group == null) return;

                // First tab is "Library", created with the group.
                var library = group.Tabs.FirstOrDefault();
                if (library != null)
                {
                    AddPin(library, Folder(Environment.SpecialFolder.DesktopDirectory));
                    AddPin(library, Down(Profile(), "Downloads"));
                    AddPin(library, Folder(Environment.SpecialFolder.MyDocuments));
                    AddPin(library, Folder(Environment.SpecialFolder.MyPictures));
                    AddPin(library, Folder(Environment.SpecialFolder.MyMusic));
                    AddPin(library, Folder(Environment.SpecialFolder.MyVideos));
                }

                var system = AddTab(group, "System");
                if (system != null)
                {
                    AddPin(system, Profile());
                    AddPin(system, "C:\\");
                    AddPin(system, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
                    AddPin(system, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
                    AddPin(system, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
                }
            }
            catch (Exception ex) { Log.Error("seed defaults failed", ex); }
        }

        private static string Profile() { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
        private static string Folder(Environment.SpecialFolder f) { return Environment.GetFolderPath(f); }
        private static string Down(string b, string s) { try { return string.IsNullOrEmpty(b) ? null : Path.Combine(b, s); } catch { return null; } }

        // ---- scanning --------------------------------------------------------

        private readonly object _scanLock = new object();

        /// <summary>Rescan the groups folder and publish the result. Returns true when the scan read the
        /// whole folder and was published; false when it failed partway, or when the groups folder
        /// changed while it ran and its result was discarded.</summary>
        public bool ScanOnce()
        {
            // Serialize scans: they can run from the UI (mutations) and the watcher debounce
            // (worker) at the same time, and Materialize/recovery move things on disk.
            lock (_scanLock) { return ScanCore(); }
        }

        private bool ScanCore()
        {
            var result = new List<PinnedGroup>();
            string root = null;
            bool complete = false;
            try
            {
                root = Root;
                Directory.CreateDirectory(root);
                RecoverTemps(root);

                foreach (var dir in Directory.GetDirectories(root))
                {
                    int order; string name;
                    Split(Path.GetFileName(dir), out order, out name);
                    var group = new PinnedGroup { Name = name, FolderPath = dir, Order = order };

                    var subdirs = Directory.GetDirectories(dir);
                    var loose = Directory.GetFiles(dir, "*.lnk");

                    if (subdirs.Length == 0 && loose.Length > 0)
                    {
                        Materialize(dir, name, loose);          // legacy -> single tab
                        subdirs = Directory.GetDirectories(dir);
                        loose = new string[0];
                    }

                    if (subdirs.Length == 0)
                    {
                        // Empty group: present one implicit tab pointing at the group folder.
                        group.Tabs.Add(new PinnedTab { Name = name, FolderPath = dir, Order = 0 });
                    }
                    else
                    {
                        foreach (var sub in subdirs)
                        {
                            int torder; string tname;
                            Split(Path.GetFileName(sub), out torder, out tname);
                            var tab = new PinnedTab { Name = tname, FolderPath = sub, Order = torder };
                            LoadPins(sub, tab);
                            group.Tabs.Add(tab);
                        }
                        group.Tabs = group.Tabs.OrderBy(t => t.Order)
                            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

                        if (loose.Length > 0)
                        {
                            // Loose files alongside tabs: fold them into a new leading tab.
                            Materialize(dir, name, loose);
                            // simplest: rescan this group next pass; for now skip the loose set
                        }
                    }

                    int active;
                    // _activeTabs is written on the UI thread (SetActiveTab) and read here on the
                    // worker; both sides take _gate (#75).
                    lock (_gate) { _activeTabs.TryGetValue(dir, out active); }
                    group.ActiveTab = Math.Max(0, Math.Min(active, group.Tabs.Count - 1));
                    result.Add(group);
                }

                result = result.OrderBy(g => g.Order)
                    .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                complete = true;
            }
            catch (Exception ex) { Log.Error("ScanOnce failed", ex); }

            return Publish(root, result) && complete;
        }

        /// <summary>Publish a scan of scannedRoot, unless the groups folder changed while it ran. Its
        /// groups then belong to the old folder, and the scan Reload queued for the new folder
        /// publishes instead (#68).</summary>
        internal bool Publish(string scannedRoot, List<PinnedGroup> groups)
        {
            lock (_gate)
            {
                if (scannedRoot == null || !SameRoot(scannedRoot, Root)) return false;
                _groups = groups;
                _publishedRoot = scannedRoot;
                return true;
            }
        }

        /// <summary>True when two groups folder paths name the same folder, ignoring case and a
        /// trailing separator.</summary>
        internal static bool SameRoot(string a, string b)
        {
            if (a == null || b == null) return a == b;
            return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when path is the groups folder root or lies inside it. A group, tab, or pin
        /// whose path fails this came from another profile's snapshot, and editing it would write
        /// into that profile's folder (#68).</summary>
        internal static bool BelongsTo(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
            var r = root.TrimEnd('\\', '/');
            if (r.Length == 0) return false;
            if (string.Equals(path.TrimEnd('\\', '/'), r, StringComparison.OrdinalIgnoreCase)) return true;
            return path.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when path is a group folder directly inside root. Groups are checked this
        /// strictly because a group from another profile whose folder lies inside this one would pass
        /// BelongsTo, and a reorder would move it into this profile.</summary>
        internal static bool IsGroupIn(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
            try
            {
                var parent = Path.GetDirectoryName(path.TrimEnd('\\', '/'));
                return parent != null && SameRoot(parent, root);
            }
            catch { return false; }
        }

        // Every edit that takes a group, tab, or pin checks it first. Paths that are null are skipped,
        // so callers can pass optional arguments straight through.
        private bool Stale(params string[] paths) { return Refuse(paths, BelongsTo); }
        private bool StaleGroup(params string[] groupPaths) { return Refuse(groupPaths, IsGroupIn); }

        /// <summary>For an edit made outside the store, such as a pin's new path: true when path is in
        /// the current groups folder. Notes a refusal in the log, like the store's own edits.</summary>
        internal bool IsCurrent(string path) { return path != null && !Stale(path); }

        private bool Refuse(string[] paths, Func<string, string, bool> owned)
        {
            var root = Root;
            foreach (var p in paths)
            {
                if (p == null || owned(p, root)) continue;
                _info("ignored an edit outside the current groups folder; the profile changed under it");
                return true;
            }
            return false;
        }

        // One listing of a tab folder gives both the pins and the sort marker (#241). The pins carry
        // their stored order, display name, and creation time, and no target yet.
        private static List<PinnedFolder> ReadPinFiles(string tabDir, out PinSortMode mode)
        {
            mode = PinSortMode.Manual;
            var pins = new List<PinnedFolder>();
            foreach (var fi in new DirectoryInfo(tabDir).GetFiles())
            {
                if (string.Equals(fi.Name, PinSorter.MarkerFileName, StringComparison.OrdinalIgnoreCase))
                {
                    try { mode = PinSorter.ParseMarker(File.ReadAllText(fi.FullName)); }
                    catch (Exception ex) { Log.Error("read the sort marker", ex); }
                    continue;
                }
                if (!fi.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) continue;
                int o; string disp;
                Split(Path.GetFileNameWithoutExtension(fi.Name), out o, out disp);
                pins.Add(new PinnedFolder { DisplayName = disp, LinkPath = fi.FullName, Order = o, AddedUtc = fi.CreationTimeUtc });
            }
            return pins;
        }

        private static void LoadPins(string tabDir, PinnedTab tab)
        {
            PinSortMode mode;
            foreach (var pin in ReadPinFiles(tabDir, out mode))
            {
                var target = ShellLink.ResolveTarget(pin.LinkPath) ?? string.Empty;
                // Existence and file-vs-folder come from the background probe cache, never from a
                // direct disk hit here: scans run on the UI thread after mutations, and probing a
                // dead network target inline froze the input queue shared with Explorer.
                pin.TargetPath = target;
                pin.Exists = target.Length > 0 && PathStatus.IsAlive(target);
                pin.IsFile = target.Length > 0 && PathStatus.IsFile(target);
                tab.Items.Add(pin);
            }
            tab.SortMode = mode;
            tab.Items = PinSorter.Order(tab.Items, mode);
        }

        private static string MarkerPath(string tabDir) { return Path.Combine(tabDir, PinSorter.MarkerFileName); }

        // Removing a sort marker: the tab becomes Manual. Absent already is fine.
        private static void DeleteMarker(string tabDir)
        {
            var marker = MarkerPath(tabDir);
            if (File.Exists(marker)) File.Delete(marker);
        }

        // The real folder pins live in. A group with no tab subfolders shows one implicit tab that
        // points at the group folder, so the first write creates its 01_<name> subfolder.
        private string EnsureTabDir(PinnedTab tab)
        {
            var dir = tab.FolderPath;
            if (IsGroupFolder(dir))
            {
                dir = Path.Combine(dir, "01_" + (string.IsNullOrWhiteSpace(tab.Name) ? "Main" : tab.Name));
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        // Bring back anything stranded under a temp name by a reorder that died halfway (crash,
        // locked folder, antivirus). Recovered entries sort to the end but keep their contents.
        private static void RecoverTemps(string root)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(root, "~ord~*"))
                    TryRename(dir, Path.Combine(root, "99_Recovered"), isDir: true);
                foreach (var group in Directory.GetDirectories(root))
                {
                    foreach (var sub in Directory.GetDirectories(group, "~ord~*"))
                        TryRename(sub, Path.Combine(group, "99_Recovered"), isDir: true);
                    foreach (var tabDir in Directory.GetDirectories(group))
                    {
                        foreach (var f in Directory.GetFiles(tabDir, "~ord~*.lnk"))
                            TryRename(f, Path.Combine(tabDir, "999_Recovered.lnk"), isDir: false);
                        foreach (var f in Directory.GetFiles(tabDir, "~new~*.lnk"))
                            TryRename(f, Path.Combine(tabDir, "999_Recovered.lnk"), isDir: false);
                    }
                }
            }
            catch (Exception ex) { Log.Error("recover temps", ex); }
        }

        private static void TryRename(string src, string destBase, bool isDir)
        {
            try
            {
                string dest = destBase;
                int n = 1;
                string dir = Path.GetDirectoryName(destBase);
                string name = Path.GetFileNameWithoutExtension(destBase);
                string ext = Path.GetExtension(destBase);
                while (isDir ? Directory.Exists(dest) : File.Exists(dest))
                    dest = Path.Combine(dir, name + " (" + (++n) + ")" + ext);
                if (isDir) Directory.Move(src, dest); else File.Move(src, dest);
                Log.Info("recovered stranded temp '" + src + "' -> '" + dest + "'");
            }
            catch (Exception ex) { Log.Error("recover temp '" + src + "'", ex); }
        }

        private static void Materialize(string groupDir, string name, string[] looseLnks)
        {
            try
            {
                var tabDir = Path.Combine(groupDir, "01_" + (string.IsNullOrWhiteSpace(name) ? "Main" : name));
                Directory.CreateDirectory(tabDir);
                foreach (var lnk in looseLnks)
                {
                    var dest = Path.Combine(tabDir, Path.GetFileName(lnk));
                    if (!File.Exists(dest)) File.Move(lnk, dest);
                }
            }
            catch (Exception ex) { Log.Error("materialize failed", ex); }
        }

        // Two-phase rename used by every reorder: move to temp names, then to final names. If any
        // step throws (locked folder, antivirus hold), roll the temps back so nothing is left
        // stranded under a ~ord~ name. ScanCore's RecoverTemps is the second net for a hard crash.
        private static void TwoPhaseRename(List<Tuple<string, string, string>> plan, bool isDir)
        {
            var done = new List<Tuple<string, string, string>>(); // original -> temp completed
            try
            {
                foreach (var step in plan)
                {
                    if (isDir ? !Directory.Exists(step.Item1) : !File.Exists(step.Item1)) continue;
                    if (isDir) Directory.Move(step.Item1, step.Item2); else File.Move(step.Item1, step.Item2);
                    done.Add(step);
                }
                foreach (var step in done)
                {
                    if (isDir) Directory.Move(step.Item2, step.Item3); else File.Move(step.Item2, step.Item3);
                }
            }
            catch
            {
                // Put whatever is still sitting at a temp name back where it came from.
                foreach (var step in done)
                {
                    try
                    {
                        if (isDir ? Directory.Exists(step.Item2) : File.Exists(step.Item2))
                        {
                            if (isDir) Directory.Move(step.Item2, step.Item1); else File.Move(step.Item2, step.Item1);
                        }
                    }
                    catch { /* recovery of stragglers happens in RecoverTemps */ }
                }
                throw;
            }
        }

        // ---- group display name ----------------------------------------------
        public static string DisplayName(PinnedGroup g)
        {
            if (g == null) return "";
            if (g.Tabs.Count == 1) return g.Tabs[0].Name;
            return string.Join(" / ", g.Tabs.Select(t => t.Name));
        }

        // ---- active tab ------------------------------------------------------
        public void SetActiveTab(PinnedGroup group, int index)
        {
            if (group == null) return;
            if (index < 0) index = 0;
            if (index >= group.Tabs.Count) index = group.Tabs.Count - 1;
            group.ActiveTab = index;
            // Memory-only, so this stays on the UI thread; _gate still guards the dictionary
            // because ScanCore reads it on the worker (#75).
            lock (_gate) { _activeTabs[group.FolderPath] = index; }
        }

        // ---- groups ----------------------------------------------------------
        public PinnedGroup CreateGroup(string name)
        {
            name = Sanitize(name);
            if (string.IsNullOrWhiteSpace(name)) name = "Group";
            int order = NextGroupOrder();
            var folder = Path.Combine(Root, order.ToString("00") + "_" + name);
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, "01_" + name)); // first tab
            ScanOnce();
            RaiseChanged();
            return _groups.FirstOrDefault(g => string.Equals(g.FolderPath, folder, StringComparison.OrdinalIgnoreCase));
        }

        public PinnedGroup CreateGroupAfter(PinnedGroup reference, string name)
        {
            var created = CreateGroup(name);
            if (created == null || reference == null) return created;
            var ordered = _groups.Where(g => !ReferenceEquals(g, created)).ToList();
            int idx = ordered.FindIndex(g => string.Equals(g.FolderPath, reference.FolderPath, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return created;
            ordered.Insert(idx + 1, created);
            ReorderGroups(ordered);
            return _groups.FirstOrDefault(g => string.Equals(DisplayName(g), Sanitize(name), StringComparison.OrdinalIgnoreCase)) ?? created;
        }

        public void DeleteGroup(PinnedGroup group)
        {
            if (group == null || StaleGroup(group.FolderPath)) return;
            Try(() => Recycle(group.FolderPath));
        }

        public void RenameGroup(PinnedGroup group, string newName)
        {
            if (group == null || StaleGroup(group.FolderPath)) return;
            // A single-tab group renames its tab; the folder label is kept in sync.
            if (group.Tabs.Count == 1)
            {
                RenameTab(group.Tabs[0], newName);
                RenameGroupFolder(group, newName);
            }
            else RenameGroupFolder(group, newName);
        }

        private void RenameGroupFolder(PinnedGroup group, string newName)
        {
            Try(() =>
            {
                newName = Sanitize(newName);
                if (string.IsNullOrWhiteSpace(newName)) return;
                var parent = Path.GetDirectoryName(group.FolderPath);
                var dest = Path.Combine(parent, group.Order.ToString("00") + "_" + newName);
                if (!string.Equals(dest, group.FolderPath, StringComparison.OrdinalIgnoreCase))
                    Directory.Move(group.FolderPath, dest);
            });
        }

        public void MoveGroup(PinnedGroup group, int delta)
        {
            if (group == null) return;
            var ordered = _groups.ToList();
            // Matched by path, not reference: a watcher rescan published between the click and this
            // edit running (PostEdit) replaces every group with a new object, and matching the
            // group the click captured by reference would turn the move into a silent no-op (#75).
            int idx = ordered.FindIndex(g => string.Equals(g.FolderPath, group.FolderPath, StringComparison.OrdinalIgnoreCase));
            int dest = idx + delta;
            if (idx < 0 || dest < 0 || dest >= ordered.Count) return;
            ordered.RemoveAt(idx); ordered.Insert(dest, group);
            ReorderGroups(ordered);
        }

        public void ReorderGroups(List<PinnedGroup> ordered)
        {
            // Sources come from the groups and the destination from Root, so a stale group here would
            // be moved into the new profile's folder.
            if (ordered == null || StaleGroup(ordered.Select(g => g.FolderPath).ToArray())) return;
            try
            {
                var parent = Root;
                var plan = new List<Tuple<string, string, string>>();
                for (int i = 0; i < ordered.Count; i++)
                {
                    var g = ordered[i];
                    plan.Add(Tuple.Create(g.FolderPath,
                        Path.Combine(parent, "~ord~" + Guid.NewGuid().ToString("N")),
                        Path.Combine(parent, (i + 1).ToString("00") + "_" + g.Name)));
                }
                TwoPhaseRename(plan, isDir: true);
            }
            catch (Exception ex) { Log.Error("ReorderGroups failed", ex); }
            ScanOnce(); RaiseChanged();
        }

        // Move every tab of source into dest, then delete source (drag a group onto a group).
        public void MergeGroups(PinnedGroup source, PinnedGroup dest)
        {
            if (source != null && dest != null && StaleGroup(source.FolderPath, dest.FolderPath)) return;
            Try(() =>
            {
                if (source == null || dest == null || ReferenceEquals(source, dest)) return;
                if (string.Equals(source.FolderPath, dest.FolderPath, StringComparison.OrdinalIgnoreCase)) return;
                if (!Directory.Exists(source.FolderPath)) return;
                // The tabs are read from disk now, not from the source object the drag captured: an
                // edit queued before this one (PostEdit, #75) may have moved another tab into the
                // source group since, and that tab must move with the rest instead of being lost.
                int order = NextTabOrder(dest.FolderPath);
                foreach (var tabDir in Directory.GetDirectories(source.FolderPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    int o; string name;
                    Split(Path.GetFileName(tabDir), out o, out name);
                    Directory.Move(tabDir, UniqueDir(dest.FolderPath, order++, name));
                }
                // Anything still inside, such as a legacy loose pin, goes to the Recycle Bin rather
                // than being deleted outright; an empty folder is simply removed.
                if (Directory.EnumerateFileSystemEntries(source.FolderPath).Any()) Recycle(source.FolderPath);
                else Directory.Delete(source.FolderPath, false);
            });
        }

        // ---- tabs ------------------------------------------------------------
        public PinnedTab AddTab(PinnedGroup group, string name)
        {
            if (group == null || StaleGroup(group.FolderPath)) return null;
            name = Sanitize(name);
            if (string.IsNullOrWhiteSpace(name)) name = "Tab";
            int order = NextTabOrder(group.FolderPath);
            var dir = UniqueDir(group.FolderPath, order, name);
            Directory.CreateDirectory(dir);
            ScanOnce(); RaiseChanged();
            return _groups.SelectMany(g => g.Tabs).FirstOrDefault(t => string.Equals(t.FolderPath, dir, StringComparison.OrdinalIgnoreCase));
        }

        public void RenameTab(PinnedTab tab, string newName)
        {
            if (tab == null || Stale(tab.FolderPath)) return;
            Try(() =>
            {
                newName = Sanitize(newName);
                if (string.IsNullOrWhiteSpace(newName) || tab == null) return;
                if (IsGroupFolder(tab.FolderPath)) return; // implicit empty tab, nothing on disk yet
                var parent = Path.GetDirectoryName(tab.FolderPath);
                var dest = Path.Combine(parent, tab.Order.ToString("00") + "_" + newName);
                if (!string.Equals(dest, tab.FolderPath, StringComparison.OrdinalIgnoreCase))
                    Directory.Move(tab.FolderPath, dest);
            });
        }

        public void DeleteTab(PinnedGroup group, PinnedTab tab)
        {
            if (group == null || tab == null || StaleGroup(group.FolderPath) || Stale(tab.FolderPath)) return;
            Try(() =>
            {
                if (group.Tabs.Count <= 1) { DeleteGroup(group); return; } // last tab: drop the group
                Recycle(tab.FolderPath); // same undo path as DeleteGroup, not an outright erase
            });
        }

        public void MoveTab(PinnedGroup group, PinnedTab tab, int delta)
        {
            if (group == null || tab == null) return;
            // Matched by path against the current scan, for the same reason as MoveGroup (#75): the
            // group the click captured may be an object a rescan has since replaced.
            var current = _groups.FirstOrDefault(g => string.Equals(g.FolderPath, group.FolderPath, StringComparison.OrdinalIgnoreCase));
            if (current == null) return;
            group = current;
            var ordered = group.Tabs.ToList();
            int idx = ordered.FindIndex(t => string.Equals(t.FolderPath, tab.FolderPath, StringComparison.OrdinalIgnoreCase));
            int dest = idx + delta;
            if (idx < 0 || dest < 0 || dest >= ordered.Count) return;
            ordered.RemoveAt(idx); ordered.Insert(dest, tab);
            ReorderTabs(group, ordered);
        }

        public void ReorderTabs(PinnedGroup group, List<PinnedTab> ordered)
        {
            if (group == null || ordered == null || StaleGroup(group.FolderPath)
                || Stale(ordered.Select(t => t.FolderPath).ToArray())) return;
            try
            {
                var parent = group.FolderPath;
                var plan = new List<Tuple<string, string, string>>();
                for (int i = 0; i < ordered.Count; i++)
                {
                    var t = ordered[i];
                    plan.Add(Tuple.Create(t.FolderPath,
                        Path.Combine(parent, "~ord~" + Guid.NewGuid().ToString("N")),
                        Path.Combine(parent, (i + 1).ToString("00") + "_" + t.Name)));
                }
                TwoPhaseRename(plan, isDir: true);
            }
            catch (Exception ex) { Log.Error("ReorderTabs failed", ex); }
            ScanOnce(); RaiseChanged();
        }

        // Move a tab into another group as a tab (optionally at an index).
        public void MoveTabToGroup(PinnedTab tab, PinnedGroup dest, int index = -1)
        {
            if (tab != null && dest != null && (Stale(tab.FolderPath) || StaleGroup(dest.FolderPath))) return;
            Try(() =>
            {
                if (tab == null || dest == null) return;
                int order = NextTabOrder(dest.FolderPath);
                var destPath = UniqueDir(dest.FolderPath, order, tab.Name);
                if (Directory.Exists(tab.FolderPath)) Directory.Move(tab.FolderPath, destPath);
            });
        }

        // Pull a tab out to become its own group at the end of the list (drag a tab to empty space).
        public PinnedGroup MoveTabToNewGroup(PinnedGroup source, PinnedTab tab)
        {
            PinnedGroup created = null;
            if (tab != null && Stale(tab.FolderPath)) return null;
            Try(() =>
            {
                if (tab == null) return;
                int order = NextGroupOrder();
                var groupDir = Path.Combine(Root, order.ToString("00") + "_" + tab.Name);
                Directory.CreateDirectory(groupDir);
                var tabDir = Path.Combine(groupDir, "01_" + tab.Name);
                if (Directory.Exists(tab.FolderPath)) Directory.Move(tab.FolderPath, tabDir);
            });
            return created;
        }

        // ---- pins ------------------------------------------------------------
        // Pins may target folders or files; both are stored as ordinary .lnk shortcuts.
        public void AddPin(PinnedGroup group, string targetPath)
        {
            if (group == null) return;
            AddPin(group.Active, targetPath);
        }

        public void AddPin(PinnedTab tab, string targetPath)
        {
            AddPins(tab, new[] { targetPath });
        }

        /// <summary>Pin several targets at once with a single rescan at the end.</summary>
        public void AddPins(PinnedTab tab, System.Collections.Generic.IEnumerable<string> targetPaths)
        {
            try
            {
                if (tab == null || targetPaths == null || Stale(tab.FolderPath)) return;
                var dir = EnsureTabDir(tab); // an implicit empty tab gets a real subfolder
                int order = NextPinOrder(dir);
                bool any = false;
                foreach (var targetPath in targetPaths)
                {
                    if (string.IsNullOrEmpty(targetPath)) continue;
                    string leaf = SafeLeaf(targetPath);
                    string lnk = UniqueLink(dir, order++, leaf);
                    ShellLink.Create(lnk, targetPath, leaf);
                    any = true;
                }
                if (any) { ScanOnce(); RaiseChanged(); }
            }
            catch (Exception ex) { Log.Error("AddPin failed", ex); }
        }

        // Pin a target into a tab at a specific position, so a folder dropped on a line lands there.
        public void AddPinAtIndex(PinnedTab tab, string targetPath, int index)
        {
            try
            {
                if (tab == null || string.IsNullOrEmpty(targetPath) || Stale(tab.FolderPath)) return;
                var dir = EnsureTabDir(tab);
                string leaf = SafeLeaf(targetPath);
                string tmpNew = Path.Combine(dir, "~new~" + Guid.NewGuid().ToString("N") + ".lnk");
                ShellLink.Create(tmpNew, targetPath, leaf);
                InsertPinAt(dir, tmpNew, leaf, index);

                ScanOnce(); RaiseChanged();
            }
            catch (Exception ex) { Log.Error("AddPinAtIndex failed", ex); }
        }

        // Put a new shortcut, already inside dir under a temp name, at a position in the order the
        // tab shows on screen, and write that order into the file names. A tab that was sorted
        // loses its marker, because a pin placed at a line is a manual order (#241).
        private static void InsertPinAt(string dir, string newLink, string newName, int index)
        {
            PinSortMode mode;
            var existing = ReadPinFiles(dir, out mode)
                .Where(p => !string.Equals(p.LinkPath, newLink, StringComparison.OrdinalIgnoreCase));
            var order = PinSorter.Order(existing, mode)
                .Select(p => Tuple.Create(p.LinkPath, p.DisplayName)).ToList();
            if (index < 0) index = 0;
            if (index > order.Count) index = order.Count;
            order.Insert(index, Tuple.Create(newLink, newName));

            var plan = new List<Tuple<string, string, string>>();
            for (int i = 0; i < order.Count; i++)
            {
                plan.Add(Tuple.Create(order[i].Item1,
                    Path.Combine(dir, "~ord~" + Guid.NewGuid().ToString("N") + ".lnk"),
                    Path.Combine(dir, (i + 1).ToString("000") + "_" + order[i].Item2 + ".lnk")));
            }
            TwoPhaseRename(plan, isDir: false);
            if (mode != PinSortMode.Manual) DeleteMarker(dir);
        }

        /// <summary>Move a pin into another tab at a position on screen, as a pin dragged from a
        /// different tab and dropped at a line. The destination keeps no sort marker afterwards.</summary>
        public void MovePinToTabAt(PinnedFolder folder, PinnedTab dest, int index)
        {
            if (folder == null || dest == null || Stale(folder.LinkPath, dest.FolderPath)) return;
            Try(() =>
            {
                var dir = EnsureTabDir(dest);
                var tmpNew = Path.Combine(dir, "~new~" + Guid.NewGuid().ToString("N") + ".lnk");
                File.Move(folder.LinkPath, tmpNew);
                InsertMovedPinAt(dir, tmpNew, folder.LinkPath, folder.DisplayName, index);
            });
        }

        // InsertPinAt rolls its renames back to the ~new~ name, and the next scan would turn that into
        // 999_Recovered and lose the pin's name and tab. So a failure puts the pin back where it came
        // from before it rethrows.
        internal static void InsertMovedPinAt(string dir, string tmpNew, string originalLink, string name, int index)
        {
            try { InsertPinAt(dir, tmpNew, name, index); }
            catch
            {
                try { if (File.Exists(tmpNew) && !File.Exists(originalLink)) File.Move(tmpNew, originalLink); }
                catch (Exception ex) { Log.Error("put back a pin after a failed move", ex); }
                throw;
            }
        }

        public void RemovePin(PinnedFolder folder)
        {
            if (folder == null || Stale(folder.LinkPath)) return;
            Try(() => { if (File.Exists(folder.LinkPath)) File.Delete(folder.LinkPath); });
        }

        public void RenamePin(PinnedFolder folder, string newName)
        {
            if (folder == null || Stale(folder.LinkPath)) return;
            Try(() =>
            {
                newName = Sanitize(newName);
                if (string.IsNullOrWhiteSpace(newName)) return;
                var dir = Path.GetDirectoryName(folder.LinkPath);
                var dest = Path.Combine(dir, folder.Order.ToString("000") + "_" + newName + ".lnk");
                if (!string.Equals(dest, folder.LinkPath, StringComparison.OrdinalIgnoreCase))
                    File.Move(folder.LinkPath, dest);
            });
        }

        public void MovePinToTab(PinnedFolder folder, PinnedTab dest)
        {
            if (folder == null || (dest != null && Stale(folder.LinkPath, dest.FolderPath))) return;
            Try(() =>
            {
                if (dest == null) return;
                var dir = EnsureTabDir(dest);
                int order = NextPinOrder(dir);
                var target = UniqueLink(dir, order, folder.DisplayName);
                File.Move(folder.LinkPath, target);
            });
        }

        public void MovePinToGroup(PinnedFolder folder, PinnedGroup dest)
        {
            if (dest != null) MovePinToTab(folder, dest.Active);
        }

        public void MovePin(PinnedTab tab, PinnedFolder folder, int delta)
        {
            if (tab == null || folder == null) return;
            // Matched by path against the current scan, for the same reason as MoveGroup (#75).
            var current = _groups.SelectMany(g => g.Tabs)
                .FirstOrDefault(t => string.Equals(t.FolderPath, tab.FolderPath, StringComparison.OrdinalIgnoreCase));
            if (current == null) return;
            tab = current;
            var ordered = tab.Items.ToList();
            int idx = ordered.FindIndex(p => string.Equals(p.LinkPath, folder.LinkPath, StringComparison.OrdinalIgnoreCase));
            int dest = idx + delta;
            if (idx < 0 || dest < 0 || dest >= ordered.Count) return;
            ordered.RemoveAt(idx); ordered.Insert(dest, folder);
            ReorderPins(tab, ordered);
        }

        public void ReorderPins(PinnedTab tab, List<PinnedFolder> ordered)
        {
            if (tab == null || ordered == null || Stale(tab.FolderPath)
                || Stale(ordered.Select(p => p.LinkPath).ToArray())) return;
            try
            {
                var dir = tab.FolderPath;
                var plan = new List<Tuple<string, string, string>>();
                for (int i = 0; i < ordered.Count; i++)
                {
                    var p = ordered[i];
                    plan.Add(Tuple.Create(p.LinkPath,
                        Path.Combine(dir, "~ord~" + Guid.NewGuid().ToString("N") + ".lnk"),
                        Path.Combine(dir, (i + 1).ToString("000") + "_" + p.DisplayName + ".lnk")));
                }
                TwoPhaseRename(plan, isDir: false);
                // A manual order replaces any sort mode. Only after the rename succeeded, so a
                // failed reorder leaves the tab sorted as it was.
                DeleteMarker(dir);
            }
            catch (Exception ex) { Log.Error("ReorderPins failed", ex); }
            ScanOnce(); RaiseChanged();
        }

        // The tab as the latest scan holds it, matched by path for the same reason as MovePin (#75).
        private PinnedTab CurrentTab(PinnedTab tab)
        {
            return _groups.SelectMany(g => g.Tabs).FirstOrDefault(t =>
                string.Equals(t.FolderPath, tab.FolderPath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Set how a tab orders its pins (#241). Name and Recent write the marker and leave
        /// the file names alone. Manual freezes the order on screen into the file names first, so
        /// nothing visibly moves when keep-sorted turns off.</summary>
        public void SetPinSort(PinnedTab tab, PinSortMode mode)
        {
            if (tab == null || Stale(tab.FolderPath)) return;
            try
            {
                if (mode == PinSortMode.Manual)
                {
                    if (IsGroupFolder(tab.FolderPath)) return; // implicit tab: nothing on disk to clear
                    var current = CurrentTab(tab);
                    if (current == null || current.SortMode == PinSortMode.Manual) return;
                    ReorderPins(current, current.Items.ToList()); // clears the marker and scans
                    return;
                }
                var dir = EnsureTabDir(tab);
                File.WriteAllText(MarkerPath(dir), PinSorter.MarkerText(mode));
                ScanOnce(); RaiseChanged();
            }
            catch (Exception ex) { Log.Error("SetPinSort failed", ex); }
        }

        /// <summary>Write the A-to-Z order into the file names and leave the tab in Manual.</summary>
        public void SortPinsOnce(PinnedTab tab)
        {
            if (tab == null || Stale(tab.FolderPath)) return;
            var current = CurrentTab(tab);
            if (current == null || IsGroupFolder(current.FolderPath)) return;
            ReorderPins(current, PinSorter.Order(current.Items, PinSortMode.Name));
        }

        // ---- cross-profile group operations ----------------------------------
        // The settings page shows one column per profile. These read and move/copy group folders between
        // each profile's own groups root, identified by its expanded folder path.

        /// <summary>List (folderPath, displayName) of the groups under an expanded root, in order.</summary>
        public static List<Tuple<string, string>> ListGroups(string expandedRoot)
        {
            var list = new List<Tuple<string, string>>();
            try
            {
                if (!Directory.Exists(expandedRoot)) return list;
                var items = new List<Tuple<int, string, string>>();
                foreach (var d in Directory.GetDirectories(expandedRoot))
                {
                    int o; string n; Split(Path.GetFileName(d), out o, out n);
                    items.Add(Tuple.Create(o, d, n));
                }
                foreach (var it in items.OrderBy(x => x.Item1).ThenBy(x => x.Item3, StringComparer.CurrentCultureIgnoreCase))
                    list.Add(Tuple.Create(it.Item2, it.Item3));
            }
            catch (Exception ex) { Log.Error("ListGroups failed", ex); }
            return list;
        }

        /// <summary>Create a new group folder (with a first tab) under any expanded root.</summary>
        public static void CreateGroupFolder(string expandedRoot, string name)
        {
            try
            {
                name = Sanitize(name);
                if (string.IsNullOrWhiteSpace(name)) name = "Group";
                Directory.CreateDirectory(expandedRoot);
                var folder = UniqueDir(expandedRoot, NextOrderInRoot(expandedRoot), name);
                Directory.CreateDirectory(folder);
                Directory.CreateDirectory(Path.Combine(folder, "01_" + name)); // first tab
            }
            catch (Exception ex) { Log.Error("CreateGroupFolder failed", ex); }
        }

        /// <summary>Delete a group folder and its contents.</summary>
        public static void DeleteGroupFolder(string folder)
        {
            Recycle(folder);
        }

        /// <summary>
        /// Send a group's folder to the Recycle Bin rather than erasing it. Deleting a group takes every
        /// pin in it with it, so the delete has to be one a person can take back from the desktop the way
        /// they take back any other deleted folder.
        /// </summary>
        internal static void Recycle(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

                // SHFileOperation reads pFrom as a double-null-terminated list.
                var from = folder + "\0\0";
                IntPtr buffer = Marshal.StringToHGlobalUni(from);
                try
                {
                    var op = new NM.SHFILEOPSTRUCT
                    {
                        wFunc = NM.FO_DELETE,
                        pFrom = buffer,
                        fFlags = (ushort)(NM.FOF_ALLOWUNDO | NM.FOF_NOCONFIRMATION | NM.FOF_NOERRORUI | NM.FOF_SILENT)
                    };
                    int rc = NM.SHFileOperation(ref op);
                    if (rc != 0)
                    {
                        Log.Info("the shell refused to recycle \"" + folder + "\" (code " + rc + "); deleting it instead.");
                        Directory.Delete(folder, true);
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            catch (Exception ex) { Log.Error("delete group folder '" + folder + "'", ex); }
        }

        /// <summary>Move a group folder into another profile's groups root (drag between columns).</summary>
        public static void MoveGroupFolder(string groupFolder, string destExpandedRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(groupFolder) || !Directory.Exists(groupFolder)) return;
                Directory.CreateDirectory(destExpandedRoot);
                if (string.Equals(Path.GetDirectoryName(groupFolder), destExpandedRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                int o; string name; Split(Path.GetFileName(groupFolder), out o, out name);
                var dest = UniqueDir(destExpandedRoot, NextOrderInRoot(destExpandedRoot), name);
                Directory.Move(groupFolder, dest);
            }
            catch (Exception ex) { Log.Error("MoveGroupFolder failed", ex); }
        }

        /// <summary>Copy a group folder into another profile's groups root (Ctrl-drag between columns).</summary>
        public static void CopyGroupFolder(string groupFolder, string destExpandedRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(groupFolder) || !Directory.Exists(groupFolder)) return;
                Directory.CreateDirectory(destExpandedRoot);
                int o; string name; Split(Path.GetFileName(groupFolder), out o, out name);
                var dest = UniqueDir(destExpandedRoot, NextOrderInRoot(destExpandedRoot), name);
                CopyDir(groupFolder, dest);
            }
            catch (Exception ex) { Log.Error("CopyGroupFolder failed", ex); }
        }

        /// <summary>Reorder the group folders under one profile's expanded root (drag-to-reorder within a
        /// profile's own group list in the settings page). Static and root-based like the other
        /// cross-profile helpers above, so it works on any profile column, active or not.</summary>
        public static void ReorderGroupFolders(string expandedRoot, List<string> orderedFolders)
        {
            try
            {
                var plan = new List<Tuple<string, string, string>>();
                for (int i = 0; i < orderedFolders.Count; i++)
                {
                    int o; string name; Split(Path.GetFileName(orderedFolders[i]), out o, out name);
                    plan.Add(Tuple.Create(orderedFolders[i],
                        Path.Combine(expandedRoot, "~ord~" + Guid.NewGuid().ToString("N")),
                        Path.Combine(expandedRoot, (i + 1).ToString("00") + "_" + name)));
                }
                TwoPhaseRename(plan, isDir: true);
            }
            catch (Exception ex) { Log.Error("ReorderGroupFolders failed", ex); }
        }

        private static int NextOrderInRoot(string root)
        {
            int max = 0;
            try
            {
                foreach (var d in Directory.GetDirectories(root))
                {
                    int o; string n; Split(Path.GetFileName(d), out o, out n);
                    if (o != int.MaxValue && o > max) max = o;
                }
            }
            catch { }
            return max + 1;
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                var copy = Path.Combine(dst, Path.GetFileName(f));
                File.Copy(f, copy, true);
                // A copy gets a new creation time, which would reorder a tab kept in Recent mode.
                try { File.SetCreationTimeUtc(copy, File.GetCreationTimeUtc(f)); }
                catch (Exception ex) { Log.Error("keep creation time of a copied file", ex); }
            }
            foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        // ---- helpers ---------------------------------------------------------
        private bool IsGroupFolder(string path)
        {
            try { return string.Equals(Path.GetDirectoryName(path), Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private int NextGroupOrder()
        {
            int max = 0; foreach (var g in _groups) if (g.Order > max && g.Order != int.MaxValue) max = g.Order; return max + 1;
        }

        private static int NextTabOrder(string groupDir)
        {
            int max = 0;
            foreach (var sub in Directory.GetDirectories(groupDir))
            {
                int o; string n; Split(Path.GetFileName(sub), out o, out n);
                if (o != int.MaxValue && o > max) max = o;
            }
            return max + 1;
        }

        private static int NextPinOrder(string tabDir)
        {
            int max = 0;
            foreach (var lnk in Directory.GetFiles(tabDir, "*.lnk"))
            {
                int o; string n; Split(Path.GetFileNameWithoutExtension(lnk), out o, out n);
                if (o != int.MaxValue && o > max) max = o;
            }
            return max + 1;
        }

        private static string UniqueDir(string parent, int order, string name)
        {
            name = Sanitize(name);
            var baseName = order.ToString("00") + "_" + name;
            var path = Path.Combine(parent, baseName);
            int n = 1;
            while (Directory.Exists(path)) path = Path.Combine(parent, baseName + " (" + (++n) + ")");
            return path;
        }

        private static string UniqueLink(string folder, int order, string leaf)
        {
            leaf = Sanitize(leaf);
            var baseName = order.ToString("000") + "_" + leaf;
            var path = Path.Combine(folder, baseName + ".lnk");
            int n = 1;
            while (File.Exists(path)) path = Path.Combine(folder, baseName + " (" + (++n) + ").lnk");
            return path;
        }

        private static string SafeLeaf(string targetPath)
        {
            try
            {
                var trimmed = targetPath.TrimEnd('\\', '/');
                var leaf = Path.GetFileName(trimmed);
                if (string.IsNullOrEmpty(leaf)) leaf = trimmed;
                return string.IsNullOrEmpty(leaf) ? "Folder" : leaf;
            }
            catch { return "Folder"; }
        }

        private static void Split(string raw, out int order, out string name)
        {
            var m = Prefix.Match(raw);
            if (m.Success && int.TryParse(m.Groups[1].Value, out order)) { name = m.Groups[2].Value; return; }
            order = int.MaxValue; name = raw;
        }

        private static string Sanitize(string name)
        {
            if (name == null) return null;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), string.Empty);
            return name.Trim();
        }

        private void Try(Action action)
        {
            try { action(); ScanOnce(); RaiseChanged(); }
            catch (Exception ex) { Log.Error("group mutation failed", ex); }
        }

        // ---- watcher ---------------------------------------------------------
        private void InstallWatcher()
        {
            try
            {
                _watcher?.Dispose();
                Directory.CreateDirectory(Root);
                _watcher = new FileSystemWatcher(Root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                };
                _watcher.Created += OnFsEvent; _watcher.Deleted += OnFsEvent;
                _watcher.Renamed += OnFsEvent; _watcher.Changed += OnFsEvent;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Error("group watcher install failed", ex); }
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            if (!TriggersRescan(e.ChangeType, e.FullPath)) return;
            // Scan on the worker, not on the watcher's threadpool callback, so the timer thread is
            // never tied up by a slow disk and the scan is serialized with everything else. _debounce
            // used to be created lazily with no lock, a small race between threadpool callbacks; the
            // lock here closes that. _rescan merges every timer fire that lands while a scan is already
            // queued or running into the one about to run (#77).
            lock (_gate)
            {
                if (_debounce == null)
                    _debounce = new Timer(_ => _rescan.Request(), null, Timeout.Infinite, Timeout.Infinite);
            }
            _debounce.Change(250, Timeout.Infinite);
        }

        /// <summary>Whether a watcher event can change the groups. Creates, deletes, and renames always
        /// can. A write-time change matters only for a .lnk, the one kind of file the scan reads: that
        /// is how a pin edited outside PowerPane, for example in its Properties dialog, is picked up.
        /// Write-time changes on folders, which fire whenever a file inside is added or removed, and on
        /// files such as desktop.ini or Thumbs.db each set off a full rescan of every pin for nothing
        /// (prisant-labs/windows-explorer-powerpane#49).</summary>
        internal static bool TriggersRescan(WatcherChangeTypes change, string path)
        {
            if (change != WatcherChangeTypes.Changed) return true;
            return path != null && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            try { _watcher?.Dispose(); } catch { }
            try { _debounce?.Dispose(); } catch { }
        }
    }
}
