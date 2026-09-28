using System;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    public sealed class ColormapTests
    {
        public void EndpointsAndMidpoint()
        {
            Check.Equal(Rgba.Black, Colormap.Grayscale.Map(0));
            Check.Equal(Rgba.White, Colormap.Grayscale.Map(1));
            var mid = Colormap.Grayscale.Map(0.5);
            Check.True(mid.R > 120 && mid.R < 135, mid.ToString()); // ~127 gray
        }

        public void ClampsOutOfRange()
        {
            Check.Equal(Rgba.Black, Colormap.Grayscale.Map(-2));
            Check.Equal(Rgba.White, Colormap.Grayscale.Map(5));
            Check.Equal(Rgba.Black, Colormap.Grayscale.Map(double.NaN)); // NaN -> low end
        }

        public void CustomStopsInterpolate()
        {
            var cm = new Colormap((0.0, new Rgba(0, 0, 0)), (1.0, new Rgba(100, 200, 40)));
            var half = cm.Map(0.5);
            Check.Equal(50, half.R);
            Check.Equal(100, half.G);
            Check.Equal(20, half.B);
        }

        public void ViridisEndpoints()
        {
            Check.Equal(new Rgba(68, 1, 84), Colormap.Viridis.Map(0));
            Check.Equal(new Rgba(253, 231, 37), Colormap.Viridis.Map(1));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => new Colormap((0.0, Rgba.Black)));
            Check.Throws<ArgumentNullException>(() => new Colormap(null!));
        }
    }
}
