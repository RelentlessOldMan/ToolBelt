using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    // Regressions from review round 196 (charts, twelfth pass): each test failed before its fix.
    public sealed class ChartsReview196Tests
    {
        private static readonly Rgba Ink = new Rgba(0, 0, 255);

        private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static double Dec(long digits, int exponent)
            => double.Parse(digits.ToString(CultureInfo.InvariantCulture) + "E" + exponent.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);

        // The nice step as digit x 10^exponent.
        private static (int Digit, int Exponent) Nice(double step)
        {
            string s = step.ToString("E0", CultureInfo.InvariantCulture);
            int at = s.IndexOf('E');
            return (int.Parse(s.Substring(0, at), CultureInfo.InvariantCulture), int.Parse(s.Substring(at + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
        }

        // Naive: the ticks k * digit x 10^exponent inside [min, max], each at its exact decimal.
        private static int InsideCount(double min, double max, int digit, int exponent)
        {
            double step = Dec(digit, exponent);
            long a = (long)Math.Floor(min / step) - 3, b = (long)Math.Ceiling(max / step) + 3;
            int n = 0;
            for (long k = a; k <= b; k++) { double v = Dec(k * digit, exponent); if (v >= min && v <= max) n++; }
            return n;
        }

        private static IEnumerable<(double Min, double Max)> NarrowRanges(int seed, int trials)
        {
            var rng = new Random(seed);
            for (int t = 0; t < trials; t++)
            {
                double a = (rng.NextDouble() * 2 - 1) * Math.Pow(10, rng.Next(-3, 10));
                double b = a + Math.Abs(a) * Math.Pow(10, -rng.Next(3, 15)) * (0.5 + rng.NextDouble());
                if (b > a) yield return (a, b);
            }
        }

        // ---------- AxisTicks ----------

        // The loose bounds came from min / step + 1e-9, which is off by more than the slack once the quotient passes about
        // 1e7: Linear(2262700.476, 2262700.486, 9) started a whole step below the data, at .474.
        public void Linear_LooseStartsAtTheLastTickBelow()
        {
            var cases = new List<(double, double, int)>
            {
                (2262700.476, 2262700.486, 9), (-549546.2992924947, -549546.298, 7), (-40000000, -39999999.94, 22),
                (4200000, 4200000.000000002, 5), (91.27, 91.27000000000051, 5), (2334.749, 2334.7490000050757, 7),
            };
            foreach (var (a, b) in NarrowRanges(1961, 3000)) cases.Add((a, b, 5));
            foreach (var (min, max, n) in cases)
            {
                TickSet t = AxisTicks.Linear(min, max, n, loose: true);
                string what = R(min) + ".." + R(max);
                Check.True(t.Values[0] <= min + t.Step * 1e-9 && t.Values[t.Count - 1] >= max - t.Step * 1e-9, what + ": brackets");
                Check.True(t.Count == 1 || t.Values[1] > min + t.Step * 1e-9, what + ": an extra step below, " + R(t.Values[0]));
                Check.True(t.Count == 1 || t.Values[t.Count - 2] < max - t.Step * 1e-9, what + ": an extra step above, " + R(t.Values[t.Count - 1]));
            }
        }

        public void Time_LooseStartsAtTheLastTickBelow()
        {
            foreach (var (min, max, n) in new[] { (9866.4, 9866.400905337758, 10), (422292756.701, 422292756.7017655, 11) })
            {
                TickSet t = AxisTicks.Time(min, max, n, loose: true);
                Check.True(t.Values[1] > min + t.Step * 1e-9, R(min) + ": an extra step below, " + R(t.Values[0]));
                Check.True(t.Values[t.Count - 2] < max - t.Step * 1e-9, R(max) + ": an extra step above");
                Check.True(t.Count <= n, R(min) + ": count " + t.Count);
            }
        }

        // The step search counted with the same quotient, so a tight axis took a coarser step than fitted:
        // Linear(-85.88, -85.87999994163204, 11, false) chose 1e-8 (6 ticks) where 5e-9 has 11.
        public void Linear_TightTakesTheSmallestStepThatFits()
        {
            var cases = new List<(double, double, int)>
            {
                (-85.88, -85.87999994163204, 11), (-84.57, -84.56999992325217, 3), (-97.594, -97.59399999999945, 5),
                (3.8E-07, 3.800000000003778E-07, 3),
            };
            foreach (var (a, b) in NarrowRanges(1962, 3000)) cases.Add((a, b, 6));
            foreach (var (min, max, n) in cases)
            {
                TickSet t = AxisTicks.Linear(min, max, n, loose: false);
                if (t.Count < 2) continue;   // the range's own ends
                var (d, e) = Nice(t.Step);
                var (d2, e2) = d == 1 ? (5, e - 1) : d == 2 ? (1, e) : (2, e);
                Check.True(InsideCount(min, max, d2, e2) > n, R(min) + ".." + R(max) + ": step " + R(t.Step) + " where " + d2 + "E" + e2 + " fits");
            }
        }

        // Tight ticks were kept within a 1e-9 slack outside the range, which is not small against a range near zero:
        // Linear(-9.53, -1.6e-11, 9, false) had a tick at 0, and Log10 kept 1e-163 below 1.0000000000000001e-163.
        public void TightTicks_LieInsideTheRange()
        {
            var linear = new List<(double, double, int)>
            {
                (-9.53287557677034, -1.6029001267640388E-11, 9), (64, 1000000000000009.5, 9), (3.5466107882310683E-183, 181.25, 10),
            };
            var rng = new Random(1963);
            for (int i = 0; i < 3000; i++)
            {
                double w = Math.Pow(10, rng.Next(-5, 6)) * (0.1 + rng.NextDouble());
                double tiny = Math.Pow(10, -rng.Next(8, 200)) * rng.NextDouble();
                linear.Add(rng.Next(2) == 0 ? (tiny, w, 2 + rng.Next(10)) : (-w, -tiny, 2 + rng.Next(10)));
            }
            foreach (var (min, max, n) in linear)
            {
                foreach (TickSet t in new[] { AxisTicks.Linear(min, max, n, loose: false), AxisTicks.Time(min, max, n, loose: false) })
                    foreach (double v in t.Values)
                        Check.True(v >= min && v <= max, R(min) + ".." + R(max) + ": tick " + R(v));
            }
            foreach (var (min, max) in new[] { (4.5, 9.462719368498183E-13), (-63, -7.51e123) })
                foreach (double v in AxisTicks.Time(min, max, 7, loose: false).Values)
                    Check.True(v >= Math.Min(min, max) && v <= Math.Max(min, max), "time " + R(min) + ": tick " + R(v));
            foreach (double v in AxisTicks.Log10(1.555e-93, 1.0000000000000001E-163, loose: false).Values)
                Check.True(v >= 1.0000000000000001E-163 && v <= 1.555e-93, "log tick " + R(v));
        }

        // The doc's one exception to maxTicks is a loose range straddling zero; [0, 0] opened to [-1, 1], which needed
        // -1|0|1 at maxTicks 2.
        public void FlatZero_KeepsTwoTicks() => TwoTicks(0.0, 0.0);

        // A subnormal range opened to its midpoint +- 1e-300, across zero, and needed three loose ticks too.
        public void SubnormalRange_KeepsTwoTicks()
        {
            foreach (var (min, max) in new[] { (1.9e-322, 2.47e-322), (-2.47e-322, -1.9e-322), (double.Epsilon, double.Epsilon), (0, double.Epsilon), (-double.Epsilon, 0) })
                TwoTicks(min, max);
        }

        private static void TwoTicks(double min, double max)
        {
            {
                TickSet t = AxisTicks.Linear(min, max, 2, loose: true);
                Check.True(t.Count <= 2, R(min) + ".." + R(max) + ": " + t.Count + " ticks");
                Check.True(t.Min <= min && t.Max >= max, R(min) + ": covers the data");
                Check.True(AxisTicks.Time(min, max, 2, loose: true).Count <= 2, R(min) + ": time");
            }
        }

        // Under a minute Time labels in one s.fff format, which keeps 15 significant digits: every tick of
        // [10, 10 + 5e-14] read "10.00000000000000s".
        public void Time_SubMinuteLabelsAreDistinct()
        {
            var cases = new List<(double, double)> { (10, 10 + 5e-14), (50, 50 + 1e-13) };
            var rng = new Random(1964);
            for (int i = 0; i < 3000; i++)
            {
                double a = (rng.NextDouble() * 2 - 1) * 60;
                cases.Add((a, a + Math.Pow(10, -rng.Next(7, 16)) * (0.5 + rng.NextDouble())));
            }
            foreach (var (min, max) in cases)
                foreach (bool loose in new[] { true, false })
                {
                    TickSet t = AxisTicks.Time(min, max, 6, loose);
                    for (int i = 1; i < t.Count; i++)
                        Check.True(t.Labels[i] != t.Labels[i - 1], R(min) + ".." + R(max) + ": " + t.Labels[i] + " twice");
                }
        }

        // ---------- SvgChart ----------

        private static IEnumerable<XElement> Elements(string svg, string name) => XElement.Parse(svg).Descendants().Where(e => e.Name.LocalName == name);

        // A bar whose pixel overflowed (1e300 on a 1e-10 range) was dropped, though a bar of 2 there is full height.
        public void Bar_PastTheRangeIsClippedNotDropped()
        {
            int Bars(double[] y, double lo, double hi)
            {
                string svg = SvgChart.Render(new[] { new Series(y, Ink, PlotStyle.Bar) }, new SvgChartOptions { YMin = lo, YMax = hi });
                return Elements(svg, "rect").Count(e => (string?)e.Attribute("fill") == "#0000ff");
            }
            Check.Equal(2, Bars(new[] { 0.5, 2 }, 0, 1e-10));     // guard
            Check.Equal(2, Bars(new[] { 0.5, 1e300 }, 0, 1e-10));
            Check.Equal(1, Bars(new[] { -1e300 }, -1e-10, 1e-10));
        }

        // On a plot too narrow for any text the legend's limit came out 0, which FitWidth reads as none: the name ran
        // 150 px past a 50 px canvas.
        public void Legend_StaysOnANarrowCanvas()
        {
            string name = new string('n', 30);
            int narrow = 0;
            for (int w = 30; w <= 140; w += 5)
            {
                string svg;
                try { svg = SvgChart.Render(new[] { new Series(new[] { 1.0, 2 }, Ink, name: name) }, new SvgChartOptions { Width = w, Height = 200, ShowYTickLabels = false }); }
                catch (ArgumentException) { continue; }   // too small for its labels, as documented
                if (w < 80) narrow++;
                foreach (XElement g in Elements(svg, "g").Where(e => (string?)e.Attribute("class") == "legend"))
                {
                    double fs = double.Parse(g.Attribute("font-size")!.Value, CultureInfo.InvariantCulture);
                    foreach (XElement t in g.Elements().Where(e => e.Name.LocalName == "text"))
                    {
                        double x = double.Parse(t.Attribute("x")!.Value, CultureInfo.InvariantCulture);
                        double width = t.Attribute("textLength") != null
                            ? double.Parse(t.Attribute("textLength")!.Value, CultureInfo.InvariantCulture)
                            : SvgUtils.EstimateTextWidth(t.Value, fs);
                        Check.True(x >= 0 && x + width <= w + 0.5, "width " + w + ": legend text " + x + " + " + width);
                    }
                }
            }
            Check.True(narrow > 0, "no narrow chart rendered");
        }

        // Both ends above YMax rounded to an accepted clip (t0 = t1 = 1) and kept the raw outer end, whose pixel is
        // -infinity on a 2e-323 range: Render threw an internal ArgumentOutOfRangeException naming "y".
        public void Segment_MissingTheRangeIsSkipped()
        {
            string svg = SvgChart.Render(new[] { new Series(new[] { 1.6e308, 1 }, Ink, PlotStyle.Line, new[] { 0.0, 1 }) },
                new SvgChartOptions { YMin = 0, YMax = 2e-323 });
            XElement.Parse(svg);
            string below = SvgChart.Render(new[] { new Series(new[] { -1.6e308, -1 }, Ink, PlotStyle.Line, new[] { 0.0, 1 }) },
                new SvgChartOptions { YMin = -2e-323, YMax = 0 });
            XElement.Parse(below);
        }

        // ---------- TimingDiagram, ScatterMatrix ----------

        // The "+offset" labels that replace clashing ones round to three decimals as well: a window two ulps wide read
        // "+0.002 ps" twice beside "+4.441e-16 s".
        public void TimingDiagram_OffsetLabelsAreDistinct()
        {
            var windows = new List<(double, double)> { (2.093496889765171, 2.093496889765173) };
            var rng = new Random(1965);
            for (int i = 0; i < 400; i++)
            {
                double a = Math.Pow(10, rng.Next(-2, 12)) * (1 + rng.NextDouble());
                double b = a;
                for (int u = rng.Next(1, 9); u > 0; u--) b = NextUp(b);
                windows.Add((a, b));
            }
            foreach (var (a, b) in windows)
            {
                var sig = TimingSignal.Digital("s", new (double, bool?)[] { (a, true), (b, false) });
                string svg = TimingDiagram.RenderSvg(new[] { sig }, new TimingDiagramOptions { Start = a, End = b });
                List<string> offsets = Elements(svg, "text").Select(e => e.Value).Where(v => v.StartsWith("+", StringComparison.Ordinal)).ToList();
                Check.Equal(offsets.Count, offsets.Distinct().Count(), R(a) + ".." + R(b) + ": " + string.Join(" | ", offsets));
            }
        }

        private static double NextUp(double v) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v) + 1);

        // The title was written without a width: 200 W's ran 2,800 px across a 218 px canvas.
        public void ScatterMatrix_TitleFitsTheCanvas()
        {
            string svg = ScatterMatrix.RenderSvg(new[] { "a" }, new[] { (IReadOnlyList<double>)new[] { 1.0, 2, 3 } },
                new ScatterMatrixOptions { Title = new string('W', 200) });
            XElement root = XElement.Parse(svg);
            double width = double.Parse(root.Attribute("width")!.Value, CultureInfo.InvariantCulture);
            XElement title = root.Descendants().First(e => e.Name.LocalName == "text" && e.Value.StartsWith("WWW", StringComparison.Ordinal));
            double x = double.Parse(title.Attribute("x")!.Value, CultureInfo.InvariantCulture);
            Check.True(title.Attribute("textLength") != null, "no textLength");
            double len = double.Parse(title.Attribute("textLength")!.Value, CultureInfo.InvariantCulture);
            Check.True(x - len / 2 >= 0 && x + len / 2 <= width, "title " + (x - len / 2) + ".." + (x + len / 2) + " on " + width);
        }
    }
}
