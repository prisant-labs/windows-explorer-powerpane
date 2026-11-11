using NUnit.Framework;
using PowerPane.UI.Settings;

namespace PowerPane.Tests
{
    /// <summary>The one decision in SettingsCards.cs that can be tested with no window: where a
    /// card's control moves under its title instead of staying beside it (below 476 px of card
    /// width). Every testable decision lives in an internal static method.</summary>
    [TestFixture]
    public class SettingsCardsTests
    {
        [TestCase(475, true)]
        [TestCase(476, false)]
        [TestCase(477, false)]
        [TestCase(1200, false)]
        [TestCase(0, true)]
        public void StackControl_WrapsBelow476Px(double width, bool expectStacked)
        {
            Assert.That(SettingsCards.StackControl(width), Is.EqualTo(expectStacked));
        }
    }
}
