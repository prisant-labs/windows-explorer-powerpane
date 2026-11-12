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
    }
}
