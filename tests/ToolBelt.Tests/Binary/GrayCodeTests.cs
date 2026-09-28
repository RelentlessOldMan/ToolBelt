using System;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class GrayCodeTests
    {
        public void KnownValues()
        {
            uint[] expected = { 0, 1, 3, 2, 6, 7, 5, 4 }; // Gray codes of 0..7
            for (uint i = 0; i < expected.Length; i++)
                Check.Equal(expected[i], GrayCode.ToGray(i));
        }

        public void RoundTrip_32()
        {
            var rng = new Random(1);
            for (int i = 0; i < 5000; i++)
            {
                uint v = (uint)rng.Next() ^ ((uint)rng.Next() << 1);
                Check.Equal(v, GrayCode.FromGray(GrayCode.ToGray(v)), $"value {v}");
            }
            Check.Equal(uint.MaxValue, GrayCode.FromGray(GrayCode.ToGray(uint.MaxValue)));
        }

        public void RoundTrip_64()
        {
            var rng = new Random(2);
            for (int i = 0; i < 5000; i++)
            {
                ulong v = ((ulong)(uint)rng.Next() << 32) | (uint)rng.Next();
                Check.Equal(v, GrayCode.FromGray(GrayCode.ToGray(v)), $"value {v}");
            }
            Check.Equal(ulong.MaxValue, GrayCode.FromGray(GrayCode.ToGray(ulong.MaxValue)));
        }

        // Defining property: consecutive integers' Gray codes differ in exactly one bit.
        public void ConsecutiveDifferByOneBit()
        {
            for (uint i = 0; i < 100000; i++)
            {
                uint diff = GrayCode.ToGray(i) ^ GrayCode.ToGray(i + 1);
                Check.Equal(1, PopCount(diff), $"i={i}");
            }
        }

        private static int PopCount(uint x)
        {
            int c = 0;
            while (x != 0) { x &= x - 1; c++; }
            return c;
        }
    }
}
