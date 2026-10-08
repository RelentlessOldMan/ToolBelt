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
            double denom = left - 2 * center + right;
            if (denom == 0) return (0, center);
            double delta = 0.5 * (left - right) / denom;
            return (delta, center - 0.25 * (left - right) * delta);
        }

        /// <summary>The peak of the Gaussian through the three (positive) values: a parabola fitted to their logarithms.</summary>
        public static (double Offset, double Peak) Gaussian(double left, double center, double right)
        {
            if (!(left > 0)) throw new ArgumentOutOfRangeException(nameof(left), left, "Gaussian interpolation needs positive values.");
            if (!(center > 0)) throw new ArgumentOutOfRangeException(nameof(center), center, "Gaussian interpolation needs positive values.");
            if (!(right > 0)) throw new ArgumentOutOfRangeException(nameof(right), right, "Gaussian interpolation needs positive values.");
            var (delta, logPeak) = Parabolic(Math.Log(left), Math.Log(center), Math.Log(right));
            return (delta, Math.Exp(logPeak));
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
            if (!(sampleRate > 0)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
            if (fftLength < 1) throw new ArgumentOutOfRangeException(nameof(fftLength), fftLength, "FFT length must be positive.");
            int k = bin ?? ArgMax(magnitude);
            if (k < 0 || k >= magnitude.Count) throw new ArgumentOutOfRangeException(nameof(bin), bin, "Bin outside the spectrum.");

            double offset = 0, peak = magnitude[k];
            if (k > 0 && k < magnitude.Count - 1 && magnitude[k] >= magnitude[k - 1] && magnitude[k] >= magnitude[k + 1])
            {
                (offset, peak) = method == PeakMethod.Gaussian && magnitude[k - 1] > 0 && magnitude[k] > 0 && magnitude[k + 1] > 0
                    ? Gaussian(magnitude[k - 1], magnitude[k], magnitude[k + 1])
                    : Parabolic(magnitude[k - 1], magnitude[k], magnitude[k + 1]);
                if (Math.Abs(offset) > 1) { offset = 0; peak = magnitude[k]; }   // not a local maximum: don't extrapolate
            }
            double fractionalBin = k + offset;
            return (fractionalBin * sampleRate / fftLength, peak, fractionalBin);
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
