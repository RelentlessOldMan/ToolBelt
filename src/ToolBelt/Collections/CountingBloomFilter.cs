// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A counting Bloom filter: like a plain Bloom filter (ToolBelt's BloomFilter) but each slot is a small saturating counter
    /// instead of a single bit, which lets items be <see cref="Remove(byte[])"/>d. <see cref="MightContain(byte[])"/>
    /// still never returns a false negative for an item that is currently present, and may return a false
    /// positive at a rate bounded by the size chosen at construction. Sizing and double-hashing match
    /// BloomFilter; the counter store and hashing are inlined so this file stands alone. Not thread-safe.
    /// </summary>
    /// <remarks>
    /// Two inherent caveats of the counting variant (both documented rather than silently wrong):
    /// <list type="bullet">
    /// <item>Counters saturate at 255. A slot that reaches the maximum can no longer be decremented safely,
    /// so after extreme over-population a <see cref="Remove(byte[])"/> may leave a slot high. This is the
    /// standard counting-Bloom trade-off; size the filter for the real load.</item>
    /// <item>Removing an item that was never added (or whose slots collide with other items) can introduce a
    /// false negative for a different item. Only remove items you actually added.</item>
    /// </list>
    /// </remarks>
    public sealed class CountingBloomFilter
    {
        private const byte MaxCount = byte.MaxValue;

        private readonly byte[] _counts;
        private readonly int _slotCount;
        private readonly int _hashCount;

        /// <summary>Builds a filter sized for <paramref name="expectedItems"/> at target false-positive rate <paramref name="falsePositiveRate"/>.</summary>
        public CountingBloomFilter(int expectedItems, double falsePositiveRate = 0.01)
        {
            if (expectedItems <= 0)
                throw new ArgumentOutOfRangeException(nameof(expectedItems), expectedItems, "Expected items must be positive.");
            if (!(falsePositiveRate > 0 && falsePositiveRate < 1))   // NaN too: it built a 1-bit filter
                throw new ArgumentOutOfRangeException(nameof(falsePositiveRate), falsePositiveRate, "Rate must be in (0, 1).");

            double ln2 = Math.Log(2);
            // Size in double first; a huge capacity at a tiny rate can exceed int.MaxValue, and an unchecked
            // (int) cast would wrap. Reject rather than build a broken filter (matches BloomFilter).
            double optimalSlots = Math.Ceiling(-expectedItems * Math.Log(falsePositiveRate) / (ln2 * ln2));
            // Past Array.MaxLength (not int.MaxValue) the counter array threw OutOfMemoryException.
            if (optimalSlots > 0x7FFFFFC7)
                throw new ArgumentOutOfRangeException(nameof(expectedItems),
                    "The requested capacity and false-positive rate would require more than Array.MaxLength (0x7FFFFFC7) slots.");
            long slots = SizeFor(expectedItems, falsePositiveRate, Math.Max(1, (long)optimalSlots), 0x7FFFFFC7);
            if (slots < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedItems),
                    "The requested capacity and false-positive rate would require more than Array.MaxLength (0x7FFFFFC7) slots.");
            _slotCount = (int)slots;
            _hashCount = HashesFor(slots, expectedItems);
            _counts = new byte[_slotCount];
        }

        // The formulas are the large-n limit: for a few items the rate they give is past the target (3 items at 0.01 got
        // 29 slots and 7 hashes, a rate of 0.0128; 1 at 0.3 got 0.34). The size grows to the smallest that meets the target
        // by RateAt, with k the rounded optimum for that size. A size past max returns −1.
        private static long SizeFor(int n, double p, long m, long max)
        {
            if (RateAt(m, n) <= p) return m;
            long hi = m;
            do
            {
                if (hi >= max) return -1;
                hi = Math.Min(max, hi * 2);
            }
            while (RateAt(hi, n) > p);
            long lo = m;   // RateAt(lo) > p >= RateAt(hi)
            while (hi - lo > 1)
            {
                long mid = lo + (hi - lo) / 2;
                if (RateAt(mid, n) <= p) hi = mid; else lo = mid;
            }
            return hi;
        }

        private static int HashesFor(long m, int n) => (int)Math.Max(1, Math.Round((double)m / n * Math.Log(2)));

        // The false-positive rate of m slots and k probes after n items, or a bound just above it; never below. The classic
        // (1 − (1 − 1/m)^(kn))^k is below it: it treats the slots as independent and a query's k probes as distinct, and
        // filters sized by it missed the target (1 item at 0.01 had 0.0121, 3 had 0.0108, 10 had 0.0104). For a few
        // throws the rate is exact: E[(X/m)^k] over the distribution of X, the slots set by kn throws. Past that it is
        // E[f^D], with f the chance a given slot is set and D the distinct slots among the k probes: d given slots are
        // all set with chance at most f^d, the occupancy of distinct slots being negatively associated. Both sums are of
        // positive terms. k is at most about 1,100 (log2 of the smallest rate), so E[f^D] is cheap.
        private static double RateAt(long m, int n)
        {
            int k = HashesFor(m, n);
            long throws = (long)k * n;
            if (throws <= 4000 && throws * Math.Min(throws, m) <= 400_000)
            {
                // P(X = j) after each throw: a throw sets a new slot with chance (m − j)/m.
                int top = (int)Math.Min(throws, m);
                var set = new double[top + 1];
                set[0] = 1;
                for (int s = 0; s < throws; s++)
                    for (int j = (int)Math.Min(s, top - 1); j >= 0; j--)
                    {
                        set[j + 1] += set[j] * ((m - j) / (double)m);
                        set[j] *= j / (double)m;
                    }
                double rate = 0;
                for (int j = 1; j <= top; j++) rate += set[j] * Math.Pow(j / (double)m, k);
                return rate;
            }
            double empty = Math.Exp((double)k * n * Log1p(-1.0 / m));   // (1 − 1/m)^(kn), the chance a slot stays clear
            double full = 1 - empty;
            // P(D = d) after each probe: a probe lands on a new slot with chance (m − d)/m.
            int most = (int)Math.Min(k, m);
            var distinct = new double[most + 1];
            distinct[0] = 1;
            for (int i = 0; i < k; i++)
                for (int d = Math.Min(i, most - 1); d >= 0; d--)
                {
                    distinct[d + 1] += distinct[d] * ((m - d) / (double)m);
                    distinct[d] *= d / (double)m;
                }
            double bound = 0, power = 1;
            for (int d = 1; d <= most; d++) { power *= full; bound += distinct[d] * power; }
            return bound;
        }

        // ln(1 + x) for small |x|, accurate where 1 + x rounds (netstandard2.0 has no Math.Log1p).
        private static double Log1p(double x) => Math.Abs(x) < 1e-4 ? x - x * x / 2 + x * x * x / 3 : Math.Log(1 + x);

        /// <summary>The number of counter slots.</summary>
        public int SlotCount => _slotCount;

        /// <summary>The number of hash probes per item.</summary>
        public int HashCount => _hashCount;

        /// <summary>Adds an item, incrementing its counter slots (saturating at 255).</summary>
        public void Add(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            uint h1 = Fnv(data, 2166136261u);
            uint h2 = Fnv(data, 2166136261u ^ 0x9E3779B9u);
            for (int i = 0; i < _hashCount; i++)
            {
                int slot = Probe(h1, h2, i, _slotCount);
                if (_counts[slot] < MaxCount) _counts[slot]++;
            }
        }

        /// <summary>Adds the UTF-8 bytes of <paramref name="text"/> (lone surrogates kept distinct).</summary>
        public void Add(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            Add(Bytes(text));
        }

        /// <summary>
        /// Removes an item by decrementing its counter slots. Returns <c>true</c> if the item was present
        /// (every slot non-zero) before the removal; a <c>false</c> result leaves the filter untouched.
        /// Saturated (255) slots are left in place to avoid corrupting other items — see the type remarks.
        /// </summary>
        public bool Remove(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (!MightContain(data)) return false; // never decrement below a membership we don't have
            uint h1 = Fnv(data, 2166136261u);
            uint h2 = Fnv(data, 2166136261u ^ 0x9E3779B9u);
            for (int i = 0; i < _hashCount; i++)
            {
                int slot = Probe(h1, h2, i, _slotCount);
                if (_counts[slot] > 0 && _counts[slot] < MaxCount) _counts[slot]--;
            }
            return true;
        }

        /// <summary>Removes the UTF-8 bytes of <paramref name="text"/> (lone surrogates kept distinct).</summary>
        public bool Remove(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Remove(Bytes(text));
        }

        /// <summary>Reports whether the item may be present. Never a false negative for a currently-added item.</summary>
        public bool MightContain(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            uint h1 = Fnv(data, 2166136261u);
            uint h2 = Fnv(data, 2166136261u ^ 0x9E3779B9u);
            for (int i = 0; i < _hashCount; i++)
            {
                int slot = Probe(h1, h2, i, _slotCount);
                if (_counts[slot] == 0)
                    return false;
            }
            return true;
        }

        /// <summary>Reports whether the UTF-8 bytes of <paramref name="text"/> (lone surrogates kept distinct) may be present.</summary>
        public bool MightContain(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return MightContain(Bytes(text));
        }

        // UTF-8, except that a lone surrogate is encoded as its own three bytes (WTF-8) rather than U+FFFD, which made
        // every string differing only in lone surrogates the same item. Well-formed strings keep their UTF-8 bytes.
        private static byte[] Bytes(string text)
        {
            bool lone = false;
            for (int i = 0; i < text.Length && !lone; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c)) { if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++; else lone = true; }
                else if (char.IsLowSurrogate(c)) lone = true;
            }
            if (!lone) return Encoding.UTF8.GetBytes(text);
            var bytes = new List<byte>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    bytes.AddRange(Encoding.UTF8.GetBytes(text.Substring(i, 2)));
                    i++;
                }
                else if (char.IsSurrogate(c))
                {
                    bytes.Add((byte)(0xE0 | (c >> 12)));
                    bytes.Add((byte)(0x80 | ((c >> 6) & 0x3F)));
                    bytes.Add((byte)(0x80 | (c & 0x3F)));
                }
                else bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
            }
            return bytes.ToArray();
        }

        // Probe i of an item. Plain double hashing (h1 + i·h2) mod m cycles after m / gcd(h2, m) probes, so in a small or
        // even-sized filter an item touched only a few distinct slots (25% false positives at a 1% target for one item);
        // the murmur3 finalizer on each probe makes them behave like independent hashes.
        //
        // In 64 bits, and uniform over the slots: a 32-bit probe taken mod m set some slots more often than others
        // (with 1.72e9 slots, 3 hash values in 2 for the lower half against 2 for the rest), so a large filter had more false
        // positives than its size promised. A hash past the last whole multiple of m is redrawn (a chance below 2^-32).
        private static int Probe(uint h1, uint h2, int i, int m)
        {
            ulong x = unchecked((((ulong)h1 << 32) | h2) + (ulong)(i + 1) * 0x9E3779B97F4A7C15UL);
            ulong surplus = (ulong.MaxValue % (ulong)m + 1) % (ulong)m;   // 2^64 mod m
            while (true)
            {
                ulong hash = x;   // splitmix64's finalizer
                hash = unchecked((hash ^ (hash >> 30)) * 0xBF58476D1CE4E5B9UL);
                hash = unchecked((hash ^ (hash >> 27)) * 0x94D049BB133111EBUL);
                hash ^= hash >> 31;
                if (surplus == 0 || hash < unchecked(0UL - surplus)) return (int)(hash % (ulong)m);
                x = unchecked(x + 0x9E3779B97F4A7C15UL);
            }
        }

        // FNV-1a with a configurable offset basis and a murmur3 finalizer, giving two independent hashes for double hashing.
        private static uint Fnv(byte[] data, uint offset)
        {
            uint hash = offset;
            foreach (byte b in data)
            {
                hash ^= b;
                hash = unchecked(hash * 16777619u);
            }
            // FNV-1a's low bits depend only on the input's low bits, so the two hashes' low bits were tied (always
            // opposite parity) and an even bit count probed half the bits: 2.5x to 20x the false-positive rate.
            // The murmur3 finalizer makes every output bit depend on every input bit.
            hash ^= hash >> 16;
            hash = unchecked(hash * 0x85EBCA6Bu);
            hash ^= hash >> 13;
            hash = unchecked(hash * 0xC2B2AE35u);
            hash ^= hash >> 16;
            return hash;
        }
    }
}
