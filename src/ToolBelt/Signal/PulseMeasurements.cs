// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Bench measurements on a pulse or step waveform: rise/fall time, pulse width and duty cycle. State levels are the
    /// histogram modes of the lower and upper halves of the signal's range (IEEE 181, and a scope's automatic base/top),
    /// so overshoot and ringing don't move them; reference crossings are found with sub-sample linear interpolation.
    /// Samples must be finite.
    /// </summary>
    public static class PulseMeasurements
    {
        private const int Bins = 100;

        /// <summary>
        /// The low and high state levels: the mean of the samples in the most populated of 100 histogram bins in the lower and
        /// upper half of the range (ties go to the outermost bin, so a signal without flat states gets roughly its min and max).
        /// </summary>
        public static (double Low, double High) StateLevels(double[] samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (samples.Length == 0) throw new ArgumentException("Signal must be non-empty.", nameof(samples));
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (int i = 0; i < samples.Length; i++)
            {
                double v = samples[i];
                // A NaN first sample used to make the levels NaN, while a NaN anywhere else was ignored.
                if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException($"samples[{i}] is {v}; samples must be finite.", nameof(samples));
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            if (lo == hi) return (lo, hi);

            // Min/max put the levels on the overshoot peaks, so a ringing step's settled top never reached the 90% reference.
            double halfRange = hi / 2 - lo / 2;
            if (!(halfRange > 0)) return (lo, hi);   // a denormal range halves to 0: the bin index was NaN and indexed out of range
            var counts = new int[Bins];
            var means = new double[Bins];
            foreach (double v in samples)
            {
                int b = (int)Math.Min(Bins - 1, Math.Floor((v / 2 - lo / 2) / halfRange * Bins));
                counts[b]++;
                means[b] += (v - means[b]) / counts[b];
            }
            int low = 0, high = Bins - 1;
            for (int b = 1; b < Bins / 2; b++) if (counts[b] > counts[low]) low = b;
            for (int b = Bins - 2; b >= Bins / 2; b--) if (counts[b] > counts[high]) high = b;
            return (counts[low] > 0 ? means[low] : lo, counts[high] > 0 ? means[high] : hi);
        }

        /// <summary>
        /// Rise time from <paramref name="lowPct"/> to <paramref name="highPct"/> of the amplitude (10%–90% by default), in
        /// seconds: from the last low-reference crossing before the first high-reference crossing (both on the same edge).
        /// </summary>
        public static double RiseTime(double[] samples, double sampleRate, double lowPct = 0.1, double highPct = 0.9)
        {
            ValidateRate(sampleRate);
            ValidateReferences(lowPct, highPct);
            var (lo, hi) = StateLevels(samples);
            double lowRef = Lerp(lo, hi, lowPct), highRef = Lerp(lo, hi, highPct);
            double t2 = FindCrossing(samples, highRef, rising: true, reach: true);
            if (t2 < 0) throw new InvalidOperationException("No rising edge crosses the high reference.");
            // The first low-reference crossing in the record paired a glitch long before the edge with the edge's top.
            double t1 = FindLastCrossing(samples, lowRef, (int)Math.Floor(t2), rising: true);
            if (t1 < 0) throw new InvalidOperationException("No rising edge crosses the low reference.");
            return (t2 - t1) / sampleRate;
        }

        /// <summary>
        /// Fall time from <paramref name="highPct"/> down to <paramref name="lowPct"/>, in seconds: from the last high-reference
        /// crossing before the first low-reference crossing.
        /// </summary>
        public static double FallTime(double[] samples, double sampleRate, double lowPct = 0.1, double highPct = 0.9)
        {
            ValidateRate(sampleRate);
            ValidateReferences(lowPct, highPct);
            var (lo, hi) = StateLevels(samples);
            double lowRef = Lerp(lo, hi, lowPct), highRef = Lerp(lo, hi, highPct);
            double t2 = FindCrossing(samples, lowRef, rising: false, reach: true);
            if (t2 < 0) throw new InvalidOperationException("No falling edge crosses the low reference.");
            double t1 = FindLastCrossing(samples, highRef, (int)Math.Floor(t2), rising: false);
            if (t1 < 0) throw new InvalidOperationException("No falling edge crosses the high reference.");
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
            double mid = lo / 2 + hi / 2;                                       // (lo + hi) / 2 overflowed
            double t1 = FindCrossing(samples, mid, rising: true, reach: false);
            if (t1 < 0) throw new InvalidOperationException("No rising mid-level crossing.");
            double t2 = FindCrossing(samples, mid, rising: false, reach: false, (int)Math.Floor(t1) + 1);
            if (t2 < 0) throw new InvalidOperationException("No falling mid-level crossing after the pulse.");
            return (t2 - t1) / sampleRate;
        }

        /// <summary>The fraction of the record spent above the mid state level, in [0, 1].</summary>
        public static double DutyCycle(double[] samples)
        {
            var (lo, hi) = StateLevels(samples);
            double mid = lo / 2 + hi / 2;
            int above = 0;
            foreach (var v in samples) if (v > mid) above++;
            return (double)above / samples.Length;
        }

        private static double Lerp(double lo, double hi, double p) => p == 1 ? hi : (1 - p) * lo + p * hi;

        // Fractional sample index of the first crossing of `level` at/after `start`, or -1. `rising` selects an upward or
        // downward crossing; `reach` counts arriving exactly at the level (a 100% reference is reached by a flat top).
        private static double FindCrossing(double[] s, double level, bool rising, bool reach, int start = 0)
        {
            for (int i = Math.Max(0, start); i < s.Length - 1; i++)
                if (Crosses(s[i], s[i + 1], level, rising, reach)) return i + Fraction(s[i], s[i + 1], level);
            return -1;
        }

        // The last crossing (leaving the level) in a segment starting at or before `end`, or -1.
        private static double FindLastCrossing(double[] s, double level, int end, bool rising)
        {
            for (int i = Math.Min(end, s.Length - 2); i >= 0; i--)
                if (Crosses(s[i], s[i + 1], level, rising, reach: false)) return i + Fraction(s[i], s[i + 1], level);
            return -1;
        }

        private static bool Crosses(double a, double b, double level, bool rising, bool reach)
            => rising ? (reach ? a < level && b >= level : a <= level && b > level)
                      : (reach ? a > level && b <= level : a >= level && b < level);

        // Halved so that neither difference overflows for samples near ±double.MaxValue.
        private static double Fraction(double a, double b, double level)
        {
            double denom = b / 2 - a / 2;
            return denom == 0 ? 0 : Math.Min(1, Math.Max(0, (level / 2 - a / 2) / denom));
        }

        private static void ValidateRate(double sampleRate)
        {
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
        }

        // The reference levels are fractions of the amplitude (0.1, not 10), low below high.
        private static void ValidateReferences(double lowPct, double highPct)
        {
            if (!(lowPct >= 0 && lowPct <= 1)) throw new ArgumentOutOfRangeException(nameof(lowPct), lowPct, "Reference levels are fractions of the amplitude, in [0, 1].");
            if (!(highPct >= 0 && highPct <= 1)) throw new ArgumentOutOfRangeException(nameof(highPct), highPct, "Reference levels are fractions of the amplitude, in [0, 1].");
            if (!(lowPct < highPct)) throw new ArgumentException("The low reference must be below the high reference.", nameof(lowPct));
        }
    }
}
