using System;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class PhaseUnwrapTests
    {
        public void UnwrapsARamp()
        {
            // A phase increasing linearly past ±π should come out as a straight line.
            const int n = 100;
            var wrapped = new double[n];
            var expected = new double[n];
            for (int i = 0; i < n; i++)
            {
                double phi = 0.3 * i;                 // grows well past π
                expected[i] = phi;
                wrapped[i] = PhaseUnwrap.Wrap(phi);
            }
            var unwrapped = PhaseUnwrap.Unwrap(wrapped);
            // Unwrapped differs from expected by at most a constant multiple of 2π; anchor on the start.
            double offset = unwrapped[0] - expected[0];
            for (int i = 0; i < n; i++) Check.Close(expected[i], unwrapped[i] - offset, 1e-9, $"[{i}]");
        }

        public void NoJumpsLeavesInputUnchanged()
        {
            var x = new double[] { 0.1, 0.2, 0.15, -0.1, 0.05 };
            var y = PhaseUnwrap.Unwrap(x);
            for (int i = 0; i < x.Length; i++) Check.Close(x[i], y[i], 1e-12, $"[{i}]");
        }

        public void SuccessiveDifferencesWithinPi()
        {
            var rng = new Random(9);
            var wrapped = new double[200];
            for (int i = 0; i < wrapped.Length; i++) wrapped[i] = rng.NextDouble() * 2 * Math.PI - Math.PI;
            var y = PhaseUnwrap.Unwrap(wrapped);
            for (int i = 1; i < y.Length; i++)
                Check.True(Math.Abs(y[i] - y[i - 1]) <= Math.PI + 1e-9, $"jump at {i}");
        }

        public void Wrap_MapsToPrincipalRange()
        {
            Check.Close(0.0, PhaseUnwrap.Wrap(2 * Math.PI), 1e-12);
            Check.Close(Math.PI, PhaseUnwrap.Wrap(Math.PI), 1e-12);           // upper bound inclusive
            Check.Close(-Math.PI + 0.1, PhaseUnwrap.Wrap(Math.PI + 0.1), 1e-12);
            Check.Close(0.5, PhaseUnwrap.Wrap(0.5 + 4 * Math.PI), 1e-12);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => PhaseUnwrap.Unwrap(null!));
            Check.Throws<ArgumentOutOfRangeException>(() => PhaseUnwrap.Unwrap(new double[] { 1 }, 0));
            Check.Equal(0, PhaseUnwrap.Unwrap(Array.Empty<double>()).Length);
        }
    }
}
