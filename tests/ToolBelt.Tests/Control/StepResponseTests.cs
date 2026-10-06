using System;
using System.Linq;
using ToolBelt.Control;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    public sealed class StepResponseTests
    {
        // Unit step response of ωn² / (s² + 2ζωn s + ωn²), underdamped.
        private static double SecondOrder(double t, double zeta, double wn)
        {
            double wd = wn * Math.Sqrt(1 - zeta * zeta);
            return 1 - Math.Exp(-zeta * wn * t) * (Math.Cos(wd * t) + zeta / Math.Sqrt(1 - zeta * zeta) * Math.Sin(wd * t));
        }

        // Independent root finder: bisection on a bracket for f(t) = level.
        private static double Solve(Func<double, double> f, double level, double lo, double hi)
        {
            for (int i = 0; i < 200; i++)
            {
                double mid = (lo + hi) / 2;
                if ((f(lo) - level) * (f(mid) - level) <= 0) hi = mid; else lo = mid;
            }
            return (lo + hi) / 2;
        }

        public void SecondOrderMatchesAnalyticFormulas()
        {
            foreach (var (zeta, wn) in new[] { (0.2, 3.0), (0.5, 1.0), (0.7, 10.0), (0.05, 2.0) })
            {
                double dt = 3e-4 / wn, horizon = 12 / (zeta * wn);
                var y = Enumerable.Range(0, (int)(horizon / dt)).Select(i => SecondOrder(i * dt, zeta, wn)).ToArray();
                var info = StepResponse.Analyze(y, dt, finalValue: 1, initialValue: 0);
                double wd = wn * Math.Sqrt(1 - zeta * zeta);
                string tag = $"ζ={zeta}";
                Check.Close(100 * Math.Exp(-Math.PI * zeta / Math.Sqrt(1 - zeta * zeta)), info.OvershootPercent, 1e-4, tag + " overshoot");
                Check.Close(Math.PI / wd, info.PeakTime, 2 * dt, tag + " peak time");
                Func<double, double> f = t => SecondOrder(t, zeta, wn);
                double t10 = Solve(f, 0.1, 0, Math.PI / wd / 2), t90 = Solve(f, 0.9, 0, Math.PI / wd);
                Check.Close(t90 - t10, info.RiseTime, 1e-6 / wn, tag + " rise");
                // Settling: the envelope crosses 2% somewhere; check the defining property on a 10× finer grid.
                double ts = info.SettlingTime;
                for (double t = ts + dt / 10; t < horizon; t += dt / 10) Check.True(Math.Abs(f(t) - 1) <= 0.02 + 1e-9, $"{tag} outside the band at {t} > ts {ts}");
                bool outsideJustBefore = false;
                for (double t = ts - 4 * dt; t < ts; t += dt / 20) outsideJustBefore |= Math.Abs(f(t) - 1) > 0.02 - 1e-9;
                Check.True(outsideJustBefore, tag + " settling time is not the last exit");
                Check.Equal(0.0, info.UndershootPercent);
            }
        }

        public void FirstOrderMatchesClosedForm()
        {
            const double tau = 2.5, dt = 0.001;
            var y = Enumerable.Range(0, 40_000).Select(i => 3 + 5 * (1 - Math.Exp(-i * dt / tau))).ToArray();
            var info = StepResponse.Analyze(y, dt, finalValue: 8);
            Check.Close(tau * Math.Log(9), info.RiseTime, 1e-5);
            Check.Close(tau * Math.Log(50), info.SettlingTime, 1e-5);
            Check.Equal(0.0, info.OvershootPercent);
            Check.Equal(3.0, info.InitialValue);
        }

        public void FallingStepAndUndershoot()
        {
            const double dt = 0.01;
            // Non-minimum-phase plant (1 − s)/(s + 1)²: unit step response 1 − e^(−t) − 2t·e^(−t) dips to 1 − 2/√e at t = 0.5.
            // Scaled to a fall from 10 to 4 it first moves the wrong way (up).
            var y = Enumerable.Range(0, 3000).Select(i =>
            {
                double t = i * dt;
                return 10 - 6 * (1 - Math.Exp(-t) - 2 * t * Math.Exp(-t));
            }).ToArray();
            var info = StepResponse.Analyze(y, dt, finalValue: 4);
            Check.Close(100 * (2 / Math.Sqrt(Math.E) - 1), info.UndershootPercent, 1e-9);
            Check.Close(4, info.FinalValue, 0);
            var clean = Enumerable.Range(0, 3000).Select(i => 10 - 6 * (1 - Math.Exp(-i * dt))).ToArray();
            var ci = StepResponse.Analyze(clean, dt, finalValue: 4);
            Check.Close(Math.Log(9), ci.RiseTime, 1e-4);
            Check.Equal(0.0, ci.UndershootPercent);
        }

        public void NotSettledOrNotRisenIsNaN()
        {
            var ramp = Enumerable.Range(0, 100).Select(i => i / 100.0).ToArray();
            var info = StepResponse.Analyze(ramp, 1, finalValue: 2);       // only ever reaches half the target
            Check.True(double.IsNaN(info.RiseTime));
            Check.True(double.IsNaN(info.SettlingTime));
            var instant = new[] { 0.0, 1, 1, 1 };
            var i2 = StepResponse.Analyze(instant, 1);
            Check.Close(0.8, i2.RiseTime, 1e-12);                           // 0.1 → 0.9 inside the first interval
            Check.Close(0.98, i2.SettlingTime, 1e-12);
        }

        public void IrregularTimesMatchUniformResult()
        {
            var t = Enumerable.Range(0, 2001).Select(i => 5 + i * 0.005).ToArray();     // step at t = 5
            var y = t.Select(x => SecondOrder(x - 5, 0.4, 2)).ToArray();
            var a = StepResponse.Analyze(t, y, finalValue: 1, initialValue: 0);
            var b = StepResponse.Analyze(y, 0.005, finalValue: 1, initialValue: 0);
            Check.Close(b.RiseTime, a.RiseTime, 1e-9);
            Check.Close(b.SettlingTime, a.SettlingTime, 1e-9);
            Check.Close(b.PeakTime, a.PeakTime, 1e-9);
        }

        public void RejectsBadInput()
        {
            Check.Throws<ArgumentException>(() => StepResponse.Analyze(new[] { 1.0, 1, 1 }, 1));
            Check.Throws<ArgumentException>(() => StepResponse.Analyze(new[] { 1.0 }, 1));
            Check.Throws<ArgumentException>(() => StepResponse.Analyze(new[] { 0.0, 1 }, new[] { 0.0, 1, 2 }));
            Check.Throws<ArgumentException>(() => StepResponse.Analyze(new[] { 0.0, 0 }, new[] { 0.0, 1 }));
            Check.Throws<ArgumentException>(() => StepResponse.Analyze(new[] { 0.0, double.NaN, 1 }, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => StepResponse.Analyze(new[] { 0.0, 1 }, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => StepResponse.Analyze(new[] { 0.0, 1 }, 1, settlingBand: 0));
            Check.Throws<ArgumentOutOfRangeException>(() => StepResponse.Analyze(new[] { 0.0, 1 }, 1, riseLow: 0.9, riseHigh: 0.1));
            Check.Throws<ArgumentNullException>(() => StepResponse.Analyze(null!, 1));
        }
    }
}
