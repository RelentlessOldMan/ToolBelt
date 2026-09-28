// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Bench measurements on a pulse or step waveform: rise/fall time, pulse width and duty cycle. State
    /// levels are taken as the signal's min and max; reference crossings are found with sub-sample linear
    /// interpolation, matching how a scope's automatic measurements behave.
    /// </summary>
    public static class PulseMeasurements
    {
        /// <summary>The low and high state levels — here, the minimum and maximum of the signal.</summary>
        public static (double Low, double High) StateLevels(double[] samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (samples.Length == 0) throw new ArgumentException("Signal must be non-empty.", nameof(samples));
            double lo = samples[0], hi = samples[0];
            foreach (var v in samples) { if (v < lo) lo = v; if (v > hi) hi = v; }
            return (lo, hi);
        }

        /// <summary>
        /// Rise time from <paramref name="lowPct"/> to <paramref name="highPct"/> of the amplitude
        /// (10%–90% by default), in seconds. Uses the first rising edge in the record.
        /// </summary>
        public static double RiseTime(double[] samples, double sampleRate, double lowPct = 0.1, double highPct = 0.9)
        {
            ValidateRate(sampleRate);
            var (lo, hi) = StateLevels(samples);
            double amp = hi - lo;
            double lowRef = lo + lowPct * amp, highRef = lo + highPct * amp;
            double t1 = FindCrossing(samples, lowRef, 0, rising: true);
            if (t1 < 0) throw new InvalidOperationException("No rising edge crosses the low reference.");
            double t2 = FindCrossing(samples, highRef, (int)Math.Floor(t1), rising: true);
            if (t2 < 0) throw new InvalidOperationException("No rising edge crosses the high reference.");
            return (t2 - t1) / sampleRate;
        }

        /// <summary>Fall time from <paramref name="highPct"/> down to <paramref name="lowPct"/>, in seconds. Uses the first falling edge.</summary>
        public static double FallTime(double[] samples, double sampleRate, double lowPct = 0.1, double highPct = 0.9)
        {
            ValidateRate(sampleRate);
            var (lo, hi) = StateLevels(samples);
            double amp = hi - lo;
            double lowRef = lo + lowPct * amp, highRef = lo + highPct * amp;
            double t1 = FindCrossing(samples, highRef, 0, rising: false);
            if (t1 < 0) throw new InvalidOperationException("No falling edge crosses the high reference.");
            double t2 = FindCrossing(samples, lowRef, (int)Math.Floor(t1), rising: false);
            if (t2 < 0) throw new InvalidOperationException("No falling edge crosses the low reference.");
            return (t2 - t1) / sampleRate;
        }

        /// <summary>
        /// Positive pulse width in seconds: the time from the first 50%-rising crossing to the next
        /// 50%-falling crossing.
        /// </summary>
        public static double PulseWidth(double[] samples, double sampleRate)
        {
            ValidateRate(sampleRate);
            var (lo, hi) = StateLevels(samples);
            double mid = 0.5 * (lo + hi);
            double t1 = FindCrossing(samples, mid, 0, rising: true);
            if (t1 < 0) throw new InvalidOperationException("No rising mid-level crossing.");
            double t2 = FindCrossing(samples, mid, (int)Math.Floor(t1) + 1, rising: false);
            if (t2 < 0) throw new InvalidOperationException("No falling mid-level crossing after the pulse.");
            return (t2 - t1) / sampleRate;
        }

        /// <summary>The fraction of the record spent above the mid state level, in [0, 1].</summary>
        public static double DutyCycle(double[] samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (samples.Length == 0) throw new ArgumentException("Signal must be non-empty.", nameof(samples));
            var (lo, hi) = StateLevels(samples);
            double mid = 0.5 * (lo + hi);
            int above = 0;
            foreach (var v in samples) if (v > mid) above++;
            return (double)above / samples.Length;
        }

        // Fractional sample index of the first crossing of `level` at/after `start`, or -1. `rising`
        // selects an upward (below->above) or downward (above->below) crossing.
        private static double FindCrossing(double[] s, double level, int start, bool rising)
        {
            for (int i = Math.Max(0, start); i < s.Length - 1; i++)
            {
                bool cross = rising ? (s[i] <= level && s[i + 1] > level)
                                    : (s[i] >= level && s[i + 1] < level);
                if (cross)
                {
                    double denom = s[i + 1] - s[i];
                    double frac = denom == 0 ? 0 : (level - s[i]) / denom;
                    return i + frac;
                }
            }
            return -1;
        }

        private static void ValidateRate(double sampleRate)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        }
    }
}
