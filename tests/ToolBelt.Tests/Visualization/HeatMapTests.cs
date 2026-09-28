using System;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    public sealed class HeatMapTests
    {
        public void MapsMinAndMaxToColormapEnds()
        {
            // 2x2 grid; min -> black, max -> white with grayscale.
            var data = new double[,] { { 0, 10 }, { 5, 0 } };
            var img = HeatMap.Render(data, Colormap.Grayscale);
            Check.Equal(2, img.Width);
            Check.Equal(2, img.Height);
            Check.Equal(Rgba.Black, img.GetPixel(0, 0));  // value 0 (min)
            Check.Equal(Rgba.White, img.GetPixel(1, 0));  // value 10 (max)
            var mid = img.GetPixel(0, 1);                 // value 5 -> ~half
            Check.True(mid.R > 120 && mid.R < 135, mid.ToString());
        }

        public void CellSizeScalesOutput()
        {
            var data = new double[,] { { 0, 1 } };
            var img = HeatMap.Render(data, Colormap.Grayscale, new HeatMapOptions { CellSize = 4 });
            Check.Equal(8, img.Width);  // 2 cols * 4
            Check.Equal(4, img.Height); // 1 row * 4
            Check.Equal(Rgba.Black, img.GetPixel(3, 3)); // still inside the first (min) cell
            Check.Equal(Rgba.White, img.GetPixel(4, 0)); // second cell (max)
        }

        public void ExplicitRangeAndNanColor()
        {
            var data = new double[,] { { double.NaN, 100 } };
            var img = HeatMap.Render(data, Colormap.Grayscale,
                new HeatMapOptions { Min = 0, Max = 100, NanColor = Rgba.Red });
            Check.Equal(Rgba.Red, img.GetPixel(0, 0));   // NaN cell
            Check.Equal(Rgba.White, img.GetPixel(1, 0)); // 100 == max
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => HeatMap.Render(null!, Colormap.Grayscale));
            Check.Throws<ArgumentOutOfRangeException>(() => HeatMap.Render(new double[,] { { 1 } }, Colormap.Grayscale, new HeatMapOptions { CellSize = 0 }));
        }
    }
}
