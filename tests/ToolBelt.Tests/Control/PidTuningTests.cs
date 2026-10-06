using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Control;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    public sealed class PidTuningTests
    {
        // Euler simulation of K·e^(−θs)/(τs + 1) driven by u(t); dt must divide θ for an exact delay.
        private sealed class FopdtPlant
        {
            private readonly double _k, _tau, _dt;
            private readonly Queue<double> _delay = new Queue<double>();
            public double Y;

            public FopdtPlant(double k, double tau, double theta, double dt)
            {
                _k = k; _tau = tau; _dt = dt;
                for (int i = 0; i < (int)Math.Round(theta / dt); i++) _delay.Enqueue(0);
            }

            public double Step(double u)
            {
                _delay.Enqueue(u);
                double delayed = _delay.Dequeue();
                // Exact zero-order-hold discretisation, so the fit sees a true FOPDT response.
                Y += (1 - Math.Exp(-_dt / _tau)) * (_k * delayed - Y);
                return Y;
            }
        }

        public void FitRecoversTrueModel()
        {
            foreach (var (k, tau, theta) in new[] { (2.0, 10.0, 3.0), (-0.5, 4.0, 0.0), (7.0, 1.0, 5.0) })
            {
                double dt = 0.001;
                var plant = new FopdtPlant(k, tau, theta, dt);
                var times = new List<double> { 0 };
                var values = new List<double> { 0 };
                for (int i = 1; i <= (theta + 12 * tau) / dt; i++) { times.Add(i * dt); values.Add(plant.Step(1.5)); }
                // The plant output lags the input by one step (Euler sample-and-hold), i.e. dead time + dt.
                var model = FopdtModel.FitStep(times, values, inputStep: 1.5, finalValue: k * 1.5);
                Check.Close(k, model.Gain, 1e-12, $"K {k}");
                Check.Close(tau, model.TimeConstant, 2 * dt, $"τ {tau}");
                Check.Close(theta + dt, model.DeadTime, 2 * dt, $"θ {theta}");
            }
        }

        public void ModelStepResponseIsTheFopdtCurve()
        {
            var m = new FopdtModel(2, 5, 1);
            Check.Equal(0.0, m.StepResponse(0.5));
            Check.Close(2 * (1 - Math.Exp(-1)), m.StepResponse(6), 1e-12);
            Check.Close(-6 * (1 - Math.Exp(-1)), m.StepResponse(6, inputStep: -3), 1e-12);
            // Fitting the model's own sampled curve gives the model back.
            var t = Enumerable.Range(0, 60_001).Select(i => i * 0.001).ToArray();
            var fit = FopdtModel.FitStep(t, t.Select(x => 10 + m.StepResponse(x)).ToArray(), 1, finalValue: 12);
            Check.Close(5, fit.TimeConstant, 1e-5);
            Check.Close(1, fit.DeadTime, 1e-5);
        }

        public void RulesMatchPublishedFormulas()
        {
            var m = new FopdtModel(2, 10, 2);
            var zn = PidTuning.ZieglerNichols(m);
            Check.Close(1.2 * 10 / (2 * 2), zn.Kp, 1e-12);
            Check.Close(4, zn.IntegralTime, 1e-12);
            Check.Close(1, zn.DerivativeTime, 1e-12);
            Check.Close(zn.Kp / 4, zn.Ki, 1e-12);
            Check.Close(zn.Kp * 1, zn.Kd, 1e-12);
            var znPi = PidTuning.ZieglerNichols(m, includeDerivative: false);
            Check.Close(0.9 * 2.5, znPi.Kp, 1e-12);
            Check.Close(2 / 0.3, znPi.IntegralTime, 1e-12);
            Check.Equal(0.0, znPi.Kd);
            var cc = PidTuning.CohenCoon(m);
            Check.Close(10 / 4.0 * (4.0 / 3 + 0.05), cc.Kp, 1e-12);
            Check.Close(2 * (32 + 1.2) / (13 + 1.6), cc.IntegralTime, 1e-12);
            Check.Close(8 / 11.4, cc.DerivativeTime, 1e-12);
            var ccPi = PidTuning.CohenCoon(m, includeDerivative: false);
            Check.Close(10 / 4.0 * (0.9 + 0.2 / 12), ccPi.Kp, 1e-12);
            Check.Close(2 * 30.6 / 13, ccPi.IntegralTime, 1e-12);
            var simc = PidTuning.Simc(m);
            Check.Close(10 / (2 * 4.0), simc.Kp, 1e-12);
            Check.Close(10, simc.IntegralTime, 1e-12);                 // min(τ = 10, 4·(2 + 2) = 16)
            Check.Close(8, PidTuning.Simc(m, closedLoopTimeConstant: 0).IntegralTime, 1e-12);
            var lambda = PidTuning.Lambda(m, 3);
            Check.Close(10 / (2 * 5.0), lambda.Kp, 1e-12);
            Check.Close(10, lambda.IntegralTime, 1e-12);
            var ult = PidTuning.ZieglerNicholsUltimate(4, 8);
            Check.Close(2.4, ult.Kp, 1e-12); Check.Close(4, ult.IntegralTime, 1e-12); Check.Close(1, ult.DerivativeTime, 1e-12);
            var ultPi = PidTuning.ZieglerNicholsUltimate(4, 8, includeDerivative: false);
            Check.Close(1.8, ultPi.Kp, 1e-12); Check.Close(8 / 1.2, ultPi.IntegralTime, 1e-12);
            Check.Equal(0.0, new PidGains(1, double.PositiveInfinity, 0).Ki);
        }

        public void TunedLoopsAreStableAndSimcIsGentle()
        {
            // Identify a plant from a step test, tune, then close the loop on the same plant.
            const double dt = 0.01, k = 1.8, tau = 8, theta = 2;
            var test = new FopdtPlant(k, tau, theta, dt);
            var t = new List<double> { 0 };
            var y = new List<double> { 0 };
            for (int i = 1; i <= 8000; i++) { t.Add(i * dt); y.Add(test.Step(1)); }
            var model = FopdtModel.FitStep(t, y, 1);

            foreach (var (name, gains, maxOvershoot) in new[]
            {
                ("SIMC", PidTuning.Simc(model), 15.0),
                ("Lambda", PidTuning.Lambda(model), 5.0),
                ("ZN", PidTuning.ZieglerNichols(model), 80.0),
                ("CohenCoon", PidTuning.CohenCoon(model), 90.0),
            })
            {
                var pid = new PidController(gains.Kp, gains.Ki, gains.Kd) { Setpoint = 1 };
                var plant = new FopdtPlant(k, tau, theta, dt);
                var response = new List<double>();
                for (int i = 0; i < 20_000; i++) response.Add(plant.Step(pid.Update(plant.Y, dt)));
                var info = StepResponse.Analyze(response, dt, finalValue: 1, initialValue: 0);
                Check.False(double.IsNaN(info.SettlingTime), $"{name} never settles");
                Check.True(info.OvershootPercent <= maxOvershoot, $"{name} overshoot {info.OvershootPercent:F1}%");
            }
        }

        public void RejectsBadInput()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new FopdtModel(0, 1, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new FopdtModel(1, 0, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new FopdtModel(1, 1, -1));
            Check.Throws<ArgumentException>(() => PidTuning.ZieglerNichols(new FopdtModel(1, 1, 0)));
            Check.Throws<ArgumentException>(() => PidTuning.CohenCoon(default));
            Check.Throws<ArgumentOutOfRangeException>(() => PidTuning.Simc(new FopdtModel(1, 1, 0), 0));
            Check.Throws<ArgumentOutOfRangeException>(() => PidTuning.Lambda(new FopdtModel(1, 1, 0), -1));
            Check.Throws<ArgumentOutOfRangeException>(() => PidTuning.ZieglerNicholsUltimate(0, 1));
            Check.Throws<ArgumentException>(() => FopdtModel.FitStep(new[] { 0.0, 1, 2 }, new[] { 1.0, 1, 1 }, 1));
            Check.Throws<ArgumentException>(() => FopdtModel.FitStep(new[] { 0.0, 1, 2 }, new[] { 0.0, 0.1, 0.2 }, 1, finalValue: 1));
            Check.Throws<ArgumentOutOfRangeException>(() => FopdtModel.FitStep(new[] { 0.0, 1, 2 }, new[] { 0.0, 0.5, 1 }, 0));
            Check.Throws<ArgumentException>(() => FopdtModel.FitStep(new[] { 0.0, 2, 1 }, new[] { 0.0, 0.5, 1 }, 1));
        }
    }
}
