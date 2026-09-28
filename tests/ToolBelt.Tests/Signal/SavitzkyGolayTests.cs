using System;
using System.Linq;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class SavitzkyGolayTests
    {
        // Key SG property: a polynomial of degree <= order passes through unchanged (every point).
        public void PreservesPolynomialsUpToOrder()
        {
            Func<double, double> f = k => 3 - 2 * k + 0.5 * k * k; // quadratic
            var data = Enumerable.Range(0, 20).Select(k => f(k)).ToArray();
            var smoothed = SavitzkyGolay.Smooth(data, windowSize: 7, polynomialOrder: 2);
            for (int i = 0; i < data.Length; i++)
                Check.Close(data[i], smoothed[i], 1e-6, $"at {i}");
        }

        public void ReducesNoiseVariance()
        {
            var rng = new Random(47);
            var clean = Enumerable.Range(0, 200).Select(k => 10.0).ToArray(); // flat signal
            var noisy = clean.Select(v => v + (rng.NextDouble() - 0.5) * 4).ToArray();
            var smoothed = SavitzkyGolay.Smooth(noisy, 11, 2);

            double VarAround10(double[] a) => a.Select(v => (v - 10) * (v - 10)).Average();
            Check.True(VarAround10(smoothed) < VarAround10(noisy), "smoothing should reduce variance");
        }

        public void PreservesConstant()
        {
            var data = Enumerable.Repeat(5.0, 15).ToArray();
            var smoothed = SavitzkyGolay.Smooth(data, 5, 2);
            Check.True(smoothed.All(v => Math.Abs(v - 5) < 1e-9));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => SavitzkyGolay.Smooth(new double[10], 4, 2));   // even window
            Check.Throws<ArgumentOutOfRangeException>(() => SavitzkyGolay.Smooth(new double[10], 5, 5)); // order >= window
            Check.Throws<ArgumentException>(() => SavitzkyGolay.Smooth(new double[3], 5, 2));    // data shorter than window
            Check.Throws<ArgumentNullException>(() => SavitzkyGolay.Smooth(null!, 5, 2));
        }
    }
}
