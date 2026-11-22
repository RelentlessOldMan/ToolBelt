using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>Regressions from review round 203 (WPF satellite, twelfth pass): each test failed before its fix.</summary>
    public sealed class WpfReview203Tests
    {
        private static NumericInputRules Rules(double min, double max, int dp)
            => new NumericInputRules { Minimum = min, Maximum = max, DecimalPlaces = dp, Culture = CultureInfo.InvariantCulture };

        // ---------- NumericInputBehavior ----------

        // Stepping past double.MaxValue was clamped only after rounding, and at DecimalPlaces above 0 the rounding parsed
        // "Infinity.00" and threw FormatException out of the arrow-key and mouse-wheel handlers. Every place count and
        // both directions now give the largest finite value, as DecimalPlaces 0 did.
        public void StepValue_PastMaxValue_ClampsAtEveryPlaceCount()
        {
            // The shortest round-trip digits of double.MaxValue, as the box shows any value (1.7976931348623157E+308).
            string max = "17976931348623157" + new string('0', 292);
            foreach (int dp in new[] { 0, 1, 2, 5, 15, 16, 40 })
            {
                var r = Rules(double.NegativeInfinity, double.PositiveInfinity, dp);
                string frac = dp == 0 ? "" : "." + new string('0', dp);
                foreach (string start in new[] { "100", "1e308", "1.7976931348623157E+308" })
                {
                    string once = NumericInputBehavior.StepValue(start, +1, 1e308, r);
                    string twice = NumericInputBehavior.StepValue(once, +1, 1e308, r);
                    Check.Equal(max + frac, twice);
                    string down = NumericInputBehavior.StepValue("-" + start, -1, 1e308, r);
                    Check.Equal("-" + max + frac, NumericInputBehavior.StepValue(down, -1, 1e308, r));
                }
            }
        }

        // A step under half a display unit moves one unit; added in binary and rounded to nearest, it rounded straight
        // back where a unit is one or two ulps, so the arrow keys did nothing (-48183132800358.88 + 0.01 at two places).
        // Graded against decimal arithmetic: the next shown value, or the next double where that reads as the start.
        public void StepValue_TinyStep_MovesToTheNextShownValue()
        {
            var r2 = Rules(double.NegativeInfinity, double.PositiveInfinity, 2);
            Check.Equal("-48183132800358.87", NumericInputBehavior.StepValue("-48183132800358.88", +1, 0.01, r2));
            Check.Equal("-44584.89780993428", NumericInputBehavior.StepValue("-44584.89780993429", +1, 1e-11, Rules(double.NegativeInfinity, double.PositiveInfinity, 11)));
            var rng = new Random(2031);
            for (int trial = 0; trial < 50000; trial++)
            {
                int d = rng.Next(12);
                var r = Rules(double.NegativeInfinity, double.PositiveInfinity, d);
                double raw = (rng.NextDouble() * 2 - 1) * Math.Pow(10, rng.Next(16 - d));
                string start = NumericInputBehavior.Coerce(raw.ToString("R", CultureInfo.InvariantCulture), r)!;
                int sign = rng.Next(2) == 0 ? -1 : 1;
                string got = NumericInputBehavior.StepValue(start, sign, 1e-300, r);
                double startD = double.Parse(start, CultureInfo.InvariantCulture);
                decimal unit = 1m / (decimal)Math.Pow(10, d);
                double want = (double)(decimal.Parse(start, NumberStyles.Float, CultureInfo.InvariantCulture) + sign * unit);
                if (want == startD) want = sign > 0 ? Math.BitIncrement(startD) : Math.BitDecrement(startD);
                Check.True(double.Parse(got, CultureInfo.InvariantCulture) == want, $"{start} {(sign > 0 ? "+" : "-")} one unit at {d} places: {got}, want {want:R}");
            }
        }

        // The keystroke filter built a string of DecimalPlaces 9s for every character typed after the separator: at
        // int.MaxValue it threw OutOfMemoryException, at 50 million each keystroke took 0.2 s. Graded against the same
        // filter at a small place count where the 9s are all built, for a random value near a random bound.
        public void IsAcceptablePartial_HugeDecimalPlaces_MatchesTheSmallCount()
        {
            Check.True(NumericInputBehavior.IsAcceptablePartial("1.", Rules(0, 10, int.MaxValue)));
            Check.False(NumericInputBehavior.IsAcceptablePartial("1.5", Rules(5, 10, int.MaxValue)));
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) NumericInputBehavior.IsAcceptablePartial("1.2345", Rules(0, 10, 50_000_000));
            Check.True(watch.Elapsed < TimeSpan.FromSeconds(2), "took " + watch.Elapsed);
            var rng = new Random(203);
            for (int trial = 0; trial < 20000; trial++)
            {
                int whole = rng.Next(100), fracDigits = rng.Next(6);
                string text = whole.ToString(CultureInfo.InvariantCulture) + "." + string.Concat(Enumerable.Range(0, fracDigits).Select(_ => rng.Next(10).ToString(CultureInfo.InvariantCulture)));
                double bound = Math.Round(whole + rng.NextDouble() * 2 - 1, rng.Next(8));
                double min = rng.Next(2) == 0 ? bound : double.NegativeInfinity, max = min == bound ? 1000 : bound;
                // 29 more places than typed builds its 9s in full; past 30 they cannot change the outcome.
                bool want = NumericInputBehavior.IsAcceptablePartial(text, Rules(min, max, fracDigits + 29));
                bool got = NumericInputBehavior.IsAcceptablePartial(text, Rules(min, max, int.MaxValue));
                Check.True(want == got, $"'{text}' in [{min:R}, {max:R}]: {got}, want {want}");
            }
        }

        // ---------- BindingFailureListener ----------

        // Only "Error:" lines written through WriteLine were recorded: a critical line was always dropped, and a warning
        // even with IncludeWarnings on (the TraceData path had the same fault, fixed before).
        public void BindingFailureListener_WriteLine_HonoursTheLevels()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                foreach (bool warnings in new[] { false, true })
                {
                    using var listener = new BindingFailureListener { IncludeWarnings = warnings };
                    listener.WriteLine("System.Windows.Data Warning: 4 : Cannot find source for binding");
                    listener.WriteLine("System.Windows.Data Critical: 7 : crit");
                    listener.WriteLine("System.Windows.Data Error: 40 : BindingExpression path error");
                    listener.WriteLine("System.Windows.Data Information: 41 : info");
                    int[] codes = listener.Failures.Select(f => f.Code).ToArray();
                    Check.Equal(warnings ? "4,7,40" : "7,40", string.Join(",", codes));
                }
            });
        }

        // The path ran to the first ';', so an indexer key holding one ("Map[a;b].Name") gave "Map[a".
        public void BindingFailure_Path_KeepsASemicolonInAnIndexer()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                using var listener = new BindingFailureListener();
                foreach (string path in new[] { "Map[a;b].Name", "Real", "Items[0].Map[x;y;z]", "A.B[;]", "" })
                {
                    listener.Clear();
                    listener.TraceEvent(null, "System.Windows.Data", TraceEventType.Error, 40,
                        "BindingExpression path error: 'Name' property not found on 'object' ''Vm' (HashCode=1)'. BindingExpression:Path="
                        + path + "; DataItem='Vm' (HashCode=1); target element is 'TextBlock' (Name=''); target property is 'Text' (type 'String')");
                    Check.Equal(path, listener.Failures[0].Path);
                    Check.Equal("Text", listener.Failures[0].TargetProperty);
                }
            });
        }

        // ---------- PlotPresenter ----------

        // With a MaxZoom below about 5e-8, ResetView set a scale WPF treats as singular, and ViewToContent then threw.
        public void PlotPresenter_ResetView_KeepsTheViewInvertible()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                foreach (double maxZoom in new[] { 1e-8, 4e-8, 1e-12, 1e-300 })
                {
                    var p = new PlotPresenter { MinZoom = maxZoom / 10, MaxZoom = maxZoom };
                    p.ResetView();
                    Check.True(p.ViewTransform.HasInverse, maxZoom.ToString("R", CultureInfo.InvariantCulture));
                    p.ViewToContent(new Point(1, 1));
                }
                var q = new PlotPresenter { MinZoom = 0.5, MaxZoom = 2 };
                q.ResetView();
                Check.Equal(1.0, q.ViewTransform.M11);
            });
        }

        // ---------- ValidationRuleSet ----------

        private sealed class Floats : RuleValidatedObject
        {
            private NFloat _a;
            public Floats(NFloat min, NFloat max) => Rules.For(nameof(A), () => A).Range(min, max);
            public NFloat A { get => _a; set => SetAndValidate(ref _a, value); }
        }

        // A NaN bound is refused for double, float and Half; an NFloat NaN was accepted, and a NaN value then passed.
        public void Range_NaNBound_IsRefusedForEveryFloatingType()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => new Floats(NFloat.NaN, 5));
            Check.Equal("min", ex.ParamName);
            ex = Check.Throws<ArgumentOutOfRangeException>(() => new Floats(0, NFloat.NaN));
            Check.Equal("max", ex.ParamName);
            var ok = new Floats(0, 5) { A = 3 };
            Check.False(ok.HasErrors);
            ok.A = 6;
            Check.True(ok.HasErrors);
        }

        // ---------- AsyncRelayCommand ----------

        private sealed class HandlerFault : Exception { public HandlerFault(string m) : base(m) { } }

        // A CanExecuteChanged handler that threw at the start stopped the operation from running and was then reported
        // twice. The operation runs; one failure is thrown as itself and several in an AggregateException, operation first.
        public void AsyncRelayCommand_ThrowingHandler_DoesNotStopTheOperation()
        {
            foreach (int faults in new[] { 0b01, 0b10, 0b11 })   // bit 0: the start raise throws; bit 1: the end raise
                foreach (bool operationFails in new[] { false, true })
                {
                    int ran = 0, raised = 0;
                    var cmd = new AsyncRelayCommand(() => { ran++; return operationFails ? Task.FromException(new InvalidOperationException("op")) : Task.CompletedTask; });
                    cmd.CanExecuteChanged += (_, _) =>
                    {
                        int which = raised++;
                        if ((faults >> which & 1) == 1) throw new HandlerFault(which == 0 ? "start" : "end");
                    };
                    Exception? thrown = null;
                    try { cmd.ExecuteAsync().GetAwaiter().GetResult(); }
                    catch (Exception e) { thrown = e; }
                    string what = $"faults {faults}, operation fails {operationFails}";
                    Check.Equal(1, ran);
                    Check.Equal(2, raised);
                    Check.False(cmd.IsExecuting);
                    var want = new List<string>();
                    if (operationFails) want.Add("op");
                    if ((faults & 1) != 0) want.Add("start");
                    if ((faults & 2) != 0) want.Add("end");
                    string got = thrown is AggregateException agg ? string.Join(",", agg.InnerExceptions.Select(e => e.Message)) : thrown?.Message ?? "";
                    Check.Equal(string.Join(",", want), got);
                    Check.True(want.Count == 1 ? thrown is not AggregateException : thrown is AggregateException, what);
                }
        }
    }
}
