// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs, PlotFrame.cs and SvgUtils.cs.
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Visualization
{
    /// <summary>Stroke pattern for annotation lines.</summary>
    public enum LineDash
    {
        Solid,
        Dashed,
        Dotted,
    }

    /// <summary>Which annotations <see cref="Annotations.ToSvg"/> emits — lets a host put bands behind its series and the rest on top.</summary>
    public enum AnnotationLayer
    {
        /// <summary>Everything, bands first.</summary>
        All,
        /// <summary>Only the shaded bands (draw before the series).</summary>
        Background,
        /// <summary>Reference lines, callouts and arrows (draw after the series).</summary>
        Foreground,
    }

    /// <summary>
    /// A layer of plot annotations in <b>data coordinates</b> — reference lines (specification limits, control
    /// limits, thresholds), shaded bands, point callouts and arrows — that turns a plot into a report figure.
    /// The layer is independent of any one chart: render it against the <see cref="PlotFrame"/> of whatever
    /// plot it decorates, as an SVG fragment (<see cref="ToSvg"/>, with labels) or onto a raster image
    /// (<see cref="Draw"/>; labels are omitted there because the raster canvas has no font engine).
    /// <para>
    /// Clipping is geometric rather than by <c>clipPath</c> (so several charts can share one SVG or HTML page
    /// without id collisions): a reference line outside the frame's range is not drawn, bands are clamped to the
    /// plot area, and callouts or arrows whose target lies outside the range are skipped. Hosts that auto-range
    /// should include <see cref="Extents"/> so that, say, a limit above every data point is still visible.
    /// </para>
    /// </summary>
    public sealed class Annotations
    {
        /// <summary>Default color for reference lines (a muted red).</summary>
        public static readonly Rgba DefaultLineColor = new Rgba(214, 39, 40);

        /// <summary>Default band fill (translucent blue).</summary>
        public static readonly Rgba DefaultBandColor = new Rgba(31, 119, 180, 48);

        /// <summary>Default callout and arrow color (dark gray).</summary>
        public static readonly Rgba DefaultCalloutColor = new Rgba(60, 60, 60);

        private readonly List<Item> _items = new List<Item>();

        /// <summary>The number of annotations.</summary>
        public int Count => _items.Count;

        /// <summary>A horizontal reference line at data <paramref name="y"/>, labelled at its right end.</summary>
        public Annotations HorizontalLine(double y, string? label = null, Rgba? color = null, LineDash dash = LineDash.Dashed, double width = 1.5)
            => Add(new Item(Kind.HLine, Finite(y, nameof(y)), 0, 0, 0, label, color ?? DefaultLineColor, dash, Positive(width)));

        /// <summary>A vertical reference line at data <paramref name="x"/>, labelled at its top.</summary>
        public Annotations VerticalLine(double x, string? label = null, Rgba? color = null, LineDash dash = LineDash.Dashed, double width = 1.5)
            => Add(new Item(Kind.VLine, Finite(x, nameof(x)), 0, 0, 0, label, color ?? DefaultLineColor, dash, Positive(width)));

        /// <summary>A shaded horizontal band between data y values (e.g. a tolerance zone).</summary>
        public Annotations HorizontalBand(double y1, double y2, string? label = null, Rgba? fill = null)
            => Add(new Item(Kind.HBand, Finite(Math.Min(y1, y2), nameof(y1)), Finite(Math.Max(y1, y2), nameof(y2)), 0, 0,
                label, fill ?? DefaultBandColor, LineDash.Solid, 0));

        /// <summary>A shaded vertical band between data x values (e.g. a time window).</summary>
        public Annotations VerticalBand(double x1, double x2, string? label = null, Rgba? fill = null)
            => Add(new Item(Kind.VBand, Finite(Math.Min(x1, x2), nameof(x1)), Finite(Math.Max(x1, x2), nameof(x2)), 0, 0,
                label, fill ?? DefaultBandColor, LineDash.Solid, 0));

        /// <summary>
        /// Marks the data point (<paramref name="x"/>, <paramref name="y"/>) and labels it with
        /// <paramref name="text"/> placed <paramref name="dx"/>/<paramref name="dy"/> pixels away, joined by an arrow.
        /// </summary>
        public Annotations Callout(double x, double y, string text, double dx = 30, double dy = -30, Rgba? color = null)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Add(new Item(Kind.Callout, Finite(x, nameof(x)), Finite(y, nameof(y)), Finite(dx, nameof(dx)), Finite(dy, nameof(dy)),
                text, color ?? DefaultCalloutColor, LineDash.Solid, 1));
        }

        /// <summary>An arrow between two data points, head at the second, with an optional label at its tail.</summary>
        public Annotations Arrow(double x1, double y1, double x2, double y2, string? label = null, Rgba? color = null, double width = 1.25)
            => Add(new Item(Kind.Arrow, Finite(x1, nameof(x1)), Finite(y1, nameof(y1)), Finite(x2, nameof(x2)), Finite(y2, nameof(y2)),
                label, color ?? DefaultCalloutColor, LineDash.Solid, Positive(width)));

        /// <summary>
        /// The data-space extent the annotations occupy on each axis (null where none contributes), for hosts that
        /// auto-range. Bands and lines contribute their values; callouts and arrows their data points.
        /// </summary>
        public (double? XMin, double? XMax, double? YMin, double? YMax) Extents()
        {
            double? xMin = null, xMax = null, yMin = null, yMax = null;
            void X(double v) { xMin = xMin is null ? v : Math.Min(xMin.Value, v); xMax = xMax is null ? v : Math.Max(xMax.Value, v); }
            void Y(double v) { yMin = yMin is null ? v : Math.Min(yMin.Value, v); yMax = yMax is null ? v : Math.Max(yMax.Value, v); }
            foreach (Item it in _items)
            {
                switch (it.Kind)
                {
                    case Kind.HLine: Y(it.A); break;
                    case Kind.VLine: X(it.A); break;
                    case Kind.HBand: Y(it.A); Y(it.B); break;
                    case Kind.VBand: X(it.A); X(it.B); break;
                    case Kind.Callout: X(it.A); Y(it.B); break;
                    case Kind.Arrow: X(it.A); Y(it.B); X(it.C); Y(it.D); break;
                }
            }
            return (xMin, xMax, yMin, yMax);
        }

        // ---------- SVG ----------

        /// <summary>
        /// Renders the layer as an SVG group (<c>&lt;g&gt;…&lt;/g&gt;</c>) to append to a document whose plot occupies
        /// <paramref name="frame"/>. Bands are emitted first so a host can place this fragment behind its series if
        /// it prefers; reference-line labels sit just above their line (below it when at the top edge).
        /// </summary>
        public string ToSvg(PlotFrame frame, double fontSize = 11, string fontFamily = "sans-serif", AnnotationLayer layer = AnnotationLayer.All)
        {
            if (!(fontSize > 0)) throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "Font size must be positive.");
            if (fontFamily is null) throw new ArgumentNullException(nameof(fontFamily));
            var sb = new StringBuilder();
            sb.Append("<g class=\"annotations\" font-family=\"").Append(SvgUtils.EscapeAttribute(fontFamily))
              .Append("\" font-size=\"").Append(SvgUtils.Number(fontSize)).Append("\">\n");
            if (layer != AnnotationLayer.Foreground)
                foreach (Item it in _items) if (it.Kind == Kind.HBand || it.Kind == Kind.VBand) SvgBand(sb, it, frame, fontSize);
            if (layer != AnnotationLayer.Background)
            {
                foreach (Item it in _items) if (it.Kind == Kind.HLine || it.Kind == Kind.VLine) SvgLine(sb, it, frame, fontSize);
                foreach (Item it in _items) if (it.Kind == Kind.Callout || it.Kind == Kind.Arrow) SvgPointer(sb, it, frame, fontSize);
            }
            sb.Append("</g>\n");
            return sb.ToString();
        }

        private static void SvgBand(StringBuilder sb, Item it, PlotFrame f, double fontSize)
        {
            double x0, x1, y0, y1;
            if (it.Kind == Kind.HBand)
            {
                if (!Overlaps(it.A, it.B, f.YMin, f.YMax)) return;
                x0 = f.Left; x1 = f.Right;
                y0 = ClampY(f.MapY(it.B), f); y1 = ClampY(f.MapY(it.A), f); // higher value is the smaller pixel y
            }
            else
            {
                if (!Overlaps(it.A, it.B, f.XMin, f.XMax)) return;
                y0 = f.Top; y1 = f.Bottom;
                x0 = ClampX(f.MapX(it.A), f); x1 = ClampX(f.MapX(it.B), f);
            }
            if (double.IsNaN(x0) || double.IsNaN(x1) || double.IsNaN(y0) || double.IsNaN(y1)) return;
            sb.Append("<rect x=\"").Append(SvgUtils.Number(Math.Min(x0, x1))).Append("\" y=\"").Append(SvgUtils.Number(Math.Min(y0, y1)))
              .Append("\" width=\"").Append(SvgUtils.Number(Math.Abs(x1 - x0))).Append("\" height=\"").Append(SvgUtils.Number(Math.Abs(y1 - y0)))
              .Append("\" ").Append(SvgUtils.FillAttributes(it.Color)).Append("/>\n");
            if (!string.IsNullOrEmpty(it.Label))
            {
                double tx = Math.Min(x0, x1) + 4, ty = Math.Min(y0, y1) + fontSize + 2;
                Text(sb, tx, ty, it.Label!, Opaque(it.Color), "start");
            }
        }

        private static void SvgLine(StringBuilder sb, Item it, PlotFrame f, double fontSize)
        {
            double x1, y1, x2, y2;
            if (it.Kind == Kind.HLine)
            {
                if (!f.ContainsY(it.A)) return;
                double py = f.MapY(it.A);
                if (double.IsNaN(py)) return;
                x1 = f.Left; x2 = f.Right; y1 = y2 = py;
            }
            else
            {
                if (!f.ContainsX(it.A)) return;
                double px = f.MapX(it.A);
                if (double.IsNaN(px)) return;
                y1 = f.Top; y2 = f.Bottom; x1 = x2 = px;
            }
            sb.Append("<line x1=\"").Append(SvgUtils.Number(x1)).Append("\" y1=\"").Append(SvgUtils.Number(y1))
              .Append("\" x2=\"").Append(SvgUtils.Number(x2)).Append("\" y2=\"").Append(SvgUtils.Number(y2)).Append("\" ")
              .Append(SvgUtils.StrokeAttributes(it.Color, it.Width, DashArray(it.Dash, it.Width))).Append("/>\n");
            if (string.IsNullOrEmpty(it.Label)) return;

            if (it.Kind == Kind.HLine)
            {
                // Above the line at its right end; below it when there is no room at the top.
                double ty = y1 - 3 - fontSize < f.Top ? y1 + fontSize + 2 : y1 - 4;
                Text(sb, f.Right - 4, ty, it.Label!, it.Color, "end");
            }
            else
            {
                // Right of the line near the top; flipped left when it would run off the plot.
                double w = SvgUtils.EstimateTextWidth(it.Label!, fontSize);
                bool flip = x1 + 4 + w > f.Right;
                Text(sb, flip ? x1 - 4 : x1 + 4, f.Top + fontSize + 2, it.Label!, it.Color, flip ? "end" : "start");
            }
        }

        private static void SvgPointer(StringBuilder sb, Item it, PlotFrame f, double fontSize)
        {
            double tipX, tipY, tailX, tailY;
            if (it.Kind == Kind.Callout)
            {
                if (!f.ContainsX(it.A) || !f.ContainsY(it.B)) return;
                tipX = f.MapX(it.A); tipY = f.MapY(it.B);
                if (double.IsNaN(tipX) || double.IsNaN(tipY)) return;
                tailX = tipX + it.C; tailY = tipY + it.D;

                // Keep the label (and so the arrow's tail) inside the plot: a point near an edge would otherwise push
                // its pixel-offset label off the chart. Horizontal room depends on which way the text runs.
                double w = SvgUtils.EstimateTextWidth(it.Label ?? "", fontSize);
                bool runsLeft = tipX >= tailX; // label anchored "end"
                double minX = f.Left + 2 + (runsLeft ? w : 0), maxX = f.Right - 2 - (runsLeft ? 0 : w);
                if (minX <= maxX) tailX = Math.Max(minX, Math.Min(maxX, tailX));
                bool above = tipY >= tailY;
                double minY = f.Top + 2 + (above ? fontSize + 3 : 0), maxY = f.Bottom - 2 - (above ? 0 : fontSize);
                if (minY <= maxY) tailY = Math.Max(minY, Math.Min(maxY, tailY));
                sb.Append("<circle cx=\"").Append(SvgUtils.Number(tipX)).Append("\" cy=\"").Append(SvgUtils.Number(tipY))
                  .Append("\" r=\"3\" ").Append(SvgUtils.FillAttributes(it.Color)).Append("/>\n");
            }
            else
            {
                if (!f.ContainsX(it.A) || !f.ContainsY(it.B) || !f.ContainsX(it.C) || !f.ContainsY(it.D)) return;
                tailX = f.MapX(it.A); tailY = f.MapY(it.B); tipX = f.MapX(it.C); tipY = f.MapY(it.D);
                if (double.IsNaN(tailX) || double.IsNaN(tailY) || double.IsNaN(tipX) || double.IsNaN(tipY)) return;
            }

            // Stop the shaft short of a callout's marker so the head stays visible.
            double len = Math.Sqrt((tipX - tailX) * (tipX - tailX) + (tipY - tailY) * (tipY - tailY));
            if (len > 1e-9)
            {
                double ux = (tipX - tailX) / len, uy = (tipY - tailY) / len;
                double gap = it.Kind == Kind.Callout ? 4 : 0;
                double hx = tipX - ux * gap, hy = tipY - uy * gap;
                sb.Append("<line x1=\"").Append(SvgUtils.Number(tailX)).Append("\" y1=\"").Append(SvgUtils.Number(tailY))
                  .Append("\" x2=\"").Append(SvgUtils.Number(hx - ux * HeadLength * 0.8)).Append("\" y2=\"").Append(SvgUtils.Number(hy - uy * HeadLength * 0.8))
                  .Append("\" ").Append(SvgUtils.StrokeAttributes(it.Color, it.Width)).Append("/>\n");
                var head = ArrowHead(hx, hy, ux, uy);
                sb.Append("<polygon points=\"");
                for (int i = 0; i < head.Length; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(SvgUtils.Number(head[i].X)).Append(',').Append(SvgUtils.Number(head[i].Y));
                }
                sb.Append("\" ").Append(SvgUtils.FillAttributes(it.Color)).Append("/>\n");
            }

            if (!string.IsNullOrEmpty(it.Label))
            {
                // Label sits at the tail, on the side away from the target.
                string anchor = tipX >= tailX ? "end" : "start";
                double ty = tailY + (tipY >= tailY ? -3 : fontSize);
                Text(sb, tailX, ty, it.Label!, it.Color, anchor);
            }
        }

        private static void Text(StringBuilder sb, double x, double y, string text, Rgba color, string anchor)
            => sb.Append("<text x=\"").Append(SvgUtils.Number(x)).Append("\" y=\"").Append(SvgUtils.Number(y))
                 .Append("\" text-anchor=\"").Append(anchor).Append("\" ").Append(SvgUtils.FillAttributes(color)).Append('>')
                 .Append(SvgUtils.EscapeText(text)).Append("</text>\n");

        // ---------- raster ----------

        /// <summary>
        /// Draws the layer onto <paramref name="image"/>, whose plot occupies <paramref name="frame"/>. Bands are
        /// alpha-blended over what is already there; lines, markers and arrows overwrite. Labels are not drawn
        /// (the raster canvas has no font engine) — use <see cref="ToSvg"/> when the labels matter.
        /// </summary>
        public void Draw(ImageBuffer image, PlotFrame frame)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            foreach (Item it in _items)
            {
                switch (it.Kind)
                {
                    case Kind.HBand:
                    case Kind.VBand:
                        RasterBand(image, it, frame);
                        break;
                }
            }
            foreach (Item it in _items)
            {
                switch (it.Kind)
                {
                    case Kind.HLine:
                        if (!frame.ContainsY(it.A)) break;
                        int py = Px(frame.MapY(it.A));
                        StyledLine(image, Px(frame.Left), py, Px(frame.Right), py, it.Color, it.Dash, Thickness(it.Width));
                        break;
                    case Kind.VLine:
                        if (!frame.ContainsX(it.A)) break;
                        int px = Px(frame.MapX(it.A));
                        StyledLine(image, px, Px(frame.Top), px, Px(frame.Bottom), it.Color, it.Dash, Thickness(it.Width));
                        break;
                    case Kind.Callout:
                    {
                        if (!frame.ContainsX(it.A) || !frame.ContainsY(it.B)) break;
                        double tx = frame.MapX(it.A), ty = frame.MapY(it.B);
                        if (double.IsNaN(tx) || double.IsNaN(ty)) break;
                        image.DrawRectangle(Px(tx) - 2, Px(ty) - 2, 5, 5, it.Color, filled: true);
                        RasterArrow(image, tx + it.C, ty + it.D, tx, ty, it.Color, gap: 4);
                        break;
                    }
                    case Kind.Arrow:
                    {
                        if (!frame.ContainsX(it.A) || !frame.ContainsY(it.B) || !frame.ContainsX(it.C) || !frame.ContainsY(it.D)) break;
                        double x1 = frame.MapX(it.A), y1 = frame.MapY(it.B), x2 = frame.MapX(it.C), y2 = frame.MapY(it.D);
                        if (double.IsNaN(x1) || double.IsNaN(y1) || double.IsNaN(x2) || double.IsNaN(y2)) break;
                        RasterArrow(image, x1, y1, x2, y2, it.Color, gap: 0);
                        break;
                    }
                }
            }
        }

        private static void RasterBand(ImageBuffer image, Item it, PlotFrame f)
        {
            double x0, x1, y0, y1;
            if (it.Kind == Kind.HBand)
            {
                if (!Overlaps(it.A, it.B, f.YMin, f.YMax)) return;
                x0 = f.Left; x1 = f.Right; y0 = ClampY(f.MapY(it.B), f); y1 = ClampY(f.MapY(it.A), f);
            }
            else
            {
                if (!Overlaps(it.A, it.B, f.XMin, f.XMax)) return;
                y0 = f.Top; y1 = f.Bottom; x0 = ClampX(f.MapX(it.A), f); x1 = ClampX(f.MapX(it.B), f);
            }
            if (double.IsNaN(x0) || double.IsNaN(x1) || double.IsNaN(y0) || double.IsNaN(y1)) return;
            int left = Math.Max(0, Px(Math.Min(x0, x1))), right = Math.Min(image.Width - 1, Px(Math.Max(x0, x1)));
            int top = Math.Max(0, Px(Math.Min(y0, y1))), bottom = Math.Min(image.Height - 1, Px(Math.Max(y0, y1)));
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                    image.SetPixel(x, y, Blend(image.GetPixel(x, y), it.Color));
        }

        private static void RasterArrow(ImageBuffer image, double tailX, double tailY, double tipX, double tipY, Rgba color, double gap)
        {
            double len = Math.Sqrt((tipX - tailX) * (tipX - tailX) + (tipY - tailY) * (tipY - tailY));
            if (len < 1e-9) return;
            double ux = (tipX - tailX) / len, uy = (tipY - tailY) / len;
            double hx = tipX - ux * gap, hy = tipY - uy * gap;
            StyledLine(image, Px(tailX), Px(tailY), Px(hx), Px(hy), color, LineDash.Solid, 1);
            var head = ArrowHead(hx, hy, ux, uy);
            StyledLine(image, Px(head[0].X), Px(head[0].Y), Px(head[1].X), Px(head[1].Y), color, LineDash.Solid, 1);
            StyledLine(image, Px(head[0].X), Px(head[0].Y), Px(head[2].X), Px(head[2].Y), color, LineDash.Solid, 1);
        }

        /// <summary>Bresenham with a dash pattern and square thickness, clipped to the image.</summary>
        private static void StyledLine(ImageBuffer image, int x0, int y0, int x1, int y1, Rgba color, LineDash dash, int thickness)
        {
            int on = dash == LineDash.Dashed ? 6 : (dash == LineDash.Dotted ? 1 : int.MaxValue);
            int off = dash == LineDash.Dashed ? 4 : (dash == LineDash.Dotted ? 2 : 0);
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, step = 0, half = (thickness - 1) / 2;
            while (true)
            {
                if (dash == LineDash.Solid || step % (on + off) < on)
                    for (int ty = -half; ty < thickness - half; ty++)
                        for (int tx = -half; tx < thickness - half; tx++)
                        {
                            int x = x0 + tx, y = y0 + ty;
                            if (x >= 0 && x < image.Width && y >= 0 && y < image.Height) image.SetPixel(x, y, color);
                        }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
                step++;
            }
        }

        // ---------- shared geometry ----------

        private const double HeadLength = 8;
        private const double HeadHalfWidth = 3.5;

        /// <summary>Triangle points: the tip, then the two barbs.</summary>
        private static (double X, double Y)[] ArrowHead(double tipX, double tipY, double ux, double uy)
        {
            double bx = tipX - ux * HeadLength, by = tipY - uy * HeadLength;
            double nx = -uy * HeadHalfWidth, ny = ux * HeadHalfWidth;
            return new[] { (tipX, tipY), (bx + nx, by + ny), (bx - nx, by - ny) };
        }

        private static Rgba Blend(Rgba dst, Rgba src)
        {
            double a = src.A / 255.0;
            byte Mix(byte d, byte s) => (byte)Math.Round(s * a + d * (1 - a));
            return new Rgba(Mix(dst.R, src.R), Mix(dst.G, src.G), Mix(dst.B, src.B), (byte)Math.Max(dst.A, src.A));
        }

        private static Rgba Opaque(Rgba c) => new Rgba(c.R, c.G, c.B, 255);

        private static string? DashArray(LineDash dash, double width) => dash switch
        {
            LineDash.Dashed => SvgUtils.Number(6 * width) + " " + SvgUtils.Number(4 * width),
            LineDash.Dotted => SvgUtils.Number(width) + " " + SvgUtils.Number(2 * width),
            _ => null,
        };

        private static bool Overlaps(double a, double b, double min, double max)
            => b >= Math.Min(min, max) && a <= Math.Max(min, max);

        private static double ClampX(double px, PlotFrame f) => double.IsNaN(px) ? px : Math.Max(f.Left, Math.Min(f.Right, px));

        private static double ClampY(double py, PlotFrame f) => double.IsNaN(py) ? py : Math.Max(f.Top, Math.Min(f.Bottom, py));

        private static int Px(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

        private static int Thickness(double width) => Math.Max(1, (int)Math.Round(width, MidpointRounding.AwayFromZero));

        private Annotations Add(Item item)
        {
            _items.Add(item);
            return this;
        }

        private static double Finite(double v, string name)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentOutOfRangeException(name, v, "Must be finite.");
            return v;
        }

        private static double Positive(double width)
        {
            if (!(width > 0) || double.IsInfinity(width)) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            return width;
        }

        private enum Kind { HLine, VLine, HBand, VBand, Callout, Arrow }

        private sealed class Item
        {
            public Item(Kind kind, double a, double b, double c, double d, string? label, Rgba color, LineDash dash, double width)
            {
                Kind = kind; A = a; B = b; C = c; D = d; Label = label; Color = color; Dash = dash; Width = width;
            }

            public Kind Kind { get; }
            public double A { get; }
            public double B { get; }
            public double C { get; }
            public double D { get; }
            public string? Label { get; }
            public Rgba Color { get; }
            public LineDash Dash { get; }
            public double Width { get; }
        }
    }
}
