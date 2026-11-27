using System;
using System.Linq;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// The display order and drop rules for pins pinned to the top of a tab (#301). The names are
    /// invented, and no file is touched.
    /// </summary>
    [TestFixture]
    public class PinToTopSorterTests
    {
        private static readonly DateTime Day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static PinnedFolder Pin(string name, int order, bool top = false, int addedDay = 0)
        {
            return new PinnedFolder
            {
                DisplayName = name, Order = order, PinnedToTop = top, AddedUtc = Day.AddDays(addedDay),
                LinkPath = @"C:\Demo\Groups\" + order.ToString("000") + (top ? "!" : "") + "_" + name + ".lnk"
            };
        }

        private static string Names(System.Collections.Generic.IEnumerable<PinnedFolder> pins)
        {
            return string.Join(",", pins.Select(p => p.DisplayName + (p.PinnedToTop ? "*" : "")));
        }

        [TestCase(PinSortMode.Manual)]
        [TestCase(PinSortMode.Name)]
        [TestCase(PinSortMode.Recent)]
        public void Order_WithNoPinnedPin_IsTheSameAsBefore(PinSortMode mode)
        {
            var pins = new[] { Pin("Delta", 1, addedDay: 3), Pin("Apollo", 2, addedDay: 1), Pin("Contoso", 3, addedDay: 2) };
            string expected;
            switch (mode)
            {
                case PinSortMode.Name: expected = "Apollo,Contoso,Delta"; break;
                case PinSortMode.Recent: expected = "Delta,Contoso,Apollo"; break;
                default: expected = "Delta,Apollo,Contoso"; break;
            }
            Assert.That(Names(PinSorter.Order(pins, mode)), Is.EqualTo(expected));
        }

        [Test]
        public void Order_Manual_PutsPinnedPinsFirstInStoredOrder()
        {
            var pins = new[] { Pin("Apollo", 1), Pin("Borealis", 2, true), Pin("Contoso", 3), Pin("Delta", 4, true) };
            Assert.That(Names(PinSorter.Order(pins, PinSortMode.Manual)), Is.EqualTo("Borealis*,Delta*,Apollo,Contoso"));
        }

        [Test]
        public void Order_Name_SortsEachPartAToZ_SoALateNameStaysAboveAnEarlyOne()
        {
            var pins = new[] { Pin("Apollo", 1), Pin("Zephyr", 2, true), Pin("Contoso", 3), Pin("Mango", 4, true) };
            Assert.That(Names(PinSorter.Order(pins, PinSortMode.Name)), Is.EqualTo("Mango*,Zephyr*,Apollo,Contoso"));
        }

        [Test]
        public void Order_Recent_SortsEachPartNewestFirst()
        {
            var pins = new[]
            {
                Pin("Apollo", 1, addedDay: 9), Pin("Borealis", 2, true, addedDay: 1),
                Pin("Contoso", 3, addedDay: 5), Pin("Delta", 4, true, addedDay: 7)
            };
            Assert.That(Names(PinSorter.Order(pins, PinSortMode.Recent)), Is.EqualTo("Delta*,Borealis*,Apollo,Contoso"));
        }

        [Test]
        public void Order_EqualNamesInTheBand_FallBackToStoredOrder()
        {
            var pins = new[] { Pin("Apollo", 5, true), Pin("Apollo", 2, true), Pin("Apollo", 3) };
            var ordered = PinSorter.Order(pins, PinSortMode.Name);
            Assert.That(ordered.Select(p => p.Order), Is.EqualTo(new[] { 2, 5, 3 }));
        }

        // Pinned A and B over ordinary C and D, as on screen.
        private static PinnedFolder[] Shown()
        {
            return new[] { Pin("A", 1, true), Pin("B", 2, true), Pin("C", 3), Pin("D", 4) };
        }

        private static string Dropped(int dragged, int line)
        {
            var shown = Shown();
            var result = PinSorter.PlaceAtLineInBands(shown, shown[dragged], line);
            return result == null ? "unchanged" : Names(result);
        }

        [Test]
        public void PlaceAtLineInBands_APinnedPinDroppedInsideOrAtTheEndOfTheBand_StaysPinned()
        {
            Assert.That(Dropped(0, 2), Is.EqualTo("B*,A*,C,D"));   // the line between B and C
            Assert.That(Dropped(1, 0), Is.EqualTo("B*,A*,C,D"));   // the line above A
        }

        [Test]
        public void PlaceAtLineInBands_APinnedPinDroppedBelowTheBand_IsUnpinned()
        {
            Assert.That(Dropped(1, 3), Is.EqualTo("A*,C,B,D"));   // the line between C and D
            Assert.That(Dropped(0, 4), Is.EqualTo("B*,C,D,A"));   // the bottom
        }

        [Test]
        public void PlaceAtLineInBands_AnOrdinaryPinDroppedInsideTheBand_IsPinned()
        {
            Assert.That(Dropped(2, 0), Is.EqualTo("C*,A*,B*,D"));
            Assert.That(Dropped(3, 1), Is.EqualTo("A*,D*,B*,C"));
        }

        [Test]
        public void PlaceAtLineInBands_AnOrdinaryPinDroppedAtTheEdge_StaysOrdinary()
        {
            Assert.That(Dropped(3, 2), Is.EqualTo("A*,B*,D,C"));   // the line between B and C
        }

        [Test]
        public void PlaceAtLineInBands_ADropThatChangesNothing_GivesNull()
        {
            Assert.That(Dropped(2, 2), Is.EqualTo("unchanged"));   // C just above itself, at the edge
            Assert.That(Dropped(2, 3), Is.EqualTo("unchanged"));   // C just below itself
            Assert.That(Dropped(1, 2), Is.EqualTo("unchanged"));   // B just below itself, still in the band
        }

        [Test]
        public void PlaceAtLineInBands_OnlyTheDraggedPinChanges_AndTheOriginalIsLeftAlone()
        {
            var shown = Shown();
            var result = PinSorter.PlaceAtLineInBands(shown, shown[3], 0);   // D into the band

            Assert.That(shown[3].PinnedToTop, Is.False, "the shown pin is not changed in place");
            Assert.That(result.Single(p => p.DisplayName == "D").PinnedToTop, Is.True);
            Assert.That(result.Single(p => p.DisplayName == "D").LinkPath, Is.EqualTo(shown[3].LinkPath));
            Assert.That(result.Where(p => p.DisplayName != "D").All(p => shown.Contains(p)), Is.True);
        }

        [TestCase(0, false, 0, false)]   // no band: nothing becomes pinned
        [TestCase(2, false, 1, true)]
        [TestCase(2, false, 2, false)]   // the edge is outside the band for an ordinary pin
        [TestCase(2, true, 2, true)]     // and inside it for a pinned one
        [TestCase(2, true, 3, false)]
        [TestCase(0, true, 0, true)]     // the only pinned pin, dropped at the top
        public void TopAfterDrop_FollowsTheEdgeRule(int pinnedCount, bool wasPinned, int at, bool expected)
        {
            Assert.That(PinSorter.TopAfterDrop(pinnedCount, wasPinned, at), Is.EqualTo(expected));
        }
    }
}
