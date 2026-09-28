// ToolBelt drop-in — fully self-contained (BCL only).
namespace ToolBelt.Binary
{
    /// <summary>
    /// Bit-twiddling helpers — population count, leading/trailing zero count, and rotations — for 32- and
    /// 64-bit unsigned integers. Hand-rolled so they work identically on netstandard2.0 (which lacks
    /// <c>System.Numerics.BitOperations</c>). Zero-count of 0 is the full width (32 or 64).
    /// </summary>
    public static class Bits
    {
        /// <summary>Number of set bits.</summary>
        public static int PopCount(uint x)
        {
            x -= (x >> 1) & 0x55555555u;
            x = (x & 0x33333333u) + ((x >> 2) & 0x33333333u);
            x = (x + (x >> 4)) & 0x0F0F0F0Fu;
            return (int)((x * 0x01010101u) >> 24);
        }

        /// <summary>Number of set bits.</summary>
        public static int PopCount(ulong x)
        {
            x -= (x >> 1) & 0x5555555555555555UL;
            x = (x & 0x3333333333333333UL) + ((x >> 2) & 0x3333333333333333UL);
            x = (x + (x >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((x * 0x0101010101010101UL) >> 56);
        }

        /// <summary>Number of leading zero bits (32 if <paramref name="x"/> is 0).</summary>
        public static int LeadingZeroCount(uint x)
        {
            if (x == 0) return 32;
            int n = 0;
            while ((x & 0x80000000u) == 0) { x <<= 1; n++; }
            return n;
        }

        /// <summary>Number of leading zero bits (64 if <paramref name="x"/> is 0).</summary>
        public static int LeadingZeroCount(ulong x)
        {
            if (x == 0) return 64;
            int n = 0;
            while ((x & 0x8000000000000000UL) == 0) { x <<= 1; n++; }
            return n;
        }

        /// <summary>Number of trailing zero bits (32 if <paramref name="x"/> is 0).</summary>
        public static int TrailingZeroCount(uint x)
        {
            if (x == 0) return 32;
            int n = 0;
            while ((x & 1u) == 0) { x >>= 1; n++; }
            return n;
        }

        /// <summary>Number of trailing zero bits (64 if <paramref name="x"/> is 0).</summary>
        public static int TrailingZeroCount(ulong x)
        {
            if (x == 0) return 64;
            int n = 0;
            while ((x & 1UL) == 0) { x >>= 1; n++; }
            return n;
        }

        /// <summary>Rotates the bits left by <paramref name="count"/> (any count; taken mod 32).</summary>
        public static uint RotateLeft(uint value, int count)
        {
            count &= 31;
            return count == 0 ? value : (value << count) | (value >> (32 - count));
        }

        /// <summary>Rotates the bits right by <paramref name="count"/> (any count; taken mod 32).</summary>
        public static uint RotateRight(uint value, int count)
        {
            count &= 31;
            return count == 0 ? value : (value >> count) | (value << (32 - count));
        }

        /// <summary>Rotates the bits left by <paramref name="count"/> (any count; taken mod 64).</summary>
        public static ulong RotateLeft(ulong value, int count)
        {
            count &= 63;
            return count == 0 ? value : (value << count) | (value >> (64 - count));
        }

        /// <summary>Rotates the bits right by <paramref name="count"/> (any count; taken mod 64).</summary>
        public static ulong RotateRight(ulong value, int count)
        {
            count &= 63;
            return count == 0 ? value : (value >> count) | (value << (64 - count));
        }
    }
}
