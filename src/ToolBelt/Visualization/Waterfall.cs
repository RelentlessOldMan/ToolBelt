// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs, Colormap.cs, Colorbar.cs, HeatMap.cs, PngWriter.cs,
// PlotFrame.cs, AxisTicks.cs, SvgUtils.cs and Annotations.cs.
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Visualization
{
    /// <summary>Options for <see cref="Waterfall"/>.</summary>
    public sealed class WaterfallOptions
    {
        /// <summary>Centre frequency of column 0.</summary>
        public double FrequencyStart { get; set; }

        /// <summary>Frequency spacing between adjacent columns (bin width); must be positive.</summary>
        public double FrequencyStep { get; set; } = 1;

        /// <summary>Start time of row 0, in seconds.</summary>
        public double TimeStart { get; set; }

        /// <summary>Duration of one row, in seconds; must be positive.</summary>
        public double TimeStep { get; set; } = 1;

        /// <summary>Draw the newest row (the last) at the top, scrolling history downward — the usual waterfall view.</summary>
        public bool NewestAtTop { get; set; } = true;

        public Colormap Colormap { get; set; } = Colormap.Viridis;

        /// <summary>Color-scale limits; any left null come from the finite data values.</summary>
        public double? ValueMin { get; set; }
        public double? ValueMax { get; set; }

        /// <summary>Color for NaN cells (transparent lets the background show through).</summary>
        public Rgba NanColor { get; set; } = Rgba.Transparent;

        // ----- vector output -----
        public double Width { get; set; } = 720;
        public double Height { get; set; } = 420;
        public string? Title { get; set; }
        public string FrequencyLabel { get; set; } = "Frequency";
        public string TimeLabel { get; set; } = "Time";

        /// <summary>Title of the color scale, e.g. "dB".</summary>
        public string? ValueLabel { get; set; }

        public int MaxFrequencyTicks { get; set; } = 8;
        public int MaxTimeTicks { get; set; } = 6;
        public double FontSize { get; set; } = 12;
        public string FontFamily { get; set; } = "sans-serif";
        public Rgba Background { get; set; } = Rgba.White;
        public Rgba AxisColor { get; set; } = new Rgba(64, 64, 64);

        /// <summary>Overlays (e.g. a vertical line at a carrier frequency); drawn in frequency/time coordinates.</summary>
        public Annotations? Annotations { get; set; }

        // ----- raster output -----
        /// <summary>Pixels per cell in <see cref="Waterfall.RenderImage"/>.</summary>
        public int CellSize { get; set; } = 1;

        /// <summary>Append a color-scale strip to the right of the raster image.</summary>
        public bool RasterColorbar { get; set; } = true;
        public int ColorbarWidth { get; set; } = 16;
    }

    /// <summary>
    /// Renders a time × frequency matrix — a spectrogram or any rolling series of spectra, row <c>r</c> being the
    /// spectrum at time <c>TimeStart + r·TimeStep</c> and column <c>c</c> the bin centred on
    /// <c>FrequencyStart + c·FrequencyStep</c> — as a waterfall: color-mapped cells with a frequency axis, a time axis
    /// (clock-formatted) and a labelled color scale.
    /// <para>
    /// <see cref="RenderSvg"/> embeds the cells as a PNG (pixel-exact, one pixel per cell, stretched with nearest-
    /// neighbour rendering) inside vector axes, so even large spectrograms stay compact; the color scale is drawn
    /// from plain rectangles (no gradient ids), so many figures can share a page. <see cref="RenderImage"/> gives the
    /// portable raster form — the cells plus an optional color strip, no text — ready for <see cref="PngWriter"/>.
    /// Auto value ranges skip NaN and infinities (−∞ is what a dB spectrum of an exact zero gives), and non-finite
    /// cells are drawn with <see cref="WaterfallOptions.NanColor"/> (NaN) or the end colors (±∞).
    /// </para>
    /// </summary>
    public static class Waterfall
    {
        private const int ScaleSteps = 64;

        /// <summary>A complete standalone SVG document.</summary>
        public static string RenderSvg(double[,] data, WaterfallOptions? options = null)
            => "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + RenderSvgElement(data, options);

        /// <summary>A bare <c>&lt;svg&gt;</c> element (no XML declaration), for inline HTML or nesting.</summary>
        public static string RenderSvgElement(double[,] data, WaterfallOptions? options = null)
        {
            options ??= new WaterfallOptions();
            var l = ComputeLayout(data, options);
            PlotFrame f = l.Frame;
            double fs = options.FontSize;
            var sb = new StringBuilder();

            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"")
              .Append(SvgUtils.Number(options.Width)).Append("\" height=\"").Append(SvgUtils.Number(options.Height))
              .Append("\" viewBox=\"0 0 ").Append(SvgUtils.Number(options.Width)).Append(' ').Append(SvgUtils.Number(options.Height))
              .Append("\" font-family=\"").Append(SvgUtils.EscapeAttribute(options.FontFamily))
              .Append("\" font-size=\"").Append(SvgUtils.Number(fs)).Append("\">\n");
            sb.Append("<rect width=\"").Append(SvgUtils.Number(options.Width)).Append("\" height=\"").Append(SvgUtils.Number(options.Height))
              .Append("\" ").Append(SvgUtils.FillAttributes(options.Background)).Append("/>\n");

            // The cells: one pixel each, stretched over the plot area without smoothing.
            string png = Convert.ToBase64String(PngWriter.Encode(CellImage(data, options, l.VMin, l.VMax, cell: 1)));
            sb.Append("<image x=\"").Append(SvgUtils.Number(f.Left)).Append("\" y=\"").Append(SvgUtils.Number(f.Top))
              .Append("\" width=\"").Append(SvgUtils.Number(f.Width)).Append("\" height=\"").Append(SvgUtils.Number(f.Height))
              .Append("\" preserveAspectRatio=\"none\" image-rendering=\"pixelated\" style=\"image-rendering:pixelated;image-rendering:crisp-edges\"")
              .Append(" href=\"data:image/png;base64,").Append(png).Append("\" xlink:href=\"data:image/png;base64,").Append(png).Append("\"/>\n");

            if (options.Annotations != null)
                sb.Append(options.Annotations.ToSvg(f, fs * 0.9, options.FontFamily, AnnotationLayer.All));

            Axes(sb, l, options);
            ColorScale(sb, l, options);
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        /// <summary>
        /// The raster form: each cell as a <see cref="WaterfallOptions.CellSize"/>-pixel square (newest row on top when
        /// <see cref="WaterfallOptions.NewestAtTop"/>), plus a 4-pixel gutter and a color strip when
        /// <see cref="WaterfallOptions.RasterColorbar"/> is set. No text — use <see cref="RenderSvg"/> for labelled output.
        /// </summary>
        public static ImageBuffer RenderImage(double[,] data, WaterfallOptions? options = null)
        {
            options ??= new WaterfallOptions();
            Validate(data, options);
            if (options.CellSize < 1) throw new ArgumentOutOfRangeException(nameof(options), "CellSize must be at least 1.");
            var (vMin, vMax) = ValueRange(data, options);
            ImageBuffer cells = CellImage(data, options, vMin, vMax, options.CellSize);
            if (!options.RasterColorbar) return cells;
            if (options.ColorbarWidth < 1) throw new ArgumentOutOfRangeException(nameof(options), "ColorbarWidth must be at least 1.");

            const int gutter = 4;
            var image = new ImageBuffer(cells.Width + gutter + options.ColorbarWidth, cells.Height, options.Background);
            image.Blit(cells, 0, 0);
            image.Blit(Colorbar.Render(options.ColorbarWidth, cells.Height, options.Colormap), cells.Width + gutter, 0);
            return image;
        }

        /// <summary>The plot area and data ranges (frequency on x, seconds on y) the SVG uses — for placing overlays.</summary>
        public static PlotFrame Layout(double[,] data, WaterfallOptions? options = null)
            => ComputeLayout(data, options ?? new WaterfallOptions()).Frame;

        /// <summary>The color-scale limits: explicit options, else the finite data minimum and maximum.</summary>
        public static (double Min, double Max) ValueRange(double[,] data, WaterfallOptions? options = null)
        {
            options ??= new WaterfallOptions();
            if (data is null) throw new ArgumentNullException(nameof(data));
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            if (options.ValueMin is null || options.ValueMax is null)
                foreach (double v in data)
                {
                    if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            min = options.ValueMin ?? min;
            max = options.ValueMax ?? max;
            if (double.IsInfinity(min) || double.IsInfinity(max)) { min = 0; max = 1; } // no finite data at all
            if (min > max) (min, max) = (max, min);
            return (min, max);
        }

        // ---------- internals ----------

        private sealed class WaterfallLayout
        {
            public PlotFrame Frame;
            public TickSet FreqTicks = null!;
            public TickSet TimeTicks = null!;
            public TickSet ValueTicks = null!;
            public double VMin, VMax;
            public double BarLeft;
        }

        private const double BarGap = 12, BarWidth = 14;

        private static WaterfallLayout ComputeLayout(double[,] data, WaterfallOptions o)
        {
            Validate(data, o);
            if (!(o.Width > 0) || !(o.Height > 0) || !(o.FontSize > 0))
                throw new ArgumentOutOfRangeException(nameof(o), "Width, Height and FontSize must be positive.");
            int rows = data.GetLength(0), cols = data.GetLength(1);
            double fLo = o.FrequencyStart - o.FrequencyStep / 2, fHi = o.FrequencyStart + (cols - 0.5) * o.FrequencyStep;
            double tLo = o.TimeStart, tHi = o.TimeStart + rows * o.TimeStep;
            var (vMin, vMax) = ValueRange(data, o);

            TickSet fT = AxisTicks.Linear(fLo, fHi, o.MaxFrequencyTicks, loose: false);
            TickSet tT = AxisTicks.Time(tLo, tHi, o.MaxTimeTicks, loose: false);
            TickSet vT = AxisTicks.Linear(vMin, vMax, 6, loose: false);

            double fs = o.FontSize;
            double timeLabelW = 0, valueLabelW = 0;
            foreach (string s in tT.Labels) timeLabelW = Math.Max(timeLabelW, SvgUtils.EstimateTextWidth(s, fs));
            foreach (string s in vT.Labels) valueLabelW = Math.Max(valueLabelW, SvgUtils.EstimateTextWidth(s, fs));

            double top = 12 + (string.IsNullOrEmpty(o.Title) ? 0 : fs * 1.35 + 8);
            double left = 10 + (string.IsNullOrEmpty(o.TimeLabel) ? 0 : fs + 8) + timeLabelW + 9;
            double bottom = 10 + fs + 8 + (string.IsNullOrEmpty(o.FrequencyLabel) ? 0 : fs + 8);
            double right = BarGap + BarWidth + 6 + valueLabelW + (string.IsNullOrEmpty(o.ValueLabel) ? 0 : fs + 10) + 10;
            double pw = o.Width - left - right, ph = o.Height - top - bottom;
            if (pw < 20 || ph < 20)
                throw new ArgumentException("The waterfall is too small for its labels; increase Width/Height or reduce FontSize.");

            // Newest at top: time increases upward (a normal y axis). Otherwise reverse the range so it grows downward.
            var frame = o.NewestAtTop
                ? new PlotFrame(left, top, pw, ph, fLo, fHi, tLo, tHi)
                : new PlotFrame(left, top, pw, ph, fLo, fHi, tHi, tLo);
            return new WaterfallLayout
            {
                Frame = frame, FreqTicks = fT, TimeTicks = tT, ValueTicks = vT, VMin = vMin, VMax = vMax,
                BarLeft = left + pw + BarGap,
            };
        }

        private static ImageBuffer CellImage(double[,] data, WaterfallOptions o, double vMin, double vMax, int cell)
        {
            int rows = data.GetLength(0), cols = data.GetLength(1);
            double[,] ordered = data;
            if (o.NewestAtTop)
            {
                ordered = new double[rows, cols];
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        ordered[r, c] = data[rows - 1 - r, c];
            }
            return HeatMap.Render(ordered, o.Colormap, new HeatMapOptions { CellSize = cell, Min = vMin, Max = vMax, NanColor = o.NanColor });
        }

        private static void Axes(StringBuilder sb, WaterfallLayout l, WaterfallOptions o)
        {
            PlotFrame f = l.Frame;
            double fs = o.FontSize;
            string axis = SvgUtils.StrokeAttributes(o.AxisColor, 1);
            string ink = SvgUtils.FillAttributes(o.AxisColor);
            sb.Append("<rect x=\"").Append(SvgUtils.Number(f.Left)).Append("\" y=\"").Append(SvgUtils.Number(f.Top))
              .Append("\" width=\"").Append(SvgUtils.Number(f.Width)).Append("\" height=\"").Append(SvgUtils.Number(f.Height))
              .Append("\" fill=\"none\" ").Append(axis).Append("/>\n");
            for (int i = 0; i < l.FreqTicks.Count; i++)
            {
                double px = f.MapX(l.FreqTicks.Values[i]);
                Tick(sb, px, f.Bottom, px, f.Bottom + 5, axis);
                Text(sb, px, f.Bottom + 7 + fs * 0.85, l.FreqTicks.Labels[i], "middle", ink);
            }
            for (int i = 0; i < l.TimeTicks.Count; i++)
            {
                double py = f.MapY(l.TimeTicks.Values[i]);
                Tick(sb, f.Left - 5, py, f.Left, py, axis);
                Text(sb, f.Left - 8, py + fs * 0.35, l.TimeTicks.Labels[i], "end", ink);
            }
            if (!string.IsNullOrEmpty(o.FrequencyLabel))
                Text(sb, f.Left + f.Width / 2, f.Bottom + fs + 8 + fs + 2, o.FrequencyLabel, "middle", ink);
            if (!string.IsNullOrEmpty(o.TimeLabel))
                Rotated(sb, 10 + fs * 0.8, f.Top + f.Height / 2, o.TimeLabel, ink);
            if (!string.IsNullOrEmpty(o.Title))
                sb.Append("<text x=\"").Append(SvgUtils.Number(o.Width / 2)).Append("\" y=\"").Append(SvgUtils.Number(10 + fs * 1.2))
                  .Append("\" text-anchor=\"middle\" font-weight=\"bold\" font-size=\"").Append(SvgUtils.Number(fs * 1.2)).Append("\" fill=\"#000000\">")
                  .Append(SvgUtils.EscapeText(o.Title!)).Append("</text>\n");
        }

        private static void ColorScale(StringBuilder sb, WaterfallLayout l, WaterfallOptions o)
        {
            PlotFrame f = l.Frame;
            double fs = o.FontSize, x = l.BarLeft, step = f.Height / ScaleSteps;
            sb.Append("<g class=\"colorscale\" shape-rendering=\"crispEdges\">\n");
            for (int k = 0; k < ScaleSteps; k++)
            {
                // Top rectangle carries the highest value.
                Rgba c = o.Colormap.Map((ScaleSteps - k - 0.5) / ScaleSteps);
                sb.Append("<rect x=\"").Append(SvgUtils.Number(x)).Append("\" y=\"").Append(SvgUtils.Number(f.Top + k * step))
                  .Append("\" width=\"").Append(SvgUtils.Number(BarWidth)).Append("\" height=\"").Append(SvgUtils.Number(step + 0.5))
                  .Append("\" ").Append(SvgUtils.FillAttributes(new Rgba(c.R, c.G, c.B))).Append("/>\n");
            }
            sb.Append("</g>\n");
            string axis = SvgUtils.StrokeAttributes(o.AxisColor, 1);
            string ink = SvgUtils.FillAttributes(o.AxisColor);
            sb.Append("<rect x=\"").Append(SvgUtils.Number(x)).Append("\" y=\"").Append(SvgUtils.Number(f.Top))
              .Append("\" width=\"").Append(SvgUtils.Number(BarWidth)).Append("\" height=\"").Append(SvgUtils.Number(f.Height))
              .Append("\" fill=\"none\" ").Append(axis).Append("/>\n");

            double range = l.VMax - l.VMin;
            if (!(range > 0))
            {
                // Flat data: every tick would land at the same height and the labels would overprint. Show the one value.
                double py = f.Bottom - 0.5 * f.Height;
                Tick(sb, x + BarWidth, py, x + BarWidth + 4, py, axis);
                Text(sb, x + BarWidth + 6, py + fs * 0.35, l.VMin.ToString("G4", System.Globalization.CultureInfo.InvariantCulture), "start", ink);
            }
            else
            {
                for (int i = 0; i < l.ValueTicks.Count; i++)
                {
                    double v = l.ValueTicks.Values[i];
                    double py = f.Bottom - (v - l.VMin) / range * f.Height;
                    Tick(sb, x + BarWidth, py, x + BarWidth + 4, py, axis);
                    Text(sb, x + BarWidth + 6, py + fs * 0.35, l.ValueTicks.Labels[i], "start", ink);
                }
            }
            if (!string.IsNullOrEmpty(o.ValueLabel))
                Rotated(sb, o.Width - 10 - fs * 0.4, f.Top + f.Height / 2, o.ValueLabel!, ink);
        }

        private static void Tick(StringBuilder sb, double x1, double y1, double x2, double y2, string stroke)
            => sb.Append("<line x1=\"").Append(SvgUtils.Number(x1)).Append("\" y1=\"").Append(SvgUtils.Number(y1))
                 .Append("\" x2=\"").Append(SvgUtils.Number(x2)).Append("\" y2=\"").Append(SvgUtils.Number(y2)).Append("\" ").Append(stroke).Append("/>\n");

        private static void Text(StringBuilder sb, double x, double y, string text, string anchor, string fill)
            => sb.Append("<text x=\"").Append(SvgUtils.Number(x)).Append("\" y=\"").Append(SvgUtils.Number(y))
                 .Append("\" text-anchor=\"").Append(anchor).Append("\" ").Append(fill).Append('>')
                 .Append(SvgUtils.EscapeText(text)).Append("</text>\n");

        private static void Rotated(StringBuilder sb, double cx, double cy, string text, string fill)
            => sb.Append("<text x=\"").Append(SvgUtils.Number(cx)).Append("\" y=\"").Append(SvgUtils.Number(cy))
                 .Append("\" text-anchor=\"middle\" transform=\"").Append(SvgUtils.Rotate(-90, cx, cy)).Append("\" ").Append(fill).Append('>')
                 .Append(SvgUtils.EscapeText(text)).Append("</text>\n");

        private static void Validate(double[,] data, WaterfallOptions o)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.GetLength(0) == 0 || data.GetLength(1) == 0) throw new ArgumentException("Data must be non-empty.", nameof(data));
            if (o.Colormap is null) throw new ArgumentException("Colormap must not be null.", nameof(o));
            if (!(o.FrequencyStep > 0) || double.IsInfinity(o.FrequencyStep))
                throw new ArgumentOutOfRangeException(nameof(o), "FrequencyStep must be positive and finite.");
            if (!(o.TimeStep > 0) || double.IsInfinity(o.TimeStep))
                throw new ArgumentOutOfRangeException(nameof(o), "TimeStep must be positive and finite.");
            if (double.IsNaN(o.FrequencyStart) || double.IsInfinity(o.FrequencyStart) || double.IsNaN(o.TimeStart) || double.IsInfinity(o.TimeStart))
                throw new ArgumentOutOfRangeException(nameof(o), "FrequencyStart and TimeStart must be finite.");
        }
    }
}
