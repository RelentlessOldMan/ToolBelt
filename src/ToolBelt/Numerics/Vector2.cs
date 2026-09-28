// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;

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
        public double Length => Math.Sqrt(LengthSquared);

        public double Dot(Vector2 other) => X * other.X + Y * other.Y;

        /// <summary>The z-component of the 3D cross product — positive if <paramref name="other"/> is counter-clockwise.</summary>
        public double Cross(Vector2 other) => X * other.Y - Y * other.X;

        public double DistanceTo(Vector2 other) => (this - other).Length;
        public double DistanceSquaredTo(Vector2 other) => (this - other).LengthSquared;

        /// <summary>Returns a unit vector in the same direction. Throws for the zero vector.</summary>
        public Vector2 Normalized()
        {
            double len = Length;
            if (len == 0) throw new InvalidOperationException("Cannot normalize the zero vector.");
            return new Vector2(X / len, Y / len);
        }

        /// <summary>Angle of the vector from the positive X axis, in radians (-π, π].</summary>
        public double Angle() => Math.Atan2(Y, X);

        /// <summary>Linear interpolation from <paramref name="a"/> to <paramref name="b"/> by <paramref name="t"/>.</summary>
        public static Vector2 Lerp(Vector2 a, Vector2 b, double t) => a + (b - a) * t;

        public bool ApproximatelyEquals(Vector2 other, double tolerance = 1e-9)
            => Math.Abs(X - other.X) <= tolerance && Math.Abs(Y - other.Y) <= tolerance;

        public bool Equals(Vector2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object? obj) => obj is Vector2 v && Equals(v);
        public static bool operator ==(Vector2 a, Vector2 b) => a.Equals(b);
        public static bool operator !=(Vector2 a, Vector2 b) => !a.Equals(b);

        public override int GetHashCode()
        {
            unchecked { return X.GetHashCode() * 397 ^ Y.GetHashCode(); }
        }

        public override string ToString()
            => "(" + X.ToString(CultureInfo.InvariantCulture) + ", " + Y.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
