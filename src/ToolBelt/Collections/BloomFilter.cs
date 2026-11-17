// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A Bloom filter: a compact, probabilistic set. <see cref="MightContain(byte[])"/> never returns a false
    /// negative (an added item always reports present), but may return a false positive at a rate bounded
    /// by the size chosen at construction. Sizing follows the standard optimal formulas. The bit store
    /// and hashing are inlined so this file stands alone. Not thread-safe.
    /// </summary>
    public sealed class BloomFilter
    {
        private readonly long[] _bits;
        private readonly int _bitCount;
        private readonly int _hashCount;

        /// <summary>Builds a filter sized for <paramref name="expectedItems"/> at target false-positive rate <paramref name="falsePositiveRate"/>.</summary>
        public BloomFilter(int expectedItems, double falsePositiveRate = 0.01)
        {
            if (expectedItems <= 0)
                throw new ArgumentOutOfRangeException(nameof(expectedItems), expectedItems, "Expected items must be positive.");
            if (!(falsePositiveRate > 0 && falsePositiveRate < 1))   // NaN too: it built a 1-bit filter
                throw new ArgumentOutOfRangeException(nameof(falsePositiveRate), falsePositiveRate, "Rate must be in (0, 1).");

            double ln2 = Math.Log(2);
            // Compute the optimal size in double first: for a large item count and a tiny false-positive
            // rate this can exceed int.MaxValue, and an unchecked (int) cast would wrap to a negative or
            // absurdly small size. Reject rather than silently build a broken filter.
            double optimalBits = Math.Ceiling(-expectedItems * Math.Log(falsePositiveRate) / (ln2 * ln2));
            if (optimalBits > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(expectedItems),
                    "The requested capacity and false-positive rate would require more than int.MaxValue bits.");
            long bits = SizeFor(expectedItems, falsePositiveRate, Math.Max(1, (long)optimalBits), int.MaxValue);
            if (bits < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedItems),
                    "The requested capacity and false-positive rate would require more than int.MaxValue bits.");
            _bitCount = (int)bits;
            _hashCount = HashesFor(bits, expectedItems);
            _bits = new long[((long)_bitCount + 63) / 64];
        }

        // The formulas are the large-n limit: for a few items the rate they give is past the target (3 items at 0.01 got
        // 29 bits and 7 hashes, a rate of 0.0128; 1 at 0.3 got 0.34). The size grows to the smallest that meets the target
        // by the exact rate (1 − (1 − 1/m)^(kn))^k, with k the rounded optimum for that size. A size past max returns −1.
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

        private static double RateAt(long m, int n)
        {
            int k = HashesFor(m, n);
            double empty = Math.Exp((double)k * n * Log1p(-1.0 / m));   // (1 − 1/m)^(kn), the chance a bit stays clear
            return Math.Pow(1 - empty, k);
        }

        // ln(1 + x) for small |x|, accurate where 1 + x rounds (netstandard2.0 has no Math.Log1p).
        private static double Log1p(double x) => Math.Abs(x) < 1e-4 ? x - x * x / 2 + x * x * x / 3 : Math.Log(1 + x);

        public int BitCount => _bitCount;

        public int HashCount => _hashCount;

        public void Add(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            uint h1 = Fnv(data, 2166136261u);
            uint h2 = Fnv(data, 2166136261u ^ 0x9E3779B9u);
            for (int i = 0; i < _hashCount; i++)
            {
                int bit = (int)(Probe(h1, h2, i) % (uint)_bitCount);
                _bits[bit >> 6] |= 1L << (bit & 63);
            }
        }

        public void Add(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            Add(Bytes(text));
        }

        public bool MightContain(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            uint h1 = Fnv(data, 2166136261u);
            uint h2 = Fnv(data, 2166136261u ^ 0x9E3779B9u);
            for (int i = 0; i < _hashCount; i++)
            {
                int bit = (int)(Probe(h1, h2, i) % (uint)_bitCount);
                if ((_bits[bit >> 6] & (1L << (bit & 63))) == 0)
                    return false;
            }
            return true;
        }

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
        private static uint Probe(uint h1, uint h2, int i)
        {
            uint hash = unchecked(h1 + (uint)i * h2);
            hash ^= hash >> 16;
            hash = unchecked(hash * 0x85EBCA6Bu);
            hash ^= hash >> 13;
            hash = unchecked(hash * 0xC2B2AE35u);
            hash ^= hash >> 16;
            return hash;
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
