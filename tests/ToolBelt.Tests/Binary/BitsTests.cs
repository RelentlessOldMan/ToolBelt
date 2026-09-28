using System;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class BitsTests
    {
        public void PopCountKnown()
        {
            Check.Equal(0, Bits.PopCount(0u));
            Check.Equal(32, Bits.PopCount(uint.MaxValue));
            Check.Equal(1, Bits.PopCount(0x8000_0000u));
            Check.Equal(64, Bits.PopCount(ulong.MaxValue));
            Check.Equal(8, Bits.PopCount(0xFFul));
        }

        public void ZeroCountsKnown()
        {
            Check.Equal(32, Bits.LeadingZeroCount(0u));
            Check.Equal(0, Bits.LeadingZeroCount(0x8000_0000u));
            Check.Equal(31, Bits.LeadingZeroCount(1u));
            Check.Equal(32, Bits.TrailingZeroCount(0u));
            Check.Equal(0, Bits.TrailingZeroCount(1u));
            Check.Equal(31, Bits.TrailingZeroCount(0x8000_0000u));
            Check.Equal(64, Bits.LeadingZeroCount(0ul));
            Check.Equal(64, Bits.TrailingZeroCount(0ul));
        }

        public void RotateRoundTrip_AndKnown()
        {
            Check.Equal(0x00000001u, Bits.RotateLeft(0x80000000u, 1));
            Check.Equal(0x80000000u, Bits.RotateRight(0x00000001u, 1));
            Check.Equal(0x12345678u, Bits.RotateLeft(0x12345678u, 0));  // no-op
            Check.Equal(0x12345678u, Bits.RotateLeft(0x12345678u, 32)); // full turn == no-op

            var rng = new Random(5);
            for (int i = 0; i < 5000; i++)
            {
                uint v = (uint)rng.Next() ^ ((uint)rng.Next() << 1);
                int n = rng.Next(0, 40);
                Check.Equal(v, Bits.RotateRight(Bits.RotateLeft(v, n), n), $"32 v={v} n={n}");

                ulong w = ((ulong)(uint)rng.Next() << 32) | (uint)rng.Next();
                Check.Equal(w, Bits.RotateRight(Bits.RotateLeft(w, n), n), $"64 v={w} n={n}");
            }
        }

        // Differential: SWAR popcount must match a naive bit-by-bit count.
        public void PopCount_Differential()
        {
            var rng = new Random(6);
            for (int i = 0; i < 5000; i++)
            {
                uint v = (uint)rng.Next() ^ ((uint)rng.Next() << 1);
                Check.Equal(NaivePop(v), Bits.PopCount(v), $"v={v}");

                ulong w = ((ulong)(uint)rng.Next() << 32) | (uint)rng.Next();
                Check.Equal(NaivePop(w), Bits.PopCount(w), $"w={w}");
            }
        }

        private static int NaivePop(uint x) { int c = 0; while (x != 0) { c += (int)(x & 1); x >>= 1; } return c; }
        private static int NaivePop(ulong x) { int c = 0; while (x != 0) { c += (int)(x & 1); x >>= 1; } return c; }
    }
}
