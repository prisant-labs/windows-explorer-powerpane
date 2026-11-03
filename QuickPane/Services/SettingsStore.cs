using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using QuickPane.Models;

namespace QuickPane.Services
{
    /// <summary>
    /// Loads and saves settings.json. Writes are atomic (temp file then replace) so a crash or
    /// Explorer close mid-write can never leave a truncated file. Raises Changed after every save
    /// so open sidebars can react live with no Apply button.
    ///
    /// Save() serializes on the calling thread and writes the file on a work lane. The caller is the
    /// UI thread, which in inside mode shares Explorer's input queue, so a write to a slow or
    /// redirected AppData folder used to freeze the Settings window and could stall Explorer too
    /// (prisant-labs/windows-explorer-quickpane#163). Saves that queue up while a write is pending
    /// collapse into one write of the newest bytes. Anything that reads settings.json from disk must
    /// call <see cref="Flush()"/> first.
    /// </summary>
    public sealed class SettingsStore
    {
        public event EventHandler Changed;

        // Raised when the active profile is switched, added, removed, or renamed, so the app can re-point
        // the group store at the new folder and refresh.
        public event EventHandler ProfilesChanged;

        // Raised continuously while the user drags the pane edge, so every sidebar can relayout live
        // without writing settings.json on each pixel. The final value is persisted on drag release.
        public event EventHandler WidthChangedLive;

        public AppSettings Current { get; private set; }

        public void NotifyWidthLive(int widthPx)
        {
            if (widthPx < 160) widthPx = 160;
            if (widthPx > 400) widthPx = 400;
            Current.SidebarWidthPx = widthPx;
            WidthChangedLive?.Invoke(this, EventArgs.Empty);
        }

        private readonly string _dir;
        private readonly string _file;

        /// <summary>How long <see cref="Flush()"/> waits by default. Long enough for a slow disk, short
        /// enough that exit, session end, and a backup never hang on a folder that does not answer.</summary>
        internal static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(3);

        // ---- deferred write (#163) ---------------------------------------------
        private readonly Action<string, Exception> _onError;
        private readonly CoalescedPost _writer;
        // Guards the three fields below. Save() swaps in new bytes under it, and the write job takes
        // them under it, so a job always writes the newest bytes, never the ones it was queued for.
        private readonly object _writeGate = new object();
        // Held for a whole write job, so the inline write in Load() and a lane write can never both
        // be writing settings.json.tmp at once.
        private readonly object _ioGate = new object();
        // Set when the newest Save() has reached the disk (or failed to), which is what Flush waits on.
        private readonly ManualResetEventSlim _idle = new ManualResetEventSlim(true);
        private byte[] _pendingBytes;
        private long _generation;
        private bool _lastWriteFailed;

        public SettingsStore()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickPane"),
                   WorkQueue.PostSettingsWrite, null)
        {
        }

        /// <summary>Points the store at an arbitrary folder instead of the real %APPDATA%\QuickPane.
        /// Exists so tests can round-trip settings in a temp directory without touching the user's own
        /// settings file. Writes run inline, so a Save() is on disk when it returns and a test that
        /// saves and then reloads needs no Flush.</summary>
        internal SettingsStore(string dir) : this(dir, null, null)
        {
        }

        /// <summary>Test seam for the deferred write. <paramref name="postWrite"/> queues the write
        /// job, and null runs it inline on the caller. <paramref name="onError"/> receives write
        /// failures and Flush timeouts, and defaults to <see cref="Log.Error(string, Exception)"/>, so
        /// a test that makes a write fail can keep that failure out of the real log.</summary>
        internal SettingsStore(string dir, Action<Action> postWrite, Action<string, Exception> onError)
        {
            _dir = dir;
            _file = Path.Combine(_dir, "settings.json");
            _onError = onError ?? Log.Error;
            // CoalescedPost keeps at most one write job queued behind the running one, however many
            // saves arrive meanwhile, and the job reads the newest bytes when it runs: last write wins.
            _writer = new CoalescedPost(WritePending, postWrite ?? (work => work()));
        }

        /// <summary>Fully expanded groups folder path (environment tokens resolved at read time).</summary>
        public string ExpandedGroupsPath
        {
            get { return Environment.ExpandEnvironmentVariables(Current.GroupsPath); }
        }

        public void Load()
        {
            try
            {
                Directory.CreateDirectory(_dir);
                if (File.Exists(_file))
                {
                    using (var fs = File.OpenRead(_file))
                    {
                        var ser = new DataContractJsonSerializer(typeof(AppSettings));
                        Current = (AppSettings)ser.ReadObject(fs);
                    }
                }
                if (Current == null) Current = AppSettings.CreateDefault();
                Current.Normalize();
                Current.LoadActiveToTop(); // the active profile is the source of truth at load
            }
            catch (Exception ex)
            {
                Log.Error("settings load failed, using defaults", ex);
                Current = AppSettings.CreateDefault();
                Current.Normalize();
                Current.LoadActiveToTop();
            }

            // The groups folder is not created here. Load runs on the UI thread before the tray icon
            // exists, and a groups folder on a share that does not answer held startup for the network
            // timeout. GroupStore creates it on the worker when it scans
            // (prisant-labs/windows-explorer-quickpane#70).

            // Materialize defaults with an inline write, not a queued one. Load already touches the
            // disk here, and pin mode (App.RunPinMode) builds a short-lived store of its own that the
            // exit Flush never sees, so a queued write could be lost when that process ends (#163).
            if (!File.Exists(_file)) Save(true);
        }

        /// <summary>Persist <see cref="Current"/>. Returns once the settings are serialized; the file
        /// itself is written on the settings lane (#163). Never throws: a failed write is logged.</summary>
        public void Save() { Save(false); }

        private void Save(bool inline)
        {
            byte[] bytes;
            try
            {
                // Everything that reads or changes Current stays on the calling thread, which owns it.
                Current.SyncTopToActive(); // persist live top-level edits into the active profile first
                Current.Normalize();
                using (var ms = new MemoryStream())
                {
                    var settings = new DataContractJsonSerializerSettings { };
                    var ser = new DataContractJsonSerializer(typeof(AppSettings), settings);
                    ser.WriteObject(ms, Current);
                    bytes = Indent(ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                _onError("settings save failed", ex);
                return;
            }

            lock (_writeGate)
            {
                _pendingBytes = bytes;
                _generation++;
                _idle.Reset();
            }
            if (inline) WritePending();
            else _writer.Request();

            // Changed fires here, on the calling thread, once Current is serialized and before the
            // bytes reach the disk (#163). Every subscriber needs the UI thread and none needs the
            // file: App's handler reloads groups inline and re-applies the pane color, and the panes,
            // the dock, the follower, and the dialog watcher relayout or rebuild from Current. Each
            // one reads Current, never settings.json. A write that later fails has still raised
            // Changed, which matches the session: the change is live, and the log says it was not
            // persisted.
            try { Changed?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { _onError("settings changed handler failed", ex); }
        }

        /// <summary>The write job. Takes the newest bytes, so a job queued for an older save writes
        /// the newer one instead, and a job that finds nothing left to write only marks the store
        /// idle. Runs on the settings lane, or inline from Load(true) and the test constructor.</summary>
        private void WritePending()
        {
            lock (_ioGate)
            {
                byte[] bytes;
                long gen;
                lock (_writeGate)
                {
                    bytes = _pendingBytes;
                    gen = _generation;
                    _pendingBytes = null;
                }

                bool failed = false;
                try
                {
                    if (bytes != null) WriteFile(bytes);
                }
                catch (Exception ex)
                {
                    failed = true;
                    _onError("settings save failed", ex);
                }
                finally
                {
                    // Set idle even after a failure, or every later Flush would wait out its timeout.
                    // A newer save that arrived mid-write keeps it reset until its own job runs.
                    lock (_writeGate)
                    {
                        if (bytes != null) _lastWriteFailed = failed;
                        if (gen == _generation) _idle.Set();
                    }
                }
            }
        }

        private void WriteFile(byte[] bytes)
        {
            Directory.CreateDirectory(_dir);
            var tmp = _file + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(_file)) File.Replace(tmp, _file, null);
            else File.Move(tmp, _file);
        }

        /// <summary>Wait up to <see cref="FlushTimeout"/> for the newest Save() to reach settings.json.
        /// Returns false, and logs, if the write is still pending when the wait ends.</summary>
        public bool Flush() { return Flush(FlushTimeout); }

        /// <summary>Wait up to <paramref name="timeout"/> for the newest Save() to reach settings.json
        /// (#163). A failed write counts as finished. Never call this from a job on the settings lane,
        /// which would wait for itself.</summary>
        public bool Flush(TimeSpan timeout)
        {
            if (_idle.Wait(timeout)) return true;
            _onError("settings write still pending after " + (long)timeout.TotalMilliseconds + " ms",
                new TimeoutException("settings.json write did not finish in time"));
            return false;
        }

        // ---- backup / restore -----------------------------------------------
        public void ExportTo(string path)
        {
            Save();
            // The copy reads settings.json from disk, so it waits for the write that Save just queued.
            // Copying early would back up the previous file and report success (#163). Throwing hands
            // BackupPage a "Backup failed" message instead of a stale backup.
            if (!Flush()) throw new IOException("Settings are still being written. Try again in a moment.");
            bool failed;
            lock (_writeGate) failed = _lastWriteFailed;
            if (failed) throw new IOException("Settings could not be written. See debug.log in the log folder.");
            File.Copy(_file, path, true);
        }

        public bool ImportFrom(string path)
        {
            try
            {
                AppSettings loaded;
                using (var fs = File.OpenRead(path))
                {
                    var ser = new DataContractJsonSerializer(typeof(AppSettings));
                    loaded = (AppSettings)ser.ReadObject(fs);
                }
                if (loaded == null) return false;
                Current = loaded;
                Current.Normalize();
                Current.LoadActiveToTop();
                // No Flush first. This Save bumps the generation and replaces the pending bytes, so a
                // write still queued from an older save writes the restored settings instead, and a
                // write already under way finishes before the follow-up job writes them, because the
                // lane runs one write at a time (#163). The restore never waits on the disk.
                Save();
                ProfilesChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex) { Log.Error("settings import failed", ex); return false; }
        }

        // ---- profiles --------------------------------------------------------

        /// <summary>Expanded groups folder for any profile.</summary>
        public string ExpandedGroupsPathFor(Profile p)
        {
            return Environment.ExpandEnvironmentVariables(p != null ? p.GroupsPath : Current.GroupsPath);
        }

        public void SwitchProfile(int index)
        {
            if (Current.Profiles == null || index < 0 || index >= Current.Profiles.Count) return;
            if (index == Current.ActiveProfileIndex) return;
            Current.SyncTopToActive();          // keep the profile we are leaving up to date
            Current.ActiveProfileIndex = index;
            Current.LoadActiveToTop();          // bring the new profile's values forward
            Save();
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }

        public Profile AddProfile(string name)
        {
            if (Current.Profiles == null) Current.Profiles = new System.Collections.Generic.List<Profile>();
            if (string.IsNullOrWhiteSpace(name)) name = "Profile " + (Current.Profiles.Count + 1);
            // Each new profile gets its own groups folder so it starts independent.
            string safe = name.Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c.ToString(), string.Empty);
            if (string.IsNullOrWhiteSpace(safe)) safe = "Profile" + (Current.Profiles.Count + 1);
            var p = new Profile
            {
                Name = name.Trim(),
                GroupsPath = @"%APPDATA%\QuickPane\Profiles\" + safe + @"\Groups",
                RecentsMaxCount = 15,
                SidebarWidthPx = 220
            };
            Current.Profiles.Add(p);
            Save();
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
            return p;
        }

        public void RemoveProfile(int index)
        {
            if (Current.Profiles == null || Current.Profiles.Count <= 1) return; // always keep one
            if (index < 0 || index >= Current.Profiles.Count) return;
            Current.Profiles.RemoveAt(index);
            if (Current.ActiveProfileIndex >= Current.Profiles.Count) Current.ActiveProfileIndex = Current.Profiles.Count - 1;
            Current.LoadActiveToTop();
            Save();
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RenameProfile(int index, string name)
        {
            if (Current.Profiles == null || index < 0 || index >= Current.Profiles.Count) return;
            if (string.IsNullOrWhiteSpace(name)) return;
            Current.Profiles[index].Name = name.Trim();
            Save();
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }

        // DataContractJsonSerializer emits compact JSON. A light indent keeps the file
        // hand-editable, which matters because groupsPath is meant to be user editable.
        private static byte[] Indent(byte[] compact)
        {
            try
            {
                var s = Encoding.UTF8.GetString(compact);
                var sb = new StringBuilder();
                int depth = 0;
                bool inStr = false;
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '"' && (i == 0 || s[i - 1] != '\\')) inStr = !inStr;
                    if (inStr) { sb.Append(c); continue; }
                    switch (c)
                    {
                        case '{':
                        case '[':
                            sb.Append(c).Append('\n').Append(new string(' ', ++depth * 2));
                            break;
                        case '}':
                        case ']':
                            sb.Append('\n').Append(new string(' ', --depth * 2)).Append(c);
                            break;
                        case ',':
                            sb.Append(c).Append('\n').Append(new string(' ', depth * 2));
                            break;
                        case ':':
                            sb.Append(": ");
                            break;
                        default:
                            sb.Append(c);
                            break;
                    }
                }
                return Encoding.UTF8.GetBytes(sb.ToString());
            }
            catch
            {
                return compact;
            }
        }
    }
}
