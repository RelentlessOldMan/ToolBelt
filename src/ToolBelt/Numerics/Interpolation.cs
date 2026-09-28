// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Scalar interpolation and range-mapping helpers: linear interpolation and its inverse, clamping,
    /// range remapping, and smoothstep. All operate on <see cref="double"/>.
    /// </summary>
    public static class Interpolation
    {
        /// <summary>Linear interpolation: returns <c>a + (b - a) * t</c>. <paramref name="t"/> is not clamped.</summary>
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>Linear interpolation with <paramref name="t"/> clamped to [0, 1].</summary>
        public static double LerpClamped(double a, double b, double t) => Lerp(a, b, Clamp(t, 0, 1));

        /// <summary>The inverse of <see cref="Lerp"/>: the <c>t</c> for which <c>Lerp(a, b, t) == value</c>. Returns 0 if a == b.</summary>
        public static double InverseLerp(double a, double b, double value)
            => a == b ? 0.0 : (value - a) / (b - a);

        /// <summary>Clamps <paramref name="value"/> to the inclusive range [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public static double Clamp(double value, double min, double max)
        {
            if (min > max)
                throw new ArgumentException("min must not exceed max.", nameof(min));
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>Maps <paramref name="value"/> from the input range onto the output range (linearly, unclamped).</summary>
        public static double Remap(double value, double inMin, double inMax, double outMin, double outMax)
            => Lerp(outMin, outMax, InverseLerp(inMin, inMax, value));

        /// <summary>
        /// Hermite smoothstep: 0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>, and a
        /// smooth S-curve in between.
        /// </summary>
        public static double SmoothStep(double edge0, double edge1, double x)
        {
            if (edge0 == edge1)
                throw new ArgumentException("edge0 and edge1 must differ.", nameof(edge1));
            double t = Clamp((x - edge0) / (edge1 - edge0), 0, 1);
            return t * t * (3 - 2 * t);
        }
    }
}
