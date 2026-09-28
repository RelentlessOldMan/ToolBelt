using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class Vector2Tests
    {
        public void Arithmetic()
        {
            var a = new Vector2(1, 2);
            var b = new Vector2(3, 4);
            Check.True((a + b) == new Vector2(4, 6));
            Check.True((b - a) == new Vector2(2, 2));
            Check.True((a * 2) == new Vector2(2, 4));
            Check.True((2 * a) == new Vector2(2, 4));
            Check.True((b / 2) == new Vector2(1.5, 2));
            Check.True(-a == new Vector2(-1, -2));
        }

        public void DotAndCross()
        {
            Check.Close(11, new Vector2(1, 2).Dot(new Vector2(3, 4)), 1e-12); // 3 + 8
            Check.Close(0, Vector2.UnitX.Dot(Vector2.UnitY), 1e-12);
            Check.Close(1, Vector2.UnitX.Cross(Vector2.UnitY), 1e-12);        // CCW
        }

        public void LengthAndDistance()
        {
            Check.Close(5, new Vector2(3, 4).Length, 1e-12);
            Check.Close(25, new Vector2(3, 4).LengthSquared, 1e-12);
            Check.Close(5, new Vector2(0, 0).DistanceTo(new Vector2(3, 4)), 1e-12);
        }

        public void Normalize()
        {
            var n = new Vector2(3, 4).Normalized();
            Check.Close(1, n.Length, 1e-12);
            Check.True(n.ApproximatelyEquals(new Vector2(0.6, 0.8)));
            Check.Throws<InvalidOperationException>(() => Vector2.Zero.Normalized());
        }

        public void Angle()
        {
            Check.Close(0, Vector2.UnitX.Angle(), 1e-12);
            Check.Close(Math.PI / 2, Vector2.UnitY.Angle(), 1e-12);
        }

        public void Lerp()
        {
            Check.True(Vector2.Lerp(new Vector2(0, 0), new Vector2(10, 20), 0.5) == new Vector2(5, 10));
            Check.True(Vector2.Lerp(new Vector2(0, 0), new Vector2(10, 20), 0) == Vector2.Zero);
        }

        public void DivideByZero_Throws()
        {
            Check.Throws<DivideByZeroException>(() => { var _ = new Vector2(1, 1) / 0; });
        }

        public void EqualityAndHash()
        {
            Check.True(new Vector2(1, 2).Equals(new Vector2(1, 2)));
            Check.Equal(new Vector2(1, 2).GetHashCode(), new Vector2(1, 2).GetHashCode());
            Check.True(new Vector2(1, 2) != new Vector2(2, 1));
        }

        // Property: a normalized non-zero vector always has unit length and preserves direction (angle).
        public void Property_NormalizePreservesDirection()
        {
            var rng = new Random(30);
            for (int t = 0; t < 5000; t++)
            {
                var v = new Vector2(rng.NextDouble() * 200 - 100, rng.NextDouble() * 200 - 100);
                if (v.Length < 1e-6) continue;
                var n = v.Normalized();
                Check.Close(1, n.Length, 1e-9, $"t{t}: unit length");
                Check.Close(v.Angle(), n.Angle(), 1e-9, $"t{t}: same angle");
            }
        }
    }
}
