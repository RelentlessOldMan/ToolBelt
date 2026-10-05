using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    public sealed class PlotFrameTests
    {
        private static readonly PlotFrame Frame = new PlotFrame(10, 20, 100, 50, xMin: 0, xMax: 10, yMin: 0, yMax: 5);

        public void Linear_MapsCornersAndMidpoints()
        {
            Check.Close(10, Frame.MapX(0));
            Check.Close(110, Frame.MapX(10));
            Check.Close(60, Frame.MapX(5));
            Check.Close(70, Frame.MapY(0));      // bottom
            Check.Close(20, Frame.MapY(5));      // top
            Check.Close(45, Frame.MapY(2.5));
            Check.Close(110, Frame.Right);
            Check.Close(70, Frame.Bottom);
        }

        public void OutOfRangeMapsOutsideArea()
        {
            Check.Close(-10, Frame.MapX(-2));
            Check.True(Frame.MapY(6) < Frame.Top);
            Check.False(Frame.ContainsX(-0.001));
            Check.True(Frame.ContainsY(5));
        }

        public void UnmapRoundTrips_Randomized()
        {
            var rng = new DeterministicRandom(42);
            var log = new PlotFrame(0, 0, 640, 480, 0.01, 1e4, 1, 1e6, logX: true, logY: true);
            for (int i = 0; i < 500; i++)
            {
                double x = rng.NextDouble() * 20 - 5, y = rng.NextDouble() * 9 - 2;
                Check.Close(x, Frame.UnmapX(Frame.MapX(x)), 1e-9);
                Check.Close(y, Frame.UnmapY(Frame.MapY(y)), 1e-9);

                double lx = Math.Pow(10, rng.NextDouble() * 6 - 2), ly = Math.Pow(10, rng.NextDouble() * 6);
                Check.Close(lx, log.UnmapX(log.MapX(lx)), lx * 1e-9);
                Check.Close(ly, log.UnmapY(log.MapY(ly)), ly * 1e-9);
            }
        }

        public void LogAxis_Decades()
        {
            var f = new PlotFrame(0, 0, 300, 100, 1, 1000, 0, 1, logX: true);
            Check.Close(0, f.MapX(1));
            Check.Close(100, f.MapX(10));
            Check.Close(200, f.MapX(100));
            Check.Close(Math.Pow(10, 1.5), f.UnmapX(150), 1e-9);
            Check.True(double.IsNaN(f.MapX(0)), "non-positive has no log position");
            Check.True(double.IsNaN(f.MapX(-3)));
        }

        public void DegenerateRange_MapsToCentre()
        {
            var f = new PlotFrame(0, 0, 100, 40, 3, 3, 7, 7);
            Check.Close(50, f.MapX(3));
            Check.Close(20, f.MapY(7));
        }

        public void WithAreaAndWithRanges()
        {
            PlotFrame moved = Frame.WithArea(200, 0, 50, 25);
            Check.Close(225, moved.MapX(5));
            Check.Close(25, moved.MapY(0));
            PlotFrame rescaled = Frame.WithRanges(0, 20, 0, 10);
            Check.Close(60, rescaled.MapX(10));
        }

        public void Validation()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new PlotFrame(0, 0, 0, 10, 0, 1, 0, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => new PlotFrame(0, 0, 10, -1, 0, 1, 0, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => new PlotFrame(0, 0, 10, 10, double.NaN, 1, 0, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => new PlotFrame(0, 0, 10, 10, 0, 10, 0, 1, logX: true));
        }
    }
}
