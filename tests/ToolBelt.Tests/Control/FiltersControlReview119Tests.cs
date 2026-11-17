using System;
using System.Linq;
using System.Numerics;
using ToolBelt.Control;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    // Regressions from review round 119 (filters & control, seventh pass).
    public sealed class FiltersControlReview119Tests
    {
        private static void Rel(double expected, double actual, double rel, string what)
            => Check.True(Math.Abs(actual - expected) <= rel * Math.Abs(expected), $"{what}: expected {expected:R}, got {actual:R}");

        // DC gains 2e200, 2e200 and 2e-300: the whole cascade's gain is 8e100, but the running product passes 4e400.
        private static IirFilter Unbalanced() =>
            new IirFilter(new Biquad(1e200, 0, 0, -0.5, 0), new Biquad(1e200, 0, 0, -0.5, 0), new Biquad(1e-300, 0, 0, -0.5, 0));

        // ---------- PidController ----------

        // Setpoint 1e308 and measurement −1e308 overflow the error to ∞ while error·dt (2e298) fits: the integral never
        // moved without limits, and with limits windup acted on the ∞ output and parked the integral at the limit.
        public void Pid_IntegralMoves_WhenTheErrorOverflows()
        {
            var free = new PidController(0, 1e-300, 0) { Setpoint = 1e308 };
            double o = 0;
            for (int i = 0; i < 3; i++) o = free.Update(-1e308, 1e-10);
            Rel(0.06, o, 1e-12, "unlimited, 3 steps");
            for (int i = 3; i < 100; i++) o = free.Update(-1e308, 1e-10);
            Rel(2, o, 1e-12, "unlimited, 100 steps");

            var ten = new PidController(0, 1e-300, 0, -10, 10) { Setpoint = 1e308 };
            for (int i = 0; i < 3; i++) o = ten.Update(-1e308, 1e-10);
            Rel(0.06, o, 1e-12, "limits ±10");

            var one = new PidController(0, 1e-300, 0, -1, 1) { Setpoint = 1e308 };
            Rel(0.02, one.Update(-1e308, 1e-10), 1e-12, "limits ±1");

            var down = new PidController(0, 1e-300, 0, -10, 10) { Setpoint = -1e308 };
            for (int i = 0; i < 3; i++) o = down.Update(1e308, 1e-10);
            Rel(-0.06, o, 1e-12, "negative error");
        }

        // ---------- SlewRateLimiter ----------

        // target − Value and rate·dt both overflowed to ∞, and ∞ > ∞ is false, so the output jumped to the target.
        public void Slew_LimitsWhenTheGapAndTheStepBothOverflow()
        {
            var up = new SlewRateLimiter(1e308);
            up.Reset(-1.7e308);
            Rel(3e307, up.Update(1.7e308, 2), 1e-12, "rising");
            Check.True(up.IsLimiting, "rising IsLimiting");
            Check.Equal(1.7e308, up.Update(1.7e308, 2));
            Check.True(!up.IsLimiting, "rising reached");

            var down = new SlewRateLimiter(1e308);
            down.Reset(1.7e308);
            Rel(-3e307, down.Update(-1.7e308, 2), 1e-12, "falling");
            Check.True(down.IsLimiting, "falling IsLimiting");

            var unlimited = new SlewRateLimiter(double.PositiveInfinity);
            unlimited.Reset(-1.7e308);
            Check.Equal(1.7e308, unlimited.Update(1.7e308, 2));
            Check.True(!unlimited.IsLimiting, "unlimited");
        }

        // ---------- IirFilter steady state and response ----------

        // The product of the section gains overflowed before the rescale could help: Reset(1) left NaN state, so every
        // output was NaN, and FiltFilt of a constant was NaN everywhere.
        public void Iir_SteadyState_SurvivesAnOverflowingGainProduct()
        {
            var f = Unbalanced();
            f.Reset(1);
            for (int i = 0; i < 5; i++) Rel(8e100, f.Process(1), 1e-14, "Reset(1) then Process(1), step " + i);

            var y = Unbalanced().FiltFilt(Enumerable.Repeat(1.0, 60).ToArray());
            foreach (double v in y) Rel(6.4e201, v, 1e-13, "FiltFilt of a constant");
        }

        // The other way: the product underflowed to 0, so the last section started from zero (a start-up transient).
        public void Iir_SteadyState_SurvivesAnUnderflowingGainProduct()
        {
            var f = new IirFilter(new Biquad(1e-300, 0, 0, -0.5, 0), new Biquad(1e-300, 0, 0, -0.5, 0), new Biquad(1e300, 0, 0, -0.5, 0));
            f.Reset(1e300);
            for (int i = 0; i < 5; i++) Rel(8, f.Process(1e300), 1e-14, "step " + i);
        }

        // The complex product over the sections overflowed to (∞, NaN) where |H| = 8e100.
        public void Iir_Response_SurvivesAnOverflowingProduct()
        {
            var f = Unbalanced();
            Complex dc = f.Response(0, 1000);
            Rel(8e100, dc.Real, 1e-14, "DC real");
            Check.Close(0, dc.Imaginary, 1e86, "DC imaginary");
            foreach (double hz in new[] { 10.0, 123.4, 400, 500 })
            {
                Complex h = f.Response(hz, 1000);
                Check.True(!double.IsNaN(h.Real) && !double.IsInfinity(h.Real) && !double.IsNaN(h.Imaginary) && !double.IsInfinity(h.Imaginary), "finite at " + hz);
                Check.Close(f.MagnitudeDb(hz, 1000), 20 * Math.Log10(h.Magnitude), 1e-9, "|H| agrees with MagnitudeDb at " + hz);
            }

            // A balanced design is unchanged against the plain product.
            var bw = IirFilter.ButterworthLowPass(8, 0.2, 1);
            Complex plain = bw.Sections.Aggregate(Complex.One, (acc, s) => acc * s.Response(0.13, 1));
            Complex got = bw.Response(0.13, 1);
            Check.Close(plain.Real, got.Real, 1e-14);
            Check.Close(plain.Imaginary, got.Imaginary, 1e-14);
        }

        // ---------- argument blame ----------

        // At the slope limit the shelf's poles sit on the unit circle; that fell through to the gain check, which blamed
        // gainDb. Every rejection near the limit must name slope.
        public void Shelf_AtTheSlopeLimit_BlamesSlope()
        {
            double gainDb = 40 * Math.Log10(2);                                    // A = 2: the limit is slope 5
            foreach (Func<double, Biquad> make in new Func<double, Biquad>[] { sl => Biquad.LowShelf(48000, 1000, gainDb, sl), sl => Biquad.HighShelf(48000, 1000, gainDb, sl) })
            {
                var ex = Check.Throws<ArgumentOutOfRangeException>(() => make(5));
                Check.Equal("slope", ex.ParamName);
                for (int k = -40; k <= 40; k++)
                {
                    double slope = 5 + k * 4.440892098500626e-16;
                    try { make(slope); }
                    catch (ArgumentOutOfRangeException e) { Check.Equal("slope", e.ParamName, "slope " + slope.ToString("R")); }
                }
            }
        }

        public void StepAnalyze_BadRiseHigh_BlamesRiseHigh()
        {
            var y = Enumerable.Range(0, 50).Select(i => 1 - Math.Exp(-i / 5.0)).ToArray();
            foreach (double high in new[] { 1.5, double.NaN, 0.05 })
            {
                var ex = Check.Throws<ArgumentOutOfRangeException>(() => StepResponse.Analyze(y, 1.0, riseLow: 0.1, riseHigh: high));
                Check.Equal("riseHigh", ex.ParamName, "riseHigh " + high);
            }
            foreach (double low in new[] { -0.1, double.NaN, 1 })
            {
                var ex = Check.Throws<ArgumentOutOfRangeException>(() => StepResponse.Analyze(y, 1.0, riseLow: low, riseHigh: 0.9));
                Check.Equal("riseLow", ex.ParamName, "riseLow " + low);
            }
        }

        public void NonFiniteTime_BlamesTimes()
        {
            var y = Enumerable.Range(0, 50).Select(i => 1 - Math.Exp(-i / 5.0)).ToArray();
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity })
            {
                var t = Enumerable.Range(0, 50).Select(i => (double)i).ToArray();
                t[2] = bad;
                Check.Equal("times", Check.Throws<ArgumentException>(() => StepResponse.Analyze(t, y)).ParamName);
                Check.Equal("times", Check.Throws<ArgumentException>(() => FopdtModel.FitStep(t, y, 1)).ParamName);
            }
        }
    }
}
