using System;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    public sealed class BandChartTests
    {
        public void FillsBetweenCurves()
        {
            var img = BandChart.Render(50, 100,
                new double[] { 0, 0, 0 }, new double[] { 2, 2, 2 }, center: null,
                new BandOptions { BandColor = Rgba.Blue, DrawFrame = false, Margin = 10, Min = 0, Max = 2 });
            // At the first column (left margin), the band fills from top to bottom of the plot area.
            int left = 10, bottom = 100 - 1 - 10, top = 10;
            Check.Equal(Rgba.Blue, img.GetPixel(left, (top + bottom) / 2));
            Check.Equal(Rgba.Blue, img.GetPixel(left, top));
            Check.Equal(Rgba.Blue, img.GetPixel(left, bottom));
        }

        public void CenterLineDrawnOnTop()
        {
            var img = BandChart.Render(50, 100,
                new double[] { 0, 0, 0 }, new double[] { 2, 2, 2 }, center: new double[] { 1, 1, 1 },
                new BandOptions { CenterColor = Rgba.Red, DrawFrame = false, Margin = 10, Min = 0, Max = 2 });
            bool foundCenter = false;
            for (int x = 0; x < img.Width && !foundCenter; x++)
                for (int y = 0; y < img.Height; y++)
                    if (img.GetPixel(x, y) == Rgba.Red) { foundCenter = true; break; }
            Check.True(foundCenter, "center line should be drawn");
        }

        public void NarrowBandIsThin()
        {
            var img = BandChart.Render(40, 100,
                new double[] { 1, 1 }, new double[] { 1.01, 1.01 }, center: null,
                new BandOptions { BandColor = Rgba.Green, DrawFrame = false, Margin = 5, Min = 0, Max = 10 });
            // Against a wide axis a near-zero-width band collapses to ~1px tall per column.
            int left = 5;
            int count = 0;
            for (int y = 0; y < img.Height; y++) if (img.GetPixel(left, y) == Rgba.Green) count++;
            Check.True(count >= 1 && count <= 3, $"thin band should be ~1px, got {count}");
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => BandChart.Render(10, 10, null!, new double[] { 1 }));
            Check.Throws<ArgumentException>(() => BandChart.Render(10, 10, new double[] { 1, 2 }, new double[] { 1 }));
            Check.Throws<ArgumentException>(() => BandChart.Render(10, 10, Array.Empty<double>(), Array.Empty<double>()));
            Check.Throws<ArgumentException>(() => BandChart.Render(10, 10, new double[] { 1 }, new double[] { 2 }, center: new double[] { 1, 2 }));
        }
    }
}
