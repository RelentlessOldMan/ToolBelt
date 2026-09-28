// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Text;

namespace ToolBelt.Cli
{
    /// <summary>
    /// Renders a textual progress bar to a string (it does not touch the console). The completed fraction
    /// is clamped to [0, 1], so out-of-range inputs render as empty or full rather than throwing.
    /// </summary>
    public static class ProgressBar
    {
        /// <summary>
        /// Renders just the bar body of the given <paramref name="width"/>, e.g. <c>████░░░░░░</c>.
        /// </summary>
        public static string Render(double fraction, int width = 20, char filledChar = '█', char emptyChar = '░')
        {
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            fraction = Clamp01(fraction);

            int filled = (int)Math.Round(fraction * width, MidpointRounding.AwayFromZero);
            if (filled > width) filled = width; // guard rounding at the top edge

            var sb = new StringBuilder(width);
            sb.Append(filledChar, filled);
            sb.Append(emptyChar, width - filled);
            return sb.ToString();
        }

        /// <summary>
        /// Renders a bracketed bar with a trailing percentage, e.g. <c>[████░░░░░░] 40%</c>.
        /// </summary>
        public static string RenderWithPercent(double fraction, int width = 20, char filledChar = '█', char emptyChar = '░')
        {
            double clamped = Clamp01(fraction);
            int percent = (int)Math.Round(clamped * 100, MidpointRounding.AwayFromZero);
            return "[" + Render(clamped, width, filledChar, emptyChar) + "] "
                 + percent.ToString(CultureInfo.InvariantCulture) + "%";
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value)) return 0;
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }
    }
}
