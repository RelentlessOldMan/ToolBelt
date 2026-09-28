using System;
using System.Linq;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Quality
{
    public sealed class ControlChartTests
    {
        public void IndividualsLimitsCenterOnMean()
        {
            var data = new double[] { 10, 11, 9, 10, 12, 8, 10 };
            var limits = ControlChart.Individuals(data);
            Check.Close(data.Average(), limits.CenterLine, 1e-9);
            Check.True(limits.UpperControlLimit > limits.CenterLine);
            Check.True(limits.LowerControlLimit < limits.CenterLine);
        }

        public void PointsBeyondLimits()
        {
            var data = new double[] { 10, 10, 10, 10, 10, 10, 50 }; // last point is a spike
            var limits = ControlChart.Individuals(data);
            var hits = ControlChart.PointsBeyondLimits(data, limits);
            Check.True(hits.Contains(6));
        }

        public void RunsOnOneSide()
        {
            // 9 consecutive points above the center line (0).
            var data = new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1 };
            var hits = ControlChart.RunsOnOneSide(data, centerLine: 0, runLength: 9);
            Check.True(hits.Contains(8)); // the 9th point completes the run
            Check.Equal(0, ControlChart.RunsOnOneSide(new double[] { 1, 1, 1 }, 0, 9).Count);
        }

        public void Trends()
        {
            var data = new double[] { 1, 2, 3, 4, 5, 6 }; // 6 steadily increasing
            var hits = ControlChart.Trends(data, length: 6);
            Check.True(hits.Contains(5));
            var noTrend = ControlChart.Trends(new double[] { 1, 2, 1, 2, 1, 2 }, 6);
            Check.Equal(0, noTrend.Count);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => ControlChart.Individuals(new double[] { 1 }));
            Check.Throws<ArgumentNullException>(() => ControlChart.Individuals(null!));
        }
    }
}
