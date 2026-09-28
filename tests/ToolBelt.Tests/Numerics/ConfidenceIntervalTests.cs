using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class ConfidenceIntervalTests
    {
        public void MeanKnownCase()
        {
            // mean 5, sample stddev sqrt(32/7)=2.138, n=8. 95% half-width = 1.95996 * s/sqrt(8) = 1.4816.
            var sample = new double[] { 2, 4, 4, 4, 5, 5, 7, 9 };
            var (lo, hi) = ConfidenceInterval.ForMean(sample, 0.95);
            Check.Close(5, (lo + hi) / 2, 1e-9);              // centered on the mean
            Check.Close(1.4816, (hi - lo) / 2, 1e-3);         // half-width matches z·SE
        }

        public void HigherConfidenceIsWider()
        {
            var sample = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var narrow = ConfidenceInterval.ForMean(sample, 0.90);
            var wide = ConfidenceInterval.ForMean(sample, 0.99);
            Check.True((wide.Upper - wide.Lower) > (narrow.Upper - narrow.Lower));
        }

        public void ProportionWilson()
        {
            // 50/100 at 95% -> Wilson interval roughly (0.404, 0.596), centered near 0.5.
            var (lo, hi) = ConfidenceInterval.ForProportion(50, 100, 0.95);
            Check.Close(0.5, (lo + hi) / 2, 0.01);
            Check.Close(0.404, lo, 0.01);
            Check.Close(0.596, hi, 0.01);
        }

        public void ProportionExtremesStayInRange()
        {
            var (lo0, hi0) = ConfidenceInterval.ForProportion(0, 20, 0.95);
            Check.True(lo0 >= 0 && hi0 > 0 && hi0 < 1, "0/20 interval must stay within [0,1)");
            var (lo1, hi1) = ConfidenceInterval.ForProportion(20, 20, 0.95);
            Check.True(hi1 <= 1 + 1e-9 && lo1 > 0 && lo1 < 1, "20/20 interval must stay within (0,1]");
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => ConfidenceInterval.ForMean(new double[] { 1 }));
            Check.Throws<ArgumentOutOfRangeException>(() => ConfidenceInterval.ForMean(new double[] { 1, 2 }, 1.5));
            Check.Throws<ArgumentOutOfRangeException>(() => ConfidenceInterval.ForProportion(5, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => ConfidenceInterval.ForProportion(11, 10));
        }
    }
}
