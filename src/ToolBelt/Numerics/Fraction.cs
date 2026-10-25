// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Numerics;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// An immutable exact rational number, always stored in lowest terms with a non-negative denominator
    /// (the sign lives on the numerator). Arithmetic is exact via <see cref="BigInteger"/>; only
    /// <see cref="ToDouble"/> is lossy. <c>default(Fraction)</c> is zero (0/1).
    /// </summary>
    public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
    {
        public BigInteger Numerator { get; }
        // Zero only in default(Fraction), which therefore reads as 0/1 instead of 0/0 (which made ToDouble NaN
        // and Equals(Zero) false).
        private readonly BigInteger _denominator;
        public BigInteger Denominator => _denominator.IsZero ? BigInteger.One : _denominator;

        public static readonly Fraction Zero = new Fraction(BigInteger.Zero, BigInteger.One);
        public static readonly Fraction One = new Fraction(BigInteger.One, BigInteger.One);

        public Fraction(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.IsZero)
                throw new DivideByZeroException("Fraction denominator must not be zero.");

            if (denominator.Sign < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            BigInteger g = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            if (g > BigInteger.One)
            {
                numerator /= g;
                denominator /= g;
            }

            Numerator = numerator;
            _denominator = denominator;
        }

        public static Fraction FromInteger(BigInteger value) => new Fraction(value, BigInteger.One);

        public bool IsZero => Numerator.IsZero;
        public int Sign => Numerator.Sign;

        public Fraction Reciprocal()
        {
            if (Numerator.IsZero) throw new DivideByZeroException("Cannot invert a zero fraction.");
            return new Fraction(Denominator, Numerator);
        }

        public Fraction Abs() => new Fraction(BigInteger.Abs(Numerator), Denominator);

        public static Fraction operator +(Fraction a, Fraction b)
            => new Fraction(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);

        public static Fraction operator -(Fraction a, Fraction b)
            => new Fraction(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);

        public static Fraction operator *(Fraction a, Fraction b)
            => new Fraction(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

        public static Fraction operator /(Fraction a, Fraction b)
        {
            if (b.Numerator.IsZero) throw new DivideByZeroException("Cannot divide a fraction by zero.");
            return new Fraction(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
        }

        public static Fraction operator -(Fraction a) => new Fraction(-a.Numerator, a.Denominator);

        /// <summary>The nearest double (within an ulp), for operands of any size.</summary>
        public double ToDouble()
        {
            BigInteger n = Numerator, d = Denominator;
            BigInteger exact = BigInteger.One << 53;
            if (BigInteger.Abs(n) <= exact && d <= exact) return (double)n / (double)d; // both exact: one rounding
            if (n.IsZero) return 0.0;

            // Converting each side first gave ∞/∞ = NaN past 1e308 and 0 for 10^308/(3·10^308): instead form a
            // quotient of 62–63 bits, n·2^s / d, with a sticky bit for the remainder, and scale back by 2^−s. It goes
            // through long, whose conversion rounds correctly; BigInteger's was an ulp off for −10^401/(7·10^400 + 1).
            BigInteger an = BigInteger.Abs(n);
            int s = 62 - (BitLength(an) - BitLength(d));
            BigInteger q = s >= 0 ? BigInteger.DivRem(an << s, d, out BigInteger rem) : BigInteger.DivRem(an, d << -s, out rem);
            if (!rem.IsZero) q |= BigInteger.One;
            double result = ScaleByPowerOfTwo((long)q, -s);
            return n.Sign < 0 ? -result : result;
        }

        private static int BitLength(BigInteger v)
        {
            byte[] bytes = v.ToByteArray(); // little-endian two's complement; v > 0 here
            int top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0) top--;
            int bits = top * 8;
            for (int b = bytes[top]; b != 0; b >>= 1) bits++;
            return bits;
        }

        // x·2^e in steps that stay representable (2^e alone overflows or underflows for |e| > 1023).
        private static double ScaleByPowerOfTwo(double x, int e)
        {
            while (e > 1000) { x *= Math.Pow(2, 1000); e -= 1000; if (double.IsInfinity(x)) return x; }
            while (e < -1000) { x *= Math.Pow(2, -1000); e += 1000; if (x == 0) return x; }
            return x * Math.Pow(2, e);
        }

        public int CompareTo(Fraction other)
            => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

        public static bool operator <(Fraction a, Fraction b) => a.CompareTo(b) < 0;
        public static bool operator >(Fraction a, Fraction b) => a.CompareTo(b) > 0;
        public static bool operator <=(Fraction a, Fraction b) => a.CompareTo(b) <= 0;
        public static bool operator >=(Fraction a, Fraction b) => a.CompareTo(b) >= 0;

        public bool Equals(Fraction other) => Numerator == other.Numerator && Denominator == other.Denominator;
        public override bool Equals(object? obj) => obj is Fraction f && Equals(f);
        public static bool operator ==(Fraction a, Fraction b) => a.Equals(b);
        public static bool operator !=(Fraction a, Fraction b) => !a.Equals(b);

        public override int GetHashCode()
        {
            unchecked { return Numerator.GetHashCode() * 397 ^ Denominator.GetHashCode(); }
        }

        /// <summary>Parses "n/d" or a plain integer "n". Whitespace around the parts is ignored.</summary>
        public static Fraction Parse(string s)
        {
            if (s is null) throw new ArgumentNullException(nameof(s));
            int slash = s.IndexOf('/');
            if (slash < 0)
            {
                if (!BigInteger.TryParse(s.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
                    throw new FormatException($"'{s}' is not a valid fraction.");
                return FromInteger(whole);
            }

            if (!BigInteger.TryParse(s.Substring(0, slash).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var num) ||
                !BigInteger.TryParse(s.Substring(slash + 1).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var den))
                throw new FormatException($"'{s}' is not a valid fraction.");
            if (den.IsZero) throw new FormatException($"'{s}' has a zero denominator."); // was DivideByZeroException
            return new Fraction(num, den);
        }

        public override string ToString()
            => Denominator.IsOne
                ? Numerator.ToString(CultureInfo.InvariantCulture)
                : Numerator.ToString(CultureInfo.InvariantCulture) + "/" + Denominator.ToString(CultureInfo.InvariantCulture);
    }
}
