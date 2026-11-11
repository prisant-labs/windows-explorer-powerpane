using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers the deferred settings.json write (#163): Save() serializes on the caller and queues
    /// the write, saves that pile up collapse into one write of the newest bytes, Flush waits for
    /// it, and a backup or a restore never sees or leaves a stale file. Each test gets its own temp
    /// directory, uses the (dir, post, onError) constructor so it never touches the real settings
    /// lane, and captures errors so an expected failure stays out of the real log. Every wait has a
    /// timeout, so a broken writer fails the test instead of hanging the run.
    /// </summary>
    [TestFixture]
    public class SettingsStoreWriteTests
    {
        private static readonly TimeSpan Long = TimeSpan.FromSeconds(5);

        // Every value stays inside 5 to 50, the range AppSettings.Normalize clamps the recents count to.

        /// <summary>Seed a settings.json whose groups folder is inside the temp dir. The recents count
        /// goes on the profile, because Load copies the active profile over the top-level values.</summary>
        private static void WriteSettingsFile(string file, string groupsPath, int recents)
        {
            var settings = AppSettings.CreateDefault();
            settings.GroupsPath = groupsPath;
            settings.RecentsMaxCount = recents;
            settings.Profiles = new List<Profile>
            {
                new Profile { Name = "Profile 1", GroupsPath = groupsPath, RecentsMaxCount = recents, SidebarWidthPx = 220 }
            };
            using (var fs = File.Create(file))
            {
                new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(fs, settings);
            }
        }

        private static void Seed(TempDir temp, int recents)
        {
            WriteSettingsFile(Path.Combine(temp.Path, "settings.json"), Path.Combine(temp.Path, "Groups"), recents);
        }

        /// <summary>What a fresh process would read from disk right now.</summary>
        private static int RecentsOnDisk(string dir)
        {
            var reloaded = new SettingsStore(dir);
            reloaded.Load();
            return reloaded.Current.RecentsMaxCount;
        }

        [Test]
        public void Save_QueuesTheWriteAndReturnsBeforeTheFileChanges()
        {
            using (var temp = new TempDir())
            {
                Seed(temp, 15);
                var posted = new List<Action>();
                var store = new SettingsStore(temp.Path, posted.Add, (m, ex) => { });
                store.Load();

                store.Current.RecentsMaxCount = 21;
                store.Save();

                Assert.That(posted.Count, Is.EqualTo(1));
                Assert.That(RecentsOnDisk(temp.Path), Is.EqualTo(15));
                posted[0]();
                Assert.That(RecentsOnDisk(temp.Path), Is.EqualTo(21));
            }
        }

        [Test]
        public void Save_SeveralBeforeTheWriteRuns_WritesOnceWithTheNewestBytes()
        {
            using (var temp = new TempDir())
            {
                Seed(temp, 15);
                var posted = new List<Action>();
                var store = new SettingsStore(temp.Path, posted.Add, (m, ex) => { });
                store.Load();

                foreach (var n in new[] { 11, 22, 33 })
                {
                    store.Current.RecentsMaxCount = n;
                    store.Save();
                }

                Assert.That(posted.Count, Is.EqualTo(1), "saves that pile up share one queued write");
                posted[0]();
                Assert.That(RecentsOnDisk(temp.Path), Is.EqualTo(33));
                Assert.That(store.Flush(TimeSpan.Zero), Is.True);
            }
        }

        [Test]
        public void Save_RaisesChangedOnTheCallerBeforeTheWriteRuns()
        {
            using (var temp = new TempDir())
            {
                Seed(temp, 15);
                var posted = new List<Action>();
                var store = new SettingsStore(temp.Path, posted.Add, (m, ex) => { });
                store.Load();
                int changedOn = -1;
                int onDiskWhenChanged = -1;
                store.Changed += (s, e) =>
                {
                    changedOn = Thread.CurrentThread.ManagedThreadId;
                    onDiskWhenChanged = RecentsOnDisk(temp.Path);
                };

                store.Current.RecentsMaxCount = 40;
                store.Save();

                Assert.That(changedOn, Is.EqualTo(Thread.CurrentThread.ManagedThreadId));
                Assert.That(onDiskWhenChanged, Is.EqualTo(15));
            }
        }

        [Test]
        public void Flush_WaitsForThePendingWrite()
        {
            using (var temp = new TempDir())
            using (var gate = new ManualResetEventSlim(false))
            {
                Seed(temp, 15);
                var errors = new List<string>();
                Action<Action> post = work => new Thread(() => { if (gate.Wait(Long)) work(); }) { IsBackground = true }.Start();
                var store = new SettingsStore(temp.Path, post, (m, ex) => { lock (errors) errors.Add(m); });
                store.Load();

                store.Current.RecentsMaxCount = 44;
                store.Save();
                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(200); gate.Set(); });

                Assert.That(store.Flush(Long), Is.True);
                Assert.That(RecentsOnDisk(temp.Path), Is.EqualTo(44));
                Assert.That(errors, Is.Empty);
            }
        }

        [Test]
        public void Flush_TimesOut_ReturnsFalseAndLogs()
        {
            using (var temp = new TempDir())
            {
                Seed(temp, 15);
                var errors = new List<string>();
                var posted = new List<Action>();   // never run, so the write stays pending
                var store = new SettingsStore(temp.Path, posted.Add, (m, ex) => errors.Add(m));
                store.Load();

                store.Current.RecentsMaxCount = 45;
                store.Save();

                Assert.That(store.Flush(TimeSpan.FromMilliseconds(50)), Is.False);
                Assert.That(errors, Has.Some.Contains("still pending"));
            }
        }

        [Test]
        public void ExportTo_RightAfterSave_CopiesTheNewContent()
        {
            using (var temp = new TempDir())
            using (var blocker = new ManualResetEventSlim(false))
            {
                Seed(temp, 15);
                var errors = new List<string>();
                var lane = new WorkLane("test-settings-export", (m, ex) => { lock (errors) errors.Add(m); }, m => { });
                var store = new SettingsStore(temp.Path, lane.Post, (m, ex) => { lock (errors) errors.Add(m); });
                store.Load();

                // Hold the lane, so the write Export queues is still pending when the copy would run.
                lane.Post(() => blocker.Wait(Long));
                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(200); blocker.Set(); });

                store.Current.RecentsMaxCount = 35;
                var backup = Path.Combine(temp.Path, "backup.json");
                store.ExportTo(backup);

                AppSettings exported;
                using (var fs = File.OpenRead(backup))
                    exported = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(fs);
                Assert.That(exported.RecentsMaxCount, Is.EqualTo(35));
                Assert.That(errors, Is.Empty);
            }
        }

        [Test]
        public void Save_WhenTheWriteFails_LogsAndDoesNotThrow()
        {
            using (var temp = new TempDir())
            {
                Seed(temp, 15);
                var errors = new List<string>();
                var store = new SettingsStore(temp.Path, null, (m, ex) => errors.Add(m));
                store.Load();
                // A folder where the temp file goes makes File.WriteAllBytes fail every time.
                Directory.CreateDirectory(Path.Combine(temp.Path, "settings.json.tmp"));
                bool changed = false;
                store.Changed += (s, e) => changed = true;

                store.Current.RecentsMaxCount = 36;
                Assert.DoesNotThrow(() => store.Save());

                Assert.That(errors, Has.Some.EqualTo("settings save failed"));
                Assert.That(changed, Is.True, "the change is live in this session even though it did not persist");
                Assert.That(store.Flush(TimeSpan.Zero), Is.True, "a failed write must not leave Flush waiting");
                Assert.That(RecentsOnDisk(temp.Path), Is.EqualTo(15));
                // A backup after a failed write reports the failure instead of copying the old file.
                Assert.Throws<IOException>(() => store.ExportTo(Path.Combine(temp.Path, "backup.json")));
            }
        }

        [Test]
        public void ImportFrom_IsNotOverwrittenByAnOlderQueuedWrite()
        {
            using (var temp = new TempDir())
            {
                Seed(temp, 15);
                var restoreFile = Path.Combine(temp.Path, "restore.json");
                WriteSettingsFile(restoreFile, Path.Combine(temp.Path, "Groups"), 47);
                var posted = new List<Action>();
                var store = new SettingsStore(temp.Path, posted.Add, (m, ex) => { });
                store.Load();

                store.Current.RecentsMaxCount = 11;
                store.Save();                  // queued, not yet written
                Assert.That(store.ImportFrom(restoreFile), Is.True);

                foreach (var work in posted.ToArray()) work();
                Assert.That(RecentsOnDisk(temp.Path), Is.EqualTo(47));
            }
        }

        [Test]
        public void ImportFrom_WhileAnOlderWriteIsHeldOnTheLane_EndsWithTheRestoredFile()
        {
            using (var temp = new TempDir())
            using (var blocker = new ManualResetEventSlim(false))
            {
                Seed(temp, 15);
                var restoreFile = Path.Combine(temp.Path, "restore.json");
                WriteSettingsFile(restoreFile, Path.Combine(temp.Path, "Groups"), 48);
                var lane = new WorkLane("test-settings-import", (m, ex) => { }, m => { });
                var store = new SettingsStore(temp.Path, lane.Post, (m, ex) => { });
                store.Load();

                lane.Post(() => blocker.Wait(Long));
                store.Current.RecentsMaxCount = 12;
                store.Save();
                Assert.That(store.ImportFrom(restoreFile), Is.True);
                blocker.Set();

                Assert.That(store.Flush(Long), Is.True);
                Assert.That(RecentsOnDisk(temp.Path), Is.EqualTo(48));
            }
        }
    }
}
