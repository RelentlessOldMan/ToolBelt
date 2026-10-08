// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Signal
{
    /// <summary>One cycle of a periodic capture, from one rising crossing to the next.</summary>
    public sealed class Cycle
    {
        internal Cycle(double start, double period, double highTime, double maximum, double minimum)
        {
            Start = start;
            Period = period;
            HighTime = highTime;
            Maximum = maximum;
            Minimum = minimum;
        }

        /// <summary>Time of the rising crossing that starts the cycle (seconds from the first sample).</summary>
        public double Start { get; }

        /// <summary>Seconds to the next rising crossing.</summary>
        public double Period { get; }

        public double Frequency => 1 / Period;

        /// <summary>Seconds above the level (rising to falling crossing).</summary>
        public double HighTime { get; }

        public double DutyCycle => HighTime / Period;

        public double Maximum { get; }
        public double Minimum { get; }
    }

    /// <summary>Cycle-by-cycle results and their summary statistics.</summary>
    public sealed class CycleStatistics
    {
        internal CycleStatistics(IReadOnlyList<Cycle> cycles, double level)
        {
            Cycles = cycles;
            Level = level;
            int n = cycles.Count;
            if (n == 0)
            {
                MeanPeriod = PeriodStandardDeviation = PeakToPeakJitter = CycleToCycleJitter = MeanDutyCycle = double.NaN;
                return;
            }
            double sum = 0, duty = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (var c in cycles) { sum += c.Period; duty += c.DutyCycle; min = Math.Min(min, c.Period); max = Math.Max(max, c.Period); }
            MeanPeriod = sum / n;
            MeanDutyCycle = duty / n;
            PeakToPeakJitter = max - min;
            double ss = 0;
            foreach (var c in cycles) ss += (c.Period - MeanPeriod) * (c.Period - MeanPeriod);
            PeriodStandardDeviation = n > 1 ? Math.Sqrt(ss / (n - 1)) : 0;
            double c2c = 0;
            for (int i = 1; i < n; i++) { double d = cycles[i].Period - cycles[i - 1].Period; c2c += d * d; }
            CycleToCycleJitter = n > 1 ? Math.Sqrt(c2c / (n - 1)) : 0;
        }

        /// <summary>The individual cycles — a series to feed control charts or capability analysis.</summary>
        public IReadOnlyList<Cycle> Cycles { get; }

        /// <summary>The crossing level used.</summary>
        public double Level { get; }

        public double MeanPeriod { get; }
        public double MeanFrequency => 1 / MeanPeriod;

        /// <summary>Standard deviation of the periods (RMS period jitter).</summary>
        public double PeriodStandardDeviation { get; }

        public double PeakToPeakJitter { get; }

        /// <summary>RMS of the differences between consecutive periods.</summary>
        public double CycleToCycleJitter { get; }

        public double MeanDutyCycle { get; }
    }

    /// <summary>
    /// Splits a periodic capture into cycles and measures each one — period, frequency, high time, duty cycle, extremes —
    /// so run-to-run variation (jitter, drift) can be analysed as a series rather than summarised away. Cycles are bounded
    /// by rising crossings of a level (by default the midpoint between the capture's extremes), located with linear
    /// interpolation between samples, and a hysteresis band rejects the extra crossings noise would otherwise add on a
    /// slow edge: an edge counts only once the signal has been beyond the band on one side and then clears it on the other,
    /// so a runt pulse that crosses the level without clearing the band is not an edge. Partial cycles at either end are
    /// dropped. Samples must be finite.
    /// </summary>
    public static class CycleMeasurements
    {
        /// <param name="samples">The capture.</param>
        /// <param name="sampleRate">Samples per second.</param>
        /// <param name="level">Crossing level (default: midpoint of the minimum and maximum).</param>
        /// <param name="hysteresis">Half-width of the band around <paramref name="level"/>, as a fraction of the capture's range (default 10%).</param>
        public static CycleStatistics Measure(IReadOnlyList<double> samples, double sampleRate, double? level = null, double hysteresis = 0.1)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            if (level is double given && (double.IsNaN(given) || double.IsInfinity(given))) throw new ArgumentOutOfRangeException(nameof(level), given, "Level must be finite.");
            if (!(hysteresis >= 0 && hysteresis < 0.5)) throw new ArgumentOutOfRangeException(nameof(hysteresis), hysteresis, "Hysteresis must be in [0, 0.5).");
            if (samples.Count < 3) return new CycleStatistics(Array.Empty<Cycle>(), level ?? double.NaN);

            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            for (int i = 0; i < samples.Count; i++)
            {
                double x = samples[i];
                if (double.IsNaN(x) || double.IsInfinity(x)) throw new ArgumentException($"samples[{i}] is {x}; samples must be finite.", nameof(samples));
                min = Math.Min(min, x);
                max = Math.Max(max, x);
            }
            double lvl = level ?? (min + max) / 2;
            double band = hysteresis * (max - min);

            // Crossing events at fractional sample positions. An edge is armed by the signal going beyond the band on the
            // far side, takes its time from the first level crossing after that, and is committed only when the signal
            // clears the band on the near side; dropping back beyond the far side first discards it (a runt).
            var rising = new List<double>();
            var falling = new List<double>();
            bool armedRise = samples[0] < lvl - band, armedFall = samples[0] > lvl + band;
            double pendingRise = double.NaN, pendingFall = double.NaN;
            for (int i = 1; i < samples.Count; i++)
            {
                double x = samples[i], prev = samples[i - 1];
                if (armedRise && double.IsNaN(pendingRise) && prev < lvl && x >= lvl) pendingRise = Cross(samples, i, lvl);
                if (armedFall && double.IsNaN(pendingFall) && prev > lvl && x <= lvl) pendingFall = Cross(samples, i, lvl);
                if (!double.IsNaN(pendingRise) && x > lvl + band)
                {
                    rising.Add(pendingRise);
                    pendingRise = double.NaN;
                    armedRise = false;
                }
                if (!double.IsNaN(pendingFall) && x < lvl - band)
                {
                    falling.Add(pendingFall);
                    pendingFall = double.NaN;
                    armedFall = false;
                }
                if (x < lvl - band) { armedRise = true; pendingRise = double.NaN; }
                if (x > lvl + band) { armedFall = true; pendingFall = double.NaN; }
            }

            var cycles = new List<Cycle>();
            int f = 0;
            for (int r = 0; r + 1 < rising.Count; r++)
            {
                double start = rising[r], end = rising[r + 1];
                while (f < falling.Count && falling[f] <= start) f++;
                double high = f < falling.Count && falling[f] < end ? falling[f] - start : double.NaN;
                double cmax = double.NegativeInfinity, cmin = double.PositiveInfinity;
                for (int i = (int)Math.Ceiling(start); i <= (int)Math.Floor(end) && i < samples.Count; i++)
                {
                    cmax = Math.Max(cmax, samples[i]);
                    cmin = Math.Min(cmin, samples[i]);
                }
                cycles.Add(new Cycle(start / sampleRate, (end - start) / sampleRate, high / sampleRate, cmax, cmin));
            }
            return new CycleStatistics(cycles, lvl);
        }

        // Fractional index where the segment (i−1, i) crosses `level`.
        private static double Cross(IReadOnlyList<double> s, int i, double level)
        {
            double a = s[i - 1], b = s[i];
            return b == a ? i : i - 1 + (level - a) / (b - a);
        }
    }
}
