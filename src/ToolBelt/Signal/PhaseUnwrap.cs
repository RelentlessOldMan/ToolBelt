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

        /// <summary>
        /// Group delay τ(f) = −dφ/dω, in seconds, from an <b>unwrapped</b> phase response (radians) sampled at
        /// <paramref name="frequencies"/> (Hz, strictly increasing; spacing may vary): central differences inside, one-sided
        /// at the ends. A pure delay of T seconds (φ = −2πfT) gives exactly T everywhere; dispersion shows up as variation.
        /// </summary>
        public static double[] GroupDelay(System.Collections.Generic.IReadOnlyList<double> unwrappedPhase, System.Collections.Generic.IReadOnlyList<double> frequencies)
        {
            if (unwrappedPhase is null) throw new ArgumentNullException(nameof(unwrappedPhase));
            if (frequencies is null) throw new ArgumentNullException(nameof(frequencies));
            int n = unwrappedPhase.Count;
            if (n != frequencies.Count) throw new ArgumentException("Phase and frequency arrays must have the same length.");
            if (n < 2) throw new ArgumentException("Need at least two points.", nameof(unwrappedPhase));
            for (int i = 1; i < n; i++)
                if (!(frequencies[i] > frequencies[i - 1])) throw new ArgumentException("Frequencies must be strictly increasing.", nameof(frequencies));
            var tau = new double[n];
            for (int i = 0; i < n; i++)
            {
                int lo = Math.Max(0, i - 1), hi = Math.Min(n - 1, i + 1);
                tau[i] = -(unwrappedPhase[hi] - unwrappedPhase[lo]) / (2 * Math.PI * (frequencies[hi] - frequencies[lo]));
            }
            return tau;
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
