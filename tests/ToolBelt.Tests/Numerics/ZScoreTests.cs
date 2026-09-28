using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class ZScoreTests
    {
        public void SingleValue()
        {
            Check.Close(2, ZScore.Standardize(20, 10, 5)); // (20-10)/5
            Check.Close(0, ZScore.Standardize(20, 20, 0)); // zero std -> 0
        }

        public void KnownSet()
        {
            var z = ZScore.Standardize(new double[] { 2, 4, 6 }); // mean 4, pop std sqrt(8/3)
            double std = Math.Sqrt(8.0 / 3.0);
            Check.Close(-2 / std, z[0], 1e-9);
            Check.Close(0, z[1], 1e-9);
            Check.Close(2 / std, z[2], 1e-9);
        }

        public void ConstantData_AllZero()
        {
            var z = ZScore.Standardize(new double[] { 5, 5, 5 });
            Check.True(z.All(v => v == 0));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => ZScore.Standardize(Array.Empty<double>()));
            Check.Throws<ArgumentException>(() => ZScore.Standardize(new double[] { 1 }, sample: true));
        }

        // Property: population-standardized data has mean ~0 and standard deviation ~1.
        public void Property_StandardizedHasZeroMeanUnitStd()
        {
            var rng = new Random(9);
            for (int trial = 0; trial < 500; trial++)
            {
                int n = rng.Next(2, 100);
                var data = new double[n];
                for (int i = 0; i < n; i++) data[i] = rng.NextDouble() * 100 - 50;

                // Skip near-constant data where std ~ 0.
                double mean0 = data.Average();
                double var0 = data.Sum(x => (x - mean0) * (x - mean0)) / n;
                if (var0 < 1e-6) continue;

                var z = ZScore.Standardize(data);
                double zMean = z.Average();
                double zVar = z.Sum(v => (v - zMean) * (v - zMean)) / n;

                Check.Close(0, zMean, 1e-9, $"trial {trial}: mean");
                Check.Close(1, zVar, 1e-6, $"trial {trial}: variance");
            }
        }
    }
}
