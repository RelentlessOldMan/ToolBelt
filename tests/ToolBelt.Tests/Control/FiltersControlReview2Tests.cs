using System;
using System.Linq;
using ToolBelt.Control;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    // Regressions from review round 30 (filters & control, second pass).
    public sealed class FiltersControlReview2Tests
    {
        private static string ParamOf(Action action)
        {
            try { action(); }
            catch (ArgumentOutOfRangeException ex) { return ex.ParamName ?? "(null)"; }
            catch (Exception ex) { return ex.GetType().Name; }
            return "(no throw)";
        }

        // ---------- IirFilter steady state ----------

        public void Iir_HighPassAtATinyCutoffStartsSettled()
        {
            var f = IirFilter.ButterworthHighPass(2, 0.01, 1e6);
            f.Reset(3);
            Check.True(Math.Abs(f.Process(3)) < 1e-6, "was 3");
            double[] y = f.FiltFilt(Enumerable.Repeat(3.0, 200).ToArray());
            Check.True(y.All(v => Math.Abs(v) < 1e-6), y.Max().ToString("R"));
        }

        public void Iir_BandPassAtATinyCutoffStartsSettled()
        {
            var f = IirFilter.ButterworthBandPass(2, 0.01, 1000, 1e6);
            f.Reset(3);
            Check.True(Math.Abs(f.Process(3)) < 1e-6);
        }

        public void Iir_LowPassAtATinyCutoffStartsSettled()
        {
            var f = IirFilter.ButterworthLowPass(2, 1e-5, 1000);
            f.Reset(1);
            Check.True(Math.Abs(f.Process(1) - 1) < 0.1, "was 4e-14");
            double[] y = f.FiltFilt(Enumerable.Repeat(1.0, 3000).ToArray());
            Check.True(y.All(v => Math.Abs(v - 1) < 0.1), y.Min().ToString("R"));
        }

        // ---------- Biquad ----------

        public void Biquad_OverflowNamesQOrSlope()
        {
            Check.Equal("q", ParamOf(() => Biquad.Peaking(48000, 1000, 1e-300, 1000)));
            Check.Equal("q", ParamOf(() => Biquad.Peaking(48000, 1000, 1e-300, -1000)));
            Check.Equal("q", ParamOf(() => Biquad.Peaking(48000, 1000, 1e-290, -1000)));
            Check.Equal("slope", ParamOf(() => Biquad.LowShelf(48000, 1000, 0, 1e-310)));
            Check.Equal("slope", ParamOf(() => Biquad.HighShelf(48000, 1000, 6, 1e-310)));
        }

        // ---------- SchmittTrigger ----------

        public void Schmitt_EqualThresholdsDoNotChatter()
        {
            var t = new SchmittTrigger(5, 5);
            for (int i = 0; i < 6; i++) Check.True(t.Update(5), $"sample {i}");    // toggled every sample
            Check.False(t.Update(4.9));
            Check.False(t.Update(4.9));
            Check.True(t.Update(5));
        }

        // ---------- PidController ----------

        public void Pid_NoIntegralGainDoesNotPoisonTheOutput()
        {
            var pid = new PidController(1, 0, 0, -10, 10);
            pid.Update(-1e308, 10);
            Check.False(double.IsNaN(pid.Update(0, 1)), "NaN forever");
        }

        public void Pid_OverflowingErrorDoesNotStickInTheIntegral()
        {
            var pid = new PidController(0, 1, 0) { Setpoint = 1e308 };
            pid.Update(-1e308, 1);
            pid.Setpoint = 0;
            Check.True(!double.IsInfinity(pid.Update(0, 1)) && !double.IsNaN(pid.Update(0, 1)), "∞ on every call");

            var pi = new PidController(1, 1, 0);
            pi.Update(-1e308, 10);
            Check.False(double.IsInfinity(pi.Update(0, 1)));
        }

        // ---------- FirstOrderLag / Kalman ----------

        public void Lag_FarApartValuesDoNotOverflow()
        {
            var lag = new FirstOrderLag(1);
            lag.Update(-1e308, 1);
            Check.False(double.IsNaN(lag.Update(1e308, 0)), "NaN");
            Check.False(double.IsNaN(lag.Update(0, 1)), "NaN forever");

            lag = new FirstOrderLag(1);
            lag.Reset(-1e308);
            double v = lag.Update(1e308, Math.Log(2));
            Check.True(Math.Abs(v) < 1e300, v.ToString("R"));                    // was ∞; ≈ 0
        }

        public void Lag_TinyCutoffNamesCutoff()
            => Check.Equal("cutoffHz", ParamOf(() => FirstOrderLag.FromCutoff(1e-320)));

        public void Kalman_FarApartValuesDoNotOverflow()
        {
            double e = new KalmanFilter1D(0, 1, -1e308, 1).Update(1e308);
            Check.Equal(0.0, e);                                                 // was ∞
            Check.False(double.IsNaN(new KalmanFilter1D(0, 1, -1e308, 0).Update(1e308)));
            Check.False(double.IsNaN(new KalmanFilter1D(1e308, 1, 0, 1e308).Update(1)));
            var k = new KalmanFilter1D(1e308, 1e308);
            k.Update(1);
            Check.False(double.IsNaN(k.Update(2)));
            Check.False(double.IsNaN(k.ErrorCovariance) || double.IsInfinity(k.ErrorCovariance));
        }

        // ---------- StepResponse / PidTuning ----------

        public void StepResponse_HugeStepIsNormalisedWithoutOverflow()
        {
            var info = StepResponse.Analyze(new[] { -1e308, 0, 1e308, 0, 0, 0 }, 1);
            Check.True(Math.Abs(info.OvershootPercent - 100) < 1e-9, info.OvershootPercent.ToString("R"));
            Check.False(double.IsNaN(info.SettlingTime));
        }

        public void StepResponse_HugeIntervalNamesSampleInterval()
            => Check.Equal("sampleInterval", ParamOf(() => StepResponse.Analyze(new[] { 0.0, 1, 1 }, 1e308)));

        public void FitStep_TinyInputStepNamesInputStep()
        {
            double[] t = Enumerable.Range(0, 200).Select(i => (double)i).ToArray();
            double[] y = t.Select(x => x < 5 ? 0 : 1e300 * (1 - Math.Exp(-(x - 5) / 20))).ToArray();
            Check.Equal("inputStep", ParamOf(() => FopdtModel.FitStep(t, y, 1e-10)));
        }

        public void Tuning_ExtremeModelNamesModel()
        {
            Check.Equal("model", ParamOf(() => PidTuning.Simc(new FopdtModel(1e-300, 1e10, 1e-10))));
            Check.Equal("model", ParamOf(() => PidTuning.ZieglerNichols(new FopdtModel(1, 1, 1e308))));    // Ti = ∞ silently
            Check.Equal("model", ParamOf(() => PidTuning.ZieglerNichols(new FopdtModel(1, 1, 1e308), includeDerivative: false)));
            var ok = PidTuning.ZieglerNichols(new FopdtModel(2, 10, 1));
            Check.Equal(6.0, ok.Kp);
        }
    }
}
