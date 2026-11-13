using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers the folder that coalesces several "Add to PowerPane" launches into one picker. Every
    /// queue here lives in the test's own temporary folder, and time comes from an injected clock.
    /// </summary>
    [TestFixture]
    public class PinQueueTests
    {
        private sealed class Fixture : IDisposable
        {
            public readonly TempDir Temp = new TempDir();
            public TimeSpan Skew = TimeSpan.Zero;
            public readonly string Folder;
            public readonly PinQueue Queue;

            public Fixture()
            {
                Folder = Path.Combine(Temp.Path, "PinQueue");
                Queue = new PinQueue(Folder, () => DateTime.UtcNow + Skew);
            }

            public void Dispose() { Temp.Dispose(); }
        }

        [Test]
        public void EnqueueThenDrain_ReturnsPathsInArrivalOrder_AndEmptiesTheFolder()
        {
            using (var f = new Fixture())
            {
                f.Queue.Enqueue(new[] { @"C:\Demo\Zephyr", @"C:\Demo\Apollo" });
                f.Queue.Enqueue(new[] { @"D:\Demo\Borealis\notes.txt" });

                Assert.That(f.Queue.PendingCount(), Is.EqualTo(3));
                Assert.That(f.Queue.Drain(),
                    Is.EqualTo(new[] { @"C:\Demo\Zephyr", @"C:\Demo\Apollo", @"D:\Demo\Borealis\notes.txt" }));
                Assert.That(Directory.GetFiles(f.Folder), Is.Empty);
                Assert.That(f.Queue.PendingCount(), Is.EqualTo(0));
                Assert.That(f.Queue.Drain(), Is.Empty);
            }
        }

        [Test]
        public void Drain_WhenTheFolderDoesNotExist_ReturnsEmpty()
        {
            using (var f = new Fixture())
            {
                Assert.That(f.Queue.Drain(), Is.Empty);
                Assert.That(f.Queue.PendingCount(), Is.EqualTo(0));
                Assert.That(f.Queue.LatestArrivalUtc(), Is.Null);
            }
        }

        [Test]
        public void Enqueue_SkipsEmptyValues()
        {
            using (var f = new Fixture())
            {
                f.Queue.Enqueue(new[] { "", null, @"C:\Demo\Apollo" });
                Assert.That(f.Queue.Drain(), Is.EqualTo(new[] { @"C:\Demo\Apollo" }));
            }
        }

        [Test]
        public void Drain_SkipsAndDeletesAFileOlderThanTenMinutes()
        {
            using (var f = new Fixture())
            {
                f.Queue.Enqueue(new[] { @"C:\Demo\Old" });
                f.Queue.Enqueue(new[] { @"C:\Demo\New" });

                // Age the first file by setting its write time 11 minutes behind the queue's clock.
                var oldFile = Directory.GetFiles(f.Folder, "*.pin").OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal).First();
                File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddMinutes(-11));

                Assert.That(f.Queue.PendingCount(), Is.EqualTo(1));
                Assert.That(f.Queue.Drain(), Is.EqualTo(new[] { @"C:\Demo\New" }));
                Assert.That(Directory.GetFiles(f.Folder), Is.Empty);
            }
        }

        [Test]
        public void Drain_JustInsideTheLimit_StillReturnsTheFile()
        {
            using (var f = new Fixture())
            {
                f.Queue.Enqueue(new[] { @"C:\Demo\Apollo" });
                f.Skew = TimeSpan.FromMinutes(9);
                Assert.That(f.Queue.Drain(), Is.EqualTo(new[] { @"C:\Demo\Apollo" }));
            }
        }

        [Test]
        public void Drain_AnUnreadableFile_DoesNotStopTheOthers()
        {
            using (var f = new Fixture())
            {
                f.Queue.Enqueue(new[] { @"C:\Demo\Locked", @"C:\Demo\Apollo" });
                var locked = Directory.GetFiles(f.Folder, "*.pin").OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal).First();

                using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert.That(f.Queue.Drain(), Is.EqualTo(new[] { @"C:\Demo\Apollo" }));
                }

                // Once the lock is gone, the skipped file is delivered by the next drain.
                Assert.That(f.Queue.Drain(), Is.EqualTo(new[] { @"C:\Demo\Locked" }));
            }
        }

        [Test]
        public void Drain_NeverReturnsATempFile()
        {
            using (var f = new Fixture())
            {
                Directory.CreateDirectory(f.Folder);
                File.WriteAllText(Path.Combine(f.Folder, "0000000000000000001_00000001_abc.tmp"), @"C:\Demo\Half");
                f.Queue.Enqueue(new[] { @"C:\Demo\Apollo" });

                Assert.That(f.Queue.PendingCount(), Is.EqualTo(1));
                Assert.That(f.Queue.Drain(), Is.EqualTo(new[] { @"C:\Demo\Apollo" }));
                Assert.That(Directory.GetFiles(f.Folder, "*.tmp").Length, Is.EqualTo(1), "a temp file belongs to a writer and stays");
            }
        }

        [Test]
        public void Enqueue_LeavesNoTempFileBehind()
        {
            using (var f = new Fixture())
            {
                f.Queue.Enqueue(new[] { @"C:\Demo\Apollo", @"C:\Demo\Zephyr" });
                Assert.That(Directory.GetFiles(f.Folder, "*.tmp"), Is.Empty);
                Assert.That(Directory.GetFiles(f.Folder, "*.pin").Length, Is.EqualTo(2));
            }
        }

        [Test]
        public void LatestArrivalUtc_IsTheNewestFilesWriteTime()
        {
            using (var f = new Fixture())
            {
                var before = DateTime.UtcNow.AddSeconds(-2);
                f.Queue.Enqueue(new[] { @"C:\Demo\Apollo" });
                var latest = f.Queue.LatestArrivalUtc();

                Assert.That(latest, Is.Not.Null);
                Assert.That(latest.Value, Is.GreaterThan(before));
                Assert.That(latest.Value, Is.LessThan(DateTime.UtcNow.AddSeconds(2)));
            }
        }

        [Test]
        public void TwoThreadsEnqueueingAtOnce_LoseNothing()
        {
            using (var f = new Fixture())
            {
                var a = Enumerable.Range(0, 100).Select(i => @"C:\Demo\A" + i).ToList();
                var b = Enumerable.Range(0, 100).Select(i => @"C:\Demo\B" + i).ToList();
                var otherQueue = new PinQueue(f.Folder, () => DateTime.UtcNow);

                Task.WaitAll(Task.Run(() => f.Queue.Enqueue(a)), Task.Run(() => otherQueue.Enqueue(b)));

                var drained = f.Queue.Drain();
                Assert.That(drained.Count, Is.EqualTo(200));
                Assert.That(drained, Is.EquivalentTo(a.Concat(b)));
                // Within one writer, arrival order holds.
                Assert.That(drained.Where(p => p.StartsWith(@"C:\Demo\A")).ToList(), Is.EqualTo(a));
                Assert.That(drained.Where(p => p.StartsWith(@"C:\Demo\B")).ToList(), Is.EqualTo(b));
            }
        }

        // ---- the quiet window ------------------------------------------------

        private static readonly DateTime T0 = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

        [Test]
        public void QuietWindowOver_At399Ms_IsFalse()
        {
            Assert.That(PinQueue.QuietWindowOver(T0, T0, T0.AddMilliseconds(399)), Is.False);
        }

        [Test]
        public void QuietWindowOver_At400Ms_IsTrue()
        {
            Assert.That(PinQueue.QuietWindowOver(T0, T0, T0.AddMilliseconds(400)), Is.True);
        }

        [Test]
        public void QuietWindowOver_ALateArrivalRestartsTheQuietPeriod()
        {
            // First launch at T0, another 300 ms later: at 600 ms the quiet period is 300 ms, not enough.
            Assert.That(PinQueue.QuietWindowOver(T0, T0.AddMilliseconds(300), T0.AddMilliseconds(600)), Is.False);
            Assert.That(PinQueue.QuietWindowOver(T0, T0.AddMilliseconds(300), T0.AddMilliseconds(700)), Is.True);
        }

        [Test]
        public void QuietWindowOver_AtTheThreeSecondCap_IsTrueEvenWithContinuingArrivals()
        {
            Assert.That(PinQueue.QuietWindowOver(T0, T0.AddMilliseconds(2900), T0.AddMilliseconds(2999)), Is.False);
            Assert.That(PinQueue.QuietWindowOver(T0, T0.AddMilliseconds(2950), T0.AddMilliseconds(3000)), Is.True);
            Assert.That(PinQueue.QuietWindowOver(T0, T0.AddMilliseconds(3000), T0.AddMilliseconds(3100)), Is.True);
        }

        [Test]
        public void PickerMutexName_IsNotTheTraysSingleInstanceName()
        {
            Assert.That(PinQueue.PickerMutexName, Does.StartWith("PowerPane.PinPicker."));
            Assert.That(PinQueue.PickerMutexName, Is.Not.EqualTo(CrashGuard.InstanceMutexName));
        }
    }
}
