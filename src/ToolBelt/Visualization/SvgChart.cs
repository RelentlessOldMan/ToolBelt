// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs, LinePlot.cs (Series), PlotFrame.cs, AxisTicks.cs,
// SvgUtils.cs and Annotations.cs.
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Visualization
{
    /// <summary>How an axis is scaled and labelled.</summary>
    public enum AxisKind
    {
        /// <summary>Linear, with 1-2-5 ticks.</summary>
        Linear,
        /// <summary>Base-10 logarithmic, with decade ticks (non-positive values are skipped).</summary>
        Log,
        /// <summary>Linear in seconds, with clock-friendly ticks labelled m:ss / h:mm:ss.</summary>
        Time,
    }

    /// <summary>Options for <see cref="SvgChart"/>.</summary>
    public sealed class SvgChartOptions
    {
        public double Width { get; set; } = 640;
        public double Height { get; set; } = 400;
        public string? Title { get; set; }
        public string? XLabel { get; set; }
        public string? YLabel { get; set; }
        public AxisKind XAxis { get; set; } = AxisKind.Linear;
        public AxisKind YAxis { get; set; } = AxisKind.Linear;

        /// <summary>Explicit axis limits; any left null are taken from the data (rounded out to nice ticks).</summary>
        public double? XMin { get; set; }
        public double? XMax { get; set; }
        public double? YMin { get; set; }
        public double? YMax { get; set; }

        public int MaxXTicks { get; set; } = 8;
        public int MaxYTicks { get; set; } = 6;
        public bool Grid { get; set; } = true;

        /// <summary>Draw a legend (only when at least one series has a name).</summary>
        public bool Legend { get; set; } = true;

        /// <summary>Show tick labels — multi-panel figures hide them on panels that share an axis.</summary>
        public bool ShowXTickLabels { get; set; } = true;
        public bool ShowYTickLabels { get; set; } = true;

        public double FontSize { get; set; } = 12;
        public string FontFamily { get; set; } = "sans-serif";
        public Rgba Background { get; set; } = Rgba.White;
        public Rgba AxisColor { get; set; } = new Rgba(64, 64, 64);
        public Rgba GridColor { get; set; } = new Rgba(226, 226, 226);

        /// <summary>Overlays drawn in data coordinates; their values are included when auto-ranging.</summary>
        public Annotations? Annotations { get; set; }

        /// <summary>
        /// Minimum margins around the plot area. Leave at 0 for automatic margins; multi-panel figures raise them
        /// so neighbouring panels' plot areas line up exactly.
        /// </summary>
        public double MinMarginLeft { get; set; }
        public double MinMarginTop { get; set; }
        public double MinMarginRight { get; set; }
        public double MinMarginBottom { get; set; }

        /// <summary>A shallow copy (the <see cref="Annotations"/> instance is shared).</summary>
        public SvgChartOptions Clone() => (SvgChartOptions)MemberwiseClone();
    }

    /// <summary>
    /// A self-contained SVG XY chart — line, scatter and bar series with titled, ticked axes (linear, log or
    /// time), gridlines, a legend and an <see cref="Visualization.Annotations"/> overlay. The vector, labelled
    /// counterpart of the raster <see cref="LinePlot"/>, meant for reports: <see cref="Render"/> gives a complete
    /// standalone document, <see cref="RenderElement"/> a bare <c>&lt;svg&gt;</c> element to inline into HTML or
    /// nest in a multi-panel figure, and <see cref="Layout"/> the exact <see cref="PlotFrame"/> used.
    /// <para>
    /// Auto-ranged axes round out to nice tick values and include annotation values (so limit lines stay
    /// visible) and, for bar series, zero. Non-finite points break a line rather than failing. Drawing is
    /// clipped geometrically to the plot area, so explicit limits narrower than the data are safe and no SVG ids
    /// are emitted — any number of charts can share one page. Output is deterministic for identical input.
    /// </para>
    /// </summary>
    public static class SvgChart
    {
        /// <summary>A complete standalone SVG document (with XML declaration).</summary>
        public static string Render(IReadOnlyList<Series> series, SvgChartOptions? options = null)
            => "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + RenderElement(series, options);

        /// <summary>
        /// A bare <c>&lt;svg&gt;</c> element (no XML declaration), optionally positioned at (<paramref name="x"/>,
        /// <paramref name="y"/>) — for inline HTML or nesting inside another SVG.
        /// </summary>
        public static string RenderElement(IReadOnlyList<Series> series, SvgChartOptions? options = null, double x = 0, double y = 0)
        {
            options ??= new SvgChartOptions();
            var layout = ComputeLayout(series, options);
            PlotFrame f = layout.Frame;
            double fs = options.FontSize;
            var sb = new StringBuilder();

            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\"");
            if (x != 0 || y != 0) sb.Append(" x=\"").Append(SvgUtils.Number(x)).Append("\" y=\"").Append(SvgUtils.Number(y)).Append('"');
            sb.Append(" width=\"").Append(SvgUtils.Number(options.Width)).Append("\" height=\"").Append(SvgUtils.Number(options.Height))
              .Append("\" viewBox=\"0 0 ").Append(SvgUtils.Number(options.Width)).Append(' ').Append(SvgUtils.Number(options.Height))
              .Append("\" font-family=\"").Append(SvgUtils.EscapeAttribute(options.FontFamily))
              .Append("\" font-size=\"").Append(SvgUtils.Number(fs)).Append("\">\n");
            sb.Append("<rect width=\"").Append(SvgUtils.Number(options.Width)).Append("\" height=\"").Append(SvgUtils.Number(options.Height))
              .Append("\" ").Append(SvgUtils.FillAttributes(options.Background)).Append("/>\n");

            if (options.Grid) Grid(sb, layout, options);
            if (options.Annotations != null)
                sb.Append(options.Annotations.ToSvg(f, fs * 0.9, options.FontFamily, AnnotationLayer.Background));

            for (int i = 0; i < series.Count; i++)
                DrawSeries(sb, series[i], f, layout.BarWidth);

            if (options.Annotations != null)
                sb.Append(options.Annotations.ToSvg(f, fs * 0.9, options.FontFamily, AnnotationLayer.Foreground));

            Axes(sb, layout, options);
            if (options.Legend) Legend(sb, series, f, options);
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        /// <summary>The plot area and data ranges <see cref="Render"/> would use for these arguments.</summary>
        public static PlotFrame Layout(IReadOnlyList<Series> series, SvgChartOptions? options = null)
            => ComputeLayout(series, options ?? new SvgChartOptions()).Frame;

        // ---------- layout ----------

        private sealed class ChartLayout
        {
            public PlotFrame Frame;
            public TickSet XTicks = null!;
            public TickSet YTicks = null!;
            public double BarWidth;
        }

        private static ChartLayout ComputeLayout(IReadOnlyList<Series> series, SvgChartOptions o)
        {
            if (series is null) throw new ArgumentNullException(nameof(series));
            for (int i = 0; i < series.Count; i++)
                if (series[i] is null) throw new ArgumentException($"Series {i} is null.", nameof(series));
            if (!(o.Width > 0) || !(o.Height > 0)) throw new ArgumentOutOfRangeException(nameof(o), "Width and height must be positive.");
            if (!(o.FontSize > 0)) throw new ArgumentOutOfRangeException(nameof(o), "FontSize must be positive.");

            // Data extents (finite values only; non-positive values cannot appear on a log axis).
            double? dxMin = null, dxMax = null, dyMin = null, dyMax = null;
            bool anyBar = false;
            foreach (Series s in series)
            {
                anyBar |= s.Style == PlotStyle.Bar;
                for (int i = 0; i < s.Y.Count; i++)
                {
                    double xv = s.X?[i] ?? i, yv = s.Y[i];
                    if (!Usable(xv, o.XAxis) || !Usable(yv, o.YAxis)) continue;
                    Extend(ref dxMin, ref dxMax, xv);
                    Extend(ref dyMin, ref dyMax, yv);
                }
            }
            if (o.Annotations != null)
            {
                var e = o.Annotations.Extents();
                if (e.XMin is double ax0 && Usable(ax0, o.XAxis)) Extend(ref dxMin, ref dxMax, ax0);
                if (e.XMax is double ax1 && Usable(ax1, o.XAxis)) Extend(ref dxMin, ref dxMax, ax1);
                if (e.YMin is double ay0 && Usable(ay0, o.YAxis)) Extend(ref dyMin, ref dyMax, ay0);
                if (e.YMax is double ay1 && Usable(ay1, o.YAxis)) Extend(ref dyMin, ref dyMax, ay1);
            }
            if (anyBar && o.YAxis != AxisKind.Log) Extend(ref dyMin, ref dyMax, 0);

            var (xTicks, xMin, xMax) = AxisRange(dxMin, dxMax, o.XMin, o.XMax, o.XAxis, o.MaxXTicks);
            var (yTicks, yMin, yMax) = AxisRange(dyMin, dyMax, o.YMin, o.YMax, o.YAxis, o.MaxYTicks);

            double fs = o.FontSize;
            double maxYLabel = 0;
            if (o.ShowYTickLabels)
                foreach (string l in yTicks.Labels) maxYLabel = Math.Max(maxYLabel, SvgUtils.EstimateTextWidth(l, fs));
            double lastXLabel = o.ShowXTickLabels && xTicks.Count > 0 ? SvgUtils.EstimateTextWidth(xTicks.Labels[xTicks.Count - 1], fs) : 0;

            double top = 12 + (string.IsNullOrEmpty(o.Title) ? 0 : fs * 1.35 + 8);
            double left = 10 + (string.IsNullOrEmpty(o.YLabel) ? 0 : fs + 8) + (o.ShowYTickLabels ? maxYLabel + 9 : 0);
            double bottom = 10 + (o.ShowXTickLabels ? fs + 8 : 0) + (string.IsNullOrEmpty(o.XLabel) ? 0 : fs + 8);
            double right = Math.Max(14, lastXLabel / 2 + 6);
            left = Math.Max(left, o.MinMarginLeft);
            top = Math.Max(top, o.MinMarginTop);
            right = Math.Max(right, o.MinMarginRight);
            bottom = Math.Max(bottom, o.MinMarginBottom);
            double pw = o.Width - left - right, ph = o.Height - top - bottom;
            if (pw < 20 || ph < 20)
                throw new ArgumentException("The chart is too small for its labels; increase Width/Height or reduce FontSize.");

            var frame = new PlotFrame(left, top, pw, ph, xMin, xMax, yMin, yMax, o.XAxis == AxisKind.Log, o.YAxis == AxisKind.Log);
            return new ChartLayout { Frame = frame, XTicks = xTicks, YTicks = yTicks, BarWidth = BarWidth(series, frame) };
        }

        private static (TickSet Ticks, double Min, double Max) AxisRange(
            double? dataMin, double? dataMax, double? explicitMin, double? explicitMax, AxisKind kind, int maxTicks)
        {
            double lo = explicitMin ?? dataMin ?? (kind == AxisKind.Log ? 1 : 0);
            double hi = explicitMax ?? dataMax ?? (kind == AxisKind.Log ? 10 : 1);
            if (lo > hi) (lo, hi) = (hi, lo);
            if (kind == AxisKind.Log && (!(lo > 0) || !(hi > 0)))
                throw new ArgumentOutOfRangeException(nameof(explicitMin), "A log axis needs strictly positive limits.");
            bool tight = explicitMin.HasValue && explicitMax.HasValue;

            TickSet ticks = kind switch
            {
                AxisKind.Log => AxisTicks.Log10(lo, hi, loose: !tight),
                AxisKind.Time => AxisTicks.Time(lo, hi, maxTicks, loose: !tight),
                _ => AxisTicks.Linear(lo, hi, maxTicks, loose: !tight),
            };
            double min = explicitMin ?? ticks.Min, max = explicitMax ?? ticks.Max;
            if (min > max) (min, max) = (max, min);
            return (ticks, min, max);
        }

        private static double BarWidth(IReadOnlyList<Series> series, PlotFrame f)
        {
            double best = double.PositiveInfinity;
            foreach (Series s in series)
            {
                if (s.Style != PlotStyle.Bar) continue;
                var xs = new List<double>();
                for (int i = 0; i < s.Y.Count; i++)
                {
                    double px = f.MapX(s.X?[i] ?? i);
                    if (!double.IsNaN(px)) xs.Add(px);
                }
                xs.Sort();
                for (int i = 1; i < xs.Count; i++)
                    if (xs[i] - xs[i - 1] > 1e-9) best = Math.Min(best, xs[i] - xs[i - 1]);
            }
            if (double.IsInfinity(best)) best = f.Width / 10;
            return Math.Max(1, best * 0.8);
        }

        // ---------- drawing ----------

        private static void Grid(StringBuilder sb, ChartLayout l, SvgChartOptions o)
        {
            PlotFrame f = l.Frame;
            string stroke = SvgUtils.StrokeAttributes(o.GridColor, 1);
            foreach (double v in l.XTicks.Values)
            {
                if (!f.ContainsX(v)) continue;
                double px = f.MapX(v);
                sb.Append("<line x1=\"").Append(SvgUtils.Number(px)).Append("\" y1=\"").Append(SvgUtils.Number(f.Top))
                  .Append("\" x2=\"").Append(SvgUtils.Number(px)).Append("\" y2=\"").Append(SvgUtils.Number(f.Bottom)).Append("\" ").Append(stroke).Append("/>\n");
            }
            foreach (double v in l.YTicks.Values)
            {
                if (!f.ContainsY(v)) continue;
                double py = f.MapY(v);
                sb.Append("<line x1=\"").Append(SvgUtils.Number(f.Left)).Append("\" y1=\"").Append(SvgUtils.Number(py))
                  .Append("\" x2=\"").Append(SvgUtils.Number(f.Right)).Append("\" y2=\"").Append(SvgUtils.Number(py)).Append("\" ").Append(stroke).Append("/>\n");
            }
        }

        private static void DrawSeries(StringBuilder sb, Series s, PlotFrame f, double barWidth)
        {
            switch (s.Style)
            {
                case PlotStyle.Line:
                {
                    var path = new PathBuilder();
                    bool havePrev = false, penDown = false;
                    double prevX = 0, prevY = 0, penX = 0, penY = 0;
                    for (int i = 0; i < s.Y.Count; i++)
                    {
                        double px = f.MapX(s.X?[i] ?? i), py = f.MapY(s.Y[i]);
                        if (double.IsNaN(px) || double.IsNaN(py) || double.IsInfinity(px) || double.IsInfinity(py))
                        {
                            havePrev = false; penDown = false; // a gap breaks the line
                            continue;
                        }
                        if (havePrev)
                        {
                            double ax = prevX, ay = prevY, bx = px, by = py;
                            if (ClipSegment(ref ax, ref ay, ref bx, ref by, f))
                            {
                                if (!penDown || ax != penX || ay != penY) path.MoveTo(ax, ay);
                                path.LineTo(bx, by);
                                penDown = true; penX = bx; penY = by;
                            }
                            else penDown = false;
                        }
                        else if (s.Y.Count == 1 && Inside(px, py, f))
                        {
                            path.MoveTo(px, py).LineTo(px, py); // a lone point still shows (round caps)
                        }
                        prevX = px; prevY = py; havePrev = true;
                    }
                    if (!path.IsEmpty)
                        sb.Append("<path d=\"").Append(path).Append("\" fill=\"none\" ")
                          .Append(SvgUtils.StrokeAttributes(s.Color, 1.75)).Append(" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>\n");
                    break;
                }
                case PlotStyle.Scatter:
                    for (int i = 0; i < s.Y.Count; i++)
                    {
                        double px = f.MapX(s.X?[i] ?? i), py = f.MapY(s.Y[i]);
                        if (!Inside(px, py, f)) continue;
                        sb.Append("<circle cx=\"").Append(SvgUtils.Number(px)).Append("\" cy=\"").Append(SvgUtils.Number(py))
                          .Append("\" r=\"2.75\" ").Append(SvgUtils.FillAttributes(s.Color)).Append("/>\n");
                    }
                    break;
                case PlotStyle.Bar:
                {
                    double baseY = f.LogY ? f.Bottom : Clamp(f.MapY(0), f.Top, f.Bottom);
                    for (int i = 0; i < s.Y.Count; i++)
                    {
                        double cx = f.MapX(s.X?[i] ?? i), py = f.MapY(s.Y[i]);
                        if (double.IsNaN(cx) || double.IsNaN(py) || double.IsInfinity(py)) continue;
                        double x0 = Clamp(cx - barWidth / 2, f.Left, f.Right), x1 = Clamp(cx + barWidth / 2, f.Left, f.Right);
                        double y0 = Clamp(Math.Min(py, baseY), f.Top, f.Bottom), y1 = Clamp(Math.Max(py, baseY), f.Top, f.Bottom);
                        if (x1 - x0 <= 0) continue;
                        sb.Append("<rect x=\"").Append(SvgUtils.Number(x0)).Append("\" y=\"").Append(SvgUtils.Number(y0))
                          .Append("\" width=\"").Append(SvgUtils.Number(x1 - x0)).Append("\" height=\"").Append(SvgUtils.Number(y1 - y0))
                          .Append("\" ").Append(SvgUtils.FillAttributes(s.Color)).Append("/>\n");
                    }
                    break;
                }
            }
        }

        private static void Axes(StringBuilder sb, ChartLayout l, SvgChartOptions o)
        {
            PlotFrame f = l.Frame;
            double fs = o.FontSize;
            string axis = SvgUtils.StrokeAttributes(o.AxisColor, 1);
            string ink = SvgUtils.FillAttributes(o.AxisColor);
            sb.Append("<rect x=\"").Append(SvgUtils.Number(f.Left)).Append("\" y=\"").Append(SvgUtils.Number(f.Top))
              .Append("\" width=\"").Append(SvgUtils.Number(f.Width)).Append("\" height=\"").Append(SvgUtils.Number(f.Height))
              .Append("\" fill=\"none\" ").Append(axis).Append("/>\n");

            for (int i = 0; i < l.XTicks.Count; i++)
            {
                double v = l.XTicks.Values[i];
                if (!f.ContainsX(v)) continue;
                double px = f.MapX(v);
                sb.Append("<line x1=\"").Append(SvgUtils.Number(px)).Append("\" y1=\"").Append(SvgUtils.Number(f.Bottom))
                  .Append("\" x2=\"").Append(SvgUtils.Number(px)).Append("\" y2=\"").Append(SvgUtils.Number(f.Bottom + 5)).Append("\" ").Append(axis).Append("/>\n");
                if (o.ShowXTickLabels)
                    Text(sb, px, f.Bottom + 7 + fs * 0.85, l.XTicks.Labels[i], "middle", ink);
            }
            for (int i = 0; i < l.YTicks.Count; i++)
            {
                double v = l.YTicks.Values[i];
                if (!f.ContainsY(v)) continue;
                double py = f.MapY(v);
                sb.Append("<line x1=\"").Append(SvgUtils.Number(f.Left - 5)).Append("\" y1=\"").Append(SvgUtils.Number(py))
                  .Append("\" x2=\"").Append(SvgUtils.Number(f.Left)).Append("\" y2=\"").Append(SvgUtils.Number(py)).Append("\" ").Append(axis).Append("/>\n");
                if (o.ShowYTickLabels)
                    Text(sb, f.Left - 8, py + fs * 0.35, l.YTicks.Labels[i], "end", ink);
            }

            if (!string.IsNullOrEmpty(o.XLabel))
            {
                double ty = f.Bottom + (o.ShowXTickLabels ? fs + 8 : 4) + fs + 2;
                Text(sb, f.Left + f.Width / 2, ty, o.XLabel!, "middle", ink);
            }
            if (!string.IsNullOrEmpty(o.YLabel))
            {
                double cx = 10 + fs * 0.8, cy = f.Top + f.Height / 2;
                sb.Append("<text x=\"").Append(SvgUtils.Number(cx)).Append("\" y=\"").Append(SvgUtils.Number(cy))
                  .Append("\" text-anchor=\"middle\" transform=\"").Append(SvgUtils.Rotate(-90, cx, cy)).Append("\" ").Append(ink).Append('>')
                  .Append(SvgUtils.EscapeText(o.YLabel!)).Append("</text>\n");
            }
            if (!string.IsNullOrEmpty(o.Title))
            {
                sb.Append("<text x=\"").Append(SvgUtils.Number(o.Width / 2)).Append("\" y=\"").Append(SvgUtils.Number(10 + fs * 1.2))
                  .Append("\" text-anchor=\"middle\" font-weight=\"bold\" font-size=\"").Append(SvgUtils.Number(fs * 1.2)).Append("\" ")
                  .Append(SvgUtils.FillAttributes(Rgba.Black)).Append('>').Append(SvgUtils.EscapeText(o.Title!)).Append("</text>\n");
            }
        }

        private static void Legend(StringBuilder sb, IReadOnlyList<Series> series, PlotFrame f, SvgChartOptions o)
        {
            var named = new List<Series>();
            foreach (Series s in series) if (!string.IsNullOrEmpty(s.Name)) named.Add(s);
            if (named.Count == 0) return;

            double fs = o.FontSize * 0.9, row = fs + 6, swatch = 18;
            double textWidth = 0;
            foreach (Series s in named) textWidth = Math.Max(textWidth, SvgUtils.EstimateTextWidth(s.Name!, fs));
            double w = 8 + swatch + 6 + textWidth + 8, h = 6 + named.Count * row;
            double x0 = f.Right - w - 6, y0 = f.Top + 6;
            sb.Append("<g class=\"legend\" font-size=\"").Append(SvgUtils.Number(fs)).Append("\">\n");
            sb.Append("<rect x=\"").Append(SvgUtils.Number(x0)).Append("\" y=\"").Append(SvgUtils.Number(y0))
              .Append("\" width=\"").Append(SvgUtils.Number(w)).Append("\" height=\"").Append(SvgUtils.Number(h))
              .Append("\" fill=\"#ffffff\" fill-opacity=\"0.85\" ").Append(SvgUtils.StrokeAttributes(o.GridColor, 1)).Append("/>\n");
            for (int i = 0; i < named.Count; i++)
            {
                Series s = named[i];
                double cy = y0 + 3 + row * i + row / 2, sx = x0 + 8;
                switch (s.Style)
                {
                    case PlotStyle.Scatter:
                        sb.Append("<circle cx=\"").Append(SvgUtils.Number(sx + swatch / 2)).Append("\" cy=\"").Append(SvgUtils.Number(cy))
                          .Append("\" r=\"3\" ").Append(SvgUtils.FillAttributes(s.Color)).Append("/>\n");
                        break;
                    case PlotStyle.Bar:
                        sb.Append("<rect x=\"").Append(SvgUtils.Number(sx + 3)).Append("\" y=\"").Append(SvgUtils.Number(cy - 5))
                          .Append("\" width=\"").Append(SvgUtils.Number(swatch - 6)).Append("\" height=\"10\" ").Append(SvgUtils.FillAttributes(s.Color)).Append("/>\n");
                        break;
                    default:
                        sb.Append("<line x1=\"").Append(SvgUtils.Number(sx)).Append("\" y1=\"").Append(SvgUtils.Number(cy))
                          .Append("\" x2=\"").Append(SvgUtils.Number(sx + swatch)).Append("\" y2=\"").Append(SvgUtils.Number(cy))
                          .Append("\" ").Append(SvgUtils.StrokeAttributes(s.Color, 2)).Append("/>\n");
                        break;
                }
                Text(sb, sx + swatch + 6, cy + fs * 0.35, s.Name!, "start", SvgUtils.FillAttributes(Rgba.Black));
            }
            sb.Append("</g>\n");
        }

        private static void Text(StringBuilder sb, double x, double y, string text, string anchor, string fill)
            => sb.Append("<text x=\"").Append(SvgUtils.Number(x)).Append("\" y=\"").Append(SvgUtils.Number(y))
                 .Append("\" text-anchor=\"").Append(anchor).Append("\" ").Append(fill).Append('>')
                 .Append(SvgUtils.EscapeText(text)).Append("</text>\n");

        // ---------- geometry ----------

        /// <summary>Liang–Barsky: clips the segment to the plot rectangle; false when nothing remains.</summary>
        private static bool ClipSegment(ref double x0, ref double y0, ref double x1, ref double y1, PlotFrame f)
        {
            double t0 = 0, t1 = 1, dx = x1 - x0, dy = y1 - y0;
            if (!Edge(-dx, x0 - f.Left, ref t0, ref t1)) return false;
            if (!Edge(dx, f.Right - x0, ref t0, ref t1)) return false;
            if (!Edge(-dy, y0 - f.Top, ref t0, ref t1)) return false;
            if (!Edge(dy, f.Bottom - y0, ref t0, ref t1)) return false;
            double nx0 = x0 + t0 * dx, ny0 = y0 + t0 * dy, nx1 = x0 + t1 * dx, ny1 = y0 + t1 * dy;
            x0 = nx0; y0 = ny0; x1 = nx1; y1 = ny1;
            return true;
        }

        private static bool Edge(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0) return q >= 0;
            double r = q / p;
            if (p < 0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }

        private static bool Inside(double px, double py, PlotFrame f)
            => !double.IsNaN(px) && !double.IsNaN(py) && px >= f.Left - 1e-9 && px <= f.Right + 1e-9 && py >= f.Top - 1e-9 && py <= f.Bottom + 1e-9;

        private static bool Usable(double v, AxisKind kind)
            => !double.IsNaN(v) && !double.IsInfinity(v) && (kind != AxisKind.Log || v > 0);

        private static void Extend(ref double? min, ref double? max, double v)
        {
            min = min is null ? v : Math.Min(min.Value, v);
            max = max is null ? v : Math.Max(max.Value, v);
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
