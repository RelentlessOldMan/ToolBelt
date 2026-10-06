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
        /// Unwraps <paramref name="phase"/> (radians): wherever a successive difference exceeds <paramref name="tolerance"/>
        /// in magnitude, whole turns of 2π are added or removed (the fewest that bring it within the tolerance). With the
        /// default tolerance π every resulting jump is at most π; a tolerance below π can't always be met by whole turns,
        /// so jumps up to 2π − tolerance remain. Phases must be finite.
        /// </summary>
        public static double[] Unwrap(double[] phase, double tolerance = Math.PI)
        {
            if (phase is null) throw new ArgumentNullException(nameof(phase));
            if (!(tolerance > 0) || double.IsInfinity(tolerance)) throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Tolerance must be positive and finite.");
            if (phase.Length == 0) return Array.Empty<double>();

            const double Turn = 2 * Math.PI;
            var result = new double[phase.Length];
            result[0] = phase[0];
            double correction = 0;
            for (int i = 0; i < phase.Length; i++)
            {
                if (double.IsNaN(phase[i]) || double.IsInfinity(phase[i]))
                    throw new ArgumentException($"Phase[{i}] is {phase[i]}; phases must be finite.", nameof(phase));
                if (i == 0) continue;
                double delta = phase[i] - phase[i - 1];
                // Count the turns directly: a loop of `delta -= 2π` never ends when delta is huge.
                if (delta > tolerance) correction -= Math.Ceiling((delta - tolerance) / Turn) * Turn;
                else if (delta < -tolerance) correction += Math.Ceiling((-delta - tolerance) / Turn) * Turn;
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
