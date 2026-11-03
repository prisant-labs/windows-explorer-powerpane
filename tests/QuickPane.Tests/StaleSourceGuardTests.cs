using System;
using System.Collections.Generic;
using NUnit.Framework;
using QuickPane.Interop;

namespace QuickPane.Tests
{
    /// <summary>The chain walk StaleSourceGuard makes before WPF's move/size hook does (#148),
    /// over invented handles: 0x10 is a pane window, 0x20 the Explorer window it sits in, and 0x30
    /// that window's owner. No real window is touched.</summary>
    [TestFixture]
    public class StaleSourceGuardTests
    {
        private static readonly IntPtr Pane = new IntPtr(0x10);
        private static readonly IntPtr Cabinet = new IntPtr(0x20);
        private static readonly IntPtr Owner = new IntPtr(0x30);

        private static bool Walk(Dictionary<IntPtr, IntPtr> parents, HashSet<IntPtr> alive, IntPtr start, out IntPtr dead)
        {
            return StaleSourceGuard.ChainIsAlive(start,
                h => alive.Contains(h),
                h => { IntPtr p; return parents.TryGetValue(h, out p) ? p : IntPtr.Zero; },
                out dead);
        }

        [Test]
        public void ChainIsAlive_WhenEveryWindowUpToTheTopExists()
        {
            var parents = new Dictionary<IntPtr, IntPtr> { { Pane, Cabinet }, { Cabinet, Owner } };
            IntPtr dead;
            Assert.That(Walk(parents, new HashSet<IntPtr> { Pane, Cabinet, Owner }, Pane, out dead), Is.True);
            Assert.That(dead, Is.EqualTo(IntPtr.Zero));
        }

        [Test]
        public void ChainIsDead_WhenTheParentWindowIsGone()
        {
            // Explorer died; the pane's own window survived and still names the cabinet as parent.
            var parents = new Dictionary<IntPtr, IntPtr> { { Pane, Cabinet } };
            IntPtr dead;
            Assert.That(Walk(parents, new HashSet<IntPtr> { Pane }, Pane, out dead), Is.False);
            Assert.That(dead, Is.EqualTo(Cabinet));
        }

        [Test]
        public void ChainIsDead_WhenAnOwnerFurtherUpIsGone()
        {
            var parents = new Dictionary<IntPtr, IntPtr> { { Pane, Cabinet }, { Cabinet, Owner } };
            IntPtr dead;
            Assert.That(Walk(parents, new HashSet<IntPtr> { Pane, Cabinet }, Pane, out dead), Is.False);
            Assert.That(dead, Is.EqualTo(Owner));
        }

        [Test]
        public void ChainIsDead_WhenThePaneWindowItselfIsGone()
        {
            // The window was destroyed with its parent, but its HwndSource never heard about it.
            IntPtr dead;
            Assert.That(Walk(new Dictionary<IntPtr, IntPtr>(), new HashSet<IntPtr>(), Pane, out dead), Is.False);
            Assert.That(dead, Is.EqualTo(Pane));
        }

        [Test]
        public void ChainWalk_StopsOnAParentLoop()
        {
            var parents = new Dictionary<IntPtr, IntPtr> { { Pane, Cabinet }, { Cabinet, Pane } };
            IntPtr dead;
            Assert.That(Walk(parents, new HashSet<IntPtr> { Pane, Cabinet }, Pane, out dead), Is.True);
        }

        [Test]
        public void ChainWalk_TreatsAZeroHandleAsNothingToCheck()
        {
            IntPtr dead;
            Assert.That(Walk(new Dictionary<IntPtr, IntPtr>(), new HashSet<IntPtr>(), IntPtr.Zero, out dead), Is.True);
        }

        // ---- which events and messages run a check (#159) ----

        private const int SomeChild = 3;           // an object inside a window, such as a list item

        [Test]
        public void CheckOnEvent_RunsWhenAnyMoveOrSizeLoopStartsOrEnds()
        {
            Assert.That(StaleSourceGuard.CheckOnEvent(NativeMethods.EVENT_SYSTEM_MOVESIZESTART, NativeMethods.OBJID_WINDOW, 0), Is.True);
            Assert.That(StaleSourceGuard.CheckOnEvent(NativeMethods.EVENT_SYSTEM_MOVESIZEEND, NativeMethods.OBJID_WINDOW, 0), Is.True);
        }

        [Test]
        public void CheckOnEvent_RunsWhenAWindowItselfIsDestroyed()
        {
            Assert.That(StaleSourceGuard.CheckOnEvent(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.OBJID_WINDOW, NativeMethods.CHILDID_SELF), Is.True);
        }

        [Test]
        public void CheckOnEvent_SkipsTheDestructionOfAnObjectInsideAWindow()
        {
            Assert.That(StaleSourceGuard.CheckOnEvent(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.OBJID_CLIENT, NativeMethods.CHILDID_SELF), Is.False);
            Assert.That(StaleSourceGuard.CheckOnEvent(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.OBJID_WINDOW, SomeChild), Is.False);
        }

        [Test]
        public void CheckOnEvent_SkipsTheOtherEventsInTheSharedLifecycleRange()
        {
            // The guard shares the CREATE..SHOW hook with the Explorer watchers, so it sees these too.
            Assert.That(StaleSourceGuard.CheckOnEvent(NativeMethods.EVENT_OBJECT_CREATE, NativeMethods.OBJID_WINDOW, 0), Is.False);
            Assert.That(StaleSourceGuard.CheckOnEvent(NativeMethods.EVENT_OBJECT_SHOW, NativeMethods.OBJID_WINDOW, 0), Is.False);
        }

        [Test]
        public void CheckOnMessage_RunsAsAnOwnWindowEntersOrLeavesItsMoveLoop()
        {
            Assert.That(StaleSourceGuard.CheckOnMessage(NativeMethods.WM_ENTERSIZEMOVE), Is.True);
            Assert.That(StaleSourceGuard.CheckOnMessage(NativeMethods.WM_EXITSIZEMOVE), Is.True);
        }

        [Test]
        public void CheckOnMessage_SkipsEveryOtherMessage()
        {
            Assert.That(StaleSourceGuard.CheckOnMessage(NativeMethods.WM_DISPLAYCHANGE), Is.False);
            Assert.That(StaleSourceGuard.CheckOnMessage(NativeMethods.WM_KEYDOWN), Is.False);
            Assert.That(StaleSourceGuard.CheckOnMessage(0), Is.False);
        }

        [Test]
        public void LeaveToWatcher_WhenThePanesOwnHostIsTheWindowBeingDestroyed()
        {
            // The routine close: the watcher tears this pane down from the same DESTROY event.
            Assert.That(StaleSourceGuard.LeaveToWatcher(Cabinet, Cabinet), Is.True);
        }

        [Test]
        public void ReleaseAtOnce_WhenTheChainDiesAtAWindowOtherThanTheOneBeingDestroyed()
        {
            // A neighbouring window's DESTROY exposed a host whose own DESTROY never arrived (#148).
            Assert.That(StaleSourceGuard.LeaveToWatcher(Cabinet, Owner), Is.False);
            // The pane's own window vanished without its HwndSource being told.
            Assert.That(StaleSourceGuard.LeaveToWatcher(Pane, Cabinet), Is.False);
        }

        [Test]
        public void ReleaseAtOnce_ForEveryTriggerOtherThanADestroy()
        {
            Assert.That(StaleSourceGuard.LeaveToWatcher(Cabinet, IntPtr.Zero), Is.False);
        }

        [Test]
        public void ReleaseContent_TreatsAMissingSourceAsAlreadyReleased()
        {
            // A pane torn down before its window was ever created has no source to release.
            Assert.That(StaleSourceGuard.ReleaseContent(null, "test"), Is.True);
        }
    }
}
