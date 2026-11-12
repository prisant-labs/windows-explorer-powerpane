using NUnit.Framework;
using PowerPane.Explorer;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers a size decision behind the inside pane in a file dialog: fitting a child control
    /// back inside its shifted parent (#238). It needs no window.
    /// </summary>
    [TestFixture]
    public class DialogInsidePaneTests
    {
        [Test]
        public void FittedWidth_BandContainerWiderThanItsShiftedParent_EndsAtTheParentsEdge()
        {
            // Measured in Windows Sandbox: the top bar's container is 964 px wide after the shift, and
            // the band container inside it kept the 1184 px it took when the dialog was 220 px wider.
            Assert.That(DialogInsidePane.FittedWidth(228, 228 + 1184, 228, 228 + 964), Is.EqualTo(964));
        }

        [Test]
        public void FittedWidth_ChildThatFits_KeepsItsWidth()
        {
            Assert.That(DialogInsidePane.FittedWidth(300, 900, 228, 1192), Is.EqualTo(600));
            Assert.That(DialogInsidePane.FittedWidth(228, 1192, 228, 1192), Is.EqualTo(964));
        }

        [Test]
        public void FittedWidth_ChildStartingOutsideItsParent_IsLeftAlone()
        {
            Assert.That(DialogInsidePane.FittedWidth(100, 1400, 228, 1192), Is.EqualTo(1300));
            Assert.That(DialogInsidePane.FittedWidth(1200, 1400, 228, 1192), Is.EqualTo(200));
        }
    }
}
