// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A generic dictionary that remembers insertion order: enumeration (and <see cref="Keys"/> /
    /// <see cref="Values"/>) yields entries in the order they were first added, while lookups, inserts,
    /// and removals stay O(1). Updating an existing key's value keeps its original position. Not
    /// thread-safe.
    /// </summary>
    public sealed class OrderedDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
        where TKey : notnull
    {
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map
            = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>();
        private readonly LinkedList<KeyValuePair<TKey, TValue>> _order
            = new LinkedList<KeyValuePair<TKey, TValue>>();

        public int Count => _map.Count;

        /// <summary>Adds a new entry at the end. Throws if the key already exists.</summary>
        public void Add(TKey key, TValue value)
        {
            ThrowIfKeyNull(key);
            if (_map.ContainsKey(key))
                throw new ArgumentException($"An entry with key '{key}' already exists.", nameof(key));
            _map[key] = _order.AddLast(new KeyValuePair<TKey, TValue>(key, value));
        }

        /// <summary>Gets or sets a value. The setter updates in place (keeping position) or appends a new entry.</summary>
        public TValue this[TKey key]
        {
            get
            {
                ThrowIfKeyNull(key);
                if (_map.TryGetValue(key, out var node))
                    return node.Value.Value;
                throw new KeyNotFoundException($"Key '{key}' was not found.");
            }
            set
            {
                ThrowIfKeyNull(key);
                if (_map.TryGetValue(key, out var node))
                    node.Value = new KeyValuePair<TKey, TValue>(key, value); // keep order position
                else
                    _map[key] = _order.AddLast(new KeyValuePair<TKey, TValue>(key, value));
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
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

        public bool ContainsKey(TKey key)
        {
            ThrowIfKeyNull(key);
            return _map.ContainsKey(key);
        }

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

        /// <summary>Keys in insertion order, as an independent snapshot.</summary>
        public IReadOnlyList<TKey> Keys
        {
            get
            {
                var keys = new List<TKey>(_map.Count);
                foreach (var pair in _order)
                    keys.Add(pair.Key);
                return keys;
            }
        }

        /// <summary>Values in insertion order, as an independent snapshot.</summary>
        public IReadOnlyList<TValue> Values
        {
            get
            {
                var values = new List<TValue>(_map.Count);
                foreach (var pair in _order)
                    values.Add(pair.Value);
                return values;
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _order.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // Guard.NotNull has a class constraint and cannot validate a possibly-value-type TKey.
        private static void ThrowIfKeyNull(TKey key)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));
        }
    }
}
