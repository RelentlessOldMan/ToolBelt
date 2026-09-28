using System;
using System.Collections.Generic;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    public sealed class LinePlotTests
    {
        public void DrawsLineBetweenMappedEndpoints()
        {
            // Two points; margin 10 on 100x100 -> plot area [10,89]. min maps to (10,89), max to (89,10).
            var series = new List<Series> { new Series(new double[] { 0, 10 }, Rgba.Red, PlotStyle.Line, new double[] { 0, 1 }) };
            var img = LinePlot.Render(100, 100, series, new PlotOptions { DrawFrame = false });

            Check.Equal(Rgba.Red, img.GetPixel(10, 89)); // first point (x=0,y=0) -> bottom-left of plot area
            Check.Equal(Rgba.Red, img.GetPixel(89, 10)); // second point (x=1,y=10) -> top-right
            Check.Equal(Rgba.White, img.GetPixel(0, 0));  // background outside the line
        }

        public void DrawsFrame()
        {
            var series = new List<Series> { new Series(new double[] { 1, 2, 3 }, Rgba.Blue) };
            var img = LinePlot.Render(60, 40, series, new PlotOptions { Margin = 5, FrameColor = Rgba.Black });
            Check.Equal(Rgba.Black, img.GetPixel(5, 5));   // frame corner
            Check.Equal(Rgba.Black, img.GetPixel(54, 34)); // opposite frame corner
        }

        public void ScatterDrawsPoints()
        {
            var series = new List<Series> { new Series(new double[] { 5 }, Rgba.Green, PlotStyle.Scatter) };
            var img = LinePlot.Render(50, 50, series, new PlotOptions { DrawFrame = false });
            // Single point with constant data maps to the plot-area center; a 3x3 marker sits there.
            Check.Equal(Rgba.Green, img.GetPixel(25, 25));
        }

        public void BackgroundFillsImage()
        {
            var series = new List<Series> { new Series(new double[] { 0, 1 }, Rgba.Red) };
            var img = LinePlot.Render(30, 30, series, new PlotOptions { Background = Rgba.Blue, DrawFrame = false });
            Check.Equal(Rgba.Blue, img.GetPixel(0, 0)); // corner is background
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => LinePlot.Render(10, 10, null!));
            Check.Throws<ArgumentException>(() =>
                LinePlot.Render(10, 10, new List<Series> { new Series(new double[] { 1 }, Rgba.Red) }, new PlotOptions { Margin = 20 }));
        }
    }
}
