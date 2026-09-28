using System;
using ToolBelt.Cli;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Cli
{
    public sealed class SparklineTests
    {
        public void MapsRangeToBlocks()
        {
            // 0..7 with explicit bounds maps to each of the 8 blocks in order.
            string s = Sparkline.Render(new double[] { 0, 1, 2, 3, 4, 5, 6, 7 }, 0, 7);
            Check.Equal("▁▂▃▄▅▆▇█", s);
        }

        public void EmptyInput()
        {
            Check.Equal("", Sparkline.Render(Array.Empty<double>()));
        }

        public void FlatSeriesAllLowest()
        {
            string s = Sparkline.Render(new double[] { 5, 5, 5 });
            Check.Equal("▁▁▁", s);
        }

        public void AutoScales()
        {
            // min and max map to the extremes regardless of absolute values.
            string s = Sparkline.Render(new double[] { 10, 20 });
            Check.Equal("▁█", s);
        }

        public void ClampsWithExplicitBounds()
        {
            // Values outside [min,max] clamp to the end blocks.
            string s = Sparkline.Render(new double[] { -5, 100 }, 0, 10);
            Check.Equal("▁█", s);
        }

        public void SingleValue()
        {
            Check.Equal("▁", Sparkline.Render(new double[] { 42 }));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Sparkline.Render(null!));
        }
    }
}
