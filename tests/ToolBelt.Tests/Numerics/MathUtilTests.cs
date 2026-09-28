using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class MathUtilTests
    {
        public void Gcd()
        {
            Check.Equal(6L, MathUtil.Gcd(54, 24));
            Check.Equal(1L, MathUtil.Gcd(17, 5));
            Check.Equal(5L, MathUtil.Gcd(-10, 15));
            Check.Equal(0L, MathUtil.Gcd(0, 0));
            Check.Equal(7L, MathUtil.Gcd(0, 7));
        }

        public void Lcm()
        {
            Check.Equal(12L, MathUtil.Lcm(4, 6));
            Check.Equal(0L, MathUtil.Lcm(0, 5));
        }

        public void PowerOfTwo()
        {
            Check.True(MathUtil.IsPowerOfTwo(1));
            Check.True(MathUtil.IsPowerOfTwo(1024));
            Check.False(MathUtil.IsPowerOfTwo(0));
            Check.False(MathUtil.IsPowerOfTwo(1000));
            Check.False(MathUtil.IsPowerOfTwo(-8));
        }

        public void NextPowerOfTwo()
        {
            Check.Equal(1L, MathUtil.NextPowerOfTwo(0));
            Check.Equal(1L, MathUtil.NextPowerOfTwo(1));
            Check.Equal(16L, MathUtil.NextPowerOfTwo(9));
            Check.Equal(1024L, MathUtil.NextPowerOfTwo(1024));
            Check.Equal(2048L, MathUtil.NextPowerOfTwo(1025));
        }

        public void Clamp()
        {
            Check.Equal(5, MathUtil.Clamp(5, 0, 10));
            Check.Equal(0, MathUtil.Clamp(-1, 0, 10));
            Check.Equal(10, MathUtil.Clamp(99, 0, 10));
            Check.Throws<ArgumentException>(() => MathUtil.Clamp(1, 10, 0));
        }

        public void Lcm_Overflow_Throws()
        {
            // Two large coprime-ish values whose LCM exceeds long range must throw, not wrap silently.
            Check.Throws<OverflowException>(() => MathUtil.Lcm(1L << 40, (1L << 40) + 2));
        }

        public void LongMinValue_Rejected()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => MathUtil.Gcd(long.MinValue, 4));
            Check.Throws<ArgumentOutOfRangeException>(() => MathUtil.Lcm(6, long.MinValue));
        }

        // Properties: gcd divides both, lcm is a multiple of both, next-pow-2 bounds and is a power of two.
        public void Properties_OverRandomInts()
        {
            var rng = new Random(246);
            for (int trial = 0; trial < 5000; trial++)
            {
                long a = rng.Next(1, 100000);
                long b = rng.Next(1, 100000);

                long g = MathUtil.Gcd(a, b);
                Check.True(a % g == 0 && b % g == 0, $"trial {trial}: gcd divides both");

                long l = MathUtil.Lcm(a, b);
                Check.True(l % a == 0 && l % b == 0, $"trial {trial}: lcm multiple of both");
                Check.Equal(a * b, g * l); // gcd*lcm == a*b

                long n = rng.Next(1, 1_000_000);
                long np = MathUtil.NextPowerOfTwo(n);
                Check.True(np >= n && MathUtil.IsPowerOfTwo(np), $"trial {trial}: next pow2");
                Check.True(np / 2 < n, $"trial {trial}: smallest such power");
            }
        }
    }
}
