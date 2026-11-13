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
            if (paths == null) return;
            System.IO.Directory.CreateDirectory(_directory);
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                string stem = _utcNow().Ticks.ToString("D19") + "_" + Interlocked.Increment(ref _sequence).ToString("D8")
                    + "_" + Guid.NewGuid().ToString("N");
                string temp = Path.Combine(_directory, stem + TempExtension);
                string final = Path.Combine(_directory, stem + ItemExtension);
                File.WriteAllText(temp, path, new UTF8Encoding(false));
                File.Move(temp, final);
            }
        }

        /// <summary>Returns the queued paths, oldest first, and deletes their files. A file older than
        /// <see cref="MaxAge"/> is deleted without being returned. A file that cannot be read right now
        /// is left in place for the next drain, and never stops the others.</summary>
        public List<string> Drain()
        {
            var result = new List<string>();
            foreach (var file in ItemFiles())
            {
                try
                {
                    if (IsStale(file)) { TryDelete(file); continue; }
                    string text = File.ReadAllText(file, Encoding.UTF8);
                    if (!TryDelete(file)) continue;   // still locked: leave it, and do not return it twice
                    if (text.Length > 0) result.Add(text);
                }
                catch (Exception)
                {
                    // Unreadable or removed by another launch. Skip it.
                }
            }
            return result;
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
