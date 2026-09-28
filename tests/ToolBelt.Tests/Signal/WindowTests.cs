using System;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class WindowTests
    {
        public void Rectangular_IsAllOnes()
        {
            var w = Window.Create(WindowType.Rectangular, 5);
            foreach (var v in w) Check.Close(1.0, v);
        }

        public void Hann_KnownEndpointsAndCenter()
        {
            var w = Window.Create(WindowType.Hann, 5);
            // 0.5 - 0.5 cos(2πn/4): endpoints 0, center (n=2) is cos(π)=-1 -> 1.0.
            Check.Close(0.0, w[0], 1e-12);
            Check.Close(0.0, w[4], 1e-12);
            Check.Close(1.0, w[2], 1e-12);
            Check.Close(0.5, w[1], 1e-12);
            Check.Close(0.5, w[3], 1e-12);
        }

        public void Hamming_KnownEndpoints()
        {
            var w = Window.Create(WindowType.Hamming, 5);
            Check.Close(0.08, w[0], 1e-12);  // 0.54 - 0.46
            Check.Close(0.08, w[4], 1e-12);
            Check.Close(1.0, w[2], 1e-12);   // 0.54 + 0.46
        }

        public void Symmetric_AllTypes()
        {
            foreach (WindowType type in Enum.GetValues(typeof(WindowType)))
            {
                var w = Window.Create(type, 33);
                for (int i = 0; i < w.Length / 2; i++)
                    Check.Close(w[i], w[w.Length - 1 - i], 1e-12, $"{type}[{i}]");
            }
        }

        public void SingleSample_IsUnity()
        {
            foreach (WindowType type in Enum.GetValues(typeof(WindowType)))
                Check.Close(1.0, Window.Create(type, 1)[0], 1e-12, type.ToString());
        }

        public void Apply_ScalesSamples()
        {
            var x = new double[] { 2, 2, 2, 2, 2 };
            var y = Window.Apply(x, WindowType.Hann);
            var w = Window.Create(WindowType.Hann, 5);
            for (int i = 0; i < 5; i++) Check.Close(2 * w[i], y[i], 1e-12, $"[{i}]");
        }

        public void CoherentGain_RectangularIsOne()
            => Check.Close(1.0, Window.CoherentGain(Window.Create(WindowType.Rectangular, 64)), 1e-12);

        public void CoherentGain_HannNearHalf()
        {
            // Mean of a Hann window tends to 0.5 for large N.
            double g = Window.CoherentGain(Window.Create(WindowType.Hann, 1024));
            Check.Close(0.5, g, 1e-3);
        }

        public void EquivalentNoiseBandwidth_KnownValues()
        {
            Check.Close(1.0, Window.EquivalentNoiseBandwidth(Window.Create(WindowType.Rectangular, 512)), 1e-9);
            Check.Close(1.5, Window.EquivalentNoiseBandwidth(Window.Create(WindowType.Hann, 4096)), 2e-3);   // ~1.50
            Check.Close(1.3628, Window.EquivalentNoiseBandwidth(Window.Create(WindowType.Hamming, 4096)), 5e-3);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Window.Create(WindowType.Hann, 0));
            Check.Throws<ArgumentNullException>(() => Window.Apply(null!, WindowType.Hann));
            Check.Throws<ArgumentException>(() => Window.Apply(new double[] { 1, 2 }, new double[] { 1 }));
            Check.Throws<ArgumentException>(() => Window.CoherentGain(Array.Empty<double>()));
        }
    }
}
