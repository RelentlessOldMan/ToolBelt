using System;
using ToolBelt.Control;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    /// <summary>Round 200 review regressions (filters and control, twelfth pass).</summary>
    public sealed class FiltersControlReview200Tests
    {
        // sign * e^(logMagnitude), the reference for products far outside the double range.
        private static double FromLog(int sign, double logMagnitude) => sign * Math.Exp(logMagnitude);

        private static void CheckRelative(double expected, double actual, double tolerance, string what)
        {
            Check.True(Math.Abs(actual - expected) <= tolerance * Math.Abs(expected),
                what + ": expected " + expected.ToString("R") + ", got " + actual.ToString("R"));
        }

        // ---------- PidController ----------

        // The derivative was kd * change / dt, and kd * change overflowed where the result fits (kd 1e245, change 1e278,
        // dt 1e272 gave -Infinity, not -1e251). Graded against the product formed from logarithms.
        public void PidController_DerivativeProductOverflow_GivesTheFiniteResult()
        {
            var pid = new PidController(0, 0, 1e245);
            pid.Update(0, 1);
            CheckRelative(-1e251, pid.Update(1e278, 1e272), 1e-14, "reviewer case");
            var rng = new Random(200);
            for (int trial = 0; trial < 5000; trial++)
            {
                double kd = (rng.Next(2) == 0 ? -1 : 1) * Math.Pow(10, 100 + rng.NextDouble() * 200);
                double change = Math.Pow(10, 100 + rng.NextDouble() * 200);
                // dt so the result is within [1e-250, 1e250].
                double logResult = (rng.NextDouble() * 500 - 250) * Math.Log(10);
                double logDt = Math.Log(Math.Abs(kd)) + Math.Log(change) - logResult;
                if (logDt > 700 || logDt < -700) continue;
                double dt = Math.Exp(logDt);
                var p = new PidController(0, 0, kd);
                p.Update(0, 1);
                double actual = p.Update(change, dt);
                double expected = FromLog(-Math.Sign(kd), Math.Log(Math.Abs(kd)) + Math.Log(change) - Math.Log(dt));
                CheckRelative(expected, actual, 1e-11, "kd " + kd.ToString("R") + " change " + change.ToString("R") + " dt " + dt.ToString("R"));
            }
        }

        // The windup fallback formed the same product in units of 2^-600, which overflowed too, so the output landed on
        // the wrong limit: +1 where P + I + D is far below -1.
        public void PidController_DerivativeOverflowInWindup_ClampsToTheRightLimit()
        {
            var pid = new PidController(0, 1, -1e250, -1, 1);
            pid.Update(0, 1);
            Check.Equal(-1.0, pid.Update(1e254, 1e270));
        }

        // Derivative on error took the difference of the two rounded errors, which lost a small measurement change next
        // to a large setpoint (setpoint 1e17, measurement 0 then 1 gave 0, not -1).
        public void PidController_DerivativeOnError_KeepsSmallChangesNextToALargeSetpoint()
        {
            var pid = new PidController(0, 0, 1, derivativeOnMeasurement: false) { Setpoint = 1e17 };
            pid.Update(0, 1);
            Check.Equal(-1.0, pid.Update(1, 1));
            var rng = new Random(2001);
            for (int trial = 0; trial < 5000; trial++)
            {
                double setpoint = (rng.Next(2) == 0 ? -1 : 1) * Math.Pow(10, 16 + rng.NextDouble() * 200);
                double m0 = rng.Next(-1000, 1000), m1 = rng.Next(-1000, 1000);
                double kd = Math.Pow(10, rng.NextDouble() * 10 - 5);
                double dt = Math.Pow(10, rng.NextDouble() * 10 - 5);
                var p = new PidController(0, 0, kd, derivativeOnMeasurement: false) { Setpoint = setpoint };
                p.Update(m0, 1);
                double expected = kd * -(m1 - m0) / dt;          // m1 - m0 is exact; one product, one quotient
                double actual = p.Update(m1, dt);
                Check.True(Math.Abs(actual - expected) <= 4e-16 * Math.Abs(expected),
                    "setpoint " + setpoint.ToString("R") + ": expected " + expected.ToString("R") + ", got " + actual.ToString("R"));
            }
        }

        // ---------- KalmanFilter1D ----------

        // The weight kept for the prior, R/P' / (1 + R/P'), underflowed to 0 where the kept term does not (estimate
        // 1e150 kept with weight 1e-400 is 1e-250); the estimate came out as the measurement.
        public void KalmanFilter1D_KeptWeightUnderflow_KeepsThePrior()
        {
            var k = new KalmanFilter1D(1e200, 1e-200, 1e150, 0);
            CheckRelative(1e-250 + 1e-290, k.Update(1e-290), 1e-13, "reviewer case");
            var rng = new Random(2002);
            for (int trial = 0; trial < 5000; trial++)
            {
                double p = Math.Pow(10, 100 + rng.NextDouble() * 200), r = Math.Pow(10, -(100 + rng.NextDouble() * 200));
                double x0 = (rng.Next(2) == 0 ? -1 : 1) * Math.Pow(10, rng.NextDouble() * 300);
                double logExpected = Math.Log(Math.Abs(x0)) + Math.Log(r) - Math.Log(p);
                if (logExpected < -700) continue;
                var f = new KalmanFilter1D(0, r, x0, p);
                CheckRelative(FromLog(Math.Sign(x0), logExpected), f.Update(0), 1e-12, "P " + p.ToString("R") + " R " + r.ToString("R"));
            }
        }

        // The mirror case: for P' < R the gain P'/R / (1 + P'/R) underflowed, so a huge measurement moved an estimate of
        // 0 not at all (P' 1e-200, R 1e200, measurement 1e300 gives 1e-100).
        public void KalmanFilter1D_GainUnderflow_StillTakesTheMeasurement()
        {
            var k = new KalmanFilter1D(0, 1e200, 0, 1e-200);
            CheckRelative(1e-100, k.Update(1e300), 1e-13, "mirror case");
            var rng = new Random(2003);
            for (int trial = 0; trial < 5000; trial++)
            {
                double p = Math.Pow(10, -(100 + rng.NextDouble() * 200)), r = Math.Pow(10, 100 + rng.NextDouble() * 200);
                double m = (rng.Next(2) == 0 ? -1 : 1) * Math.Pow(10, rng.NextDouble() * 300);
                double logExpected = Math.Log(Math.Abs(m)) + Math.Log(p) - Math.Log(r);
                if (logExpected < -700) continue;
                var f = new KalmanFilter1D(0, r, 0, p);
                CheckRelative(FromLog(Math.Sign(m), logExpected), f.Update(m), 1e-12, "P " + p.ToString("R") + " R " + r.ToString("R"));
            }
        }

        // ---------- FirstOrderLag ----------

        // e^(-dt/tau) underflowed to 0 past about 745 time constants, so a large value was dropped where its decayed
        // part is still far above the input (1e300 after 800 tau is 3.67e-48, not the input 1e-300).
        public void FirstOrderLag_DecayFactorUnderflow_KeepsTheDecayedValue()
        {
            var lag = new FirstOrderLag(1);
            lag.Reset(1e300);
            CheckRelative(1e300 * Math.Exp(-400) * Math.Exp(-400), lag.Update(1e-300, 800), 1e-13, "reviewer case");
            var rng = new Random(2004);
            for (int trial = 0; trial < 5000; trial++)
            {
                double tau = Math.Pow(10, rng.NextDouble() * 12 - 6);
                double ratio = 700 + rng.NextDouble() * 700;
                double dt = tau * ratio;
                double x0 = (rng.Next(2) == 0 ? -1 : 1) * Math.Pow(10, 200 + rng.NextDouble() * 108);
                double logExpected = Math.Log(Math.Abs(x0)) - dt / tau;
                if (logExpected < -700) continue;
                var f = new FirstOrderLag(tau);
                f.Reset(x0);
                CheckRelative(FromLog(Math.Sign(x0), logExpected), f.Update(0, dt), 1e-11, "tau " + tau.ToString("R") + " dt " + dt.ToString("R"));
            }
        }
    }
}
