using System.Windows;
using NUnit.Framework;
using PowerPane.UI.Settings;

namespace PowerPane.Tests
{
    /// <summary>The one layout decision in SettingsWindow.cs that can be tested with no window at
    /// all: how much margin compensates for the resize-frame overhang WindowChrome leaves when
    /// maximized. Every testable decision lives in an internal static method.</summary>
    [TestFixture]
    public class SettingsWindowTests
    {
        [Test]
        public void RootMarginFor_Maximized_UsesResizeBorderThickness()
        {
            var border = new Thickness(7);
            Assert.That(SettingsWindow.RootMarginFor(WindowState.Maximized, border), Is.EqualTo(border));
        }

        [TestCase(WindowState.Normal)]
        [TestCase(WindowState.Minimized)]
        public void RootMarginFor_NotMaximized_IsZero(WindowState state)
        {
            var border = new Thickness(7);
            Assert.That(SettingsWindow.RootMarginFor(state, border), Is.EqualTo(new Thickness(0)));
        }

        // #97: the two timing log lines are the only part of the instrumentation that
        // is a pure decision, so they are the only part covered here; the Stopwatch calls around
        // them need a real window and are left to the sandbox's timing measurements.

        [Test]
        public void FormatOpenTiming_IncludesElapsedMilliseconds()
        {
            Assert.That(SettingsWindow.FormatOpenTiming(123), Is.EqualTo("SettingsWindow: opened in 123 ms"));
        }

        [Test]
        public void FormatPageSwitchTiming_IncludesPageIdAndElapsedMilliseconds()
        {
            Assert.That(SettingsWindow.FormatPageSwitchTiming("groups", 45),
                Is.EqualTo("SettingsWindow: switched to page 'groups' in 45 ms"));
        }
    }
}
