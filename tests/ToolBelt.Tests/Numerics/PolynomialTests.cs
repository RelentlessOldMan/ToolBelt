using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class PolynomialTests
    {
        public void EvaluateHorner()
        {
            // 1 + 2x + 3x^2 at x=2 -> 1 + 4 + 12 = 17
            var p = new Polynomial(1, 2, 3);
            Check.Close(17, p.Evaluate(2), 1e-9);
            Check.Equal(2, p.Degree);
        }

        public void TrimsTrailingZeros()
        {
            var p = new Polynomial(1, 2, 0, 0);
            Check.Equal(1, p.Degree);
        }

        public void DerivativeAndIntegral()
        {
            var p = new Polynomial(3, 2, 1); // 3 + 2x + x^2
            var d = p.Derivative();          // 2 + 2x
            Check.True(d.Coefficients.SequenceEqual(new double[] { 2, 2 }));
            var integ = p.Derivative().Integral(3); // back to 3 + 2x + x^2
            Check.Close(3, integ.Evaluate(0), 1e-9);
            Check.Close(p.Evaluate(2.5), integ.Evaluate(2.5), 1e-9);
        }

        public void LinearRoot()
        {
            Check.Close(2, new Polynomial(-4, 2).RealRoots()[0], 1e-9); // 2x - 4 = 0 -> 2
        }

        public void QuadraticRoots()
        {
            var roots = new Polynomial(-6, 1, 1).RealRoots(); // x^2 + x - 6 -> -3, 2
            Check.Equal(2, roots.Length);
            Check.Close(-3, roots[0], 1e-9);
            Check.Close(2, roots[1], 1e-9);
            Check.Equal(0, new Polynomial(1, 0, 1).RealRoots().Length); // x^2 + 1 has no real roots
        }

        public void CubicRoots()
        {
            // (x-1)(x-2)(x-3) = -6 + 11x - 6x^2 + x^3
            var roots = new Polynomial(-6, 11, -6, 1).RealRoots();
            Check.Equal(3, roots.Length);
            Check.Close(1, roots[0], 1e-6);
            Check.Close(2, roots[1], 1e-6);
            Check.Close(3, roots[2], 1e-6);
        }

        public void CubicSingleRealRoot()
        {
            // x^3 + x + 1 has one real root near -0.6823
            var roots = new Polynomial(1, 1, 0, 1).RealRoots();
            Check.Equal(1, roots.Length);
            Check.Close(0, new Polynomial(1, 1, 0, 1).Evaluate(roots[0]), 1e-6);
        }

        public void FitRecoversPolynomial()
        {
            // Sample an exact quadratic and fit degree 2 -> recovers coefficients.
            Func<double, double> f = x => 2 - 3 * x + 0.5 * x * x;
            var xs = new double[] { -2, -1, 0, 1, 2, 3 };
            var ys = xs.Select(f).ToArray();
            var fit = Polynomial.Fit(xs, ys, 2);
            Check.Close(2, fit.Coefficients[0], 1e-6);
            Check.Close(-3, fit.Coefficients[1], 1e-6);
            Check.Close(0.5, fit.Coefficients[2], 1e-6);
        }

        public void FitLineThroughNoisyDataIsClose()
        {
            var xs = new double[] { 0, 1, 2, 3, 4 };
            var ys = new double[] { 1.1, 2.9, 5.1, 6.9, 9.1 }; // ~ 1 + 2x
            var fit = Polynomial.Fit(xs, ys, 1);
            Check.Close(1, fit.Coefficients[0], 0.2);
            Check.Close(2, fit.Coefficients[1], 0.1);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => Polynomial.Fit(new double[] { 1 }, new double[] { 1 }, 2)); // too few points
            Check.Throws<ArgumentException>(() => Polynomial.Fit(new double[] { 1, 2 }, new double[] { 1 }, 1)); // length mismatch
            Check.Throws<NotSupportedException>(() => new Polynomial(1, 0, 0, 0, 1).RealRoots()); // degree 4
        }
    }
}
