using System;
using System.Globalization;
using System.Linq;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    // Regressions from review round 224 (charts, fourteenth pass): each test failed before its fix.
    public sealed class ChartsReview224Tests
    {
        // ---------- Colormap.MapFraction ----------

        // With every stop at one position, MapFraction(1) went through Map(EndPosition), which returns the first stop's
        // color for a position on the first stop, so 1 gave the first color as 0 did (Colorbar and HeatMap painted one).
        public void Colormap_MapFractionOne_IsTheLastStopEvenWhenAllStopsCoincide()
        {
            var rng = new Random(224);
            for (int trial = 0; trial < 500; trial++)
            {
                int n = rng.Next(2, 6);
                bool flat = trial % 2 == 0;
                double p = rng.NextDouble() * 100 - 50;
                var stops = Enumerable.Range(0, n).Select(i => (
                    flat ? p : p + i + rng.NextDouble() * 0.5,
                    new Rgba((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)))).ToArray();
                var map = new Colormap(stops);
                // The reference: 0 and below (and NaN) is the first stop given, 1 and above the last.
                Check.Equal(stops[0].Item2, map.MapFraction(0), "0");
                Check.Equal(stops[0].Item2, map.MapFraction(double.NaN), "NaN");
                Check.Equal(stops[n - 1].Item2, map.MapFraction(1), "1, flat " + flat);
                Check.Equal(stops[n - 1].Item2, map.MapFraction(7), "7");
            }
        }

        // ---------- TimingDiagram.FormatSeconds ----------

        private static readonly (string Unit, double Scale)[] Units = { ("ps", 1e-12), ("ns", 1e-9), ("µs", 1e-6), ("ms", 1e-3), ("s", 1) };

        // Reads a label back: (value in seconds, the number as printed, the unit, exponent form?).
        private static (double Value, double Number, string Unit, bool Exponent) Back(string label)
        {
            int sp = label.LastIndexOf(' ');
            string num = label.Substring(0, sp), unit = label.Substring(sp + 1);
            double x = double.Parse(num, NumberStyles.Float, CultureInfo.InvariantCulture);
            return (x * Units.First(u => u.Unit == unit).Scale, x, unit, num.Contains("e"));
        }

        // Below 1 ps the label kept three decimals of a picosecond, so few digits (5e-16 read "0.001 ps", twice its value),
        // and a value just under 1e15 s printed as "1000000000000000 s" where 1e15 printed "1e+15 s".
        public void FormatSeconds_EveryLabelHoldsFourDigitsAndStaysInItsForm()
        {
            Check.Equal("5e-16 s", TimingDiagram.FormatSeconds(5e-16));
            Check.Equal("1e+15 s", TimingDiagram.FormatSeconds(999999999999999.9));
            Check.Equal("1 ps", TimingDiagram.FormatSeconds(9.9999999e-13));
            Check.Equal("1 ps", TimingDiagram.FormatSeconds(1e-12));

            var rng = new Random(2240);
            for (int trial = 0; trial < 20000; trial++)
            {
                double v = Math.Pow(10, rng.NextDouble() * 40 - 22) * (rng.Next(2) == 0 ? 1 : -1);
                if (trial % 4 == 0) v = (rng.Next(2) == 0 ? 1e-12 : 1e15) * (1 + (rng.NextDouble() - 0.5) * 1e-6);
                string label = TimingDiagram.FormatSeconds(v);
                var b = Back(label);
                // Four significant digits at least: the fixed forms keep three decimals of a number of at least 1, the
                // exponent form three decimals of its mantissa.
                Check.True(Math.Abs(b.Value - v) <= 5.0001e-4 * Math.Abs(v), $"{v:R} -> {label}");
                double m = Math.Abs(b.Number);
                if (b.Exponent) Check.True(Math.Abs(b.Value) < 1e-12 || Math.Abs(b.Value) >= 1e15, $"{v:R} -> {label}: exponent form between 1 ps and 1e15 s");
                else
                {
                    Check.True(m >= 1, $"{v:R} -> {label}: below one of its unit");
                    Check.True(b.Unit == "s" ? m < 1e15 : m < 1000, $"{v:R} -> {label}: a thousand of its unit or 1e15 s");
                }
            }
        }

        // ---------- TimingDiagram axis ----------

        // The axis labels: the <text> elements on the lowest row below the lanes.
        private static string[] AxisLabels(string svg)
        {
            var texts = System.Xml.Linq.XDocument.Parse(svg).Descendants().Where(e => e.Name.LocalName == "text")
                .Select(e => (Y: double.Parse((string?)e.Attribute("y") ?? "0", CultureInfo.InvariantCulture), T: e.Value))
                .Where(t => t.Y > 30 && t.T != "a").ToList();
            double y = texts.Max(t => t.Y);
            return texts.Where(t => t.Y == y).Select(t => t.T).ToArray();
        }

        // The exact offsets came from the tick labels parsed as decimal, whose 28 places rounded labels near 1e-17 that
        // differ near 1e-31 together: every offset read "+0 s" (or "+-0 s"). Graded: past the first, labels are offsets
        // that rise tick by tick.
        public void Timing_OffsetLabelsOnATinyWindow_Rise()
        {
            var sig = TimingSignal.Digital("a", new (double, bool?)[] { (0, true) });
            var windows = new System.Collections.Generic.List<(double S, double E, double W)>
            {
                (6.408733168219132E-17, 6.408733168437764E-17, 957028), (1e-17, 1e-17 + 2e-32, 800),
                (-1e-17, -1e-17 + 2e-32, 800), (1e-20, 1e-20 + 4e-35, 800),
            };
            var rng = new Random(22400);
            for (int trial = 0; trial < 300; trial++)
            {
                double s = (rng.NextDouble() - 0.3) * Math.Pow(10, rng.Next(-24, -12));
                double e = s + Math.Abs(s) * Math.Pow(10, -rng.Next(10, 16)) * (1 + rng.NextDouble());
                if (e > s) windows.Add((s, e, 200 + rng.NextDouble() * 2000));
            }
            foreach (var (s, e, w) in windows)
            {
                string[] labels = AxisLabels(TimingDiagram.RenderSvg(new[] { sig }, new TimingDiagramOptions { Start = s, End = e, Width = w }));
                string where = $"[{s:R}, {e:R}] width {w:R}";
                for (int i = 1; i < labels.Length; i++) Check.False(labels[i] == labels[i - 1], where + ": '" + labels[i] + "' twice");
                if (labels.Length < 2 || !labels[1].StartsWith("+", StringComparison.Ordinal)) continue;
                double prev = 0;
                for (int i = 1; i < labels.Length; i++)
                {
                    Check.True(labels[i].StartsWith("+", StringComparison.Ordinal) && labels[i].EndsWith(" s", StringComparison.Ordinal), where + ": " + labels[i]);
                    double d = Back(labels[i].Substring(1)).Value;
                    Check.True(d > prev, $"{where}: offset {labels[i]} after {labels[i - 1]}");
                    prev = d;
                }
            }
        }
    }
}
