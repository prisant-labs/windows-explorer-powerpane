using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PowerPane.UI;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers IconRequests, which replaced the single "key already extracting" flag with one that also
    /// tracks every row waiting on that key, so a slow extraction two rows both asked for runs once and
    /// reaches both (prisant-labs/windows-explorer-powerpane#76), and caps the table so a storm of
    /// distinct misses cannot grow it forever while a cache refill catches up (#78).
    /// </summary>
    [TestFixture]
    public class IconRequestsTests
    {
        [Test]
        public void Add_FirstWaiterOnAKey_ReturnsTrue()
        {
            var requests = new IconRequests<object>();
            Assert.That(requests.Add("k", new object()), Is.True);
        }

        [Test]
        public void Add_SecondWaiterOnTheSameKey_ReturnsFalse()
        {
            var requests = new IconRequests<object>();
            requests.Add("k", new object());
            Assert.That(requests.Add("k", new object()), Is.False);
        }

        [Test]
        public void Complete_ReturnsEveryWaiterStillAlive_AndRemovesTheKey()
        {
            var requests = new IconRequests<object>();
            var a = new object();
            var b = new object();
            requests.Add("k", a);
            requests.Add("k", b);

            var result = requests.Complete("k");

            Assert.That(result, Is.EquivalentTo(new[] { a, b }));
            // The key is gone, so a new waiter on it is first again.
            Assert.That(requests.Add("k", new object()), Is.True);
        }

        [Test]
        public void Complete_UnknownKey_ReturnsEmpty()
        {
            var requests = new IconRequests<object>();
            Assert.That(requests.Complete("missing"), Is.Empty);
        }

        [Test]
        public void Add_PastTheCap_IsRefused()
        {
            var requests = new IconRequests<object>();
            // Held for the life of the test: nothing here relies on garbage collection, which would be
            // flaky under a test runner.
            var held = new List<object>();
            for (int i = 0; i < IconRequests<object>.MaxKeys; i++)
            {
                var target = new object();
                held.Add(target);
                Assert.That(requests.Add("k" + i, target), Is.True);
            }

            var extra = new object();
            held.Add(extra);
            Assert.That(requests.Add("one-more-key", extra), Is.False);
        }

        [Test]
        public void IconHelper_DrainsAtMostSixteenIconsPerBatch()
        {
            // The cap that lets other general-lane work (scans, probes, drops) get a turn between
            // batches instead of one task holding the lane until every pending icon is done (#76).
            var field = typeof(IconHelper).GetField("BatchSize", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            Assert.That(field.GetValue(null), Is.EqualTo(16));
        }
    }
}
