using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerPane.Services
{
    /// <summary>
    /// Cached, background-probed path facts (exists / file-vs-folder). The UI reads only this cache
    /// and never touches the disk itself, because probing a dead network target can block for many
    /// seconds and the UI thread shares Explorer's input queue. A path that has never been probed is
    /// reported optimistically (exists, folder unless the leaf has an extension); the probe runs on
    /// the worker and, whenever a result differs from what is cached, a coalesced Changed event is
    /// raised on the UI thread so the sections can re-render with the truth.
    /// </summary>
    internal static class PathStatus
    {
        /// <summary>Raised on the UI thread, coalesced to at most one call per ~300 ms, after a probe
        /// result changed something a row shows: a path the cache had reported alive was gone, or the
        /// other way round, or a file turned out to be a folder. Also after the first probe of a path a
        /// <see cref="ConfirmedAlive"/> reader asked about, since that reader waits for it. Sections
        /// rebuild on it, so a first probe that only confirms what the cache already said must not
        /// raise it: an expanded pin on a share would otherwise re-enumerate its children once per
        /// child probe (#139 review).</summary>
        public static event Action Changed;

        /// <summary>Raised on the UI thread, coalesced with <see cref="Changed"/>, after any path's
        /// first probe completed, whatever it found. For a waiter on one path's answer, such as a
        /// parked icon extraction (#139), rather than for rebuilding a section.</summary>
        public static event Action Probed;

        /// <summary>How long a check made while the user waits, such as before a picker opens, may take.
        /// A local folder answers in microseconds; a share that does not answer would otherwise hold
        /// the click for the network timeout.</summary>
        internal const int InteractiveProbeMs = 1000;

        /// <summary>Whether a folder exists, giving up after timeoutMs. For the few places that must
        /// know before they can continue, such as choosing a picker's start folder. The check runs on
        /// a thread-pool thread, so an unreachable share costs the caller at most the timeout, and the
        /// abandoned check finishes on its own. timedOut tells "not there" apart from "not answering"
        /// (prisant-labs/windows-explorer-powerpane#41).</summary>
        internal static bool FolderExistsWithin(string path, int timeoutMs, out bool timedOut,
            Func<string, bool> exists = null)
        {
            timedOut = false;
            if (string.IsNullOrEmpty(path)) return false;
            var check = exists ?? Directory.Exists;
            try
            {
                var task = System.Threading.Tasks.Task.Run(() => check(path));
                if (!task.Wait(timeoutMs)) { timedOut = true; return false; }
                return task.Result;
            }
            catch { return false; }
        }

        /// <summary>Whether any of sources is a folder, giving up after timeoutMs. Classifying a
        /// drop used to call Directory.Exists in a loop straight on the UI thread; one source on an
        /// unreachable share cost the network timeout, right on the thread Explorer shares input
        /// with (prisant-labs/windows-explorer-powerpane#75). A timeout reads as "contains a
        /// folder": the caller then offers the ambiguous-drop menu rather than moving silently what
        /// might have been a folder.</summary>
        internal static bool AnyFolderWithin(string[] sources, int timeoutMs, out bool timedOut,
            Func<string, bool> exists = null)
        {
            timedOut = false;
            if (sources == null || sources.Length == 0) return false;
            var check = exists ?? Directory.Exists;
            try
            {
                var task = System.Threading.Tasks.Task.Run(() =>
                    sources.Any(s => { try { return check(s); } catch { return false; } }));
                if (!task.Wait(timeoutMs)) { timedOut = true; return true; }
                return task.Result;
            }
            catch { return false; }
        }

        private sealed class Entry
        {
            public bool Exists;
            public bool IsFile;
            public bool Probed;
            public bool InFlight;
            public bool NotifyOnProbe; // a ConfirmedAlive reader waits on the first probe
            public int Tick;      // completion time of the last probe
        }

        private const int TtlMs = 15000;
        private static readonly object Gate = new object();
        // Capped, because it held every path any row had ever shown for the life of the process, and
        // PowerPane runs for days (prisant-labs/windows-explorer-powerpane#48). An evicted path reads
        // as never probed: optimistic, with a fresh probe queued, which is what a new path gets.
        private const int MaxEntries = 4096;
        private static readonly LruCache<string, Entry> Map =
            new LruCache<string, Entry>(MaxEntries, StringComparer.OrdinalIgnoreCase);
        private static bool _raiseQueued;

        /// <summary>Cached existence; optimistic true for never-probed paths. Queues a probe.</summary>
        public static bool IsAlive(string path)
        {
            var e = Lookup(path);
            return e == null || e.Exists;
        }

        /// <summary>Cached file-vs-folder; extension guess for never-probed paths. Queues a probe.</summary>
        public static bool IsFile(string path)
        {
            var e = Lookup(path);
            if (e == null) return false;
            return e.Probed ? e.IsFile : GuessIsFile(path);
        }

        /// <summary>True only when a probe has completed and found the path alive. Used to gate
        /// operations that would themselves hit the disk (real shell icon extraction).</summary>
        public static bool ConfirmedAlive(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            lock (Gate)
            {
                Entry e;
                if (!Map.TryGetValue(path, out e)) return false;
                // The caller decides something on this answer and waits for the rebuild that follows
                // the probe, such as restoring an expanded subtree, so that probe must raise Changed
                // even when it only confirms the optimistic answer.
                if (!e.Probed) { e.NotifyOnProbe = true; return false; }
                return e.Exists;
            }
        }

        internal enum ProbeState { NotYet, Alive, Gone }

        /// <summary>What the last completed probe found, read in one step under the lock. NotYet means
        /// no probe has completed: the path is new, or its entry was evicted. Reading "alive" and
        /// "probed" separately could see a probe land between the two reads and call a live path gone,
        /// so the icon gate reads this instead (#139). It never queues a probe; call Touch for that.</summary>
        internal static ProbeState State(string path)
        {
            if (string.IsNullOrEmpty(path)) return ProbeState.Gone;
            lock (Gate)
            {
                Entry e;
                if (!Map.TryGetValue(path, out e) || !e.Probed) return ProbeState.NotYet;
                return e.Exists ? ProbeState.Alive : ProbeState.Gone;
            }
        }

        /// <summary>Ask for a (re-)probe without reading anything.</summary>
        public static void Touch(string path) { Lookup(path); }

        /// <summary>The cached answer for a path, for code that draws rows. False when the cache has
        /// no entry, never probed or evicted, and then nothing is queued: drawing must not start
        /// probes, because a build that probes every pin can keep evicting and re-probing a working
        /// set larger than the cache and never settle (#136 review). A present but stale entry does
        /// queue a re-probe, as any other read would.</summary>
        internal static bool TryGetCached(string path, out bool exists, out bool isFile)
        {
            exists = false; isFile = false;
            if (string.IsNullOrEmpty(path)) return false;
            bool probe = false;
            lock (Gate)
            {
                Entry e;
                if (!Map.TryGetValue(path, out e)) return false;
                exists = e.Exists;
                isFile = e.Probed ? e.IsFile : GuessIsFile(path);
                if (!e.InFlight && (!e.Probed || Environment.TickCount - e.Tick > TtlMs))
                {
                    probe = true;
                    e.InFlight = true;
                }
            }
            if (probe)
            {
                var p = path;
                WorkQueue.Post(() => Probe(p));
            }
            return true;
        }

        // Returns the entry (never probes on the calling thread) and queues a probe when the entry
        // is new or stale. Null for empty paths.
        private static Entry Lookup(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Entry e;
            bool probe = false;
            lock (Gate)
            {
                if (!Map.TryGetValue(path, out e))
                {
                    e = new Entry { Exists = true, IsFile = GuessIsFile(path) };
                    Map.Set(path, e);
                    probe = true;
                }
                else if (!e.InFlight && (!e.Probed || Environment.TickCount - e.Tick > TtlMs))
                {
                    probe = true;
                }
                if (probe) e.InFlight = true;
            }
            if (probe)
            {
                var p = path;
                WorkQueue.Post(() => Probe(p));
            }
            return e;
        }

        private static void Probe(string path)
        {
            bool exists = false, isFile = false;
            try
            {
                var attr = File.GetAttributes(path); // one syscall answers both questions
                exists = true;
                isFile = (attr & FileAttributes.Directory) == 0;
            }
            catch { exists = false; }

            bool changed = false, first = false;
            lock (Gate)
            {
                Entry e;
                if (Map.TryGetValue(path, out e))
                {
                    first = !e.Probed;
                    changed = ProbeChangesRows(e.Exists, e.IsFile, exists, isFile, first, e.NotifyOnProbe);
                    e.Exists = exists;
                    if (exists) e.IsFile = isFile;
                    e.Probed = true;
                    e.InFlight = false;
                    e.NotifyOnProbe = false;
                    e.Tick = Environment.TickCount;
                }
            }
            if (changed || first) QueueRaise(changed, first);
        }

        /// <summary>Whether a probe result must rebuild the sections: what rows show changed, or this
        /// is the first probe and a ConfirmedAlive reader is waiting on it. A never-probed entry holds
        /// the optimistic answer the rows were drawn with (alive, file by extension), so comparing
        /// against it covers the first probe too (#139 review).</summary>
        internal static bool ProbeChangesRows(bool wasExists, bool wasIsFile, bool exists, bool isFile,
            bool firstProbe, bool readerWaiting)
        {
            if (wasExists != exists) return true;
            if (exists && wasIsFile != isFile) return true;
            return firstProbe && readerWaiting;
        }

        private static bool _pendingChanged, _pendingProbed;

        private static void QueueRaise(bool changed, bool probed)
        {
            lock (Gate)
            {
                _pendingChanged |= changed;
                _pendingProbed |= probed;
                if (_raiseQueued) return;
                _raiseQueued = true;
            }
            WorkQueue.PostUI(() =>
            {
                // Small settle delay so a burst of probe results turns into one rebuild.
                var t = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(300) };
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    bool raiseChanged, raiseProbed;
                    lock (Gate)
                    {
                        _raiseQueued = false;
                        raiseChanged = _pendingChanged; raiseProbed = _pendingProbed;
                        _pendingChanged = false; _pendingProbed = false;
                    }
                    var h = Changed;
                    if (raiseChanged && h != null) h();
                    var p = Probed;
                    if (raiseProbed && p != null) p();
                };
                t.Start();
            });
        }

        private static bool GuessIsFile(string path)
        {
            try
            {
                var trimmed = path.TrimEnd('\\', '/');
                return !string.IsNullOrEmpty(Path.GetExtension(trimmed));
            }
            catch { return false; }
        }
    }
}
