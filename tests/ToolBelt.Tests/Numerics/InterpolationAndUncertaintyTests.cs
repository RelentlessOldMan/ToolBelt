using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>Akima spline, scattered-data interpolation, and uncertainty rounding.</summary>
    public sealed class InterpolationAndUncertaintyTests
    {
        // ---------- Akima ----------

        public void Akima_PassesThroughPointsAndReproducesLines()
        {
            double[] x = { 0, 1, 2.5, 4, 4.5, 7 };
            double[] lin = x.Select(v => 3 - 2 * v).ToArray();
            foreach (bool modified in new[] { false, true })
            {
                var s = new AkimaSpline(x, lin, modified);
                for (int i = 0; i < x.Length; i++) Check.Close(lin[i], s.Interpolate(x[i]), 1e-12);
                foreach (double t in new[] { 0.3, 1.7, 3.3, 6.9 })
                {
                    Check.Close(3 - 2 * t, s.Interpolate(t), 1e-12);
                    Check.Close(-2, s.Derivative(t), 1e-12);
                }
            }
        }

        public void Akima_StepDoesNotRing()
        {
            double[] x = Enumerable.Range(0, 10).Select(i => (double)i).ToArray();
            double[] y = { 0, 0, 0, 0, 0, 1, 1, 1, 1, 1 };
            var s = new AkimaSpline(x, y);
            for (double t = 0; t <= 9; t += 0.01)
            {
                double v = s.Interpolate(t);
                Check.True(v >= -1e-12 && v <= 1 + 1e-12, $"overshoot {v} at {t}");
            }
        }

        public void Makima_AvoidsUndershootWhereClassicAkimaHasIt()
        {
            double[] x = Enumerable.Range(0, 8).Select(i => (double)i).ToArray();
            double[] y = { 0, 0, 0, 0, 1, 2, 3, 4 };                          // flat run, then a ramp
            double MinOn(AkimaSpline s) => Enumerable.Range(0, 101).Select(i => s.Interpolate(2 + i / 100.0)).Min();
            Check.True(MinOn(new AkimaSpline(x, y)) < -1e-3, "classic Akima dips below the flat run");
            Check.True(MinOn(new AkimaSpline(x, y, modified: true)) >= -1e-12, "makima stays flat");
        }

        public void Akima_DerivativeIsContinuousAndMatchesFiniteDifferences()
        {
            var rng = new DeterministicRandom(1);
            double[] x = Enumerable.Range(0, 12).Select(i => i + rng.NextDouble() * 0.5).ToArray();
            double[] y = x.Select(v => Math.Sin(v) + rng.NextDouble() * 0.2).ToArray();
            var s = new AkimaSpline(x, y);
            for (int i = 1; i < x.Length - 1; i++)
            {
                double left = (s.Interpolate(x[i]) - s.Interpolate(x[i] - 1e-7)) / 1e-7;
                double right = (s.Interpolate(x[i] + 1e-7) - s.Interpolate(x[i])) / 1e-7;
                Check.Close(s.Slopes[i], left, 1e-5);
                Check.Close(s.Slopes[i], right, 1e-5);
            }
            double t = (x[3] + x[4]) / 2;
            Check.Close((s.Interpolate(t + 1e-6) - s.Interpolate(t - 1e-6)) / 2e-6, s.Derivative(t), 1e-6);
        }

        public void Akima_SmallInputsAndClamping()
        {
            var two = new AkimaSpline(new double[] { 0, 2 }, new double[] { 1, 5 });
            Check.Close(3, two.Interpolate(1), 1e-15);
            var three = new AkimaSpline(new double[] { 0, 1, 2 }, new double[] { 0, 1, 4 });
            Check.Close(1, three.Interpolate(1), 1e-15);
            Check.Equal(0.0, three.Interpolate(-5));
            Check.Equal(4.0, three.Interpolate(50));
            Check.Throws<ArgumentException>(() => new AkimaSpline(new double[] { 0, 0 }, new double[] { 1, 2 }));
            Check.Throws<ArgumentException>(() => new AkimaSpline(new double[] { 0 }, new double[] { 1 }));
        }

        // ---------- scattered interpolation ----------

        private static readonly (double X, double Y, double Value)[] Samples =
        {
            (0, 0, 10), (4, 0, 20), (0, 4, 30), (4, 4, 40), (2, 2, 25),
        };

        public void Idw_ExactBoundedAndSymmetric()
        {
            foreach (var s in Samples) Check.Equal(s.Value, ScatteredInterpolation.InverseDistance(Samples, s.X, s.Y));
            var rng = new DeterministicRandom(2);
            for (int i = 0; i < 200; i++)
            {
                double v = ScatteredInterpolation.InverseDistance(Samples, rng.NextDouble() * 6 - 1, rng.NextDouble() * 6 - 1, power: 1 + rng.NextDouble() * 3);
                Check.True(v >= 10 && v <= 40, $"{v} outside the data range");
            }
            var two = new[] { (0.0, 0.0, 1.0), (2.0, 0.0, 3.0) };
            Check.Close(2, ScatteredInterpolation.InverseDistance(two, 1, 0), 1e-15);   // equidistant: plain average
            var flat = Samples.Select(s => (s.X, s.Y, 7.0)).ToArray();
            Check.Close(7, ScatteredInterpolation.InverseDistance(flat, 1.3, 2.9), 1e-12);
        }

        public void Idw_NearestAndRadius()
        {
            Check.Equal(10.0, ScatteredInterpolation.InverseDistance(Samples, 0.4, 0.3, nearest: 1));
            Check.True(double.IsNaN(ScatteredInterpolation.InverseDistance(Samples, 50, 50, radius: 3)));
            Check.Equal(20.0, ScatteredInterpolation.InverseDistance(Samples, 4.5, 0, radius: 1));
        }

        public void ToGrid_ShapeAndNodes()
        {
            double[,] g = ScatteredInterpolation.ToGrid(Samples, 0, 4, 5, 0, 4, 3);
            Check.Equal(3, g.GetLength(0));
            Check.Equal(5, g.GetLength(1));
            Check.Equal(10.0, g[0, 0]);                                       // (0, 0)
            Check.Equal(20.0, g[0, 4]);                                       // (4, 0)
            Check.Equal(40.0, g[2, 4]);                                       // (4, 4)
            Check.Equal(25.0, g[1, 2]);                                       // (2, 2)
            Check.Throws<ArgumentOutOfRangeException>(() => ScatteredInterpolation.ToGrid(Samples, 0, 1, 0, 0, 1, 1));
            Check.Throws<ArgumentException>(() => ScatteredInterpolation.InverseDistance(Array.Empty<(double, double, double)>(), 0, 0));
        }

        // ---------- uncertainty ----------

        public void Uncertainty_ParticleDataGroupRule()
        {
            Check.Equal((12.346, 0.012, 3), Uncertainty.Round(12.3456, 0.0123));
            Check.Equal(0.035, Uncertainty.Round(1, 0.0354).Uncertainty);   // 354 → two digits
            Check.Equal(0.04, Uncertainty.Round(1, 0.0355).Uncertainty);    // 355 → one digit
            Check.Equal(0.09, Uncertainty.Round(1, 0.0949).Uncertainty);
            var big = Uncertainty.Round(1, 0.096);                           // 950–999 → 0.10
            Check.Equal(0.10, big.Uncertainty);
            Check.Equal(2, big.Decimals);
        }

        public void Uncertainty_OtherRulesAndCarry()
        {
            Check.Equal(0.10, Uncertainty.Round(5, 0.0996, UncertaintyRule.TwoSignificantDigits).Uncertainty);
            Check.Equal(2, Uncertainty.Round(5, 0.0996, UncertaintyRule.TwoSignificantDigits).Decimals);
            Check.Equal(0.1, Uncertainty.Round(5, 0.096, UncertaintyRule.OneSignificantDigit).Uncertainty);
            Check.Equal((123500.0, 1200.0, -2), Uncertainty.Round(123456, 1234));
        }

        public void Uncertainty_Formatting()
        {
            Check.Equal("12.346 ± 0.012", Uncertainty.Format(12.3456, 0.0123));
            Check.Equal("12.346(12)", Uncertainty.Format(12.3456, 0.0123, UncertaintyStyle.Parenthetical));
            Check.Equal("-3.1416 ± 0.0020", Uncertainty.Format(-3.14159, 0.002));
            Check.Equal("123500 ± 1200", Uncertainty.Format(123456, 1234));
            Check.Equal("(1.235 ± 0.030)e-7", Uncertainty.Format(1.23456e-7, 3e-9));
            Check.Equal("1.235(30)e-7", Uncertainty.Format(1.23456e-7, 3e-9, UncertaintyStyle.Parenthetical));
            Check.Equal("(1.23457 ± 0.00023)e7", Uncertainty.Format(12345678.9, 2345));
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Check.Equal("12.346 ± 0.012", Uncertainty.Format(12.3456, 0.0123));
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }

        public void Uncertainty_Validation()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Uncertainty.Round(1, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => Uncertainty.Round(1, -1));
            Check.Throws<ArgumentOutOfRangeException>(() => Uncertainty.Round(double.NaN, 1));
        }
    }
}
