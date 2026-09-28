// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A multimap: each key maps to an ordered list of values, and a key may hold duplicates. Adding
    /// appends; removing a value deletes the first matching occurrence and drops the key once its last
    /// value is gone. Value lookups return an independent snapshot, so the caller cannot mutate internal
    /// state. Not thread-safe.
    /// </summary>
    public sealed class MultiDictionary<TKey, TValue>
        where TKey : notnull
    {
        private readonly Dictionary<TKey, List<TValue>> _map = new Dictionary<TKey, List<TValue>>();
        private int _count;

        /// <summary>Number of distinct keys.</summary>
        public int Count => _map.Count;

        /// <summary>Total number of values across all keys.</summary>
        public int Total => _count;

        public IReadOnlyCollection<TKey> Keys => _map.Keys;

        /// <summary>Appends <paramref name="value"/> under <paramref name="key"/>.</summary>
        public void Add(TKey key, TValue value)
        {
            ThrowIfKeyNull(key);
            if (!_map.TryGetValue(key, out var list))
            {
                list = new List<TValue>();
                _map[key] = list;
            }
            list.Add(value);
            _count++;
        }

        /// <summary>Removes the first occurrence of <paramref name="value"/> under <paramref name="key"/>.</summary>
        public bool Remove(TKey key, TValue value)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var list))
            {
                int index = list.IndexOf(value);
                if (index >= 0)
                {
                    list.RemoveAt(index);
                    _count--;
                    if (list.Count == 0)
                        _map.Remove(key);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Removes a key and all of its values. Returns whether the key was present.</summary>
        public bool RemoveAll(TKey key)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var list))
            {
                _count -= list.Count;
                _map.Remove(key);
                return true;
            }
            return false;
        }

        /// <summary>The values under a key as a snapshot, or an empty list if the key is absent.</summary>
        public IReadOnlyList<TValue> this[TKey key] => GetValues(key);

        /// <summary>The values under a key as an independent snapshot (empty if absent).</summary>
        public IReadOnlyList<TValue> GetValues(TKey key)
        {
            ThrowIfKeyNull(key);
            return _map.TryGetValue(key, out var list) ? list.ToArray() : Array.Empty<TValue>();
        }

        public bool ContainsKey(TKey key)
        {
            ThrowIfKeyNull(key);
            return _map.ContainsKey(key);
        }

        public bool Contains(TKey key, TValue value)
        {
            ThrowIfKeyNull(key);
            return _map.TryGetValue(key, out var list) && list.Contains(value);
        }

        public void Clear()
        {
            _map.Clear();
            _count = 0;
        }

        // Guard.NotNull carries a class constraint, so it cannot validate a possibly-value-type TKey.
        // This is the documented inline-null-throw fallback for generic keys.
        private static void ThrowIfKeyNull(TKey key)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));
        }
    }
}
