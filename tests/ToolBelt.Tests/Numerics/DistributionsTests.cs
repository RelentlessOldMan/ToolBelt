using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class DistributionsTests
    {
        public void NormalPdf()
        {
            Check.Close(1.0 / Math.Sqrt(2 * Math.PI), Distributions.NormalPdf(0), 1e-9); // peak at 0
            Check.Close(Distributions.NormalPdf(-1), Distributions.NormalPdf(1), 1e-12);  // symmetry
        }

        public void NormalCdf()
        {
            Check.Close(0.5, Distributions.NormalCdf(0), 1e-9);
            Check.Close(0.975, Distributions.NormalCdf(1.959963985), 1e-4);
            Check.Close(0.8413447, Distributions.NormalCdf(1), 1e-6); // one sigma
            // Symmetry Cdf(-x) = 1 - Cdf(x)
            Check.Close(Distributions.NormalCdf(-1.3), 1 - Distributions.NormalCdf(1.3), 1e-6);
        }

        public void NormalQuantile()
        {
            Check.Close(0, Distributions.NormalQuantile(0.5), 1e-9);
            Check.Close(1.959963985, Distributions.NormalQuantile(0.975), 1e-6);
            Check.Close(-1.959963985, Distributions.NormalQuantile(0.025), 1e-6);
        }

        public void NormalQuantileRoundTrip()
        {
            for (double p = 0.01; p < 1.0; p += 0.01)
                Check.Close(p, Distributions.NormalCdf(Distributions.NormalQuantile(p)), 1e-6, $"p={p}");
        }

        public void ScaledNormal()
        {
            Check.Close(0.5, Distributions.NormalCdf(10, 10, 2), 1e-9);            // at the mean
            Check.Close(10, Distributions.NormalQuantile(0.5, 10, 2), 1e-9);
        }

        public void Exponential()
        {
            Check.Close(2.0, Distributions.ExponentialPdf(0, 2), 1e-9);            // rate at x=0
            Check.Close(1 - Math.Exp(-2), Distributions.ExponentialCdf(1, 2), 1e-9);
            Check.Equal(0.0, Distributions.ExponentialCdf(-1, 2));
            Check.Close(Math.Log(2) / 2, Distributions.ExponentialQuantile(0.5, 2), 1e-9); // median
        }

        public void Uniform()
        {
            Check.Close(0.25, Distributions.UniformPdf(1, 0, 4), 1e-9);
            Check.Equal(0.0, Distributions.UniformPdf(5, 0, 4));
            Check.Close(0.5, Distributions.UniformCdf(2, 0, 4), 1e-9);
            Check.Equal(0.0, Distributions.UniformCdf(-1, 0, 4));
            Check.Equal(1.0, Distributions.UniformCdf(9, 0, 4));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.NormalPdf(0, 0, -1));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.NormalQuantile(0));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.NormalQuantile(1));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.ExponentialPdf(0, 0));
            Check.Throws<ArgumentException>(() => Distributions.UniformPdf(0, 5, 5));
        }
    }
}
