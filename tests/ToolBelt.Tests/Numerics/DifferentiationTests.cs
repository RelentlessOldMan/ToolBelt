using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class DifferentiationTests
    {
        public void UniformSampleQuadraticIsExact()
        {
            // f(x)=x^2 on [0,4] step 1 -> f'(x)=2x, exact for central + 2nd-order ends.
            var y = new double[] { 0, 1, 4, 9, 16 };
            var d = Differentiation.Sample(y, 1.0);
            for (int i = 0; i < d.Length; i++)
                Check.Close(2 * i, d[i], 1e-9, $"at {i}");
        }

        public void UnevenSampleQuadraticIsExact()
        {
            double[] x = { 0, 1, 3, 4, 7 };
            var y = new double[x.Length];
            for (int i = 0; i < x.Length; i++) y[i] = x[i] * x[i]; // f'(x)=2x
            var d = Differentiation.Sample(x, y);
            for (int i = 0; i < x.Length; i++)
                Check.Close(2 * x[i], d[i], 1e-7, $"at x={x[i]}");
        }

        public void FunctionDerivative()
        {
            Check.Close(Math.Cos(1.0), Differentiation.Derivative(Math.Sin, 1.0), 1e-7);
            Check.Close(Math.Exp(2.0), Differentiation.Derivative(Math.Exp, 2.0), 1e-4);
        }

        public void RichardsonBeatsPlainCentral()
        {
            // Richardson should be at least as accurate on a smooth function with a coarse step.
            double x = 1.3, h = 0.1;
            double exact = Math.Cos(x);
            double plain = Math.Abs(Differentiation.Derivative(Math.Sin, x, h) - exact);
            double rich = Math.Abs(Differentiation.RichardsonDerivative(Math.Sin, x, h) - exact);
            Check.True(rich < plain, $"Richardson {rich} should beat plain {plain}");
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => Differentiation.Sample(new double[] { 1 }, 1.0));
            Check.Throws<ArgumentOutOfRangeException>(() => Differentiation.Sample(new double[] { 1, 2 }, 0));
            Check.Throws<ArgumentException>(() => Differentiation.Sample(new double[] { 0, 1 }, new double[] { 0, 1 })); // <3
        }

        // Closed form: derivative of a random quadratic sampled at uneven points is exact.
        public void Property_ExactForQuadratics()
        {
            var rng = new Random(34);
            for (int t = 0; t < 500; t++)
            {
                double a = rng.NextDouble() * 4 - 2, b = rng.NextDouble() * 4 - 2, c = rng.NextDouble() * 4 - 2;
                double[] x = { 0, 0.5, 1.7, 3.1, 5.0, 6.2 };
                var y = new double[x.Length];
                for (int i = 0; i < x.Length; i++) y[i] = a * x[i] * x[i] + b * x[i] + c;

                var d = Differentiation.Sample(x, y);
                for (int i = 0; i < x.Length; i++)
                    Check.Close(2 * a * x[i] + b, d[i], 1e-7, $"t{t} at x={x[i]}");
            }
        }
    }
}
