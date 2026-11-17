using System;
using System.Linq;
using System.Numerics;
using ToolBelt.Control;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    // Regressions from review round 107 (filters & control, sixth pass).
    public sealed class FiltersControlReview107Tests
    {
        private static double Ulps(double x, long k) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + k);

        private static double[] Concat(double small, double large, int n)
            => Enumerable.Repeat(small, n).Concat(Enumerable.Repeat(large, n)).ToArray();

        // ---------- rescaling after an overflow ----------

        // One output past double.MaxValue redid the whole buffer at 2^-64, so outputs before it (which never overflowed)
        // lost their low bits, or became 0: the causal Filter depended on later samples.
        public void Filter_AnOverflowLater_LeavesEarlierOutputsExact()
        {
            var iir = IirFilter.ButterworthLowPass(4, 0.1, 1);
            var fir = FirFilter.LowPass(31, 0.1, 1);
            foreach (double small in new[] { 1e-300, 1e-310, 3.7 })
            {
                var x = Concat(small, 1.7e308, 50);
                var head = x.Take(50).ToArray();
                double[] iirAlone = iir.Filter(head), iirAll = iir.Filter(x);
                double[] firAlone = fir.Filter(head), firAll = fir.Filter(x);
                for (int i = 0; i < 50; i++)
                {
                    Check.Equal(iirAlone[i], iirAll[i], "IIR " + small + " at " + i);
                    Check.Equal(firAlone[i], firAll[i], "FIR " + small + " at " + i);
                }
                Check.True(iirAll.Skip(50).All(v => !double.IsNaN(v)), "IIR NaN after the overflow");
                Check.True(firAll.Skip(50).All(v => !double.IsNaN(v)), "FIR NaN after the overflow");
            }
        }

        // The non-causal passes keep a finite unscaled output too: it never saw an overflow, so it is exact.
        public void FiltFiltAndAligned_KeepOutputsThatNeverOverflowed()
        {
            var iir = IirFilter.ButterworthLowPass(2, 0.2, 1);
            var fir = FirFilter.LowPass(11, 0.1, 1);
            var x = Enumerable.Repeat(1e-300, 4000).Concat(Enumerable.Repeat(1.7e308, 20)).ToArray();
            var head = x.Take(300).ToArray();
            double[] alone = fir.FilterAligned(head), all = fir.FilterAligned(x);
            for (int i = 0; i < 290; i++) Check.Equal(alone[i], all[i], "FIR aligned at " + i);
            // FiltFilt's backward pass carries the burst to every sample, but it decays below 1e-300 within about 2000.
            double[] ff = iir.FiltFilt(x);
            Near(1e-300, ff[10], 1e-9, "FiltFilt far before the burst");
        }

        private static void Near(double expected, double actual, double rel, string message)
            => Check.Close(expected, actual, rel * Math.Abs(expected), message);

        // ---------- Process: the shift comes back down ----------

        // After one burst near double.MaxValue the streaming shift stayed raised for good, so every later small signal was
        // flushed to 0 (an impulse of 1e-300 long after the ringing died out came out as 0). Filter streams the same way.
        public void Process_AfterABurst_FiltersSmallSignalsAgain()
        {
            var make = new Func<IirFilter>(() => IirFilter.ButterworthLowPass(2, 0.2, 1));
            var filter = make();
            const int quiet = 20000;
            var x = new double[50 + quiet + 10];
            for (int i = 0; i < 50; i++) x[i] = 1.7e308;
            x[50 + quiet] = 1e-300;
            var streamed = x.Select(filter.Process).ToArray();
            var batch = make().Filter(x);
            var fresh = make();
            for (int k = 0; k < 6; k++)
            {
                double expected = fresh.Process(k == 0 ? 1e-300 : 0);
                Near(expected, streamed[50 + quiet + k], 1e-12, "Process at +" + k);
                Near(expected, batch[50 + quiet + k], 1e-12, "Filter at +" + k);
            }
            // A constant tiny signal after the burst settles to itself, as on a fresh filter.
            var settle = make();
            for (int i = 0; i < 50; i++) settle.Process(1.7e308);
            double last = 0;
            for (int i = 0; i < quiet; i++) last = settle.Process(1e-295);
            Near(1e-295, last, 1e-9, "constant 1e-295");
        }

        // ---------- frequency responses alias ----------

        // A frequency far past the sample rate lost its phase in the ratio (2^60 Hz at 1 Hz is DC) or overflowed to NaN.
        public void Responses_AliasFarPastTheSampleRate()
        {
            var iir = IirFilter.ButterworthLowPass(3, 0.1, 1);
            var fir = FirFilter.LowPass(21, 0.1, 1);
            var q = Biquad.LowPass(1, 0.1);
            double f = Math.Pow(2, 60);
            Complex dc = iir.Response(0, 1), far = iir.Response(f, 1);
            Check.Close(dc.Real, far.Real, 1e-12, "IIR real");
            Check.Close(dc.Imaginary, far.Imaginary, 1e-12, "IIR imaginary");
            Check.Close(iir.MagnitudeDb(0, 1), iir.MagnitudeDb(f, 1), 1e-9, "IIR dB");
            Check.Close(iir.GroupDelay(0, 1), iir.GroupDelay(f, 1), 1e-9, "IIR delay");
            Check.Close(fir.Response(0, 1).Real, fir.Response(f, 1).Real, 1e-12, "FIR");
            Check.Close(q.Response(0, 1).Real, q.Response(f, 1).Real, 1e-12, "biquad");
            // 3.25 cycles is a quarter cycle.
            Check.Close(iir.MagnitudeDb(0.25, 1), iir.MagnitudeDb(3.25, 1), 1e-9, "3.25");
            Complex r = iir.Response(1e300, 1e-10);
            Check.True(!double.IsNaN(r.Real) && !double.IsNaN(r.Imaginary), "1e300 Hz at 1e-10 Hz was NaN");
            double reduced = Math.IEEERemainder(1e300, 1e-10);
            Check.Close(iir.Response(reduced, 1e-10).Real, r.Real, 1e-12, "reduced");
            Check.True(!double.IsNaN(fir.Response(1e300, 1e-10).Real), "FIR NaN");
        }

        // ---------- Biquad: an extreme Q is named ----------

        // Q = 1e14 at 1 kHz of 48 kHz puts the poles on the circle; the frequency, mid-band, was blamed.
        public void Biquad_ExtremeQ_NamesQ()
        {
            foreach (double q in new[] { 1e12, 1e14, 1e17 })
            {
                Check.Equal("q", Check.Throws<ArgumentOutOfRangeException>(() => Biquad.Notch(48000, 1000, q)).ParamName, "Notch " + q);
                Check.Equal("q", Check.Throws<ArgumentOutOfRangeException>(() => Biquad.BandPass(48000, 1000, q)).ParamName, "BandPass " + q);
                Check.Equal("q", Check.Throws<ArgumentOutOfRangeException>(() => Biquad.LowPass(48000, 1000, q)).ParamName, "LowPass " + q);
                Check.Equal("q", Check.Throws<ArgumentOutOfRangeException>(() => Biquad.Peaking(48000, 1000, q, 6)).ParamName, "Peaking " + q);
            }
            // A frequency too close to DC is still the frequency's fault.
            Check.Equal("frequency", Check.Throws<ArgumentOutOfRangeException>(() => Biquad.LowPass(1e9, 1)).ParamName);
        }

        // ---------- ButterworthOrder: band edges a few ulps apart ----------

        // Rounding put the selectivity just below 1, the estimate went negative and Math.Max made it order 1, which gives
        // −1 dB where −40 dB was asked. Chebyshev and elliptic refuse such specs; Butterworth must too.
        public void ButterworthOrder_UlpCloseBandEdges_AreRefused()
        {
            var rng = new Random(107);
            int refused = 0;
            for (int t = 0; t < 4000; t++)
            {
                double a = 0.05 + 0.2 * rng.NextDouble(), b = a + 0.01 + 0.2 * rng.NextDouble();
                long k1 = rng.Next(1, 8), k2 = rng.Next(1, 8);
                bool bandPass = t % 2 == 0;
                double sLow = bandPass ? Ulps(a, -k1) : Ulps(a, k1), sHigh = bandPass ? Ulps(b, k2) : Ulps(b, -k2);
                bool chebyshevRefuses;
                try { IirFilter.Chebyshev1Order(a, b, sLow, sHigh, 1, 40, 1); chebyshevRefuses = false; }
                catch (ArgumentException) { chebyshevRefuses = true; }
                if (!chebyshevRefuses) continue;
                refused++;
                double pl = a, ph = b, sl = sLow, sh = sHigh;
                Check.Throws<ArgumentException>(() => IirFilter.ButterworthOrder(pl, ph, sl, sh, 1, 40, 1), "trial " + t);
            }
            Check.True(refused > 0, "no trial reached a refused spec");
        }

        // ---------- KaiserParameters: subnormal sample rates ----------

        // sampleRate / 2 rounded for a subnormal rate (1.5e-323 / 2), so the same ratio gave a different tap count.
        public void KaiserParameters_SubnormalSampleRate_MatchesTheSameRatio()
        {
            double eps = double.Epsilon;
            foreach (double atten in new[] { 30.0, 60, 100 })
            {
                var normal = FirFilter.KaiserParameters(atten, 1, 3);
                var tiny = FirFilter.KaiserParameters(atten, eps, 3 * eps);
                Check.Equal(normal.Taps, tiny.Taps, "taps at " + atten);
                Check.Equal(normal.Beta, tiny.Beta, "beta at " + atten);
            }
        }

        // ---------- KalmanFilter1D, PidTuning ----------

        public void Kalman_Reset_ClearsLastGain()
        {
            var k = new KalmanFilter1D(0.1, 1);
            k.Update(3);
            Check.True(k.LastGain > 0);
            k.Reset(0, 1);
            Check.Equal(0.0, k.LastGain);
        }

        // With no dead time the default τc = θ is 0; the error named closedLoopTimeConstant, which was never passed.
        public void Simc_NoDeadTimeAndNoTimeConstant_NamesTheModel()
        {
            var m = new FopdtModel(2, 5, 0);
            Check.Equal("model", Check.Throws<ArgumentException>(() => PidTuning.Simc(m)).ParamName);
            Check.True(PidTuning.Simc(m, 1).Kp > 0, "an explicit τc still works");
        }
    }
}
