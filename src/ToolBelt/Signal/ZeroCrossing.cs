// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Locates the points where a signal crosses zero, returning fractional sample indices via linear
    /// interpolation between the straddling samples. The shared primitive underneath period, frequency and
    /// pulse-timing measurements. A run of exact zeros counts once, at its middle, and only when the signal changes sign
    /// across it (touching zero and turning back is no crossing); a run at either end of the record counts once.
    /// Non-finite samples are dropouts: no crossing is reported next to them.
    /// </summary>
    public static class ZeroCrossing
    {
        public static IReadOnlyList<double> Find(IReadOnlyList<double> signal)
        {
            if (signal is null) throw new ArgumentNullException(nameof(signal));
            var crossings = new List<double>();

            int n = signal.Count;
            for (int i = 0; i < n; i++)
            {
                double a = signal[i];
                if (a == 0)
                {
                    // Every zero sample used to count, so N samples of digital silence were N crossings.
                    int end = i;
                    while (end + 1 < n && signal[end + 1] == 0) end++;
                    int before = i > 0 ? Sign(signal[i - 1]) : Edge, after = end + 1 < n ? Sign(signal[end + 1]) : Edge;
                    bool crosses = before == Edge ? after is 1 or -1
                                 : after == Edge ? before is 1 or -1
                                 : before * after == -1;
                    if (crosses) crossings.Add(i + (end - i) / 2.0);
                    i = end;
                    continue;
                }
                if (i + 1 >= n) break;
                double b = signal[i + 1];
                // Opposite signs (and b not exactly zero): interpolate the crossing between i and i+1. A NaN or ∞ on either
                // side (a dropout) is no crossing. a / (a − b) overflowed for ±1e308; 1 / (1 − b/a) can't.
                if (IsFinite(a) && IsFinite(b) && ((a < 0 && b > 0) || (a > 0 && b < 0)))
                    crossings.Add(i + 1 / (1 - b / a));
            }
            return crossings;
        }

        // Sign gives 1 or −1 for a finite non-zero value and 0 for a dropout (NaN or ∞); Edge is "beyond the record".
        private const int Edge = 2;

        private static int Sign(double v) => !IsFinite(v) ? 0 : v > 0 ? 1 : -1;

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
