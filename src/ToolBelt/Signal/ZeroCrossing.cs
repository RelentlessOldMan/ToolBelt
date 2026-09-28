// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Locates the points where a signal crosses zero, returning fractional sample indices via linear
    /// interpolation between the straddling samples. The shared primitive underneath period, frequency and
    /// pulse-timing measurements.
    /// </summary>
    public static class ZeroCrossing
    {
        public static IReadOnlyList<double> Find(IReadOnlyList<double> signal)
        {
            if (signal is null) throw new ArgumentNullException(nameof(signal));
            var crossings = new List<double>();

            for (int i = 0; i + 1 < signal.Count; i++)
            {
                double a = signal[i], b = signal[i + 1];

                if (a == 0)
                {
                    crossings.Add(i); // exact crossing at the sample
                    continue;
                }
                // Opposite signs (and b not exactly zero): interpolate the crossing between i and i+1.
                if (b != 0 && Math.Sign(a) != Math.Sign(b))
                    crossings.Add(i + a / (a - b));
            }

            // A trailing exact zero at the last sample.
            if (signal.Count > 0 && signal[signal.Count - 1] == 0)
                crossings.Add(signal.Count - 1);

            return crossings;
        }
    }
}
