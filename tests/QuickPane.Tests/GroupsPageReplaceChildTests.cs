using System.Threading;
using System.Windows.Controls;
using NUnit.Framework;
using QuickPane.UI.Settings.Pages;

namespace QuickPane.Tests
{
    /// <summary>The Groups page swaps each profile's "Loading groups…" placeholder for its loaded
    /// card. Assigning over the occupied slot threw on every Settings open (#143). Builds controls
    /// only; no window.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class GroupsPageReplaceChildTests
    {
        private static StackPanel ThreeRows(out TextBlock a, out TextBlock b, out TextBlock c)
        {
            var panel = new StackPanel();
            a = new TextBlock { Text = "a" };
            b = new TextBlock { Text = "b" };
            c = new TextBlock { Text = "c" };
            panel.Children.Add(a);
            panel.Children.Add(b);
            panel.Children.Add(c);
            return panel;
        }

        [Test]
        public void ReplaceChild_SwapsTheOccupiedSlotInPlace()
        {
            TextBlock a, b, c;
            var panel = ThreeRows(out a, out b, out c);
            var loaded = new TextBlock { Text = "b loaded" };

            GroupsPage.ReplaceChild(panel, 1, loaded);

            Assert.That(panel.Children.Count, Is.EqualTo(3));
            Assert.That(panel.Children[0], Is.SameAs(a));
            Assert.That(panel.Children[1], Is.SameAs(loaded));
            Assert.That(panel.Children[2], Is.SameAs(c));
            Assert.That(b.Parent, Is.Null, "the placeholder is detached");
        }

        [Test]
        public void ReplaceChild_IgnoresASlotThatNoLongerExists()
        {
            TextBlock a, b, c;
            var panel = ThreeRows(out a, out b, out c);

            GroupsPage.ReplaceChild(panel, 3, new TextBlock());
            GroupsPage.ReplaceChild(panel, -1, new TextBlock());

            Assert.That(panel.Children.Count, Is.EqualTo(3));
            Assert.That(panel.Children[2], Is.SameAs(c));
        }
    }
}
