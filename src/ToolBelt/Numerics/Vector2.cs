// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using BigInteger = System.Numerics.BigInteger;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// An immutable 2D vector of <see cref="double"/> components with the usual arithmetic and geometric
    /// operations. Value equality is exact; use <see cref="ApproximatelyEquals"/> for tolerance-based
    /// comparison of computed results.
    /// </summary>
    public readonly struct Vector2 : IEquatable<Vector2>
    {
        public double X { get; }
        public double Y { get; }

        public Vector2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vector2 Zero = new Vector2(0, 0);
        public static readonly Vector2 UnitX = new Vector2(1, 0);
        public static readonly Vector2 UnitY = new Vector2(0, 1);

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.X, -a.Y);
        public static Vector2 operator *(Vector2 a, double scalar) => new Vector2(a.X * scalar, a.Y * scalar);
        public static Vector2 operator *(double scalar, Vector2 a) => a * scalar;

        public static Vector2 operator /(Vector2 a, double scalar)
        {
            if (scalar == 0) throw new DivideByZeroException("Cannot divide a vector by zero.");
            return new Vector2(a.X / scalar, a.Y / scalar);
        }

        public double LengthSquared => X * X + Y * Y;

        /// <summary>The Euclidean length, computed without overflow or underflow of the squares.</summary>
        public double Length
        {
            get
            {
                double ax = Math.Abs(X), ay = Math.Abs(Y);
                if (double.IsInfinity(ax) || double.IsInfinity(ay)) return double.PositiveInfinity;
                double big = Math.Max(ax, ay), small = Math.Min(ax, ay);
                if (big == 0 || double.IsNaN(big) || double.IsNaN(small)) return big + small; // 0, or NaN
                double r = small / big;
                return big * Math.Sqrt(1 + r * r);
            }
        }

        public double Dot(Vector2 other) => X * other.X + Y * other.Y;

        /// <summary>The z-component of the 3D cross product — positive if <paramref name="other"/> is counter-clockwise.</summary>
        public double Cross(Vector2 other) => X * other.Y - Y * other.X;

        public double DistanceTo(Vector2 other) => (this - other).Length;
        public double DistanceSquaredTo(Vector2 other) => (this - other).LengthSquared;

        /// <summary>
        /// Returns a unit vector in the same direction. Throws for the zero vector. Infinite components dominate: (∞, 3)
        /// normalizes to (1, 0) and (−∞, ∞) to (−√½, √½). A NaN component gives NaN.
        /// </summary>
        public Vector2 Normalized()
        {
            if (double.IsNaN(X) || double.IsNaN(Y)) return new Vector2(double.NaN, double.NaN);   // (∞, NaN) gave (1, 0)
            if (double.IsInfinity(X) || double.IsInfinity(Y))
                return new Vector2(double.IsInfinity(X) ? Math.Sign(X) : 0, double.IsInfinity(Y) ? Math.Sign(Y) : 0).Normalized();
            // Scale first: dividing by an overflowed length gave (0, 0), and by an underflowed one threw.
            double scale = Math.Max(Math.Abs(X), Math.Abs(Y));
            if (scale == 0) throw new InvalidOperationException("Cannot normalize the zero vector.");
            var v = new Vector2(X / scale, Y / scale);
            double len = v.Length;
            return new Vector2(v.X / len, v.Y / len);
        }

        /// <summary>Angle of the vector from the positive X axis, in radians (-π, π].</summary>
        public double Angle()
        {
            double angle = Math.Atan2(Y, X);
            return angle == -Math.PI ? Math.PI : angle; // Atan2(−0, −1) is −π; keep the documented half-open range
        }

        /// <summary>Linear interpolation from <paramref name="a"/> to <paramref name="b"/> by <paramref name="t"/>.</summary>
        public static Vector2 Lerp(Vector2 a, Vector2 b, double t) => new Vector2(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t));

        private static double Lerp(double a, double b, double t)
        {
            // The ends are exact and an interior t stays between them: a + (b − a)·1 lost a small b next to a large a
            // (Lerp(1e16, 1, 1) gave 0).
            if (t == 0) return a;
            if (t == 1) return b;
            // Equal ends are the whole line: ∞ − ∞ made Lerp(∞, ∞, 0.5) NaN though both ends are ∞.
            if (a == b && !double.IsNaN(t) && !double.IsInfinity(t)) return a;
            double d = b - a, r;
            // Finite ends of opposite sign can overflow b − a (−1e308 to 1e308): interpolate the halves instead.
            if (double.IsInfinity(d) && !double.IsInfinity(a) && !double.IsInfinity(b))
                r = 2 * (0.5 * a + (0.5 * b - 0.5 * a) * t);
            // An infinite end with a finite t: a + (b − a)·t was ∞ + (−∞)·t, so Lerp(∞, 1, 0.5) was NaN though
            // Lerp(1, ∞, 0.5) was ∞. The weighted form gives both ends' signs their due.
            else if ((double.IsInfinity(a) || double.IsInfinity(b)) && !double.IsInfinity(t))
                r = (1 - t) * a + t * b;
            else
                r = a + d * t;
            if (t > 0 && t < 1) r = Math.Min(Math.Max(r, Math.Min(a, b)), Math.Max(a, b));
            return r;
        }

        public bool ApproximatelyEquals(Vector2 other, double tolerance = 1e-9)
            => Near(X, other.X, tolerance) && Near(Y, other.Y, tolerance);

        // Equal components count as near: ∞ − ∞ is NaN, so a vector with an infinite part was not near itself.
        private static bool Near(double a, double b, double tolerance) => a.Equals(b) || Math.Abs(a - b) <= tolerance;

        public bool Equals(Vector2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object? obj) => obj is Vector2 v && Equals(v);
        public static bool operator ==(Vector2 a, Vector2 b) => a.Equals(b);
        public static bool operator !=(Vector2 a, Vector2 b) => !a.Equals(b);

        public override int GetHashCode()
        {
            unchecked { return Hash(X) * 397 ^ Hash(Y); }
        }

        // Equals treats every NaN as equal, and 0.0 as -0.0, but .NET Framework hashes their bits: canonicalise.
        private static int Hash(double d) => double.IsNaN(d) ? 1 : (d == 0 ? 0 : d.GetHashCode());

        public override string ToString() => "(" + Format(X) + ", " + Format(Y) + ")";

        // The shortest round-trip form, written as .NET Core's double.ToString() writes it, on every framework: .NET
        // Framework wrote 15 digits, so 0.1 + 0.2 came out "0.3", which reads back as another number.
        private static string Format(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x.ToString(CultureInfo.InvariantCulture);
            if (x == 0) return BitConverter.DoubleToInt64Bits(x) < 0 ? "-0" : "0";
            var (d, q) = Shortest(Math.Abs(x));
            string s = d.ToString(CultureInfo.InvariantCulture);
            int e = q + s.Length - 1;                                  // the decimal exponent of the first digit
            s = s.TrimEnd('0');
            string r;
            if (e >= 17 || e <= -5)                            // where .NET Core switches to exponent form
                r = s.Substring(0, 1) + (s.Length > 1 ? "." + s.Substring(1) : "") + (e < 0 ? "E-" : "E+")
                    + Math.Abs(e).ToString("00", CultureInfo.InvariantCulture);
            else if (e < 0) r = "0." + new string('0', -e - 1) + s;
            else if (s.Length <= e + 1) r = s + new string('0', e + 1 - s.Length);
            else r = s.Substring(0, e + 1) + "." + s.Substring(e + 1);
            return x < 0 ? "-" + r : r;
        }

        // The shortest decimal digits·10^exponent that reads back as x (positive and finite), found exactly with
        // BigInteger so every framework agrees (as EngineeringNotation does).
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
                BigInteger digits = BigInteger.DivRem(num, den, out BigInteger rem);
                int half = (2 * rem).CompareTo(den);
                if (half > 0 || (half == 0 && !digits.IsEven)) digits++;  // ties to even, as .NET Core rounds
                BigInteger lhs = digits * tenUp * BigInteger.Pow(2, Math.Max(2 - k, 0));
                BigInteger gap = lhs >= xs ? BigInteger.Pow(2, Math.Max(k - 1, 1)) * ten
                    : BigInteger.Pow(2, tight ? Math.Max(k - 2, 0) : Math.Max(k - 1, 1)) * ten;
                BigInteger diff = BigInteger.Abs(lhs - xs);
                if (diff < gap || (diff == gap && (m & 1) == 0)) return (digits, q);
            }
            throw new InvalidOperationException("Unreachable: 18 digits always read back.");
        }
    }
}
