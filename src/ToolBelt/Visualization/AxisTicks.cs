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
            double raw = max / (maxTicks - 1) - min / (maxTicks - 1); // max − min overflowed for ±1e308
            // The step is digit × 10^exponent and each tick is parsed from its exact decimal (k·digit)E(exponent):
            // rounding k·step to at most 15 decimals collapsed every tick of [1.6e-19, 1.7e-19] onto 0.
            int exponent0 = (int)Math.Floor(Math.Log10(raw));
            double step = 0;
            int digit = 1, exponent = exponent0;
            // Walk 1, 2, 5, 10, 20, 50 … × magnitude until the ticks fit. Once the step reaches the largest magnitude
            // on the axis the count is at its true floor — 2, or 3 when the range straddles zero (−s, 0, s) — and no
            // larger step can do better, so stop there. (Merely spanning the range is not enough: a step just wider
            // than the range can still put a grid line inside it.)
            double reach = Math.Max(Math.Abs(min), Math.Abs(max));
            for (int i = 0; ; i++)
            {
                int d = NiceMultipliers[i % 3], e = exponent0 + i / 3;
                double candidate = Decimal(d, e);
                if (double.IsInfinity(candidate)) break; // only near double.MaxValue: keep the last finite step
                step = candidate; digit = d; exponent = e;
                if (CountTicks(min, max, candidate) <= maxTicks || (candidate >= reach && CountTicks(min, max, candidate) != long.MaxValue))
                    break;
            }

            int decimals = DecimalsFor(step);
            long first = (long)Math.Floor(min / step + 1e-9);
            long last = (long)Math.Ceiling(max / step - 1e-9);
            var values = new List<double>();
            for (long k = first; k <= last; k++)
            {
                double v = Decimal(k * digit, exponent);
                if (double.IsInfinity(v)) continue; // a loose tick past double.MaxValue
                if (!loose && (v < min - step * 1e-9 || v > max + step * 1e-9)) continue;
                values.Add(v);
            }
            var labels = new List<string>(values.Count);
            foreach (double v in values) labels.Add(FormatLinear(v, step, decimals));
            return loose
                ? new TickSet(values, labels, step, Math.Min(values[0], min), Math.Max(values[values.Count - 1], max))
                : new TickSet(values, labels, step, min, max);
        }

        /// <summary>
        /// Decade ticks (…, 0.1, 1, 10, 100, …) for a log axis over a strictly positive range. Loose labelling
        /// rounds out to whole decades. A tight range that holds fewer than two decades (e.g. [2, 8]) gets
        /// <see cref="Linear"/> ticks inside it instead, so the axis is never unlabelled.
        /// </summary>
        public static TickSet Log10(double min, double max, bool loose = true)
        {
            if (double.IsNaN(min) || double.IsNaN(max) || double.IsInfinity(min) || double.IsInfinity(max))
                throw new ArgumentOutOfRangeException(nameof(min), "Range must be finite.");
            if (!(min > 0) || !(max > 0)) throw new ArgumentOutOfRangeException(nameof(min), "A log axis needs a strictly positive range.");
            if (min > max) (min, max) = (max, min);
            // Decades stay representable: 10^-324 is 0 and 10^309 is ∞ (a log frame rejects both), so the loose range
            // ends at the data there instead.
            int lo = Math.Max(-323, (int)Math.Floor(Math.Log10(min) + 1e-9));
            int hi = Math.Min(308, (int)Math.Ceiling(Math.Log10(max) - 1e-9));
            if (hi == lo) { if (hi < 308) hi++; else lo--; }
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
            if (!loose && values.Count < 2)
                return Linear(min, max, 6, loose: false); // no decade inside [2, 8] left the axis with no ticks
            return loose
                ? new TickSet(values, labels, 10, Math.Min(Math.Pow(10, lo), min), Math.Max(Math.Pow(10, hi), max))
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
            if (decimals < 0 || decimals > 15) throw new ArgumentOutOfRangeException(nameof(decimals), decimals, "Decimals must be in [0, 15].");
            // Rounded before splitting: the fraction rounded on its own carried into the seconds (65.9996 printed
            // "1:051.000"), and a fraction just below zero printed "1:06-.000".
            double a = Math.Round(Math.Abs(seconds), decimals, MidpointRounding.AwayFromZero);
            string sign = seconds < 0 && a > 0 ? "-" : "";
            if (span < 60)
                return sign + a.ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture) + "s";
            // In doubles: a long wrapped past 9.2e18 s. The divisions are exact (multiples of 60 below 2^53).
            double whole = Math.Floor(a), frac = a - whole;
            double s = whole % 60, minutes = (whole - s) / 60, m = minutes % 60, h = (minutes - m) / 60;
            string secs = s.ToString("00", CultureInfo.InvariantCulture);
            if (decimals > 0)
                secs += frac.ToString("." + new string('0', decimals), CultureInfo.InvariantCulture).TrimStart('0');
            return span < 3600
                ? sign + minutes.ToString("0", CultureInfo.InvariantCulture) + ":" + secs
                : sign + h.ToString("0", CultureInfo.InvariantCulture) + ":" + m.ToString("00", CultureInfo.InvariantCulture) + ":" + secs;
        }

        private static readonly int[] NiceMultipliers = { 1, 2, 5 };

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
            // A span this close to the subnormal range underflowed the step to 0, and the step search never ended
            // ([ε, ε], [−ε, ε]). Both ends are below ~1e-234 here, so the midpoint ± 1e-250 is representable.
            if (max - min < 1e-250)
            {
                double mid = min / 2 + max / 2;
                min = mid - 1e-250;
                max = mid + 1e-250;
            }
        }

        // In doubles: the long casts wrapped once |value / step| passed 9.2e18 (epoch nanoseconds read as seconds),
        // so a hopeless step "fitted". Past 2^52 steps the multiples are not even distinct, so count it as too many.
        private static long CountTicks(double min, double max, double step)
        {
            double a = min / step, b = max / step;
            if (!(Math.Abs(a) < 4.5e15) || !(Math.Abs(b) < 4.5e15)) return long.MaxValue;
            return (long)Math.Ceiling(b - 1e-9) - (long)Math.Floor(a + 1e-9) + 1;
        }

        private static int DecimalsFor(double step)
        {
            int d = (int)Math.Ceiling(-Math.Log10(step) - 1e-9);
            return Math.Max(0, Math.Min(15, d));
        }

        // digits × 10^exponent, correctly rounded (parsed from its decimal; .NET Framework throws on overflow).
        private static double Decimal(long digits, int exponent)
        {
            try
            {
                return double.Parse(digits.ToString(CultureInfo.InvariantCulture) + "E" + exponent.ToString(CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                return digits < 0 ? double.NegativeInfinity : double.PositiveInfinity;
            }
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
            {
                if (value == 0) return "0";
                // Enough mantissa digits to resolve the step: a fixed "0.###" gave every tick of
                // [12345670, 12345680] the label "1.235E+7".
                int digits = (int)Math.Floor(Math.Log10(Math.Abs(value))) - (int)Math.Floor(Math.Log10(step) + 1e-9);
                digits = Math.Max(0, Math.Min(16, digits));
                return value.ToString(digits == 0 ? "0E+0" : "0." + new string('#', digits) + "E+0", CultureInfo.InvariantCulture);
            }
            return value.ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture);
        }
    }
}
