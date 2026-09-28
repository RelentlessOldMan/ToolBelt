// ToolBelt drop-in — fully self-contained (BCL only).
using System;
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
            if (falsePositiveRate <= 0 || falsePositiveRate >= 1)
                throw new ArgumentOutOfRangeException(nameof(falsePositiveRate), falsePositiveRate, "Rate must be in (0, 1).");

            double ln2 = Math.Log(2);
            // Compute the optimal size in double first: for a large item count and a tiny false-positive
            // rate this can exceed int.MaxValue, and an unchecked (int) cast would wrap to a negative or
            // absurdly small size. Reject rather than silently build a broken filter.
            double optimalBits = Math.Ceiling(-expectedItems * Math.Log(falsePositiveRate) / (ln2 * ln2));
            if (optimalBits > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(expectedItems),
                    "The requested capacity and false-positive rate would require more than int.MaxValue bits.");
            _bitCount = Math.Max(1, (int)optimalBits);
            _hashCount = Math.Max(1, (int)Math.Round((double)_bitCount / expectedItems * ln2));
            _bits = new long[((long)_bitCount + 63) / 64];
        }

        public int BitCount => _bitCount;

        public int HashCount => _hashCount;

        public void Add(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            uint h1 = Fnv(data, 2166136261u);
            uint h2 = Fnv(data, 2166136261u ^ 0x9E3779B9u);
            for (int i = 0; i < _hashCount; i++)
            {
                int bit = (int)(unchecked(h1 + (uint)i * h2) % (uint)_bitCount);
                _bits[bit >> 6] |= 1L << (bit & 63);
            }
        }

        public void Add(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            Add(Encoding.UTF8.GetBytes(text));
        }

        public bool MightContain(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            uint h1 = Fnv(data, 2166136261u);
            uint h2 = Fnv(data, 2166136261u ^ 0x9E3779B9u);
            for (int i = 0; i < _hashCount; i++)
            {
                int bit = (int)(unchecked(h1 + (uint)i * h2) % (uint)_bitCount);
                if ((_bits[bit >> 6] & (1L << (bit & 63))) == 0)
                    return false;
            }
            return true;
        }

        public bool MightContain(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return MightContain(Encoding.UTF8.GetBytes(text));
        }

        // FNV-1a with a configurable offset basis, giving two independent-enough hashes for double hashing.
        private static uint Fnv(byte[] data, uint offset)
        {
            uint hash = offset;
            foreach (byte b in data)
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
