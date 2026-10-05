// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs, Colormap.cs, PlotFrame.cs, AxisTicks.cs and SvgUtils.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ToolBelt.Visualization
{
    /// <summary>One occupied hexagon.</summary>
    public readonly struct HexCell
    {
        public HexCell(double centerX, double centerY, int count, double dataX, double dataY)
        {
            CenterX = centerX;
            CenterY = centerY;
            Count = count;
            DataX = dataX;
            DataY = dataY;
        }

        /// <summary>Centre in pixels.</summary>
        public double CenterX { get; }
        public double CenterY { get; }

        public int Count { get; }

        /// <summary>Centre in data units.</summary>
        public double DataX { get; }
        public double DataY { get; }
    }

    /// <summary>Options for <see cref="HexBin.RenderSvg"/>.</summary>
    public sealed class HexBinOptions
    {
        public double Width { get; set; } = 640;
        public double Height { get; set; } = 480;

        /// <summary>Hexagon circumradius in pixels.</summary>
        public double Radius { get; set; } = 10;

        public Colormap Colormap { get; set; } = Colormap.Viridis;

        /// <summary>Colour by log(count) — keeps sparse areas visible next to a dense core.</summary>
        public bool LogScale { get; set; }

        public string? Title { get; set; }
        public string? XLabel { get; set; }
        public string? YLabel { get; set; }

        /// <summary>Data ranges (default: the data's extent).</summary>
        public double? XMin { get; set; }
        public double? XMax { get; set; }
        public double? YMin { get; set; }
        public double? YMax { get; set; }
    }

    /// <summary>
    /// Hexagonal binning — the scatter plot for when there are too many points to see: points are counted into a
    /// pointy-top hexagonal grid laid out in <em>pixel</em> space (so hexagons are regular whatever the data's aspect) and
    /// each occupied hexagon is coloured by its count. <see cref="Compute"/> is the pure binning (each point goes to the
    /// hexagon whose centre is nearest, exactly), <see cref="RenderSvg"/> draws it with axes and a count scale.
    /// </summary>
    public static class HexBin
    {
        private static readonly double Sqrt3 = Math.Sqrt(3);

        /// <summary>Bins points through <paramref name="frame"/> into hexagons of circumradius <paramref name="radius"/> pixels. Non-finite points are skipped.</summary>
        public static IReadOnlyList<HexCell> Compute(IEnumerable<(double X, double Y)> points, PlotFrame frame, double radius)
        {
            if (points is null) throw new ArgumentNullException(nameof(points));
            if (!(radius > 0)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be positive.");
            double dx = radius * Sqrt3, dy = radius * 1.5;
            var counts = new Dictionary<(int, int), int>();
            foreach (var (x, y) in points)
            {
                double px = frame.MapX(x) - frame.Left, py = frame.MapY(y) - frame.Top;
                if (double.IsNaN(px) || double.IsNaN(py) || double.IsInfinity(px) || double.IsInfinity(py)) continue;
                var key = Nearest(px, py, dx, dy);
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            return counts.Select(kv =>
            {
                var (cx, cy) = Center(kv.Key.Item1, kv.Key.Item2, dx, dy);
                double ax = cx + frame.Left, ay = cy + frame.Top;
                return new HexCell(ax, ay, kv.Value, frame.UnmapX(ax), frame.UnmapY(ay));
            }).OrderBy(c => c.CenterY).ThenBy(c => c.CenterX).ToArray();
        }

        /// <summary>Pixel centre of hexagon (column <paramref name="i"/>, row <paramref name="j"/>) relative to the plot origin.</summary>
        public static (double X, double Y) Center(int i, int j, double dx, double dy)
            => ((i + ((j & 1) != 0 ? 0.5 : 0)) * dx, j * dy);

        // The hexagon containing a point. Its centre is within one circumradius r of the point, i.e. within 2/3 of a row
        // spacing (1.5r) vertically, so it lies in row floor(fy) or floor(fy)+1: take the nearest column in each of those
        // two rows and keep the closer centre. Exact (ties go to the lower row).
        private static (int, int) Nearest(double px, double py, double dx, double dy)
        {
            int j0 = (int)Math.Floor(py / dy);
            (int, int) best = (0, 0);
            double bestD = double.PositiveInfinity;
            for (int j = j0; j <= j0 + 1; j++)
            {
                int i = (int)Math.Round(px / dx - ((j & 1) != 0 ? 0.5 : 0));
                var (cx, cy) = Center(i, j, dx, dy);
                double d = (px - cx) * (px - cx) + (py - cy) * (py - cy);
                if (d < bestD) { bestD = d; best = (i, j); }
            }
            return best;
        }

        /// <summary>A complete SVG: axes, hexagons coloured by count, and a count scale.</summary>
        public static string RenderSvg(IReadOnlyList<(double X, double Y)> points, HexBinOptions? options = null)
        {
            if (points is null) throw new ArgumentNullException(nameof(points));
            var o = options ?? new HexBinOptions();
            var finite = points.Where(p => !double.IsNaN(p.X) && !double.IsNaN(p.Y) && !double.IsInfinity(p.X) && !double.IsInfinity(p.Y)).ToArray();
            double xmin = o.XMin ?? (finite.Length > 0 ? finite.Min(p => p.X) : 0), xmax = o.XMax ?? (finite.Length > 0 ? finite.Max(p => p.X) : 1);
            double ymin = o.YMin ?? (finite.Length > 0 ? finite.Min(p => p.Y) : 0), ymax = o.YMax ?? (finite.Length > 0 ? finite.Max(p => p.Y) : 1);
            if (xmax <= xmin) { xmin -= 0.5; xmax += 0.5; }
            if (ymax <= ymin) { ymin -= 0.5; ymax += 0.5; }
            TickSet xt = AxisTicks.Linear(xmin, xmax), yt = AxisTicks.Linear(ymin, ymax);

            double left = 64, right = 90, top = o.Title != null ? 36 : 14, bottom = o.XLabel != null ? 50 : 34;
            if (o.YLabel != null) left += 18;
            double pw = o.Width - left - right, ph = o.Height - top - bottom;
            if (pw <= 20 || ph <= 20) throw new ArgumentException("Width/Height leave no room for the plot.");
            var frame = new PlotFrame(left, top, pw, ph, xt.Min, xt.Max, yt.Min, yt.Max);
            var cells = Compute(finite, frame, o.Radius);
            int maxCount = cells.Count == 0 ? 1 : cells.Max(c => c.Count);

            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(SvgUtils.Number(o.Width)).Append("\" height=\"").Append(SvgUtils.Number(o.Height))
              .Append("\" viewBox=\"0 0 ").Append(SvgUtils.Number(o.Width)).Append(' ').Append(SvgUtils.Number(o.Height))
              .Append("\" font-family=\"Helvetica, Arial, sans-serif\" font-size=\"11\">\n");
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>\n");
            AppendAxes(sb, frame, xt, yt);

            // Hexagons, clipped geometrically to the plot area (no clipPath ids, so many charts can share a page). Cells
            // straddling an edge are drawn partially — dropping them would hide data at the extremes of the range.
            sb.Append("<g stroke=\"none\">\n");
            foreach (var c in cells)
            {
                double t = o.LogScale ? Math.Log(c.Count) / Math.Max(Math.Log(maxCount), 1e-12) : (maxCount == 1 ? 1 : (c.Count - 1) / (double)(maxCount - 1));
                if (maxCount == 1) t = 1;
                var hex = new List<(double X, double Y)>(6);
                for (int k = 0; k < 6; k++)
                {
                    double a = Math.PI / 180 * (60 * k - 30);
                    hex.Add((c.CenterX + o.Radius * Math.Cos(a), c.CenterY + o.Radius * Math.Sin(a)));
                }
                var clipped = ClipToRect(hex, frame.Left, frame.Top, frame.Right, frame.Bottom);
                if (clipped.Count < 3) continue;
                sb.Append("<path d=\"").Append(PathBuilder.FromPoints(clipped, closed: true)).Append("\" ").Append(SvgUtils.FillAttributes(o.Colormap.Map(t)))
                  .Append("><title>").Append(c.Count.ToString(CultureInfo.InvariantCulture)).Append("</title></path>\n");
            }
            sb.Append("</g>\n");

            // Count scale.
            double sx = frame.Right + 18, sh = Math.Min(160, ph), sy = frame.Top;
            const int steps = 32;
            for (int k = 0; k < steps; k++)
            {
                double t = 1 - (k + 0.5) / steps;
                sb.Append("<rect x=\"").Append(SvgUtils.Number(sx)).Append("\" y=\"").Append(SvgUtils.Number(sy + sh * k / steps))
                  .Append("\" width=\"12\" height=\"").Append(SvgUtils.Number(sh / steps + 0.5)).Append("\" ").Append(SvgUtils.FillAttributes(o.Colormap.Map(t))).Append("/>\n");
            }
            sb.Append("<rect x=\"").Append(SvgUtils.Number(sx)).Append("\" y=\"").Append(SvgUtils.Number(sy)).Append("\" width=\"12\" height=\"")
              .Append(SvgUtils.Number(sh)).Append("\" fill=\"none\" stroke=\"#444\" stroke-width=\"0.5\"/>\n");
            sb.Append(Text(sx + 16, sy + 4, maxCount.ToString(CultureInfo.InvariantCulture), "start"));
            sb.Append(Text(sx + 16, sy + sh, "1", "start"));
            sb.Append(Text(sx, sy + sh + 14, o.LogScale ? "count (log)" : "count", "start"));

            if (o.Title != null) sb.Append("<text x=\"").Append(SvgUtils.Number(frame.Left + pw / 2)).Append("\" y=\"20\" text-anchor=\"middle\" font-size=\"14\" font-weight=\"bold\">")
                                   .Append(SvgUtils.EscapeText(o.Title)).Append("</text>\n");
            if (o.XLabel != null) sb.Append(Text(frame.Left + pw / 2, o.Height - 10, o.XLabel, "middle"));
            if (o.YLabel != null)
                sb.Append("<text x=\"16\" y=\"").Append(SvgUtils.Number(frame.Top + ph / 2)).Append("\" text-anchor=\"middle\" transform=\"")
                  .Append(SvgUtils.Rotate(-90, 16, frame.Top + ph / 2)).Append("\">").Append(SvgUtils.EscapeText(o.YLabel)).Append("</text>\n");
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        // Sutherland–Hodgman clip of a convex polygon to an axis-aligned rectangle.
        private static List<(double X, double Y)> ClipToRect(List<(double X, double Y)> poly, double left, double top, double right, double bottom)
        {
            var result = poly;
            for (int edge = 0; edge < 4 && result.Count > 0; edge++)
            {
                bool Inside((double X, double Y) p) => edge switch { 0 => p.X >= left, 1 => p.X <= right, 2 => p.Y >= top, _ => p.Y <= bottom };
                (double X, double Y) Cross((double X, double Y) a, (double X, double Y) b)
                {
                    double t = edge switch
                    {
                        0 => (left - a.X) / (b.X - a.X),
                        1 => (right - a.X) / (b.X - a.X),
                        2 => (top - a.Y) / (b.Y - a.Y),
                        _ => (bottom - a.Y) / (b.Y - a.Y),
                    };
                    return (a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
                }
                var input = result;
                result = new List<(double X, double Y)>(input.Count + 2);
                for (int i = 0; i < input.Count; i++)
                {
                    var cur = input[i];
                    var prev = input[(i + input.Count - 1) % input.Count];
                    bool curIn = Inside(cur), prevIn = Inside(prev);
                    if (curIn)
                    {
                        if (!prevIn) result.Add(Cross(prev, cur));
                        result.Add(cur);
                    }
                    else if (prevIn) result.Add(Cross(prev, cur));
                }
            }
            return result;
        }

        internal static void AppendAxes(StringBuilder sb, PlotFrame f, TickSet xt, TickSet yt)
        {
            sb.Append("<g stroke=\"#e6e6e6\" stroke-width=\"1\">\n");
            foreach (double v in xt.Values) sb.Append(Line(f.MapX(v), f.Top, f.MapX(v), f.Bottom));
            foreach (double v in yt.Values) sb.Append(Line(f.Left, f.MapY(v), f.Right, f.MapY(v)));
            sb.Append("</g>\n<rect x=\"").Append(SvgUtils.Number(f.Left)).Append("\" y=\"").Append(SvgUtils.Number(f.Top)).Append("\" width=\"")
              .Append(SvgUtils.Number(f.Width)).Append("\" height=\"").Append(SvgUtils.Number(f.Height)).Append("\" fill=\"none\" stroke=\"#444\"/>\n");
            for (int k = 0; k < xt.Count; k++) sb.Append(Text(f.MapX(xt.Values[k]), f.Bottom + 15, xt.Labels[k], "middle"));
            for (int k = 0; k < yt.Count; k++) sb.Append(Text(f.Left - 6, f.MapY(yt.Values[k]) + 4, yt.Labels[k], "end"));
        }

        private static string Line(double x1, double y1, double x2, double y2)
            => "<line x1=\"" + SvgUtils.Number(x1) + "\" y1=\"" + SvgUtils.Number(y1) + "\" x2=\"" + SvgUtils.Number(x2) + "\" y2=\"" + SvgUtils.Number(y2) + "\"/>\n";

        private static string Text(double x, double y, string text, string anchor)
            => "<text x=\"" + SvgUtils.Number(x) + "\" y=\"" + SvgUtils.Number(y) + "\" text-anchor=\"" + anchor + "\">" + SvgUtils.EscapeText(text) + "</text>\n";
    }
}
