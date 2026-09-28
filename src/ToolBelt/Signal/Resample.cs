// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Sample-rate conversion by linear interpolation, plus integer decimation with an anti-alias
    /// pre-average. Linear resampling is cheap and phase-linear — the right default when the signal is
    /// already comfortably oversampled; for aggressive downsampling, pre-filter first.
    /// </summary>
    public static class Resample
    {
        /// <summary>
        /// Resamples <paramref name="samples"/> to exactly <paramref name="newLength"/> points by linear
        /// interpolation, holding the endpoints fixed.
        /// </summary>
        public static double[] Linear(double[] samples, int newLength)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (newLength < 1) throw new ArgumentOutOfRangeException(nameof(newLength), newLength, "New length must be positive.");
            if (samples.Length == 0) throw new ArgumentException("Input must be non-empty.", nameof(samples));

            var result = new double[newLength];
            if (samples.Length == 1 || newLength == 1)
            {
                for (int i = 0; i < newLength; i++) result[i] = samples[0];
                return result;
            }

            double scale = (double)(samples.Length - 1) / (newLength - 1);
            for (int i = 0; i < newLength; i++)
            {
                double pos = i * scale;
                int lo = (int)Math.Floor(pos);
                if (lo >= samples.Length - 1) { result[i] = samples[samples.Length - 1]; continue; }
                double frac = pos - lo;
                result[i] = samples[lo] * (1 - frac) + samples[lo + 1] * frac;
            }
            return result;
        }

        /// <summary>Resamples from <paramref name="sourceRate"/> to <paramref name="targetRate"/> (Hz).</summary>
        public static double[] ToRate(double[] samples, double sourceRate, double targetRate)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (sourceRate <= 0) throw new ArgumentOutOfRangeException(nameof(sourceRate), sourceRate, "Source rate must be positive.");
            if (targetRate <= 0) throw new ArgumentOutOfRangeException(nameof(targetRate), targetRate, "Target rate must be positive.");
            int newLength = Math.Max(1, (int)Math.Round(samples.Length * targetRate / sourceRate));
            return Linear(samples, newLength);
        }

        /// <summary>
        /// Downsamples by an integer <paramref name="factor"/>, averaging each block of
        /// <paramref name="factor"/> samples first to suppress aliasing.
        /// </summary>
        public static double[] Decimate(double[] samples, int factor)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (factor < 1) throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be at least 1.");
            if (factor == 1) return (double[])samples.Clone();

            int outLen = samples.Length / factor;
            var result = new double[outLen];
            for (int i = 0; i < outLen; i++)
            {
                double sum = 0;
                for (int j = 0; j < factor; j++) sum += samples[i * factor + j];
                result[i] = sum / factor;
            }
            return result;
        }
    }
}
