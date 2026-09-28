using System;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class HilbertTests
    {
        public void RealPart_ReconstructsInput()
        {
            var rng = new Random(71);
            const int n = 64;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = rng.NextDouble() * 2 - 1;
            var analytic = Hilbert.Analytic(x);
            for (int i = 0; i < n; i++) Check.Close(x[i], analytic[i].Real, 1e-9, $"[{i}]");
        }

        public void Envelope_OfAmplitudeModulatedTone()
        {
            // A carrier modulated by a slow envelope: the analytic magnitude recovers the envelope.
            const int n = 512;
            const double fs = 512, carrier = 64;
            var x = new double[n];
            var trueEnv = new double[n];
            for (int i = 0; i < n; i++)
            {
                double env = 1.0 + 0.5 * Math.Sin(2 * Math.PI * 2 * i / fs); // slow 2 Hz envelope
                trueEnv[i] = env;
                x[i] = env * Math.Cos(2 * Math.PI * carrier * i / fs);
            }
            var env2 = Hilbert.Envelope(x);
            // Compare away from the edges where the transform ripples.
            for (int i = 64; i < n - 64; i++) Check.Close(trueEnv[i], env2[i], 0.05, $"[{i}]");
        }

        public void EnvelopeOfPureCosineIsConstant()
        {
            const int n = 256;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = 3.0 * Math.Cos(2 * Math.PI * 20 * i / n);
            var env = Hilbert.Envelope(x);
            for (int i = 32; i < n - 32; i++) Check.Close(3.0, env[i], 1e-3, $"[{i}]");
        }

        public void InstantaneousFrequency_TracksTone()
        {
            const int n = 256;
            const double fs = 256, tone = 30;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = Math.Cos(2 * Math.PI * tone * i / fs);
            var freq = Hilbert.InstantaneousFrequency(x, fs);
            // Middle of the record should read the tone frequency.
            for (int i = 64; i < n - 64; i++) Check.Close(tone, freq[i], 0.5, $"[{i}]");
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Hilbert.Analytic(null!));
            Check.Throws<ArgumentOutOfRangeException>(() => Hilbert.InstantaneousFrequency(new double[] { 1, 2 }, 0));
            Check.Equal(0, Hilbert.Analytic(Array.Empty<double>()).Length);
        }
    }
}
