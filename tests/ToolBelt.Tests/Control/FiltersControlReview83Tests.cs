using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Control;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    // Regressions from review round 83 (filters & control, fifth pass).
    public sealed class FiltersControlReview83Tests
    {
        private static void Near(double expected, double actual, double rel, string? message = null)
            => Check.Close(expected, actual, rel * Math.Abs(expected), message);

        private static bool AllFinite(IEnumerable<double> values) => values.All(v => !double.IsNaN(v) && !double.IsInfinity(v));

        // The reference for a linear filter: the same input at a small scale, the output scaled back.
        private static void SameAsScaled(double[] expectedSmall, double[] actual, double scale, string what)
        {
            Check.True(actual.All(v => !double.IsNaN(v)), what + ": NaN output");
            for (int i = 0; i < actual.Length; i++)
            {
                double e = expectedSmall[i] * scale;     // infinite where the true output is past double.MaxValue
                if (double.IsInfinity(e)) Check.Equal(e, actual[i], what + " at " + i);
                else Check.Close(e, actual[i], 1e-9 * Math.Max(Math.Abs(e), scale), what + " at " + i);
            }
        }

        // ---------- IirFilter: section scaling ----------

        // Each partial product of the cascade peaks near the final peak. It was aimed at the unscaled peak, leaving every
        // intermediate signal about 1/|K| times the output (1e140 for a 40th-order low-pass at 1e-4·fs).
        public void SectionPartialPeaksSitAtTheFinalPeak()
        {
            var designs = new (string Name, IirFilter Filter)[]
            {
                ("BW LP 8 @ 0.005", IirFilter.ButterworthLowPass(8, 0.005, 1)),
                ("BW LP 40 @ 1e-4", IirFilter.ButterworthLowPass(40, 1e-4, 1)),
                ("BW BP 40", IirFilter.ButterworthBandPass(40, 0.01, 0.0101, 1)),
                ("Cheb1 HP 40 @ 0.49", IirFilter.Chebyshev1HighPass(40, 1, 0.49, 1)),
                ("Bessel LP 25 @ 0.001", IirFilter.BesselLowPass(25, 0.001, 1)),
                ("BW BS 40", IirFilter.ButterworthBandStop(40, 0.001, 0.49, 1)),
            };
            var grid = new List<double>();
            for (int i = 1; i < 2000; i++) grid.Add(0.5 * i / 2000);
            for (int i = 0; i <= 400; i++) grid.Add(0.5 * Math.Pow(10, -10.0 * i / 400));
            for (int i = 1; i <= 400; i++) grid.Add(0.5 - 0.5 * Math.Pow(10, -10.0 * i / 400));
            foreach (var (name, filter) in designs)
            {
                var sections = filter.Sections;
                double final = grid.Max(f => filter.MagnitudeDb(f, 1));
                for (int k = 1; k < sections.Count; k++)
                {
                    var prefix = new IirFilter(sections.Take(k));
                    double partial = grid.Max(f => prefix.MagnitudeDb(f, 1));
                    Check.True(partial <= final + 20, $"{name}: sections 1..{k} peak at {partial:F1} dB, the output at {final:F1} dB");
                }
            }
        }

        public void HighOrderLowPassFiltersALargeConstant()
        {
            var filter = IirFilter.ButterworthLowPass(40, 1e-4, 1);
            double[] small = filter.Filter(Enumerable.Repeat(1.0, 64).ToArray());
            SameAsScaled(small, filter.Filter(Enumerable.Repeat(1e200, 64).ToArray()), 1e200, "Filter");
        }

        // ---------- IirFilter: group delay next to unit-circle zeros ----------

        // The numerator's sums cancelled next to its zeros: a high-pass's delay near DC came out as −8e15 samples.
        public void GroupDelayIsSmoothNextToZerosOnTheCircle()
        {
            var hp = IirFilter.ButterworthHighPass(4, 1000, 48000);
            Near(hp.GroupDelay(0.5, 48000), hp.GroupDelay(1e-4, 48000), 1e-5, "high-pass near DC");
            Near(19.934, hp.GroupDelay(1e-4, 48000), 1e-3, "high-pass near DC");
            var lp = IirFilter.ButterworthLowPass(4, 1000, 48000);
            Near(lp.GroupDelay(24000 - 0.5, 48000), lp.GroupDelay(24000 - 1e-8, 48000), 1e-4, "low-pass near Nyquist");        }

        // ---------- IirFilter: overflow near double.MaxValue ----------

        // The odd extension x0 + (x0 − x[k]) passed double.MaxValue for a swing of ±1e308: every output was NaN or ∞.
        public void FiltFiltTakesFullScaleSwings()
        {
            var filter = IirFilter.ButterworthLowPass(2, 50, 1000);
            var alternating = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? 1.0 : -1.0).ToArray();
            SameAsScaled(filter.FiltFilt(alternating), filter.FiltFilt(alternating.Select(v => v * 1e308).ToArray()), 1e308, "alternating");
            var step = Enumerable.Range(0, 100).Select(i => i < 50 ? 1.0 : -1.0).ToArray();
            SameAsScaled(filter.FiltFilt(step), filter.FiltFilt(step.Select(v => v * 1e308).ToArray()), 1e308, "step");
        }

        // A finite input whose state overflowed (A1·y past double.MaxValue) left the streaming state NaN for good.
        public void ProcessRescalesInsteadOfOverflowing()
        {
            var big = IirFilter.ButterworthLowPass(2, 5, 1000);
            var small = IirFilter.ButterworthLowPass(2, 5, 1000);
            for (int i = 0; i < 200; i++)
            {
                double expected = small.Process(1) * 1e308, actual = big.Process(1e308);
                Check.True(!double.IsNaN(actual) && !double.IsInfinity(actual), "sample " + i + ": " + actual);
                Check.Close(expected, actual, 1e-9 * 1e308, "sample " + i);
            }
            // The rescaled state keeps filtering after the large samples, and Reset() returns it to unit scale.
            for (int i = 0; i < 2000; i++) Check.Close(small.Process(0) * 1e308, big.Process(0), 1e-9 * 1e308, "tail " + i);
            big.Reset();
            small.Reset();
            for (int i = 0; i < 20; i++) Check.Close(small.Process(1), big.Process(1), 1e-15);

            var eighth = IirFilter.ButterworthLowPass(8, 5, 1000);
            eighth.Reset(1e306);
            Near(1e306, eighth.Process(1e306), 1e-9, "Reset(1e306) then Process(1e306)");
            var filtered = eighth.Filter(Enumerable.Repeat(1e306, 200).ToArray());
            Check.True(AllFinite(filtered), "Filter of a constant 1e306");
        }

        public void ProcessOfAnInfiniteSampleStaysUntilReset()
        {
            var f = IirFilter.ButterworthLowPass(2, 5, 1000);
            f.Process(double.PositiveInfinity);
            for (int i = 0; i < 1000; i++) f.Process(1);
            Check.True(double.IsNaN(f.Process(1)), "an infinite sample is documented to stay");
            f.Reset();
            Check.True(!double.IsNaN(f.Process(1)));
        }

        // ---------- PidController ----------

        // P = +∞ and D = −∞ made the tentative output NaN: neither windup test fired and the integral took the whole
        // step (0 to 1e9) past the limit.
        public void WindupHoldsWhenPAndDOverflowWithOppositeSigns()
        {
            var pid = new PidController(1e300, 1, 1e300, -1, 1);
            pid.Update(-1.5e9, 1);
            pid.Update(-1e9, 1);
            pid.Setpoint = -1e9;                 // error 0 and no change: the output is ki · integral
            Check.Close(0, pid.Update(-1e9, 1), 1e-12);

            var reference = new PidController(1e290, 1, 1e290, -1, 1);
            reference.Update(-1.5e9, 1);
            reference.Update(-1e9, 1);
            reference.Setpoint = -1e9;
            Check.Close(0, reference.Update(-1e9, 1), 1e-12);
        }

        // ---------- FirFilter ----------

        // The running sum passed double.MaxValue (positive taps sum to 1.15) though the output fits.
        public void FirRunningSumDoesNotOverflowAFittingOutput()
        {
            var fir = FirFilter.LowPass(31, 100, 1000);
            var constant = Enumerable.Repeat(1.7e308, 80).ToArray();
            var ones = Enumerable.Repeat(1.0, 80).ToArray();
            SameAsScaled(fir.Filter(ones), fir.Filter(constant), 1.7e308, "Filter");
            SameAsScaled(fir.FilterAligned(ones), fir.FilterAligned(constant), 1.7e308, "FilterAligned");
            var streamed = new FirFilter(fir.Taps);
            var expected = new FirFilter(fir.Taps);
            for (int i = 0; i < 80; i++)
            {
                double e = expected.Process(1) * 1.7e308, a = streamed.Process(1.7e308);
                if (double.IsInfinity(e)) Check.Equal(e, a, "Process at " + i);
                else Check.Close(e, a, 1e-9 * 1.7e308, "Process at " + i);
            }
            // A true output past double.MaxValue is infinite, not NaN.
            var gain = new FirFilter(new[] { 1.0, 1.0 });
            Check.Equal(double.PositiveInfinity, gain.Filter(new[] { 1.7e308, 1.7e308 })[1]);
        }

        // ---------- Correctness: causal flush, deadband ties, lag cutoff, Schmitt ParamName ----------

        // Filter turned the subnormal flush on from sample 0 when any later sample was large, so a later 1.0 changed the
        // first outputs: Filter must agree with Process sample by sample.
        public void FilterFlushIsCausalLikeProcess()
        {
            var filter = IirFilter.ButterworthLowPass(2, 0.1, 1);
            var input = new[] { 1e-310, 0, 0, 1.0, 1e-310, 0 };
            var batch = filter.Filter(input);
            var streamed = IirFilter.ButterworthLowPass(2, 0.1, 1);
            for (int i = 0; i < input.Length; i++) Check.Equal(streamed.Process(input[i]), batch[i], "sample " + i);
            Check.True(batch[0] != 0, "a tiny first sample filters as sosfilt does");
            var alone = filter.Filter(new[] { 1e-310, 0, 0 });
            for (int i = 0; i < 3; i++) Check.Equal(alone[i], batch[i], "sample " + i + " ignores later input");
        }

        // −1 about 1e17 is 1e17 + 1 away; the rounded distance was exactly the half-width, so it read as inside.
        public void DeadbandDecidesTiesOnTheExactDistance()
        {
            Check.Equal(-1.0, Deadband.Apply(-1, 1e17, 1e17, continuous: false));
            Check.Equal(1e17, Deadband.Apply(1, 1e17, 1e17, continuous: false));       // 1e17 − 1 away: inside
            Check.Equal(1e17, Deadband.Apply(0, 1e17, 1e17, continuous: false));       // exactly on the edge: inside
            Check.Equal(3.0, Deadband.Apply(3, 1, 1, continuous: false));
            Check.Equal(1.0, Deadband.Apply(2, 1, 1, continuous: false));
            var filter = new DeadbandFilter(1e17);
            filter.Update(1e17);
            Check.True(filter.Update(-1), "a change of 1e17 + 1 is past the threshold");
            Check.Equal(-1.0, filter.Value);
            Check.False(filter.Update(1e17 - 16), "1e17 − 15 away from −1: inside");
            var near = new DeadbandFilter(1e17);
            near.Update(1e17);
            Check.False(near.Update(1), "1e17 − 1 away");
        }

        // 2π·cutoff overflowed past about 2.9e307, so the time constant came out 0 (a pass-through).
        public void LagFromAHugeCutoffKeepsItsTimeConstant()
        {
            foreach (double fc in new[] { 5e307, 1e308, double.MaxValue, 1.0, 1e-300 })
                Near(1 / (2 * Math.PI) / fc, FirstOrderLag.FromCutoff(fc).TimeConstant, 1e-15, "cutoff " + fc);
        }

        public void SchmittTriggerNamesTheNaNThreshold()
        {
            Check.Equal("highThreshold", Check.Throws<ArgumentException>(() => new SchmittTrigger(0, double.NaN)).ParamName);
            Check.Equal("lowThreshold", Check.Throws<ArgumentException>(() => new SchmittTrigger(double.NaN, 0)).ParamName);
            Check.Equal("highThreshold", Check.Throws<ArgumentException>(() => SchmittTrigger.Apply(new[] { 1.0 }, 0, double.NaN)).ParamName);
            Check.Equal("lowThreshold", Check.Throws<ArgumentException>(() => new SchmittTrigger(1, 0)).ParamName);
        }
    }
}
