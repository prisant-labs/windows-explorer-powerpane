using System.Collections.Generic;
using NUnit.Framework;
using PowerPane.Explorer;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers every combination of the window Mode and the "File dialogs" setting against
    /// ResolveMode, the pure decision DialogPaneWatcher.CurrentMode delegates to. No window, no
    /// Settings instance, and no dialog on screen are needed for any of this.
    /// </summary>
    [TestFixture]
    public class DialogPaneWatcherTests
    {
        // "" , "banana" (an unrecognized value), and null all have to normalize the same way as a
        // setting file nobody has touched yet.
        private static readonly string[] WindowModes = { "inside", "beside", "off", "", "banana", null };
        private static readonly string[] DialogModes = { "same", "beside", "off", "", "banana", null };

        private static IEnumerable<TestCaseData> Combinations()
        {
            foreach (var w in WindowModes)
                foreach (var d in DialogModes)
                    yield return new TestCaseData(w, d).SetName("ResolveMode(window: " + (w ?? "null") + ", dialog: " + (d ?? "null") + ")");
        }

        [TestCaseSource(nameof(Combinations))]
        public void ResolveMode_EveryCombination_MatchesTheDocumentedRule(string windowMode, string dialogMode)
        {
            // The dialog setting overrides the window mode only for "beside" and "off"; everything
            // else (including "same", the default) falls back to the normalized window mode.
            string expected;
            if (dialogMode == "beside") expected = "beside";
            else if (dialogMode == "off") expected = "off";
            else if (windowMode == "beside") expected = "beside";
            else if (windowMode == "off") expected = "off";
            else expected = "inside";

            Assert.That(DialogPaneWatcher.ResolveMode(windowMode, dialogMode), Is.EqualTo(expected));
        }

        // ---- the beside list ----------------------------------------------------------------------

        [Test]
        public void RecordFight_OneDialogDoesNotListTheApp_TheSecondDoes()
        {
            var fights = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            Assert.That(DialogPaneWatcher.RecordFight(fights, "demoapp", DialogPaneWatcher.FightsBeforeBeside), Is.False);
            Assert.That(DialogPaneWatcher.RecordFight(fights, "DemoApp", DialogPaneWatcher.FightsBeforeBeside), Is.True,
                "the process name is matched without case");
            Assert.That(DialogPaneWatcher.RecordFight(fights, "otherapp", DialogPaneWatcher.FightsBeforeBeside), Is.False,
                "each app is counted on its own");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("unknown")]
        public void RecordFight_NeverListsAnUnnamedApp(string owner)
        {
            var fights = new Dictionary<string, int>();
            for (int i = 0; i < 5; i++)
                Assert.That(DialogPaneWatcher.RecordFight(fights, owner, 2), Is.False);
        }

        [Test]
        public void FightsBeforeBeside_IsMoreThanOne()
        {
            Assert.That(DialogPaneWatcher.FightsBeforeBeside, Is.GreaterThan(1));
        }

        [Test]
        public void DescribeActivity_SaysWhatAndHowLongAgo()
        {
            var at = new System.DateTime(2026, 1, 2, 3, 4, 5);
            Assert.That(PowerPane.Services.Log.DescribeActivity(@"dialog navigate 1A2B to C:\Demo", at, at.AddMilliseconds(2140)),
                Is.EqualTo(@"last action ""dialog navigate 1A2B to C:\Demo"", 2.1 s earlier"));
            Assert.That(PowerPane.Services.Log.DescribeActivity(null, at, at), Is.EqualTo("no recorded action"));
            Assert.That(PowerPane.Services.Log.DescribeActivity("idle", at, at.AddSeconds(-1)), Does.EndWith("0.0 s earlier"));
        }
    }
}
