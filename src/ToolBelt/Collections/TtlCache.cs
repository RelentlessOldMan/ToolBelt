// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A cache whose entries expire a fixed <c>ttl</c> after they are written. Expired entries are
    /// treated as absent and removed lazily on access (or eagerly via <see cref="Prune"/>); <see cref="Set"/>
    /// also prunes whenever the entry count doubles, so keys that are written once and never read again
    /// cannot accumulate. A <c>ttl</c> of <see cref="TimeSpan.MaxValue"/> never expires. Time is read
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
        private int _pruneAt = MinPruneAt; // Set prunes when the count reaches this, then doubles it

        private const int MinPruneAt = 64;

        public TtlCache(TimeSpan ttl, Func<DateTimeOffset>? clock = null)
        {
            if (ttl <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "TTL must be positive.");
            _ttl = ttl;
            _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        }

        /// <summary>Number of entries that have not yet expired (expired entries are pruned first, so this is O(n)).</summary>
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
            var now = _clock();
            if (_entries.Count >= _pruneAt)
            {
                PruneExpired(now);
                _pruneAt = Math.Max(MinPruneAt, _entries.Count <= int.MaxValue / 2 ? _entries.Count * 2 : int.MaxValue);
            }
            // Saturate rather than throw when now + ttl is past the end of the calendar. In ticks, since
            // MaxValue - ttl itself underflows for a huge ttl (the long difference cannot overflow).
            var expiresAt = now.UtcTicks > DateTimeOffset.MaxValue.UtcTicks - _ttl.Ticks ? DateTimeOffset.MaxValue : now + _ttl;
            _entries[key] = new Entry(value, expiresAt);
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
        public void Prune() => PruneExpired(_clock());

        private void PruneExpired(DateTimeOffset now)
        {
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
