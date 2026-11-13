using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PowerPane.Services
{
    /// <summary>
    /// A small folder of pending "Add to PowerPane" paths, one file per path. Explorer may start the
    /// verb once per selected item, in separate processes. Each launch writes its paths here and one
    /// of them (the picker owner) drains the folder into a single picker, so a multi-selection shows
    /// one window. No message channel to the tray is needed. A file is written under a temporary name
    /// and renamed, so a reader never sees half a file.
    /// </summary>
    internal sealed class PinQueue
    {
        /// <summary>The named mutex the picker owner holds. It is deliberately not
        /// CrashGuard.InstanceMutexName, so pin mode never touches the tray's single-instance check.</summary>
        public const string PickerMutexName = "PowerPane.PinPicker.{3db29322-e90e-4eef-a9dc-debe2ffdc52f}";

        /// <summary>The owner waits until no new file has arrived for this long before it drains.</summary>
        public const int QuietMs = 400;

        /// <summary>The owner never waits longer than this in total, however many files keep arriving.</summary>
        public const int MaxWaitMs = 3000;

        /// <summary>A file older than this is ignored and deleted, so a crashed owner cannot leave stale
        /// pins for a later run.</summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

        private const string ItemExtension = ".pin";
        private const string TempExtension = ".tmp";

        // Keeps names increasing within one process when the clock does not advance between calls.
        private static long _sequence;

        private readonly string _directory;
        private readonly Func<DateTime> _utcNow;

        // Names the launch that wrote a file. Every launch is its own process with its own queue
        // instance, so a drain can tell one launch carrying several paths (Explorer's own order) from
        // several launches (no known order).
        private readonly string _launchId = Guid.NewGuid().ToString("N");

        private static readonly Lazy<PinQueue> _default = new Lazy<PinQueue>(() => new PinQueue(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerPane", "PinQueue"),
            () => DateTime.UtcNow));

        /// <summary>The queue under %LOCALAPPDATA%\PowerPane\PinQueue, on the real clock.</summary>
        public static PinQueue Default { get { return _default.Value; } }

        /// <summary>For tests: a queue in any folder, with an injected UTC clock.</summary>
        public PinQueue(string directory, Func<DateTime> utcNow)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("A queue folder is required.", "directory");
            _directory = directory;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public string FolderPath { get { return _directory; } }

        /// <summary>Writes one file per path. Empty values are skipped. Each file is written as
        /// <c>name.tmp</c> and then renamed, so a reader never sees a partial file.</summary>
        public void Enqueue(IEnumerable<string> paths)
        {
            Enqueue(paths, null);
        }

        /// <summary>As <see cref="Enqueue(IEnumerable{string})"/>, and adds each path to
        /// <paramref name="written"/> once its file is in place. When a write throws partway, the list
        /// holds exactly the paths that did reach the queue, so the caller can show the rest itself.</summary>
        public void Enqueue(IEnumerable<string> paths, ICollection<string> written)
        {
            if (paths == null) return;
            System.IO.Directory.CreateDirectory(_directory);
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                string stem = _utcNow().Ticks.ToString("D19") + "_" + Interlocked.Increment(ref _sequence).ToString("D8")
                    + "_" + _launchId + "_" + Guid.NewGuid().ToString("N");
                string temp = Path.Combine(_directory, stem + TempExtension);
                string final = Path.Combine(_directory, stem + ItemExtension);
                File.WriteAllText(temp, path, new UTF8Encoding(false));
                File.Move(temp, final);
                if (written != null) written.Add(path);
            }
        }

        /// <summary>Returns the queued paths and deletes their files. Paths from one launch stay in the
        /// order that launch gave them. Paths from more than one launch come back sorted by path
        /// (ordinal, ignoring case), because separate processes race and arrival order would change
        /// from run to run. A file older than
        /// <see cref="MaxAge"/> is deleted without being returned. A file that cannot be read right now
        /// is left in place for the next drain, and never stops the others.</summary>
        public List<string> Drain()
        {
            var result = new List<string>();
            var launches = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in ItemFiles())
            {
                try
                {
                    if (IsStale(file)) { TryDelete(file); continue; }
                    string text = File.ReadAllText(file, Encoding.UTF8);
                    if (!TryDelete(file)) continue;   // still locked: leave it, and do not return it twice
                    if (text.Length > 0)
                    {
                        result.Add(text);
                        launches.Add(LaunchOf(file));
                    }
                }
                catch (Exception)
                {
                    // Unreadable or removed by another launch. Skip it.
                }
            }
            if (launches.Count > 1) result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        // The file name is ticks_sequence_launch_guid. A name without that shape counts as its own launch.
        private static string LaunchOf(string file)
        {
            var parts = Path.GetFileNameWithoutExtension(file).Split('_');
            return parts.Length >= 4 ? parts[2] : Path.GetFileName(file);
        }

        /// <summary>Claims the picker by ownership, not by whether the kernel object was new. A launch
        /// that only opens the mutex for a moment, and loses, never blocks the owner's re-claim. Must be
        /// called, and the result released, on the same thread. An abandoned mutex (its owner crashed)
        /// counts as claimed.</summary>
        internal static bool TryClaim(string name, out Mutex claimed)
        {
            claimed = null;
            var m = new Mutex(false, name);
            bool owned;
            try { owned = m.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }
            catch (Exception) { m.Dispose(); throw; }
            if (!owned) { m.Dispose(); return false; }
            claimed = m;
            return true;
        }

        /// <summary>The number of files waiting that are not stale.</summary>
        public int PendingCount()
        {
            int n = 0;
            foreach (var file in ItemFiles())
            {
                try { if (!IsStale(file)) n++; } catch (Exception) { }
            }
            return n;
        }

        /// <summary>The write time, in UTC, of the newest file waiting that is not stale, or null when
        /// none is. The owner feeds it to <see cref="QuietWindowOver"/>.</summary>
        public DateTime? LatestArrivalUtc()
        {
            DateTime? latest = null;
            foreach (var file in ItemFiles())
            {
                try
                {
                    if (IsStale(file)) continue;
                    var t = File.GetLastWriteTimeUtc(file);
                    if (latest == null || t > latest.Value) latest = t;
                }
                catch (Exception) { }
            }
            return latest;
        }

        /// <summary>True once the owner should stop waiting for more launches: no arrival for
        /// <see cref="QuietMs"/>, or <see cref="MaxWaitMs"/> since the first launch, whichever comes first.</summary>
        internal static bool QuietWindowOver(DateTime started, DateTime lastArrival, DateTime now)
        {
            if (now - started >= TimeSpan.FromMilliseconds(MaxWaitMs)) return true;
            return now - lastArrival >= TimeSpan.FromMilliseconds(QuietMs);
        }

        private IEnumerable<string> ItemFiles()
        {
            if (!System.IO.Directory.Exists(_directory)) return Enumerable.Empty<string>();
            try
            {
                // The ".pin" filter never matches a ".tmp" file. Ordinal order is arrival order,
                // because every name starts with its zero-padded UTC ticks.
                return System.IO.Directory.GetFiles(_directory, "*" + ItemExtension)
                    .Where(f => string.Equals(Path.GetExtension(f), ItemExtension, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception)
            {
                return Enumerable.Empty<string>();
            }
        }

        private bool IsStale(string file)
        {
            return _utcNow() - File.GetLastWriteTimeUtc(file) > MaxAge;
        }

        private static bool TryDelete(string file)
        {
            try { File.Delete(file); return true; }
            catch (Exception) { return false; }
        }
    }
}
