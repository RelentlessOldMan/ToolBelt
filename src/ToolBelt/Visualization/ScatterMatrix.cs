// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs, PlotFrame.cs, AxisTicks.cs, SvgUtils.cs and Palettes.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ToolBelt.Visualization
{
    /// <summary>Options for <see cref="ScatterMatrix.RenderSvg"/>.</summary>
    public sealed class ScatterMatrixOptions
    {
        /// <summary>Size of each panel in pixels.</summary>
        public double CellSize { get; set; } = 150;

        public double Gap { get; set; } = 8;
        public double PointRadius { get; set; } = 1.8;
        public double PointOpacity { get; set; } = 0.6;

        /// <summary>Histogram bins on the diagonal.</summary>
        public int Bins { get; set; } = 20;

        /// <summary>Optional group per row (colours points and stacks nothing — histograms stay overall).</summary>
        public IReadOnlyList<string>? Groups { get; set; }

        public IReadOnlyList<Rgba> Palette { get; set; } = Palettes.OkabeItoNoBlack;

        /// <summary>Show Pearson r in the corner of every off-diagonal panel.</summary>
        public bool ShowCorrelation { get; set; } = true;

        public string? Title { get; set; }
    }

    /// <summary>
    /// A scatter-plot matrix ("pairs plot"): for k variables, a k×k grid where panel (row i, column j) plots variable j
    /// against variable i and the diagonal shows each variable's histogram — the first look at any multi-channel
    /// dataset, revealing correlations, clusters and outliers at once. Each column shares its x range and each row its y
    /// range (padded 5% so nothing sits on a panel edge), ticks are drawn only on the outer edges, points can be coloured by group (with a legend), and the
    /// Pearson correlation is printed in each panel. Rows with a non-finite value in a pair are skipped for that pair.
    /// </summary>
    public static class ScatterMatrix
    {
        public static string RenderSvg(IReadOnlyList<string> names, IReadOnlyList<IReadOnlyList<double>> columns, ScatterMatrixOptions? options = null)
        {
            if (names is null) throw new ArgumentNullException(nameof(names));
            if (columns is null) throw new ArgumentNullException(nameof(columns));
            int k = names.Count;
            if (k < 1 || columns.Count != k) throw new ArgumentException("Need one column per name (at least one).");
            if (columns.Any(c => c is null)) throw new ArgumentException("Columns must not be null.", nameof(columns));
            int n = columns[0].Count;
            if (columns.Any(c => c.Count != n)) throw new ArgumentException("All columns must have the same length.");
            var o = options ?? new ScatterMatrixOptions();
            if (o.Groups != null && o.Groups.Count != n) throw new ArgumentException("Groups must have one entry per row.");
            if (o.Groups != null && o.Groups.Any(g => g is null)) // threw from inside a dictionary as "key"
                throw new ArgumentException("Groups must not contain null labels.", nameof(options));
            // Checked here so a bad value names the option (they failed inside SvgUtils as "value" or "width"); Bins is
            // capped as each diagonal panel allocates them (int.MaxValue ran out of memory).
            if (o.Bins < 1 || o.Bins > 100_000) throw new ArgumentOutOfRangeException(nameof(options), "Bins must be in [1, 100000].");
            if (!(o.CellSize > 0) || o.CellSize > 1e6) throw new ArgumentOutOfRangeException(nameof(options), "CellSize must be in (0, 1e6].");
            if (!(o.Gap >= 0) || o.Gap > 1e6) throw new ArgumentOutOfRangeException(nameof(options), "Gap must be in [0, 1e6].");
            if (!(o.PointRadius >= 0) || o.PointRadius > 1e6) throw new ArgumentOutOfRangeException(nameof(options), "PointRadius must be in [0, 1e6].");
            if (!(o.PointOpacity >= 0 && o.PointOpacity <= 1)) throw new ArgumentOutOfRangeException(nameof(options), "PointOpacity must be in [0, 1].");

            var ticks = new TickSet[k];
            for (int v = 0; v < k; v++)
            {
                var finite = columns[v].Where(IsFinite).ToArray();
                double lo = finite.Length > 0 ? finite.Min() : 0, hi = finite.Length > 0 ? finite.Max() : 1;
                if (hi <= lo) { lo -= 0.5; hi += 0.5; }
                // Pad 5% and label only ticks strictly inside: "nice" outer ticks sit on the panel edges, where adjacent
                // panels' labels collide and points are half-clipped by the frame.
                // Halved, and clamped: (hi − lo) overflowed to ∞ for finite columns spanning ±1e308.
                double pad = 0.05 * hi - 0.05 * lo;
                ticks[v] = AxisTicks.Linear(Math.Max(-double.MaxValue, lo - pad), Math.Min(double.MaxValue, hi + pad), maxTicks: 6, loose: false);
            }
            var groupNames = o.Groups?.Distinct().ToArray() ?? Array.Empty<string>();
            var groupIndex = groupNames.Select((g, i) => (g, i)).ToDictionary(t => t.g, t => t.i);

            double left = 56, top = o.Title != null ? 34 : 12, bottom = 40, legendWidth = groupNames.Length > 0 ? 120 : 12;
            double size = o.CellSize, gap = o.Gap;
            double width = left + k * size + (k - 1) * gap + legendWidth, height = top + k * size + (k - 1) * gap + bottom;
            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(SvgUtils.Number(width)).Append("\" height=\"").Append(SvgUtils.Number(height))
              .Append("\" viewBox=\"0 0 ").Append(SvgUtils.Number(width)).Append(' ').Append(SvgUtils.Number(height))
              .Append("\" font-family=\"Helvetica, Arial, sans-serif\" font-size=\"10\">\n<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>\n");
            if (o.Title != null)
                sb.Append("<text x=\"").Append(SvgUtils.Number(left + (k * size + (k - 1) * gap) / 2)).Append("\" y=\"20\" text-anchor=\"middle\" font-size=\"14\" font-weight=\"bold\">")
                  .Append(SvgUtils.EscapeText(o.Title)).Append("</text>\n");

            for (int row = 0; row < k; row++)
            {
                for (int col = 0; col < k; col++)
                {
                    double x0 = left + col * (size + gap), y0 = top + row * (size + gap);
                    var xt = ticks[col];
                    sb.Append("<g>\n<rect x=\"").Append(SvgUtils.Number(x0)).Append("\" y=\"").Append(SvgUtils.Number(y0)).Append("\" width=\"")
                      .Append(SvgUtils.Number(size)).Append("\" height=\"").Append(SvgUtils.Number(size)).Append("\" fill=\"#fafafa\" stroke=\"#999\" stroke-width=\"0.75\"/>\n");
                    if (row == col)
                    {
                        AppendHistogram(sb, columns[col], xt, x0, y0, size, o.Bins);
                        sb.Append("<text x=\"").Append(SvgUtils.Number(x0 + 5)).Append("\" y=\"").Append(SvgUtils.Number(y0 + 13))
                          .Append("\" font-weight=\"bold\" font-size=\"11\">").Append(SvgUtils.EscapeText(names[col])).Append("</text>\n");
                    }
                    else
                    {
                        var yt = ticks[row];
                        var frame = new PlotFrame(x0, y0, size, size, xt.Min, xt.Max, yt.Min, yt.Max);
                        for (int i = 0; i < n; i++)
                        {
                            double xv = columns[col][i], yv = columns[row][i];
                            if (!IsFinite(xv) || !IsFinite(yv)) continue;
                            Rgba c = o.Groups != null ? Palettes.Cycle(o.Palette, groupIndex[o.Groups[i]]) : Palettes.Cycle(o.Palette, 0);
                            sb.Append("<circle cx=\"").Append(SvgUtils.Number(frame.MapX(xv))).Append("\" cy=\"").Append(SvgUtils.Number(frame.MapY(yv)))
                              .Append("\" r=\"").Append(SvgUtils.Number(o.PointRadius)).Append("\" fill=\"").Append(SvgUtils.Color(c))
                              .Append("\" fill-opacity=\"").Append(SvgUtils.Number(o.PointOpacity)).Append("\"/>\n");
                        }
                        if (o.ShowCorrelation && Pearson(columns[col], columns[row]) is double r)
                            sb.Append("<text x=\"").Append(SvgUtils.Number(x0 + size - 4)).Append("\" y=\"").Append(SvgUtils.Number(y0 + 12))
                              .Append("\" text-anchor=\"end\" fill=\"#555\" stroke=\"#fafafa\" stroke-width=\"3\" paint-order=\"stroke\">r = ").Append(r.ToString("0.00", CultureInfo.InvariantCulture)).Append("</text>\n");
                    }
                    sb.Append("</g>\n");

                    // Outer-edge ticks: bottom row gets x ticks, left column gets y ticks (diagonal panels use their x scale).
                    if (row == k - 1)
                        for (int t = 0; t < xt.Count; t++)
                        {
                            double px = x0 + (0.5 * xt.Values[t] - 0.5 * xt.Min) / (0.5 * xt.Max - 0.5 * xt.Min) * size; // halved: no ∞ span
                            sb.Append("<line x1=\"").Append(SvgUtils.Number(px)).Append("\" y1=\"").Append(SvgUtils.Number(y0 + size)).Append("\" x2=\"").Append(SvgUtils.Number(px))
                              .Append("\" y2=\"").Append(SvgUtils.Number(y0 + size + 4)).Append("\" stroke=\"#444\"/>\n<text x=\"").Append(SvgUtils.Number(px)).Append("\" y=\"")
                              .Append(SvgUtils.Number(y0 + size + 15)).Append("\" text-anchor=\"middle\">").Append(SvgUtils.EscapeText(xt.Labels[t])).Append("</text>\n");
                        }
                    if (col == 0 && row != 0)
                    {
                        var yt = ticks[row];
                        for (int t = 0; t < yt.Count; t++)
                        {
                            double py = y0 + size - (0.5 * yt.Values[t] - 0.5 * yt.Min) / (0.5 * yt.Max - 0.5 * yt.Min) * size;
                            sb.Append("<line x1=\"").Append(SvgUtils.Number(x0 - 4)).Append("\" y1=\"").Append(SvgUtils.Number(py)).Append("\" x2=\"").Append(SvgUtils.Number(x0))
                              .Append("\" y2=\"").Append(SvgUtils.Number(py)).Append("\" stroke=\"#444\"/>\n<text x=\"").Append(SvgUtils.Number(x0 - 6)).Append("\" y=\"")
                              .Append(SvgUtils.Number(py + 3.5)).Append("\" text-anchor=\"end\">").Append(SvgUtils.EscapeText(yt.Labels[t])).Append("</text>\n");
                        }
                    }
                }
                sb.Append("<text x=\"").Append(SvgUtils.Number(left + row * (size + gap) + size / 2)).Append("\" y=\"").Append(SvgUtils.Number(height - 6))
                  .Append("\" text-anchor=\"middle\" font-size=\"11\">").Append(SvgUtils.EscapeText(names[row])).Append("</text>\n");
            }

            if (groupNames.Length > 0)
            {
                double lx = left + k * size + (k - 1) * gap + 14, ly = top + 8;
                for (int g = 0; g < groupNames.Length; g++)
                    sb.Append("<circle cx=\"").Append(SvgUtils.Number(lx + 5)).Append("\" cy=\"").Append(SvgUtils.Number(ly + g * 16)).Append("\" r=\"4\" fill=\"")
                      .Append(SvgUtils.Color(Palettes.Cycle(o.Palette, g))).Append("\"/>\n<text x=\"").Append(SvgUtils.Number(lx + 14)).Append("\" y=\"")
                      .Append(SvgUtils.Number(ly + g * 16 + 3.5)).Append("\">").Append(SvgUtils.EscapeText(groupNames[g])).Append("</text>\n");
            }
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        private static void AppendHistogram(StringBuilder sb, IReadOnlyList<double> values, TickSet xt, double x0, double y0, double size, int bins)
        {
            var counts = new int[bins];
            double lo = xt.Min, hi = xt.Max;
            foreach (double v in values)
            {
                if (!IsFinite(v)) continue;
                int b = (int)((0.5 * v - 0.5 * lo) / (0.5 * hi - 0.5 * lo) * bins);
                counts[Math.Max(0, Math.Min(bins - 1, b))]++;
            }
            int max = Math.Max(1, counts.Max());
            double bw = size / bins, usable = size - 18;
            for (int b = 0; b < bins; b++)
            {
                if (counts[b] == 0) continue;
                double h = usable * counts[b] / max;
                sb.Append("<rect x=\"").Append(SvgUtils.Number(x0 + b * bw + 0.5)).Append("\" y=\"").Append(SvgUtils.Number(y0 + size - h)).Append("\" width=\"")
                  .Append(SvgUtils.Number(Math.Max(0.5, bw - 1))).Append("\" height=\"").Append(SvgUtils.Number(h)).Append("\" fill=\"#7f9fbf\"/>\n");
            }
        }

        /// <summary>Pearson correlation over rows where both values are finite (null if fewer than 3 such rows or no variance).</summary>
        public static double? Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            // r does not depend on scale, so the deviations are halved (no overflow for ±1e308) and divided by their
            // largest: the squares and Sqrt(sxx·syy) overflowed at 1e200 and underflowed to 0 at 1e-90 (r came out 1).
            int count = Math.Min(x.Count, y.Count), n = 0;
            for (int i = 0; i < count; i++) if (IsFinite(x[i]) && IsFinite(y[i])) n++;
            if (n < 3) return null;
            double mx = 0, my = 0;
            for (int i = 0; i < count; i++) if (IsFinite(x[i]) && IsFinite(y[i])) { mx += x[i] / n; my += y[i] / n; }
            double ax = 0, ay = 0;
            for (int i = 0; i < count; i++)
            {
                if (!IsFinite(x[i]) || !IsFinite(y[i])) continue;
                ax = Math.Max(ax, Math.Abs(x[i] / 2 - mx / 2));
                ay = Math.Max(ay, Math.Abs(y[i] / 2 - my / 2));
            }
            if (!(ax > 0) || !(ay > 0)) return null;
            double sxx = 0, syy = 0, sxy = 0;
            for (int i = 0; i < count; i++)
            {
                if (!IsFinite(x[i]) || !IsFinite(y[i])) continue;
                double dx = (x[i] / 2 - mx / 2) / ax, dy = (y[i] / 2 - my / 2) / ay;
                sxx += dx * dx; syy += dy * dy; sxy += dx * dy;
            }
            return sxx > 0 && syy > 0 ? Math.Max(-1, Math.Min(1, sxy / Math.Sqrt(sxx * syy))) : (double?)null;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
