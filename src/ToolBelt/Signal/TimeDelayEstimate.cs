// ToolBelt drop-in — self-contained; depends on ToolBelt.Signal.Fft.
using System;
using System.Numerics;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Estimates the lag (in samples) that best aligns two signals — the core of arrival-time and
    /// echo-delay measurement. Plain cross-correlation for clean signals; GCC-PHAT when a sharp,
    /// noise-robust peak matters more than amplitude fidelity.
    /// </summary>
    public static class TimeDelayEstimate
    {
        /// <summary>
        /// The integer lag k (in samples) maximizing the cross-correlation of <paramref name="reference"/>
        /// and <paramref name="delayed"/>. A positive result means <paramref name="delayed"/> lags
        /// <paramref name="reference"/> by k samples.
        /// </summary>
        public static int CrossCorrelationLag(double[] reference, double[] delayed)
        {
            var (lag, _) = CrossCorrelationPeak(reference, delayed);
            return lag;
        }

        /// <summary>The lag and the correlation value at that lag.</summary>
        public static (int Lag, double Value) CrossCorrelationPeak(double[] reference, double[] delayed)
        {
            if (reference is null) throw new ArgumentNullException(nameof(reference));
            if (delayed is null) throw new ArgumentNullException(nameof(delayed));
            if (reference.Length == 0) throw new ArgumentException("Both signals must be non-empty.", nameof(reference));
            if (delayed.Length == 0) throw new ArgumentException("Both signals must be non-empty.", nameof(delayed));
            CheckFinite(reference, nameof(reference));
            CheckFinite(delayed, nameof(delayed));

            // Linear cross-correlation via circular convolution with padding.
            int m = PaddedLength((long)reference.Length + delayed.Length - 1, nameof(delayed));

            // Each input is scaled to a peak of 1 first: the products overflowed (NaN, so lag 0 with value −∞) from about
            // 1e154 and underflowed (every lag 0, so lag 0) below about 1e-165. The value is scaled back at the end.
            double sa = MaxAbs(reference), sb = MaxAbs(delayed);
            var fa = Fft.Forward(Pad(reference, m, sa));
            var fb = Fft.Forward(Pad(delayed, m, sb));
            var prod = new Complex[m];
            // conj(A)·B places lag k = sum_l delayed[l+k]·reference[l], so a right-shifted (later)
            // delayed copy peaks at a positive lag — the intuitive sign.
            for (int i = 0; i < m; i++) prod[i] = Complex.Conjugate(fa[i]) * fb[i];
            var corr = Fft.Inverse(prod);

            var (lag, value) = FindPeak(corr, reference.Length - 1, delayed.Length - 1);
            return (lag, value * sa * sb);
        }

        /// <summary>
        /// GCC-PHAT lag: cross-correlation with the magnitude whitened away, leaving only phase. Far
        /// sharper and more robust than plain correlation for reverberant or coloured signals. Requires
        /// the two signals to be the same length. Frequencies whose cross-power is below <paramref name="epsilon"/> times the
        /// strongest one are left out rather than whitened (their phase is noise), so the result does not depend on scale.
        /// </summary>
        public static int GccPhat(double[] a, double[] b, double epsilon = 1e-12)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length) throw new ArgumentException("GCC-PHAT requires equal-length signals.", nameof(b));
            if (a.Length == 0) throw new ArgumentException("Signals must be non-empty.", nameof(a));
            if (!(epsilon >= 0 && epsilon < 1)) throw new ArgumentOutOfRangeException(nameof(epsilon), epsilon, "Epsilon is a relative threshold in [0, 1).");
            CheckFinite(a, nameof(a));
            CheckFinite(b, nameof(b));

            int len = a.Length;
            int m = PaddedLength(2L * len, nameof(a));

            var fa = Fft.Forward(Pad(a, m, MaxAbs(a)));                 // scaled, as above: PHAT itself is scale-free
            var fb = Fft.Forward(Pad(b, m, MaxAbs(b)));
            var cross = new Complex[m];
            double peak = 0;
            for (int i = 0; i < m; i++)
            {
                cross[i] = Complex.Conjugate(fa[i]) * fb[i]; // positive lag => b is a later copy of a
                peak = Math.Max(peak, cross[i].Magnitude);
            }
            double threshold = epsilon * peak;
            for (int i = 0; i < m; i++)
            {
                double mag = cross[i].Magnitude;
                cross[i] = mag == 0 || mag < threshold ? Complex.Zero : cross[i] / mag;
            }
            var corr = Fft.Inverse(cross);

            // Peak search over lags [-(len-1), len-1], stored circularly.
            return FindPeak(corr, len - 1, len - 1).Lag;
        }

        private static void CheckFinite(double[] x, string name)
        {
            for (int i = 0; i < x.Length; i++)
                if (double.IsNaN(x[i]) || double.IsInfinity(x[i])) throw new ArgumentException($"{name}[{i}] is {x[i]}; samples must be finite.", name);
        }

        // The power of two at or above `length`, which must stay a usable array size.
        private static int PaddedLength(long length, string paramName)
        {
            if (length > 1 << 30) throw new ArgumentException("The signals are too long to correlate (the padded transform would exceed 2^30 points).", paramName);
            int m = 1;
            while (m < length) m <<= 1;
            return m;
        }

        private static double MaxAbs(double[] x)
        {
            double max = 0;
            foreach (double v in x) max = Math.Max(max, Math.Abs(v));
            return max == 0 ? 1 : max;
        }

        private static double[] Pad(double[] x, int length, double scale)
        {
            var padded = new double[length];
            for (int i = 0; i < x.Length; i++) padded[i] = x[i] / scale;
            return padded;
        }

        // The correlation array holds lag L at index L for L>=0 and lag L at index m+L for L<0.
        // Valid lags span [-maxNegative, maxPositive] = [-(reference-1), delayed-1]. Ties (all-zero input, say) go to the
        // lag nearest zero.
        private static (int Lag, double Value) FindPeak(Complex[] corr, int maxNegative, int maxPositive)
        {
            int m = corr.Length;
            int bestLag = 0;
            double best = double.NegativeInfinity;
            for (int lag = -maxNegative; lag <= maxPositive; lag++)
            {
                int idx = lag >= 0 ? lag : m + lag;
                double v = corr[idx].Real;
                if (v > best || (v == best && Math.Abs(lag) < Math.Abs(bestLag))) { best = v; bestLag = lag; }
            }
            return (bestLag, best);
        }
    }
}
