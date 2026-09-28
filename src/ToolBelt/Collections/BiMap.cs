// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A bidirectional one-to-one map: every key maps to a unique value and vice versa, so you can look
    /// up in either direction in O(1). Adding a key or value that already exists throws, preserving the
    /// bijection. Not thread-safe.
    /// </summary>
    public sealed class BiMap<TKey, TValue>
        where TKey : notnull
        where TValue : notnull
    {
        private readonly Dictionary<TKey, TValue> _forward = new Dictionary<TKey, TValue>();
        private readonly Dictionary<TValue, TKey> _backward = new Dictionary<TValue, TKey>();

        public int Count => _forward.Count;

        public IReadOnlyCollection<TKey> Keys => _forward.Keys;

        public IReadOnlyCollection<TValue> Values => _backward.Keys;

        /// <summary>Adds a key/value pair. Throws if either side is already present.</summary>
        public void Add(TKey key, TValue value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (_forward.ContainsKey(key))
                throw new ArgumentException($"Key '{key}' is already present.", nameof(key));
            if (_backward.ContainsKey(value))
                throw new ArgumentException($"Value '{value}' is already present.", nameof(value));

            _forward[key] = value;
            _backward[value] = key;
        }

        public TValue GetByKey(TKey key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return _forward[key];
        }

        public TKey GetByValue(TValue value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            return _backward[value];
        }

        public bool TryGetByKey(TKey key, out TValue value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return _forward.TryGetValue(key, out value!);
        }

        public bool TryGetByValue(TValue value, out TKey key)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            return _backward.TryGetValue(value, out key!);
        }

        public bool ContainsKey(TKey key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return _forward.ContainsKey(key);
        }

        public bool ContainsValue(TValue value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            return _backward.ContainsKey(value);
        }

        public bool RemoveByKey(TKey key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (_forward.TryGetValue(key, out var value))
            {
                _forward.Remove(key);
                _backward.Remove(value);
                return true;
            }
            return false;
        }

        public bool RemoveByValue(TValue value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (_backward.TryGetValue(value, out var key))
            {
                _backward.Remove(value);
                _forward.Remove(key);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            _forward.Clear();
            _backward.Clear();
        }
    }
}
