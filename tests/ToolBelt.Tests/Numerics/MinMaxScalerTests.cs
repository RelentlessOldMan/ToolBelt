using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class MinMaxScalerTests
    {
        public void ToUnit()
        {
            var scaled = MinMaxScaler.ToUnit(new double[] { 0, 5, 10 });
            Check.True(scaled.SequenceEqual(new double[] { 0, 0.5, 1 }));
        }

        public void ToRange()
        {
            var scaled = MinMaxScaler.ToRange(new double[] { 0, 5, 10 }, 0, 100);
            Check.True(scaled.SequenceEqual(new double[] { 0, 50, 100 }));
        }

        public void ConstantData_MapsToLow()
        {
            var scaled = MinMaxScaler.ToRange(new double[] { 3, 3, 3 }, 10, 20);
            Check.True(scaled.All(x => x == 10));
        }

        public void NegativeAndReversedRange()
        {
            var scaled = MinMaxScaler.ToRange(new double[] { -10, 0, 10 }, 1, 0); // reversed target range is allowed
            Check.Close(1, scaled[0], 1e-9);
            Check.Close(0.5, scaled[1], 1e-9);
            Check.Close(0, scaled[2], 1e-9);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => MinMaxScaler.ToUnit(Array.Empty<double>()));
            Check.Throws<ArgumentNullException>(() => MinMaxScaler.ToUnit(null!));
        }

        // Property: min maps to newMin, max maps to newMax, output stays within the (possibly reversed) range.
        public void Property_Endpoints()
        {
            var rng = new Random(1234);
            for (int trial = 0; trial < 1000; trial++)
            {
                int n = rng.Next(2, 40);
                var data = new double[n];
                for (int i = 0; i < n; i++) data[i] = rng.NextDouble() * 200 - 100;
                if (data.Distinct().Count() < 2) continue;

                var scaled = MinMaxScaler.ToUnit(data);
                double min = data.Min(), max = data.Max();
                for (int i = 0; i < n; i++)
                {
                    Check.True(scaled[i] >= -1e-9 && scaled[i] <= 1 + 1e-9, $"trial {trial}: out of [0,1]");
                    if (data[i] == min) Check.Close(0, scaled[i], 1e-9, $"trial {trial}: min->0");
                    if (data[i] == max) Check.Close(1, scaled[i], 1e-9, $"trial {trial}: max->1");
                }
            }
        }
    }
}
