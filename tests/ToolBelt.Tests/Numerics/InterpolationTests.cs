using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class InterpolationTests
    {
        public void Lerp_Endpoints_And_Mid()
        {
            Check.Close(0, Interpolation.Lerp(0, 10, 0));
            Check.Close(10, Interpolation.Lerp(0, 10, 1));
            Check.Close(5, Interpolation.Lerp(0, 10, 0.5));
            Check.Close(20, Interpolation.Lerp(0, 10, 2)); // unclamped
        }

        public void LerpClamped()
        {
            Check.Close(10, Interpolation.LerpClamped(0, 10, 2));
            Check.Close(0, Interpolation.LerpClamped(0, 10, -1));
        }

        public void InverseLerp()
        {
            Check.Close(0.5, Interpolation.InverseLerp(0, 10, 5));
            Check.Close(0, Interpolation.InverseLerp(5, 5, 5)); // degenerate range
        }

        public void Clamp()
        {
            Check.Close(5, Interpolation.Clamp(5, 0, 10));
            Check.Close(0, Interpolation.Clamp(-3, 0, 10));
            Check.Close(10, Interpolation.Clamp(99, 0, 10));
            Check.Throws<ArgumentException>(() => Interpolation.Clamp(1, 10, 0));
        }

        public void Remap()
        {
            Check.Close(50, Interpolation.Remap(5, 0, 10, 0, 100));
            Check.Close(-1, Interpolation.Remap(0, 0, 10, -1, 1));
            Check.Close(1, Interpolation.Remap(10, 0, 10, -1, 1));
        }

        public void SmoothStep()
        {
            Check.Close(0, Interpolation.SmoothStep(0, 1, -1));
            Check.Close(1, Interpolation.SmoothStep(0, 1, 2));
            Check.Close(0.5, Interpolation.SmoothStep(0, 1, 0.5));
            Check.Throws<ArgumentException>(() => Interpolation.SmoothStep(1, 1, 0.5));
        }

        // Property: Lerp and InverseLerp are inverses over random ranges and points.
        public void Property_LerpInverseRoundTrip()
        {
            var rng = new Random(1717);
            for (int trial = 0; trial < 5000; trial++)
            {
                double a = rng.NextDouble() * 200 - 100;
                double b = a + (rng.NextDouble() * 200 - 100);
                if (Math.Abs(b - a) < 1e-6) continue; // skip near-degenerate ranges

                double t = rng.NextDouble() * 2 - 0.5;
                double value = Interpolation.Lerp(a, b, t);
                Check.Close(t, Interpolation.InverseLerp(a, b, value), 1e-6, $"trial {trial}");
            }
        }
    }
}
