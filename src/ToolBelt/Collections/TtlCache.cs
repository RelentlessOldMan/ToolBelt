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
        // .NET Framework hashes a double or float NaN by its bits and a decimal by its scale, so equal keys (NaNs with
        // other payloads, 1.0m and 1.00m) fell into separate buckets: one key counted twice. Keys of those types hash
        // by value; everything else uses the default comparer.
        private static class ValueKeys
        {
            public static IEqualityComparer<TK> For<TK>()
            {
#if NETSTANDARD2_0
                object? c = typeof(TK) == typeof(double) ? DoubleKeys.Instance
                    : typeof(TK) == typeof(float) ? SingleKeys.Instance
                    : typeof(TK) == typeof(decimal) ? DecimalKeys.Instance
                    : typeof(TK) == typeof(double?) ? new NullableKeys<double>(DoubleKeys.Instance)
                    : typeof(TK) == typeof(float?) ? new NullableKeys<float>(SingleKeys.Instance)
                    : typeof(TK) == typeof(decimal?) ? new NullableKeys<decimal>(DecimalKeys.Instance)
                    : (object?)null;
                if (c is not null) return (IEqualityComparer<TK>)c;
#endif
                return EqualityComparer<TK>.Default;
            }
#if NETSTANDARD2_0

            private sealed class DoubleKeys : IEqualityComparer<double>
            {
                public static readonly DoubleKeys Instance = new DoubleKeys();
                public bool Equals(double x, double y) => x.Equals(y);
                public int GetHashCode(double v) => double.IsNaN(v) ? -1 : v.GetHashCode();
            }

            private sealed class SingleKeys : IEqualityComparer<float>
            {
                public static readonly SingleKeys Instance = new SingleKeys();
                public bool Equals(float x, float y) => x.Equals(y);
                public int GetHashCode(float v) => float.IsNaN(v) ? -1 : v.GetHashCode();
            }

            private sealed class DecimalKeys : IEqualityComparer<decimal>
            {
                public static readonly DecimalKeys Instance = new DecimalKeys();
                public bool Equals(decimal x, decimal y) => x == y;
                // Not through double: the conversion can round 1.0m and 1.00m-style pairs apart (the Framework's own bug).
                // Dividing by one drops trailing zeros, so equal nonzero decimals have one representation.
                public int GetHashCode(decimal v)
                {
                    if (v == 0) return 0;   // 0m and -0m
                    int[] b = decimal.GetBits(v / 1.000000000000000000000000000000000m);
                    return unchecked(b[0] ^ (b[1] * 31) ^ (b[2] * 961) ^ b[3]);
                }
            }

            private sealed class NullableKeys<TV> : IEqualityComparer<TV?> where TV : struct
            {
                private readonly IEqualityComparer<TV> _inner;
                public NullableKeys(IEqualityComparer<TV> inner) => _inner = inner;
                public bool Equals(TV? x, TV? y) => x.HasValue ? y.HasValue && _inner.Equals(x.Value, y.Value) : !y.HasValue;
                public int GetHashCode(TV? v) => v.HasValue ? _inner.GetHashCode(v.Value) : 0;
            }
#endif
        }

        private readonly struct Entry
        {
            public Entry(TValue value, long expiresTicks)
            {
                Value = value;
                ExpiresTicks = expiresTicks;
            }

            public TValue Value { get; }
            public long ExpiresTicks { get; } // UTC ticks; long.MaxValue (past every clock reading) never expires

            // An expiry saturated at the end of the calendar (a ttl of TimeSpan.MaxValue) is never reached: the entry
            // expired when the clock read DateTimeOffset.MaxValue, though the doc says it never does. Saturation is its own
            // value, not DateTimeOffset.MaxValue: a finite ttl that ends exactly there was taken as saturated and never expired.
            public bool LiveAt(DateTimeOffset now) => now.UtcTicks < ExpiresTicks;
        }

        private readonly Dictionary<TKey, Entry> _entries = new Dictionary<TKey, Entry>(ValueKeys.For<TKey>());
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
            }
            // Saturate rather than throw when now + ttl is past the end of the calendar. In ticks, since
            // MaxValue - ttl itself underflows for a huge ttl (the long difference cannot overflow). In UTC: expiry is
            // compared in UTC, and the local time (which reaches the end first under a positive offset) made an entry
            // that fits never expire.
            long expiresTicks = now.UtcTicks > DateTimeOffset.MaxValue.UtcTicks - _ttl.Ticks
                ? long.MaxValue : now.UtcTicks + _ttl.Ticks;
            _entries[key] = new Entry(value, expiresTicks);
        }

        /// <summary>Gets a value if present and not expired; removes it and returns false if it has expired.</summary>
        public bool TryGet(TKey key, out TValue value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (_entries.TryGetValue(key, out var entry))
            {
                if (entry.LiveAt(_clock()))
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
            // An expired entry is absent, as TryGet says; it returned true for one not yet pruned.
            if (!_entries.TryGetValue(key, out var entry)) return false;
            bool live = entry.LiveAt(_clock());   // before removing: a clock that threw removed the entry and then failed
            _entries.Remove(key);
            if (_entries.Count <= _pruneAt / 4) ResetPruneAt();
            return live;
        }

        public void Clear()
        {
            _entries.Clear();
            ResetPruneAt();
        }

        // The threshold only ever grew, so after a burst and a Clear write-once keys piled up to the old peak.
        private void ResetPruneAt()
            => _pruneAt = Math.Max(MinPruneAt, _entries.Count <= int.MaxValue / 2 ? _entries.Count * 2 : int.MaxValue);

        /// <summary>Removes all currently-expired entries.</summary>
        public void Prune() => PruneExpired(_clock());

        private void PruneExpired(DateTimeOffset now)
        {
            List<TKey>? expired = null;
            foreach (var pair in _entries)
            {
                if (!pair.Value.LiveAt(now))
                    (expired ??= new List<TKey>()).Add(pair.Key);
            }
            if (expired is not null)
                foreach (var key in expired)
                    _entries.Remove(key);
            ResetPruneAt();
        }
    }
}
