using System;
using System.Collections.Generic;
using NUnit.Framework;
using PowerPane.Interop;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers WinEventHub, which shares one native WinEvent hook among every subscriber for the same
    /// event range on the same thread, instead of installing the same system-wide hook two or three
    /// times (#43). The native install and uninstall are fakes, so nothing is hooked.
    /// </summary>
    [TestFixture]
    public class WinEventHubTests
    {
        private const uint Location = 0x800B;
        private const uint Foreground = 0x0003;

        private sealed class FakeNative
        {
            public readonly List<NativeMethods.WinEventDelegate> Installed = new List<NativeMethods.WinEventDelegate>();
            public readonly List<IntPtr> Removed = new List<IntPtr>();
            public readonly List<string> Errors = new List<string>();
            public uint Thread = 1;
            public bool Fail;
            public bool FailUnhook;

            // Errors are collected here rather than written to the real log.
            public WinEventHub Hub()
            {
                return new WinEventHub(
                    (min, max, proc) =>
                    {
                        if (Fail) return IntPtr.Zero;
                        Installed.Add(proc);
                        return new IntPtr(Installed.Count);
                    },
                    handle => { Removed.Add(handle); return !FailUnhook; },
                    () => Thread,
                    (message, ex) => Errors.Add(message));
            }

            public void Fire(int hook, uint evt)
            {
                Installed[hook].Invoke(new IntPtr(hook + 1), evt, new IntPtr(0x1234), 0, 0, 7, 0);
            }
        }

        [Test]
        public void Attach_SameRangeTwice_InstallsOneNativeHook()
        {
            // The bug: location changes were hooked twice and foreground changes three times.
            var native = new FakeNative();
            var hub = native.Hub();
            hub.Attach(Location, Location, (e, h, o, c, t) => { });
            hub.Attach(Location, Location, (e, h, o, c, t) => { });
            hub.Attach(Foreground, Foreground, (e, h, o, c, t) => { });

            Assert.That(native.Installed.Count, Is.EqualTo(2));
            Assert.That(hub.NativeHookCount, Is.EqualTo(2));
        }

        [Test]
        public void Event_ReachesEverySubscriber()
        {
            var native = new FakeNative();
            var hub = native.Hub();
            var seen = new List<string>();
            hub.Attach(Location, Location, (e, h, o, c, t) => seen.Add("first " + e.ToString("X")));
            hub.Attach(Location, Location, (e, h, o, c, t) => seen.Add("second " + h.ToString("X")));

            native.Fire(0, Location);

            Assert.That(seen, Is.EqualTo(new[] { "first 800B", "second 1234" }));
        }

        [Test]
        public void Event_ThrowingSubscriber_DoesNotStopTheOthers()
        {
            var native = new FakeNative();
            var hub = native.Hub();
            bool reached = false;
            hub.Attach(Location, Location, (e, h, o, c, t) => { throw new InvalidOperationException("expected by the test"); });
            hub.Attach(Location, Location, (e, h, o, c, t) => reached = true);

            Assert.DoesNotThrow(() => native.Fire(0, Location));
            Assert.That(reached, Is.True);
            Assert.That(native.Errors, Is.EqualTo(new[] { "WinEvent handler threw" }));
        }

        [Test]
        public void Detach_LastSubscriber_RemovesTheNativeHook()
        {
            var native = new FakeNative();
            var hub = native.Hub();
            var a = hub.Attach(Location, Location, (e, h, o, c, t) => { });
            var b = hub.Attach(Location, Location, (e, h, o, c, t) => { });

            hub.Detach(a);
            Assert.That(native.Removed, Is.Empty);
            hub.Detach(b);
            Assert.That(native.Removed, Is.EqualTo(new[] { new IntPtr(1) }));
            Assert.That(hub.NativeHookCount, Is.EqualTo(0));
        }

        [Test]
        public void Detach_StopsDeliveryToThatSubscriberOnly()
        {
            var native = new FakeNative();
            var hub = native.Hub();
            int a = 0, b = 0;
            var tokenA = hub.Attach(Location, Location, (e, h, o, c, t) => a++);
            hub.Attach(Location, Location, (e, h, o, c, t) => b++);

            hub.Detach(tokenA);
            hub.Detach(tokenA); // a second detach is harmless
            native.Fire(0, Location);

            Assert.That(a, Is.EqualTo(0));
            Assert.That(b, Is.EqualTo(1));
        }

        [Test]
        public void Attach_OtherThread_GetsItsOwnHook()
        {
            // An out-of-context hook delivers to the thread that installed it, so sharing across
            // threads would move a subscriber's events onto the wrong thread.
            var native = new FakeNative();
            var hub = native.Hub();
            hub.Attach(Location, Location, (e, h, o, c, t) => { });
            native.Thread = 2;
            hub.Attach(Location, Location, (e, h, o, c, t) => { });

            Assert.That(native.Installed.Count, Is.EqualTo(2));
        }

        [Test]
        public void Detach_UnhookFails_IsReported()
        {
            var native = new FakeNative { FailUnhook = true };
            var hub = native.Hub();
            var token = hub.Attach(Location, Location, (e, h, o, c, t) => { });

            hub.Detach(token);

            Assert.That(native.Removed, Is.EqualTo(new[] { new IntPtr(1) }));
            Assert.That(native.Errors, Is.EqualTo(new[] { "UnhookWinEvent failed for range 800B-800B" }));
        }

        [Test]
        public void Detach_FromAnotherThread_IsReportedAndStillUnhooks()
        {
            // Windows removes a hook only on its own thread, so this unhook would fail for real.
            var native = new FakeNative();
            var hub = native.Hub();
            var token = hub.Attach(Location, Location, (e, h, o, c, t) => { });
            native.Thread = 2;

            hub.Detach(token);

            Assert.That(native.Errors, Is.EqualTo(new[] { "WinEvent hook for range 800B-800B detached on thread 2 but installed on thread 1" }));
            Assert.That(native.Removed.Count, Is.EqualTo(1));
        }

        [Test]
        public void Attach_NativeFailure_ReturnsNullAndRetriesNextTime()
        {
            var native = new FakeNative { Fail = true };
            var hub = native.Hub();
            Assert.That(hub.Attach(Location, Location, (e, h, o, c, t) => { }), Is.Null);
            Assert.That(hub.NativeHookCount, Is.EqualTo(0));

            native.Fail = false;
            Assert.That(hub.Attach(Location, Location, (e, h, o, c, t) => { }), Is.Not.Null);
            Assert.That(hub.NativeHookCount, Is.EqualTo(1));
        }
    }
}
