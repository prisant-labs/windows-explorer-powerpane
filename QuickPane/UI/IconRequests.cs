using System;
using System.Collections.Generic;

namespace QuickPane.UI
{
    /// <summary>
    /// Tracks which targets are waiting on a keyed background result, so a slow extraction that two
    /// rows both asked for runs once and reaches both (prisant-labs/windows-explorer-quickpane#76), and
    /// a storm of distinct misses cannot grow this table without bound while a cache refill is catching
    /// up (#78). Pure logic: the caller supplies its own lock, as IconHelper does with its Gate.
    /// </summary>
    internal sealed class IconRequests<T> where T : class
    {
        // Bounded separately from the icon cache: a path can be in flight before it is ever cached, so
        // without its own cap a burst of distinct misses (an expanded folder with thousands of
        // children) would grow this table forever even though the cache itself stays at 600 (#47).
        // A refused row keeps its placeholder until the next rebuild, so the cap sits well above any
        // single folder a person expands: the same 4,096 as the path-status cache (#48).
        internal const int MaxKeys = 4096;
        private readonly Dictionary<string, List<WeakReference<T>>> _waiters =
            new Dictionary<string, List<WeakReference<T>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registers target as waiting on key. Returns true only for the first waiter on that
        /// key, so the caller queues one extraction per key instead of one per row. A key past the cap
        /// is refused: the row that asked keeps its placeholder instead of growing this table further.</summary>
        public bool Add(string key, T target)
        {
            List<WeakReference<T>> list;
            if (_waiters.TryGetValue(key, out list))
            {
                list.Add(new WeakReference<T>(target));
                return false;
            }
            if (_waiters.Count >= MaxKeys) return false;
            list = new List<WeakReference<T>>();
            list.Add(new WeakReference<T>(target));
            _waiters[key] = list;
            return true;
        }

        /// <summary>Removes key and returns the targets still alive. Weak references mean a row whose
        /// pane already closed is never kept alive just because an icon for it was still in flight.</summary>
        public List<T> Complete(string key)
        {
            var result = new List<T>();
            List<WeakReference<T>> list;
            if (_waiters.TryGetValue(key, out list))
            {
                foreach (var w in list)
                {
                    T t;
                    if (w.TryGetTarget(out t)) result.Add(t);
                }
                _waiters.Remove(key);
            }
            return result;
        }
    }
}
