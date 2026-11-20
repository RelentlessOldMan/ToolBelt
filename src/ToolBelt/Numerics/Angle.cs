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
            Finite(degrees, nameof(degrees));
            double d = degrees % 360.0 + 0.0; // + 0.0 turns −0 (from −0 or −360) into +0
            if (d < 0) d += 360.0;
            return d == 360.0 ? 0.0 : d; // guard the -0-rounds-to-360 edge
        }

        /// <summary>Normalizes to the range (-180, 180].</summary>
        public static double NormalizeSignedDegrees(double degrees)
        {
            Finite(degrees, nameof(degrees));
            // Straight from the remainder (exact), and each step below is exact too: going through [0, 360) rounded a
            // small negative angle to 360, so −1e-15 came out 0.
            double d = degrees % 360.0;
            if (d > 180.0) d -= 360.0;
            else if (d <= -180.0) d += 360.0;
            return d + 0.0; // + 0.0 turns −0 into +0
        }

        /// <summary>The shortest signed rotation from <paramref name="from"/> to <paramref name="to"/>, in (-180, 180].</summary>
        // Each angle is reduced (exactly) before the subtraction: to − from overflowed for 1.7e308 and −1.7e308 (NaN) and
        // lost the small part of a large angle (3.6e17 to 10 was 0, not 10).
        public static double DifferenceDegrees(double from, double to)
        {
            Finite(from, nameof(from));
            Finite(to, nameof(to));
            // The subtraction's rounding error is kept and added back after the (exact) reduction, so the result is
            // rounded once at its own size: rounded at the size of the difference (up to 720), (359, 1e-14) gave 1.
            double a = to % 360.0, b = -(from % 360.0);
            double s = a + b, bp = s - a, e = (a - (s - bp)) + (b - bp); // TwoSum: s + e = a + b exactly
            double d = NormalizeSignedDegrees(s);
            if (e > 0 && d == 180) d = -180;               // s + e is just past 180
            else if (e < 0 && d + 180 <= -e) d += 360;     // s + e is at or below −180
            double r = d + e;
            if (r == -180) r = 180;                        // just above −180, rounded onto it
            return r + 0.0;
        }

        /// <summary>Interpolates from <paramref name="from"/> toward <paramref name="to"/> along the shortest arc, result in [0, 360).</summary>
        // Stepped from the nearer end, so both ends are exact: from + (to − from)·1 rounded (−353.7 to 193.9 at t = 1 came
        // out 193.9000000000001).
        public static double LerpDegrees(double from, double to, double t)
        {
            // An infinite t threw only when the ends differed (0 · ∞ was NaN when they were equal).
            if (double.IsInfinity(t)) throw new ArgumentOutOfRangeException(nameof(t), t, "The step from from toward to overflows.");
            double diff = DifferenceDegrees(from, to);
            bool fromEnd = t <= 0.5;
            double step = diff * (fromEnd ? t : t - 1);
            if (double.IsInfinity(step))
                throw new ArgumentOutOfRangeException(nameof(t), t, "The step from from toward to overflows.");
            return NormalizeDegrees((fromEnd ? from : to) % 360.0 + step);
        }

        // ±∞ has no direction: every normalization of it was NaN. NaN still passes through.
        private static void Finite(double degrees, string name)
        {
            if (double.IsInfinity(degrees))
                throw new ArgumentOutOfRangeException(name, degrees, "Angle must not be infinite.");
        }

        public static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180.0);

        public static double RadiansToDegrees(double radians) => radians * (180.0 / Math.PI);
    }
}
