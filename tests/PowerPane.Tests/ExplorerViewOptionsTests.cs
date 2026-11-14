using System;
using NUnit.Framework;
using PowerPane.Services;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>The View > Show toggles, without the registry: reading and writing it from a test would
    /// change the Explorer settings of whoever runs the tests.</summary>
    [TestFixture]
    public class ExplorerViewOptionsTests
    {
        [TestCase(ExplorerViewOptions.HiddenItems, 1, true)]
        [TestCase(ExplorerViewOptions.HiddenItems, 2, false)]
        [TestCase(ExplorerViewOptions.HiddenItems, null, false)]
        [TestCase(ExplorerViewOptions.Extensions, 0, true)]
        [TestCase(ExplorerViewOptions.Extensions, 1, false)]
        [TestCase(ExplorerViewOptions.Extensions, null, false)]
        [TestCase(ExplorerViewOptions.CheckBoxes, 1, true)]
        [TestCase(ExplorerViewOptions.CheckBoxes, 0, false)]
        [TestCase(ExplorerViewOptions.CompactView, 1, true)]
        [TestCase(ExplorerViewOptions.CompactView, null, false)]
        public void StateFromAdvanced_ReadsEachValueTheWayExplorerWritesIt(string id, object value, bool expected)
        {
            Assert.That(ExplorerViewOptions.StateFromAdvanced(id, value), Is.EqualTo(expected));
        }

        [Test]
        public void NavPane_MissingValueMeansShown()
        {
            Assert.That(ExplorerViewOptions.NavPaneShown(null), Is.True);
            Assert.That(ExplorerViewOptions.NavPaneShown(new byte[] { 0xA0, 0, 0 }), Is.True);
        }

        [Test]
        public void WithNavPane_FlipsOnlyItsByte()
        {
            var blob = new byte[] { 0xA0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0x2C, 1, 0, 0 };
            var off = ExplorerViewOptions.WithNavPane(blob, false);
            Assert.That(ExplorerViewOptions.NavPaneShown(off), Is.False);
            off[ExplorerViewOptions.NavPaneByte] = 1;
            Assert.That(off, Is.EqualTo(blob), "every other byte, the pane's width included, is kept");
            Assert.That(blob[ExplorerViewOptions.NavPaneByte], Is.EqualTo(1), "the input is not changed");
        }

        [Test]
        public void WithNavPane_StartsFromExplorersDefaultWhenMissing()
        {
            var on = ExplorerViewOptions.WithNavPane(null, true);
            var off = ExplorerViewOptions.WithNavPane(new byte[2], false);
            Assert.That(ExplorerViewOptions.NavPaneShown(on), Is.True);
            Assert.That(ExplorerViewOptions.NavPaneShown(off), Is.False);
            Assert.That(on.Length, Is.EqualTo(off.Length));
        }

        // The bit positions were confirmed against SHGetSetSettings in Windows Sandbox.
        [TestCase(0x1u, true, 1, 0)]
        [TestCase(0x2u, true, 2, 0)]
        [TestCase(0x00800000u, true, 0, 8)]
        [TestCase(0x1u, false, 0, 0)]
        [TestCase(0x00800000u, false, 0, 0)]
        public void EncodeShellState_SetsOnlyTheMaskedBit(uint mask, bool on, int first, int second)
        {
            var buffer = new byte[ExplorerViewOptions.ShellStateSize];
            for (int i = 0; i < buffer.Length; i++) buffer[i] = 0xFF;
            ExplorerViewOptions.EncodeShellState(buffer, mask, on);
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(first));
            Assert.That(BitConverter.ToInt32(buffer, ExplorerViewOptions.SecondFlagsOffset), Is.EqualTo(second));
        }

        [Test]
        public void EveryToggle_StartsOffAndStaysOutOfPinnedRows()
        {
            int toggles = 0;
            foreach (var def in QuickActionLibrary.All)
            {
                if (def.Kind != QuickActionKind.Toggle) continue;
                toggles++;
                Assert.That(def.DefaultEnabled, Is.False, def.Id);
                Assert.That(ExplorerViewOptions.IsViewOption(def.Id), Is.True, def.Id);
                Assert.That(QuickActionLibrary.AppliesToPinnedRow(def), Is.False, def.Id);
                Assert.That(QuickActionLibrary.UsesSelection(def), Is.False, def.Id);
            }
            Assert.That(toggles, Is.EqualTo(5));
        }

        [Test]
        public void ToggleGlyph_DiffersByState()
        {
            Assert.That(QuickActionsSection.ToggleGlyph(true), Is.Not.EqualTo(QuickActionsSection.ToggleGlyph(false)));
        }
    }
}
