using System.Collections.Generic;
using NUnit.Framework;
using PowerPane.UI.Settings.Pages;

namespace PowerPane.Tests
{
    /// <summary>The one decision in SshPage.cs that can be tested with no window and no real disk:
    /// which drive letter "Add a connection" picks next (SettingsPanel.xaml.cs's old
    /// NextFreeDriveLetter, lines 864-872). The existence check is injected so this needs no real
    /// drive letters at all. Every testable decision lives in an internal static method.</summary>
    [TestFixture]
    public class Settings94SshPageTests
    {
        [Test]
        public void NextFreeDriveLetter_NoneTaken_ReturnsS()
        {
            Assert.That(SshPage.NextFreeDriveLetter(root => false), Is.EqualTo("S:"));
        }

        [Test]
        public void NextFreeDriveLetter_SomeTaken_SkipsThem()
        {
            var taken = new HashSet<string> { @"S:\", @"T:\", @"U:\" };
            Assert.That(SshPage.NextFreeDriveLetter(root => taken.Contains(root)), Is.EqualTo("V:"));
        }

        [Test]
        public void NextFreeDriveLetter_AllTaken_FallsBackToS()
        {
            Assert.That(SshPage.NextFreeDriveLetter(root => true), Is.EqualTo("S:"));
        }
    }
}
