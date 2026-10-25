// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// A seeded pseudo-random generator with a fixed, documented algorithm (xoshiro256** seeded through
    /// SplitMix64), so a given seed produces the same sequence on every platform and runtime version. The
    /// framework's <see cref="System.Random"/> is not guaranteed stable across .NET versions, which makes it
    /// unsuitable when reproducibility matters (recorded simulations, fuzz corpora, golden tests). Derives
    /// from <see cref="Random"/> so it drops into any API expecting one; every overridable member is overridden
    /// (on .NET 6+ that includes NextInt64, NextSingle and the span NextBytes), so no call reaches the framework's
    /// own, version-dependent derivation. Members added to <see cref="Random"/> in future .NET versions are not
    /// covered until overridden here. Not thread-safe.
    /// </summary>
    public sealed class DeterministicRandom : Random
    {
        private ulong _s0, _s1, _s2, _s3;

        public DeterministicRandom(long seed)
        {
            ulong x = unchecked((ulong)seed);
            _s0 = SplitMix64(ref x);
            _s1 = SplitMix64(ref x);
            _s2 = SplitMix64(ref x);
            _s3 = SplitMix64(ref x);
        }

        /// <summary>The raw 64-bit generator output.</summary>
        public ulong NextUInt64()
        {
            unchecked
            {
                ulong result = Rotl(_s1 * 5UL, 7) * 9UL;
                ulong t = _s1 << 17;
                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = Rotl(_s3, 45);
                return result;
            }
        }

        public override double NextDouble()
            => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0); // 53-bit mantissa in [0, 1)

        protected override double Sample() => NextDouble();

        /// <remarks>
        /// Uses modulo reduction, so extremely large ranges carry a negligible (&lt; 2⁻⁶⁴·range) bias toward
        /// smaller values. This is irrelevant for typical ranges; use a rejection scheme if perfectly
        /// uniform integers over a near-2⁶⁴ range are required.
        /// </remarks>
        public override int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue)
                throw new ArgumentOutOfRangeException(nameof(minValue), "minValue must not exceed maxValue.");
            long range = (long)maxValue - minValue;
            if (range <= 0) return minValue;
            return minValue + (int)(NextUInt64() % (ulong)range);
        }

        public override int Next(int maxValue)
        {
            if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be non-negative.");
            return Next(0, maxValue);
        }

        public override int Next() => Next(0, int.MaxValue);

        public override void NextBytes(byte[] buffer)
        {
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));
            int i = 0;
            while (i < buffer.Length)
            {
                ulong block = NextUInt64();
                for (int b = 0; b < 8 && i < buffer.Length; b++, i++)
                    buffer[i] = (byte)(block >> (8 * b));
            }
        }

#if NET6_0_OR_GREATER
        public override void NextBytes(Span<byte> buffer)
        {
            int i = 0;
            while (i < buffer.Length)
            {
                ulong block = NextUInt64();
                for (int b = 0; b < 8 && i < buffer.Length; b++, i++)
                    buffer[i] = (byte)(block >> (8 * b));
            }
        }

        public override long NextInt64() => NextInt64(0, long.MaxValue);

        public override long NextInt64(long maxValue)
        {
            if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be non-negative.");
            return NextInt64(0, maxValue);
        }

        /// <remarks>Modulo reduction, with the same negligible bias as <see cref="Next(int, int)"/>.</remarks>
        public override long NextInt64(long minValue, long maxValue)
        {
            if (minValue > maxValue)
                throw new ArgumentOutOfRangeException(nameof(minValue), "minValue must not exceed maxValue.");
            ulong range = unchecked((ulong)maxValue - (ulong)minValue);
            if (range == 0) return minValue;
            return unchecked(minValue + (long)(NextUInt64() % range));
        }

        public override float NextSingle() => (NextUInt64() >> 40) * (1.0f / 16777216.0f); // 24 bits in [0, 1)
#endif

        private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        private static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
