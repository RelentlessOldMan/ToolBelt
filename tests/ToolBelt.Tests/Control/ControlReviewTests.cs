using System;
using ToolBelt.Control;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    // Regressions from review round 1 (filters & control).
    public sealed class ControlReviewTests
    {
        public void Pid_AntiWindupWorksForReverseActingGains()
        {
            // Reverse-acting plant (negative gain, as FitStep gives for a falling response) and the gains PidTuning returns for
            // it: both negative. Saturate against an unreachable setpoint, then release; the output must come off the
            // limit as fast as the mirrored direct-acting loop does.
            int Recover(double sign)
            {
                var gains = PidTuning.Simc(new FopdtModel(2 * sign, 10, 1));
                var pid = new PidController(gains.Kp, gains.Ki, 0, -1, 1) { Setpoint = -5 * sign };
                double y = 0, dt = 0.1;
                for (int i = 0; i < 2000; i++) y += dt / 10 * (2 * sign * pid.Update(y, dt) - y);    // first-order plant
                pid.Setpoint = 0;
                for (int i = 0; i < 100000; i++)
                {
                    double u = pid.Update(y, dt);
                    if (Math.Abs(u) < 1) return i;
                    y += dt / 10 * (2 * sign * u - y);
                }
                return int.MaxValue;
            }
            int direct = Recover(1), reverse = Recover(-1);
            Check.True(direct < 100, $"direct-acting took {direct} steps");
            Check.Equal(direct, reverse);
        }

        public void Pid_RejectsNonFiniteInputsInsteadOfPoisoningState()
        {
            var pid = new PidController(1, 1, 0);
            Check.Throws<ArgumentOutOfRangeException>(() => pid.Update(double.NaN, 0.1));
            Check.Throws<ArgumentOutOfRangeException>(() => pid.Update(double.PositiveInfinity, 0.1));
            Check.Throws<ArgumentOutOfRangeException>(() => pid.Update(0, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => pid.Update(0, double.PositiveInfinity));
            Check.Throws<ArgumentOutOfRangeException>(() => pid.Setpoint = double.NaN);
            Check.Close(1.1, pid.Update(-1, 0.1), 1e-12);                   // state untouched by the rejected calls
            Check.Throws<ArgumentOutOfRangeException>(() => new PidController(double.NaN, 1, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new PidController(1, double.PositiveInfinity, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new PidController(1, 1, 0, 0, double.NaN));
        }

        public void Kalman_ValidatesAndCanReset()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new KalmanFilter1D(double.NaN, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => new KalmanFilter1D(double.PositiveInfinity, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => new KalmanFilter1D(0, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => new KalmanFilter1D(0, 1, 0, -0.5));
            Check.Throws<ArgumentOutOfRangeException>(() => new KalmanFilter1D(0, 1, double.NaN));

            var k = new KalmanFilter1D(0.1, 1);
            Check.Throws<ArgumentOutOfRangeException>(() => k.Update(double.NaN));
            double e = k.Update(5);
            Check.True(e > 0 && e < 5);
            k.Reset(10, 2);
            Check.Equal(10.0, k.Estimate);
            Check.Equal(2.0, k.ErrorCovariance);
            Check.Throws<ArgumentOutOfRangeException>(() => k.Reset(0, -1));
        }

        public void Deadband_NaNPassesThroughAndInfinityIsReportedOnce()
        {
            Check.True(double.IsNaN(Deadband.Apply(double.NaN, 1)));
            Check.Throws<ArgumentOutOfRangeException>(() => Deadband.Apply(1, 1, double.NaN));
            var f = new DeadbandFilter(1);
            Check.True(f.Update(double.PositiveInfinity));
            Check.False(f.Update(double.PositiveInfinity));
        }

        public void SlewRateLimiter_UnlimitedRateFollowsEvenAtZeroStep()
        {
            var s = new SlewRateLimiter(double.PositiveInfinity);
            s.Update(0, 1);
            Check.Equal(5.0, s.Update(5, 0));
            Check.False(s.IsLimiting);
        }

        public void PidGains_RejectsNonPositiveIntegralTime()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new PidGains(1, 0, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new PidGains(1, -1, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new PidGains(double.NaN, 1, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new PidGains(1, 1, -1));
            Check.Equal(0.0, default(PidGains).Ki);
            Check.Equal(0.0, new PidGains(2, double.PositiveInfinity, 0).Ki);
        }

        public void StepResponse_RejectsAStepTooLargeToRepresent()
        {
            Check.Throws<ArgumentException>(() => StepResponse.Analyze(new[] { -1.7e308, 0, 1.7e308, 1.7e308 }, 1));
        }
    }
}
