using NUnit.Framework;
using PowerPane.Services;
using PowerPane.UI;
using PowerPane.UI.Settings.Pages;

namespace PowerPane.Tests
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

        // #204: the row, the pane's header, and the Settings page all use one name.
        [Test]
        public void SshSection_IsNamedAfterItsSettingsPage()
        {
            Assert.That(SectionsPage.Friendly("ssh"), Is.EqualTo(SshSection.DefaultTitle));
            Assert.That(SshSection.DefaultTitle, Is.EqualTo(new SshPage().Title));
        }
    }
}
