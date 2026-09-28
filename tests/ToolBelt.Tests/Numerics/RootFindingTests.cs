using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class RootFindingTests
    {
        private static readonly Func<double, double> Sqrt2 = x => x * x - 2;   // root sqrt(2)
        private static readonly Func<double, double> CosX = x => Math.Cos(x) - x; // Dottie number ~0.739085

        public void Bisection()
        {
            var r = RootFinding.Bisection(Sqrt2, 0, 2);
            Check.True(r.Converged);
            Check.Close(Math.Sqrt(2), r.Root, 1e-9);
        }

        public void BisectionRequiresBracket()
        {
            var r = RootFinding.Bisection(Sqrt2, 2, 3); // both positive
            Check.False(r.Converged);
            Check.True(r.Message.Length > 0);
        }

        public void Brent()
        {
            var r = RootFinding.Brent(CosX, 0, 1);
            Check.True(r.Converged);
            Check.Close(0.7390851332151607, r.Root, 1e-9);
        }

        public void NewtonWithAnalyticDerivative()
        {
            var r = RootFinding.Newton(Sqrt2, 1.0, x => 2 * x);
            Check.True(r.Converged);
            Check.Close(Math.Sqrt(2), r.Root, 1e-9);
        }

        public void NewtonWithNumericDerivative()
        {
            var r = RootFinding.Newton(CosX, 0.5);
            Check.True(r.Converged);
            Check.Close(0.7390851332151607, r.Root, 1e-8);
        }

        public void NewtonReportsVanishingDerivative()
        {
            // f(x)=x^2 has f'(0)=0; starting exactly at 0 stalls.
            var r = RootFinding.Newton(x => x * x, 0.0, x => 2 * x);
            // At x=0, f=0 already within tolerance -> converged at the root; use a flat function instead.
            var flat = RootFinding.Newton(x => 5.0, 1.0, x => 0.0);
            Check.False(flat.Converged);
        }

        public void Secant()
        {
            var r = RootFinding.Secant(Sqrt2, 1.0, 2.0);
            Check.True(r.Converged);
            Check.Close(Math.Sqrt(2), r.Root, 1e-9);
        }

        public void NullFunction_Throws()
        {
            Check.Throws<ArgumentNullException>(() => RootFinding.Bisection(null!, 0, 1));
        }

        // Differential: on random monotone cubics with a known bracket, every method finds the same root.
        public void Property_MethodsAgree()
        {
            var rng = new Random(35);
            for (int t = 0; t < 300; t++)
            {
                double root = rng.NextDouble() * 6 - 3;
                double slope = 1 + rng.NextDouble() * 3; // positive -> strictly increasing
                Func<double, double> f = x => slope * (x - root) + (x - root) * (x - root) * (x - root);

                double a = root - 2, b = root + 2; // f(a)<0<f(b) since increasing through the root
                var brent = RootFinding.Brent(f, a, b);
                var bisect = RootFinding.Bisection(f, a, b);
                var secant = RootFinding.Secant(f, a, b);

                Check.True(brent.Converged && bisect.Converged && secant.Converged, $"t{t}: convergence");
                Check.Close(root, brent.Root, 1e-6, $"t{t}: brent");
                Check.Close(root, bisect.Root, 1e-6, $"t{t}: bisection");
                Check.Close(root, secant.Root, 1e-6, $"t{t}: secant");
            }
        }
    }
}
