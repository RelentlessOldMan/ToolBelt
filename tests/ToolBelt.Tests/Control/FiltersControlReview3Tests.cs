using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Control;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    // Regressions from review round 53 (filters & control, third pass).
    public sealed class FiltersControlReview3Tests
    {
        private static string ParamOf(Action action)
        {
            try { action(); }
            catch (ArgumentException ex) { return ex.ParamName ?? "(null " + ex.GetType().Name + ")"; }
            catch (Exception ex) { return ex.GetType().Name; }
            return "(no throw)";
        }

        // ---------- IIR cascade accuracy, against the same sections run in double-double ----------

        // A double-double (hi + lo) number, enough to make the reference cascade's own rounding negligible.
        private struct DD
        {
            public double Hi, Lo;
            public DD(double hi, double lo) { Hi = hi; Lo = lo; }

            private static DD TwoSum(double a, double b)
            {
                double s = a + b, bb = s - a;
                return new DD(s, (a - (s - bb)) + (b - bb));
            }

            private static void Split(double a, out double hi, out double lo)
            {
                double t = 134217729.0 * a;                                     // 2^27 + 1
                hi = t - (t - a);
                lo = a - hi;
            }

            private static DD TwoProd(double a, double b)
            {
                double p = a * b;
                Split(a, out double ah, out double al);
                Split(b, out double bh, out double bl);
                return new DD(p, ((ah * bh - p) + ah * bl + al * bh) + al * bl);
            }

            public static DD operator +(DD a, DD b)
            {
                var s = TwoSum(a.Hi, b.Hi);
                double lo = s.Lo + a.Lo + b.Lo;
                double hi = s.Hi + lo;
                return new DD(hi, lo - (hi - s.Hi));
            }

            public static DD operator *(double c, DD a)
            {
                var p = TwoProd(c, a.Hi);
                double lo = p.Lo + c * a.Lo;
                double hi = p.Hi + lo;
                return new DD(hi, lo - (hi - p.Hi));
            }

            public static DD operator -(DD a) => new DD(-a.Hi, -a.Lo);
        }

        private static DD[] ReferenceFilter(IReadOnlyList<Biquad> sections, double[] x)
        {
            var y = x.Select(v => new DD(v, 0)).ToArray();
            foreach (var s in sections)
            {
                var o = new DD[y.Length];
                DD x1 = default, x2 = default, y1 = default, y2 = default;
                for (int i = 0; i < y.Length; i++)
                {
                    o[i] = s.B0 * y[i] + s.B1 * x1 + s.B2 * x2 + -(s.A1 * y1) + -(s.A2 * y2);
                    x2 = x1; x1 = y[i]; y2 = y1; y1 = o[i];
                }
                y = o;
            }
            return y;
        }

        // ‖double cascade − exact cascade‖ / ‖exact cascade‖ for the filter's own sections.
        private static double CascadeError(IirFilter f, double[] x)
        {
            double[] yd = f.Filter(x);
            DD[] yr = ReferenceFilter(f.Sections, x);
            double num = 0, den = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double d = (yd[i] - yr[i].Hi) - yr[i].Lo;
                num += d * d;
                den += yr[i].Hi * yr[i].Hi;
            }
            return Math.Sqrt(num / den);
        }

        private static double[] Signal(int seed, int n = 300)
        {
            var rng = new Random(seed);
            return Enumerable.Range(0, n).Select(_ => 1 + (rng.NextDouble() - 0.5) * 3).ToArray();
        }

        public void Iir_HighOrderBandStopCascadeIsAccurate()
        {
            // Ordered by pole radius alone these were 3e4, 1.75 and 3e13 times their own output in error.
            var x = Signal(53);
            var cases = new (string Name, IirFilter Filter)[]
            {
                ("bw 36", IirFilter.ButterworthBandStop(36, 1.66, 300, 1000)),
                ("bw 17", IirFilter.ButterworthBandStop(17, 7.9, 400, 1000)),
                ("bw 14", IirFilter.ButterworthBandStop(14, 35.6, 400, 1000)),
                ("c1 30", IirFilter.Chebyshev1BandStop(30, 1, 10, 400, 1000)),
                ("c1 10", IirFilter.Chebyshev1BandStop(10, 1, 5, 400, 1000)),
            };
            foreach (var (name, f) in cases)
            {
                double e = CascadeError(f, x);
                Check.True(e < 1e-11, name + " " + e.ToString("R"));
            }
        }

        public void Iir_RandomDesignsCascadeIsAccurate()
        {
            var rng = new Random(5301);
            var x = Signal(7, 200);
            for (int trial = 0; trial < 120; trial++)
            {
                int order = 1 + rng.Next(30);
                double lo = 1000 * (0.002 + 0.2 * rng.NextDouble()), hi = lo + (480 - lo) * (0.05 + 0.9 * rng.NextDouble());
                IirFilter f;
                switch (rng.Next(8))
                {
                    case 0: f = IirFilter.ButterworthBandStop(order, lo, hi, 1000); break;
                    case 1: f = IirFilter.ButterworthBandPass(order, lo, hi, 1000); break;
                    case 2: f = IirFilter.Chebyshev1BandStop(order, 0.5, lo, hi, 1000); break;
                    case 3: f = IirFilter.Chebyshev2BandStop(Math.Min(order, 20), 60, lo, hi, 1000); break;
                    case 4: f = IirFilter.EllipticBandStop(Math.Min(order, 8), 0.5, 60, lo, hi, 1000); break;
                    case 5: f = IirFilter.ButterworthLowPass(order, lo, 1000); break;
                    case 6: f = IirFilter.Chebyshev1HighPass(order, 1, lo, 1000); break;
                    default: f = IirFilter.BesselBandPass(Math.Min(order, 20), lo, hi, 1000); break;
                }
                double e = CascadeError(f, x);
                Check.True(e < 1e-9, $"trial {trial} order {order} [{lo:R}, {hi:R}]: {e:R}");
            }
        }

        // ---------- poles within rounding of the unit circle ----------

        public void Iir_EdgeTooCloseToZeroOrNyquistIsRejected()
        {
            // ButterworthLowPass(2, 1, 1e9) had a section pole at exactly z = 1: DC gain ∞, a constant 3 came out as 3e-17.
            Check.Equal("cutoff", ParamOf(() => IirFilter.ButterworthLowPass(2, 1, 1e9)));
            Check.Equal("cutoff", ParamOf(() => IirFilter.ButterworthHighPass(4, 3, 1e9)));
            Check.Equal("low", ParamOf(() => IirFilter.ButterworthBandStop(2, 1, 1e8, 1e9)));
            Check.Equal("high", ParamOf(() => IirFilter.ButterworthBandPass(2, 1e8, 1e9 / 2 * (1 - 1e-12), 1e9)));
            Check.Equal("frequency", ParamOf(() => Biquad.LowPass(1e9, 1)));
            Check.Equal("frequency", ParamOf(() => Biquad.HighPass(1e9, 3)));
            Check.Equal("frequency", ParamOf(() => Biquad.Notch(1e9, 1e9 / 2 * (1 - 1e-12), 1)));
            Check.Equal("frequency", ParamOf(() => Biquad.LowShelf(1e9, 1, 12)));
            Check.Equal("frequency", ParamOf(() => Biquad.Peaking(1e9, 1, 1, 0)));
            // An extreme gain alone did it too: the DC response came out NaN or 0.
            foreach (double g in new[] { 600.0, 1000, -1000 })
            {
                Check.Equal("gainDb", ParamOf(() => Biquad.Peaking(48000, 1000, 1, g)));
                Check.Equal("gainDb", ParamOf(() => Biquad.LowShelf(48000, 1000, g)));
                Check.Equal("gainDb", ParamOf(() => Biquad.HighShelf(48000, 1000, g)));
            }
            // ±300 dB still designs, to the expected gain.
            Check.Close(1e15, Biquad.LowShelf(48000, 1000, 300).Response(0, 48000).Magnitude, 1e-6 * 1e15);
            Check.Close(1e15, Biquad.Peaking(48000, 1000, 1, 300).Response(1000, 48000).Magnitude, 1e-6 * 1e15);
            // Just above the limit a design still holds: a 0.1 Hz DC blocker at 48 kHz.
            var dcBlock = IirFilter.ButterworthHighPass(2, 0.1, 48000);
            Check.True(dcBlock.Response(10000, 48000).Magnitude > 0.999999);
            var lp = Biquad.LowPass(48000, 0.1);
            Check.Close(1, lp.Response(0, 48000).Magnitude, 1e-6);
        }

        public void Iir_RoundedPolesStayWithinTheCheckedAccuracy()
        {
            // Every design that is accepted keeps its rounded coefficients' DC and Nyquist response within ~1e-4.
            foreach (double f in new[] { 0.5, 1, 2, 5, 10, 100 })
            {
                IirFilter lp;
                try { lp = IirFilter.ButterworthLowPass(2, f, 1e6); }
                catch (ArgumentOutOfRangeException ex) { Check.Equal("cutoff", ex.ParamName); continue; }
                double dc = 1;
                foreach (var s in lp.Sections) dc *= (s.B0 + s.B1 + s.B2) / ((1 + s.A1) + s.A2);
                Check.True(Math.Abs(dc - 1) < 1e-3, f + ": " + dc.ToString("R"));
            }
        }

        // ---------- elliptic designs on .NET Framework ----------

        public void Elliptic_SmallRippleMatchesSciPy()
        {
            // .NET Framework's Complex.Asin was 9e-8 off at j·1e5, which moved these designs by up to 7e-3.
            var cases = new (int N, double Rp, double Rs, double[] Db)[]
            {
                (4, 1e-4, 60, new[] { -1.9586658817762292e-05, -9.999999999556365e-05, -0.09336604606683382, -24.385288492272306 }),
                (6, 1e-6, 80, new[] { -9.879833337487522e-07, -1.0000000202647455e-06, -0.06439677256890924, -49.81172228458693 }),
                (4, 4.3e-10, 60, new[] { -8.666218076464314e-11, -4.3000326062142006e-10, -3.8058047567248213e-07, -0.0026177135734800133 }),
                (5, 1e-12, 100, new[] { -3.0762046183050404e-13, -9.865069982840688e-13, -6.805815351081092e-09, -0.00041803229227677964 }),
                (6, 1e-8, 40, new[] { -9.814761988782682e-09, -1.0000023760230884e-08, -0.0008437465396795379, -40.111015577164395 }),
            };
            double[] freqs = { 50, 100, 150, 300 };
            foreach (var c in cases)
            {
                var f = IirFilter.EllipticLowPass(c.N, c.Rp, c.Rs, 100, 1000);
                for (int i = 0; i < freqs.Length; i++)
                {
                    double db = f.MagnitudeDb(freqs[i], 1000);
                    Check.True(Math.Abs(db - c.Db[i]) < 1e-5 * Math.Abs(c.Db[i]) + 1e-12,
                        $"N={c.N} f={freqs[i]}: {db:R} vs {c.Db[i]:R}");
                }
            }
        }

        // ---------- tiny ripple ----------

        public void Order_TinyRippleIsComputedNotRejected()
        {
            // The discrimination overflowed and was reported as "transition band too narrow" (ripple 1e-305 dB).
            Check.Equal(446, IirFilter.ButterworthOrder(100, 200, 1e-305, 60, 1000).Order);
            Check.Equal(439, IirFilter.ButterworthOrder(100, 200, 1e-300, 60, 1000).Order);
            Check.Equal(250, IirFilter.Chebyshev1Order(100, 200, 1e-305, 60, 1000).Order);
            Check.Equal(246, IirFilter.Chebyshev1Order(100, 200, 1e-300, 60, 1000).Order);
            Check.True(IirFilter.EllipticOrder(100, 200, 1e-305, 60, 1000).Order > 0);
            // A ripple below the smallest representable 10^(r/10) − 1 is named.
            Check.Equal("passRippleDb", ParamOf(() => IirFilter.ButterworthOrder(100, 200, 1e-323, 60, 1000)));
        }

        public void Design_TinyRippleNamesTheRipple()
        {
            // Chebyshev I and elliptic designs with an unrepresentably small ripple said "numerically unstable".
            Check.Equal("rippleDb", ParamOf(() => IirFilter.Chebyshev1LowPass(4, 1e-323, 100, 1000)));
            Check.Equal("passRippleDb", ParamOf(() => IirFilter.EllipticLowPass(4, 1e-323, 60, 100, 1000)));
        }

        // ---------- FiltFilt, FIR ----------

        public void FiltFilt_HugeConstantStaysFinite()
        {
            // 2·x0 − x[k] overflowed: a constant 1e308 came out NaN.
            var f = IirFilter.ButterworthLowPass(2, 50, 1000);
            double[] y = f.FiltFilt(Enumerable.Repeat(1e308, 100).ToArray());
            Check.True(y.All(v => Math.Abs(v / 1e308 - 1) < 1e-9), y[0].ToString("R"));
            var g = new FirFilter(new[] { 1.0 });
            Check.Equal(1e308, g.Filter(new[] { 1e308 })[0]);
        }

        public void Fir_TapsAreExactlySymmetric()
        {
            // The window's cosines rounded differently at n and taps − 1 − n, so the phase was not exactly linear.
            foreach (FirWindow w in Enum.GetValues(typeof(FirWindow)))
                foreach (int taps in new[] { 31, 64, 101, 255 })
                {
                    var designs = new[]
                    {
                        FirFilter.LowPass(taps, 123.4, 1000, w), FirFilter.BandPass(taps | 1, 50.5, 210.7, 1000, w),
                        FirFilter.HighPass(taps | 1, 77.7, 1000, w), FirFilter.BandStop(taps | 1, 50.5, 210.7, 1000, w),
                    };
                    foreach (var d in designs)
                    {
                        var h = d.Taps;
                        for (int n = 0; n < h.Count; n++)
                            Check.True(h[n].Equals(h[h.Count - 1 - n]), $"{w} {h.Count} tap {n}: {h[n]:R} vs {h[h.Count - 1 - n]:R}");
                    }
                }
        }

        // ---------- parameter names ----------

        public void Signal_ParamNames()
        {
            Check.Equal("high", ParamOf(() => IirFilter.ButterworthBandPass(2, 200, 100, 1000)));
            Check.Equal("high", ParamOf(() => FirFilter.BandPass(31, 200, 100, 1000)));
            Check.Equal("a1", ParamOf(() => new Biquad(1, 0, 0, double.NaN, 0)));
            Check.Equal("b2", ParamOf(() => new Biquad(1, 0, double.PositiveInfinity, 0, 0)));
            Check.Equal("stopEdge", ParamOf(() => IirFilter.ButterworthOrder(100, 100, 1, 60, 1000)));
            Check.Equal("stopLow", ParamOf(() => IirFilter.ButterworthOrder(100, 200, 150, 250, 1, 60, 1000)));
        }

        public void Control_ParamNames()
        {
            Check.Equal("lowThreshold", ParamOf(() => new SchmittTrigger(2, 1)));
            Check.Equal("outputMin", ParamOf(() => new PidController(1, 0, 0, 2, 1)));
            Check.Equal("initialEstimate", ParamOf(() => new KalmanFilter1D(1, 1, double.NaN)));
            Check.Equal("initialErrorCovariance", ParamOf(() => new KalmanFilter1D(1, 1, 0, -1)));
            double[] t = { 0, 1, 2, 3 };
            Check.Equal("values", ParamOf(() => FopdtModel.FitStep(t, new double[] { 1, 1, 1, 1 }, 1)));
            Check.Equal("finalValue", ParamOf(() => FopdtModel.FitStep(t, new double[] { 0, 1, 2, 3 }, 1, finalValue: double.NaN)));
            Check.Equal("values", ParamOf(() => FopdtModel.FitStep(t, new double[] { 0, 0.1, 0.2, 0.25 }, 1, finalValue: 1)));
            Check.Equal("initialValue", ParamOf(() => StepResponse.Analyze(t, new double[] { 0, 1, 1, 1 }, initialValue: double.NaN)));
            Check.Equal("values", ParamOf(() => StepResponse.Analyze(t, new double[] { 1, 1, 1, 1 })));
            Check.Equal("values", ParamOf(() => StepResponse.Analyze(t, new double[] { -1e308, 0, 1e308, 1e308 })));
        }

        // ---------- PID ----------

        public void Pid_ZeroGainTimesOverflowIsZero()
        {
            // 0 · ∞ (an error or change that overflowed) made the output NaN.
            var pi = new PidController(0, 1, 0) { Setpoint = 1.7e308 };
            Check.Equal(0.0, pi.Update(-1.7e308, 1e-300));
            // The error overflowed to ∞, so a tiny gain gave ∞ instead of 3.4e8.
            var pd = new PidController(1e-300, 0, 0) { Setpoint = 1.7e308 };
            Check.Close(3.4e8, pd.Update(-1.7e308, 1), 1e-12 * 3.4e8);
            var pdm = new PidController(1, 0, 0, derivativeOnMeasurement: false) { Setpoint = 0 };
            pdm.Update(1.7e308, 1);
            Check.Equal(1.7e308, pdm.Update(-1.7e308, 1));                     // Kd = 0: no 0 · ∞ derivative
        }

        public void Pid_OppositeOverflowsAreSummedExactly()
        {
            // Kp·e = +∞ and Kd·de/dt = −∞ gave ∞ − ∞ = NaN; the true output is finite.
            // P = 10 · 1.5e308 = 1.5e309 and D = 10 · (1.5e308 − 1.64e308) / 0.1 = −1.4e309: the sum is 1e308.
            var pid = new PidController(10, 0, 10, derivativeOnMeasurement: false) { Setpoint = 0 };
            pid.Update(-1.64e308, 0.1);
            Check.Close(1e308, pid.Update(-1.5e308, 0.1), 1e-12 * 1e308);
            var pidm = new PidController(10, 0, 10) { Setpoint = 0 };                       // on measurement
            pidm.Update(-1.64e308, 0.1);
            Check.Close(1e308, pidm.Update(-1.5e308, 0.1), 1e-12 * 1e308);
        }

        public void Pid_InfiniteLimitPairsAreRejected()
        {
            Check.Equal("outputMin", ParamOf(() => new PidController(1, 0, 0, double.PositiveInfinity, double.PositiveInfinity)));
            Check.Equal("outputMax", ParamOf(() => new PidController(1, 0, 0, double.NegativeInfinity, double.NegativeInfinity)));
        }

        // ---------- tuning ----------

        public void PidGains_UnrepresentableKiOrKdIsRejected()
        {
            Check.Equal("integralTime", ParamOf(() => new PidGains(1e300, 1e-300, 0)));
            Check.Equal("integralTime", ParamOf(() => new PidGains(1e-300, 1e300, 0)));
            Check.Equal("derivativeTime", ParamOf(() => new PidGains(1e300, 1, 1e300)));
            Check.Equal("derivativeTime", ParamOf(() => new PidGains(1e-300, 1, 1e-300)));
            var ok = new PidGains(0, 1e-300, 1e300);                             // Kp = 0: Ki = Kd = 0 exactly
            Check.Equal(0.0, ok.Ki);
        }

        public void Tuning_OverflowedSumStillTunes()
        {
            // λ + θ overflowed to ∞, so Kp silently came out 0.
            var model = new FopdtModel(1, 1e308, 1e308);
            Check.Close(0.5, PidTuning.Lambda(model).Kp, 1e-15);
            var simc = PidTuning.Simc(new FopdtModel(1, 1e308, 1e308), 1e308);
            Check.Close(0.5, simc.Kp, 1e-15);
            Check.Equal(1e308, simc.IntegralTime);
        }

        public void ZieglerNicholsUltimate_UnderflowNamesThePeriod()
        {
            Check.Equal("ultimatePeriod", ParamOf(() => PidTuning.ZieglerNicholsUltimate(1, 5e-324)));
            Check.Equal("ultimatePeriod", ParamOf(() => PidTuning.ZieglerNicholsUltimate(1, 2e-323)));      // Td = Pu/8 → 0
            Check.Equal("ultimatePeriod", ParamOf(() => PidTuning.ZieglerNicholsUltimate(1e300, 1e-300)));  // Ki overflows
            Check.Equal("ultimateGain", ParamOf(() => PidTuning.ZieglerNicholsUltimate(5e-324, 1, includeDerivative: false)));     // Kp = 0.45 · 5e-324 → 0
        }

        public void FitStep_HugeChangeAndExtremeStep()
        {
            // An output change past double.MaxValue was "did not change"; an underflowed gain blamed "gain".
            var t = Enumerable.Range(0, 200).Select(i => i * 0.1).ToArray();
            var y = t.Select(ti => { double u = ti < 1 ? 0 : 1 - Math.Exp(-(ti - 1) / 2); return -1e308 * (1 - u) + 1e308 * u; }).ToArray();
            var m = FopdtModel.FitStep(t, y, 4, initialValue: -1e308, finalValue: 1e308);
            Check.Close(5e307, m.Gain, 1e-12 * 5e307);
            Check.Close(2, m.TimeConstant, 0.1);
            var small = t.Select(ti => ti < 1 ? 0 : 1e-300 * (1 - Math.Exp(-(ti - 1) / 2))).ToArray();
            Check.Equal("inputStep", ParamOf(() => FopdtModel.FitStep(t, small, 1e300, finalValue: 1e-300)));
        }

        // ---------- small blocks ----------

        public void Deadband_FarValuesAndCentres()
        {
            // center + offset − halfWidth overflowed (offset 3e308) and cancelled (3 about 1e17 gave 1, not 4).
            Check.Equal(1.5e308 - 1, Deadband.Apply(1.5e308, 1, -1.5e308));
            Check.Equal(-1.5e308 + 1, Deadband.Apply(-1.5e308, 1, 1.5e308));
            Check.Equal(4.0, Deadband.Apply(3, 1, 1e17));
            var rng = new Random(531);
            for (int i = 0; i < 2000; i++)
            {
                double c = (rng.NextDouble() - 0.5) * 100, w = rng.NextDouble() * 10, v = (rng.NextDouble() - 0.5) * 100;
                decimal off = (decimal)v - (decimal)c, expected = Math.Abs(off) <= (decimal)w ? (decimal)c : (decimal)v - Math.Sign(off) * (decimal)w;
                Check.Close((double)expected, Deadband.Apply(v, w, c), 1e-13 * Math.Max(1, Math.Abs((double)expected)));
            }
        }

        public void FirstOrderLag_SmallStepIsAccurate()
        {
            // The series below 1e-5 and Exp(x) − 1 above lost about 2500 ulps just past the switch.
            foreach (double x in new[] { 1.0000001e-5, 1.2e-5, 3e-5, 1e-4, 1e-3, 0.3 })
            {
                var lag = new FirstOrderLag(1);
                lag.Update(0, 0);
                double alpha = lag.Update(1, x);
                double reference = 0, term = 1;                                  // 1 − e^(−x) = x − x²/2 + x³/6 − …
                for (int k = 1; k < 30; k++) { term *= x / k; reference += (k % 2 == 1 ? term : -term); }
                Check.True(Math.Abs(alpha - reference) <= 2e-15 * reference, $"{x:R}: {alpha:R} vs {reference:R}");
            }
        }
    }
}
