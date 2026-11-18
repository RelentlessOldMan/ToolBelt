// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Numerics;

namespace ToolBelt.Numerics
{
    /// <summary>How many significant digits an uncertainty keeps.</summary>
    public enum UncertaintyRule
    {
        /// <summary>
        /// The Particle Data Group convention: from the three leading digits of the uncertainty, 100–354 keep two digits,
        /// 355–949 keep one, and 950–999 round up to 1000 and keep two.
        /// </summary>
        ParticleDataGroup,
        TwoSignificantDigits,
        OneSignificantDigit,
    }

    /// <summary>How <see cref="Uncertainty.Format"/> writes a value with its uncertainty.</summary>
    public enum UncertaintyStyle
    {
        /// <summary>"12.346 ± 0.012"</summary>
        PlusMinus,
        /// <summary>"12.346(12)" — the uncertainty in units of the last digit.</summary>
        Parenthetical,
    }

    /// <summary>
    /// Reports a measured value at the precision its uncertainty justifies: the uncertainty is rounded to one or two
    /// significant digits and the value to the same decimal place — so a report shows "12.346 ± 0.012" instead of fifteen
    /// meaningless digits. Values and uncertainties of any magnitude are handled (rounding that would carry past
    /// <see cref="double.MaxValue"/> throws <see cref="ArgumentOutOfRangeException"/>); very large or small ones are written
    /// with a shared exponent ("(1.234 ± 0.030)e-7"), down to subnormal uncertainties. An uncertainty below 10⁻¹⁷ of
    /// the value is finer than the value itself can be known and is rejected. Formatting is culture-invariant.
    /// </summary>
    public static class Uncertainty
    {
        /// <summary>
        /// The rounded value and uncertainty, and the number of decimal places they share (negative when rounding to tens,
        /// hundreds, ...).
        /// </summary>
        public static (double Value, double Uncertainty, int Decimals) Round(double value, double uncertainty, UncertaintyRule rule = UncertaintyRule.ParticleDataGroup)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be finite.");
            // An undefined rule took the PDG digit counts without PDG's 950–999 step.
            if (rule != UncertaintyRule.ParticleDataGroup && rule != UncertaintyRule.TwoSignificantDigits && rule != UncertaintyRule.OneSignificantDigit)
                throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown uncertainty rule.");
            if (!(uncertainty > 0) || double.IsInfinity(uncertainty)) throw new ArgumentOutOfRangeException(nameof(uncertainty), uncertainty, "Uncertainty must be positive and finite.");
            // Rounding 1e300 to 1e-300's decimal place overflowed 10³⁰¹ and returned NaN.
            if (uncertainty < Math.Abs(value) * 1e-17)
                throw new ArgumentOutOfRangeException(nameof(uncertainty), uncertainty, "Uncertainty is below the value's own precision (10^-17 of it).");

            // Leading three digits of the uncertainty (100 ≤ lead < 1000) and its decimal exponent, read exactly from its
            // shortest round-trip decimal: a +1e-9 fudge on the shifted double made 0.0354999999999 lead 355 (one digit).
            var (sd, sq) = Shortest(uncertainty);
            int length = sd.ToString(CultureInfo.InvariantCulture).Length;
            int exponent = sq + length - 1;
            int lead = (int)(length >= 3 ? sd / BigInteger.Pow(10, length - 3) : sd * BigInteger.Pow(10, 3 - length));

            int digits = rule switch
            {
                UncertaintyRule.OneSignificantDigit => 1,
                UncertaintyRule.TwoSignificantDigits => 2,
                _ => lead <= 354 ? 2 : (lead <= 949 ? 1 : 2),
            };
            // PDG: 950–999 is rounded up to 1000 and shown with two digits — i.e. "10" one decade higher (0.096 → 0.10).
            if (rule == UncertaintyRule.ParticleDataGroup && lead >= 950) exponent++;
            int decimals = digits - 1 - exponent;
            double u = RoundTo(uncertainty, decimals);
            RequireInRange(u, nameof(uncertainty));
            // Rounding may carry into a new leading digit (0.0996 → 0.100; PDG's 950–999 → 0.10): keep `digits` digits of the result.
            int roundedExponent = (int)Math.Floor(Math.Log10(u) + 1e-12);
            if (roundedExponent > exponent)
            {
                decimals = digits - 1 - roundedExponent;
                u = RoundTo(uncertainty, decimals);
                RequireInRange(u, nameof(uncertainty));
            }
            double rounded = RoundTo(value, decimals);
            RequireInRange(rounded, nameof(value));
            return (rounded, u, decimals);
        }

        // Rounding near double.MaxValue can carry past it (1.79e308 to the nearest 1e308 is 2e308).
        private static void RequireInRange(double rounded, string paramName)
        {
            if (double.IsInfinity(rounded))
                throw new ArgumentOutOfRangeException(paramName, "Rounded, this exceeds the double range.");
        }

        /// <summary>
        /// The value and its uncertainty as text, rounded per <paramref name="rule"/>. Plain notation is used when the shared
        /// decimal place is between 10⁻⁶ and 10⁶ and the value is below 10⁷; otherwise both share an exponent. Each number is
        /// rounded as its shortest round-trip decimal (1.7e308 is 1.7·10³⁰⁸, not the binary 1.6999999999999999734·10³⁰⁸), so
        /// the text is the same on every framework however many digits it shows.
        /// </summary>
        public static string Format(double value, double uncertainty, UncertaintyStyle style = UncertaintyStyle.PlusMinus,
            UncertaintyRule rule = UncertaintyRule.ParticleDataGroup)
        {
            // An undefined style wrote PlusMinus.
            if (style != UncertaintyStyle.PlusMinus && style != UncertaintyStyle.Parenthetical)
                throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown uncertainty style.");
            var (v, u, decimals) = Round(value, uncertainty, rule);
            if (decimals <= 6 && decimals >= -6 && Math.Abs(v) < 1e7)
                return Compose(v, u, Math.Max(decimals, 0), style, null);

            int e = (int)Math.Floor(Math.Log10(Math.Max(Math.Abs(v), u)));
            int d = Math.Max(decimals + e, 0);
            // The mantissas can need 17 or more digits, past what "F" gives the same on every framework (.NET Framework
            // pads zeros after 15, so 1/3 ± 3e-17 came out 10 uncertainties off; .NET 8 showed binary noise). So they are
            // rounded in decimal, from each double's shortest round-trip digits.
            int place = e - d;
            string vs = Fixed(value, place, d), us = Fixed(u, place, d);
            string es = e.ToString(CultureInfo.InvariantCulture);
            if (style == UncertaintyStyle.Parenthetical)
                return vs + "(" + RoundAt(u, place).ToString(CultureInfo.InvariantCulture) + ")e" + es;
            return "(" + vs + " ± " + us + ")e" + es;
        }

        // x / 10^(place + decimals) written with `decimals` decimals, x rounded half away from zero at 10^place.
        private static string Fixed(double x, int place, int decimals)
        {
            BigInteger n = RoundAt(x, place);
            string digits = BigInteger.Abs(n).ToString(CultureInfo.InvariantCulture);
            if (decimals > 0)
            {
                if (digits.Length <= decimals) digits = new string('0', decimals - digits.Length + 1) + digits;
                digits = digits.Substring(0, digits.Length - decimals) + "." + digits.Substring(digits.Length - decimals);
            }
            return n.Sign < 0 ? "-" + digits : digits;
        }

        // x / 10^place rounded half away from zero, taking x as its shortest round-trip decimal.
        private static BigInteger RoundAt(double x, int place)
        {
            if (x == 0) return BigInteger.Zero;
            var (digits, q) = Shortest(Math.Abs(x));
            BigInteger n = q >= place ? digits * BigInteger.Pow(10, q - place) : DivideRounded(digits, BigInteger.Pow(10, place - q));
            return x < 0 ? -n : n;
        }

        private static BigInteger DivideRounded(BigInteger num, BigInteger den) => (2 * num + den) / (2 * den);

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

        private static string Compose(double v, double u, int decimals, UncertaintyStyle style, int? exponent)
        {
            string fmt = decimals > 0 ? "F" + decimals.ToString(CultureInfo.InvariantCulture) : "F0";
            string vs = Clean(v.ToString(fmt, CultureInfo.InvariantCulture));
            string text;
            if (style == UncertaintyStyle.Parenthetical)
            {
                // The uncertainty in units of the last shown digit of the value.
                double inLastDigit = Math.Round(u * Math.Pow(10, Math.Max(decimals, 0)), MidpointRounding.AwayFromZero);
                text = vs + "(" + inLastDigit.ToString("0", CultureInfo.InvariantCulture) + ")";
                return exponent is int e1 ? text + "e" + e1.ToString(CultureInfo.InvariantCulture) : text;
            }
            text = vs + " ± " + u.ToString(fmt, CultureInfo.InvariantCulture);
            return exponent is int e2 ? "(" + text + ")e" + e2.ToString(CultureInfo.InvariantCulture) : text;
        }

        private static string Clean(string s) => s.StartsWith("-", StringComparison.Ordinal) && s.Trim('-', '0', '.').Length == 0 ? s.Substring(1) : s;

        // x rounded half away from zero at 10^-decimals, taking x as its shortest round-trip decimal as Format does, and
        // read back correctly rounded. Math.Round rounded the binary value for 0–15 decimals (1.005 to 1.00) while the
        // shifted path rounded the decimal (1.005e-9 to 1.01e-9), so the same digits rounded by their magnitude.
        private static double RoundTo(double x, int decimals)
        {
            if (x == 0) return x;
            BigInteger n = RoundAt(x, -decimals);
            if (n.IsZero) return x < 0 ? -0.0 : 0.0;
            double r = Nearest(BigInteger.Abs(n), -decimals);
            return n.Sign < 0 ? -r : r;
        }

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
    }
}
