// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Text
{
    /// <summary>
    /// Hamming distance — the number of positions at which two equal-length inputs differ. Both operands
    /// must be the same length (the metric is undefined otherwise). <see cref="Between(string, string)"/>
    /// counts differing characters; <see cref="BitDistance"/> counts differing bits across equal-length
    /// byte arrays.
    /// </summary>
    public static class HammingDistance
    {
        /// <summary>Number of differing character positions. Requires equal-length strings.</summary>
        public static int Between(string a, string b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length)
                throw new ArgumentException("Strings must have equal length.", nameof(b));

            int distance = 0;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    distance++;
            return distance;
        }

        /// <summary>Number of differing bits across two equal-length byte arrays.</summary>
        public static int BitDistance(byte[] a, byte[] b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length)
                throw new ArgumentException("Byte arrays must have equal length.", nameof(b));

            int distance = 0;
            for (int i = 0; i < a.Length; i++)
                distance += PopCount((byte)(a[i] ^ b[i]));
            return distance;
        }

        private static int PopCount(byte value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= (byte)(value - 1);
                count++;
            }
            return count;
        }
    }
}
