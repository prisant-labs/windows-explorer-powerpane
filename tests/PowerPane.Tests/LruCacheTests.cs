using System;
using NUnit.Framework;
using PowerPane.Services;

namespace PowerPane.Tests
{
    /// <summary>
    /// Covers LruCache, which replaced the icon cache's clear-everything-at-600 rule (#47) and caps
    /// the path-status cache (#48).
    /// </summary>
    [TestFixture]
    public class LruCacheTests
    {
        [Test]
        public void Set_OverCapacity_DropsTheLeastRecentlyUsed()
        {
            var cache = new LruCache<string, int>(2);
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);

            int v;
            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.TryGetValue("a", out v), Is.False);
            Assert.That(cache.TryGetValue("b", out v) && v == 2, Is.True);
            Assert.That(cache.TryGetValue("c", out v) && v == 3, Is.True);
        }

        [Test]
        public void TryGetValue_MarksTheEntryAsUsed()
        {
            // The bug this replaces: a full icon cache threw away icons that were on screen a moment ago.
            var cache = new LruCache<string, int>(2);
            cache.Set("a", 1);
            cache.Set("b", 2);
            int v;
            cache.TryGetValue("a", out v);
            cache.Set("c", 3);

            Assert.That(cache.TryGetValue("a", out v), Is.True);
            Assert.That(cache.TryGetValue("b", out v), Is.False);
        }

        [Test]
        public void Set_ExistingKey_ReplacesWithoutGrowing()
        {
            var cache = new LruCache<string, int>(2);
            cache.Set("a", 1);
            cache.Set("a", 5);

            int v;
            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.TryGetValue("a", out v) && v == 5, Is.True);
        }

        [Test]
        public void Comparer_IsUsed()
        {
            var cache = new LruCache<string, int>(4, StringComparer.OrdinalIgnoreCase);
            cache.Set(@"C:\Demo\Projects", 1);
            int v;
            Assert.That(cache.TryGetValue(@"c:\demo\projects", out v), Is.True);
        }

        [Test]
        public void Remove_And_Clear()
        {
            var cache = new LruCache<string, int>(4);
            cache.Set("a", 1);
            cache.Set("b", 2);
            Assert.That(cache.Remove("a"), Is.True);
            Assert.That(cache.Remove("a"), Is.False);
            Assert.That(cache.Count, Is.EqualTo(1));
            cache.Clear();
            Assert.That(cache.Count, Is.EqualTo(0));
        }

        [Test]
        public void Constructor_ZeroCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
        }
    }
}
