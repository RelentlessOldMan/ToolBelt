// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Numerics;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Formats a value with an SI prefix (yocto through yotta) at a chosen significant-figure count, and
    /// parses the same form back. Appears in every plot axis label, report table and console readout, and
    /// is otherwise re-derived badly. Round-trips to the chosen precision (NaN and ±infinity format as
    /// "NaN" / "Infinity" / "-Infinity" and parse back); values beyond the yocto–yotta range use E notation with
    /// no prefix. More than 17 significant digits are treated as 17 (all a double holds). Parsing accepts the
    /// micro sign (U+00B5), Greek mu (U+03BC) and ASCII 'u' for micro.
    /// </summary>
    public static class EngineeringNotation
    {
        // Index 0 == exponent 0; positive indices step up by 3, negative down by 3.
        private static readonly string[] PositivePrefixes = { "", "k", "M", "G", "T", "P", "E", "Z", "Y" };
        private static readonly string[] NegativePrefixes = { "", "m", "µ", "n", "p", "f", "a", "z", "y" };
        private const int MaxExponent = 24;

        /// <summary>Formats <paramref name="value"/> as a number with an SI prefix, e.g. 1500 -> "1.5 k".</summary>
        public static string Format(double value, int significantDigits = 3)
        {
            if (significantDigits < 1) throw new ArgumentOutOfRangeException(nameof(significantDigits), significantDigits, "Must be at least 1.");
            if (double.IsNaN(value) || double.IsInfinity(value))
                return value.ToString(CultureInfo.InvariantCulture);
            if (value == 0) return "0";
            if (significantDigits > 17) significantDigits = 17; // all a double holds

            // Rounded on the decimal digits, not by scaling with powers of ten: scaling rounded twice, so 999.9999999999999
            // at 17 digits printed "1 k" and full-precision output did not round-trip.
            Digits(Math.Abs(value), out string digits, out int decimalExponent);
            digits = RoundDigits(digits, significantDigits, ref decimalExponent);
            int exponent = FloorDiv(decimalExponent, 3) * 3;
            // Clamping to yocto/yotta printed "1E-06 y" and "1230000 Y"; out of range, plain E notation reads better.
            // From the digits rounded above: "G" rounded the binary value, so 1.25e27 at 2 digits was "1.2E+27" though
            // 1.25e24 was "1.3 Y".
            if (exponent > MaxExponent || exponent < -MaxExponent)
                return (value < 0 ? "-" : "") + digits.Substring(0, 1) + (digits.Length > 1 ? "." + digits.Substring(1) : "")
                       + (decimalExponent < 0 ? "E-" : "E+") + Math.Abs(decimalExponent).ToString("00", CultureInfo.InvariantCulture);

            // 1 to 3 digits before the point; fixed point always ("G" turned 120 at 2 digits into "1.2E+02").
            int integerDigits = decimalExponent - exponent + 1;
            string number = digits.Length <= integerDigits
                ? digits + new string('0', integerDigits - digits.Length)
                : digits.Substring(0, integerDigits) + "." + digits.Substring(integerDigits);
            if (value < 0) number = "-" + number;
            string prefix = PrefixFor(exponent);
            return prefix.Length == 0 ? number : number + " " + prefix;
        }

        public static double Parse(string text)
        {
            if (!TryParse(text, out double value))
                throw new FormatException($"'{text}' is not a valid engineering-notation value.");
            return value;
        }

        public static bool TryParse(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim();
            // A plain number first, so Format's "NaN" / "Infinity" round-trip (the 'y' of "Infinity" is yocto). A finite
            // text that overflows ("1e400") is not a value; .NET Core parses it as infinity.
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                // A plain decimal is read exactly: .NET Framework's parse is not correctly rounded (Format's own
                // "-4.230547953038732 m" read back an ulp off), so the same text gave different doubles by framework.
                if (Exact(s, 0, out double exact)) value = exact;
                if (!double.IsInfinity(value) || s.IndexOf("Infinity", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                value = 0; // a failed parse reports 0, not the ∞ it overflowed to
                return false;
            }
            char last = s[s.Length - 1];
            int exponent;
            if (char.IsLetter(last) || last == 'µ' || last == 'μ')
            {
                if (!TryPrefixExponent(last, out exponent)) return false;
                s = s.Substring(0, s.Length - 1).Trim();
            }
            else
            {
                return false; // not a plain number and no prefix
            }

            // The prefix joins the mantissa's own exponent and the decimal is parsed once, correctly rounded: multiplying
            // by 10^k rounded twice, and 1e306 k overflowed to infinity instead of failing.
            long total = exponent;
            int ePos = s.IndexOfAny(new[] { 'E', 'e' });
            if (ePos >= 0)
            {
                if (!long.TryParse(s.Substring(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long own))
                    return false;
                total += Math.Max(-100000, Math.Min(100000, own));
                s = s.Substring(0, ePos);
            }
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double mantissa)
                || double.IsNaN(mantissa) || double.IsInfinity(mantissa))
                return false;
            if (!Exact(s.Trim(), total, out double parsed) || double.IsInfinity(parsed)) return false;
            value = parsed;
            return true;
        }

        // [sign] digits [. digits] [e [sign] digits] times 10^shift, as the nearest double; false for any other text.
        private static bool Exact(string s, long shift, out double value)
        {
            value = 0;
            int i = 0, n = s.Length, fraction = 0;
            bool negative = false, any = false, point = false;
            if (i < n && (s[i] == '+' || s[i] == '-')) negative = s[i++] == '-';
            var digits = new System.Text.StringBuilder();
            for (; i < n; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') { any = true; if (digits.Length > 0 || c != '0') digits.Append(c); if (point) fraction++; }
                else if (c == '.' && !point) point = true;
                else break;
            }
            if (!any) return false;
            long own = 0;
            if (i < n && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                bool down = false;
                if (i < n && (s[i] == '+' || s[i] == '-')) down = s[i++] == '-';
                if (i == n) return false;
                for (; i < n && s[i] >= '0' && s[i] <= '9'; i++) own = Math.Min(own * 10 + (s[i] - '0'), 1_000_000);
                if (down) own = -own;
            }
            if (i != n) return false;
            if (digits.Length == 0) { value = negative ? -0.0 : 0.0; return true; }
            long exponent = shift + own - fraction;   // of the last digit
            double r;
            if (exponent + digits.Length > 400) r = double.PositiveInfinity;
            else if (exponent + digits.Length < -400) r = 0;
            else r = Nearest(BigInteger.Parse(digits.ToString(), CultureInfo.InvariantCulture), (int)exponent);
            value = negative ? -r : r;
            return true;
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

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

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

        private static string PrefixFor(int exponent)
        {
            int index = exponent / 3;
            return index >= 0 ? PositivePrefixes[index] : NegativePrefixes[-index];
        }

        private static bool TryPrefixExponent(char symbol, out int exponent)
        {
            switch (symbol)
            {
                case 'k': case 'K': exponent = 3; return true;   // accept uppercase K leniently
                case 'M': exponent = 6; return true;
                case 'G': exponent = 9; return true;
                case 'T': exponent = 12; return true;
                case 'P': exponent = 15; return true;
                case 'E': exponent = 18; return true;
                case 'Z': exponent = 21; return true;
                case 'Y': exponent = 24; return true;
                case 'm': exponent = -3; return true;
                case 'µ': case 'μ': case 'u': exponent = -6; return true;  // micro sign, Greek mu, ASCII 'u'
                case 'n': exponent = -9; return true;
                case 'p': exponent = -12; return true;
                case 'f': exponent = -15; return true;
                case 'a': exponent = -18; return true;
                case 'z': exponent = -21; return true;
                case 'y': exponent = -24; return true;
                default: exponent = 0; return false;
            }
        }
    }
}
