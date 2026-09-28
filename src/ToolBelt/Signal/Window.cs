// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Signal
{
    /// <summary>The tapering window applied before a transform to suppress spectral leakage.</summary>
    public enum WindowType
    {
        /// <summary>No taper (a boxcar). Best frequency resolution, worst leakage.</summary>
        Rectangular,
        /// <summary>Raised cosine. The sensible default for general spectral work.</summary>
        Hann,
        /// <summary>Raised cosine with a non-zero pedestal; lower first side-lobe than Hann.</summary>
        Hamming,
        /// <summary>Three-term cosine; strong side-lobe suppression.</summary>
        Blackman,
        /// <summary>Four-term cosine; ~92 dB side-lobe attenuation.</summary>
        BlackmanHarris,
        /// <summary>Five-term; near-flat main lobe for accurate amplitude estimation.</summary>
        FlatTop,
    }

    /// <summary>
    /// Tapering windows and the correction factors that go with them. Coefficients use the symmetric
    /// (N−1 denominator) definition, matching NumPy/MATLAB and the usual reference tables.
    /// </summary>
    public static class Window
    {
        /// <summary>Builds a window of the given length.</summary>
        public static double[] Create(WindowType type, int length)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be positive.");
            var w = new double[length];
            if (length == 1) { w[0] = 1.0; return w; }

            double denom = length - 1;
            for (int n = 0; n < length; n++)
            {
                double x = 2.0 * Math.PI * n / denom;
                w[n] = type switch
                {
                    WindowType.Rectangular => 1.0,
                    WindowType.Hann => 0.5 - 0.5 * Math.Cos(x),
                    WindowType.Hamming => 0.54 - 0.46 * Math.Cos(x),
                    WindowType.Blackman => 0.42 - 0.5 * Math.Cos(x) + 0.08 * Math.Cos(2 * x),
                    WindowType.BlackmanHarris =>
                        0.35875 - 0.48829 * Math.Cos(x) + 0.14128 * Math.Cos(2 * x) - 0.01168 * Math.Cos(3 * x),
                    WindowType.FlatTop =>
                        0.21557895 - 0.41663158 * Math.Cos(x) + 0.277263158 * Math.Cos(2 * x)
                        - 0.083578947 * Math.Cos(3 * x) + 0.006947368 * Math.Cos(4 * x),
                    _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown window type."),
                };
            }
            return w;
        }

        /// <summary>Returns <paramref name="samples"/> multiplied element-wise by the window.</summary>
        public static double[] Apply(IReadOnlyList<double> samples, WindowType type)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            var w = Create(type, samples.Count);
            var result = new double[samples.Count];
            for (int i = 0; i < samples.Count; i++) result[i] = samples[i] * w[i];
            return result;
        }

        /// <summary>Multiplies <paramref name="samples"/> element-wise by <paramref name="window"/> in place-free fashion.</summary>
        public static double[] Apply(IReadOnlyList<double> samples, double[] window)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (window is null) throw new ArgumentNullException(nameof(window));
            if (samples.Count != window.Length)
                throw new ArgumentException("Sample and window lengths differ.", nameof(window));
            var result = new double[samples.Count];
            for (int i = 0; i < samples.Count; i++) result[i] = samples[i] * window[i];
            return result;
        }

        /// <summary>
        /// Coherent gain: the mean of the window. Amplitude estimates from a windowed transform must be
        /// divided by this to undo the taper's attenuation of a coherent tone.
        /// </summary>
        public static double CoherentGain(double[] window)
        {
            if (window is null) throw new ArgumentNullException(nameof(window));
            if (window.Length == 0) throw new ArgumentException("Window is empty.", nameof(window));
            double sum = 0;
            foreach (var v in window) sum += v;
            return sum / window.Length;
        }

        /// <summary>
        /// Equivalent noise bandwidth (in bins): the width of the ideal rectangular filter passing the same
        /// noise power. Power-spectral-density estimates are divided by this to become density-correct.
        /// </summary>
        public static double EquivalentNoiseBandwidth(double[] window)
        {
            if (window is null) throw new ArgumentNullException(nameof(window));
            if (window.Length == 0) throw new ArgumentException("Window is empty.", nameof(window));
            double sum = 0, sumSq = 0;
            foreach (var v in window) { sum += v; sumSq += v * v; }
            return window.Length * sumSq / (sum * sum);
        }
    }
}
