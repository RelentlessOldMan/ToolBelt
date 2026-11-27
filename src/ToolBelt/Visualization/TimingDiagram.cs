// ToolBelt drop-in — also copy Visualization/AxisTicks.cs and SvgUtils.cs (and ImageBuffer.cs for Rgba).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace ToolBelt.Visualization
{
    /// <summary>One lane of a <see cref="TimingDiagram"/>.</summary>
    public sealed class TimingSignal
    {
        private TimingSignal(string name, bool isBus, IReadOnlyList<(double Time, string? Value)> changes)
        {
            Name = name;
            IsBus = isBus;
            Changes = changes;
        }

        public string Name { get; }

        /// <summary>True for a multi-bit bus (drawn as labelled segments), false for a single digital line.</summary>
        public bool IsBus { get; }

        /// <summary>Value changes in time order. Digital values are "1"/"0"; null means unknown/undriven (drawn hatched).</summary>
        public IReadOnlyList<(double Time, string? Value)> Changes { get; }

        /// <summary>A digital line from (time, level) changes; null level = unknown.</summary>
        public static TimingSignal Digital(string name, IEnumerable<(double Time, bool? Level)> changes)
            => new TimingSignal(Check(name), false, Sorted(changes?.Select(c => (c.Time, c.Level is bool b ? (b ? "1" : "0") : null))));

        /// <summary>A bus from (time, value text) changes; null value = unknown.</summary>
        public static TimingSignal Bus(string name, IEnumerable<(double Time, string? Value)> changes)
            => new TimingSignal(Check(name), true, Sorted(changes));

        /// <summary>A clock: rising edges at <paramref name="start"/> + k·<paramref name="period"/> until <paramref name="end"/>.</summary>
        public static TimingSignal Clock(string name, double period, double start, double end, double duty = 0.5)
        {
            // ∞ gave zero cycles and an empty clock without a word.
            if (!(period > 0) || double.IsInfinity(period)) throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be positive and finite.");
            if (!(duty > 0 && duty < 1)) throw new ArgumentOutOfRangeException(nameof(duty), duty, "Duty must be in (0, 1).");
            if (double.IsNaN(start) || double.IsInfinity(start) || double.IsNaN(end) || double.IsInfinity(end)) throw new ArgumentException("Start and end must be finite.", double.IsNaN(start) || double.IsInfinity(start) ? nameof(start) : nameof(end));
            // A reversed window gave no cycles, so the lane drew as all unknown without a word.
            if (end < start) throw new ArgumentException("The clock's end is before its start.", nameof(end));
            double cycles = Math.Ceiling((end - start) / period);
            if (cycles > 100_000) throw new ArgumentException("Too many clock cycles to draw.", nameof(period));
            var c = new List<(double, bool?)>();
            // Integer cycle index: `t += period` stops advancing when start is huge relative to period (epoch seconds, 100 MHz).
            for (int k = 0; k < cycles; k++)
            {
                double t = start + k * period;
                if (t >= end) break;
                double fall = t + period * duty;
                // Past double.MaxValue the edge failed in the constructor, naming "changes" (not a parameter here).
                if (double.IsInfinity(fall)) throw new ArgumentException("A clock edge lies beyond double.MaxValue.", nameof(period));
                // At a start far above the period the edges rounded onto each other (1e15 with period 0.01), and
                // equal times kept only the last change, so the clock drew as a flat line.
                if (!(fall > t) || !(start + (k + 1) * period > fall))
                    throw new ArgumentException("The period is too small to resolve at this start time.", nameof(period));
                c.Add((t, true));
                c.Add((fall, false));
            }
            return Digital(name, c);
        }

        /// <summary>
        /// A digital line from sampled data: high above <paramref name="highThreshold"/>, low below
        /// <paramref name="lowThreshold"/> (hysteresis between), edges at sample times. Starts unknown until the first
        /// sample outside the band. A NaN sample (a dropout) is unknown, and so is the line after it until a sample
        /// leaves the band.
        /// </summary>
        public static TimingSignal FromSamples(string name, IReadOnlyList<double> samples, double sampleRate, double lowThreshold, double highThreshold, double startTime = 0)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            // ∞ put every sample at startTime, so only the last change survived.
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            // A NaN low threshold was reported as the high one.
            if (double.IsNaN(lowThreshold)) throw new ArgumentOutOfRangeException(nameof(lowThreshold), lowThreshold, "Low threshold must not be NaN.");
            if (double.IsNaN(highThreshold)) throw new ArgumentOutOfRangeException(nameof(highThreshold), highThreshold, "High threshold must not be NaN.");
            if (!(highThreshold >= lowThreshold)) throw new ArgumentException("High threshold must not be below the low threshold.", nameof(highThreshold));
            // Both failed in the constructor, naming "changes" (not a parameter here).
            if (double.IsNaN(startTime) || double.IsInfinity(startTime)) throw new ArgumentOutOfRangeException(nameof(startTime), startTime, "Start time must be finite.");
            if (samples.Count > 1 && double.IsInfinity(startTime + (samples.Count - 1) / sampleRate))
                throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "The sample times run beyond double.MaxValue.");
            // Sample times that round onto each other (startTime 1e300) collapsed the edges.
            if (samples.Count > 1 && !(startTime + 1 / sampleRate > startTime && startTime + (samples.Count - 1) / sampleRate > startTime + (samples.Count - 2) / sampleRate))
                throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "The sample interval is too small to resolve at this start time.");
            var c = new List<(double, bool?)>();
            bool? level = null;
            c.Add((startTime, null));
            for (int i = 0; i < samples.Count; i++)
            {
                // NaN is neither above nor below, so it held the last level: a dropout drew as a valid high or low.
                bool? next = double.IsNaN(samples[i]) ? null : samples[i] > highThreshold ? true : samples[i] < lowThreshold ? false : level;
                if (next != level) { c.Add((startTime + i / sampleRate, next)); level = next; }
            }
            return Digital(name, c);
        }

        private static string Check(string name) => name ?? throw new ArgumentNullException(nameof(name));

        private static IReadOnlyList<(double, string?)> Sorted(IEnumerable<(double Time, string? Value)>? changes)
        {
            if (changes is null) throw new ArgumentNullException(nameof(changes));
            var list = changes.ToList();
            if (list.Any(c => double.IsNaN(c.Time) || double.IsInfinity(c.Time))) throw new ArgumentException("Change times must be finite.", nameof(changes));
            // Stable sort; for equal times the last change wins.
            var ordered = list.Select((c, i) => (c, i)).OrderBy(t => t.c.Time).ThenBy(t => t.i).Select(t => t.c).ToList();
            var result = new List<(double, string?)>();
            foreach (var c in ordered)
            {
                if (result.Count > 0 && result[result.Count - 1].Item1 == c.Time)
                {
                    // The replacement may now repeat its predecessor's value (0→1 then 1→0 at one time): drop it then,
                    // as a same-value change is dropped anywhere else.
                    if (result.Count > 1 && result[result.Count - 2].Item2 == c.Value) result.RemoveAt(result.Count - 1);
                    else result[result.Count - 1] = (c.Time, c.Value);
                }
                else if (result.Count == 0 || result[result.Count - 1].Item2 != c.Value) result.Add((c.Time, c.Value));
            }
            return result.AsReadOnly(); // a bare List could be cast back and edited
        }
    }

    /// <summary>A labelled vertical cursor on a timing diagram.</summary>
    public readonly struct TimingMarker
    {
        public TimingMarker(double time, string label, Rgba? color = null)
        {
            if (double.IsNaN(time) || double.IsInfinity(time))
                throw new ArgumentOutOfRangeException(nameof(time), time, "Marker time must be finite.");
            Time = time;
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Color = color ?? new Rgba(213, 94, 0);
        }

        public double Time { get; }
        public string Label { get; }
        public Rgba Color { get; }
    }

    /// <summary>Options for <see cref="TimingDiagram.RenderSvg"/>.</summary>
    public sealed class TimingDiagramOptions
    {
        public double Width { get; set; } = 900;
        public double LaneHeight { get; set; } = 28;
        public double LaneGap { get; set; } = 10;

        /// <summary>Time window (default: first to last change across all signals).</summary>
        public double? Start { get; set; }
        public double? End { get; set; }

        /// <summary>Axis label formatter for times (default: SI seconds, e.g. "1.5 ms").</summary>
        public Func<double, string>? FormatTime { get; set; }

        public string? Title { get; set; }
        public IReadOnlyList<TimingMarker> Markers { get; set; } = Array.Empty<TimingMarker>();
    }

    /// <summary>
    /// A logic-analyser-style timing diagram in SVG: digital lanes drawn as square waves, buses as hexagon-ended segments
    /// with their value written inside (dropped when the segment is too narrow to hold it), unknown states hatched, a
    /// time axis with engineering-unit labels, and optional labelled cursors — the picture for documenting a protocol,
    /// a reset sequence or an instrument's trigger timing. Pure output; build lanes with <see cref="TimingSignal"/>.
    /// </summary>
    public static class TimingDiagram
    {
        public static string RenderSvg(IReadOnlyList<TimingSignal> signals, TimingDiagramOptions? options = null)
        {
            if (signals is null) throw new ArgumentNullException(nameof(signals));
            if (signals.Count == 0) throw new ArgumentException("Need at least one signal.", nameof(signals));
            for (int i = 0; i < signals.Count; i++)
                if (signals[i] is null) throw new ArgumentException($"Signal {i} is null.", nameof(signals));
            var o = options ?? new TimingDiagramOptions();
            // A non-finite window failed inside AxisTicks as "min", and End < Start was silently replaced by [Start, Start + 1].
            if (o.Start is double s0 && (double.IsNaN(s0) || double.IsInfinity(s0)) || o.End is double e0 && (double.IsNaN(e0) || double.IsInfinity(e0)))
                throw new ArgumentOutOfRangeException(nameof(options), "Start and End must be finite.");
            if (o.Start is double sx && o.End is double ex && !(ex > sx))
                throw new ArgumentException("End must be after Start.", nameof(options));
            var times = signals.SelectMany(s => s.Changes.Select(c => c.Time)).ToList();
            double t0 = o.Start ?? (times.Count > 0 ? times.Min() : 0), t1 = o.End ?? (times.Count > 0 ? times.Max() : 1);
            // One unit was below the spacing of doubles past 2^53 (epoch nanoseconds), so t1 == t0 and every x was 0/0.
            // An End at or before every change was replaced by t0 + 1, silently: the window opens before End instead.
            if (!(t1 > t0) && o.End.HasValue && !o.Start.HasValue) t0 = Math.Max(-double.MaxValue, t1 - Math.Max(1, Math.Abs(t1) * 1e-9));
            if (!(t1 > t0)) t1 = t0 + Math.Max(1, Math.Abs(t0) * 1e-9);
            if (double.IsInfinity(t1)) { t1 = double.MaxValue; t0 = Math.Min(t0, t1 - Math.Max(1, Math.Abs(t1) * 1e-9)); }
            Func<double, string> fmt = o.FormatTime ?? FormatSeconds;

            // Checked here so a bad value names the option (it failed later inside SvgUtils, naming "value"), and capped:
            // the hatch loop steps 6 px at a time, so Width = 1e9 ran for minutes and 1e17 never ended (x + 6 == x).
            if (!(o.Width > 0) || o.Width > 1e6) throw new ArgumentOutOfRangeException(nameof(options), "Width must be in (0, 1e6].");
            // Capped like Width (the hatch steps across LaneHeight too: 1e17 never ended), and above the 2 × 3 px insets,
            // which made a negative rect height and drew 1 below 0.
            if (!(o.LaneHeight > 6) || o.LaneHeight > 1e6) throw new ArgumentOutOfRangeException(nameof(options), "LaneHeight must be in (6, 1e6].");
            // Capped like LaneHeight: the lane sum overflowed (LaneGap = 1e308 with two signals).
            if (!(o.LaneGap >= 0) || o.LaneGap > 1e6) throw new ArgumentOutOfRangeException(nameof(options), "LaneGap must be in [0, 1e6].");
            if (o.Markers is null) // was a NullReferenceException
                throw new ArgumentException("Markers must not be null.", nameof(options));
            // default(TimingMarker) has no label: it failed inside the text escaper, naming its "text".
            foreach (var m in o.Markers)
                if (m.Label is null) throw new ArgumentException("A marker has no label (default(TimingMarker)).", nameof(options));
            double nameWidth = Math.Max(60, signals.Max(s => SvgUtils.EstimateTextWidth(s.Name, 12)) + 18);
            double left = nameWidth, right = 16, top = o.Title != null ? 40 : 16;
            double plotW = o.Width - left - right;
            if (plotW < 50) throw new ArgumentException("Width leaves no room for the diagram.", nameof(options));
            double lanesH = signals.Count * o.LaneHeight + (signals.Count - 1) * o.LaneGap;
            double axisY = top + lanesH + 8, height = axisY + 34;
            // Halved only when t1 − t0 overflows (finite times near ±double.MaxValue): halving always rounded a subnormal span
            // ([0, ε]) to 0, and every x was 0/0.
            bool halve = double.IsInfinity(t1 - t0);
            double X(double t) => halve
                ? left + (0.5 * Math.Max(t0, Math.Min(t1, t)) - 0.5 * t0) / (0.5 * t1 - 0.5 * t0) * plotW
                : left + (Math.Max(t0, Math.Min(t1, t)) - t0) / (t1 - t0) * plotW;

            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(SvgUtils.Number(o.Width)).Append("\" height=\"").Append(SvgUtils.Number(height))
              .Append("\" viewBox=\"0 0 ").Append(SvgUtils.Number(o.Width)).Append(' ').Append(SvgUtils.Number(height))
              .Append("\" font-family=\"Helvetica, Arial, sans-serif\" font-size=\"11\">\n<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>\n");
            // A hatch for unknown states, drawn with explicit lines (no <pattern> ids, so many diagrams can share a page).
            if (o.Title != null)
                sb.Append("<text x=\"").Append(SvgUtils.Number(left)).Append("\" y=\"22\" font-size=\"14\" font-weight=\"bold\"")
                  .Append(SvgUtils.FitWidth(o.Title, 14, o.Width - left - 4, bold: true)).Append('>').Append(SvgUtils.EscapeText(o.Title)).Append("</text>\n"); // a long title ran off the right

            // Capped at AxisTicks' 10,000: a Width past about 900,000 failed naming "maxTicks".
            TickSet ticks = AxisTicks.Linear(t0, t1, maxTicks: Math.Min(10000, Math.Max(2, (int)(plotW / 90))), loose: false);
            var tickLabels = ticks.Values.Select(v => fmt(v) ?? "").ToList(); // a null label failed as "text"
            if (o.FormatTime is null)
            {
                // Far from zero (e.g. epoch timestamps) neighbouring ticks rounded to the same label; then label the
                // first tick in full and the rest as offsets from it.
                bool clash = false;
                for (int i = 1; i < tickLabels.Count; i++) clash |= tickLabels[i] == tickLabels[i - 1];
                if (clash)
                {
                    for (int i = 1; i < tickLabels.Count; i++)
                        tickLabels[i] = "+" + FormatSeconds(ticks.Values[i] - ticks.Values[0]);
                    // The offsets round to three decimals too: a window two ulps wide read "+0.002 ps" twice beside
                    // "+4.441e-16 s". Then they are the exact differences of the ticks' decimals (their labels), in seconds:
                    // ticks an ulp or two apart are not whole steps apart as doubles.
                    bool still = false;
                    for (int i = 2; i < tickLabels.Count; i++) still |= tickLabels[i] == tickLabels[i - 1];
                    // Exact, not decimal: its 28 places rounded labels near 1e-17 that differ near 1e-31 together, and every
                    // offset read "+0 s".
                    if (still)
                    {
                        bool parsed = TryExact(ticks.Labels[0], out BigInteger mFirst, out int eFirst);
                        for (int i = 1; i < tickLabels.Count; i++)
                        {
                            double d = parsed && TryExact(ticks.Labels[i], out BigInteger mTick, out int eTick)
                                ? ExactDifference(mTick, eTick, mFirst, eFirst)
                                : Math.Round((ticks.Values[i] - ticks.Values[0]) / ticks.Step) * ticks.Step;
                            tickLabels[i] = "+" + d.ToString("G15", CultureInfo.InvariantCulture) + " s";
                        }
                    }
                }
            }
            sb.Append("<g stroke=\"#eeeeee\">\n");
            foreach (double v in ticks.Values)
                sb.Append("<line x1=\"").Append(SvgUtils.Number(X(v))).Append("\" y1=\"").Append(SvgUtils.Number(top - 4)).Append("\" x2=\"").Append(SvgUtils.Number(X(v)))
                  .Append("\" y2=\"").Append(SvgUtils.Number(axisY)).Append("\"/>\n");
            sb.Append("</g>\n<line x1=\"").Append(SvgUtils.Number(left)).Append("\" y1=\"").Append(SvgUtils.Number(axisY)).Append("\" x2=\"").Append(SvgUtils.Number(left + plotW))
              .Append("\" y2=\"").Append(SvgUtils.Number(axisY)).Append("\" stroke=\"#444\"/>\n");
            for (int ti = 0; ti < ticks.Count; ti++)
            {
                double v = ticks.Values[ti];
                // A label that would run past the canvas ends (or starts) at its tick instead of centring on it: the right
                // margin is 16 px, so the last label at t1 was clipped once wider than about 32 px.
                double lw = SvgUtils.EstimateTextWidth(tickLabels[ti], 11);
                string tickAnchor = X(v) + lw / 2 > o.Width - 2 ? "end" : X(v) - lw / 2 < 2 ? "start" : "middle";
                sb.Append("<line x1=\"").Append(SvgUtils.Number(X(v))).Append("\" y1=\"").Append(SvgUtils.Number(axisY)).Append("\" x2=\"").Append(SvgUtils.Number(X(v)))
                  .Append("\" y2=\"").Append(SvgUtils.Number(axisY + 4)).Append("\" stroke=\"#444\"/>\n<text x=\"").Append(SvgUtils.Number(X(v))).Append("\" y=\"")
                  .Append(SvgUtils.Number(axisY + 16)).Append("\" text-anchor=\"").Append(tickAnchor).Append("\">").Append(SvgUtils.EscapeText(tickLabels[ti])).Append("</text>\n");
            }

            for (int s = 0; s < signals.Count; s++)
            {
                var sig = signals[s];
                double y0 = top + s * (o.LaneHeight + o.LaneGap), yHi = y0 + 3, yLo = y0 + o.LaneHeight - 3, yMid = (yHi + yLo) / 2;
                sb.Append("<text x=\"").Append(SvgUtils.Number(left - 10)).Append("\" y=\"").Append(SvgUtils.Number(yMid + 4))
                  .Append("\" text-anchor=\"end\" font-size=\"12\">").Append(SvgUtils.EscapeText(sig.Name)).Append("</text>\n");
                var segs = Segments(sig, t0, t1);
                if (sig.IsBus) AppendBus(sb, segs, X, yHi, yLo, yMid);
                else AppendDigital(sb, segs, X, yHi, yLo);
            }

            foreach (var m in o.Markers)
            {
                if (m.Time < t0 || m.Time > t1) continue;
                double x = X(m.Time);
                sb.Append("<line x1=\"").Append(SvgUtils.Number(x)).Append("\" y1=\"").Append(SvgUtils.Number(top - 6)).Append("\" x2=\"").Append(SvgUtils.Number(x))
                  .Append("\" y2=\"").Append(SvgUtils.Number(axisY)).Append("\" stroke=\"").Append(SvgUtils.Color(m.Color)).Append("\" stroke-dasharray=\"4 3\"/>\n")
                  .Append("<text x=\"");
                // A label near the right edge ran past the canvas: it ends at the cursor instead when that fits.
                double lw = SvgUtils.EstimateTextWidth(m.Label, 11);
                bool flip = x + 3 + lw > o.Width - 2 && x - 3 - lw >= 2;
                sb.Append(SvgUtils.Number(flip ? x - 3 : x + 3)).Append("\" y=\"").Append(SvgUtils.Number(top - 8)).Append("\" fill=\"")
                  .Append(SvgUtils.Color(m.Color)).Append(flip ? "\" text-anchor=\"end\">" : "\">").Append(SvgUtils.EscapeText(m.Label)).Append("</text>\n");
            }
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        /// <summary>The constant-value segments of a signal inside [t0, t1] (value before the first change is unknown).</summary>
        public static IReadOnlyList<(double Start, double End, string? Value)> Segments(TimingSignal signal, double t0, double t1)
        {
            if (signal is null) throw new ArgumentNullException(nameof(signal));
            // NaN compared false everywhere and returned a NaN-ended segment; a reversed window returned nothing.
            if (double.IsNaN(t0)) throw new ArgumentOutOfRangeException(nameof(t0), t0, "The window start must not be NaN.");
            if (double.IsNaN(t1)) throw new ArgumentOutOfRangeException(nameof(t1), t1, "The window end must not be NaN.");
            if (t1 < t0) throw new ArgumentException("The window end is before its start.", nameof(t1));
            var result = new List<(double, double, string?)>();
            string? current = null;
            double from = t0;
            foreach (var (time, value) in signal.Changes)
            {
                if (time <= t0) { current = value; continue; }
                if (time >= t1) break;
                if (value != current)
                {
                    if (time > from) result.Add((from, time, current));
                    from = time;
                    current = value;
                }
            }
            if (t1 > from) result.Add((from, t1, current));
            return result;
        }

        private static void AppendDigital(StringBuilder sb, IReadOnlyList<(double Start, double End, string? Value)> segs, Func<double, double> X, double yHi, double yLo)
        {
            var path = new PathBuilder();
            bool started = false;
            foreach (var (a, b, v) in segs)
            {
                double xa = X(a), xb = X(b);
                if (v == null)
                {
                    AppendUnknown(sb, xa, xb, yHi, yLo);
                    started = false;
                    continue;
                }
                double y = v == "1" ? yHi : yLo;
                if (!started) { path.MoveTo(xa, y); started = true; }
                else path.LineTo(xa, y);                                        // the vertical edge
                path.LineTo(xb, y);
            }
            if (!path.IsEmpty) sb.Append("<path d=\"").Append(path).Append("\" fill=\"none\" stroke=\"#0072b2\" stroke-width=\"1.6\"/>\n");
        }

        private static void AppendBus(StringBuilder sb, IReadOnlyList<(double Start, double End, string? Value)> segs, Func<double, double> X, double yHi, double yLo, double yMid)
        {
            foreach (var (a, b, v) in segs)
            {
                double xa = X(a), xb = X(b);
                if (v == null) { AppendUnknown(sb, xa, xb, yHi, yLo); continue; }
                double slant = Math.Min(4, (xb - xa) / 2);
                var p = new PathBuilder().MoveTo(xa, yMid).LineTo(xa + slant, yHi).LineTo(xb - slant, yHi).LineTo(xb, yMid)
                                         .LineTo(xb - slant, yLo).LineTo(xa + slant, yLo).Close();
                sb.Append("<path d=\"").Append(p).Append("\" fill=\"#eaf2f8\" stroke=\"#0072b2\" stroke-width=\"1.2\"/>\n");
                double room = xb - xa - 2 * slant - 4;
                if (SvgUtils.EstimateTextWidth(v, 10) <= room)
                    sb.Append("<text x=\"").Append(SvgUtils.Number((xa + xb) / 2)).Append("\" y=\"").Append(SvgUtils.Number(yMid + 3.5))
                      .Append("\" text-anchor=\"middle\" font-size=\"10\">").Append(SvgUtils.EscapeText(v)).Append("</text>\n");
            }
        }

        private static void AppendUnknown(StringBuilder sb, double xa, double xb, double yHi, double yLo)
        {
            sb.Append("<rect x=\"").Append(SvgUtils.Number(xa)).Append("\" y=\"").Append(SvgUtils.Number(yHi)).Append("\" width=\"").Append(SvgUtils.Number(Math.Max(0, xb - xa)))
              .Append("\" height=\"").Append(SvgUtils.Number(yLo - yHi)).Append("\" fill=\"#f3f3f3\" stroke=\"#bbbbbb\" stroke-width=\"0.8\"/>\n");
            var hatch = new PathBuilder();
            double h = yLo - yHi;
            // Every segment drew (width + LaneHeight) / 6 lines, so a tall lane of many narrow unknown slivers wrote
            // gigabytes: past the segment's own width / 6 plus 200, the lines spread out. The 200 scales down below 6 px
            // wide: each of many sub-pixel slivers still drew 200 lines, so the output grew with the number of changes.
            double w = xb - xa;
            double step = Math.Max(6, (w + h) / (w / 6 + 200 * Math.Min(1, w / 6)));
            for (double x = xa - h; x < xb; x += step)
            {
                double x1 = Math.Max(xa, x), y1 = yLo - (x1 - x);
                double x2 = Math.Min(xb, x + h), y2 = yLo - (x2 - x);
                if (x2 > x1) hatch.MoveTo(x1, y1).LineTo(x2, y2);
            }
            if (!hatch.IsEmpty) sb.Append("<path d=\"").Append(hatch).Append("\" stroke=\"#c8c8c8\" stroke-width=\"0.8\" fill=\"none\"/>\n");
        }

        /// <summary>Seconds with an SI prefix: 0.0015 → "1.5 ms", 2e-6 → "2 µs", 0 → "0"; below 1 ps or from 1e15 s (as rounded) in exponent form ("5e-16 s", "1e+300 s").</summary>
        public static string FormatSeconds(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) // gave "NaN ps" and "Infinity s"
                throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Seconds must be finite.");
            if (seconds == 0) return "0";
            double a = Math.Abs(seconds);
            // 1e300 s printed all 301 digits. Below 1 ps the picoseconds kept three decimals but few digits (5e-16 read
            // "0.001 ps", twice its value), and a value just under 1e15 printed as "1000000000000000 s": both ends are
            // judged as rounded.
            if (a >= 1e15 || Rounded(a) >= 1e15 || Rounded(a * 1e12) < 1)
                return seconds.ToString("0.###e+0", CultureInfo.InvariantCulture) + " s";
            (double scale, string unit) = a >= 1 ? (1, "s") : a >= 1e-3 ? (1e3, "ms") : a >= 1e-6 ? (1e6, "µs") : a >= 1e-9 ? (1e9, "ns") : (1e12, "ps");
            // Choose the unit after rounding: 0.9999999 s came out as "1000 ms". By the label's own rounding: Math.Round's
            // ties-to-even disagreed with the format string at 999.9995, printing "1000 ps" for 9.999995e-10.
            if (scale > 1 && Rounded(a * scale) >= 1000)
            {
                scale /= 1000;
                unit = unit == "ms" ? "s" : unit == "µs" ? "ms" : unit == "ns" ? "µs" : "ns";
            }
            return (seconds * scale).ToString("0.###", CultureInfo.InvariantCulture) + " " + unit;
        }

        // A tick label ("-1.5E+7", "0.25", "6.40873316821915E-17") as exactly m × 10^e.
        private static bool TryExact(string s, out BigInteger m, out int e)
        {
            m = BigInteger.Zero;
            e = 0;
            int i = 0;
            bool negative = i < s.Length && s[i] == '-';
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            var digits = new StringBuilder();
            bool point = false;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') { digits.Append(c); if (point) e--; }
                else if (c == '.' && !point) point = true;
                else break;
            }
            if (digits.Length == 0) return false;
            if (i < s.Length)
            {
                if (s[i] != 'E' && s[i] != 'e') return false;
                if (!int.TryParse(s.Substring(i + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int x) || Math.Abs(x) > 100000) return false;
                e += x;
            }
            m = BigInteger.Parse(digits.ToString(), CultureInfo.InvariantCulture);
            if (negative) m = -m;
            return true;
        }

        // (a × 10^ea) − (b × 10^eb) as a double.
        private static double ExactDifference(BigInteger a, int ea, BigInteger b, int eb)
        {
            int e = Math.Min(ea, eb);
            BigInteger d = a * BigInteger.Pow(10, ea - e) - b * BigInteger.Pow(10, eb - e);
            return double.Parse(d.ToString(CultureInfo.InvariantCulture) + "E" + e.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // A magnitude as the "0.###" label shows it.
        private static double Rounded(double x) =>
            double.Parse(x.ToString("0.###", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
