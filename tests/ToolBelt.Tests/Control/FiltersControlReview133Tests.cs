using System;
using ToolBelt.Control;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    /// <summary>Round 133 review regressions (filters and control, eighth pass).</summary>
    public sealed class FiltersControlReview133Tests
    {
        // kaiserord takes 8 dB (it refuses only less); KaiserParameters refused 8 itself. Values from scipy.signal.kaiserord.
        public void KaiserParameters_EightDbIsAllowed_AsKaiserordAllowsIt()
        {
            var (taps, beta) = FirFilter.KaiserParameters(8, 100, 1000);   // width 0.2 of Nyquist
            Check.Equal(2, taps);
            Check.Equal(0.0, beta);
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.KaiserParameters(7.999, 100, 1000));
            Check.Equal("attenuationDb", ex.ParamName);
            var (t30, b30) = FirFilter.KaiserParameters(30, 25, 1000);     // width 0.05
            Check.Equal(63, t30);
            Check.Close(2.1166248611409806, b30, 1e-12);
        }

        // ---------- IIR response next to a pole ----------

        // The denominator was summed directly, which next to a pole near the unit circle lost its digits: this elliptic
        // high-pass's pass-band edge read −0.184 dB (−0.209 through Response). The reference, −0.474614 dB, is the
        // filter's own sections evaluated exactly in mpmath at 60 digits. They are the sections EllipticHighPass(11,
        // 0.6746060043311932, 3.7811020546517495, fc, fs) gave until round 170, which now refuses that design: they miss
        // its −0.6746 dB at the edge.
        public void Iir_MagnitudeNextToAPole_IsTakenFromTheRoots()
        {
            const double fc = 3188.5956914197245, fs = 24005303.363861687;
            var f = new IirFilter(
                new Biquad(0.8287241811812442, -1.657447785136065, 0.8287241811812442, -1.9999992888922535, 0.9999999854250644),
                new Biquad(0.9834350879009769, -1.9668694908031876, 0.9834350879009766, -1.9999993032202183, 0.9999999997571996),
                new Biquad(0.9931769288216235, -1.986353166626214, 0.9931769288216232, -1.9999984289654789, 0.9999991252479943),
                new Biquad(1.2264323996102655, -1.2264323996102655, 0, -0.9994070642141338, 0),
                new Biquad(0.9852502718704154, -1.9704998574775145, 0.9852502718704153, -1.999999303458972, 0.9999999999960227),
                new Biquad(1.0220855323834095, -2.044170398743394, 1.0220855323834093, -1.9999473838616497, 0.9999480652866688));
            Check.Close(-0.4746136, f.MagnitudeDb(fc, fs), 1e-4);
            Check.Close(-0.4746136, 20 * Math.Log10(f.Response(fc, fs).Magnitude), 1e-4);
            Check.Close(-0.4746136, 20 * Math.Log10(f.Response(-fc, fs).Magnitude), 1e-4);
            Check.Close(f.Response(fc, fs).Phase, -f.Response(-fc, fs).Phase, 1e-9);
        }

        // ---------- Nyquist checks at subnormal rates ----------

        // sampleRate / 2 rounds for a subnormal rate (5ε / 2 is 2ε, 9ε / 2 is 4ε), so a frequency below Nyquist was refused.
        public void Nyquist_IsCheckedAsARatio_AtSubnormalRates()
        {
            double e = double.Epsilon;
            Check.NotNull(IirFilter.ButterworthLowPass(2, 2 * e, 5 * e));
            Check.NotNull(Biquad.LowPass(9 * e, 4 * e));
            var (order, _) = IirFilter.ButterworthOrder(1 * e, 2 * e, 1, 40, 5 * e);
            Check.True(order >= 1);
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => Biquad.LowPass(8 * e, 4 * e));
            Check.Equal("frequency", ex.ParamName);
        }

        // ---------- overflow rescue next to a non-finite sample ----------

        // A finite input that overflowed was rescued only when every sample was finite: a NaN appended at the end left the
        // earlier overflowed outputs as ∞ although they do not depend on it.
        public void IirFilter_OverflowAheadOfATrailingNaN_IsStillRescued()
        {
            var f = IirFilter.ButterworthLowPass(2, 0.1, 1);
            var clean = new double[6];
            for (int i = 0; i < 6; i++) clean[i] = 1.7e308;
            var withNaN = new double[7];
            Array.Copy(clean, withNaN, 6);
            withNaN[6] = double.NaN;
            double[] a = f.Filter(clean), b = f.Filter(withNaN);
            for (int i = 0; i < 6; i++)
            {
                Check.True(!double.IsInfinity(a[i]) && !double.IsNaN(a[i]), $"clean output {i} is finite");
                Check.Equal(a[i], b[i], $"output {i}");
            }
            Check.True(double.IsNaN(b[6]));
        }

        public void FirFilter_OverflowAwayFromANaN_IsStillRescued()
        {
            var fir = new FirFilter(new[] { 1.5, -0.5 });
            double[] clean = { 1e308, 1.7e308, 1.7e308, 1.0 }, dirty = { 1e308, 1.7e308, 1.7e308, 1.0, double.NaN };
            double[] a = fir.Filter(clean), b = fir.Filter(dirty);
            for (int i = 0; i < 4; i++) Check.Equal(a[i], b[i], $"Filter output {i}");
            Check.Close(1.5e308, a[0], 1e293);
            Check.Close(1.7e308, a[2], 1e293);   // 1.5 × 1.7e308 overflowed on the way
            Check.True(double.IsNaN(b[4]));

            var aligned = new FirFilter(new[] { -0.5, 2, -0.5 });
            double[] c = { 1.7e308, 1.7e308, 1.7e308, 1.0, 1.0, 1.0 }, d = { 1.7e308, 1.7e308, 1.7e308, 1.0, 1.0, double.NaN };
            double[] ca = aligned.FilterAligned(c), da = aligned.FilterAligned(d);
            for (int i = 0; i < 4; i++) Check.Equal(ca[i], da[i], $"FilterAligned output {i}");
            Check.Close(1.7e308, ca[1], 1e293);   // 2 × 1.7e308 overflowed on the way
            Check.True(double.IsNaN(da[4]) && double.IsNaN(da[5]));
        }

        // ---------- Kalman ----------

        // (1 − K)·P was formed as K·R, which lost P when P/R underflowed: P 1e-30 against R 1e300 came out 0, after which
        // the filter (with no process noise) could never take a measurement again.
        public void Kalman_TinyCovarianceAgainstHugeNoise_IsKept()
        {
            var k = new KalmanFilter1D(0, 1e300, 0, 1e-30);
            k.Update(1);
            Check.Close(1e-30, k.ErrorCovariance, 1e-44);
            var rnd = new Random(1331);
            for (int i = 0; i < 500; i++)
            {
                double p = Math.Pow(10, rnd.Next(-300, 300)) * (1 + rnd.NextDouble()), r = Math.Pow(10, rnd.Next(-300, 300)) * (1 + rnd.NextDouble());
                var f = new KalmanFilter1D(0, r, 0, p);
                f.Update(1);
                double want = p < r ? p / (1 + p / r) : r / (1 + r / p);   // PR / (P + R)
                Check.True(Math.Abs(f.ErrorCovariance - want) <= 1e-15 * want, $"P {p}, R {r}: {f.ErrorCovariance} vs {want}");
            }
        }

        // ---------- PID at tiny scales ----------

        // (atLimit − integral)·error underflowed (5e-201 × 1e-200), so an I-only loop clamped at a tiny limit never moved.
        public void Pid_StepToATinyLimit_IsTaken()
        {
            var pid = new PidController(0, 1, 0, double.NegativeInfinity, 5e-201) { Setpoint = 1e-200 };
            Check.Equal(5e-201, pid.Update(0, 1));
        }

        // ki·error underflowed to 0 (1e-200 × 1e-200), so neither windup test fired and the integral wound up past the
        // limit: after the setpoint flipped the output stayed at the upper limit.
        public void Pid_AntiWindup_HoldsAtTinyGains()
        {
            var pid = new PidController(0, 1e-200, 0, -1e-150, 1e-150) { Setpoint = 1e-200 };
            for (int i = 0; i < 5; i++) Check.Equal(1e-150, pid.Update(0, 1e300));
            pid.Setpoint = -1e-200;
            Check.Equal(-1e-150, pid.Update(0, 1e300));
        }

        // ---------- tuning ----------

        // 4τ overflowed for τ = 5e307, so Cohen–Coon's derivative time came out ∞.
        public void CohenCoon_HugeTimeConstant_DerivativeTimeIsFinite()
        {
            var g = PidTuning.CohenCoon(new FopdtModel(1, 5e307, 1e308));
            Check.Close(4.0 / 3 * 0.5 + 0.25, g.Kp, 1e-15);
            Check.Close(5e307 * (4 / 7.5), g.DerivativeTime, 1e293);
        }

        // StepResponse returned NaN for a NaN time and 0 for an infinite step before the dead time.
        public void StepResponse_RejectsNaNTimeAndNonFiniteStep()
        {
            var m = new FopdtModel(2, 3, 1);
            Check.Equal("time", Check.Throws<ArgumentOutOfRangeException>(() => m.StepResponse(double.NaN)).ParamName);
            Check.Equal("inputStep", Check.Throws<ArgumentOutOfRangeException>(() => m.StepResponse(0.5, double.PositiveInfinity)).ParamName);
            Check.Equal("inputStep", Check.Throws<ArgumentOutOfRangeException>(() => m.StepResponse(5, double.NaN)).ParamName);
            Check.Equal(0.0, m.StepResponse(0.5));
            Check.Close(2 * (1 - Math.Exp(-1)), m.StepResponse(4), 1e-15);
        }

        // ---------- slew limiter ----------

        // An unlimited rise rate sent every update down the halved path, where a subnormal fall step rounded: a fall of 3ε
        // per update went 4ε.
        public void Slew_UnlimitedRise_FallStepIsExact()
        {
            double e = double.Epsilon;
            var s = new SlewRateLimiter(double.PositiveInfinity, 1);
            s.Reset(0);
            Check.Equal(-3 * e, s.Update(-1, 3 * e));
            Check.True(s.IsLimiting);
            Check.Equal(5.0, s.Update(5, 1e-300));
            var t = new SlewRateLimiter(1, double.PositiveInfinity);
            t.Reset(0);
            Check.Equal(3 * e, t.Update(1, 3 * e));
            Check.Equal(-7.0, t.Update(-7, 0));
            // A finite rate whose step overflows still limits (the halved path).
            var u = new SlewRateLimiter(1e308);
            u.Reset(-1e308);
            Check.Equal(1e308, u.Update(1e308, 1e10));
            Check.False(u.IsLimiting);
            u.Reset(-1e308);
            Check.Equal(0.0, u.Update(1e308, 1));
            Check.True(u.IsLimiting);
        }
    }
}
