// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Signal
{
    /// <summary>
    /// A peak-following envelope detector with separate attack and release time constants — the cheap, streaming
    /// alternative to the Hilbert envelope (which needs the whole record): it rises quickly toward a growing |x| and decays
    /// slowly when the signal falls, like an audio level meter or a compressor's detector. Each time constant is the time
    /// to cover 1 − 1/e (63%) of a step. Works sample by sample (<see cref="Next"/>) or over an array (<see cref="Process"/>).
    /// Not thread-safe.
    /// </summary>
    public sealed class EnvelopeFollower
    {
        private readonly double _attack, _release;

        /// <param name="sampleRate">Samples per second.</param>
        /// <param name="attackSeconds">Rise time constant (0 = follow rises instantly).</param>
        /// <param name="releaseSeconds">Decay time constant (0 = follow falls instantly).</param>
        public EnvelopeFollower(double sampleRate, double attackSeconds, double releaseSeconds)
        {
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
            if (!(attackSeconds >= 0)) throw new ArgumentOutOfRangeException(nameof(attackSeconds), attackSeconds, "Must be non-negative.");
            if (!(releaseSeconds >= 0)) throw new ArgumentOutOfRangeException(nameof(releaseSeconds), releaseSeconds, "Must be non-negative.");
            _attack = Coefficient(attackSeconds, sampleRate);
            _release = Coefficient(releaseSeconds, sampleRate);
        }

        /// <summary>The current envelope value.</summary>
        public double Value { get; private set; }

        /// <summary>Feeds one sample and returns the updated envelope.</summary>
        public double Next(double sample)
        {
            double level = Math.Abs(sample);
            double a = level > Value ? _attack : _release;
            Value = a * Value + (1 - a) * level;
            return Value;
        }

        /// <summary>Restarts from <paramref name="value"/>.</summary>
        public void Reset(double value = 0) => Value = value;

        /// <summary>The envelope of a whole array, starting from zero.</summary>
        public static double[] Process(double[] samples, double sampleRate, double attackSeconds, double releaseSeconds)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            var f = new EnvelopeFollower(sampleRate, attackSeconds, releaseSeconds);
            var result = new double[samples.Length];
            for (int i = 0; i < samples.Length; i++) result[i] = f.Next(samples[i]);
            return result;
        }

        // One-pole smoothing coefficient whose step response reaches 1 − 1/e after τ seconds.
        private static double Coefficient(double tau, double fs) => tau == 0 ? 0 : Math.Exp(-1 / (tau * fs));
    }
}
