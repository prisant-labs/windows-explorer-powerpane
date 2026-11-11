using NUnit.Framework;
using PowerPane.UI.Settings.Pages;

namespace PowerPane.Tests
{
    /// <summary>The pure decisions #93's two pages (Keyboard, Backup & restore) pull out of their
    /// UI so they are unit-testable with no window, no SettingsStore, and no GroupStore.
    /// See <see cref="KeyboardPage.ValidateHotkeyText"/> and the message formatters on
    /// <see cref="BackupPage"/>.</summary>
    [TestFixture]
    public class Settings93PageTests
    {
        // ---- KeyboardPage.ValidateHotkeyText ----------------------
        //
        // Pins the same contract as the old text box's LostFocus handler at commit 068301b
        // (SettingsPanel.xaml.cs's HotkeysBlock): HotkeyService.TryParse decides accept/revert,
        // and a trimmed value is kept only when it parses.

        [TestCase("ctrl+shift+Q")]
        [TestCase("alt+F4")]
        [TestCase("  ctrl+shift+Q  ")]
        public void ValidateHotkeyText_ValidSpec_IsAcceptedAndTrimmed(string input)
        {
            bool accepted;
            string result = KeyboardPage.ValidateHotkeyText(input, "ctrl+shift+Z", out accepted);

            Assert.That(accepted, Is.True);
            Assert.That(result, Is.EqualTo(input.Trim()));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("garbage")]
        [TestCase("Q")]                 // a bare key with no modifier: HotkeyService.TryParse rejects this on purpose
        [TestCase("ctrl+shift")]        // modifiers but no terminating key
        public void ValidateHotkeyText_InvalidSpec_RevertsToLastValid(string input)
        {
            bool accepted;
            string result = KeyboardPage.ValidateHotkeyText(input, "ctrl+shift+Z", out accepted);

            Assert.That(accepted, Is.False);
            Assert.That(result, Is.EqualTo("ctrl+shift+Z"));
        }

        // ---- BackupPage message formatters ------------------------
        //
        // Pins the same status text the old Back up/Restore handlers at 068301b wrote, now shown
        // in an info bar instead of a status TextBlock.

        [Test]
        public void BackedUpMessage_NamesThePath()
        {
            Assert.That(BackupPage.BackedUpMessage(@"C:\Demo\PowerPane-settings.json"),
                Is.EqualTo(@"Backed up to C:\Demo\PowerPane-settings.json"));
        }

        [Test]
        public void BackupFailedMessage_NamesTheException()
        {
            Assert.That(BackupPage.BackupFailedMessage("disk is full"),
                Is.EqualTo("Backup failed: disk is full"));
        }

        [Test]
        public void RestoreFailedMessage_MatchesTheOldText()
        {
            Assert.That(BackupPage.RestoreFailedMessage, Is.EqualTo("Restore failed: the file could not be read."));
        }

        [Test]
        public void RestoredMessage_MatchesTheOldText()
        {
            Assert.That(BackupPage.RestoredMessage, Is.EqualTo("Settings restored."));
        }
    }
}
