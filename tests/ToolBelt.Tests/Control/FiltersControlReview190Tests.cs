using System;
using System.Globalization;
using System.Numerics;
using ToolBelt.Control;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    /// <summary>Round 190 review regressions (filters and control, eleventh pass).</summary>
    public sealed class FiltersControlReview190Tests
    {
        // ---------- FirstOrderLag ----------

        // The output kept 1 - alpha of the old value, which cancelled for a long step: decaying toward 0 over 40 time
        // constants gave exactly 0, not e^-40 = 4.2e-18, and 30 was off by 1.7e-4. Graded against x0 * e^(-dt/tau).
        public void FirstOrderLag_LongStepTowardZero_KeepsTheExactDecay()
        {
            var lag = new FirstOrderLag(1);
            lag.Reset(1);
            Check.Close(Math.Exp(-40), lag.Update(0, 40), 1e-14 * Math.Exp(-40));
            var rng = new Random(190);
            for (int trial = 0; trial < 20000; trial++)
            {
                double tau = Math.Pow(10, rng.NextDouble() * 12 - 6);
                double ratio = rng.NextDouble() * (trial % 2 == 0 ? 50 : 700);
                double dt = tau * ratio;
                double x0 = (rng.Next(2) == 0 ? -1 : 1) * Math.Pow(10, rng.NextDouble() * 40 - 20);
                var f = new FirstOrderLag(tau);
                f.Reset(x0);
                double got = f.Update(0, dt);
                double expected = x0 * Math.Exp(-dt / tau);
                Check.True(Math.Abs(got - expected) <= 1e-13 * Math.Abs(expected) + 4 * double.Epsilon,
                    $"tau {tau:R}, dt {dt:R}, x0 {x0:R}: got {got:R}, expected {expected:R}");
            }
        }

        // ---------- KalmanFilter1D ----------

        // v * 2^1074 as an integer (v finite).
        private static BigInteger Scaled(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else mant |= 1L << 52;
            BigInteger m = new BigInteger(mant) << (exp - 1);
            return bits < 0 ? -m : m;
        }

        // (R x + P z) / (P + R) exactly, as a double within a relative 2^-60.
        private static double ExactBlend(double x, double z, double p, double r)
        {
            BigInteger n = Scaled(r) * Scaled(x) + Scaled(p) * Scaled(z);   // * 2^2148
            BigInteger d = Scaled(p) + Scaled(r);                            // * 2^1074
            if (n.IsZero) return 0;
            bool negative = n.Sign < 0;
            BigInteger q = (BigInteger.Abs(n) << 64) / d;                     // * 2^(1074 + 64)
            int length = q.ToByteArray().Length * 8;
            int shift = Math.Max(0, length - 120);
            double head = (double)(q >> shift) * Math.Pow(2, -569) * Math.Pow(2, shift - 64 - 1074 + 569);
            return negative ? -head : head;
        }

        // 1 - gain rounded to 0 once R/P fell below about 1e-16, so the prior was dropped: estimate 1e8 with P 1e16 and
        // R 1 updated to 0 for a measurement of 0, not 1e-8. Graded against the exact blend over random P/R up to 1e80.
        public void Kalman_CovarianceFarAboveNoise_KeepsThePriorsShare()
        {
            var k = new KalmanFilter1D(0, 1, initialEstimate: 1e8, initialErrorCovariance: 1e16);
            Check.Close(1e-8, k.Update(0), 1e-21);
            var rng = new Random(1900);
            for (int trial = 0; trial < 20000; trial++)
            {
                double p = Math.Pow(10, rng.NextDouble() * 80 - 40);
                double r = Math.Pow(10, rng.NextDouble() * 80 - 40);
                double x = (rng.Next(2) == 0 ? -1 : 1) * Math.Pow(10, rng.NextDouble() * 60 - 30);
                double z = (rng.Next(2) == 0 ? -1 : 1) * Math.Pow(10, rng.NextDouble() * 60 - 30);
                var f = new KalmanFilter1D(0, r, x, p);
                double got = f.Update(z);
                double expected = ExactBlend(x, z, p, r);
                // Each share is good to a few ulps, so the error is bounded by the larger share's size.
                double scale = Math.Max(Math.Abs(x) * (r / (p + r)), Math.Abs(z) * (p / (p + r)));
                Check.True(Math.Abs(got - expected) <= 8e-16 * scale + 4 * double.Epsilon,
                    $"x {x:R}, z {z:R}, P {p:R}, R {r:R}: got {got:R}, expected {expected:R}");
            }
        }

        // ---------- PidGains ----------

        private static bool IsNegativeZero(double v) => v == 0 && BitConverter.DoubleToInt64Bits(v) < 0;

        // Kd was Kp * 0, which is -0 for a reverse-acting PI, so ToString wrote "Kd=-0" on .NET and "Kd=0" on .NET
        // Framework; a Kp of -0 leaked through Kp and Ki the same way.
        public void PidGains_ZeroGains_ArePositiveZeroOnEveryRuntime()
        {
            var pi = PidTuning.Lambda(new FopdtModel(-2, 10, 1));
            Check.True(pi.Kp < 0, "reverse acting");
            Check.False(IsNegativeZero(pi.Kd), "Kd is -0");
            Check.True(pi.ToString().Contains("Kd=0 "), pi.ToString());
            var zero = new PidGains(-0.0, double.PositiveInfinity, -0.0);
            Check.False(IsNegativeZero(zero.Kp), "Kp is -0");
            Check.False(IsNegativeZero(zero.Ki), "Ki is -0");
            Check.False(IsNegativeZero(zero.Kd), "Kd is -0");
            Check.False(IsNegativeZero(zero.DerivativeTime), "Td is -0");
            var noTd = new PidGains(-3, 5, 0);
            Check.False(IsNegativeZero(noTd.Kd), "Kd of a negative PI is -0");
            var negKp = new PidGains(-0.0, 5, 0);
            Check.False(IsNegativeZero(negKp.Ki), "Ki of Kp -0 is -0");
            Check.False(pi.ToString().Contains("-0 ") || zero.ToString().Contains("=-0"), zero.ToString());
        }

        // ---------- PidController ----------

        // The integral past double.MaxValue was held in units of 2^600, which ran out past about 4e488: Ki 1e-300 with an
        // error of 1e300 over dt 1e200 stayed at 0 for good, not 1e200, 2e200, 3e200. Graded against k * Ki * error * dt
        // (through logarithms) for tiny gains and huge steps, forward and reverse acting.
        public void Pid_HugeIntegralWithTinyKi_IsStillTaken()
        {
            var pid = new PidController(0, 1e-300, 0, -1e300, 1e300) { Setpoint = 1e300 };
            Check.Close(1e200, pid.Update(0, 1e200), 1e188);
            Check.Close(2e200, pid.Update(0, 1e200), 2e188);
            Check.Close(3e200, pid.Update(0, 1e200), 3e188);
            var rng = new Random(19001);
            int checkedCount = 0;
            for (int trial = 0; trial < 4000; trial++)
            {
                double ki = (rng.Next(2) == 0 ? -1 : 1) * Math.Pow(10, -320 + rng.NextDouble() * 220);
                double error = (rng.Next(2) == 0 ? -1 : 1) * Math.Pow(10, 100 + rng.NextDouble() * 208);
                double dt = Math.Pow(10, 100 + rng.NextDouble() * 200);
                var c = new PidController(0, ki, 0, -double.MaxValue, double.MaxValue) { Setpoint = error };
                for (int k = 1; k <= 3; k++)
                {
                    double got = c.Update(0, dt);
                    double log = Math.Log(Math.Abs(ki)) + Math.Log(Math.Abs(error)) + Math.Log(dt) + Math.Log(k);
                    if (log > Math.Log(1e307) || log < Math.Log(1e-300)) break;
                    double expected = Math.Sign(ki) * Math.Sign(error) * Math.Exp(log);
                    Check.True(Math.Abs(got - expected) <= 1e-11 * Math.Abs(expected),
                        $"ki {ki:R}, error {error:R}, dt {dt:R}, step {k}: got {got:R}, expected {expected:R}");
                    checkedCount++;
                }
            }
            Check.True(checkedCount > 2000, "trials checked: " + checkedCount);
        }

        // ---------- FirFilter ----------

        // An edge whose ratio to the sample rate underflowed to 0 passed validation: a low-pass then blamed taps ("the
        // window leaves no gain"), and a high-pass or band edge was taken as 0. Two edges rounding to one ratio made an
        // empty band.
        public void Fir_EdgeTooSmallForTheSampleRate_IsRefusedByName()
        {
            var lp = Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.LowPass(31, double.Epsilon, 1e10));
            Check.Equal("cutoff", lp.ParamName);
            var hp = Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.HighPass(31, 1e-320, 1e10));
            Check.Equal("cutoff", hp.ParamName);
            var bp = Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.BandPass(31, 1e-320, 1e3, 1e10));
            Check.Equal("low", bp.ParamName);
            var close = Check.Throws<ArgumentException>(() => FirFilter.BandPass(31, 3e-314, 3.0000001e-314, 1e10));
            Check.Equal("high", close.ParamName);
            FirFilter.BandPass(31, 1e-300, 1e3, 1e10);   // a tiny but representable edge still designs
        }

        // ---------- Biquad ----------

        private static bool OnFramework => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            .StartsWith(".NET Framework", StringComparison.Ordinal);

        private static double[] Coefficients(string text)
        {
            var parts = text.Replace("b=[", "").Replace("] a=[", ", ").Replace("]", "").Split(new[] { ", " }, StringSplitOptions.None);
            var v = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++) v[i] = double.Parse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture);
            return v;
        }

        // ToString used the default double format, which on .NET Framework wrote 15 digits (0.30000000000000004 as 0.3), so
        // logged coefficients did not read back. Graded by reading 4,000 random sections back, bit for bit, and against
        // .NET's "R" where that is the default.
        public void Biquad_ToString_RoundTripsOnEveryRuntime()
        {
            Check.Equal("b=[0.30000000000000004, -0, 1E-05] a=[1, 1.7976931348623157E+308, 0.0001]",
                new Biquad(0.30000000000000004, -0.0, 1e-5, double.MaxValue, 1e-4).ToString());
            var rng = new Random(19002);
            var bytes = new byte[8];
            for (int trial = 0; trial < 4000; trial++)
            {
                var c = new double[5];
                for (int i = 0; i < 5; i++)
                {
                    do
                    {
                        rng.NextBytes(bytes);
                        c[i] = trial % 2 == 0 ? BitConverter.ToDouble(bytes, 0) : (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-8, 20));
                    } while (double.IsNaN(c[i]) || double.IsInfinity(c[i]));
                }
                string text = new Biquad(c[0], c[1], c[2], c[3], c[4]).ToString();
                double[] back = Coefficients(text);
                double[] want = { c[0], c[1], c[2], 1, c[3], c[4] };
                for (int i = 0; i < 6; i++)
                    Check.True(BitConverter.DoubleToInt64Bits(want[i]) == BitConverter.DoubleToInt64Bits(back[i]), text);
                if (!OnFramework)
                    Check.Equal(FormattableString.Invariant($"b=[{c[0]:R}, {c[1]:R}, {c[2]:R}] a=[1, {c[3]:R}, {c[4]:R}]"), text);
            }
        }

        // ---------- FitStep and StepResponse.Analyze (R107-3) ----------

        // An explicit initial and final value that are equal blamed values, whose samples did change.
        public void FitStepAndAnalyze_EqualExplicitValues_BlameThem()
        {
            var times = new[] { 0.0, 1, 2, 3, 4 };
            var values = new[] { 0.0, 2, 4, 5, 5 };
            var both = Check.Throws<ArgumentException>(() => FopdtModel.FitStep(times, values, 1, initialValue: 5, finalValue: 5));
            Check.Equal("finalValue", both.ParamName);
            var initial = Check.Throws<ArgumentException>(() => FopdtModel.FitStep(times, values, 1, initialValue: 5));
            Check.Equal("initialValue", initial.ParamName);
            var flat = Check.Throws<ArgumentException>(() => FopdtModel.FitStep(times, new[] { 1.0, 0, 2, 0, 1 }, 1));
            Check.Equal("values", flat.ParamName);
            var a1 = Check.Throws<ArgumentException>(() => StepResponse.Analyze(times, values, finalValue: 5, initialValue: 5));
            Check.Equal("finalValue", a1.ParamName);
            var a2 = Check.Throws<ArgumentException>(() => StepResponse.Analyze(times, values, initialValue: 5));
            Check.Equal("initialValue", a2.ParamName);
            var a3 = Check.Throws<ArgumentException>(() => StepResponse.Analyze(times, values, initialValue: -double.MaxValue, finalValue: double.MaxValue));
            Check.Equal("finalValue", a3.ParamName);
        }
    }
}
