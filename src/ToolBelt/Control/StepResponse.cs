// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ToolBelt.Control
{
    /// <summary>
    /// Transient metrics of a recorded step response (MATLAB <c>stepinfo</c>-style). Times are measured from the step
    /// instant (the first sample's time). Metrics the response never reaches are NaN: <see cref="RiseTime"/> when it
    /// doesn't pass the upper rise level, <see cref="SettlingTime"/> when the last sample is still outside the band.
    /// </summary>
    public sealed class StepResponseInfo
    {
        internal StepResponseInfo(double initial, double final, double rise, double settling, double peak, double peakTime, double overshoot, double undershoot)
        {
            InitialValue = initial;
            FinalValue = final;
            RiseTime = rise;
            SettlingTime = settling;
            PeakValue = peak;
            PeakTime = peakTime;
            OvershootPercent = overshoot;
            UndershootPercent = undershoot;
        }

        public double InitialValue { get; }
        public double FinalValue { get; }

        /// <summary>Time to go from the lower to the upper rise level (10% → 90% of the step by default).</summary>
        public double RiseTime { get; }

        /// <summary>Time after which the response stays within the settling band (±2% of the step by default).</summary>
        public double SettlingTime { get; }

        /// <summary>The value furthest along the step direction (the maximum for a rising step).</summary>
        public double PeakValue { get; }

        public double PeakTime { get; }

        /// <summary>How far the peak passes the final value, as a percentage of the step (0 when it never does).</summary>
        public double OvershootPercent { get; }

        /// <summary>How far the response first moves the wrong way, as a percentage of the step (non-minimum-phase plants).</summary>
        public double UndershootPercent { get; }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "rise {0:G4}, settling {1:G4}, overshoot {2:F2}%, peak {3:G6} at {4:G4}", RiseTime, SettlingTime, OvershootPercent, PeakValue, PeakTime);
    }

    /// <summary>
    /// Measures rise time, settling time, overshoot and peak from a sampled step response — the numbers a controller
    /// tuning is judged by. Crossing times are linearly interpolated between samples, so the results are not quantised
    /// to the sample interval. Works for falling steps too (everything is measured in the step's direction).
    /// </summary>
    public static class StepResponse
    {
        /// <summary>Analyses a response sampled every <paramref name="sampleInterval"/> seconds, the step applied at the first sample.</summary>
        public static StepResponseInfo Analyze(IReadOnlyList<double> values, double sampleInterval, double? finalValue = null, double? initialValue = null,
            double settlingBand = 0.02, double riseLow = 0.1, double riseHigh = 0.9)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            if (!(sampleInterval > 0) || double.IsInfinity(sampleInterval)) throw new ArgumentOutOfRangeException(nameof(sampleInterval), sampleInterval, "Sample interval must be positive and finite.");
            var times = new double[values.Count];
            for (int i = 0; i < times.Length; i++) times[i] = i * sampleInterval;
            return Analyze(times, values, finalValue, initialValue, settlingBand, riseLow, riseHigh);
        }

        /// <summary>
        /// Analyses a response at increasing <paramref name="times"/>; the step is applied at <c>times[0]</c>. The final value
        /// defaults to the last sample (pass the setpoint, or a better steady-state estimate, if the record is noisy or short);
        /// the initial value defaults to the first sample.
        /// </summary>
        public static StepResponseInfo Analyze(IReadOnlyList<double> times, IReadOnlyList<double> values, double? finalValue = null, double? initialValue = null,
            double settlingBand = 0.02, double riseLow = 0.1, double riseHigh = 0.9)
        {
            if (times is null) throw new ArgumentNullException(nameof(times));
            if (values is null) throw new ArgumentNullException(nameof(values));
            int n = values.Count;
            if (times.Count != n) throw new ArgumentException("Times and values must have the same length.", nameof(times));
            if (n < 2) throw new ArgumentException("At least two samples are required.", nameof(values));
            if (!(settlingBand > 0 && settlingBand < 1)) throw new ArgumentOutOfRangeException(nameof(settlingBand), settlingBand, "The settling band must be in (0, 1).");
            if (!(riseLow >= 0 && riseLow < riseHigh && riseHigh <= 1)) throw new ArgumentOutOfRangeException(nameof(riseLow), riseLow, "Rise levels must satisfy 0 ≤ low < high ≤ 1.");
            for (int i = 0; i < n; i++)
            {
                if (!IsFinite(values[i]) || !IsFinite(times[i])) throw new ArgumentException($"Sample {i} is not finite.", nameof(values));
                if (i > 0 && !(times[i] > times[i - 1])) throw new ArgumentException("Times must be strictly increasing.", nameof(times));
            }

            double y0 = initialValue ?? values[0], yf = finalValue ?? values[n - 1];
            if (!IsFinite(y0) || !IsFinite(yf)) throw new ArgumentException("Initial and final values must be finite.");
            double step = yf - y0;
            if (step == 0) throw new ArgumentException("The initial and final values are equal: there is no step to analyse.");
            double t0 = times[0];
            double U(int i) => (values[i] - y0) / step;                // 0 at the start, 1 at the end, in the step's direction

            // Rise: first upward crossing of the low level, then of the high level after it.
            double tLow = FirstCrossing(times, U, riseLow, 0), rise = double.NaN;
            if (!double.IsNaN(tLow))
            {
                double tHigh = FirstCrossing(times, U, riseHigh, IndexAtOrAfter(times, tLow));
                if (!double.IsNaN(tHigh)) rise = tHigh - tLow;
            }

            int peakIndex = 0;
            double minU = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (U(i) > U(peakIndex)) peakIndex = i;
                minU = Math.Min(minU, U(i));
            }
            double overshoot = Math.Max(0, U(peakIndex) - 1) * 100;
            double undershoot = Math.Max(0, -minU) * 100;

            // Settling: after the last sample outside the band, interpolate where the error re-enters it.
            double settling;
            int last = -1;
            for (int i = n - 1; i >= 0; i--)
                if (Math.Abs(U(i) - 1) > settlingBand) { last = i; break; }
            if (last == -1) settling = 0;
            else if (last == n - 1) settling = double.NaN;
            else
            {
                double e0 = U(last) - 1, e1 = U(last + 1) - 1;
                double edge = Math.Sign(e0) * settlingBand;
                double frac = (e0 - edge) / (e0 - e1);
                settling = times[last] + frac * (times[last + 1] - times[last]) - t0;
            }

            return new StepResponseInfo(y0, yf, rise, settling, values[peakIndex], times[peakIndex] - t0, overshoot, undershoot);

            double FirstCrossing(IReadOnlyList<double> t, Func<int, double> u, double level, int from)
            {
                if (from >= n) return double.NaN;
                if (u(from) >= level) return t[from];
                for (int i = from + 1; i < n; i++)
                    if (u(i) >= level)
                    {
                        double a = u(i - 1), b = u(i);
                        return t[i - 1] + (level - a) / (b - a) * (t[i] - t[i - 1]);
                    }
                return double.NaN;
            }
        }

        private static int IndexAtOrAfter(IReadOnlyList<double> times, double t)
        {
            int i = 0;
            while (i < times.Count - 1 && times[i + 1] <= t) i++;
            return i;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
