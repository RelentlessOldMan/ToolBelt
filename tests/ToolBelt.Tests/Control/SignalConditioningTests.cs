using System;
using System.Linq;
using ToolBelt.Control;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    public sealed class SignalConditioningTests
    {
        public void SlewRateLimiter_RampsAtTheConfiguredRates()
        {
            var s = new SlewRateLimiter(maxRisePerSecond: 2, maxFallPerSecond: 5);
            Check.Equal(10.0, s.Update(10, 0.1));                       // first value initialises
            Check.False(s.IsLimiting);
            for (int i = 1; i <= 10; i++)
            {
                Check.Close(10 + 0.2 * i, s.Update(100, 0.1), 1e-12, $"rise {i}");
                Check.True(s.IsLimiting);
            }
            Check.Close(11.5, s.Update(11.5, 0.1), 1e-12, "within reach: lands exactly");
            Check.False(s.IsLimiting);
            Check.Close(11.0, s.Update(-100, 0.1), 1e-12, "falls at 5/s");
            Check.Close(11.0, s.Update(-100, 0), 1e-12, "dt = 0 moves nothing");
            s.Reset(-3);
            Check.Equal(-3.0, s.Value);
            s.Reset();
            Check.False(s.HasValue);
            var unlimited = new SlewRateLimiter(double.PositiveInfinity);
            unlimited.Update(0, 1);
            Check.Equal(1e9, unlimited.Update(1e9, 0.001));
            Check.Equal(5.0, unlimited.Update(5, 0), "an unlimited rate follows even when no time passes (∞ × 0 is not NaN)");
            Check.False(unlimited.IsLimiting);
        }

        public void SlewRateLimiter_MatchesNaiveClampDifferentially()
        {
            var rng = new Random(11);
            var s = new SlewRateLimiter(3, 1.5);
            double model = 0;
            s.Reset(0);
            for (int i = 0; i < 5000; i++)
            {
                double target = rng.NextDouble() * 20 - 10, dt = rng.NextDouble() * 0.5;
                model = Math.Max(model - 1.5 * dt, Math.Min(model + 3 * dt, target));
                Check.Close(model, s.Update(target, dt), 1e-9, $"[{i}]");
            }
        }

        public void SchmittTrigger_IgnoresChatterInsideTheBand()
        {
            var noisy = new[] { 0.0, 0.9, 1.1, 0.95, 1.05, 2.1, 1.5, 1.2, 1.9, 0.99, 1.0, 1.01, 2.0 };
            var state = SchmittTrigger.Apply(noisy, lowThreshold: 1, highThreshold: 2);
            Check.Equal("FFFFFTTTTFFFT", new string(state.Select(b => b ? 'T' : 'F').ToArray()));
            var t = new SchmittTrigger(1, 2);
            t.Update(2.5);
            Check.True(t.Changed && t.State);
            t.Update(1.5);
            Check.False(t.Changed);
            t.Update(double.NaN);
            Check.True(t.State, "NaN leaves the state alone");
            t.Reset();
            Check.False(t.State || t.Changed);
            Check.Throws<ArgumentException>(() => new SchmittTrigger(2, 1));
            Check.Throws<ArgumentException>(() => new SchmittTrigger(double.NaN, 1));
            // Equal thresholds degenerate to a plain comparator.
            Check.Equal(3, SchmittTrigger.Apply(new[] { 0.0, 1, 0, 1, 0, 1 }, 0.5, 0.5).Count(b => b));
        }

        public void Deadband_ShapesAroundTheCentre()
        {
            Check.Equal(0.0, Deadband.Apply(0.3, 0.5));
            Check.Equal(0.0, Deadband.Apply(-0.5, 0.5));
            Check.Close(0.25, Deadband.Apply(0.75, 0.5), 1e-15);
            Check.Close(-0.25, Deadband.Apply(-0.75, 0.5), 1e-15);
            Check.Equal(0.75, Deadband.Apply(0.75, 0.5, continuous: false));
            Check.Equal(10.0, Deadband.Apply(10.4, 0.5, center: 10));
            Check.Close(11.5, Deadband.Apply(12, 0.5, center: 10), 1e-15);
            // Continuity at the band edge.
            Check.Close(10, Deadband.Apply(10.5 + 1e-12, 0.5, center: 10), 1e-11);
            Check.Throws<ArgumentOutOfRangeException>(() => Deadband.Apply(1, -1));
        }

        public void DeadbandFilter_ReportsOnlyRealChanges()
        {
            var f = new DeadbandFilter(0.5);
            var readings = new[] { 10.0, 10.2, 10.4, 10.6, 10.3, 10.15, 9.0, double.NaN, 9.4 };
            var reported = readings.Select(f.Update).ToArray();
            Check.Equal("TFFTFFTFF", new string(reported.Select(b => b ? 'T' : 'F').ToArray()));
            Check.Equal(9.0, f.Value);
            f.Reset();
            Check.True(f.Update(1) && f.Value == 1);
            Check.Throws<ArgumentOutOfRangeException>(() => new DeadbandFilter(-1));
        }

        public void FirstOrderLag_IsRateIndependent()
        {
            // The same 3 s of a unit step, sampled at 1 kHz or irregularly, reaches the same 1 − e^(−3/τ).
            const double tau = 1.5;
            var fine = new FirstOrderLag(tau);
            fine.Update(0, 0);
            for (int i = 0; i < 3000; i++) fine.Update(1, 0.001);
            var rng = new Random(4);
            var coarse = new FirstOrderLag(tau);
            coarse.Update(0, 0);
            double elapsed = 0;
            while (elapsed < 3)
            {
                double dt = Math.Min(3 - elapsed, rng.NextDouble() * 0.4);
                coarse.Update(1, dt);
                elapsed += dt;
            }
            Check.Close(1 - Math.Exp(-3 / tau), fine.Value, 1e-12);
            Check.Close(fine.Value, coarse.Value, 1e-12);
            Check.Close(1 / (2 * Math.PI * 50), FirstOrderLag.FromCutoff(50).TimeConstant, 1e-15);
            var pass = new FirstOrderLag(0);
            pass.Update(1, 1);
            Check.Equal(7.0, pass.Update(7, 0.01));
            var tiny = new FirstOrderLag(1);
            tiny.Reset(0);
            Check.Close(1e-9 - 5e-19, tiny.Update(1, 1e-9), 1e-24, "tiny steps keep full precision (1 - e^-x, not rounded to x)");
            Check.Throws<ArgumentOutOfRangeException>(() => new FirstOrderLag(-1));
            Check.Throws<ArgumentOutOfRangeException>(() => tiny.Update(1, -1));
            Check.Throws<ArgumentOutOfRangeException>(() => FirstOrderLag.FromCutoff(0));
        }
    }
}
