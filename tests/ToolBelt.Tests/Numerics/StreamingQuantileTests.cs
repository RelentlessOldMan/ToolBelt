using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class StreamingQuantileTests
    {
        private static double Track(double p, Func<Random, double> draw, int n, int seed)
        {
            var rng = new DeterministicRandom(seed);
            var q = new StreamingQuantile(p);
            for (int i = 0; i < n; i++) q.Add(draw(rng));
            return q.Estimate;
        }

        public void FewValues_AreExact()
        {
            var q = new StreamingQuantile(0.5);
            Check.True(double.IsNaN(q.Estimate));
            q.AddRange(new double[] { 9, 1, 5 });
            Check.Equal(5.0, q.Estimate);
            Check.Equal(1.0, q.Min);
            Check.Equal(9.0, q.Max);
            q.Add(7);
            Check.Close(6, q.Estimate);                                     // linear interpolation of {1,5,7,9}
        }

        public void Uniform_MedianAndTails()
        {
            Check.Close(0.5, Track(0.5, r => r.NextDouble(), 100000, 1), 0.005);
            Check.Close(0.9, Track(0.9, r => r.NextDouble(), 100000, 2), 0.005);
            Check.Close(0.99, Track(0.99, r => r.NextDouble(), 100000, 3), 0.003);
        }

        public void Normal_AndExponential()
        {
            Check.Close(0, Track(0.5, r => r.NextGaussian(), 100000, 4), 0.02);
            Check.Close(1.644854, Track(0.95, r => r.NextGaussian(), 100000, 5), 0.03);
            Check.Close(Math.Log(100), Track(0.99, r => r.NextExponential(), 100000, 6), 0.1);  // p99 of Exp(1) = ln 100
        }

        public void SortedStream_StillTracksTheMedian()
        {
            var q = new StreamingQuantile(0.5);
            for (int i = 1; i <= 10001; i++) q.Add(i);
            Check.Close(5001, q.Estimate, 50);
        }

        public void SeveralQuantiles()
        {
            var qs = new StreamingQuantiles(0.1, 0.5, 0.9);
            var rng = new DeterministicRandom(9);
            for (int i = 0; i < 50000; i++) qs.Add(rng.NextDouble() * 100);
            Check.Equal(50000L, qs.Count);
            Check.Close(10, qs[0], 1);
            Check.Close(50, qs[1], 1);
            Check.Close(90, qs.Estimates()[0.9], 1);
        }

        public void Validation()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new StreamingQuantile(0));
            Check.Throws<ArgumentOutOfRangeException>(() => new StreamingQuantile(1));
            Check.Throws<ArgumentException>(() => new StreamingQuantile(0.5).Add(double.NaN));
            Check.Throws<ArgumentException>(() => new StreamingQuantiles());
        }
    }
}
