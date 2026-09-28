using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class IntegrationTests
    {
        private static double[] Sample(Func<double, double> f, double a, double b, int n)
        {
            var y = new double[n + 1];
            double dx = (b - a) / n;
            for (int i = 0; i <= n; i++) y[i] = f(a + i * dx);
            return y;
        }

        public void TrapezoidLinearIsExact()
        {
            // ∫0^10 (2x+1) dx = 110
            var y = Sample(x => 2 * x + 1, 0, 10, 5);
            Check.Close(110, Integration.Trapezoid(y, 2.0), 1e-9);
        }

        public void TrapezoidUneven()
        {
            double[] x = { 0, 1, 3, 6 };
            double[] y = { 0, 1, 3, 6 }; // f(x)=x
            Check.Close(18, Integration.Trapezoid(x, y), 1e-9); // ∫0^6 x dx = 18
        }

        public void SimpsonCubicIsExact()
        {
            // Simpson integrates cubics exactly: ∫0^2 x^3 dx = 4
            var y = Sample(x => x * x * x, 0, 2, 4);
            Check.Close(4, Integration.Simpson(y, 0.5), 1e-9);
        }

        public void SimpsonOddIntervalsFallsBack()
        {
            // 5 intervals (odd) over ∫0^1 x^2 dx = 1/3; still close.
            var y = Sample(x => x * x, 0, 1, 5);
            Check.Close(1.0 / 3.0, Integration.Simpson(y, 0.2), 5e-3); // trailing trapezoid loses some accuracy
        }

        public void AdaptiveSimpsonSine()
        {
            Check.Close(2.0, Integration.AdaptiveSimpson(Math.Sin, 0, Math.PI), 1e-9);
            Check.Close(Math.E - 1, Integration.AdaptiveSimpson(Math.Exp, 0, 1), 1e-9);
            Check.Close(0, Integration.AdaptiveSimpson(Math.Sin, 0, 2 * Math.PI), 1e-9);
        }

        public void CumulativeTrapezoid()
        {
            // f(x)=1 over dx=1 -> running integral is 0,1,2,3,4
            var y = new double[] { 1, 1, 1, 1, 1 };
            var cum = Integration.CumulativeTrapezoid(y, 1.0);
            Check.True(cum.SequenceEqual(new double[] { 0, 1, 2, 3, 4 }));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => Integration.Trapezoid(new double[] { 1 }, 1.0));
            Check.Throws<ArgumentOutOfRangeException>(() => Integration.Trapezoid(new double[] { 1, 2 }, 0));
            Check.Throws<ArgumentException>(() => Integration.Trapezoid(new double[] { 0, 1 }, new double[] { 0, 1, 2 }));
        }

        // Closed form: ∫a^b of a random cubic equals its antiderivative difference; Simpson must match.
        public void Property_SimpsonExactForCubics()
        {
            var rng = new Random(33);
            for (int t = 0; t < 500; t++)
            {
                double c3 = rng.NextDouble() * 4 - 2, c2 = rng.NextDouble() * 4 - 2;
                double c1 = rng.NextDouble() * 4 - 2, c0 = rng.NextDouble() * 4 - 2;
                Func<double, double> f = x => c3 * x * x * x + c2 * x * x + c1 * x + c0;
                Func<double, double> F = x => c3 * x * x * x * x / 4 + c2 * x * x * x / 3 + c1 * x * x / 2 + c0 * x;

                double a = 0, b = 4;
                int n = 8; // even intervals
                var y = Sample(f, a, b, n);
                Check.Close(F(b) - F(a), Integration.Simpson(y, (b - a) / n), 1e-7, $"t{t}");
            }
        }
    }
}
