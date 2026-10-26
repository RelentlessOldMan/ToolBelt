// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Cli
{
    /// <summary>
    /// Renders a numeric series as a one-line block-character mini chart (▁▂▃▄▅▆▇█), auto-scaling to the
    /// data's range unless explicit bounds are given. Tiny and disproportionately useful for console
    /// diagnostics where opening an image would be overkill. NaN draws as a space, +∞ as the top block and −∞ as
    /// the bottom one; auto-scaling uses the finite values only, and a bound you supply is kept even if the other is
    /// computed.
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
                    if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                    if (min is null && v < lo) lo = v;
                    if (max is null && v > hi) hi = v;
                }
            }

            // One explicit bound past all the data: the computed one is pulled to it. A max below every value made the
            // range negative and drew them all as the lowest block; they clamp to the max and draw at the top.
            if (min is null && max is not null && lo > hi) lo = hi;
            if (max is null && min is not null && hi < lo) hi = lo;

            var sb = new StringBuilder(values.Count);
            double range = hi - lo;
            bool halve = double.IsInfinity(range) && !double.IsInfinity(lo) && !double.IsInfinity(hi);
            if (halve) range = hi / 2 - lo / 2; // the full range overflows a double; scale both halves instead
            foreach (double v in values)
            {
                if (double.IsNaN(v)) { sb.Append(' '); continue; }
                int level;
                if (double.IsPositiveInfinity(v))
                    level = Blocks.Length - 1;
                else if (double.IsNegativeInfinity(v))
                    level = 0;
                else if (!(range > 0) || double.IsInfinity(range))
                    level = range == 0 && v > hi ? Blocks.Length - 1 : 0; // flat (or no finite) series -> lowest block, unless above the max
                else
                {
                    double t = halve ? (v / 2 - lo / 2) / range : (v - lo) / range;
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
