// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A fixed-capacity least-recently-used cache. Reads and writes are O(1). When a write would exceed
    /// the capacity, the least recently used entry is evicted. Both reads (<see cref="TryGet"/>) and
    /// writes (<see cref="Set"/>) count as "use" and refresh recency; <see cref="ContainsKey"/> and
    /// <see cref="Peek"/> do not. Not thread-safe.
    /// </summary>
    public sealed class LruCache<TKey, TValue>
        where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
        // Most-recently-used at the head, least-recently-used at the tail.
        private readonly LinkedList<Entry> _order = new LinkedList<Entry>();

        public LruCache(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            _capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<Entry>>(capacity);
        }

        public int Capacity => _capacity;

        public int Count => _map.Count;

        /// <summary>Keys ordered most-recently-used first. Enumerating does not affect recency.</summary>
        public IReadOnlyList<TKey> Keys
        {
            get
            {
                var keys = new List<TKey>(_map.Count);
                for (var node = _order.First; node is not null; node = node.Next)
                    keys.Add(node.Value.Key);
                return keys;
            }
        }

        /// <summary>Looks up a key, refreshing its recency on a hit.</summary>
        public bool TryGet(TKey key, out TValue value)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }

        /// <summary>Inserts or updates a key, marking it most-recently-used and evicting the LRU entry if over capacity.</summary>
        public void Set(TKey key, TValue value)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var node))
            {
                node.Value = new Entry(key, value);
                _order.Remove(node);
                _order.AddFirst(node);
                return;
            }

            var fresh = new LinkedListNode<Entry>(new Entry(key, value));
            _order.AddFirst(fresh);
            _map[key] = fresh;

            if (_map.Count > _capacity)
            {
                var lru = _order.Last!;
                _order.RemoveLast();
                _map.Remove(lru.Value.Key);
            }
        }

        /// <summary>Tests membership without affecting recency.</summary>
        public bool ContainsKey(TKey key)
        {
            ThrowIfKeyNull(key);
            return _map.ContainsKey(key);
        }

        /// <summary>Reads a value without affecting recency.</summary>
        public bool Peek(TKey key, out TValue value)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var node))
            {
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }

        /// <summary>Removes a key if present. Returns whether anything was removed.</summary>
        public bool Remove(TKey key)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _map.Remove(key);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            _map.Clear();
            _order.Clear();
        }

        // Guard.NotNull carries a class constraint, so it cannot validate a possibly-value-type TKey.
        // This is the documented inline-null-throw fallback for generic keys.
        private static void ThrowIfKeyNull(TKey key)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));
        }

        private readonly struct Entry
        {
            public Entry(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }

            public TKey Key { get; }
            public TValue Value { get; }
        }
    }
}
