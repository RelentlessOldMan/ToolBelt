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
    /// three (−s, 0, s) even when <c>maxTicks</c> is 2 (it must be 2 to 10,000). With
    /// <c>loose</c> labelling the ticks bracket the data and <see cref="TickSet.Min"/>/<see cref="TickSet.Max"/>
    /// give the rounded axis range; otherwise only ticks inside the data range are returned.
    /// </summary>
    public static class AxisTicks
    {
        /// <summary>Linear ticks over [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public static TickSet Linear(double min, double max, int maxTicks = 6, bool loose = true)
        {
            double givenMin = Math.Min(min, max), givenMax = Math.Max(min, max);
            bool opened = Normalize(ref min, ref max, maxTicks);
            // max − min overflowed for ±1e308, but dividing first lost a span of a few ulps at 1e15 (1e15 + 8.125 to
            // 1e15 + 8.25 gave 0): Log10(0) cast to int.MinValue and the search walked 2³¹ steps, then indexed out of range.
            double raw = double.IsInfinity(max - min) ? max / (maxTicks - 1) - min / (maxTicks - 1) : (max - min) / (maxTicks - 1);
            // The step is digit × 10^exponent and each tick is parsed from its exact decimal (k·digit)E(exponent):
            // rounding k·step to at most 15 decimals collapsed every tick of [1.6e-19, 1.7e-19] onto 0.
            // With two ticks the division doesn't halve the span, so ±1e308 still overflowed: Log10(∞) cast to int.MinValue,
            // every candidate parsed to 0 and the search walked all 2³¹ steps (4 minutes) before indexing out of range.
            double logRaw = double.IsInfinity(raw) ? Math.Log10(max / 2 - min / 2) + Math.Log10(2.0 / (maxTicks - 1)) : Math.Log10(raw);
            // A tight axis counts only the ticks inside the range, so a step below span / (maxTicks − 1) can fit: starting
            // at that magnitude, Linear(0.03, 0.13, 2, false) took step 0.1 (one tick) over 0.05 (0.05 and 0.1). A decade
            // lower reaches span / (maxTicks + 1), below which no step fits.
            int exponent0 = (int)Math.Floor(logRaw) - (loose ? 0 : 1);
            double step = 0;
            int digit = 1, exponent = exponent0;
            // Walk 1, 2, 5, 10, 20, 50 … × magnitude until the ticks fit. Once the step reaches the largest magnitude
            // on the axis the count is at its true floor — 2, or 3 when the range straddles zero (−s, 0, s) — and no
            // larger step can do better, so stop there. (Merely spanning the range is not enough: a step just wider
            // than the range can still put a grid line inside it.) Loose only: a tight axis drops −s and s once the step
            // passes the range, so Linear(−1, 1, 2, false) stopped at step 1 with three ticks where step 2 has one.
            double reach = Math.Max(Math.Abs(min), Math.Abs(max));
            for (int i = 0; ; i++)
            {
                int d = NiceMultipliers[i % 3], e = exponent0 + i / 3;
                double candidate = Decimal(d, e);
                if (double.IsInfinity(candidate)) break; // only near double.MaxValue: keep the last finite step
                step = candidate; digit = d; exponent = e;
                // A tight axis keeps only the ticks inside the range, so only those count: counting the bracketing ones
                // chose step 10 for Linear(3.4, 6.68, 2, false), which has no tick inside, over step 2 (4 and 6).
                long count = CountTicks(min, max, candidate, loose);
                // The quotient count and the exact decimal ticks can disagree at the ends (six ticks for
                // Linear(−1e100, −1.00000000000001e100, 5, false)), so a tight count that fits is recounted as emitted.
                if (!loose && count <= maxTicks) count = TightTicks(min, max, d, e, candidate).Count;
                if (count <= maxTicks || (loose && candidate >= reach && count != long.MaxValue))
                    break;
            }

            int decimals = DecimalsFor(step);
            var values = new List<double>();
            var multiples = new List<long>();
            if (loose)
            {
                long first = (long)Math.Floor(min / step + 1e-9);
                long last = (long)Math.Ceiling(max / step - 1e-9);
                for (long k = first; k <= last; k++)
                {
                    double v = Decimal(k * digit, exponent);
                    if (double.IsInfinity(v)) continue; // a loose tick past double.MaxValue
                    values.Add(v);
                    multiples.Add(k * digit);
                }
            }
            else
                foreach (long m in TightTicks(min, max, digit, exponent, step)) { values.Add(Decimal(m, exponent)); multiples.Add(m); }
            var labels = new List<string>(values.Count);
            // One format for the whole axis, from its largest magnitude: per tick, 0..1.2e7 read 0|5000000|1E+7|1.5E+7.
            double axisMag = values.Count == 0 ? 0 : Math.Max(Math.Abs(values[0]), Math.Abs(values[values.Count - 1]));
            for (int i = 0; i < values.Count; i++)
                labels.Add(FormatLinear(values[i], step, decimals, axisMag, multiples[i], exponent));
            // A range a few ulps wide has no tick inside at any step (Linear(253583.40202531937, 253583.40202531934, 6,
            // false) came back empty, an unlabelled axis): its ends are the ticks, as for an opened range.
            if (!loose && (opened || values.Count == 0)) return Within(values, labels, step, givenMin, givenMax);
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
            // Log10 of a subnormal is off by far more than the slack (Log10(1e-320) = −320.0000049), which added a decade
            // below the data: checked against the decades themselves.
            if (lo < hi && Decimal(1, lo + 1) <= min) lo++;
            if (hi > lo && Decimal(1, hi - 1) >= max) hi--;
            if (hi == lo) { if (hi < 308) hi++; else lo--; }
            var values = new List<double>();
            var labels = new List<string>();
            for (int e = lo; e <= hi; e++)
            {
                double v = Decimal(1, e); // correctly rounded: Math.Pow(10, −320) is off on .NET Framework
                if (!loose && (v < min * (1 - 1e-9) || v > max * (1 + 1e-9))) continue;
                values.Add(v);
                labels.Add(e >= -3 && e <= 5
                    ? v.ToString("0.###", CultureInfo.InvariantCulture)
                    : "1e" + e.ToString(CultureInfo.InvariantCulture));
            }
            if (!loose && values.Count < 2)
                return Linear(min, max, 6, loose: false); // no decade inside [2, 8] left the axis with no ticks
            return loose
                ? new TickSet(values, labels, 10, Math.Min(Decimal(1, lo), min), Math.Max(Decimal(1, hi), max))
                : new TickSet(values, labels, 10, min, max);
        }

        /// <summary>
        /// Ticks for a time axis given in seconds, on clock-friendly steps, labelled <c>s.fff</c>-style for
        /// sub-minute spans, <c>m:ss</c> under an hour and <c>h:mm:ss</c> beyond (hours are not wrapped at 24).
        /// </summary>
        public static TickSet Time(double minSeconds, double maxSeconds, int maxTicks = 6, bool loose = true)
        {
            double givenMin = minSeconds, givenMax = maxSeconds;
            Normalize(ref minSeconds, ref maxSeconds, maxTicks);
            double step = double.NaN;
            // As Linear: once the step reaches the largest magnitude no larger one has fewer loose ticks, so stop there
            // (Time(−1, 1, 2) fell through to day-wide ticks); a tight axis goes on to a step with fewer inside.
            double reach = Math.Max(Math.Abs(minSeconds), Math.Abs(maxSeconds));
            // Below a second the table is the 1-2-5 decimal series, which Linear continues past its 1 ms end:
            // [0.0001, 0.0002] got no tight ticks at all, and a loose [0, 0.001] axis.
            TickSet linear = Linear(givenMin, givenMax, maxTicks, loose);   // the caller's range: Linear opens it as it needs
            double fine = linear.Step;
            // Past 15 decimals the seconds labels and the rounding to them can't follow ([1.6e-19, 1.7e-19] came out as six
            // ticks at 0, all "0.000000000000000s"), so the linear ticks are used with their own labels.
            if (fine < 1e-15)
            {
                var suffixed = new List<string>(linear.Labels.Count);
                foreach (string l in linear.Labels) suffixed.Add(l + "s");
                return new TickSet(linear.Values, suffixed, linear.Step, linear.Min, linear.Max);
            }
            if (fine < TimeSteps[0]) step = fine;
            else foreach (double candidate in TimeSteps)
                if (CountTicks(minSeconds, maxSeconds, candidate, loose) <= maxTicks || (loose && candidate >= reach)) { step = candidate; break; }
            if (double.IsNaN(step))
            {
                // Beyond the table (multi-day spans): fall back to whole multiples of a day on a nice 1-2-5 scale.
                TickSet days = Linear(minSeconds / 86400, maxSeconds / 86400, maxTicks, loose);
                step = Math.Max(1, days.Step) * 86400;
            }

            int decimals = step >= 1 ? 0 : DecimalsFor(step);
            // Near double.MaxValue k·step overflowed to ∞ (Time(0, MaxValue) threw from FormatTime, naming "seconds"),
            // and a multiple past 2^62 would not fit the long: the linear ticks, in seconds, are used there.
            double firstMultiple = Math.Floor(minSeconds / step + 1e-9), lastMultiple = Math.Ceiling(maxSeconds / step - 1e-9);
            // A day step past double.MaxValue (Time(0, 1.73e308, 2)) made 0 · ∞ = NaN, which got past an ∞-only test and
            // threw from FormatTime naming "seconds".
            if (!(Math.Abs(firstMultiple) < 4.6e18 && Math.Abs(lastMultiple) < 4.6e18)
                || !(Math.Abs(firstMultiple * step) <= double.MaxValue) || !(Math.Abs(lastMultiple * step) <= double.MaxValue))
            {
                var suffixed = new List<string>(linear.Labels.Count);
                foreach (string l in linear.Labels) suffixed.Add(l + "s");
                return new TickSet(linear.Values, suffixed, linear.Step, linear.Min, linear.Max);
            }
            long first = (long)firstMultiple;
            long last = (long)lastMultiple;
            var values = new List<double>();
            for (long k = first; k <= last; k++)
            {
                double v = Clean(k * step, decimals);
                if (!loose && (v < minSeconds - step * 1e-9 || v > maxSeconds + step * 1e-9)) continue;
                values.Add(v);
            }
            // No tick inside a range a few ulps wide: the linear ticks, which are then its ends (it was unlabelled). And
            // the day steps were counted in days, not as the seconds ticks round, so Time(−5.82926823563374e24,
            // −5.829268235633367e24, 4, false) had five: the linear ticks keep to maxTicks.
            if (!loose && (values.Count == 0 || values.Count > maxTicks))
            {
                var suffixed = new List<string>(linear.Labels.Count);
                foreach (string l in linear.Labels) suffixed.Add(l + "s");
                return new TickSet(linear.Values, suffixed, linear.Step, linear.Min, linear.Max);
            }
            // Label format follows the largest magnitude on the axis.
            double span = Math.Max(Math.Abs(minSeconds), Math.Abs(maxSeconds));
            if (values.Count > 0)
                span = Math.Max(span, Math.Max(Math.Abs(values[0]), Math.Abs(values[values.Count - 1])));
            var labels = new List<string>(values.Count);
            foreach (double v in values) labels.Add(Math.Abs(v) >= 9007199254740992.0 ? ExponentSeconds(v, step) : FormatTime(v, span, decimals));
            return loose
                // Clamped as Linear's: the 1e-9 slack dropped a bracketing tick (Time(−3.5e-8, 8054.7, 10) gave Min 0).
                ? new TickSet(values, labels, step, Math.Min(values[0], minSeconds), Math.Max(values[values.Count - 1], maxSeconds))
                : new TickSet(values, labels, step, minSeconds, maxSeconds);
        }

        /// <summary>Formats a number of seconds the way <see cref="Time"/> labels it (<c>m:ss</c>, <c>h:mm:ss</c>, or seconds).</summary>
        public static string FormatTime(double seconds, double span, int decimals = 0)
        {
            if (decimals < 0 || decimals > 15) throw new ArgumentOutOfRangeException(nameof(decimals), decimals, "Decimals must be in [0, 15].");
            // NaN printed "NaNs" and ∞ "Infinitys".
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Seconds must be finite.");
            // h:mm:ss ran to 300 digits for 1e300 s. From 2^53 s (285 million years) whole seconds are not exact, so the
            // digits after the hours were rounding noise: exponent-form seconds from there.
            if (Math.Abs(seconds) >= 9007199254740992.0) return seconds.ToString("0.###e+0", CultureInfo.InvariantCulture) + "s";
            // Rounded before splitting: the fraction rounded on its own carried into the seconds (65.9996 printed
            // "1:051.000"), and a fraction just below zero printed "1:06-.000".
            double a = RoundAt(Math.Abs(seconds), decimals, MidpointRounding.AwayFromZero);
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

        // From 2^53 s FormatTime keeps four significant digits, so distinct ticks read "5.028e+306s" twice: here the
        // digits follow the step, one more than it needs, in FormatTime's form.
        private static string ExponentSeconds(double v, double step)
        {
            int digits = (int)Math.Floor(Math.Log10(Math.Abs(v))) - (int)Math.Floor(Math.Log10(step)) + 2;
            digits = Math.Max(4, Math.Min(17, digits));
            string e = v.ToString("E" + (digits - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            int at = e.IndexOf('E');
            string mantissa = e.Substring(0, at);
            if (mantissa.IndexOf('.') >= 0) mantissa = mantissa.TrimEnd('0').TrimEnd('.');
            int exponent = int.Parse(e.Substring(at + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return mantissa + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString(CultureInfo.InvariantCulture) + "s";
        }

        private static readonly int[] NiceMultipliers = { 1, 2, 5 };

        private const int MaxTickCount = 10000;

        private static readonly double[] TimeSteps =
        {
            0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5,
            1, 2, 5, 10, 15, 30,
            60, 120, 300, 600, 900, 1800,
            3600, 7200, 10800, 21600, 43200, 86400,
        };

        // True when the range was opened because it is too narrow to step.
        private static bool Normalize(ref double min, ref double max, int maxTicks)
        {
            if (maxTicks < 2) throw new ArgumentOutOfRangeException(nameof(maxTicks), maxTicks, "Need at least two ticks.");
            // int.MaxValue ticks built 2^31 ticks and labels (or ran out of memory).
            if (maxTicks > MaxTickCount) throw new ArgumentOutOfRangeException(nameof(maxTicks), maxTicks, $"At most {MaxTickCount} ticks.");
            if (double.IsNaN(min) || double.IsNaN(max) || double.IsInfinity(min) || double.IsInfinity(max))
                throw new ArgumentOutOfRangeException(nameof(min), "Range must be finite.");
            if (min > max) (min, max) = (max, min);
            if (min == max)
            {
                // A flat range still needs an axis: open it symmetrically around the value.
                double pad = min == 0 ? 1 : Math.Abs(min) * 0.1;
                // Near ±double.MaxValue one side overflowed to ∞, and the tick loop then never ended: pad the other side.
                if (double.IsInfinity(max + pad)) min -= 2 * pad;
                else if (double.IsInfinity(min - pad)) max += 2 * pad;
                else { min -= pad; max += pad; }
            }
            // A span this close to the subnormal range underflowed the step to 0, and the step search never ended
            // ([ε, ε], [−ε, ε]). Both ends are below ~1e-284 here, so the midpoint ± 1e-300 is representable. (From
            // 1e-250 this opened [1e-300, 2e-300], which steps fine.)
            if (max - min < 1e-300)
            {
                double mid = min / 2 + max / 2;
                min = mid - 1e-300;
                max = mid + 1e-300;
                return true;
            }
            return false;
        }

        // In doubles: the long casts wrapped once |value / step| passed 9.2e18 (epoch nanoseconds read as seconds),
        // so a hopeless step "fitted". Past 2^52 steps the multiples are not even distinct, so count it as too many.
        // Loose: the ticks that bracket the range; tight: those inside it.
        private static long CountTicks(double min, double max, double step, bool loose)
        {
            double a = min / step, b = max / step;
            if (!(Math.Abs(a) < 4.5e15) || !(Math.Abs(b) < 4.5e15)) return long.MaxValue;
            return loose
                ? (long)Math.Ceiling(b - 1e-9) - (long)Math.Floor(a + 1e-9) + 1
                : (long)Math.Floor(b + 1e-9) - (long)Math.Ceiling(a - 1e-9) + 1;
        }

        // The multiples (in units of 10^exponent) of the tight ticks inside [min, max], each tested at its exact decimal
        // value, as they are emitted. Called only once the quotient count fits, so the walk is short.
        private static List<long> TightTicks(double min, double max, int digit, int exponent, double step)
        {
            var multiples = new List<long>();
            long first = (long)Math.Floor(min / step + 1e-9), last = (long)Math.Ceiling(max / step - 1e-9);
            for (long k = first; k <= last; k++)
            {
                double v = Decimal(k * digit, exponent);
                // A tick past double.MaxValue (max + step · 1e-9 is ∞ there, so the range test keeps it).
                if (double.IsInfinity(v) || v < min - step * 1e-9 || v > max + step * 1e-9) continue;
                multiples.Add(k * digit);
            }
            return multiples;
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
            double v = RoundAt(value, decimals, MidpointRounding.ToEven);
            return v == 0 ? 0 : v; // no negative zero
        }

        // Math.Round(v, d) scales by 10^d, which is inexact once |v|·10^d passes 2^52: ticks at 1e15 + 0.5 came out
        // 1e15 + 0.375 and 1e12 + 0.0005 rounded to 1e12. Past that only the fraction (exact: v − trunc(v)) is rounded.
        private static double RoundAt(double v, int decimals, MidpointRounding mode)
        {
            double a = Math.Abs(v);
            if (a >= 4503599627370496.0) return v; // 2^52: no fraction left
            if (a * Math.Pow(10, decimals) < 4503599627370496.0) return Math.Round(v, decimals, mode);
            double whole = Math.Truncate(v);
            return whole + Math.Round(v - whole, decimals, mode);
        }

        // The tick is multiple × 10^exponent, exactly.
        private static string FormatLinear(double value, double step, int decimals, double axisMag, long multiple, int exponent)
        {
            double mag = Math.Max(axisMag, step);
            if (mag >= 1e7 || step < 1e-5)
            {
                // From the tick's exact decimal: a custom format prints at most 15 significant digits, so the ticks of
                // [1, 1 + 4e-15] read 1E+0 five times (and the 12345670..12345680 ticks once all read "1.235E+7").
                if (multiple == 0) return "0";
                string digits = (multiple < 0 ? -multiple : multiple).ToString(CultureInfo.InvariantCulture);
                int scientific = exponent + digits.Length - 1;
                digits = digits.TrimEnd('0');
                string mantissa = digits.Length == 1 ? digits : digits.Substring(0, 1) + "." + digits.Substring(1);
                return (multiple < 0 ? "-" : "") + mantissa + "E" + (scientific < 0 ? "-" : "+")
                    + Math.Abs(scientific).ToString(CultureInfo.InvariantCulture);
            }
            return value.ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture);
        }

        // The ticks inside [min, max] (an axis opened around a range too narrow to step), and the range itself; its ends
        // are the ticks when no step fits inside. The opened range was returned as the tight range, and Log10(1e-300,
        // 2e-300, loose: false) had ticks at ±1e-250 and none on the data.
        // The fewest significant digits that read back: "R" printed double.Epsilon as "5E-324" on .NET Core and
        // "4.94065645841247E-324" on .NET Framework.
        private static string Shortest(double v)
        {
            for (int p = 1; p < 17; p++)
            {
                string s = v.ToString("G" + p.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                if (double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture) == v) return s;
            }
            return v.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static TickSet Within(List<double> values, List<string> labels, double step, double min, double max)
        {
            var v = new List<double>();
            var l = new List<string>();
            for (int i = 0; i < values.Count; i++)
                if (values[i] >= min && values[i] <= max) { v.Add(values[i]); l.Add(labels[i]); }
            if (v.Count < 2)
            {
                v.Clear(); l.Clear();
                v.Add(min); l.Add(Shortest(min));
                if (max != min) { v.Add(max); l.Add(Shortest(max)); }
            }
            return new TickSet(v, l, step, min, max);
        }
    }
}
