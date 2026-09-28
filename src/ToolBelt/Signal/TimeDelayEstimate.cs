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
            if (reference.Length == 0 || delayed.Length == 0)
                throw new ArgumentException("Both signals must be non-empty.");

            // Linear cross-correlation via circular convolution with padding.
            int n = reference.Length + delayed.Length - 1;
            int m = 1;
            while (m < n) m <<= 1;

            var fa = Fft.Forward(Pad(reference, m));
            var fb = Fft.Forward(Pad(delayed, m));
            var prod = new Complex[m];
            // conj(A)·B places lag k = sum_l delayed[l+k]·reference[l], so a right-shifted (later)
            // delayed copy peaks at a positive lag — the intuitive sign.
            for (int i = 0; i < m; i++) prod[i] = Complex.Conjugate(fa[i]) * fb[i];
            var corr = Fft.Inverse(prod);

            return FindPeak(corr, reference.Length - 1, delayed.Length - 1);
        }

        /// <summary>
        /// GCC-PHAT lag: cross-correlation with the magnitude whitened away, leaving only phase. Far
        /// sharper and more robust than plain correlation for reverberant or coloured signals. Requires
        /// the two signals to be the same length.
        /// </summary>
        public static int GccPhat(double[] a, double[] b, double epsilon = 1e-12)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length) throw new ArgumentException("GCC-PHAT requires equal-length signals.", nameof(b));
            if (a.Length == 0) throw new ArgumentException("Signals must be non-empty.", nameof(a));

            int len = a.Length;
            int m = 1;
            while (m < 2 * len) m <<= 1;

            var fa = Fft.Forward(Pad(a, m));
            var fb = Fft.Forward(Pad(b, m));
            var cross = new Complex[m];
            for (int i = 0; i < m; i++)
            {
                var c = Complex.Conjugate(fa[i]) * fb[i]; // positive lag => b is a later copy of a
                double mag = c.Magnitude;
                cross[i] = mag < epsilon ? Complex.Zero : c / mag;
            }
            var corr = Fft.Inverse(cross);

            // Peak search over lags [-(len-1), len-1], stored circularly.
            int bestLag = 0;
            double best = double.NegativeInfinity;
            for (int lag = -(len - 1); lag <= len - 1; lag++)
            {
                int idx = lag >= 0 ? lag : m + lag;
                double v = corr[idx].Real;
                if (v > best) { best = v; bestLag = lag; }
            }
            return bestLag;
        }

        private static double[] Pad(double[] x, int length)
        {
            var padded = new double[length];
            Array.Copy(x, padded, x.Length);
            return padded;
        }

        // The correlation array holds lag L at index L for L>=0 and lag L at index m+L for L<0.
        // Valid lags span [-maxNegative, maxPositive] = [-(reference-1), delayed-1].
        private static (int Lag, double Value) FindPeak(Complex[] corr, int maxNegative, int maxPositive)
        {
            int m = corr.Length;
            int bestLag = 0;
            double best = double.NegativeInfinity;
            for (int lag = -maxNegative; lag <= maxPositive; lag++)
            {
                int idx = lag >= 0 ? lag : m + lag;
                double v = corr[idx].Real;
                if (v > best) { best = v; bestLag = lag; }
            }
            return (bestLag, best);
        }
    }
}
