using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class MeansTests
    {
        public void KnownValues()
        {
            var data = new double[] { 1, 2, 4 };
            Check.Close(7.0 / 3.0, Means.Arithmetic(data), 1e-9);
            Check.Close(2.0, Means.Geometric(data), 1e-9);              // cbrt(8)
            Check.Close(3.0 / 1.75, Means.Harmonic(data), 1e-9);        // 3 / (1 + 0.5 + 0.25)
            Check.Close(Math.Sqrt(21.0 / 3.0), Means.RootMeanSquare(data), 1e-9);
        }

        public void NonPositive_ThrowsForGeometricAndHarmonic()
        {
            Check.Throws<ArgumentException>(() => Means.Geometric(new double[] { 1, 0, 2 }));
            Check.Throws<ArgumentException>(() => Means.Harmonic(new double[] { 1, -2 }));
            // Arithmetic/RMS accept any values.
            Check.Close(0, Means.Arithmetic(new double[] { -1, 1 }), 1e-9);
        }

        public void Empty_Throws()
        {
            Check.Throws<ArgumentException>(() => Means.Arithmetic(Array.Empty<double>()));
            Check.Throws<ArgumentNullException>(() => Means.Geometric(null!));
        }

        public void SingleValue_AllMeansEqualIt()
        {
            var one = new double[] { 5 };
            Check.Close(5, Means.Arithmetic(one), 1e-9);
            Check.Close(5, Means.Geometric(one), 1e-9);
            Check.Close(5, Means.Harmonic(one), 1e-9);
            Check.Close(5, Means.RootMeanSquare(one), 1e-9);
        }

        // Property: for positive data, HM <= GM <= AM <= RMS.
        public void Property_MeanInequalityChain()
        {
            var rng = new Random(2024);
            for (int trial = 0; trial < 2000; trial++)
            {
                int n = rng.Next(1, 30);
                var data = new double[n];
                for (int i = 0; i < n; i++) data[i] = rng.NextDouble() * 100 + 0.01; // strictly positive

                double hm = Means.Harmonic(data);
                double gm = Means.Geometric(data);
                double am = Means.Arithmetic(data);
                double rms = Means.RootMeanSquare(data);

                Check.True(hm <= gm + 1e-6, $"trial {trial}: HM {hm} > GM {gm}");
                Check.True(gm <= am + 1e-6, $"trial {trial}: GM {gm} > AM {am}");
                Check.True(am <= rms + 1e-6, $"trial {trial}: AM {am} > RMS {rms}");
            }
        }
    }
}
