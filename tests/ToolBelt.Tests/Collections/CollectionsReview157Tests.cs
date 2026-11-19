using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    /// <summary>Regressions from review round 157 (Collections, ninth pass): each test failed before its fix.</summary>
    public sealed class CollectionsReview157Tests
    {
        // A finite ttl whose expiry fell exactly on DateTimeOffset.MaxValue was taken as saturated and never expired.
        public void Ttl_AnExpiryAtTheEndOfTheCalendarIsReached()
        {
            long max = DateTimeOffset.MaxValue.UtcTicks;
            var now = new DateTimeOffset(max - 10, TimeSpan.Zero);
            var cache = new TtlCache<string, int>(TimeSpan.FromTicks(10), () => now);
            cache.Set("a", 1);
            Check.True(cache.TryGet("a", out _));
            now = DateTimeOffset.MaxValue;
            Check.False(cache.TryGet("a", out _));
            Check.Equal(0, cache.Count);
        }

        // Against a model: the entry lives while now < set + ttl, and forever when set + ttl is past the calendar's end.
        public void Ttl_ExpiryNearTheEndOfTheCalendarMatchesAModel()
        {
            long max = DateTimeOffset.MaxValue.UtcTicks;
            var rng = new Random(157);
            for (int trial = 0; trial < 2000; trial++)
            {
                long ttl = 1 + rng.Next(40);
                long set = max - rng.Next(60);
                long read = Math.Min(max, set + rng.Next(60));
                var now = new DateTimeOffset(set, TimeSpan.Zero);
                var cache = new TtlCache<int, int>(TimeSpan.FromTicks(ttl), () => now);
                cache.Set(1, 1);
                now = new DateTimeOffset(read, TimeSpan.Zero);
                bool expected = set > max - ttl || read < set + ttl;
                Check.Equal(expected, cache.TryGet(1, out _));
            }
            var never = DateTimeOffset.MinValue;
            var forever = new TtlCache<int, int>(TimeSpan.MaxValue, () => never);
            forever.Set(1, 1);
            never = DateTimeOffset.MaxValue;
            Check.True(forever.TryGet(1, out _));
        }
        // ---------------- Bloom sizing: the rate it promises ----------------

        // E[(X/m)^k] exactly, X the bits set by t independent throws: P(X = j) = C(m, j)·j!·S(t, j) / m^t.
        private static double ExactRate(long m, int k, int t)
        {
            var stirling = new BigInteger[t + 1];   // S(s, j) for the current s
            stirling[0] = 1;
            for (int s = 1; s <= t; s++)
                for (int j = Math.Min(s, t); j >= 0; j--)
                    stirling[j] = j == 0 ? BigInteger.Zero : j * stirling[j] + stirling[j - 1];
            BigInteger num = 0, fallingM = 1;   // m(m − 1)…(m − j + 1)
            for (int j = 1; j <= Math.Min(t, m); j++)
            {
                fallingM *= m - j + 1;
                num += fallingM * stirling[j] * BigInteger.Pow(j, k);
            }
            BigInteger den = BigInteger.Pow(m, t + k);
            return Math.Exp(BigInteger.Log(num) - BigInteger.Log(den));
        }

        // The same by occupancy over the throws, for more throws than ExactRate takes.
        private static double OccupancyRate(long m, int k, long t)
        {
            var p = new double[m + 1];
            p[0] = 1;
            for (long s = 0; s < t; s++)
                for (long j = Math.Min(s, m - 1); j >= 0; j--)
                {
                    p[j + 1] += p[j] * (m - j) / m;
                    p[j] *= (double)j / m;
                }
            double rate = 0;
            for (long j = 1; j <= m; j++) rate += p[j] * Math.Pow((double)j / m, k);
            return rate;
        }

        // Sized by (1 − (1 − 1/m)^(kn))^k, which is below the true rate: 1 item at 0.01 had 0.0121, 3 had 0.0108.
        public void Bloom_FewItemsMeetTheTargetRate()
        {
            foreach (double p in new[] { 0.3, 0.1, 0.01, 0.001 })
                for (int n = 1; n <= 12; n++)
                {
                    var bloom = new BloomFilter(n, p);
                    var counting = new CountingBloomFilter(n, p);
                    Check.Equal(bloom.BitCount, counting.SlotCount);
                    Check.Equal(bloom.HashCount, counting.HashCount);
                    double rate = ExactRate(bloom.BitCount, bloom.HashCount, bloom.HashCount * n);
                    Check.True(rate <= p * (1 + 1e-9), $"n={n} p={p}: m={bloom.BitCount} k={bloom.HashCount} rate {rate}");
                }
        }

        public void Bloom_ManyItemsMeetTheTargetRate()
        {
            foreach (var (n, p) in new[] { (300, 0.01), (1000, 0.01), (600, 0.05), (2000, 0.1) })
            {
                var bloom = new BloomFilter(n, p);
                double rate = OccupancyRate(bloom.BitCount, bloom.HashCount, (long)bloom.HashCount * n);
                Check.True(rate <= p, $"n={n} p={p}: m={bloom.BitCount} k={bloom.HashCount} rate {rate}");
                Check.True(rate > p * 0.97, $"n={n} p={p}: oversized, rate {rate}");
            }
        }

        // ---------------- Bloom probes: uniform over the bits ----------------

        // A 32-bit hash mod m hits the 2^32 mod m lowest bits once more than the rest (43 against 42 here).
        private static void CheckUniform(Func<int, bool> isSet, int m, int added)
        {
            long low = (long)((1UL << 32) % (ulong)m);
            double f = (double)low / m;
            Check.True(f > 0.2 && f < 0.8, $"m={m}: f={f}");
            long setLow = 0, setAll = 0;
            for (int i = 0; i < m; i++)
                if (isSet(i)) { setAll++; if (i < low) setLow++; }
            double share = (double)setLow / setAll, sigma = Math.Sqrt(f * (1 - f) / setAll);
            Check.True(setAll > added * 0.9, $"set {setAll}");
            Check.True(Math.Abs(share - f) < 6 * sigma, $"m={m}: share {share}, expected {f} ± {sigma}");
        }

        public void Bloom_ProbesAreUniformOverALargeFilter()
        {
            const int added = 10_000_000;
            var bloom = new BloomFilter(70_000_000, 0.5);
            Check.Equal(1, bloom.HashCount);
            for (int i = 0; i < added; i++) bloom.Add(BitConverter.GetBytes(i));
            var bits = (long[])typeof(BloomFilter).GetField("_bits", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bloom)!;
            CheckUniform(i => (bits[i >> 6] & (1L << (i & 63))) != 0, bloom.BitCount, added);
        }

        public void CountingBloom_ProbesAreUniformOverALargeFilter()
        {
            const int added = 10_000_000;
            var filter = new CountingBloomFilter(70_000_000, 0.5);
            Check.Equal(1, filter.HashCount);
            for (int i = 0; i < added; i++) filter.Add(BitConverter.GetBytes(i));
            var counts = (byte[])typeof(CountingBloomFilter).GetField("_counts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(filter)!;
            CheckUniform(i => counts[i] != 0, filter.SlotCount, added);
        }

        // ---------------- Aggregation: signed zeros ----------------

        private static bool Negative(double v) => BitConverter.DoubleToInt64Bits(v) < 0;

        // Math.Min and Math.Max order signed zeros on .NET Core only: {−0, +0} had a mean of −0 there, +0 on .NET Framework,
        // and a max of −0 on both.
        public void Summarize_SignedZerosAreOrdered()
        {
            var cases = new (double[] Values, bool MinNeg, bool MaxNeg, bool MeanNeg)[]
            {
                (new[] { -0.0, 0.0 }, true, false, false),
                (new[] { 0.0, -0.0 }, true, false, false),
                (new[] { -0.0 }, true, true, true),
                (new[] { -0.0, -0.0, -0.0 }, true, true, true),
                (new[] { 0.0 }, false, false, false),
                (new[] { -1.0, 1.0 }, true, false, false),
                (new[] { -0.0, 1.0, -1.0 }, true, false, false),
            };
            foreach (var (values, minNeg, maxNeg, meanNeg) in cases)
            {
                var s = Aggregation.Summarize(values, _ => 0, v => v)[0];
                string label = string.Join(",", values);
                Check.Equal(minNeg, Negative(s.Min), label + " min");
                Check.Equal(maxNeg, Negative(s.Max), label + " max");
                Check.Equal(meanNeg, Negative(s.Mean), label + " mean");
            }
        }
    }
}
