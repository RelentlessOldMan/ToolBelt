// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs, LinePlot.cs, PlotFrame.cs, AxisTicks.cs, SvgUtils.cs,
// Annotations.cs and SvgChart.cs.
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Visualization
{
    /// <summary>One panel of a <see cref="MultiPanel"/> figure: its series and (optional) chart options.</summary>
    public sealed class ChartPanel
    {
        public ChartPanel(IReadOnlyList<Series> series, SvgChartOptions? options = null)
        {
            Series = series ?? throw new ArgumentNullException(nameof(series));
            Options = options;
        }

        public IReadOnlyList<Series> Series { get; }
        public SvgChartOptions? Options { get; }
    }

    /// <summary>Options for <see cref="MultiPanel"/>.</summary>
    public sealed class MultiPanelOptions
    {
        /// <summary>Panels per row; panels fill row by row.</summary>
        public int Columns { get; set; } = 1;
        /// <summary>
        /// The size of a fully labelled panel. Panels in a row or column whose shared-axis labels are hidden are
        /// trimmed by the space those labels would have taken, so every plot area keeps the same size.
        /// </summary>
        public double PanelWidth { get; set; } = 560;
        public double PanelHeight { get; set; } = 240;

        /// <summary>Space between panels and around the figure.</summary>
        public double Gap { get; set; } = 8;

        /// <summary>A common title above all panels.</summary>
        public string? Title { get; set; }

        /// <summary>
        /// Give every panel the same x range (the union of their auto or explicit ranges) and show x tick labels and
        /// the x-axis title only on the bottom panel of each column.
        /// </summary>
        public bool ShareX { get; set; }

        /// <summary>Same for y: one common range, labels only on the first panel of each row.</summary>
        public bool ShareY { get; set; }

        public double FontSize { get; set; } = 13;
        public string FontFamily { get; set; } = "sans-serif";
        public Rgba Background { get; set; } = Rgba.White;
    }

    /// <summary>
    /// Composes several plots into one figure. <see cref="RenderSvg"/> lays <see cref="SvgChart"/> panels out on a
    /// grid under a common title, with optional shared x and/or y axes: a shared axis gets one common range and
    /// its tick labels only on the outer panels, and every panel's plot area is aligned to the same margins so
    /// shared axes genuinely line up. <see cref="Layout"/> returns each panel's <see cref="PlotFrame"/> in figure
    /// coordinates (for overlays). <see cref="Compose"/> is the raster counterpart: it tiles pre-rendered images
    /// onto a grid separated by background-colored gutters. The caller's options objects are never modified.
    /// </summary>
    public static class MultiPanel
    {
        /// <summary>A complete standalone SVG document holding every panel.</summary>
        public static string RenderSvg(IReadOnlyList<ChartPanel> panels, MultiPanelOptions? options = null)
            => "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + RenderSvgElement(panels, options);

        /// <summary>The figure as a bare <c>&lt;svg&gt;</c> element (for inline HTML).</summary>
        public static string RenderSvgElement(IReadOnlyList<ChartPanel> panels, MultiPanelOptions? options = null)
        {
            options ??= new MultiPanelOptions();
            var plan = Plan(panels, options);
            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(SvgUtils.Number(plan.Width))
              .Append("\" height=\"").Append(SvgUtils.Number(plan.Height)).Append("\" viewBox=\"0 0 ")
              .Append(SvgUtils.Number(plan.Width)).Append(' ').Append(SvgUtils.Number(plan.Height))
              .Append("\" font-family=\"").Append(SvgUtils.EscapeAttribute(options.FontFamily)).Append("\">\n");
            sb.Append("<rect width=\"").Append(SvgUtils.Number(plan.Width)).Append("\" height=\"").Append(SvgUtils.Number(plan.Height))
              .Append("\" ").Append(SvgUtils.FillAttributes(options.Background)).Append("/>\n");
            if (!string.IsNullOrEmpty(options.Title))
            {
                sb.Append("<text x=\"").Append(SvgUtils.Number(plan.Width / 2)).Append("\" y=\"")
                  .Append(SvgUtils.Number(options.Gap + options.FontSize * 1.25)).Append("\" text-anchor=\"middle\" font-weight=\"bold\" font-size=\"")
                  .Append(SvgUtils.Number(options.FontSize * 1.25)).Append("\" fill=\"#000000\">")
                  .Append(SvgUtils.EscapeText(options.Title!)).Append("</text>\n");
            }
            for (int i = 0; i < panels.Count; i++)
                sb.Append(SvgChart.RenderElement(panels[i].Series, plan.Options[i], plan.X[i], plan.Y[i]));
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        /// <summary>Each panel's plot frame in figure coordinates, in panel order.</summary>
        public static IReadOnlyList<PlotFrame> Layout(IReadOnlyList<ChartPanel> panels, MultiPanelOptions? options = null)
        {
            options ??= new MultiPanelOptions();
            var plan = Plan(panels, options);
            var frames = new PlotFrame[panels.Count];
            for (int i = 0; i < panels.Count; i++)
            {
                PlotFrame f = SvgChart.Layout(panels[i].Series, plan.Options[i]);
                frames[i] = f.WithArea(f.Left + plan.X[i], f.Top + plan.Y[i], f.Width, f.Height);
            }
            return frames;
        }

        /// <summary>
        /// Tiles raster panels onto a grid, row by row: each cell is as large as the largest panel, panels sit at their
        /// cell's top-left, and <paramref name="gap"/>-pixel gutters (also around the edge) are filled with
        /// <paramref name="background"/>, which doubles as the divider color.
        /// </summary>
        public static ImageBuffer Compose(IReadOnlyList<ImageBuffer> panels, int columns = 1, int gap = 4, Rgba? background = null)
        {
            if (panels is null) throw new ArgumentNullException(nameof(panels));
            if (panels.Count == 0) throw new ArgumentException("At least one panel is required.", nameof(panels));
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns), columns, "Columns must be positive.");
            if (gap < 0) throw new ArgumentOutOfRangeException(nameof(gap), gap, "Gap must be non-negative.");
            int cellW = 0, cellH = 0;
            for (int i = 0; i < panels.Count; i++)
            {
                if (panels[i] is null) throw new ArgumentException($"Panel {i} is null.", nameof(panels));
                cellW = Math.Max(cellW, panels[i].Width);
                cellH = Math.Max(cellH, panels[i].Height);
            }
            int cols = Math.Min(columns, panels.Count), rows = (panels.Count + cols - 1) / cols;
            var image = new ImageBuffer(cols * cellW + (cols + 1) * gap, rows * cellH + (rows + 1) * gap, background ?? Rgba.White);
            for (int i = 0; i < panels.Count; i++)
                image.Blit(panels[i], gap + i % cols * (cellW + gap), gap + i / cols * (cellH + gap));
            return image;
        }

        // ---------- planning ----------

        private sealed class FigurePlan
        {
            public SvgChartOptions[] Options = null!;
            public double[] X = null!;
            public double[] Y = null!;
            public double Width;
            public double Height;
        }

        private static FigurePlan Plan(IReadOnlyList<ChartPanel> panels, MultiPanelOptions o)
        {
            if (panels is null) throw new ArgumentNullException(nameof(panels));
            if (panels.Count == 0) throw new ArgumentException("At least one panel is required.", nameof(panels));
            if (o.Columns < 1) throw new ArgumentOutOfRangeException(nameof(o), "Columns must be positive.");
            if (!(o.PanelWidth > 0) || !(o.PanelHeight > 0)) throw new ArgumentOutOfRangeException(nameof(o), "Panel size must be positive.");
            if (!(o.Gap >= 0)) throw new ArgumentOutOfRangeException(nameof(o), "Gap must be non-negative.");
            for (int i = 0; i < panels.Count; i++)
                if (panels[i] is null) throw new ArgumentException($"Panel {i} is null.", nameof(panels));

            int n = panels.Count, cols = Math.Min(o.Columns, n), rows = (n + cols - 1) / cols;
            var opts = new SvgChartOptions[n];
            for (int i = 0; i < n; i++)
            {
                opts[i] = (panels[i].Options ?? new SvgChartOptions()).Clone();
                opts[i].Width = o.PanelWidth;
                opts[i].Height = o.PanelHeight;
            }

            // Shared ranges: the union of what each panel would show on its own.
            if (o.ShareX || o.ShareY)
            {
                double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity;
                double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
                for (int i = 0; i < n; i++)
                {
                    PlotFrame f = SvgChart.Layout(panels[i].Series, opts[i]);
                    xMin = Math.Min(xMin, f.XMin); xMax = Math.Max(xMax, f.XMax);
                    yMin = Math.Min(yMin, f.YMin); yMax = Math.Max(yMax, f.YMax);
                }
                for (int i = 0; i < n; i++)
                {
                    int row = i / cols, col = i % cols;
                    if (o.ShareX)
                    {
                        opts[i].XMin = xMin; opts[i].XMax = xMax;
                        bool bottomOfColumn = row == rows - 1 || i + cols >= n;
                        if (!bottomOfColumn) { opts[i].ShowXTickLabels = false; opts[i].XLabel = null; }
                    }
                    if (o.ShareY)
                    {
                        opts[i].YMin = yMin; opts[i].YMax = yMax;
                        if (col != 0) { opts[i].ShowYTickLabels = false; opts[i].YLabel = null; }
                    }
                }
            }

            // Figure-level layout. Every plot area gets one common size; each column takes the left/right margins its
            // panels need and each row its top/bottom margins, so a row or column without tick labels shrinks rather
            // than reserving empty space. Panels sharing a column (or row) get identical margins and sizes, so their
            // plot areas line up exactly along that axis.
            var colL = new double[cols]; var colR = new double[cols];
            var rowT = new double[rows]; var rowB = new double[rows];
            for (int i = 0; i < n; i++)
            {
                PlotFrame f = SvgChart.Layout(panels[i].Series, opts[i]);
                int r = i / cols, c = i % cols;
                colL[c] = Math.Max(colL[c], f.Left);                       // read directly: exact
                rowT[r] = Math.Max(rowT[r], f.Top);
                colR[c] = Math.Max(colR[c], o.PanelWidth - f.Right);       // size − edge: carries rounding noise
                rowB[r] = Math.Max(rowB[r], o.PanelHeight - f.Bottom);
            }
            double maxH = 0, maxV = 0;
            for (int c = 0; c < cols; c++) { colR[c] = CeilMicro(colR[c]); maxH = Math.Max(maxH, colL[c] + colR[c]); }
            for (int r = 0; r < rows; r++) { rowB[r] = CeilMicro(rowB[r]); maxV = Math.Max(maxV, rowT[r] + rowB[r]); }
            double plotW = o.PanelWidth - maxH, plotH = o.PanelHeight - maxV;
            if (plotW < 20 || plotH < 20)
                throw new ArgumentException("Panels are too small for their labels; increase PanelWidth/PanelHeight.");

            var colW = new double[cols]; var rowH = new double[rows];
            for (int c = 0; c < cols; c++) colW[c] = colL[c] + plotW + colR[c];
            for (int r = 0; r < rows; r++) rowH[r] = rowT[r] + plotH + rowB[r];
            for (int i = 0; i < n; i++)
            {
                int r = i / cols, c = i % cols;
                opts[i].Width = colW[c]; opts[i].Height = rowH[r];
                opts[i].MinMarginLeft = colL[c]; opts[i].MinMarginRight = colR[c];
                opts[i].MinMarginTop = rowT[r]; opts[i].MinMarginBottom = rowB[r];
            }

            var colX = new double[cols]; var rowY = new double[rows];
            double titleH = string.IsNullOrEmpty(o.Title) ? 0 : o.FontSize * 1.25 + o.Gap;
            double x = o.Gap, y = titleH + o.Gap;
            for (int c = 0; c < cols; c++) { colX[c] = x; x += colW[c] + o.Gap; }
            for (int r = 0; r < rows; r++) { rowY[r] = y; y += rowH[r] + o.Gap; }

            var plan = new FigurePlan { Options = opts, X = new double[n], Y = new double[n], Width = x, Height = y };
            for (int i = 0; i < n; i++)
            {
                plan.X[i] = colX[i % cols];
                plan.Y[i] = rowY[i / cols];
            }
            return plan;
        }

        private static double CeilMicro(double v) => Math.Ceiling(v * 1e6) / 1e6;
    }
}
