using System;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class TtlCacheTests
    {
        private sealed class FakeClock
        {
            public DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public Func<DateTimeOffset> Func => () => Now;
            public void Advance(TimeSpan by) => Now += by;
        }

        public void HitWithinTtl_MissAfter()
        {
            var clock = new FakeClock();
            var cache = new TtlCache<string, int>(TimeSpan.FromSeconds(10), clock.Func);
            cache.Set("a", 42);

            clock.Advance(TimeSpan.FromSeconds(9));
            Check.True(cache.TryGet("a", out int v) && v == 42);

            clock.Advance(TimeSpan.FromSeconds(2)); // now 11s > 10s ttl
            Check.False(cache.TryGet("a", out _));
        }

        public void ExpiryBoundary_IsExclusive()
        {
            var clock = new FakeClock();
            var cache = new TtlCache<string, int>(TimeSpan.FromSeconds(10), clock.Func);
            cache.Set("a", 1);
            clock.Advance(TimeSpan.FromSeconds(10)); // exactly at expiry -> expired
            Check.False(cache.TryGet("a", out _));
        }

        public void SetResetsTtl()
        {
            var clock = new FakeClock();
            var cache = new TtlCache<string, int>(TimeSpan.FromSeconds(10), clock.Func);
            cache.Set("a", 1);
            clock.Advance(TimeSpan.FromSeconds(8));
            cache.Set("a", 2); // refreshes expiry to now+10
            clock.Advance(TimeSpan.FromSeconds(8)); // 16s from first set, 8s from refresh
            Check.True(cache.TryGet("a", out int v) && v == 2);
        }

        public void Prune_And_Count()
        {
            var clock = new FakeClock();
            var cache = new TtlCache<int, int>(TimeSpan.FromSeconds(5), clock.Func);
            cache.Set(1, 1);
            clock.Advance(TimeSpan.FromSeconds(3));
            cache.Set(2, 2);
            clock.Advance(TimeSpan.FromSeconds(3)); // key 1 (age 6) expired, key 2 (age 3) alive
            Check.Equal(1, cache.Count); // Count prunes expired first
            Check.True(cache.Contains(2));
            Check.False(cache.Contains(1));
        }

        public void RemoveAndClear()
        {
            var cache = new TtlCache<string, int>(TimeSpan.FromSeconds(10));
            cache.Set("a", 1);
            Check.True(cache.Remove("a"));
            Check.False(cache.Remove("a"));
            cache.Set("b", 2);
            cache.Clear();
            Check.False(cache.Contains("b"));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new TtlCache<string, int>(TimeSpan.Zero));
            var cache = new TtlCache<string, int>(TimeSpan.FromSeconds(1));
            Check.Throws<ArgumentNullException>(() => cache.Set(null!, 1));
        }
    }
}
