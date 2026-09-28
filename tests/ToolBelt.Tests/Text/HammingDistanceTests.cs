using System;
using System.Text;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class HammingDistanceTests
    {
        public void KnownStrings()
        {
            Check.Equal(3, HammingDistance.Between("karolin", "kathrin"));
            Check.Equal(2, HammingDistance.Between("1011101", "1001001"));
            Check.Equal(0, HammingDistance.Between("same", "same"));
        }

        public void BitDistance_Known()
        {
            Check.Equal(0, HammingDistance.BitDistance(new byte[] { 0xFF }, new byte[] { 0xFF }));
            Check.Equal(8, HammingDistance.BitDistance(new byte[] { 0x00 }, new byte[] { 0xFF }));
            Check.Equal(2, HammingDistance.BitDistance(new byte[] { 0b1010 }, new byte[] { 0b0000 }));
        }

        public void LengthMismatch_Throws()
        {
            Check.Throws<ArgumentException>(() => HammingDistance.Between("ab", "abc"));
            Check.Throws<ArgumentException>(() => HammingDistance.BitDistance(new byte[1], new byte[2]));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => HammingDistance.Between(null!, "a"));
        }

        // Property: symmetric, self-distance is zero, and bounded by length.
        public void Properties_OverRandomStrings()
        {
            var rng = new Random(4);
            for (int trial = 0; trial < 2000; trial++)
            {
                int len = rng.Next(0, 20);
                var a = new char[len];
                var b = new char[len];
                for (int i = 0; i < len; i++)
                {
                    a[i] = (char)('a' + rng.Next(0, 3));
                    b[i] = (char)('a' + rng.Next(0, 3));
                }
                string sa = new string(a), sb = new string(b);
                int d = HammingDistance.Between(sa, sb);
                Check.Equal(d, HammingDistance.Between(sb, sa), $"trial {trial}: symmetry");
                Check.Equal(0, HammingDistance.Between(sa, sa), $"trial {trial}: self");
                Check.True(d >= 0 && d <= len, $"trial {trial}: bounds");
            }
        }
    }
}
