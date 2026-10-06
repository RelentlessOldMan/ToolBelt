using System;
using System.Linq;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class FilterDesignTests
    {
        private const double Fs = FilterReferenceData.SampleRate;

        internal static double[] TestSignal()
        {
            var x = new double[FilterReferenceData.SignalLength];
            for (int i = 0; i < x.Length; i++)
                x[i] = 1.5 + Math.Sin(0.05 * i) + 0.5 * Math.Sin(0.9 * i) + 0.3 * Math.Cos(2.1 * i + 0.4) + 0.004 * i;
            return x;
        }

        private static IirFilter Design(int index)
        {
            var (proto, band, order, rp, rs, f1, f2, norm) = FilterReferenceData.IirDesigns[index];
            var bessel = norm == "delay" ? BesselNormalization.Delay : norm == "mag" ? BesselNormalization.Magnitude : BesselNormalization.Phase;
            switch (proto + band)
            {
                case "ButterworthLowPass": return IirFilter.ButterworthLowPass(order, f1, Fs);
                case "ButterworthHighPass": return IirFilter.ButterworthHighPass(order, f1, Fs);
                case "ButterworthBandPass": return IirFilter.ButterworthBandPass(order, f1, f2, Fs);
                case "ButterworthBandStop": return IirFilter.ButterworthBandStop(order, f1, f2, Fs);
                case "Chebyshev1LowPass": return IirFilter.Chebyshev1LowPass(order, rp, f1, Fs);
                case "Chebyshev1HighPass": return IirFilter.Chebyshev1HighPass(order, rp, f1, Fs);
                case "Chebyshev1BandPass": return IirFilter.Chebyshev1BandPass(order, rp, f1, f2, Fs);
                case "Chebyshev1BandStop": return IirFilter.Chebyshev1BandStop(order, rp, f1, f2, Fs);
                case "Chebyshev2LowPass": return IirFilter.Chebyshev2LowPass(order, rs, f1, Fs);
                case "Chebyshev2HighPass": return IirFilter.Chebyshev2HighPass(order, rs, f1, Fs);
                case "Chebyshev2BandPass": return IirFilter.Chebyshev2BandPass(order, rs, f1, f2, Fs);
                case "Chebyshev2BandStop": return IirFilter.Chebyshev2BandStop(order, rs, f1, f2, Fs);
                case "EllipticLowPass": return IirFilter.EllipticLowPass(order, rp, rs, f1, Fs);
                case "EllipticHighPass": return IirFilter.EllipticHighPass(order, rp, rs, f1, Fs);
                case "EllipticBandPass": return IirFilter.EllipticBandPass(order, rp, rs, f1, f2, Fs);
                case "EllipticBandStop": return IirFilter.EllipticBandStop(order, rp, rs, f1, f2, Fs);
                case "BesselLowPass": return IirFilter.BesselLowPass(order, f1, Fs, bessel);
                case "BesselHighPass": return IirFilter.BesselHighPass(order, f1, Fs, bessel);
                case "BesselBandPass": return IirFilter.BesselBandPass(order, f1, f2, Fs, bessel);
                default: return IirFilter.BesselBandStop(order, f1, f2, Fs, bessel);
            }
        }

        public void Iir_GroupDelayMatchesSciPy()
        {
            foreach (var (d, f, expected) in FilterReferenceData.IirGroupDelay)
                Check.Close(expected, Design(d).GroupDelay(f, Fs), 1e-8 * Math.Max(1, Math.Abs(expected)), $"design {d} at {f} Hz");
        }

        public void Iir_GroupDelayAgreesWithPhaseDerivative()
        {
            // Independent of the analytic formula: −dφ/dω by central difference on the unwrapped response phase.
            foreach (var f in new[] { IirFilter.EllipticLowPass(6, 0.5, 60, 100, Fs), IirFilter.BesselBandPass(4, 50, 150, Fs), IirFilter.Chebyshev2HighPass(5, 40, 80, Fs) })
                foreach (double hz in new[] { 30.0, 75, 120, 300 })
                {
                    double h = 1e-4, w = 2 * Math.PI / Fs;
                    double dphi = Math.IEEERemainder(f.Response(hz + h, Fs).Phase - f.Response(hz - h, Fs).Phase, 2 * Math.PI);
                    Check.Close(-dphi / (2 * h * w), f.GroupDelay(hz, Fs), 1e-4 * Math.Max(1, Math.Abs(f.GroupDelay(hz, Fs))), $"{hz} Hz");
                }
        }

        public void Iir_Chebyshev2StopBandNeverRisesAboveTheAttenuation()
        {
            foreach (int order in new[] { 2, 3, 6, 9 })
            {
                var f = IirFilter.Chebyshev2LowPass(order, 50, 100, Fs);
                Check.Close(-50, f.MagnitudeDb(100, Fs), 1e-6, $"order {order}: −rs exactly at the stop edge");
                for (double hz = 100; hz < Fs / 2; hz += 0.5) Check.True(f.MagnitudeDb(hz, Fs) <= -50 + 1e-6, $"order {order} at {hz} Hz");
                double previous = 0;
                for (double hz = 0; hz <= 60; hz += 0.5)
                {
                    double db = f.MagnitudeDb(hz, Fs);
                    Check.True(db <= previous + 1e-9, $"order {order}: passband must fall monotonically ({hz} Hz)");
                    previous = db;
                }
            }
        }

        public void Iir_EllipticMeetsBothRippleAndAttenuation()
        {
            foreach (int order in new[] { 2, 3, 5, 8 })
            {
                var f = IirFilter.EllipticLowPass(order, 0.5, 60, 100, Fs);
                for (double hz = 0; hz <= 100; hz += 0.25)
                {
                    double db = f.MagnitudeDb(hz, Fs);
                    Check.True(db <= 1e-9 && db >= -0.5 - 1e-9, $"order {order} passband {hz} Hz: {db}");
                }
                Check.Close(-0.5, f.MagnitudeDb(100, Fs), 1e-6);
                // Steepest of all: an order-5 elliptic is down 60 dB long before an order-5 Butterworth is.
                if (order == 5)
                {
                    double stopEdge = 100;
                    while (f.MagnitudeDb(stopEdge, Fs) > -60) stopEdge += 0.5;
                    for (double hz = stopEdge; hz < Fs / 2; hz += 0.5) Check.True(f.MagnitudeDb(hz, Fs) <= -60 + 1e-6, $"stop band at {hz} Hz");
                    double butterDb = IirFilter.ButterworthLowPass(5, 100, Fs).MagnitudeDb(stopEdge, Fs);
                    Check.True(butterDb > -30, $"the Butterworth manages only {butterDb:F1} dB at the elliptic stop edge {stopEdge} Hz");
                }
            }
        }

        public void Iir_BesselPreservesStepShapeAndDelay()
        {
            // A step through an order-6 Bessel barely overshoots; the same-order Butterworth rings by over 10%.
            double Overshoot(IirFilter f)
            {
                double peak = 0;
                for (int i = 0; i < 2000; i++) peak = Math.Max(peak, f.Process(1));
                return peak - 1;
            }
            Check.True(Overshoot(IirFilter.BesselLowPass(6, 20, Fs)) < 0.01, "Bessel overshoot");
            Check.True(Overshoot(IirFilter.ButterworthLowPass(6, 20, Fs)) > 0.1, "Butterworth overshoot");

            // Delay normalisation: flat group delay of 1/(2π·fc) seconds through the passband (cutoff ≪ Nyquist so the
            // bilinear warping is negligible), varying by under 1% across it — far flatter than the Butterworth.
            const double fc = 10;
            var bessel = IirFilter.BesselLowPass(5, fc, Fs, BesselNormalization.Delay);
            double expected = Fs / (2 * Math.PI * fc);
            foreach (double hz in new[] { 0.1, 2, 5, 8 })
                Check.Close(expected, bessel.GroupDelay(hz, Fs), 0.01 * expected, $"{hz} Hz");
            var butter = IirFilter.ButterworthLowPass(5, fc, Fs);
            Check.True(Math.Abs(butter.GroupDelay(8, Fs) / butter.GroupDelay(0.1, Fs) - 1) > 0.3, "Butterworth delay is not flat");

            // Magnitude normalisation: −3 dB at the cutoff.
            foreach (int order in new[] { 1, 2, 5, 10, 20 })
                Check.Close(-3.0103, IirFilter.BesselLowPass(order, 40, Fs, BesselNormalization.Magnitude).MagnitudeDb(40, Fs), 1e-6, $"order {order}");
        }

        public void Iir_NewFamiliesRejectBadArguments()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.Chebyshev2LowPass(3, 0, 100, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.EllipticLowPass(3, 0, 40, 100, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.EllipticLowPass(3, 3, 2, 100, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.EllipticLowPass(26, 1, 40, 100, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.BesselLowPass(26, 100, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.BesselLowPass(3, 100, Fs, (BesselNormalization)7));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.BesselBandPass(3, 100, 600, Fs));
        }

        public void Iir_ResponseMatchesSciPy()
        {
            foreach (var (d, f, re, im) in FilterReferenceData.IirResponse)
            {
                Complex h = Design(d).Response(f, Fs);
                double expected = new Complex(re, im).Magnitude;
                Check.True((h - new Complex(re, im)).Magnitude <= 1e-12 + 1e-10 * expected, $"design {d} at {f} Hz: {h} vs ({re}, {im})");
            }
        }

        public void Iir_SectionCountsMatchOrder()
        {
            for (int d = 0; d < FilterReferenceData.IirDesigns.Length; d++)
            {
                var (_, band, order, _, _, _, _, _) = FilterReferenceData.IirDesigns[d];
                int poles = band == "BandPass" || band == "BandStop" ? 2 * order : order;
                Check.Equal((poles + 1) / 2, Design(d).Sections.Count, $"design {d}");
            }
        }

        public void Iir_FilterAndFiltFiltMatchSciPy()
        {
            var x = TestSignal();
            for (int d = 0; d < FilterReferenceData.IirDesigns.Length; d++)
            {
                var filter = Design(d);
                var causal = filter.Filter(x);
                var zeroPhase = filter.FiltFilt(x);
                foreach (var row in FilterReferenceData.IirOutputs.Where(r => r.Design == d))
                {
                    Check.Close(row.Causal, causal[row.Index], 1e-9 * Math.Max(1, Math.Abs(row.Causal)), $"sosfilt design {d} [{row.Index}]");
                    Check.Close(row.ZeroPhase, zeroPhase[row.Index], 1e-9 * Math.Max(1, Math.Abs(row.ZeroPhase)), $"sosfiltfilt design {d} [{row.Index}]");
                }
            }
        }

        public void Iir_ButterworthIsMinus3DbAtCutoffAndFlatInPassband()
        {
            foreach (int order in new[] { 1, 2, 3, 4, 7, 10 })
            {
                var lp = IirFilter.ButterworthLowPass(order, 100, Fs);
                Check.Close(-3.0103, lp.MagnitudeDb(100, Fs), 1e-3, $"order {order}");
                Check.Close(0, lp.MagnitudeDb(0, Fs), 1e-9);
                Check.True(lp.MagnitudeDb(20, Fs) > -0.01 || order == 1, $"order {order} not flat");
                var hp = IirFilter.ButterworthHighPass(order, 100, Fs);
                Check.Close(-3.0103, hp.MagnitudeDb(100, Fs), 1e-3);
                Check.Close(0, hp.MagnitudeDb(Fs / 2, Fs), 1e-9);
            }
        }

        public void Iir_Chebyshev1RippleStaysWithinBand()
        {
            foreach (int order in new[] { 2, 3, 5, 6 })
            {
                var f = IirFilter.Chebyshev1LowPass(order, 1.0, 100, Fs);
                double min = double.MaxValue, max = double.MinValue;
                for (double hz = 0; hz <= 100; hz += 0.25)
                {
                    double db = f.MagnitudeDb(hz, Fs);
                    min = Math.Min(min, db);
                    max = Math.Max(max, db);
                }
                Check.True(max <= 1e-9 && min >= -1.0 - 1e-9, $"order {order}: passband [{min}, {max}] dB");
                Check.Close(-1.0, f.MagnitudeDb(100, Fs), 1e-6, "gain at the edge is -ripple");
            }
        }

        public void Iir_ProcessMatchesFilterAndResetClears()
        {
            var x = TestSignal();
            var f = IirFilter.ButterworthBandPass(3, 20, 200, Fs);
            var batch = f.Filter(x);
            for (int i = 0; i < x.Length; i++) Check.Close(batch[i], f.Process(x[i]), 1e-12, $"[{i}]");
            f.Reset();
            Check.Close(batch[0], f.Process(x[0]), 1e-15);
        }

        public void Iir_ResetToValueRemovesStartupTransient()
        {
            var f = IirFilter.ButterworthLowPass(4, 10, Fs);
            f.Reset(42);
            for (int i = 0; i < 100; i++) Check.Close(42, f.Process(42), 1e-9, $"[{i}]");
        }

        public void Iir_FiltFiltHasNoPhaseShift()
        {
            var x = new double[2000];
            for (int i = 0; i < x.Length; i++) x[i] = Math.Sin(2 * Math.PI * 5 * i / Fs);
            var y = IirFilter.ButterworthLowPass(4, 50, Fs).FiltFilt(x);
            for (int i = 500; i < 1500; i++) Check.Close(x[i], y[i], 1e-4, $"[{i}]");    // 5 Hz is deep in the passband
            var causal = IirFilter.ButterworthLowPass(4, 50, Fs).Filter(x);
            Check.True(Math.Abs(causal[1000] - x[1000]) > 0.01, "the causal pass is delayed");
        }

        public void Iir_RejectsBadArguments()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.ButterworthLowPass(0, 10, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.ButterworthLowPass(41, 10, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.ButterworthLowPass(2, 500, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.ButterworthLowPass(2, 0, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.ButterworthLowPass(2, double.NaN, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.ButterworthLowPass(2, 10, double.PositiveInfinity));
            Check.Throws<ArgumentException>(() => IirFilter.ButterworthBandPass(2, 200, 100, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.Chebyshev1LowPass(2, 0, 10, Fs));
            Check.Throws<ArgumentException>(() => new IirFilter());
            Check.Throws<ArgumentException>(() => new IirFilter(new Biquad[] { null! }));
            Check.Throws<ArgumentException>(() => new Biquad(double.NaN, 0, 0, 0, 0));
            var f = IirFilter.ButterworthLowPass(4, 10, Fs);
            Check.Throws<ArgumentException>(() => f.FiltFilt(new double[15]));               // default pad is 3 × (2 sections · 2 + 1) = 15
            Check.Equal(16, f.FiltFilt(new double[16]).Length);
            Check.Equal(5, f.FiltFilt(new double[] { 1, 2, 3, 4, 5 }, padLength: 2).Length);
            Check.Throws<ArgumentOutOfRangeException>(() => f.FiltFilt(new double[50], padLength: -1));
        }

        // ---- Biquad cookbook properties ---------------------------------------------------------------------------------

        public void Biquad_CookbookShapes()
        {
            const double f0 = 1000, fs = 48000;
            var lp = Biquad.LowPass(fs, f0, 2);
            Check.Close(1, lp.Response(0, fs).Magnitude, 1e-12);
            Check.Close(0, lp.Response(fs / 2, fs).Magnitude, 1e-12);
            Check.Close(2, lp.Response(f0, fs).Magnitude, 1e-9, "|H(f0)| = Q");
            var hp = Biquad.HighPass(fs, f0);
            Check.Close(0, hp.Response(0, fs).Magnitude, 1e-12);
            Check.Close(1, hp.Response(fs / 2, fs).Magnitude, 1e-12);
            Check.Close(Math.Sqrt(0.5), hp.Response(f0, fs).Magnitude, 1e-9);
            var bp = Biquad.BandPass(fs, f0, 5);
            Check.Close(1, bp.Response(f0, fs).Magnitude, 1e-12);
            Check.True(bp.Response(f0 * 3, fs).Magnitude < 0.2);
            var notch = Biquad.Notch(fs, 60, 30);
            Check.Close(0, notch.Response(60, fs).Magnitude, 1e-9);
            Check.Close(1, notch.Response(1000, fs).Magnitude, 1e-3);
            var ap = Biquad.AllPass(fs, f0, 0.7);
            foreach (double f in new[] { 10.0, 500, 1000, 5000, 20000 }) Check.Close(1, ap.Response(f, fs).Magnitude, 1e-12);
            Check.Close(Math.PI, Math.Abs(ap.Response(f0, fs).Phase), 1e-9, "-180° at f0");
            var peak = Biquad.Peaking(fs, f0, 1, 6);
            Check.Close(6, 20 * Math.Log10(peak.Response(f0, fs).Magnitude), 1e-9);
            Check.Close(0, 20 * Math.Log10(peak.Response(0, fs).Magnitude), 1e-9);
            var ls = Biquad.LowShelf(fs, f0, -9);
            Check.Close(-9, 20 * Math.Log10(ls.Response(0, fs).Magnitude), 1e-9);
            Check.Close(0, 20 * Math.Log10(ls.Response(fs / 2, fs).Magnitude), 1e-9);
            Check.Close(-4.5, 20 * Math.Log10(ls.Response(f0, fs).Magnitude), 1e-9, "half the gain at the shelf frequency");
            var hs = Biquad.HighShelf(fs, f0, 12);
            Check.Close(0, 20 * Math.Log10(hs.Response(0, fs).Magnitude), 1e-9);
            Check.Close(12, 20 * Math.Log10(hs.Response(fs / 2, fs).Magnitude), 1e-9);
        }

        public void Biquad_SecondOrderButterworthMatchesDesign()
        {
            var cookbook = new IirFilter(Biquad.LowPass(Fs, 50));
            var designed = IirFilter.ButterworthLowPass(2, 50, Fs);
            foreach (double f in new[] { 0.0, 10, 50, 120, 400 })
                Check.True((cookbook.Response(f, Fs) - designed.Response(f, Fs)).Magnitude < 1e-12, $"{f} Hz");
        }

        public void Biquad_RejectsBadArguments()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Biquad.LowPass(48000, 24000));
            Check.Throws<ArgumentOutOfRangeException>(() => Biquad.LowPass(48000, 1000, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => Biquad.LowPass(0, 1000));
            Check.Throws<ArgumentOutOfRangeException>(() => Biquad.Peaking(48000, 1000, 1, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => Biquad.LowShelf(48000, 1000, 24, slope: 5));
        }

        // ---- FIR -------------------------------------------------------------------------------------------------------

        public void Fir_TapsMatchSciPyFirwin()
        {
            foreach (var (band, taps, f1, f2, window, beta, expected) in FilterReferenceData.FirDesigns)
            {
                var w = (FirWindow)Enum.Parse(typeof(FirWindow), window);
                FirFilter f;
                switch (band)
                {
                    case "LowPass": f = FirFilter.LowPass(taps, f1, Fs, w, beta); break;
                    case "HighPass": f = FirFilter.HighPass(taps, f1, Fs, w, beta); break;
                    case "BandPass": f = FirFilter.BandPass(taps, f1, f2, Fs, w, beta); break;
                    default: f = FirFilter.BandStop(taps, f1, f2, Fs, w, beta); break;
                }
                Check.Equal(expected.Length, f.Taps.Count);
                for (int i = 0; i < expected.Length; i++) Check.Close(expected[i], f.Taps[i], 1e-14, $"{band} {taps} {window} [{i}]");
            }
        }

        public void Fir_KaiserParametersMatchSciPy()
        {
            foreach (var (a, width, taps, beta) in FilterReferenceData.Kaiser)
            {
                var (n, b) = FirFilter.KaiserParameters(a, width, Fs);
                Check.Equal(taps, n, $"{a} dB");
                Check.Close(beta, b, 1e-12);
            }
        }

        public void Fir_KaiserDesignMeetsItsSpecification()
        {
            // 60 dB of rejection beyond 120 Hz with the passband edge at 100 Hz: cutoff in the middle of the transition.
            var (taps, beta) = FirFilter.KaiserParameters(60, 20, Fs);
            if (taps % 2 == 0) taps++;
            var f = FirFilter.LowPass(taps, 110, Fs, FirWindow.Kaiser, beta);
            for (double hz = 120; hz < Fs / 2; hz += 0.5) Check.True(f.MagnitudeDb(hz, Fs) < -59, $"{hz} Hz: {f.MagnitudeDb(hz, Fs)} dB");
            for (double hz = 0; hz <= 100; hz += 0.5) Check.True(Math.Abs(f.MagnitudeDb(hz, Fs)) < 0.02, $"{hz} Hz");
        }

        public void Fir_ProcessMatchesFilterAndAlignedRemovesDelay()
        {
            var x = TestSignal();
            var f = FirFilter.LowPass(21, 80, Fs);
            var batch = f.Filter(x);
            for (int i = 0; i < x.Length; i++) Check.Close(batch[i], f.Process(x[i]), 1e-12, $"[{i}]");
            Check.Equal(10.0, f.DelaySamples);
            var aligned = f.FilterAligned(x);
            for (int i = 0; i + 10 < x.Length; i++) Check.Close(batch[i + 10], aligned[i], 1e-12, $"aligned [{i}]");
            // Near the end the aligned output sees zeros past the last sample, exactly like convolution "same".
            int n = x.Length;
            double tail = 0;
            for (int k = 0; k < 21; k++) { int j = n - 1 + 10 - k; if (j < n) tail += f.Taps[k] * x[j]; }
            Check.Close(tail, aligned[n - 1], 1e-12);
            f.Reset();
            Check.Close(batch[0], f.Process(x[0]), 1e-15);
            Check.Throws<InvalidOperationException>(() => FirFilter.LowPass(20, 80, Fs).FilterAligned(x));
        }

        public void Fir_LinearPhase()
        {
            var f = FirFilter.BandPass(41, 50, 150, Fs);
            for (int i = 0; i < 41; i++) Check.Close(f.Taps[i], f.Taps[40 - i], 1e-15, "symmetric taps");
            // Phase = −2π·f·delay/fs (mod π for sign flips) throughout the passband.
            foreach (double hz in new[] { 70.0, 100, 130 })
            {
                double phase = f.Response(hz, Fs).Phase, expected = -2 * Math.PI * hz * 20 / Fs;
                double diff = Math.IEEERemainder(phase - expected, Math.PI);
                Check.Close(0, diff, 1e-9, $"{hz} Hz");
            }
        }

        public void Fir_RejectsBadArguments()
        {
            Check.Throws<ArgumentException>(() => FirFilter.HighPass(30, 100, Fs));
            Check.Throws<ArgumentException>(() => FirFilter.BandStop(30, 100, 200, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.LowPass(0, 100, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.LowPass(11, 600, Fs));
            Check.Throws<ArgumentException>(() => FirFilter.BandPass(11, 200, 100, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.LowPass(11, 100, Fs, FirWindow.Kaiser, -1));
            Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.LowPass(11, 100, Fs, (FirWindow)99));
            Check.Throws<ArgumentException>(() => new FirFilter(new double[0]));
            Check.Throws<ArgumentException>(() => new FirFilter(new[] { 1.0, double.NaN }));
            Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.KaiserParameters(5, 10, Fs));
            Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.KaiserParameters(60, 0, Fs));
        }
    }
}
