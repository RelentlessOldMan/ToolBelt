// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ToolBelt.Visualization
{
    /// <summary>A computed set of axis ticks: positions, their labels, and the spacing between them.</summary>
    public sealed class TickSet
    {
        internal TickSet(IReadOnlyList<double> values, IReadOnlyList<string> labels, double step, double min, double max)
        {
            Values = values;
            Labels = labels;
            Step = step;
            Min = min;
            Max = max;
        }

        /// <summary>Tick positions in data units, ascending.</summary>
        public IReadOnlyList<double> Values { get; }

        /// <summary>A label per tick, formatted to the precision the spacing warrants.</summary>
        public IReadOnlyList<string> Labels { get; }

        /// <summary>The spacing between ticks (for log ticks, the decade factor, i.e. 10).</summary>
        public double Step { get; }

        /// <summary>The suggested axis range: the outermost ticks for loose labelling, else the data range.</summary>
        public double Min { get; }

        /// <summary>See <see cref="Min"/>.</summary>
        public double Max { get; }

        public int Count => Values.Count;
    }

    /// <summary>
    /// "Nice" axis ticks: round steps of 1, 2 or 5 × 10ⁿ for linear axes, whole decades for log axes, and
    /// clock-friendly steps (1/2/5/10/15/30 s, 1/2/5/10/15/30 min, 1/2/3/6/12 h …) for time axes, each with
    /// matching labels. The linear and time pickers choose the smallest nice step whose tick count does not
    /// exceed <c>maxTicks</c>, so the bound is guaranteed (unlike the classic loose heuristics) — with one
    /// unavoidable exception: ticks sit on multiples of the step, so a loose range straddling zero needs at least
    /// three (−s, 0, s) even when <c>maxTicks</c> is 2. With
    /// <c>loose</c> labelling the ticks bracket the data and <see cref="TickSet.Min"/>/<see cref="TickSet.Max"/>
    /// give the rounded axis range; otherwise only ticks inside the data range are returned.
    /// </summary>
    public static class AxisTicks
    {
        /// <summary>Linear ticks over [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public static TickSet Linear(double min, double max, int maxTicks = 6, bool loose = true)
        {
            Normalize(ref min, ref max, maxTicks);
            double raw = (max - min) / (maxTicks - 1);
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double step = 0;
            // Walk 1, 2, 5, 10, 20, 50 … × magnitude until the ticks fit. Once the step reaches the largest magnitude
            // on the axis the count is at its true floor — 2, or 3 when the range straddles zero (−s, 0, s) — and no
            // larger step can do better, so stop there. (Merely spanning the range is not enough: a step just wider
            // than the range can still put a grid line inside it.)
            double reach = Math.Max(Math.Abs(min), Math.Abs(max));
            for (int i = 0; ; i++)
            {
                double candidate = NiceMultipliers[i % 3] * magnitude * Math.Pow(10, i / 3);
                if (CountTicks(min, max, candidate) <= maxTicks || candidate >= reach) { step = candidate; break; }
            }

            int decimals = DecimalsFor(step);
            long first = (long)Math.Floor(min / step + 1e-9);
            long last = (long)Math.Ceiling(max / step - 1e-9);
            var values = new List<double>();
            for (long k = first; k <= last; k++)
            {
                double v = Clean(k * step, decimals);
                if (!loose && (v < min - step * 1e-9 || v > max + step * 1e-9)) continue;
                values.Add(v);
            }
            var labels = new List<string>(values.Count);
            foreach (double v in values) labels.Add(FormatLinear(v, step, decimals));
            return loose
                ? new TickSet(values, labels, step, values[0], values[values.Count - 1])
                : new TickSet(values, labels, step, min, max);
        }

        /// <summary>
        /// Decade ticks (…, 0.1, 1, 10, 100, …) for a log axis over a strictly positive range. Loose labelling
        /// rounds out to whole decades.
        /// </summary>
        public static TickSet Log10(double min, double max, bool loose = true)
        {
            if (double.IsNaN(min) || double.IsNaN(max) || double.IsInfinity(min) || double.IsInfinity(max))
                throw new ArgumentOutOfRangeException(nameof(min), "Range must be finite.");
            if (!(min > 0) || !(max > 0)) throw new ArgumentOutOfRangeException(nameof(min), "A log axis needs a strictly positive range.");
            if (min > max) (min, max) = (max, min);
            int lo = (int)Math.Floor(Math.Log10(min) + 1e-9);
            int hi = (int)Math.Ceiling(Math.Log10(max) - 1e-9);
            if (hi == lo) hi = lo + 1;
            var values = new List<double>();
            var labels = new List<string>();
            for (int e = lo; e <= hi; e++)
            {
                double v = Math.Pow(10, e);
                if (!loose && (v < min * (1 - 1e-9) || v > max * (1 + 1e-9))) continue;
                values.Add(v);
                labels.Add(e >= -3 && e <= 5
                    ? v.ToString("0.###", CultureInfo.InvariantCulture)
                    : "1e" + e.ToString(CultureInfo.InvariantCulture));
            }
            return loose
                ? new TickSet(values, labels, 10, Math.Pow(10, lo), Math.Pow(10, hi))
                : new TickSet(values, labels, 10, min, max);
        }

        /// <summary>
        /// Ticks for a time axis given in seconds, on clock-friendly steps, labelled <c>s.fff</c>-style for
        /// sub-minute spans, <c>m:ss</c> under an hour and <c>h:mm:ss</c> beyond (hours are not wrapped at 24).
        /// </summary>
        public static TickSet Time(double minSeconds, double maxSeconds, int maxTicks = 6, bool loose = true)
        {
            Normalize(ref minSeconds, ref maxSeconds, maxTicks);
            double step = double.NaN;
            foreach (double candidate in TimeSteps)
                if (CountTicks(minSeconds, maxSeconds, candidate) <= maxTicks) { step = candidate; break; }
            if (double.IsNaN(step))
            {
                // Beyond the table (multi-day spans): fall back to whole multiples of a day on a nice 1-2-5 scale.
                TickSet days = Linear(minSeconds / 86400, maxSeconds / 86400, maxTicks, loose);
                step = Math.Max(1, days.Step) * 86400;
            }

            int decimals = step >= 1 ? 0 : DecimalsFor(step);
            long first = (long)Math.Floor(minSeconds / step + 1e-9);
            long last = (long)Math.Ceiling(maxSeconds / step - 1e-9);
            var values = new List<double>();
            for (long k = first; k <= last; k++)
            {
                double v = Clean(k * step, decimals);
                if (!loose && (v < minSeconds - step * 1e-9 || v > maxSeconds + step * 1e-9)) continue;
                values.Add(v);
            }
            // Label format follows the largest magnitude on the axis (tight labelling may leave no ticks at all).
            double span = Math.Max(Math.Abs(minSeconds), Math.Abs(maxSeconds));
            if (values.Count > 0)
                span = Math.Max(span, Math.Max(Math.Abs(values[0]), Math.Abs(values[values.Count - 1])));
            var labels = new List<string>(values.Count);
            foreach (double v in values) labels.Add(FormatTime(v, span, decimals));
            return loose
                ? new TickSet(values, labels, step, values[0], values[values.Count - 1])
                : new TickSet(values, labels, step, minSeconds, maxSeconds);
        }

        /// <summary>Formats a number of seconds the way <see cref="Time"/> labels it (<c>m:ss</c>, <c>h:mm:ss</c>, or seconds).</summary>
        public static string FormatTime(double seconds, double span, int decimals = 0)
        {
            string sign = seconds < 0 ? "-" : "";
            double a = Math.Abs(seconds);
            if (span < 60)
                return sign + a.ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture) + "s";
            long whole = (long)Math.Floor(a + 1e-9);
            double frac = a - whole;
            long h = whole / 3600, m = whole / 60 % 60, s = whole % 60;
            string secs = s.ToString("00", CultureInfo.InvariantCulture);
            if (decimals > 0)
                secs += frac.ToString("." + new string('0', decimals), CultureInfo.InvariantCulture).TrimStart('0');
            return span < 3600
                ? sign + (whole / 60).ToString(CultureInfo.InvariantCulture) + ":" + secs
                : sign + h.ToString(CultureInfo.InvariantCulture) + ":" + m.ToString("00", CultureInfo.InvariantCulture) + ":" + secs;
        }

        private static readonly double[] NiceMultipliers = { 1, 2, 5 };

        private static readonly double[] TimeSteps =
        {
            0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5,
            1, 2, 5, 10, 15, 30,
            60, 120, 300, 600, 900, 1800,
            3600, 7200, 10800, 21600, 43200, 86400,
        };

        private static void Normalize(ref double min, ref double max, int maxTicks)
        {
            if (maxTicks < 2) throw new ArgumentOutOfRangeException(nameof(maxTicks), maxTicks, "Need at least two ticks.");
            if (double.IsNaN(min) || double.IsNaN(max) || double.IsInfinity(min) || double.IsInfinity(max))
                throw new ArgumentOutOfRangeException(nameof(min), "Range must be finite.");
            if (min > max) (min, max) = (max, min);
            if (min == max)
            {
                // A flat range still needs an axis: open it symmetrically around the value.
                double pad = min == 0 ? 1 : Math.Abs(min) * 0.1;
                min -= pad;
                max += pad;
            }
        }

        private static long CountTicks(double min, double max, double step)
            => (long)Math.Ceiling(max / step - 1e-9) - (long)Math.Floor(min / step + 1e-9) + 1;

        private static int DecimalsFor(double step)
        {
            int d = (int)Math.Ceiling(-Math.Log10(step) - 1e-9);
            return Math.Max(0, Math.Min(15, d));
        }

        private static double Clean(double value, int decimals)
        {
            double v = Math.Round(value, decimals);
            return v == 0 ? 0 : v; // no negative zero
        }

        private static string FormatLinear(double value, double step, int decimals)
        {
            double mag = Math.Max(Math.Abs(value), step);
            if (mag >= 1e7 || step < 1e-5)
                return value == 0 ? "0" : value.ToString("0.###E+0", CultureInfo.InvariantCulture);
            return value.ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture);
        }
    }
}
