using System;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    /// <summary>
    /// Round 170 review regressions (filters and control, tenth pass): each failed on the code before its fix.
    /// </summary>
    public sealed class FiltersReview170Tests
    {
        // −d arg / dω of 1 + a1·z⁻¹ + a2·z⁻², summed directly: the poles of a notch are well inside the circle.
        private static double PoleDelay(Biquad s, double f, double fs)
        {
            Complex z1 = Complex.FromPolarCoordinates(1, -2 * Math.PI * (f / fs)), z2 = z1 * z1;
            return ((s.A1 * z1 + 2 * s.A2 * z2) / (1 + s.A1 * z1 + s.A2 * z2)).Real;
        }

        // A notch has b0 == b2, so its zeros are exactly on the circle and each adds exactly ½ sample away from them. At
        // and near the centre the delay came out as ±1e6 to ±1e15 samples (Notch(48000, 50, 30): 1.03e12 at f0, 1.87e6 at
        // f0·(1 + 1e-9), for 9167.39): the zeros were taken at a radius off 1 by about 1e-16.
        public void Notch_GroupDelayAtTheCentre_IsTheDesigns()
        {
            var rng = new Random(170);
            for (int trial = 0; trial < 400; trial++)
            {
                double fs = new[] { 1000.0, 8000, 44100, 48000, 96000 }[rng.Next(5)];
                double f0 = fs * Math.Exp(Math.Log(1e-4) + rng.NextDouble() * (Math.Log(0.49) - Math.Log(1e-4)));
                double q = Math.Exp(Math.Log(0.3) + rng.NextDouble() * (Math.Log(100) - Math.Log(0.3)));
                var s = Biquad.Notch(fs, f0, q);
                Check.True(s.B0 == s.B2, "b0 == b2");
                foreach (double k in new[] { -1e-6, -1e-9, 0, 1e-9, 1e-6 })
                {
                    double f = f0 * (1 + k), got = s.GroupDelay(f, fs);
                    if (double.IsNaN(got) && double.IsNegativeInfinity(new IirFilter(new[] { s }).MagnitudeDb(f, fs))) continue;   // exactly on a zero
                    double expected = 1 - PoleDelay(s, f, fs);
                    Check.True(Math.Abs(got - expected) <= 1e-6 * Math.Abs(expected) + 1e-9,
                        $"Notch({fs}, {f0:R}, {q:R}) at f0·(1 + {k}): {got:R}, expected {expected:R}");
                    Check.True(Math.Abs(new IirFilter(new[] { s }).GroupDelay(f, fs) - expected) <= 1e-6 * Math.Abs(expected) + 1e-9, "IirFilter");
                }
            }
        }

        // A first-order section's zero is exactly at z = −1: at Nyquist the delay is undefined, as the doc says, but the
        // fallback to the direct sum gave 0.66246 samples for ButterworthLowPass(1, 100, 1000) (orders 2 and up gave NaN).
        public void GroupDelay_ExactlyOnAZero_IsNotFinite()
        {
            var rng = new Random(2170);
            for (int trial = 0; trial < 200; trial++)
            {
                double fs = 1000 * (1 + rng.Next(96));
                double fc = fs * (0.01 + 0.45 * rng.NextDouble());
                int order = 1 + rng.Next(6);
                var lp = IirFilter.ButterworthLowPass(order, fc, fs);
                Check.True(double.IsNegativeInfinity(lp.MagnitudeDb(fs / 2, fs)), "a zero at Nyquist");
                Check.True(double.IsNaN(lp.GroupDelay(fs / 2, fs)), $"low-pass order {order}, {fc:R} of {fs}: {lp.GroupDelay(fs / 2, fs):R}");
                var hp = IirFilter.ButterworthHighPass(order, fc, fs);
                Check.True(double.IsNaN(hp.GroupDelay(0, fs)), $"high-pass order {order}, {fc:R} of {fs}: {hp.GroupDelay(0, fs):R}");
            }
        }

        // An accepted design holds its spec at the edge, or it throws. The per-section test let through an elliptic
        // high-pass with its edge at 5.7e-6·fs whose rounded sections had +0.90 dB at the edge, for −2.775 dB.
        public void Elliptic_EdgeTooCloseForItsSections_IsRejected()
        {
            var e = Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.EllipticHighPass(16, 2.775, 24.438, 0.08889856141058335, 15476.266913367028));
            Check.Equal("cutoff", e.ParamName);
            // Its sections had −0.4746 dB at the edge, for −0.6746 dB (round 133's suspect).
            e = Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.EllipticHighPass(11, 0.6746060043311932, 3.7811020546517495, 3188.5956914197245, 24005303.363861687));
            Check.Equal("cutoff", e.ParamName);
            var rng = new Random(3170);
            int accepted = 0;
            for (int trial = 0; trial < 1500; trial++)
            {
                double fs = 1000 * (1 + rng.Next(48));
                double fc = fs * Math.Exp(Math.Log(1e-6) + rng.NextDouble() * (Math.Log(0.49) - Math.Log(1e-6)));
                int order = 1 + rng.Next(16);
                double rp = 0.05 + 5 * rng.NextDouble(), rs = rp + 5 + 60 * rng.NextDouble();
                bool high = rng.Next(2) == 0, elliptic = rng.Next(2) == 0;
                IirFilter filter;
                try
                {
                    filter = elliptic
                        ? (high ? IirFilter.EllipticHighPass(order, rp, rs, fc, fs) : IirFilter.EllipticLowPass(order, rp, rs, fc, fs))
                        : (high ? IirFilter.Chebyshev1HighPass(order, rp, fc, fs) : IirFilter.Chebyshev1LowPass(order, rp, fc, fs));
                }
                catch (ArgumentOutOfRangeException) { continue; }
                accepted++;
                double db = filter.MagnitudeDb(fc, fs);
                Check.True(Math.Abs(db + rp) <= 0.02,
                    $"{(elliptic ? "Elliptic" : "Chebyshev1")}{(high ? "HighPass" : "LowPass")}({order}, {rp:R}, {rs:R}, {fc:R}, {fs}): {db:R} dB at the edge");
            }
            Check.True(accepted > 1000, "accepted " + accepted);
        }
    }
}
