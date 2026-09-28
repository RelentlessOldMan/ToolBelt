// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Angle helpers in degrees: normalization into a canonical range, shortest signed difference,
    /// shortest-path interpolation, and degree/radian conversion. Handles wraparound so, e.g., the
    /// difference from 350° to 10° is +20°, not -340°.
    /// </summary>
    public static class Angle
    {
        /// <summary>Normalizes to the range [0, 360).</summary>
        public static double NormalizeDegrees(double degrees)
        {
            double d = degrees % 360.0;
            if (d < 0) d += 360.0;
            return d == 360.0 ? 0.0 : d; // guard the -0-rounds-to-360 edge
        }

        /// <summary>Normalizes to the range (-180, 180].</summary>
        public static double NormalizeSignedDegrees(double degrees)
        {
            double d = NormalizeDegrees(degrees);
            return d > 180.0 ? d - 360.0 : d;
        }

        /// <summary>The shortest signed rotation from <paramref name="from"/> to <paramref name="to"/>, in (-180, 180].</summary>
        public static double DifferenceDegrees(double from, double to)
            => NormalizeSignedDegrees(to - from);

        /// <summary>Interpolates from <paramref name="from"/> toward <paramref name="to"/> along the shortest arc, result in [0, 360).</summary>
        public static double LerpDegrees(double from, double to, double t)
            => NormalizeDegrees(from + DifferenceDegrees(from, to) * t);

        public static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180.0);

        public static double RadiansToDegrees(double radians) => radians * (180.0 / Math.PI);
    }
}
