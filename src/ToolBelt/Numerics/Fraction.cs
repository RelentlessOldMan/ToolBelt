// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Numerics;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// An immutable exact rational number, always stored in lowest terms with a non-negative denominator
    /// (the sign lives on the numerator). Arithmetic is exact via <see cref="BigInteger"/>; only
    /// <see cref="ToDouble"/> is lossy.
    /// </summary>
    public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
    {
        public BigInteger Numerator { get; }
        public BigInteger Denominator { get; }

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
            Denominator = denominator;
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

        public double ToDouble() => (double)Numerator / (double)Denominator;

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
            return new Fraction(num, den);
        }

        public override string ToString()
            => Denominator.IsOne
                ? Numerator.ToString(CultureInfo.InvariantCulture)
                : Numerator.ToString(CultureInfo.InvariantCulture) + "/" + Denominator.ToString(CultureInfo.InvariantCulture);
    }
}
