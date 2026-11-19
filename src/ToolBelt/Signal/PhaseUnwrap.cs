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
        /// Unwraps <paramref name="phase"/> (radians): wherever a successive difference is at least <paramref name="tolerance"/>
        /// in magnitude, whole turns of 2π are added or removed to bring it into (−π, π], as NumPy's <c>unwrap</c> (with
        /// <c>discont</c>) and MATLAB's <c>unwrap</c> (with <c>tol</c>) do. A tolerance below π therefore acts like π (a
        /// jump already within π is left alone); one above π leaves jumps below it alone. Phases must be finite.
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
                // Count the turns directly: a loop of `delta -= 2π` never ends when delta is huge. A jump at the tolerance is
                // corrected, and into (−π, π]: correcting only past it, and only to within it, left [0, 10] at tolerance 4
                // as [0, 3.72] where NumPy gives [0, −2.57].
                if (Math.Abs(delta) >= tolerance)
                {
                    double turns = Math.Floor((delta + Math.PI) / Turn);
                    // delta + π is rounded, so a jump a few ulps below 5π counted 3 turns and landed below −π: the remainder
                    // itself decides (+π stays +π, as in NumPy), computed with the product's rounding error so it is rounded
                    // once (delta − turns·2π is exact by Sterbenz's lemma).
                    double product = turns * Turn;
                    double rest = delta - product - ProductError(turns, Turn, product);
                    if (rest < -Math.PI || (rest == -Math.PI && delta > 0)) turns--;
                    else if (rest > Math.PI) turns++;
                    correction -= turns * Turn;
                }
                result[i] = phase[i] + correction;
                // One jump of ±3.4e308 made the correction infinite and every later sample −∞.
                if (double.IsInfinity(result[i]) || double.IsNaN(result[i]))
                    throw new ArgumentException($"The jump at phase[{i}] is too large to unwrap.", nameof(phase));
            }
            return result;
        }

        // a·b − p exactly, for p = a·b rounded (Dekker's split; 0 where the split would overflow).
        private static double ProductError(double a, double b, double p)
        {
            if (Math.Abs(a) > 1e290 || Math.Abs(b) > 1e290) return 0;
            const double Split = 134217729;                                     // 2^27 + 1
            double ah = a * Split; ah -= ah - a; double al = a - ah;
            double bh = b * Split; bh -= bh - b; double bl = b - bh;
            return ((ah * bh - p) + ah * bl + al * bh) + al * bl;
        }

        /// <summary>
        /// Group delay τ(f) = −dφ/dω, in seconds, from an <b>unwrapped</b> phase response (radians) sampled at
        /// <paramref name="frequencies"/> (Hz, strictly increasing; spacing may vary): central differences inside (on an
        /// uneven grid the second-order one, NumPy's <c>gradient</c>), one-sided at the ends. A pure delay of T seconds (φ = −2πfT) gives exactly T everywhere; dispersion shows up as variation.
        /// Phases and frequencies must be finite.
        /// </summary>
        public static double[] GroupDelay(System.Collections.Generic.IReadOnlyList<double> unwrappedPhase, System.Collections.Generic.IReadOnlyList<double> frequencies)
        {
            if (unwrappedPhase is null) throw new ArgumentNullException(nameof(unwrappedPhase));
            if (frequencies is null) throw new ArgumentNullException(nameof(frequencies));
            int n = unwrappedPhase.Count;
            if (n != frequencies.Count) throw new ArgumentException("Phase and frequency arrays must have the same length.", nameof(frequencies));
            if (n < 2) throw new ArgumentException("Need at least two points.", nameof(unwrappedPhase));
            // Past Array.MaxLength the result threw OutOfMemoryException ("array dimensions exceeded"), after the whole check.
            if (n > 0x7FFFFFC7) throw new ArgumentException($"The phase has {n} points, more than an array holds.", nameof(unwrappedPhase));
            // An infinite frequency passed the order check and made Δf infinite: the delay at it and at its finite
            // neighbour came out 0. A NaN phase only spread NaN.
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(frequencies[i]) || double.IsInfinity(frequencies[i]))
                    throw new ArgumentException($"Frequencies[{i}] is {frequencies[i]}; frequencies must be finite.", nameof(frequencies));
                if (double.IsNaN(unwrappedPhase[i]) || double.IsInfinity(unwrappedPhase[i]))
                    throw new ArgumentException($"UnwrappedPhase[{i}] is {unwrappedPhase[i]}; phases must be finite.", nameof(unwrappedPhase));
            }
            for (int i = 1; i < n; i++)
                if (!(frequencies[i] > frequencies[i - 1])) throw new ArgumentException("Frequencies must be strictly increasing.", nameof(frequencies));
            var tau = new double[n];
            for (int i = 0; i < n; i++)
            {
                int lo = Math.Max(0, i - 1), hi = Math.Min(n - 1, i + 1);
                // On an uneven grid the secant over [f(i−1), f(i+1)] is the slope at their midpoint, not at f(i): φ = −πf²
                // (τ = f) at f = [0, 1, 10] gave 5 at f = 1. The two one-sided slopes are weighted by the far spacing,
                // which is exact for a quadratic phase.
                if (lo < i && i < hi && frequencies[i] - frequencies[lo] != frequencies[hi] - frequencies[i])
                {
                    tau[i] = Uneven(unwrappedPhase, frequencies, i);
                    continue;
                }
                tau[i] = Delay(unwrappedPhase, frequencies, lo, hi);
            }
            return tau;
        }

        // Weighted mean of the one-sided delays either side of i, each weighted by the other side's spacing.
        private static double Uneven(System.Collections.Generic.IReadOnlyList<double> phase, System.Collections.Generic.IReadOnlyList<double> f, int i)
        {
            double left = Delay(phase, f, i - 1, i), right = Delay(phase, f, i, i + 1);
            if (left == right) return left;                                     // a pure delay stays exact
            double a = f[i] - f[i - 1], b = f[i + 1] - f[i];
            if (double.IsInfinity(a) || double.IsInfinity(b) || double.IsInfinity(a + b))
            {
                a = f[i] / 2 - f[i - 1] / 2;
                b = f[i + 1] / 2 - f[i] / 2;
            }
            else if (a + b < 2.2250738585072014e-308)
            {
                a *= Up;                                                        // exact: a subnormal spacing loses no bits
                b *= Up;
            }
            double sum = a + b;
            return left * (b / sum) + right * (a / sum);
        }

        // −Δφ / (2π·Δf) between two points.
        private static double Delay(System.Collections.Generic.IReadOnlyList<double> unwrappedPhase, System.Collections.Generic.IReadOnlyList<double> frequencies, int lo, int hi)
        {
            {
                double dp = unwrappedPhase[hi] - unwrappedPhase[lo], df = frequencies[hi] - frequencies[lo];
                // Frequencies spanning ±1e308 overflowed Δf or 2π·Δf and gave a delay of 0: the differences are halved
                // where one overflowed, and 2π divided out last where 2π·Δf would.
                if (double.IsInfinity(dp) || double.IsInfinity(df))
                {
                    dp = unwrappedPhase[hi] / 2 - unwrappedPhase[lo] / 2;
                    df = frequencies[hi] / 2 - frequencies[lo] / 2;
                }
                double omega = 2 * Math.PI * df;
                // A subnormal Δf lost bits in 2π·Δf (Δf = 3ε gave a delay 6% off): it is scaled up by 2^600 (exact) first.
                if (omega < 2.2250738585072014e-308) return -(dp / (2 * Math.PI * (df * Up))) * Up;
                return double.IsInfinity(omega) ? -(dp / df) / (2 * Math.PI) : -dp / omega;
            }
        }

        private static readonly double Up = Math.Pow(2, 600);

        /// <summary>Wraps a single angle into the principal range (−π, π]. An infinite or NaN angle has no direction and gives
        /// NaN, as <see cref="Math.Sin"/> does (<see cref="Unwrap"/> and <see cref="GroupDelay"/> refuse them).</summary>
        public static double Wrap(double angle)
        {
            double wrapped = angle % (2 * Math.PI);
            if (wrapped <= -Math.PI) wrapped += 2 * Math.PI;
            else if (wrapped > Math.PI) wrapped -= 2 * Math.PI;
            return wrapped;
        }
    }
}
