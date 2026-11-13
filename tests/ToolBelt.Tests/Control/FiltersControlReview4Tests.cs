using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Control;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    // Regressions from review round 67 (filters & control, fourth pass).
    public sealed class FiltersControlReview4Tests
    {
        private static void Near(double expected, double actual, double rel) => Check.Close(expected, actual, rel * Math.Abs(expected));

        // ---------- PidController: conditional integration clamps at the limit instead of skipping the step ----------

        public void IntegralOnlyControllerReachesSaturationWithALargeStep()
        {
            var pid = new PidController(0, 1, 0, -1, 1) { Setpoint = 2 };
            double u = 0;
            for (int i = 0; i < 5; i++) u = pid.Update(0, 1);
            Check.Equal(1.0, u);                                    // was 0 for good: every step would cross the limit
            var rev = new PidController(0, 1, 0, -1, 1) { Setpoint = -2 };
            for (int i = 0; i < 5; i++) u = rev.Update(0, 1);
            Check.Equal(-1.0, u);
        }

        public void HugeErrorStillDrivesTheOutputToTheLimit()
        {
            var pid = new PidController(0, 1, 0, -1, 1) { Setpoint = 1e308 };
            double u = 0;
            for (int i = 0; i < 5; i++) u = pid.Update(-1e308, 1e10);   // error·dt overflows
            Check.Equal(1.0, u);
            var low = new PidController(0, 1, 0, -1, 1) { Setpoint = -1e308 };
            for (int i = 0; i < 5; i++) u = low.Update(1e308, 1e10);
            Check.Equal(-1.0, u);
        }

        public void ClosedLoopPiSettlesWithACoarseStep()
        {
            // First-order plant, τ = 5; dt = 2 used to stick at y = 0.15.
            var pid = new PidController(0.2, 1, 0, 0, 1) { Setpoint = 0.9 };
            double y = 0, dt = 2;
            for (int i = 0; i < 100; i++)
            {
                double u = pid.Update(y, dt);
                y += (u - y) * (1 - Math.Exp(-dt / 5));
            }
            Check.Close(0.9, y, 1e-3);
        }

        public void UnreachableSetpointSaturatesTheOutput()
        {
            var pid = new PidController(0.1, 5, 0, 0, 1) { Setpoint = 1 };
            double y = 0, u = 0, dt = 0.5;
            for (int i = 0; i < 400; i++)
            {
                u = pid.Update(y, dt);
                y += (0.5 * u - y) * (1 - Math.Exp(-dt / 5));
            }
            Check.Equal(1.0, u);
            Check.Close(0.5, y, 1e-6);
        }

        // ---------- Huge and subnormal sample rates: frequency ratios first ----------

        public void ResponsesAreScaleInvariantAtAHugeSampleRate()
        {
            const double s = 1.6e308;
            var iir = IirFilter.ButterworthLowPass(2, 0.375, 1);
            var a = iir.Response(0.375 * s, s);
            var b = iir.Response(0.375, 1);
            Check.Close(b.Real, a.Real, 1e-12);
            Check.Close(b.Imaginary, a.Imaginary, 1e-12);
            Near(iir.GroupDelay(0.375, 1), iir.GroupDelay(0.375 * s, s), 1e-12);
            var fir = FirFilter.LowPass(11, 0.3, 1);
            var c = fir.Response(0.3 * s, s);
            var d = fir.Response(0.3, 1);
            Check.Close(d.Real, c.Real, 1e-12);
            Check.Close(d.Imaginary, c.Imaginary, 1e-12);
            var big = IirFilter.ButterworthLowPass(2, 0.375 * s, s);
            Check.Close(iir.Sections[0].B0, big.Sections[0].B0, 1e-12);
            Check.Close(iir.Sections[0].A1, big.Sections[0].A1, 1e-12);
            var shelf = Biquad.LowShelf(1e308, 3e307, 6);
            var shelfRef = Biquad.LowShelf(1, 0.3, 6);
            Check.Close(shelfRef.B0, shelf.B0, 1e-12);
            Check.Close(shelfRef.A1, shelf.A1, 1e-12);
        }

        public void OrderEstimatesAreScaleInvariantAtAHugeSampleRate()
        {
            const double s = 1.6e308;
            var (n, wc) = IirFilter.ButterworthOrder(0.3 * s, 0.4 * s, 1, 40, s);
            var (n1, wc1) = IirFilter.ButterworthOrder(0.3, 0.4, 1, 40, 1);
            Check.Equal(n1, n);
            Near(wc1 * s, wc, 1e-12);
            var (m, e) = IirFilter.Chebyshev2Order(0.3 * s, 0.4 * s, 1, 40, s);
            var (m1, e1) = IirFilter.Chebyshev2Order(0.3, 0.4, 1, 40, 1);
            Check.Equal(m1, m);
            Near(e1 * s, e, 1e-12);
        }

        public void DesignsAreScaleInvariantAtASubnormalSampleRate()
        {
            const double fs = 1e-320;
            // 0.1·fs is itself subnormal and rounded, so the reference takes the ratio the design actually sees.
            double ratio = 0.1 * fs / fs;
            var sub = Biquad.LowPass(fs, 0.1 * fs);
            var reference = Biquad.LowPass(1, ratio);
            Check.Close(reference.B0, sub.B0, 1e-12);      // was 0.06723 against 0.06746: fs/2 rounded
            Check.Close(reference.A1, sub.A1, 1e-12);
            var fir = FirFilter.LowPass(7, 0.1 * fs, fs);
            var firRef = FirFilter.LowPass(7, ratio, 1);
            for (int i = 0; i < 7; i++) Check.Close(firRef.Taps[i], fir.Taps[i], 1e-12);
        }

        // ---------- FIR ----------

        public void TinyCutoffTapsKeepTheirDigits()
        {
            foreach (double c in new[] { 1e-318, 1e-321, 2e-323 })
            {
                var f = FirFilter.LowPass(21, c, 1);
                var w = Enumerable.Range(0, 21).Select(n => 0.54 - 0.46 * Math.Cos(2 * Math.PI * n / 20)).ToArray();
                double sum = w.Sum();
                for (int i = 0; i < 21; i++) Near(w[i] / sum, f.Taps[i], 1e-12);   // was 62% off at 2e-323
            }
        }

        public void TapCountsPastTheLargestArrayAreRefusedByName()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.LowPass(int.MaxValue, 0.1, 1));
            Check.Equal("taps", ex.ParamName);
            // A transition width that needs a count between Array.MaxLength and int.MaxValue.
            double k = (60 - 7.95) / 2.285 / Math.PI, width = k / 2147483619.5;
            var kx = Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.KaiserParameters(60, width / 2, 1));
            Check.Equal("transitionWidth", kx.ParamName);
        }

        public void NonFiniteFrequencyIsRefused()
        {
            var fir = FirFilter.LowPass(11, 0.3, 1);
            var iir = IirFilter.ButterworthLowPass(2, 0.1, 1);
            var bq = Biquad.LowPass(1, 0.1);
            foreach (double f in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Check.Equal("frequency", Check.Throws<ArgumentOutOfRangeException>(() => fir.Response(f, 1)).ParamName);
                Check.Equal("frequency", Check.Throws<ArgumentOutOfRangeException>(() => iir.Response(f, 1)).ParamName);
                Check.Equal("frequency", Check.Throws<ArgumentOutOfRangeException>(() => iir.GroupDelay(f, 1)).ParamName);
                Check.Equal("frequency", Check.Throws<ArgumentOutOfRangeException>(() => bq.Response(f, 1)).ParamName);
            }
        }

        // ---------- IIR: FiltFilt, MagnitudeDb, subnormal state ----------

        private sealed class Virtual : IReadOnlyList<double>
        {
            public Virtual(int count) { Count = count; }
            public int Count { get; }
            public double this[int index] => 0;
            public IEnumerator<double> GetEnumerator() { for (int i = 0; i < Count; i++) yield return 0; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public void FiltFiltPaddedLengthOverflowIsRefusedByName()
        {
            var f = IirFilter.ButterworthLowPass(2, 0.1, 1);
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => f.FiltFilt(new Virtual(1_200_000_000), 1_000_000_000));
            Check.Equal("padLength", ex.ParamName);
        }

        public void FiltFiltOfAnEmptySignalWithNoPadIsEmpty()
        {
            Check.Equal(0, IirFilter.ButterworthLowPass(2, 0.1, 1).FiltFilt(new double[0], 0).Length);
        }

        public void MagnitudeDbDeepInTheStopBandIsFinite()
        {
            // Digital Butterworth: |H|² = 1 / (1 + (tan(πf)/tan(πfc))^(2N)).
            var f = IirFilter.ButterworthLowPass(40, 0.01, 1);
            foreach (double d in new[] { 1e-3, 1e-6, 1e-9, 1e-12 })
            {
                double r = Math.Log10(1 / Math.Tan(Math.PI * d)) - Math.Log10(Math.Tan(Math.PI * 0.01));
                double expected = -800 * r;                          // the 1 is negligible here
                // The stored coefficients put the 40 zeros about 2e-16 from −1, which moves each factor by a relative
                // 2e-16 / (πd): that is the filter's own response, so the tolerance allows it.
                double tol = 1e-6 + 8.686 * 40 * 4e-16 / (Math.PI * d);
                Check.Close(expected, f.MagnitudeDb(0.5 - d, 1), tol);   // was −∞ (|H| underflowed) from d = 1e-9
            }
            var g = IirFilter.ButterworthLowPass(4, 0.1, 1);
            Check.Close(20 * Math.Log10(g.Response(0.2, 1).Magnitude), g.MagnitudeDb(0.2, 1), 1e-10);
        }

        private static int Subnormals(IEnumerable<double> values) => values.Count(v => v != 0 && Math.Abs(v) < 2.2250738585072014E-308);

        public void DecayingTailFlushesToZeroInsteadOfRunningSubnormal()
        {
            var filt = IirFilter.ButterworthLowPass(8, 100, 48000);
            var x = new double[300_000];
            x[0] = 1;
            Check.Equal(0, Subnormals(filt.Filter(x)));               // was about 10 000 subnormal outputs (20× slower)
            filt.Reset();
            var y = x.Select(v => filt.Process(v)).ToArray();
            Check.Equal(0, Subnormals(y));
            // A signal that is itself tiny keeps its subnormal arithmetic: the output scales with the input.
            var tiny = new double[50];
            tiny[0] = 1e-300;
            var a = IirFilter.ButterworthLowPass(2, 0.1, 1).Filter(tiny);
            var b = IirFilter.ButterworthLowPass(2, 0.1, 1).Filter(x.Take(50).ToList());
            for (int i = 0; i < 50; i++) if (Math.Abs(b[i]) > 1e-8) Near(b[i] * 1e-300, a[i], 1e-9);
        }

        // ---------- Elliptic designs ----------

        // Smallest and largest passband gain in dB, sampled up to the cutoff.
        private static (double Min, double Max) Passband(IirFilter f, double cutoff)
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (int i = 0; i <= 4000; i++)
            {
                double db = f.MagnitudeDb(cutoff * i / 4000, 1);
                lo = Math.Min(lo, db);
                hi = Math.Max(hi, db);
            }
            return (lo, hi);
        }

        public void HighOrderEllipticDesignsMeetTheirPassbandSpec()
        {
            // The degree equation's 8-term nome series was used near nome 1: high order or high selectivity missed the
            // ripple spec (passband dipping past −rp).
            foreach (var (rp, rs) in new[] { (3.0, 20.0), (1.0, 40.0), (0.5, 60.0), (0.1, 80.0), (3.0, 10.0), (1.0, 5.0) })
                for (int n = 2; n <= 25; n++)
                {
                    IirFilter f;
                    try { f = IirFilter.EllipticLowPass(n, rp, rs, 0.2, 1); }
                    catch (ArgumentOutOfRangeException ex)
                    {
                        // Too selective for double precision: that must be said of the spec, not of the mid-band cutoff.
                        Check.True(ex.ParamName == "stopAttenuationDb" || ex.ParamName == "passRippleDb", $"n={n} rp={rp} rs={rs}: {ex.ParamName}");
                        continue;
                    }
                    var (lo, hi) = Passband(f, 0.2);
                    // The tolerance follows the conditioning: a pole a distance δ from the unit circle turns one rounding
                    // of a coefficient into a gain error of about ε/δ (at n = 18, rp = 3, rs = 20, δ = 1.6e-10 and SciPy's
                    // own design is 5e-6 dB over 0). The old series missed by far more (−3.087 dB at n = 20, δ = 1.3e-11).
                    double delta = f.Sections.Min(s => (1 - s.A2) / (1 + Math.Sqrt(Math.Abs(s.A2))));
                    double tol = 1e-6 + 8.7 * 4e-16 / delta;
                    Check.True(lo >= -rp - tol && hi <= tol, $"n={n} rp={rp} rs={rs}: passband [{lo}, {hi}] dB, tolerance {tol}");
                }
        }

        public void EllipticAttenuationJustAboveTheRippleDesigns()
        {
            // 1 − k1² was formed by subtraction and lost its digits (SciPy's own n = 4 design here peaks at +11 dB).
            foreach (var (n, rs) in new[] { (2, 1.000001), (3, 1.01) })
            {
                var f = IirFilter.EllipticLowPass(n, 1, rs, 0.2, 1);
                var (lo, hi) = Passband(f, 0.2);
                Check.True(lo >= -1 - 1e-5 && hi <= 1e-5, $"n={n} rs={rs}: passband [{lo}, {hi}] dB");
                double stop = Enumerable.Range(0, 200).Select(i => f.MagnitudeDb(0.3 + 0.1999 * i / 200, 1)).Max();
                Check.True(stop <= -rs + 1e-4, $"n={n} rs={rs}: stop band peak {stop} dB");
            }
            // At n = 4 the exact poles are 5.5e-22 from the jω axis: refused, and said of the spec.
            Check.Equal("stopAttenuationDb", Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.EllipticLowPass(4, 1, 1.000001, 0.2, 1)).ParamName);
        }

        public void OverSelectiveEllipticSpecIsNamed()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.EllipticLowPass(16, 3, 10, 0.2, 1));
            Check.Equal("stopAttenuationDb", ex.ParamName);           // was "cutoff", for a mid-band 0.2·fs
        }

        // ---------- StepResponse ----------

        public void RiseTimeAcrossTheWholeDoubleRangeIsPositive()
        {
            var info = StepResponse.Analyze(new[] { -1e308, 1e308 }, new[] { 0.0, 1 });
            Near(1.6e308, info.RiseTime, 1e-12);                        // was −∞
        }

        public void SettlingWithSamplesFarLargerThanTheStep()
        {
            var info = StepResponse.Analyze(new[] { 0.0, 1e-300, 1e300, 1e-300, 1e-300, 1e-300 }, 1);
            Check.False(double.IsNaN(info.SettlingTime));
            Check.True(info.SettlingTime >= 2 && info.SettlingTime <= 3, info.SettlingTime.ToString("R"));
            var other = StepResponse.Analyze(new[] { 0.0, -1e300, 1e-300, 1e-300, 1e-300 }, 1);
            Check.False(double.IsNaN(other.SettlingTime));
            Check.False(double.IsNaN(other.RiseTime));
        }

        // ---------- FopdtModel and tuning rules ----------

        public void FopdtStepResponseKeepsItsDigits()
        {
            Near(3e-20, new FopdtModel(3, 1, 0).StepResponse(1e-20), 1e-12);          // was 0
            Near(1e299, new FopdtModel(1e308, 1, 0).StepResponse(1e-10, 10), 1e-9);    // was ∞
            Near(3 * (1 - Math.Exp(-0.5)), new FopdtModel(3, 2, 1).StepResponse(2), 1e-14);
        }

        public void FitStepAcrossTheWholeDoubleRange()
        {
            var model = FopdtModel.FitStep(new[] { -1.7e308, -1.6e308, 1.7e308 }, new[] { 0.0, 0.5, 1 }, 1);
            Check.False(double.IsNaN(model.TimeConstant) || double.IsInfinity(model.TimeConstant));
            Check.True(model.TimeConstant > 0);
            var other = FopdtModel.FitStep(new[] { -1e308, 0, 1e308 }, new[] { 0.0, 0.5, 1 }, 1);
            Check.True(other.TimeConstant > 0 && other.DeadTime >= 0 && !double.IsInfinity(other.DeadTime));
        }

        public void ZieglerNicholsWithAHugeGainAndDeadTime()
        {
            var g = PidTuning.ZieglerNichols(new FopdtModel(1e300, 1e300, 1e10));
            Near(1.2e-10, g.Kp, 1e-12);
            Near(2e10, g.IntegralTime, 1e-12);
        }

        public void CohenCoonWithADeadTimeFarLongerThanTheTimeConstant()
        {
            var g = PidTuning.CohenCoon(new FopdtModel(1, 1e-10, 1e300));
            Near(0.25, g.Kp, 1e-9);
            Near(7.5e299, g.IntegralTime, 1e-9);
            Near(2e-10, g.DerivativeTime, 1e-9);
            var pi = PidTuning.CohenCoon(new FopdtModel(1, 1e-200, 1e200), false);
            Near(1.0 / 12, pi.Kp, 1e-9);
            Near(1.5e199, pi.IntegralTime, 1e-9);
        }

        public void CohenCoonAgreesWithTheTextbookFormOnOrdinaryModels()
        {
            var rnd = new Random(67);
            for (int trial = 0; trial < 200; trial++)
            {
                double k = 0.1 + 10 * rnd.NextDouble(), tau = 0.1 + 10 * rnd.NextDouble(), theta = 0.1 + 10 * rnd.NextDouble();
                double r = theta / tau;
                var g = PidTuning.CohenCoon(new FopdtModel(k, tau, theta));
                Near(tau / (k * theta) * (4.0 / 3 + r / 4), g.Kp, 1e-12);
                Near(theta * (32 + 6 * r) / (13 + 8 * r), g.IntegralTime, 1e-12);
                Near(4 * theta / (11 + 2 * r), g.DerivativeTime, 1e-12);
                var p = PidTuning.CohenCoon(new FopdtModel(k, tau, theta), false);
                Near(tau / (k * theta) * (0.9 + r / 12), p.Kp, 1e-12);
                Near(theta * (30 + 3 * r) / (9 + 20 * r), p.IntegralTime, 1e-12);
            }
        }
    }
}
