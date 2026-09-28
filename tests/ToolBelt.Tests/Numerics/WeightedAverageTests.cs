using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class WeightedAverageTests
    {
        public void KnownValue()
        {
            // (1*1 + 2*2 + 3*3) / (1+2+3) = 14/6.
            Check.Close(14.0 / 6.0, WeightedAverage.Compute(new double[] { 1, 2, 3 }, new double[] { 1, 2, 3 }));
        }

        public void EqualWeights_IsArithmeticMean()
        {
            var values = new double[] { 4, 8, 15, 16, 23, 42 };
            var weights = Enumerable.Repeat(1.0, values.Length).ToArray();
            Check.Close(values.Average(), WeightedAverage.Compute(values, weights), 1e-9);
        }

        public void ZeroWeightIgnored()
        {
            Check.Close(10, WeightedAverage.Compute(new double[] { 10, 999 }, new double[] { 1, 0 }));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => WeightedAverage.Compute(new double[] { 1 }, new double[] { 1, 2 }));
            Check.Throws<ArgumentException>(() => WeightedAverage.Compute(Array.Empty<double>(), Array.Empty<double>()));
            Check.Throws<ArgumentException>(() => WeightedAverage.Compute(new double[] { 1, 2 }, new double[] { 0, 0 }));
            Check.Throws<ArgumentException>(() => WeightedAverage.Compute(new double[] { 1 }, new double[] { -1 }));
        }

        // Differential: independent loop reference over random values/weights.
        public void Differential_MatchesReference()
        {
            var rng = new Random(1234);
            for (int trial = 0; trial < 2000; trial++)
            {
                int n = rng.Next(1, 30);
                var values = new double[n];
                var weights = new double[n];
                double refNum = 0, refDen = 0;
                for (int i = 0; i < n; i++)
                {
                    values[i] = rng.NextDouble() * 200 - 100;
                    weights[i] = rng.NextDouble() * 10;
                    refNum += values[i] * weights[i];
                    refDen += weights[i];
                }
                if (refDen <= 0) continue;

                Check.Close(refNum / refDen, WeightedAverage.Compute(values, weights), 1e-6, $"trial {trial}");
            }
        }
    }
}
