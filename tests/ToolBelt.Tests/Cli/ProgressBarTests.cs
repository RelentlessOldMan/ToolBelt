using System;
using System.Linq;
using ToolBelt.Cli;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Cli
{
    public sealed class ProgressBarTests
    {
        public void RendersWithAsciiChars()
        {
            Check.Equal("#####-----", ProgressBar.Render(0.5, 10, '#', '-'));
            Check.Equal("##########", ProgressBar.Render(1.0, 10, '#', '-'));
            Check.Equal("----------", ProgressBar.Render(0.0, 10, '#', '-'));
        }

        public void ClampsOutOfRange()
        {
            Check.Equal("##########", ProgressBar.Render(1.5, 10, '#', '-'));
            Check.Equal("----------", ProgressBar.Render(-0.5, 10, '#', '-'));
            Check.Equal("----------", ProgressBar.Render(double.NaN, 10, '#', '-'));
        }

        public void RenderWithPercent()
        {
            Check.Equal("[#####-----] 50%", ProgressBar.RenderWithPercent(0.5, 10, '#', '-'));
            Check.Equal("[##########] 100%", ProgressBar.RenderWithPercent(1.0, 10, '#', '-'));
            Check.Equal("[----------] 0%", ProgressBar.RenderWithPercent(0.0, 10, '#', '-'));
        }

        public void InvalidWidth_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => ProgressBar.Render(0.5, 0));
        }

        // Property: bar is always exactly `width` chars, and the filled count is monotonic in fraction.
        public void Property_WidthAndMonotonic()
        {
            for (int width = 1; width <= 50; width++)
            {
                int prevFilled = -1;
                for (int step = 0; step <= 100; step++)
                {
                    string bar = ProgressBar.Render(step / 100.0, width, '#', '-');
                    Check.Equal(width, bar.Length, $"width {width} step {step}");
                    int filled = bar.Count(c => c == '#');
                    Check.True(filled >= prevFilled, $"filled decreased at width {width} step {step}");
                    prevFilled = filled;
                }
            }
        }
    }
}
