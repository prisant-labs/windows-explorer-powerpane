using System;
using System.Collections.Generic;

namespace PowerPane.Services
{
    /// <summary>
    /// A dictionary with a size cap that, when full, drops the entry used longest ago. The icon
    /// cache used to clear itself completely at 600 entries, so every visible row re-extracted its
    /// icon at once (prisant-labs/windows-explorer-powerpane#47), and the path-status cache never
    /// shrank at all (#48). Not thread-safe: each caller already guards its cache with a lock.
    /// </summary>
    internal sealed class LruCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map;
        // Most recently used first.
        private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new LinkedList<KeyValuePair<TKey, TValue>>();

        public LruCache(int capacity, IEqualityComparer<TKey> comparer = null)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity");
            _capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public int Count { get { return _map.Count; } }

        /// <summary>Look up a key and mark it as just used.</summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            LinkedListNode<KeyValuePair<TKey, TValue>> node;
            if (_map.TryGetValue(key, out node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default(TValue);
            return false;
        }

        /// <summary>Add or replace a value, marking it as just used, and drop the entry used longest
        /// ago if the cache is now over its cap.</summary>
        public void Set(TKey key, TValue value)
        {
            LinkedListNode<KeyValuePair<TKey, TValue>> node;
            if (_map.TryGetValue(key, out node)) _order.Remove(node);
            node = new LinkedListNode<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(key, value));
            _order.AddFirst(node);
            _map[key] = node;
            if (_map.Count > _capacity)
            {
                var last = _order.Last;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }

        public bool Remove(TKey key)
        {
            LinkedListNode<KeyValuePair<TKey, TValue>> node;
            if (!_map.TryGetValue(key, out node)) return false;
            _order.Remove(node);
            _map.Remove(key);
            return true;
        }

        public void Clear()
        {
            _map.Clear();
            _order.Clear();
        }
    }
}
