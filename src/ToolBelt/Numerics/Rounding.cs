// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Numerics;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Rounding helpers beyond <see cref="Math.Round(double)"/>: rounding to an arbitrary multiple and
    /// rounding to a number of significant digits.
    /// </summary>
    public static class Rounding
    {
        /// <summary>Rounds <paramref name="value"/> to the nearest multiple of <paramref name="multiple"/> (which must be
        /// positive), halves away from zero, returning the double nearest that multiple. As in
        /// <see cref="RoundToSignificantDigits"/>, both are read as the shortest decimals that round-trip them, the way they
        /// print: 0.15 rounds to 0.2 at a multiple of 0.1, and 0.3 stays 0.3. Rounding up past <see cref="double.MaxValue"/>
        /// (1.7e308 to a multiple of 1e308) returns infinity.</summary>
        public static double RoundToMultiple(double value, double multiple)
        {
            if (!(multiple > 0) || double.IsInfinity(multiple))
                throw new ArgumentOutOfRangeException(nameof(multiple), multiple, "Multiple must be positive and finite.");
            if (value == 0 || double.IsNaN(value) || double.IsInfinity(value)) return value;
            // Exactly, on the decimals: value / multiple rounded first (0.15 / 0.1 is 1.4999999999999998, so 0.15 went to 0.1;
            // past 2^53 the quotient lost the remainder, and 3.7638018064963644E+22 moved an ulp off a multiple of 6), and
            // k·multiple rounded again (3 · 0.1 is 0.30000000000000004).
            var (vd, ve) = Shortest(Math.Abs(value));
            var (md, me) = Shortest(multiple);
            int e = Math.Min(ve, me);
            BigInteger v = vd * BigInteger.Pow(10, ve - e), m = md * BigInteger.Pow(10, me - e);
            BigInteger k = BigInteger.DivRem(v, m, out BigInteger rem);
            if (2 * rem >= m) k += 1;
            double r = k.IsZero ? 0.0 : Nearest(k * md, me);
            return value < 0 ? -r : r;
        }

        /// <summary>
        /// Rounds <paramref name="value"/> to <paramref name="significantDigits"/> significant figures (halves away
        /// from zero), returning the double nearest that decimal. The digits rounded are those of the shortest decimal
        /// that round-trips the value, the way it prints: 0.15 rounds to 0.2 at one digit even though the double
        /// stored for 0.15 is slightly below it. Works across the whole double range, subnormals included; rounding up
        /// past <see cref="double.MaxValue"/> returns infinity of the same sign.
        /// </summary>
        public static double RoundToSignificantDigits(double value, int significantDigits)
        {
            if (significantDigits < 1)
                throw new ArgumentOutOfRangeException(nameof(significantDigits), significantDigits, "Significant digits must be at least 1.");
            if (value == 0 || double.IsNaN(value) || double.IsInfinity(value))
                return value;

            // Scaling by a power of ten rounded twice (0.15·10 = 1.4999999999999998 kept 0.1; 999.96 at 3 digits gave
            // 999.9999999999999), so the rounding is done on the decimal digits themselves and read back once, exactly.
            Digits(Math.Abs(value), out string digits, out int exponent);
            digits = RoundDigits(digits, significantDigits, ref exponent);
            // Past double.MaxValue this is infinity of the same sign.
            double r = Nearest(BigInteger.Parse(digits, CultureInfo.InvariantCulture), exponent - (digits.Length - 1));
            return value < 0 ? -r : r;
        }

        // The significant digits of the shortest round-trip decimal for a positive finite value (no leading or trailing
        // zeros) and the decimal exponent of the first one: 123.45 → "12345", 2. Found exactly: "R" and double.Parse on
        // .NET Framework are not correctly rounded (it reads 5.39205e252 one ulp low), so they disagreed with .NET Core.
        private static void Digits(double value, out string digits, out int exponent)
        {
            var (d, q) = Shortest(value);
            string s = d.ToString(CultureInfo.InvariantCulture);
            exponent = q + s.Length - 1;
            digits = s.TrimEnd('0');
        }

        // The shortest decimal digits·10^exponent that reads back as x (positive and finite), found exactly with
        // BigInteger so every framework agrees.
        private static (BigInteger Digits, int Exponent) Shortest(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int be = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (be == 0) be = 1; else m |= 1L << 52;
            int k = be - 1075;                                         // x = m·2^k
            bool tight = m == 1L << 52 && be > 1;                      // the gap below a power of two is half the one above
            int top = (int)Math.Floor(Math.Log10(x));
            for (int n = 1; n <= 18; n++)
            {
                int q = top - n + 1;                                   // the place of the last digit
                // Everything times A = 10^max(−q, 0)·2^max(2 − k, 0), so all terms are integers.
                BigInteger ten = BigInteger.Pow(10, Math.Max(-q, 0)), tenUp = BigInteger.Pow(10, Math.Max(q, 0));
                BigInteger xs = (BigInteger)m * BigInteger.Pow(2, Math.Max(k, 2)) * ten;
                BigInteger num = (BigInteger)m * BigInteger.Pow(2, Math.Max(k, 0)) * ten;
                BigInteger den = BigInteger.Pow(2, Math.Max(-k, 0)) * tenUp;
                BigInteger digits = DivideRounded(num, den);
                BigInteger lhs = digits * tenUp * BigInteger.Pow(2, Math.Max(2 - k, 0));
                BigInteger gap = lhs >= xs ? BigInteger.Pow(2, Math.Max(k - 1, 1)) * ten
                    : BigInteger.Pow(2, tight ? Math.Max(k - 2, 0) : Math.Max(k - 1, 1)) * ten;
                BigInteger diff = BigInteger.Abs(lhs - xs);
                if (diff < gap || (diff == gap && (m & 1) == 0)) return (digits, q);
            }
            throw new InvalidOperationException("Unreachable: 18 digits always read back.");
        }

        private static BigInteger DivideRounded(BigInteger num, BigInteger den) => (2 * num + den) / (2 * den);

        // The double nearest digits·10^exponent (ties to even), found exactly: .NET Framework's double.Parse is not
        // correctly rounded (it reads 5.39205e252 one ulp low).
        private static double Nearest(BigInteger digits, int exponent)
        {
            BigInteger num = exponent >= 0 ? digits * BigInteger.Pow(10, exponent) : digits;
            BigInteger den = exponent >= 0 ? BigInteger.One : BigInteger.Pow(10, -exponent);
            // num / den = q·2^-s with q holding 54 bits (53 and a guard bit), but no finer than half the subnormal spacing.
            int s = 54 - (BitLength(num) - BitLength(den));
            BigInteger q, rem;
            while (true)
            {
                if (s > 1075) s = 1075;
                q = BigInteger.DivRem(s >= 0 ? num << s : num, s >= 0 ? den : den << -s, out rem);
                if (s < 1075 && q < BigInteger.One << 53) { s++; continue; }
                if (q >= BigInteger.One << 54) { s--; continue; }
                break;
            }
            BigInteger mant = q >> 1;
            if (!q.IsEven && (!rem.IsZero || !mant.IsEven)) mant += 1;
            int e = 1 - s;                                       // the value is mant·2^e
            if (e > 971 + 1) return double.PositiveInfinity;
            double r = (double)mant;                             // at most 2^53: exact
            if (e < -1000) { r *= Pow2(-1000); e += 1000; }      // stays normal: mant ≥ 1, so ≥ 2^-1000
            return r * Pow2(e);                                  // exact: mant is on the double grid at that scale
        }

        private static int BitLength(BigInteger v)
        {
            byte[] b = v.ToByteArray();
            int top = b.Length - 1;
            while (top > 0 && b[top] == 0) top--;
            int bits = top * 8, last = b[top];
            while (last != 0) { bits++; last >>= 1; }
            return bits;
        }

        private static double Pow2(int k) => k > 1023 ? Pow2(1023) * Pow2(k - 1023) : BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);


        // Keeps the first `count` digits, rounding half away from zero; a carry out of all nines becomes "1" one decade up.
        private static string RoundDigits(string digits, int count, ref int exponent)
        {
            if (digits.Length <= count) return digits;
            char[] kept = digits.Substring(0, count).ToCharArray();
            if (digits[count] >= '5')
            {
                int i = count - 1;
                while (i >= 0 && kept[i] == '9') kept[i--] = '0';
                if (i < 0) { exponent++; return "1"; }
                kept[i]++;
            }
            return new string(kept).TrimEnd('0');
        }
    }
}
