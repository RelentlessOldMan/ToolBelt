using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class TrendTests
    {
        public void StrictlyIncreasing()
        {
            var r = Trend.MannKendall(new double[] { 1, 2, 3, 4, 5 });
            Check.Equal(10, r.S);              // all 10 pairs concordant (5*4/2)
            Check.Close(1.0, r.Tau, 1e-9);
            Check.Close(1.0, r.SensSlope, 1e-9);
        }

        public void StrictlyDecreasing()
        {
            var r = Trend.MannKendall(new double[] { 5, 4, 3, 2, 1 });
            Check.Equal(-10, r.S);
            Check.Close(-1.0, r.Tau, 1e-9);
            Check.Close(-1.0, r.SensSlope, 1e-9);
        }

        public void Flat()
        {
            var r = Trend.MannKendall(new double[] { 7, 7, 7, 7 });
            Check.Equal(0, r.S);
            Check.Close(0.0, r.Tau, 1e-9);
            Check.Close(0.0, r.SensSlope, 1e-9);
        }

        public void SensSlopeRobustToOutlier()
        {
            // y = 2x with one wild outlier; Sen's slope stays near 2 (median is robust).
            var r = Trend.MannKendall(new double[] { 0, 2, 4, 6, 100, 10, 12 });
            Check.Close(2.0, r.SensSlope, 0.5);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => Trend.MannKendall(new double[] { 1 }));
            Check.Throws<ArgumentNullException>(() => Trend.MannKendall(null!));
        }
    }
}
