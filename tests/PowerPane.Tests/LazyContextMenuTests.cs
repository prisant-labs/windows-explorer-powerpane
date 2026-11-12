using System;
using System.Threading;
using System.Windows.Controls;
using NUnit.Framework;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>A pin's menu is built when it opens, not when the row is built, because every row in
    /// every pane used to carry a full menu. Builds controls only, no window, and closes a menu by
    /// raising its Closed event, since a real open needs a real desktop.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class LazyContextMenuTests
    {
        private static readonly Action<Exception> FailOnError = ex => Assert.Fail("unexpected: " + ex);

        private static void RaiseClosed(ContextMenu menu)
        {
            menu.RaiseEvent(new System.Windows.RoutedEventArgs(ContextMenu.ClosedEvent, menu));
        }

        [Test]
        public void Attach_BuildsNothing_AndAllRowsShareOnePlaceholder()
        {
            int builds = 0;
            var first = new Border();
            var second = new Border();

            LazyContextMenu.Attach(first, () => { builds++; return new ContextMenu(); });
            LazyContextMenu.Attach(second, () => { builds++; return new ContextMenu(); });

            Assert.That(builds, Is.EqualTo(0));
            Assert.That(first.ContextMenu, Is.Not.Null, "WPF only opens a menu for an element that already has one");
            Assert.That(first.ContextMenu, Is.SameAs(second.ContextMenu));
        }

        [Test]
        public void Swap_PutsTheBuiltMenuOnTheRow()
        {
            var row = new Border { ContextMenu = LazyContextMenu.Placeholder };
            var built = new ContextMenu();

            var result = LazyContextMenu.Swap(row, () => built, FailOnError);

            Assert.That(result, Is.SameAs(built));
            Assert.That(row.ContextMenu, Is.SameAs(built));
        }

        [Test]
        public void Closing_ReturnsTheRowToThePlaceholder_SoTheMenuCanBeCollected()
        {
            var row = new Border { ContextMenu = LazyContextMenu.Placeholder };
            var built = LazyContextMenu.Swap(row, () => new ContextMenu(), FailOnError);

            RaiseClosed(built);

            Assert.That(row.ContextMenu, Is.SameAs(LazyContextMenu.Placeholder));
        }

        [Test]
        public void EachOpen_BuildsAFreshMenu()
        {
            int builds = 0;
            var row = new Border { ContextMenu = LazyContextMenu.Placeholder };
            Func<ContextMenu> build = () => { builds++; return new ContextMenu(); };

            RaiseClosed(LazyContextMenu.Swap(row, build, FailOnError));
            RaiseClosed(LazyContextMenu.Swap(row, build, FailOnError));

            Assert.That(builds, Is.EqualTo(2));
        }

        // A second right-click can install a newer menu before the older menu's Closed arrives.
        [Test]
        public void AnOlderMenuClosingLate_LeavesTheNewerMenu()
        {
            var row = new Border { ContextMenu = LazyContextMenu.Placeholder };
            var older = LazyContextMenu.Swap(row, () => new ContextMenu(), FailOnError);
            var newer = LazyContextMenu.Swap(row, () => new ContextMenu(), FailOnError);

            RaiseClosed(older);

            Assert.That(row.ContextMenu, Is.SameAs(newer));
        }

        // Handling the event instead would skip the closing event that clears SuppressAutoHide.
        [Test]
        public void AFailedBuild_ReportsTheError_AndStillShowsAMenu()
        {
            Exception reported = null;
            var row = new Border { ContextMenu = LazyContextMenu.Placeholder };

            var menu = LazyContextMenu.Swap(row, () => { throw new InvalidOperationException("boom"); }, ex => reported = ex);

            Assert.That(reported, Is.TypeOf<InvalidOperationException>());
            Assert.That(menu.Items.Count, Is.EqualTo(1));
            Assert.That(((MenuItem)menu.Items[0]).IsEnabled, Is.False);
            Assert.That(row.ContextMenu, Is.SameAs(menu));
        }

        [Test]
        public void ABuilderThatReturnsNothing_StillShowsAMenu()
        {
            var row = new Border { ContextMenu = LazyContextMenu.Placeholder };

            var menu = LazyContextMenu.Swap(row, () => null, FailOnError);

            Assert.That(menu.Items.Count, Is.EqualTo(1));
        }
    }
}
