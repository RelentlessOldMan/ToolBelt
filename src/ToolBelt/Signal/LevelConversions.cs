// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Decibel and level conversions — the arithmetic every audio/RF pipeline repeats. Amplitude ratios
    /// use the 20·log₁₀ convention, power ratios the 10·log₁₀ one; mixing them is the classic 3-dB bug.
    /// </summary>
    public static class LevelConversions
    {
        /// <summary>Amplitude (field) ratio to decibels: <c>20·log10(ratio)</c>.</summary>
        public static double AmplitudeToDb(double ratio)
        {
            if (ratio <= 0) throw new ArgumentOutOfRangeException(nameof(ratio), ratio, "Ratio must be positive.");
            return 20.0 * Math.Log10(ratio);
        }

        /// <summary>Decibels to an amplitude ratio: <c>10^(dB/20)</c>.</summary>
        public static double DbToAmplitude(double db) => Math.Pow(10.0, db / 20.0);

        /// <summary>Power ratio to decibels: <c>10·log10(ratio)</c>.</summary>
        public static double PowerToDb(double ratio)
        {
            if (ratio <= 0) throw new ArgumentOutOfRangeException(nameof(ratio), ratio, "Ratio must be positive.");
            return 10.0 * Math.Log10(ratio);
        }

        /// <summary>Decibels to a power ratio: <c>10^(dB/10)</c>.</summary>
        public static double DbToPower(double db) => Math.Pow(10.0, db / 10.0);

        /// <summary>Root-mean-square level of a signal.</summary>
        public static double Rms(IReadOnlyList<double> samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (samples.Count == 0) throw new ArgumentException("At least one sample is required.", nameof(samples));
            // Scale by the largest magnitude so the squares neither overflow (1e200²) nor underflow (1e-200²).
            double scale = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                double a = Math.Abs(samples[i]);
                if (double.IsNaN(a)) return double.NaN;
                if (a > scale) scale = a;
            }
            if (scale == 0 || double.IsInfinity(scale)) return scale;
            double sum = 0;
            for (int i = 0; i < samples.Count; i++) { double r = samples[i] / scale; sum += r * r; }
            return scale * Math.Sqrt(sum / samples.Count);
        }

        /// <summary>
        /// The RMS level relative to a full-scale amplitude, in dBFS (≤ 0 for signals within range).
        /// A full-scale sine reads about −3.01 dBFS; a full-scale square wave reads 0; digital silence reads −∞.
        /// </summary>
        public static double DbFs(IReadOnlyList<double> samples, double fullScale = 1.0)
        {
            if (!(fullScale > 0) || double.IsInfinity(fullScale)) throw new ArgumentOutOfRangeException(nameof(fullScale), fullScale, "Full scale must be positive and finite.");
            double rms = Rms(samples);
            return rms == 0 ? double.NegativeInfinity : 20.0 * Math.Log10(rms / fullScale);
        }
    }
}
