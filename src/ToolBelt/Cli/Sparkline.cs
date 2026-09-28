// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Cli
{
    /// <summary>
    /// Renders a numeric series as a one-line block-character mini chart (▁▂▃▄▅▆▇█), auto-scaling to the
    /// data's range unless explicit bounds are given. Tiny and disproportionately useful for console
    /// diagnostics where opening an image would be overkill.
    /// </summary>
    public static class Sparkline
    {
        private const string Blocks = "▁▂▃▄▅▆▇█"; // 8 levels, lowest to highest

        public static string Render(IReadOnlyList<double> values, double? min = null, double? max = null)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            if (values.Count == 0) return string.Empty;

            double lo = min ?? double.PositiveInfinity;
            double hi = max ?? double.NegativeInfinity;
            if (min is null || max is null)
            {
                foreach (double v in values)
                {
                    if (double.IsNaN(v)) continue;
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                }
            }

            var sb = new StringBuilder(values.Count);
            double range = hi - lo;
            foreach (double v in values)
            {
                if (double.IsNaN(v)) { sb.Append(' '); continue; }
                int level;
                if (range <= 0)
                    level = 0; // flat series -> all lowest block
                else
                {
                    double t = (v - lo) / range;
                    if (t < 0) t = 0;
                    if (t > 1) t = 1;
                    level = (int)Math.Round(t * (Blocks.Length - 1), MidpointRounding.AwayFromZero);
                }
                sb.Append(Blocks[level]);
            }
            return sb.ToString();
        }
    }
}
