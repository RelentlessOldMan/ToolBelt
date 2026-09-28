using System;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    public sealed class ErrorBarChartTests
    {
        public void SinglePoint_MarkerAtCenter()
        {
            // Constant x and y collapse both ranges, so the point maps to the plot-area center.
            var img = ErrorBarChart.Render(51, 51,
                new double[] { 0 }, new double[] { 0 }, new double[] { 0 },
                new ErrorBarOptions { Color = Rgba.Red, DrawFrame = false, Margin = 5 });
            // center = (left+right)/2 = (5 + 45)/2 = 25.
            Check.Equal(Rgba.Red, img.GetPixel(25, 25));
        }

        public void ErrorBarSpansValuePlusMinusError()
        {
            var img = ErrorBarChart.Render(60, 100,
                new double[] { 0 }, new double[] { 5 }, new double[] { 5 },
                new ErrorBarOptions { Color = Rgba.Blue, DrawFrame = false, Margin = 10, CapHalfWidth = 4 });
            // y range = [0,10]; the whisker should reach both near the top and near the bottom of the plot.
            bool nearTop = false, nearBottom = false;
            int cx = (10 + (60 - 1 - 10)) / 2;
            for (int y = 0; y < img.Height; y++)
                if (img.GetPixel(cx, y) == Rgba.Blue)
                {
                    if (y < 20) nearTop = true;
                    if (y > 80) nearBottom = true;
                }
            Check.True(nearTop && nearBottom, "whisker should span the full error range");
        }

        public void CapsAreWiderThanTheStem()
        {
            var img = ErrorBarChart.Render(80, 80,
                new double[] { 1 }, new double[] { 1 }, new double[] { 1 },
                new ErrorBarOptions { Color = Rgba.Green, DrawFrame = false, CapHalfWidth = 5 });
            // Count blue-green pixels per row; the widest rows are the caps.
            int maxRowWidth = 0;
            for (int y = 0; y < img.Height; y++)
            {
                int w = 0;
                for (int x = 0; x < img.Width; x++) if (img.GetPixel(x, y) == Rgba.Green) w++;
                if (w > maxRowWidth) maxRowWidth = w;
            }
            Check.True(maxRowWidth >= 9, $"cap row should be ~11px wide, got {maxRowWidth}");
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => ErrorBarChart.Render(10, 10, null!, new double[] { 1 }, new double[] { 1 }));
            Check.Throws<ArgumentException>(() => ErrorBarChart.Render(10, 10, new double[] { 1, 2 }, new double[] { 1 }, new double[] { 1 }));
        }
    }
}
