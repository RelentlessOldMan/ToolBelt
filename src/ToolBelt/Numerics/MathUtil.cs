// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Small integer math helpers not in the BCL: greatest common divisor, least common multiple,
    /// power-of-two tests, and integer clamping.
    /// </summary>
    public static class MathUtil
    {
        /// <summary>Greatest common divisor (non-negative). <c>Gcd(0, 0)</c> is 0.</summary>
        /// <remarks><see cref="long.MinValue"/> is rejected: its magnitude (2^63) is not representable as a positive long.</remarks>
        public static long Gcd(long a, long b)
        {
            // Math.Abs(long.MinValue) overflows; reject up front with a clear argument error.
            if (a == long.MinValue)
                throw new ArgumentOutOfRangeException(nameof(a), "long.MinValue is not supported.");
            if (b == long.MinValue)
                throw new ArgumentOutOfRangeException(nameof(b), "long.MinValue is not supported.");
            a = Math.Abs(a);
            b = Math.Abs(b);
            while (b != 0)
            {
                long t = b;
                b = a % b;
                a = t;
            }
            return a;
        }

        /// <summary>Least common multiple (non-negative). Zero if either operand is zero. Throws <see cref="OverflowException"/> if the result exceeds <see cref="long"/>.</summary>
        public static long Lcm(long a, long b)
        {
            if (a == 0 || b == 0)
                return 0;
            long gcd = Gcd(a, b); // also rejects long.MinValue
            // checked so an out-of-range LCM throws rather than silently wrapping to a wrong value.
            return Math.Abs(checked(a / gcd * b));
        }

        /// <summary>Whether <paramref name="value"/> is a positive power of two.</summary>
        public static bool IsPowerOfTwo(long value) => value > 0 && (value & (value - 1)) == 0;

        /// <summary>The smallest power of two that is &gt;= <paramref name="value"/> (1 for value &lt;= 1).</summary>
        public static long NextPowerOfTwo(long value)
        {
            if (value <= 1)
                return 1;
            if (value > (1L << 62))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Next power of two would overflow.");

            long n = value - 1;
            n |= n >> 1;
            n |= n >> 2;
            n |= n >> 4;
            n |= n >> 8;
            n |= n >> 16;
            n |= n >> 32;
            return n + 1;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (min > max) throw new ArgumentException("min must not exceed max.", nameof(min));
            return value < min ? min : value > max ? max : value;
        }

        public static long Clamp(long value, long min, long max)
        {
            if (min > max) throw new ArgumentException("min must not exceed max.", nameof(min));
            return value < min ? min : value > max ? max : value;
        }
    }
}
