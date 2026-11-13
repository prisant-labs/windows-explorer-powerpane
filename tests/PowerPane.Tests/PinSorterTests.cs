using System;
using System.Linq;
using NUnit.Framework;
using PowerPane.Models;
using PowerPane.Services;

namespace PowerPane.Tests
{
    [TestFixture]
    public class PinSorterTests
    {
        private static PinnedFolder Pin(string name, int order = 1, DateTime? added = null, string link = null)
        {
            return new PinnedFolder
            {
                DisplayName = name, Order = order,
                AddedUtc = added ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LinkPath = link ?? (@"C:\Demo\Groups\" + order.ToString("000") + "_" + name + ".lnk")
            };
        }

        [TestCase("name", PinSortMode.Name)]
        [TestCase("NAME ", PinSortMode.Name)]
        [TestCase("recent", PinSortMode.Recent)]
        [TestCase("  Recent\r\n", PinSortMode.Recent)]
        [TestCase("", PinSortMode.Manual)]
        [TestCase(null, PinSortMode.Manual)]
        [TestCase("garbage", PinSortMode.Manual)]
        public void ParseMarker_ReadsKnownValuesAndTreatsTheRestAsManual(string text, PinSortMode expected)
        {
            Assert.That(PinSorter.ParseMarker(text), Is.EqualTo(expected));
        }

        [Test]
        public void MarkerText_RoundTripsAndManualHasNoMarker()
        {
            Assert.That(PinSorter.MarkerText(PinSortMode.Name), Is.EqualTo("name"));
            Assert.That(PinSorter.MarkerText(PinSortMode.Recent), Is.EqualTo("recent"));
            Assert.That(PinSorter.MarkerText(PinSortMode.Manual), Is.Null);
            Assert.That(PinSorter.ParseMarker(PinSorter.MarkerText(PinSortMode.Name)), Is.EqualTo(PinSortMode.Name));
            Assert.That(PinSorter.ParseMarker(PinSorter.MarkerText(PinSortMode.Recent)), Is.EqualTo(PinSortMode.Recent));
        }

        [Test]
        public void CompareNames_IsNaturalAndCaseInsensitive()
        {
            Assert.That(PinSorter.CompareNames("Item 2", "Item 10"), Is.LessThan(0));
            Assert.That(PinSorter.CompareNames("Item 10", "Item 2"), Is.GreaterThan(0));
            Assert.That(PinSorter.CompareNames("apollo", "Apollo"), Is.EqualTo(0));
            Assert.That(PinSorter.CompareNames("Zephyr", "Apollo"), Is.GreaterThan(0));
            Assert.That(PinSorter.CompareNames(null, "Apollo"), Is.LessThan(0));
        }

        [Test]
        public void Order_Name_IsNaturalWithTiesBrokenByStoredOrderThenPath()
        {
            var pins = new[]
            {
                Pin("Zephyr", 1), Pin("Item 10", 2), Pin("apollo", 4), Pin("Item 2", 3), Pin("Apollo", 5)
            };
            var names = PinSorter.Order(pins, PinSortMode.Name).Select(p => p.DisplayName).ToArray();
            // "apollo" and "Apollo" tie on name, so the stored order (4 before 5) decides.
            Assert.That(names, Is.EqualTo(new[] { "apollo", "Apollo", "Item 2", "Item 10", "Zephyr" }));

            // Same name and same stored order: the path decides, whatever the input order was.
            var a = Pin("Apollo", 1, link: @"C:\Demo\Groups\a\001_Apollo.lnk");
            var b = Pin("Apollo", 1, link: @"C:\Demo\Groups\b\001_Apollo.lnk");
            Assert.That(PinSorter.Order(new[] { b, a }, PinSortMode.Name).First(), Is.SameAs(a));
        }

        [Test]
        public void Order_Recent_IsNewestFirstWithTiesByName()
        {
            var t = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
            var pins = new[]
            {
                Pin("Old", 1, t), Pin("Newest", 2, t.AddDays(2)), Pin("Mid B", 3, t.AddDays(1)), Pin("Mid A", 4, t.AddDays(1))
            };
            var names = PinSorter.Order(pins, PinSortMode.Recent).Select(p => p.DisplayName).ToArray();
            Assert.That(names, Is.EqualTo(new[] { "Newest", "Mid A", "Mid B", "Old" }));
        }

        [Test]
        public void Order_Manual_IsTheStoredOrder()
        {
            var pins = new[] { Pin("Zephyr", 1), Pin("Apollo", 3), Pin("Item 2", 2) };
            var names = PinSorter.Order(pins, PinSortMode.Manual).Select(p => p.DisplayName).ToArray();
            Assert.That(names, Is.EqualTo(new[] { "Zephyr", "Item 2", "Apollo" }));
        }

        [Test]
        public void Order_NullInput_GivesAnEmptyList()
        {
            Assert.That(PinSorter.Order(null, PinSortMode.Name), Is.Empty);
        }

        // Rows on screen are [A, B, C, D]. A line is numbered by the row below it, so line 4 is the
        // bottom.
        private static string Placed(int dragged, int line)
        {
            var shown = new[] { Pin("A", 1), Pin("B", 2), Pin("C", 3), Pin("D", 4) };
            var result = PinSorter.PlaceAtLine(shown, shown[dragged], line);
            return result == null ? "unchanged" : string.Concat(result.Select(p => p.DisplayName));
        }

        [Test]
        public void PlaceAtLine_DraggingDown_LandsAtTheLineAndNotOneRowTooFar()
        {
            Assert.That(Placed(0, 3), Is.EqualTo("BCAD"));   // A to the line between C and D
            Assert.That(Placed(0, 4), Is.EqualTo("BCDA"));   // A to the bottom
        }

        [Test]
        public void PlaceAtLine_DraggingUp_LandsAtTheLine()
        {
            Assert.That(Placed(3, 1), Is.EqualTo("ADBC"));
            Assert.That(Placed(2, 0), Is.EqualTo("CABD"));
        }

        [Test]
        public void PlaceAtLine_ADropThatChangesNothing_GivesNull()
        {
            Assert.That(Placed(1, 1), Is.EqualTo("unchanged"));   // the line just above its own row
            Assert.That(Placed(1, 2), Is.EqualTo("unchanged"));   // the line just below its own row
            Assert.That(Placed(3, 4), Is.EqualTo("unchanged"));
            Assert.That(Placed(0, 0), Is.EqualTo("unchanged"));
        }
    }
}
