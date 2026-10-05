using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    /// <summary>HexBin, ScatterMatrix and TimingDiagram.</summary>
    public sealed class HexScatterTimingTests
    {
        private static XDocument Parse(string svg) => XDocument.Parse(svg);                // well-formed XML or throws

        private static int Count(string svg, string element) => Regex.Matches(svg, "<" + element + "[ >]").Count;

        // ---------- hexbin ----------

        public void HexBin_EveryPointGoesToItsNearestCentre()
        {
            var rng = new DeterministicRandom(1);
            var frame = new PlotFrame(10, 20, 400, 300, -5, 5, -3, 3);
            const double r = 7;
            double dx = r * Math.Sqrt(3), dy = r * 1.5;
            for (int trial = 0; trial < 3000; trial++)
            {
                var p = (rng.NextDouble() * 10 - 5, rng.NextDouble() * 6 - 3);
                HexCell cell = HexBin.Compute(new[] { p }, frame, r).Single();
                double px = frame.MapX(p.Item1) - frame.Left, py = frame.MapY(p.Item2) - frame.Top;
                // Brute force over a neighbourhood of candidate centres.
                double best = double.PositiveInfinity;
                int j0 = (int)Math.Floor(py / dy);
                for (int j = j0 - 2; j <= j0 + 3; j++)
                    for (int i = (int)(px / dx) - 3; i <= (int)(px / dx) + 3; i++)
                    {
                        var (cx, cy) = HexBin.Center(i, j, dx, dy);
                        best = Math.Min(best, (px - cx) * (px - cx) + (py - cy) * (py - cy));
                    }
                double got = Math.Pow(px - (cell.CenterX - frame.Left), 2) + Math.Pow(py - (cell.CenterY - frame.Top), 2);
                Check.Close(best, got, 1e-9, $"point {p}");
                Check.True(Math.Sqrt(got) <= r + 1e-9, "a point lies within one circumradius of its hexagon's centre");
            }
        }

        public void HexBin_CountsAreConservedAndCentresMapBack()
        {
            var rng = new DeterministicRandom(2);
            var pts = Enumerable.Range(0, 5000).Select(_ => (rng.NextGaussian(), rng.NextGaussian())).ToList();
            pts.Add((double.NaN, 1));
            var frame = new PlotFrame(0, 0, 300, 300, -4, 4, -4, 4);
            var cells = HexBin.Compute(pts, frame, 8);
            Check.Equal(5000, cells.Sum(c => c.Count));                                // the NaN point is skipped
            foreach (var c in cells.Take(20))
            {
                Check.Close(c.CenterX, frame.MapX(c.DataX), 1e-9);
                Check.Close(c.CenterY, frame.MapY(c.DataY), 1e-9);
            }
            Check.Throws<ArgumentOutOfRangeException>(() => HexBin.Compute(pts, frame, 0));
        }

        public void HexBin_SvgIsWellFormedWithOneHexPerVisibleCell()
        {
            var pts = new List<(double, double)> { (0, 0), (0, 0), (0, 0), (1, 1), (5, 5) };
            string svg = HexBin.RenderSvg(pts, new HexBinOptions { Title = "T & <x>", Radius = 12 });
            Parse(svg);
            Check.True(svg.Contains("T &amp; &lt;x&gt;"));
            Check.Equal(3, Regex.Matches(svg, "<path d=\"M[^\"]*Z\" fill").Count);                // incl. the corner cell, clipped
            Check.True(svg.Contains("<title>3</title>"), "the densest hexagon carries its count");
            Parse(HexBin.RenderSvg(new List<(double, double)>()));                    // empty data still renders
        }

        // ---------- scatter matrix ----------

        public void ScatterMatrix_LayoutAndCorrelation()
        {
            double[] a = { 1, 2, 3, 4, 5, double.NaN };
            double[] b = { 2, 4, 6, 8, 10, 3 };
            double[] c = { 5, 3, 4, 1, 2, 0 };
            string svg = ScatterMatrix.RenderSvg(new[] { "a", "b", "c" }, new[] { a, b, c },
                new ScatterMatrixOptions { Groups = new[] { "g1", "g2", "g1", "g2", "g1", "g2" } });
            Parse(svg);
            // 3x3 = 6 off-diagonal panels; each plots the rows finite in both columns.
            int expectedPoints = 2 * (5 + 5 + 6);                                      // (a,b) (a,c) (b,c), both orders
            Check.Equal(expectedPoints + 2, Count(svg, "circle"));                    // + 2 legend dots
            Check.True(svg.Contains("r = 1.00"), "a and b are perfectly correlated over finite rows");
            Check.Close(1, ScatterMatrix.Pearson(a, b)!.Value, 1e-12);
            Check.Close(-0.8, ScatterMatrix.Pearson(new double[] { 1, 2, 3, 4, 5 }, new double[] { 5, 3, 4, 1, 2 })!.Value, 1e-12);   // Σdxdy = −8, Σdx² = Σdy² = 10
            Check.Null(ScatterMatrix.Pearson(new double[] { 1, 1, 1 }, new double[] { 1, 2, 3 }));
            Check.Throws<ArgumentException>(() => ScatterMatrix.RenderSvg(new[] { "a", "b" }, new[] { a, new double[] { 1 } }));
        }

        // ---------- timing diagram ----------

        public void Timing_SegmentsFollowTheChanges()
        {
            var s = TimingSignal.Bus("B", new (double, string?)[] { (1, "A"), (3, "B"), (3, "C"), (5, "C"), (7, null) });
            Check.Equal("[0..1:?][1..3:A][3..7:C][7..9:?]",
                string.Concat(TimingDiagram.Segments(s, 0, 9).Select(g => $"[{g.Start}..{g.End}:{g.Value ?? "?"}]")));   // equal times: last wins; repeats merged
            Check.Equal("[2..3:A][3..4:C]", string.Concat(TimingDiagram.Segments(s, 2, 4).Select(g => $"[{g.Start}..{g.End}:{g.Value ?? "?"}]")));
        }

        public void Timing_ClockAndSamples()
        {
            var clk = TimingSignal.Clock("CLK", 2, 0, 6, duty: 0.25);
            Check.Equal("0:1 0.5:0 2:1 2.5:0 4:1 4.5:0", string.Join(" ", clk.Changes.Select(c => $"{c.Time}:{c.Value}")));
            var fromSamples = TimingSignal.FromSamples("D", new double[] { 0.5, 0.9, 0.5, 0.1, 0.45, 0.6, 0.95 }, sampleRate: 1, lowThreshold: 0.3, highThreshold: 0.7);
            // Unknown until the first sample outside the band; the hysteresis band holds the level.
            Check.Equal("0:? 1:1 3:0 6:1", string.Join(" ", fromSamples.Changes.Select(c => $"{c.Time}:{c.Value ?? "?"}")));
        }

        public void Timing_SvgRendersLanesLabelsAndMarkers()
        {
            var sigs = new[]
            {
                TimingSignal.Clock("CLK", 1, 0, 10),
                TimingSignal.Bus("DATA", new (double, string?)[] { (0, null), (2, "0xAB"), (5, "a value far too long to fit"), (5.2, "0x01") }),
            };
            string svg = TimingDiagram.RenderSvg(sigs, new TimingDiagramOptions { Markers = new[] { new TimingMarker(2, "t<sub>") } });
            Parse(svg);
            Check.True(svg.Contains(">CLK<") && svg.Contains(">DATA<"));
            Check.True(svg.Contains(">0xAB<") && svg.Contains(">0x01<"));
            Check.False(svg.Contains("far too long"), "a label wider than its segment is dropped");
            Check.True(svg.Contains("t&lt;sub&gt;"));
            Check.Equal("1.5 ms", TimingDiagram.FormatSeconds(0.0015));
            Check.Equal("2 µs", TimingDiagram.FormatSeconds(2e-6));
            Check.Equal("-3 ns", TimingDiagram.FormatSeconds(-3e-9));
            Check.Throws<ArgumentException>(() => TimingDiagram.RenderSvg(Array.Empty<TimingSignal>()));
        }
    }
}
