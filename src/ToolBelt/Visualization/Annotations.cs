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
            => Add(new Item(Kind.HLine, Finite(y, nameof(y)), 0, 0, 0, label, color ?? DefaultLineColor, Dash(dash), Positive(width)));

        /// <summary>A vertical reference line at data <paramref name="x"/>, labelled at its top.</summary>
        public Annotations VerticalLine(double x, string? label = null, Rgba? color = null, LineDash dash = LineDash.Dashed, double width = 1.5)
            => Add(new Item(Kind.VLine, Finite(x, nameof(x)), 0, 0, 0, label, color ?? DefaultLineColor, Dash(dash), Positive(width)));

        // An undefined dash drew as solid in a raster and as a dotted line in SVG.
        private static LineDash Dash(LineDash dash)
            => dash < LineDash.Solid || dash > LineDash.Dotted ? throw new ArgumentOutOfRangeException(nameof(dash), dash, "Unknown dash.") : dash;

        // default(PlotFrame) (zero area, NaN mapping) drew nothing and returned no error; HexBin rejects it.
        private static void CheckFrame(PlotFrame frame)
        {
            if (!(frame.Width > 0) || !(frame.Height > 0)) throw new ArgumentException("The frame has no area (default(PlotFrame)?).", nameof(frame));
        }

        /// <summary>A shaded horizontal band between data y values (e.g. a tolerance zone).</summary>
        public Annotations HorizontalBand(double y1, double y2, string? label = null, Rgba? fill = null)
            => Add(new Item(Kind.HBand, Math.Min(Finite(y1, nameof(y1)), Finite(y2, nameof(y2))), Math.Max(y1, y2), 0, 0,
                label, fill ?? DefaultBandColor, LineDash.Solid, 0));

        /// <summary>A shaded vertical band between data x values (e.g. a time window).</summary>
        public Annotations VerticalBand(double x1, double x2, string? label = null, Rgba? fill = null)
            => Add(new Item(Kind.VBand, Math.Min(Finite(x1, nameof(x1)), Finite(x2, nameof(x2))), Math.Max(x1, x2), 0, 0,
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
            CheckFrame(frame);
            // An undefined layer emitted nothing.
            if (layer < AnnotationLayer.All || layer > AnnotationLayer.Foreground) throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown layer.");
            // ∞ failed inside SvgUtils, naming "value".
            if (!(fontSize > 0) || double.IsInfinity(fontSize)) throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "Font size must be positive and finite.");
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
                y0 = ClampY(BandY(it.B, f), f); y1 = ClampY(BandY(it.A, f), f); // higher value is the smaller pixel y
            }
            else
            {
                if (!Overlaps(it.A, it.B, f.XMin, f.XMax)) return;
                y0 = f.Top; y1 = f.Bottom;
                x0 = ClampX(BandX(it.A, f), f); x1 = ClampX(BandX(it.B, f), f);
            }
            if (double.IsNaN(x0) || double.IsNaN(x1) || double.IsNaN(y0) || double.IsNaN(y1)) return;
            sb.Append("<rect x=\"").Append(SvgUtils.Number(Math.Min(x0, x1))).Append("\" y=\"").Append(SvgUtils.Number(Math.Min(y0, y1)))
              .Append("\" width=\"").Append(SvgUtils.Number(Math.Abs(x1 - x0))).Append("\" height=\"").Append(SvgUtils.Number(Math.Abs(y1 - y0)))
              .Append("\" ").Append(SvgUtils.FillAttributes(it.Color)).Append("/>\n");
            if (!string.IsNullOrEmpty(it.Label))
            {
                // Squeezed to the plot's right edge: a long label ran off the chart.
                double tx = Math.Min(x0, x1) + 4, ty = Baseline(Math.Min(y0, y1) + fontSize + 2, f, fontSize);
                Text(sb, tx, ty, it.Label!, Opaque(it.Color), "start", fontSize, f.Right - 2 - tx);
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
                // Squeezed into the plot when wider: it ran off the left edge of the chart.
                double ty = Baseline(y1 - 3 - fontSize < f.Top ? y1 + fontSize + 2 : y1 - 4, f, fontSize);
                Text(sb, f.Right - 4, ty, it.Label!, it.Color, "end", fontSize, f.Right - 4 - (f.Left + 2));
            }
            else
            {
                // Right of the line near the top; flipped left when it would run off the plot and fits there. Wider than
                // both sides, it is squeezed into the roomier one: the flip checked only the right, so a line near the
                // left edge ran its label off the chart.
                double w = SvgUtils.EstimateTextWidth(it.Label!, fontSize);
                double roomRight = f.Right - 2 - (x1 + 4), roomLeft = x1 - 4 - (f.Left + 2);
                bool flip = w > roomRight && (w <= roomLeft || roomLeft > roomRight);
                Text(sb, flip ? x1 - 4 : x1 + 4, Baseline(f.Top + fontSize + 2, f, fontSize), it.Label!, it.Color, flip ? "end" : "start",
                    fontSize, flip ? roomLeft : roomRight);
            }
        }

        private static void SvgPointer(StringBuilder sb, Item it, PlotFrame f, double fontSize)
        {
            double tipX, tipY, tailX, tailY;
            // The label's side is decided once, from the requested offset: re-deciding it after the clamp could flip the
            // anchor (a tail clamped past the tip), running the label off the plot it was clamped to stay inside.
            bool runsLeft, above;
            if (it.Kind == Kind.Callout)
            {
                if (!f.ContainsX(it.A) || !f.ContainsY(it.B)) return;
                tipX = f.MapX(it.A); tipY = f.MapY(it.B);
                if (double.IsNaN(tipX) || double.IsNaN(tipY)) return;
                CalloutTail(it, f, tipX, tipY, fontSize, out tailX, out tailY, out runsLeft, out above);
                sb.Append("<circle cx=\"").Append(SvgUtils.Number(tipX)).Append("\" cy=\"").Append(SvgUtils.Number(tipY))
                  .Append("\" r=\"3\" ").Append(SvgUtils.FillAttributes(it.Color)).Append("/>\n");
            }
            else
            {
                if (!f.ContainsX(it.A) || !f.ContainsY(it.B) || !f.ContainsX(it.C) || !f.ContainsY(it.D)) return;
                tailX = f.MapX(it.A); tailY = f.MapY(it.B); tipX = f.MapX(it.C); tipY = f.MapY(it.D);
                if (double.IsNaN(tailX) || double.IsNaN(tailY) || double.IsNaN(tipX) || double.IsNaN(tipY)) return;
                runsLeft = tipX >= tailX; above = tipY >= tailY;
            }

            // Stop the shaft short of a callout's marker so the head stays visible.
            double gap = it.Kind == Kind.Callout ? 4 : 0;
            if (Direction(tailX, tailY, tipX, tipY, out double ux, out double uy, out double len) && len - gap > 1e-9)
            {
                double hx = tipX - ux * gap, hy = tipY - uy * gap;
                // A head no longer than the arrow: an 8-px head on a 2-px arrow ran its shaft and base back past the tail.
                double headLength = Math.Min(HeadLength, len - gap);
                sb.Append("<line x1=\"").Append(SvgUtils.Number(tailX)).Append("\" y1=\"").Append(SvgUtils.Number(tailY))
                  .Append("\" x2=\"").Append(SvgUtils.Number(hx - ux * headLength * 0.8)).Append("\" y2=\"").Append(SvgUtils.Number(hy - uy * headLength * 0.8))
                  .Append("\" ").Append(SvgUtils.StrokeAttributes(it.Color, it.Width)).Append("/>\n");
                var head = ArrowHead(hx, hy, ux, uy, headLength);
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
                string anchor = runsLeft ? "end" : "start";
                double ty = Baseline(tailY + (above ? -3 : fontSize), f, fontSize);
                Text(sb, tailX, ty, it.Label!, it.Color, anchor, fontSize, runsLeft ? tailX - (f.Left + 2) : f.Right - 2 - tailX);
            }
        }

        // A label's baseline held inside the plot, as a callout's is: a thin band at the bottom, an arrow's tail on the top
        // edge, or a line's label on a short plot put the text over the axis labels. A plot shorter than the text keeps
        // the baseline at the bottom.
        private static double Baseline(double y, PlotFrame f, double fontSize)
            => Math.Min(f.Bottom - 2, Math.Max(f.Top + 2 + fontSize, y));

        // Keeps the label (and so the arrow's tail) inside the plot: a point near an edge would otherwise push its
        // pixel-offset label off the chart. Horizontal room depends on which way the text runs. Shared by the SVG and
        // raster layers: the raster callout took the raw offset and ran through the margin to the image edge.
        private static void CalloutTail(Item it, PlotFrame f, double tipX, double tipY, double fontSize,
            out double tailX, out double tailY, out bool runsLeft, out bool above)
        {
            tailX = tipX + it.C; tailY = tipY + it.D;
            double w = SvgUtils.EstimateTextWidth(it.Label ?? "", fontSize);
            runsLeft = tipX >= tailX; // label anchored "end"
            double minX = f.Left + 2 + (runsLeft ? w : 0), maxX = f.Right - 2 - (runsLeft ? 0 : w);
            // Wider than the plot, the label starts at the edge it runs from and is squeezed to the plot below: the
            // clamp was skipped, and the label ran off the chart.
            if (minX <= maxX) tailX = Math.Max(minX, Math.Min(maxX, tailX));
            else tailX = runsLeft ? f.Right - 2 : f.Left + 2;
            above = tipY >= tailY;
            double minY = f.Top + 2 + (above ? fontSize + 3 : 0), maxY = f.Bottom - 2 - (above ? 0 : fontSize);
            if (minY <= maxY) tailY = Math.Max(minY, Math.Min(maxY, tailY));
            else tailY = Math.Max(f.Top, Math.Min(f.Bottom, tailY));
        }

        // The unit direction from (x0, y0) to (x1, y1) and its length, from halved, scaled deltas: the squares overflowed
        // on a frame 1e200 px wide (a zero direction, the head a point and the shaft run to the tip).
        private static bool Direction(double x0, double y0, double x1, double y1, out double ux, out double uy, out double len)
        {
            ux = uy = len = 0;
            double dx = x1 * 0.5 - x0 * 0.5, dy = y1 * 0.5 - y0 * 0.5, sc = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (!(sc * 2 >= 1e-9) || double.IsInfinity(sc)) return false;
            double hyp = Math.Sqrt((dx / sc) * (dx / sc) + (dy / sc) * (dy / sc));
            len = 2 * sc * hyp;
            if (!(len >= 1e-9)) return false;
            ux = dx / sc / hyp; uy = dy / sc / hyp;
            return true;
        }

        // Squeezed to maxWidth when its estimate is wider (no limit when maxWidth ≤ 0: no room at all to fit it to).
        private static void Text(StringBuilder sb, double x, double y, string text, Rgba color, string anchor, double fontSize, double maxWidth)
            => sb.Append("<text x=\"").Append(SvgUtils.Number(x)).Append("\" y=\"").Append(SvgUtils.Number(y))
                 .Append("\" text-anchor=\"").Append(anchor).Append('"').Append(SvgUtils.FitWidth(text, fontSize, maxWidth))
                 .Append(' ').Append(SvgUtils.FillAttributes(color)).Append('>')
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
            CheckFrame(frame);
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
                        double py = frame.MapY(it.A);
                        StyledLine(image, frame.Left, py, frame.Right, py, it.Color, it.Dash, Thickness(it.Width));
                        break;
                    case Kind.VLine:
                        if (!frame.ContainsX(it.A)) break;
                        double px = frame.MapX(it.A);
                        StyledLine(image, px, frame.Top, px, frame.Bottom, it.Color, it.Dash, Thickness(it.Width));
                        break;
                    case Kind.Callout:
                    {
                        if (!frame.ContainsX(it.A) || !frame.ContainsY(it.B)) break;
                        double tx = frame.MapX(it.A), ty = frame.MapY(it.B);
                        if (double.IsNaN(tx) || double.IsNaN(ty)) break;
                        // Cut to the plot area: a point on the frame painted 2 px into the margin.
                        FillClipped(image, Px(tx) - 2, Px(ty) - 2, 5, 5, it.Color, frame);
                        // The tail where the SVG layer puts it (at its default font size), inside the plot.
                        CalloutTail(it, frame, tx, ty, 11, out double cx, out double cy, out _, out _);
                        RasterArrow(image, frame, cx, cy, tx, ty, it.Color, gap: 4);
                        break;
                    }
                    case Kind.Arrow:
                    {
                        if (!frame.ContainsX(it.A) || !frame.ContainsY(it.B) || !frame.ContainsX(it.C) || !frame.ContainsY(it.D)) break;
                        double x1 = frame.MapX(it.A), y1 = frame.MapY(it.B), x2 = frame.MapX(it.C), y2 = frame.MapY(it.D);
                        if (double.IsNaN(x1) || double.IsNaN(y1) || double.IsNaN(x2) || double.IsNaN(y2)) break;
                        RasterArrow(image, frame, x1, y1, x2, y2, it.Color, gap: 0);
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
                x0 = f.Left; x1 = f.Right; y0 = ClampY(BandY(it.B, f), f); y1 = ClampY(BandY(it.A, f), f);
            }
            else
            {
                if (!Overlaps(it.A, it.B, f.XMin, f.XMax)) return;
                y0 = f.Top; y1 = f.Bottom; x0 = ClampX(BandX(it.A, f), f); x1 = ClampX(BandX(it.B, f), f);
            }
            if (double.IsNaN(x0) || double.IsNaN(x1) || double.IsNaN(y0) || double.IsNaN(y1)) return;
            // The pixels whose centres are in [low, high), and the plot's far edge: inclusive at both ends, bands that tile
            // a range (0..1 and 1..2) both blended their shared row, a darker seam the SVG does not have. Each pixel
            // belongs to one band of a tiling, so a band that covers no pixel centre has no pixels.
            int left = Math.Max(0, First(Math.Min(x0, x1))), right = Math.Min(image.Width - 1, Last(Math.Max(x0, x1), f.Right));
            int top = Math.Max(0, First(Math.Min(y0, y1))), bottom = Math.Min(image.Height - 1, Last(Math.Max(y0, y1), f.Bottom));
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                    image.SetPixel(x, y, Blend(image.GetPixel(x, y), it.Color));
        }

        private static void RasterArrow(ImageBuffer image, PlotFrame f, double tailX, double tailY, double tipX, double tipY, Rgba color, double gap)
        {
            // Scaled, from halved deltas: the squares overflowed for dx = 1e200 (len = ∞, a NaN direction, no arrow).
            if (!Direction(tailX, tailY, tipX, tipY, out double ux, out double uy, out double len) || !(len - gap > 1e-9)) return;
            double hx = tipX - ux * gap, hy = tipY - uy * gap;
            PlotLine(image, f, tailX, tailY, hx, hy, color);
            var head = ArrowHead(hx, hy, ux, uy, Math.Min(HeadLength, len - gap));
            PlotLine(image, f, head[0].X, head[0].Y, head[1].X, head[1].Y, color);
            PlotLine(image, f, head[0].X, head[0].Y, head[2].X, head[2].Y, color);
        }

        // A 1-px segment cut to the plot area: the wings of a head at a point on the frame reached 4 px into the margin.
        private static void PlotLine(ImageBuffer image, PlotFrame f, double x0, double y0, double x1, double y1, Rgba color)
        {
            double dx = x1 - x0, dy = y1 - y0, t0 = 0, t1 = 1;
            double l = Px(f.Left), r = Px(f.Right), t = Px(f.Top), b = Px(f.Bottom);
            if (!Clip(-dx, x0 - l, ref t0, ref t1) || !Clip(dx, r - x0, ref t0, ref t1)
                || !Clip(-dy, y0 - t, ref t0, ref t1) || !Clip(dy, b - y0, ref t0, ref t1))
                return;
            StyledLine(image, t0 == 0 ? x0 : x0 + t0 * dx, t0 == 0 ? y0 : y0 + t0 * dy,
                t1 == 1 ? x1 : x0 + t1 * dx, t1 == 1 ? y1 : y0 + t1 * dy, color, LineDash.Solid, 1);
        }

        /// <summary>
        /// Bresenham with a dash pattern and square thickness, clipped to the image. The segment is first cut to the
        /// image (plus the pen) in doubles: a far off-image endpoint was stepped through pixel by pixel, or overflowed
        /// the int cast, and a pen wider than the image cost thickness² per pixel for nothing.
        /// </summary>
        private static void StyledLine(ImageBuffer image, double fx0, double fy0, double fx1, double fy1, Rgba color, LineDash dash, int thickness)
        {
            if (double.IsNaN(fx0) || double.IsNaN(fy0) || double.IsNaN(fx1) || double.IsNaN(fy1)) return;
            thickness = Math.Min(thickness, image.Width + image.Height);
            double m = thickness + 1;
            // Clipped from the end nearer the image: from the far end x0 + t·dx cancels (t ≈ 1), so a callout whose start
            // was 1e200 px off drew its line through the wrong pixels or not at all. The drawing direction is kept.
            double Out(double x, double y) => Math.Max(Math.Max(-x, x - (image.Width - 1)), Math.Max(-y, y - (image.Height - 1)));
            bool flip = Out(fx0, fy0) > Out(fx1, fy1);
            double nx = flip ? fx1 : fx0, ny = flip ? fy1 : fy0, ex = flip ? fx0 : fx1, ey = flip ? fy0 : fy1;
            double ddx = ex - nx, ddy = ey - ny, t0 = 0, t1 = 1;
            if (double.IsInfinity(ddx) || double.IsInfinity(ddy)) return;
            if (!Clip(-ddx, nx + m, ref t0, ref t1) || !Clip(ddx, image.Width - 1 + m - nx, ref t0, ref t1)
                || !Clip(-ddy, ny + m, ref t0, ref t1) || !Clip(ddy, image.Height - 1 + m - ny, ref t0, ref t1))
                return;
            int on = dash == LineDash.Dashed ? 6 : (dash == LineDash.Dotted ? 1 : int.MaxValue);
            int off = dash == LineDash.Dashed ? 4 : (dash == LineDash.Dotted ? 2 : 0);
            int na = Px(t0 == 0 ? nx : nx + t0 * ddx), nb = Px(t0 == 0 ? ny : ny + t0 * ddy);
            int ea = Px(t1 == 1 ? ex : nx + t1 * ddx), eb = Px(t1 == 1 ? ey : ny + t1 * ddy);
            int x0 = flip ? ea : na, y0 = flip ? eb : nb, x1 = flip ? na : ea, y1 = flip ? nb : eb;
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            // Keep the dash phase of the part cut off the start.
            double cut = flip ? 1 - t1 : t0;
            int step = dash == LineDash.Solid ? 0 : (int)(Math.Round(cut * Math.Max(Math.Abs(ddx), Math.Abs(ddy))) % (on + off));
            int err = dx + dy, half = (thickness - 1) / 2;
            // The last square drawn is skipped across a gap too: it is already this colour, and re-stamping the whole
            // square at every dash start cost thickness² each (a dotted line 1e6 wide took 14 s on a 2000-px image).
            bool stamped = false;
            int lastX = 0, lastY = 0;
            while (true)
            {
                if (dash == LineDash.Solid || step % (on + off) < on)
                {
                    Stamp(image, x0, y0, half, thickness, color, stamped, lastX, lastY);
                    stamped = true; lastX = x0; lastY = y0;
                }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
                step++;
            }
        }

        // One Liang–Barsky edge test.
        private static bool Clip(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0) return q >= 0;
            double t = q / p;
            if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }

        // ---------- shared geometry ----------

        private const double HeadLength = 8;
        private const double HeadHalfWidth = 3.5;

        /// <summary>Triangle points: the tip, then the two barbs.</summary>
        // A head of the given length (at most HeadLength), its width in proportion.
        private static (double X, double Y)[] ArrowHead(double tipX, double tipY, double ux, double uy, double length)
        {
            double bx = tipX - ux * length, by = tipY - uy * length, half = HeadHalfWidth * length / HeadLength;
            double nx = -uy * half, ny = ux * half;
            return new[] { (tipX, tipY), (bx + nx, by + ny), (bx - nx, by - ny) };
        }

        // Source over destination. The destination's alpha was ignored, so a band on a transparent canvas came out as its
        // colour faded toward black (31,119,180 at 48 became 6,22,34), and two half-transparent layers kept alpha 128.
        private static Rgba Blend(Rgba dst, Rgba src)
        {
            double sa = src.A / 255.0, da = dst.A / 255.0 * (1 - sa), outA = sa + da;
            if (outA <= 0) return new Rgba(0, 0, 0, 0);
            byte Mix(byte d, byte s) => (byte)Math.Round((s * sa + d * da) / outA);
            return new Rgba(Mix(dst.R, src.R), Mix(dst.G, src.G), Mix(dst.B, src.B), (byte)Math.Round(outA * 255));
        }

        private static Rgba Opaque(Rgba c) => new Rgba(c.R, c.G, c.B, 255);

        // The pattern is capped where 6 × width would overflow (width = 1e308 failed as "value"); it is still wider than any plot.
        private static string? DashArray(LineDash dash, double width) => DashArrayOf(dash, Math.Min(width, 1e300));

        private static string? DashArrayOf(LineDash dash, double width) => dash switch
        {
            LineDash.Dashed => SvgUtils.Number(6 * width) + " " + SvgUtils.Number(4 * width),
            LineDash.Dotted => SvgUtils.Number(width) + " " + SvgUtils.Number(2 * width),
            _ => null,
        };

        private static bool Overlaps(double a, double b, double min, double max)
            => b >= Math.Min(min, max) && a <= Math.Max(min, max);

        // A band bound at or below zero on a log axis has no position, but the band still covers the axis down to
        // its lower edge (it was dropped altogether).
        private static double BandX(double v, PlotFrame f) => f.MapX(f.LogX && !(v > 0) ? Math.Min(f.XMin, f.XMax) : v);

        private static double BandY(double v, PlotFrame f) => f.MapY(f.LogY && !(v > 0) ? Math.Min(f.YMin, f.YMax) : v);

        private static double ClampX(double px, PlotFrame f) => double.IsNaN(px) ? px : Math.Max(f.Left, Math.Min(f.Right, px));

        private static double ClampY(double py, PlotFrame f) => double.IsNaN(py) ? py : Math.Max(f.Top, Math.Min(f.Bottom, py));

        // Held to ±1e9 first: a frame far outside the image (Left = −1e12) wrapped the cast, or threw under checked arithmetic.
        private static void FillClipped(ImageBuffer image, int x, int y, int w, int h, Rgba color, PlotFrame f)
        {
            int x0 = Math.Max(x, Px(f.Left)), y0 = Math.Max(y, Px(f.Top));
            int x1 = Math.Min(x + w - 1, Px(f.Right)), y1 = Math.Min(y + h - 1, Px(f.Bottom));
            if (x1 >= x0 && y1 >= y0) image.DrawRectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1, color, filled: true);
        }

        // The first pixel centre at or after v, and the last before v (at v when v is the plot's far edge), held to ±1e9.
        private static int First(double v) => (int)Math.Ceiling(Math.Max(-1e9, Math.Min(1e9, v)));

        private static int Last(double v, double farEdge)
        {
            double c = Math.Max(-1e9, Math.Min(1e9, v)), fl = Math.Floor(c);
            return (int)(fl == c && v < farEdge ? fl - 1 : fl);
        }

        private static int Px(double v) => (int)Math.Round(Math.Max(-1e9, Math.Min(1e9, v)), MidpointRounding.AwayFromZero);

        // Capped before the cast: 1e10 wrapped to int.MinValue (a 1-px line), or threw under checked arithmetic.
        private static int Thickness(double width) => Math.Max(1, (int)Math.Round(Math.Min(width, 1e6), MidpointRounding.AwayFromZero));

        /// <summary>
        /// The pen square at (<paramref name="cx"/>, <paramref name="cy"/>), clipped to the image. After a drawn step only
        /// the part the previous square did not cover is painted: the whole square cost thickness² per step, so a
        /// 2000-px line on a 1000 × 1000 image ran for minutes.
        /// </summary>
        private static void Stamp(ImageBuffer image, int cx, int cy, int half, int thickness, Rgba color, bool hasPrevious, int px, int py)
        {
            int xa = Math.Max(0, cx - half), xb = Math.Min(image.Width - 1, cx - half + thickness - 1);
            int ya = Math.Max(0, cy - half), yb = Math.Min(image.Height - 1, cy - half + thickness - 1);
            int oxa = px - half, oxb = px - half + thickness - 1, oya = py - half, oyb = py - half + thickness - 1;
            for (int y = ya; y <= yb; y++)
            {
                bool covered = hasPrevious && y >= oya && y <= oyb;
                for (int x = xa; x <= xb; x++)
                {
                    if (covered && x >= oxa && x <= oxb) { x = oxb; continue; }
                    image.SetPixel(x, y, color);
                }
            }
        }

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
