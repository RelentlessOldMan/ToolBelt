using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>Round 33 review regressions (WPF satellite, second pass).</summary>
    public sealed class WpfReview2Tests
    {
        private static NumericInputRules Rules(double min = double.NegativeInfinity, double max = double.PositiveInfinity, int dp = 0)
            => new NumericInputRules { Minimum = min, Maximum = max, DecimalPlaces = dp, Culture = CultureInfo.InvariantCulture };

        private static string ParamOf(Action action)
        {
            try { action(); }
            catch (ArgumentException ex) { return ex.ParamName ?? "(null)"; }
            catch (Exception ex) { return ex.GetType().Name; }
            return "(no throw)";
        }

        public void Numeric_ClampedValueStaysInRange()
        {
            Check.Equal("0.3", NumericInputBehavior.Coerce("0", Rules(0.25, 10, 1)));                 // "0.2"
            Check.Equal("0.3", NumericInputBehavior.StepValue("0.3", -1, 0.1, Rules(0.25, 10, 1)));   // "0.2"
            Check.Equal("3", NumericInputBehavior.Coerce("9", Rules(0, 3.5, 0)));                    // "4"
            Check.Equal("1.7", NumericInputBehavior.Coerce("5", Rules(0, 1.75, 1)));                 // "1.8"
            Check.Equal("-1.7", NumericInputBehavior.Coerce("-9", Rules(-1.75, 0, 1)));
            Check.Equal("0.5", NumericInputBehavior.Coerce("0.5", Rules(0.25, 10, 1)));
        }

        public void Numeric_NoInfinityOrFormatException()
        {
            string once = NumericInputBehavior.StepValue("100", +1, 1e308, Rules());
            string twice = NumericInputBehavior.StepValue(once, +1, 1e308, Rules());
            Check.False(double.IsInfinity(double.Parse(twice, CultureInfo.InvariantCulture)), twice);  // "Infinity"
            Check.False(NumericInputBehavior.Coerce("5", Rules(double.PositiveInfinity))!.Contains("Infinity"));      // "Infinity"
            string? many = NumericInputBehavior.Coerce("1.5", Rules(dp: 1_000_000_000));            // FormatException
            Check.Equal(101, many!.Length);
            Check.Equal("0.10000000000000000000", NumericInputBehavior.Coerce("0.1", Rules(dp: 20)));  // binary noise
        }

        public void RegexRules_TrailingNewlineIsInvalid()
        {
            Check.False(new RegexValidationRule { Pattern = "[0-9]+" }.Validate("123\n", CultureInfo.InvariantCulture).IsValid);
            Check.True(new RegexValidationRule { Pattern = "[0-9]+" }.Validate("123", CultureInfo.InvariantCulture).IsValid);
            var set = new ValidationRuleSet();
            string v = "123\n";
            set.For("Code", () => v).Matches("[0-9]+");
            Check.Equal(1, set.Validate("Code").Count);
            v = "123";
            Check.Equal(0, set.Validate("Code").Count);
        }

        public void RuleBuilder_EmptyRangeNamesMax()
            => Check.Equal("max", ParamOf(() => new ValidationRuleSet().For("N", () => 3).Range(5, 1)));

        public void NotifyTaskCompletion_RaisesOnTheCreatingContext()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                int uiThread = Environment.CurrentManagedThreadId;
                var raisedOn = new List<int>();
                var tcs = new TaskCompletionSource<int>();
                var frame = new DispatcherFrame();
                NotifyTaskCompletion<int>? ntc = null;
                dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        ntc = new NotifyTaskCompletion<int>(tcs.Task);
                        ntc.PropertyChanged += (_, __) => raisedOn.Add(Environment.CurrentManagedThreadId);
                        _ = Task.Run(async () => { await Task.Delay(30); tcs.SetResult(7); });
                        await ntc.Completion;
                    }
                    finally { frame.Continue = false; }
                }));
                Dispatcher.PushFrame(frame);
                Check.True(raisedOn.Count > 0);
                Check.True(raisedOn.TrueForAll(id => id == uiThread), "PropertyChanged must be raised on the UI thread");
                Check.Equal(7, ntc!.Result);
            });
        }

        public void CountToVisibility_AnyNumericTypeAndUnset()
        {
            var c = new CountToVisibilityConverter();
            foreach (object zero in new object[] { (nint)0, (Half)0, (Int128)0, BigInteger.Zero, DependencyProperty.UnsetValue, Half.NaN })
                Check.Equal(Visibility.Collapsed, c.Convert(zero, typeof(Visibility), null, CultureInfo.InvariantCulture));
            foreach (object one in new object[] { (nint)2, (Half)0.5, (Int128)1, BigInteger.One, new object() })
                Check.Equal(Visibility.Visible, c.Convert(one, typeof(Visibility), null, CultureInfo.InvariantCulture));
        }

        public void EnumToBoolean_NumericStringsAreNotNames()
        {
            var c = new EnumToBooleanConverter();
            Check.Equal(Binding.DoNothing, c.ConvertBack(true, typeof(DayOfWeek), "99", CultureInfo.InvariantCulture));   // (DayOfWeek)99
            Check.Equal(false, c.Convert(DayOfWeek.Sunday, typeof(bool), "0", CultureInfo.InvariantCulture));
            Check.Equal(true, c.Convert(DayOfWeek.Sunday, typeof(bool), "Sunday", CultureInfo.InvariantCulture));
            Check.Equal(DayOfWeek.Monday, c.ConvertBack(true, typeof(DayOfWeek), "Monday", CultureInfo.InvariantCulture));
        }

        public void HotkeyGesture_RejectsNumbersAndPseudoKeys()
        {
            foreach (string bad in new[] { "Ctrl+65", "Ctrl+999", "Ctrl+12", "Ctrl+ImeProcessed", "Ctrl+DeadCharProcessed", "Ctrl+A, B" })
                Check.False(HotkeyGesture.TryParse(bad, out _), bad);
            Check.Equal(Key.D1, HotkeyGesture.Parse("Ctrl+1").Key);
            Check.Equal(Key.F12, HotkeyGesture.Parse("Ctrl+F12").Key);
            Check.Equal(Key.NumPad1, HotkeyGesture.Parse("Ctrl+NumPad1").Key);
            Check.Equal("text", ParamOf(() => HotkeyGesture.Parse(null!)));                       // FormatException
        }

        private static readonly string[] Candidates =
        {
            "Ctrl+Alt+Shift+F24", "Ctrl+Alt+Shift+F23", "Ctrl+Alt+Shift+F22", "Ctrl+Alt+Win+F21",
        };

        public void GlobalHotkeys_OtherThreadThrowsAndChangesNothing()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                HotkeyGesture gesture = default;
                int id = 0;
                using (var hk = new GlobalHotkeys())
                {
                    foreach (string c in Candidates)
                        if (hk.TryRegister(HotkeyGesture.Parse(c), () => { }, out id)) { gesture = HotkeyGesture.Parse(c); break; }
                    Check.True(id != 0, "no candidate hotkey was free");
                    Exception? unregister = null, dispose = null;
                    Task.Run(() =>
                    {
                        try { hk.Unregister(id); } catch (Exception ex) { unregister = ex; }
                        try { hk.Dispose(); } catch (Exception ex) { dispose = ex; }
                    }).Wait();
                    Check.True(unregister is InvalidOperationException, unregister?.GetType().Name ?? "no throw");   // true, leaking it
                    Check.True(dispose is InvalidOperationException, dispose?.GetType().Name ?? "no throw");
                    Check.Equal(1, hk.Count);
                }
                using (var again = new GlobalHotkeys())                       // the owner's Dispose released it
                    Check.True(again.TryRegister(gesture, () => { }, out _));
            });
        }

        public void GlobalHotkeys_DefaultGestureIsRejected()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                using var hk = new GlobalHotkeys();
                Check.Equal("gesture", ParamOf(() => hk.TryRegister(default, () => { }, out _)));   // registered VK 0
                Check.Equal(0, hk.Count);
            });
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public void GlobalHotkeys_HostileWParamIsIgnored()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                using var hk = new GlobalHotkeys();
                Exception? error = null;
                DispatcherUnhandledExceptionEventHandler h = (_, e) => { error = e.Exception; e.Handled = true; };
                Dispatcher.CurrentDispatcher.UnhandledException += h;
                try { SendMessage(hk.Handle, 0x0312, new IntPtr(0x1_0000_0001L), IntPtr.Zero); }
                catch (Exception ex) { error = ex; }
                finally { Dispatcher.CurrentDispatcher.UnhandledException -= h; }
                Check.True(error is null, error?.GetType().Name ?? "");                             // OverflowException
            });
        }

        public void ThemeManager_AdoptsADictionaryTheAppAlreadyMerged()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var target = new ResourceDictionary();
                var light = new ResourceDictionary { ["Bg"] = "light" };
                var dark = new ResourceDictionary { ["Bg"] = "dark" };
                target.MergedDictionaries.Add(light);                                  // as App.xaml would
                var tm = new ThemeManager(target).Register(AppTheme.Light, light).Register(AppTheme.Dark, dark);
                tm.Apply(AppTheme.Light);
                Check.Equal(1, target.MergedDictionaries.Count);                       // 2
                tm.Apply(AppTheme.Dark);
                Check.Equal("dark", target["Bg"]);                                     // "light"
                tm.Apply(AppTheme.Light);
                Check.Equal("light", target["Bg"]);
                Check.Equal(1, target.MergedDictionaries.Count);
            });
        }

        public void PlotPresenter_ZoomLimitsAreValidatedAndFitStaysInvertible()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var p = new PlotPresenter();
                foreach (double bad in new[] { 0.0, -1, double.NaN, double.PositiveInfinity })
                {
                    Check.Equal("value", ParamOf(() => p.MinZoom = bad));
                    Check.Equal("value", ParamOf(() => p.MaxZoom = bad));
                }
                p.MinZoom = 1e-12;
                var drawing = new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(0, 0, 1e10, 1)));
                p.Source = new DrawingImage(drawing);
                p.Measure(new Size(200, 100));
                p.Arrange(new Rect(0, 0, 200, 100));
                p.FitToView();
                Check.True(p.ViewTransform.HasInverse);                                // Zoom 1e-8, not invertible
                p.ViewToContent(new Point(1, 1));
            });
        }

        public void BitmapFromRgba_ValidatesSizeAndDpi()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                Check.Equal("rgba", ParamOf(() => PlotPresenter.BitmapFromRgba(32768, 32768, new byte[0])));    // WPF's error, no name
                Check.Equal("rgba", ParamOf(() => PlotPresenter.BitmapFromRgba(0x40000001, 1, new byte[4])));   // OverflowException
                Check.Equal("dpi", ParamOf(() => PlotPresenter.BitmapFromRgba(1, 1, new byte[4], double.NaN)));
                Check.Equal("dpi", ParamOf(() => PlotPresenter.BitmapFromRgba(1, 1, new byte[4], 0)));
                Check.Equal(1, PlotPresenter.BitmapFromRgba(1, 1, new byte[4]).PixelWidth);
            });
        }

        public void FileDrop_CompoundExtensionsMatch()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { @"C:\nope\a.tar.gz", @"C:\nope\b.gz", @"C:\nope\c.tar" });
                Check.Equal(@"C:\nope\a.tar.gz", string.Join("|", FileDropBehavior.GetFiles(data, ".tar.gz")));
                Check.Equal(2, FileDropBehavior.GetFiles(data, "gz").Length);
            });
        }

        private static DragDropEffects RaiseDragOver(UIElement element, IDataObject data)
        {
            ConstructorInfo ctor = typeof(DragEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            object?[] args = ctor.GetParameters().Select(p =>
                p.ParameterType == typeof(IDataObject) ? data
                : p.ParameterType == typeof(DependencyObject) ? element
                : p.ParameterType == typeof(DragDropEffects) ? DragDropEffects.All
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            var e = (DragEventArgs)ctor.Invoke(args);
            e.RoutedEvent = DragDrop.PreviewDragOverEvent;
            element.RaiseEvent(e);
            return e.Effects;
        }

        public void FileDrop_FiltersOncePerDrag()
        {
            string root = Path.Combine(Path.GetTempPath(), "tbwpf_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "x.csv");
            Directory.CreateDirectory(path);
            try
            {
                FileDropBehaviorTests.RunSta(() =>
                {
                    var border = new Border();
                    FileDropBehavior.SetExtensions(border, ".csv");
                    FileDropBehavior.SetCommand(border, new RelayCommand<object>(_ => { }));
                    var data = new DataObject(DataFormats.FileDrop, new[] { path });
                    Check.Equal(DragDropEffects.None, RaiseDragOver(border, data));      // a folder
                    Directory.Delete(path);
                    File.WriteAllText(path, "a");
                    // Same drag: the disk was not checked again (it was once per file on every DragOver).
                    Check.Equal(DragDropEffects.None, RaiseDragOver(border, data));
                    Check.Equal(DragDropEffects.Copy, RaiseDragOver(border, new DataObject(DataFormats.FileDrop, new[] { path })));
                });
            }
            finally { Directory.Delete(root, true); }
        }

        public void BindingFailureListener_HugeIdDoesNotThrow()
        {
            using var listener = new BindingFailureListener();
            listener.WriteLine("System.Windows.Data Error: 99999999999 : boom");                   // OverflowException
            listener.WriteLine("System.Windows.Data Error: 40 : real");
            Check.Equal(1, listener.Failures.Count);
            Check.Equal(40, listener.Failures[0].Code);
        }
    }
}
