using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Tests.Framework;
using System.Windows;
using System.Windows.Media;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>Regressions from review round 175 (WPF satellite, tenth pass): each test failed before its fix.</summary>
    public sealed class WpfReview175Tests
    {
        // A handler that reset the list during ResetAsync's clear had its state overwritten by the outer reset: IsLoading
        // read false while the newer reset's page loaded, and after a short page HasMoreItems went true again, the loader
        // ran past the end and the outer call returned 0.
        public async Task IncrementalCollection_ResetFromAResetHandler_KeepsTheNewerResetsState()
        {
            // Async loader: the newer reset's page is in flight when the outer call returns.
            var pending = new List<TaskCompletionSource<IReadOnlyList<int>>>();
            var c = new IncrementalCollection<int>((s, t, ct) =>
            {
                var tcs = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending.Add(tcs);
                return tcs.Task;
            }, 3);
            Task<int> first = c.LoadMoreAsync();
            pending[0].SetResult(new[] { 1, 2, 3 });
            Check.Equal(3, await first);
            Task<int>? inner = null;
            bool once = true;
            c.CollectionChanged += (s, e) => { if (e.Action == NotifyCollectionChangedAction.Reset && once) { once = false; inner = c.ResetAsync(); } };
            Task<int> outer = c.ResetAsync();
            Check.Equal(2, pending.Count, "one load for both resets");
            Check.True(c.IsLoading, "the newer reset's page is loading");
            pending[1].SetResult(new[] { 4 });
            Check.Equal(1, await outer);
            Check.Equal(1, await inner!);
            Check.False(c.IsLoading);
            Check.False(c.HasMoreItems);
            Check.True(c.SequenceEqual(new[] { 4 }));

            // Synchronous loaders, against a model of one fresh reset, with the nested reset raised from each place a
            // handler can see the reset: the clear, and the HasMoreItems, IsLoading and LastError notifications.
            var rng = new Random(175);
            string[] triggers = { "Reset", nameof(c.HasMoreItems), nameof(c.IsLoading), nameof(c.LastError) };
            for (int trial = 0; trial < 400; trial++)
            {
                int pageSize = 1 + rng.Next(4);
                int total = rng.Next(3 * pageSize + 2);
                var skips = new List<int>();
                var coll = new IncrementalCollection<int>((s, t, ct) =>
                {
                    skips.Add(s);
                    IReadOnlyList<int> page = Enumerable.Range(s, Math.Max(0, Math.Min(t, total - s))).ToArray();
                    return Task.FromResult(page);
                }, pageSize);
                int preload = rng.Next(4);
                for (int i = 0; i < preload; i++) await coll.LoadMoreAsync();
                string trigger = triggers[rng.Next(triggers.Length)];
                bool armed = true;
                Task<int>? nested = null;
                coll.CollectionChanged += (s, e) =>
                {
                    if (armed && trigger == "Reset" && e.Action == NotifyCollectionChangedAction.Reset) { armed = false; nested = coll.ResetAsync(); }
                };
                ((INotifyPropertyChanged)coll).PropertyChanged += (s, e) =>
                {
                    if (armed && e.PropertyName == trigger) { armed = false; nested = coll.ResetAsync(); }
                };
                skips.Clear();
                Task<int> top = coll.ResetAsync();
                int expected = Math.Min(pageSize, total);
                string shown = "pageSize " + pageSize + " total " + total + " preload " + preload + " trigger " + trigger;
                Check.Equal(expected, await top, shown);
                if (nested != null) Check.Equal(expected, await nested, shown);
                Check.True(coll.SequenceEqual(Enumerable.Range(0, expected)), shown);
                Check.Equal(total >= pageSize, coll.HasMoreItems, shown);
                Check.False(coll.IsLoading, shown);
                Check.True(skips.All(s => s == 0), "no load past the first page: " + string.Join(",", skips) + " " + shown);
            }
        }

        // ---------------- RuleValidatedObject ----------------

        private sealed class Ranged : RuleValidatedObject
        {
            private int _a = 5;
            public Ranged() { Rules.For(nameof(A), () => A).Range(0, 10); }
            public int A { get => _a; set => SetAndValidate(ref _a, value); }
        }

        // A PropertyChanged handler that threw skipped the validation: A = 50 against Range(0, 10) stored 50 with HasErrors
        // false.
        public void RuleValidatedObject_ThrowingPropertyChangedHandler_StillValidates()
        {
            var rng = new Random(1751);
            for (int trial = 0; trial < 300; trial++)
            {
                var vm = new Ranged();
                bool throwing = rng.Next(2) == 0;
                ((INotifyPropertyChanged)vm).PropertyChanged += (s, e) =>
                {
                    if (throwing && e.PropertyName == nameof(Ranged.A)) throw new InvalidOperationException("handler");
                };
                for (int step = 0; step < 4; step++)
                {
                    int value = rng.Next(-20, 31);
                    bool changes = value != vm.A;
                    try
                    {
                        vm.A = value;
                        Check.False(throwing && changes, "the handler's exception was swallowed");
                    }
                    catch (InvalidOperationException ex) when (ex.Message == "handler") { Check.True(throwing && changes); }
                    Check.Equal(value, vm.A);
                    bool invalid = value < 0 || value > 10;
                    Check.Equal(invalid, vm.HasErrors, "A = " + value + (throwing ? " with a throwing handler" : ""));
                    Check.Equal(invalid, vm.GetErrors(nameof(Ranged.A)).Cast<object>().Any());
                }
            }
        }

        // ---------------- CountToVisibilityConverter ----------------

        private sealed class NullEnumerable : System.Collections.IEnumerable
        {
            public System.Collections.IEnumerator GetEnumerator() => null!;
        }

        // A non-zero Complex with a zero or negative real part was Collapsed (IsPositive reads the real part only), and an
        // IEnumerable whose GetEnumerator returned null threw NullReferenceException.
        public void CountToVisibility_ComplexAndNullEnumerators()
        {
            var c = new CountToVisibilityConverter();
            object? Show(object v) => c.Convert(v, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Check.Equal(Visibility.Collapsed, Show(new NullEnumerable()));
            Check.Equal(Visibility.Visible, Show(new Complex(0, 5)));
            Check.Equal(Visibility.Visible, Show(new Complex(-1, 5)));

            var rng = new Random(1752);
            double[] parts = { 0, -0.0, 1, -1, 2.5, -3, double.Epsilon, double.PositiveInfinity, double.NegativeInfinity, double.NaN };
            for (int trial = 0; trial < 2000; trial++)
            {
                double re = parts[rng.Next(parts.Length)], im = parts[rng.Next(parts.Length)];
                // Reference: NaN or zero hides; a real value shows when positive; any other non-zero complex shows.
                bool visible = !(double.IsNaN(re) || double.IsNaN(im)) && !(re == 0 && im == 0) && (im != 0 || re > 0);
                Check.Equal(visible ? Visibility.Visible : Visibility.Collapsed, Show(new Complex(re, im)), "(" + re + ", " + im + ")");
            }
        }

        // ---------------- ThemeManager ----------------

        // Only an int 0 read as dark: a QWORD AppsUseLightTheme comes back as a long, and its 0 gave Light.
        public void ThemeFromRegistryValue_AnyIntegerZeroIsDark()
        {
            var rng = new Random(1753);
            for (int trial = 0; trial < 1000; trial++)
            {
                long n = rng.Next(3) == 0 ? 0 : rng.Next(1, 200);
                object[] boxes = { (int)n, n, (uint)n, (ulong)n, (short)n, (ushort)n, (byte)n, (sbyte)n };
                foreach (object box in boxes)
                    Check.Equal(n == 0 ? AppTheme.Dark : AppTheme.Light, ThemeManager.ThemeFromRegistryValue(box), box.GetType().Name + " " + n);
            }
            Check.Equal(AppTheme.Light, ThemeManager.ThemeFromRegistryValue(null));
            Check.Equal(AppTheme.Light, ThemeManager.ThemeFromRegistryValue("0"));
        }

        // ---------------- RecordingDialogService ----------------

        // Strict mode threw before recording the call, and the recorder shared the enqueued array and the caller's options
        // with the caller: changing either afterwards changed the answer and the record.
        public void RecordingDialogService_RecordsUnscriptedCallsAndCopiesWhatItShares()
        {
            var d = new RecordingDialogService();
            Check.Throws<InvalidOperationException>(() => d.Confirm("Delete?"));
            Check.Equal(1, d.Calls.Count);
            Check.Equal(DialogCallKind.Confirm, d.LastCall!.Kind);
            Check.Equal("Delete?", d.LastCall.Message);
            Check.Throws<InvalidOperationException>(() => d.AskYesNoCancel("Save?"));
            Check.Throws<InvalidOperationException>(() => d.OpenFile(new FileDialogOptions { Title = "Open" }));
            Check.Throws<InvalidOperationException>(() => d.OpenFiles());
            Check.Throws<InvalidOperationException>(() => d.SaveFile());
            Check.Throws<InvalidOperationException>(() => d.PickFolder("Where"));
            Check.Equal("Confirm,YesNoCancel,OpenFile,OpenFiles,SaveFile,PickFolder", string.Join(",", d.Calls.Select(x => x.Kind)));
            Check.Equal("Open", d.Calls[2].Options!.Title);

            var arr = new[] { "a.txt", "b.txt" };
            d.EnqueueOpenFiles(arr);
            var options = new FileDialogOptions { Title = "T1", FileName = "f", Filter = "Text|*.txt", OverwritePrompt = false };
            IReadOnlyList<string>? got = d.OpenFiles(options);
            arr[0] = "mutated";
            options.Title = "T2";
            options.FileName = "g";
            Check.Equal("a.txt,b.txt", string.Join(",", got!));
            Check.Equal("T1", d.LastCall!.Options!.Title);
            Check.Equal("f", d.LastCall.Options.FileName);
            Check.Equal("Text|*.txt", d.LastCall.Options.Filter);
            Check.False(d.LastCall.Options.OverwritePrompt);
            ((string[])got!)[1] = "changed";
            Check.Equal("a.txt,b.txt", string.Join(",", (string[])d.LastCall.Response!));
        }

        // ---------------- NumericRangeValidationRule ----------------

        // With an infinite bound, "1e400" passed: it parses as +Infinity. A number too large for a double is outside every
        // range; the culture's infinity symbol still passes an infinite bound.
        public void NumericRange_OverflowingLiteral_IsOutsideEveryRange()
        {
            var rng = new Random(1754);
            var cultures = new[] { CultureInfo.InvariantCulture, new CultureInfo("en-US"), new CultureInfo("de-DE"), new CultureInfo("fr-FR") };
            for (int trial = 0; trial < 2000; trial++)
            {
                CultureInfo culture = cultures[rng.Next(cultures.Length)];
                var rule = new NumericRangeValidationRule { Minimum = double.NegativeInfinity, Maximum = double.PositiveInfinity };
                int whole = rng.Next(1, 10), tenth = rng.Next(10), exp = rng.Next(300, 320);
                bool negative = rng.Next(2) == 0;
                // Reference: m * 10^exp with m = whole.tenth overflows past 10^308 * 1.79769...
                bool overflows = exp > 308 || (exp == 308 && whole * 10 + tenth > 17);
                string text = (negative ? culture.NumberFormat.NegativeSign : "") + whole + culture.NumberFormat.NumberDecimalSeparator + tenth + "e" + exp;
                Check.Equal(!overflows, rule.Validate(text, culture).IsValid, text + " in " + culture.Name);
            }
            var en = new CultureInfo("en-US");
            var open = new NumericRangeValidationRule { Minimum = 0, Maximum = double.PositiveInfinity };
            Check.True(open.Validate(en.NumberFormat.PositiveInfinitySymbol, en).IsValid);
            Check.True(open.Validate("1e300", en).IsValid);
        }

        // ---------------- PlotPresenter ----------------

        // ResetView set the scale to 1 whatever the limits: with MinZoom 2 the view sat at 1, and a zoom by 1 jumped to 2.
        public void PlotPresenter_ResetView_KeepsTheZoomLimits()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var rng = new Random(1755);
                for (int trial = 0; trial < 300; trial++)
                {
                    double a = Math.Pow(10, rng.NextDouble() * 4 - 2), b = Math.Pow(10, rng.NextDouble() * 4 - 2);
                    var p = new PlotPresenter { MinZoom = Math.Min(a, b), MaxZoom = Math.Max(a, b) };
                    p.Source = PlotPresenter.BitmapFromRgba(10, 10, new byte[400]);
                    p.Measure(new Size(200, 100));
                    p.Arrange(new Rect(0, 0, 200, 100));
                    p.ResetView();
                    double expected = Math.Min(Math.Max(1.0, p.MinZoom), p.MaxZoom);
                    string shown = "MinZoom " + p.MinZoom + " MaxZoom " + p.MaxZoom;
                    Check.Equal(expected, p.Zoom, shown);
                    Check.Equal(0.0, p.ViewTransform.OffsetX, shown);
                    Check.Equal(0.0, p.ViewTransform.OffsetY, shown);
                    Matrix before = p.ViewTransform;
                    p.ZoomAt(new Point(rng.Next(200), rng.Next(100)), 1.0);
                    Check.True(p.ViewTransform == before, "a zoom by 1 moved the view: " + shown);
                }
            });
        }
    }
}
