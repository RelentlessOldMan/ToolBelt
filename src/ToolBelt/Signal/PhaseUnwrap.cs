// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Removes the 2π discontinuities from a wrapped phase sequence, turning a sawtooth of principal
    /// values into the continuous phase it stands for. The prerequisite for differentiating phase into
    /// instantaneous frequency.
    /// </summary>
    public static class PhaseUnwrap
    {
        /// <summary>
        /// Unwraps <paramref name="phase"/> (radians) so that successive differences never exceed
        /// <paramref name="tolerance"/> in magnitude, adding whole turns of 2π where they would.
        /// </summary>
        public static double[] Unwrap(double[] phase, double tolerance = Math.PI)
        {
            if (phase is null) throw new ArgumentNullException(nameof(phase));
            if (tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Tolerance must be positive.");
            if (phase.Length == 0) return Array.Empty<double>();

            var result = new double[phase.Length];
            result[0] = phase[0];
            double correction = 0;
            for (int i = 1; i < phase.Length; i++)
            {
                double delta = phase[i] - phase[i - 1];
                while (delta > tolerance) { correction -= 2 * Math.PI; delta -= 2 * Math.PI; }
                while (delta < -tolerance) { correction += 2 * Math.PI; delta += 2 * Math.PI; }
                result[i] = phase[i] + correction;
            }
            return result;
        }

        /// <summary>Wraps a single angle into the principal range (−π, π].</summary>
        public static double Wrap(double angle)
        {
            double wrapped = angle % (2 * Math.PI);
            if (wrapped <= -Math.PI) wrapped += 2 * Math.PI;
            else if (wrapped > Math.PI) wrapped -= 2 * Math.PI;
            return wrapped;
        }
    }
}
