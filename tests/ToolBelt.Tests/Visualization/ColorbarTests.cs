using System;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    public sealed class ColorbarTests
    {
        public void Vertical_TopIsHighBottomIsLow()
        {
            var img = Colorbar.Render(10, 100, Colormap.Grayscale, vertical: true);
            Check.Equal(Rgba.White, img.GetPixel(5, 0));          // top = value 1 = white
            Check.Equal(Rgba.Black, img.GetPixel(5, 99));         // bottom = value 0 = black
            var mid = img.GetPixel(5, 50);
            Check.True(mid.R > 100 && mid.R < 160, "midpoint is a mid-gray: " + mid);
        }

        public void Horizontal_LeftIsLowRightIsHigh()
        {
            var img = Colorbar.Render(100, 10, Colormap.Grayscale, vertical: false);
            Check.Equal(Rgba.Black, img.GetPixel(0, 5));          // left = value 0
            Check.Equal(Rgba.White, img.GetPixel(99, 5));         // right = value 1
        }

        public void EachRowIsConstant_Vertical()
        {
            var img = Colorbar.Render(8, 20, Colormap.Viridis, vertical: true);
            for (int y = 0; y < img.Height; y++)
            {
                var first = img.GetPixel(0, y);
                for (int x = 1; x < img.Width; x++) Check.Equal(first, img.GetPixel(x, y), $"row {y}");
            }
        }

        public void Border_Drawn()
        {
            var img = Colorbar.Render(20, 20, Colormap.Grayscale, vertical: true, border: Rgba.Red);
            Check.Equal(Rgba.Red, img.GetPixel(0, 0));
            Check.Equal(Rgba.Red, img.GetPixel(19, 19));
        }

        public void SingleRowOrColumn_DoesNotThrow()
        {
            Check.Equal(1, Colorbar.Render(5, 1, Colormap.Hot, vertical: true).Height);
            Check.Equal(1, Colorbar.Render(1, 5, Colormap.Hot, vertical: false).Width);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Colorbar.Render(10, 10, null!));
        }
    }
}
