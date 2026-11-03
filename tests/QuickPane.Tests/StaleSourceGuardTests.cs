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
    }
}
