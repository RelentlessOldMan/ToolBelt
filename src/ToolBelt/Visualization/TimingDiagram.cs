// ToolBelt drop-in — also copy Visualization/AxisTicks.cs and SvgUtils.cs (and ImageBuffer.cs for Rgba).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            if (!(period > 0)) throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be positive.");
            if (!(duty > 0 && duty < 1)) throw new ArgumentOutOfRangeException(nameof(duty), duty, "Duty must be in (0, 1).");
            if ((end - start) / period > 100_000) throw new ArgumentException("Too many clock cycles to draw.");
            var c = new List<(double, bool?)>();
            for (double t = start; t < end; t += period) { c.Add((t, true)); c.Add((t + period * duty, false)); }
            return Digital(name, c);
        }

        /// <summary>
        /// A digital line from sampled data: high above <paramref name="highThreshold"/>, low below
        /// <paramref name="lowThreshold"/> (hysteresis between), edges at sample times. Starts unknown until the first
        /// sample outside the band.
        /// </summary>
        public static TimingSignal FromSamples(string name, IReadOnlyList<double> samples, double sampleRate, double lowThreshold, double highThreshold, double startTime = 0)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (!(sampleRate > 0)) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (!(highThreshold >= lowThreshold)) throw new ArgumentException("High threshold must not be below the low threshold.");
            var c = new List<(double, bool?)>();
            bool? level = null;
            c.Add((startTime, null));
            for (int i = 0; i < samples.Count; i++)
            {
                bool? next = samples[i] > highThreshold ? true : samples[i] < lowThreshold ? false : level;
                if (next != level) { c.Add((startTime + i / sampleRate, next)); level = next; }
            }
            return Digital(name, c);
        }

        private static string Check(string name) => name ?? throw new ArgumentNullException(nameof(name));

        private static IReadOnlyList<(double, string?)> Sorted(IEnumerable<(double Time, string? Value)>? changes)
        {
            if (changes is null) throw new ArgumentNullException(nameof(changes));
            var list = changes.ToList();
            if (list.Any(c => double.IsNaN(c.Time) || double.IsInfinity(c.Time))) throw new ArgumentException("Change times must be finite.");
            // Stable sort; for equal times the last change wins.
            var ordered = list.Select((c, i) => (c, i)).OrderBy(t => t.c.Time).ThenBy(t => t.i).Select(t => t.c).ToList();
            var result = new List<(double, string?)>();
            foreach (var c in ordered)
            {
                if (result.Count > 0 && result[result.Count - 1].Item1 == c.Time) result[result.Count - 1] = (c.Time, c.Value);
                else if (result.Count == 0 || result[result.Count - 1].Item2 != c.Value) result.Add((c.Time, c.Value));
            }
            return result;
        }
    }

    /// <summary>A labelled vertical cursor on a timing diagram.</summary>
    public readonly struct TimingMarker
    {
        public TimingMarker(double time, string label, Rgba? color = null)
        {
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
            var o = options ?? new TimingDiagramOptions();
            var times = signals.SelectMany(s => s.Changes.Select(c => c.Time)).ToList();
            double t0 = o.Start ?? (times.Count > 0 ? times.Min() : 0), t1 = o.End ?? (times.Count > 0 ? times.Max() : 1);
            if (!(t1 > t0)) t1 = t0 + 1;
            Func<double, string> fmt = o.FormatTime ?? FormatSeconds;

            double nameWidth = Math.Max(60, signals.Max(s => SvgUtils.EstimateTextWidth(s.Name, 12)) + 18);
            double left = nameWidth, right = 16, top = o.Title != null ? 40 : 16;
            double plotW = o.Width - left - right;
            if (plotW < 50) throw new ArgumentException("Width leaves no room for the diagram.");
            double lanesH = signals.Count * o.LaneHeight + (signals.Count - 1) * o.LaneGap;
            double axisY = top + lanesH + 8, height = axisY + 34;
            double X(double t) => left + (Math.Max(t0, Math.Min(t1, t)) - t0) / (t1 - t0) * plotW;

            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(SvgUtils.Number(o.Width)).Append("\" height=\"").Append(SvgUtils.Number(height))
              .Append("\" viewBox=\"0 0 ").Append(SvgUtils.Number(o.Width)).Append(' ').Append(SvgUtils.Number(height))
              .Append("\" font-family=\"Helvetica, Arial, sans-serif\" font-size=\"11\">\n<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>\n");
            // A hatch for unknown states, drawn with explicit lines (no <pattern> ids, so many diagrams can share a page).
            if (o.Title != null)
                sb.Append("<text x=\"").Append(SvgUtils.Number(left)).Append("\" y=\"22\" font-size=\"14\" font-weight=\"bold\">").Append(SvgUtils.EscapeText(o.Title)).Append("</text>\n");

            TickSet ticks = AxisTicks.Linear(t0, t1, maxTicks: Math.Max(2, (int)(plotW / 90)), loose: false);
            sb.Append("<g stroke=\"#eeeeee\">\n");
            foreach (double v in ticks.Values)
                sb.Append("<line x1=\"").Append(SvgUtils.Number(X(v))).Append("\" y1=\"").Append(SvgUtils.Number(top - 4)).Append("\" x2=\"").Append(SvgUtils.Number(X(v)))
                  .Append("\" y2=\"").Append(SvgUtils.Number(axisY)).Append("\"/>\n");
            sb.Append("</g>\n<line x1=\"").Append(SvgUtils.Number(left)).Append("\" y1=\"").Append(SvgUtils.Number(axisY)).Append("\" x2=\"").Append(SvgUtils.Number(left + plotW))
              .Append("\" y2=\"").Append(SvgUtils.Number(axisY)).Append("\" stroke=\"#444\"/>\n");
            foreach (double v in ticks.Values)
                sb.Append("<line x1=\"").Append(SvgUtils.Number(X(v))).Append("\" y1=\"").Append(SvgUtils.Number(axisY)).Append("\" x2=\"").Append(SvgUtils.Number(X(v)))
                  .Append("\" y2=\"").Append(SvgUtils.Number(axisY + 4)).Append("\" stroke=\"#444\"/>\n<text x=\"").Append(SvgUtils.Number(X(v))).Append("\" y=\"")
                  .Append(SvgUtils.Number(axisY + 16)).Append("\" text-anchor=\"middle\">").Append(SvgUtils.EscapeText(fmt(v))).Append("</text>\n");

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
                  .Append("<text x=\"").Append(SvgUtils.Number(x + 3)).Append("\" y=\"").Append(SvgUtils.Number(top - 8)).Append("\" fill=\"")
                  .Append(SvgUtils.Color(m.Color)).Append("\">").Append(SvgUtils.EscapeText(m.Label)).Append("</text>\n");
            }
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        /// <summary>The constant-value segments of a signal inside [t0, t1] (value before the first change is unknown).</summary>
        public static IReadOnlyList<(double Start, double End, string? Value)> Segments(TimingSignal signal, double t0, double t1)
        {
            if (signal is null) throw new ArgumentNullException(nameof(signal));
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
            sb.Append("<path d=\"").Append(path).Append("\" fill=\"none\" stroke=\"#0072b2\" stroke-width=\"1.6\"/>\n");
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
            for (double x = xa - h; x < xb; x += 6)
            {
                double x1 = Math.Max(xa, x), y1 = yLo - (x1 - x);
                double x2 = Math.Min(xb, x + h), y2 = yLo - (x2 - x);
                if (x2 > x1) hatch.MoveTo(x1, y1).LineTo(x2, y2);
            }
            sb.Append("<path d=\"").Append(hatch).Append("\" stroke=\"#c8c8c8\" stroke-width=\"0.8\" fill=\"none\"/>\n");
        }

        /// <summary>Seconds with an SI prefix: 0.0015 → "1.5 ms", 2e-6 → "2 µs", 0 → "0".</summary>
        public static string FormatSeconds(double seconds)
        {
            if (seconds == 0) return "0";
            double a = Math.Abs(seconds);
            (double scale, string unit) = a >= 1 ? (1, "s") : a >= 1e-3 ? (1e3, "ms") : a >= 1e-6 ? (1e6, "µs") : a >= 1e-9 ? (1e9, "ns") : (1e12, "ps");
            return (seconds * scale).ToString("0.###", CultureInfo.InvariantCulture) + " " + unit;
        }
    }
}
