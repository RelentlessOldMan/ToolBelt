using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class MonotoneCubicTests
    {
        public void PassesThroughKnots()
        {
            var interp = new MonotoneCubic(new double[] { 0, 1, 2, 3 }, new double[] { 0, 10, 20, 30 });
            Check.Close(0, interp.Interpolate(0), 1e-9);
            Check.Close(10, interp.Interpolate(1), 1e-9);
            Check.Close(30, interp.Interpolate(3), 1e-9);
        }

        public void LinearDataInterpolatesLinearly()
        {
            var interp = new MonotoneCubic(new double[] { 0, 1, 2, 3 }, new double[] { 0, 10, 20, 30 });
            Check.Close(5, interp.Interpolate(0.5), 1e-9);
            Check.Close(25, interp.Interpolate(2.5), 1e-9);
        }

        public void DoesNotOvershoot()
        {
            // A sharp step: monotone cubic must stay within the neighboring values (no ringing).
            var interp = new MonotoneCubic(new double[] { 0, 1, 2, 3 }, new double[] { 0, 0, 0, 10 });
            for (double x = 0; x <= 2; x += 0.05)
                Check.True(Math.Abs(interp.Interpolate(x)) < 1e-9, $"overshoot at {x}: flat region should stay 0");
            for (double x = 2; x <= 3; x += 0.05)
            {
                double v = interp.Interpolate(x);
                Check.True(v >= -1e-9 && v <= 10 + 1e-9, $"overshoot at {x}: {v}");
            }
        }

        public void PreservesMonotonicity()
        {
            var xs = new double[] { 0, 1, 2, 3, 4 };
            var ys = new double[] { 0, 1, 1, 5, 6 }; // non-decreasing
            var interp = new MonotoneCubic(xs, ys);
            double prev = interp.Interpolate(0);
            for (double x = 0; x <= 4; x += 0.05)
            {
                double v = interp.Interpolate(x);
                Check.True(v >= prev - 1e-9, $"not monotone at {x}: {v} < {prev}");
                prev = v;
            }
        }

        public void ClampsOutsideRange()
        {
            var interp = new MonotoneCubic(new double[] { 0, 1 }, new double[] { 5, 10 });
            Check.Close(5, interp.Interpolate(-1), 1e-9);
            Check.Close(10, interp.Interpolate(2), 1e-9);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => new MonotoneCubic(new double[] { 0 }, new double[] { 0 }));
            Check.Throws<ArgumentException>(() => new MonotoneCubic(new double[] { 1, 1 }, new double[] { 0, 1 })); // not increasing
        }
    }
}
