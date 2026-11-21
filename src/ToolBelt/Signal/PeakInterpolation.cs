// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Signal
{
    /// <summary>How <see cref="PeakInterpolation.SpectralPeak"/> refines a peak between bins.</summary>
    public enum PeakMethod
    {
        /// <summary>A parabola through the three linear magnitudes around the peak.</summary>
        Parabolic,

        /// <summary>
        /// A parabola through the log magnitudes (equivalently, "parabolic on dB") — exact for a Gaussian-shaped peak and
        /// markedly better than <see cref="Parabolic"/> with Hann or Gaussian windows. Needs positive magnitudes.
        /// </summary>
        Gaussian,
    }

    /// <summary>
    /// Sub-sample peak location: fits a parabola (or a Gaussian, via a parabola on logarithms) through a sample and its two
    /// neighbours and returns the fractional offset of the true maximum and its height. Applied to a magnitude spectrum it
    /// turns a frequency readout quantised to the bin spacing into one accurate to a small fraction of a bin — a cheap fix
    /// that improves every spectral frequency/amplitude reading.
    /// </summary>
    public static class PeakInterpolation
    {
        /// <summary>
        /// The vertex of the parabola through (−1, <paramref name="left"/>), (0, <paramref name="center"/>), (+1, <paramref name="right"/>):
        /// offset in [−½, ½] relative to the centre sample when the centre is the local maximum, and the peak height.
        /// </summary>
        public static (double Offset, double Peak) Parabolic(double left, double center, double right)
        {
            // Values near ±double.MaxValue overflowed 2·centre or left − right (the offset came out −0 or NaN): the same in
            // quarters, which is exact there.
            if (IsFinite(left) && IsFinite(center) && IsFinite(right)
                && (double.IsInfinity(2 * center) || double.IsInfinity(left - right)
                    || double.IsInfinity(left - center) || double.IsInfinity(right - center)
                    || double.IsInfinity((left - center) + (right - center))))   // (−0.9e308, 0.8e308, −0.85e308): offset 0
            {
                var (o, p) = Vertex(left / 4, center / 4, right / 4);
                return (o, p * 4);
            }
            // Subnormal values: 0.5·(left − right) lost its bit and the differences rounded, so (ε, 2ε, 0) had offset −0 for
            // −1/6. Scaled up by 2^600 (exact) the vertex is found as for normal values, and its height scaled back.
            double peak = Math.Max(Math.Abs(left), Math.Max(Math.Abs(center), Math.Abs(right)));
            if (peak > 0 && peak < Small)
            {
                var (o, p) = Vertex(left * Up, center * Up, right * Up);
                return (o, p * Down);
            }
            return Vertex(left, center, right);
        }

        private static readonly double Up = Math.Pow(2, 600), Down = Math.Pow(2, -600), Small = Math.Pow(2, -400);

        private static (double Offset, double Peak) Vertex(double left, double center, double right)
        {
            // (left − c) + (right − c): left − 2c + right cancelled on a near-flat peak, 55,921 ulps off the exact vertex at
            // (6.2434219313738834e144, 6.243479082300001e144, 6.243452974771369e144). Close values subtract exactly.
            double denom = (left - center) + (right - center);
            if (denom == 0) return (0, center);
            double delta = 0.5 * (left - right) / denom;
            return (delta, center - 0.25 * (left - right) * delta);
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>The peak of the Gaussian through the three (positive) values: a parabola fitted to their logarithms.</summary>
        public static (double Offset, double Peak) Gaussian(double left, double center, double right)
        {
            if (!(left > 0)) throw new ArgumentOutOfRangeException(nameof(left), left, "Gaussian interpolation needs positive values.");
            if (!(center > 0)) throw new ArgumentOutOfRangeException(nameof(center), center, "Gaussian interpolation needs positive values.");
            if (!(right > 0)) throw new ArgumentOutOfRangeException(nameof(right), right, "Gaussian interpolation needs positive values.");
            // Fitted to the logs relative to the centre: logs of size 700 cancelled, so the offset depended on the scale
            // (-0.000352 for values near 1.906, -0.002262 for the same times 2^1000), and the height, exp(ln c), lost up to 264
            // ulps (a flat 1e300 came back 9.999999999999763e299, double.Epsilon came back 0).
            var (delta, logGain) = Parabolic(LogRatio(left, center), 0, LogRatio(right, center));
            double peak = center * Math.Exp(logGain);
            if (double.IsInfinity(peak)) peak = Math.Exp(Math.Log(center) + logGain);
            return (delta, peak);
        }

        // ln(v / c), from the logs only when the ratio leaves the normal range.
        private static double LogRatio(double v, double c)
        {
            double q = v / c;
            return q >= 2.2250738585072014E-308 && !double.IsInfinity(q) ? Math.Log(q) : Math.Log(v) - Math.Log(c);
        }

        /// <summary>
        /// Refines the peak of a magnitude spectrum (bins 0 … fftLength/2): at <paramref name="bin"/>, or at the largest bin if
        /// none is given (NaN bins are skipped). Returns the interpolated frequency, magnitude and fractional bin. A peak in the
        /// first or last bin, or a <paramref name="bin"/> that is not a local maximum, is returned uninterpolated.
        /// </summary>
        public static (double Frequency, double Magnitude, double Bin) SpectralPeak(IReadOnlyList<double> magnitude, double sampleRate,
            int fftLength, PeakMethod method = PeakMethod.Gaussian, int? bin = null)
        {
            if (magnitude is null) throw new ArgumentNullException(nameof(magnitude));
            if (magnitude.Count == 0) throw new ArgumentException("Spectrum is empty.", nameof(magnitude));
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            if (fftLength < 1) throw new ArgumentOutOfRangeException(nameof(fftLength), fftLength, "FFT length must be positive.");
            // An undefined method was taken as Parabolic; WelchPsd and Window refuse undefined enum values too.
            if (!Enum.IsDefined(typeof(PeakMethod), method)) throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown peak method.");
            int k = bin ?? ArgMax(magnitude);
            if (k < 0 || k >= magnitude.Count) throw new ArgumentOutOfRangeException(nameof(bin), bin, "Bin outside the spectrum.");

            double offset = 0, peak = magnitude[k];
            if (k > 0 && k < magnitude.Count - 1 && magnitude[k] >= magnitude[k - 1] && magnitude[k] >= magnitude[k + 1])
            {
                (offset, peak) = method == PeakMethod.Gaussian && magnitude[k - 1] > 0 && magnitude[k] > 0 && magnitude[k + 1] > 0
                    ? Gaussian(magnitude[k - 1], magnitude[k], magnitude[k + 1])
                    : Parabolic(magnitude[k - 1], magnitude[k], magnitude[k + 1]);
                // Not a local maximum (or a NaN vertex, from ∞ neighbours): don't extrapolate.
                if (!(Math.Abs(offset) <= 1)) { offset = 0; peak = magnitude[k]; }
            }
            double fractionalBin = k + offset;
            double frequency = fractionalBin * sampleRate / fftLength;   // k·fs overflowed for a huge rate
            if (double.IsInfinity(frequency)) frequency = fractionalBin * (sampleRate / fftLength);
            return (frequency, peak, fractionalBin);
        }

        private static int ArgMax(IReadOnlyList<double> v)
        {
            int best = -1;
            for (int i = 0; i < v.Count; i++) if (!double.IsNaN(v[i]) && (best < 0 || v[i] > v[best])) best = i;
            if (best < 0) throw new ArgumentException("Spectrum is all NaN.", "magnitude");
            return best;
        }
    }
}
