// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A cache whose entries expire a fixed <c>ttl</c> after they are written. Expired entries are
    /// treated as absent and removed lazily on access (or eagerly via <see cref="Prune"/>). Time is read
    /// through an injectable clock, so expiry is deterministic in tests. Not thread-safe.
    /// </summary>
    public sealed class TtlCache<TKey, TValue>
        where TKey : notnull
    {
        private readonly struct Entry
        {
            public Entry(TValue value, DateTimeOffset expiresAt)
            {
                Value = value;
                ExpiresAt = expiresAt;
            }

            public TValue Value { get; }
            public DateTimeOffset ExpiresAt { get; }
        }

        private readonly Dictionary<TKey, Entry> _entries = new Dictionary<TKey, Entry>();
        private readonly TimeSpan _ttl;
        private readonly Func<DateTimeOffset> _clock;

        public TtlCache(TimeSpan ttl, Func<DateTimeOffset>? clock = null)
        {
            if (ttl <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "TTL must be positive.");
            _ttl = ttl;
            _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        }

        /// <summary>Number of entries that have not yet expired (expired entries are pruned first).</summary>
        public int Count
        {
            get
            {
                Prune();
                return _entries.Count;
            }
        }

        /// <summary>Inserts or replaces a value, (re)starting its TTL from now.</summary>
        public void Set(TKey key, TValue value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            _entries[key] = new Entry(value, _clock() + _ttl);
        }

        /// <summary>Gets a value if present and not expired; removes it and returns false if it has expired.</summary>
        public bool TryGet(TKey key, out TValue value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (_entries.TryGetValue(key, out var entry))
            {
                if (_clock() < entry.ExpiresAt)
                {
                    value = entry.Value;
                    return true;
                }
                _entries.Remove(key); // lazily evict on expiry
            }
            value = default!;
            return false;
        }

        /// <summary>Whether a non-expired value exists for the key.</summary>
        public bool Contains(TKey key) => TryGet(key, out _);

        public bool Remove(TKey key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return _entries.Remove(key);
        }

        public void Clear() => _entries.Clear();

        /// <summary>Removes all currently-expired entries.</summary>
        public void Prune()
        {
            var now = _clock();
            List<TKey>? expired = null;
            foreach (var pair in _entries)
            {
                if (now >= pair.Value.ExpiresAt)
                    (expired ??= new List<TKey>()).Add(pair.Key);
            }
            if (expired is not null)
                foreach (var key in expired)
                    _entries.Remove(key);
        }
    }
}
