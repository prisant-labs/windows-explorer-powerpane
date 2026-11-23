using NUnit.Framework;
using PowerPane.Services;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers the icon gate's decision for a slow-volume path (#139): extract once a probe found the
    /// path alive, drop once it found it gone, and wait, rather than give up, while no probe has
    /// answered. Before #139 a path with no probe kept the generic icon for good on rows that a probe
    /// never rebuilds. No path is probed here.
    /// </summary>
    [TestFixture]
    public class IconGateTests
    {
        [Test]
        public void StepFor_ProbeFoundPathAlive_Extracts()
        {
            Assert.That(IconHelper.StepFor(PathStatus.ProbeState.Alive), Is.EqualTo(IconHelper.GateStep.Extract));
        }

        [Test]
        public void StepFor_ProbeFoundPathGone_Drops()
        {
            Assert.That(IconHelper.StepFor(PathStatus.ProbeState.Gone), Is.EqualTo(IconHelper.GateStep.Drop));
        }

        [Test]
        public void StepFor_NoProbeYet_Waits()
        {
            Assert.That(IconHelper.StepFor(PathStatus.ProbeState.NotYet), Is.EqualTo(IconHelper.GateStep.Wait));
        }

        [TestCase(null)]
        [TestCase("")]
        public void State_EmptyPath_IsGone(string path)
        {
            // An empty path can never answer, so the gate must not wait on it forever.
            Assert.That(PathStatus.State(path), Is.EqualTo(PathStatus.ProbeState.Gone));
        }

        [Test]
        public void State_PathNeverAskedAbout_IsNotYet()
        {
            // State only reads the cache: it neither probes nor queues a probe.
            Assert.That(PathStatus.State(@"\\demo-server\never-asked\" + System.Guid.NewGuid().ToString("N")),
                Is.EqualTo(PathStatus.ProbeState.NotYet));
        }

        [Test]
        public void ConfirmedAlive_PathNeverAskedAbout_IsFalse()
        {
            Assert.That(PathStatus.ConfirmedAlive(@"\\demo-server\never-asked\" + System.Guid.NewGuid().ToString("N")), Is.False);
        }
    }
}
