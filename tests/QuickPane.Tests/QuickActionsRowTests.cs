using NUnit.Framework;
using QuickPane.Services;
using QuickPane.UI.Settings.Pages;

namespace QuickPane.Tests
{
    /// <summary>What a row on the Quick actions settings page shows, with no window.</summary>
    [TestFixture]
    public class QuickActionsRowTests
    {
        // #202: a card reserves the icon's width either way, so an empty glyph is a blank gap.
        [Test]
        public void RowGlyph_EveryBuiltInActionHasAnIcon()
        {
            foreach (var def in QuickActionLibrary.All)
                Assert.That(QuickActionsPage.RowGlyph(def), Is.Not.Null.And.Not.Empty, def.Id);
        }

        [Test]
        public void RowGlyph_AnUnknownActionStillHasAnIcon()
        {
            Assert.That(QuickActionsPage.RowGlyph(null), Is.Not.Null.And.Not.Empty);
        }
    }
}
