using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>Round 49 review regressions (WPF satellite, third pass).</summary>
    public sealed class WpfReview3Tests
    {
        private enum Color3 { Red, Green, Blue }

        private static NumericInputRules Rules(double min = double.NegativeInfinity, double max = double.PositiveInfinity, int dp = 0)
            => new NumericInputRules { Minimum = min, Maximum = max, DecimalPlaces = dp, Culture = CultureInfo.InvariantCulture };

        private static string ParamOf(Action action)
        {
            try { action(); }
            catch (ArgumentException ex) { return ex.ParamName ?? "(null)"; }
            catch (Exception ex) { return ex.GetType().Name; }
            return "(no throw)";
        }

        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private static readonly string[] Candidates =
        {
            "Ctrl+Alt+Shift+F24", "Ctrl+Alt+Shift+F23", "Ctrl+Alt+Shift+F22", "Ctrl+Alt+Shift+F21", "Ctrl+Alt+Shift+F20",
        };

        public void EnumToBoolean_CommaListsOnlyForFlags()
        {
            var c = new EnumToBooleanConverter();
            Check.Equal(Binding.DoNothing, c.ConvertBack(true, typeof(Color3), "Green,Blue", CultureInfo.InvariantCulture));   // (Color3)3
            Check.Equal(Binding.DoNothing, c.ConvertBack(true, typeof(Color3), "Green, Blue", CultureInfo.InvariantCulture));
            Check.Equal(false, c.Convert(Color3.Green, typeof(bool), "Green,Red", CultureInfo.InvariantCulture));           // Green|Red == Green
            Check.Equal(Color3.Blue, c.ConvertBack(true, typeof(Color3), "Blue", CultureInfo.InvariantCulture));
            Check.Equal(FileAttributes.Hidden | FileAttributes.System,
                c.ConvertBack(true, typeof(FileAttributes), "Hidden, System", CultureInfo.InvariantCulture));                  // [Flags] still parse lists
        }

        private sealed class Validated : ValidationObservableObject
        {
            public void Set(string name, params string[] errors) => SetErrors(name, errors);
            public void Add(string name, string error) => AddError(name, error);
            public bool Clear(string name) => ClearErrors(name);
            public void ClearAll() => ClearAllErrors();
        }

        public void Validation_NotifiesOnlyRealChanges()
        {
            var v = new Validated();
            int errorsChanged = 0, hasErrors = 0;
            v.ErrorsChanged += (_, __) => errorsChanged++;
            v.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(v.HasErrors)) hasErrors++; };

            v.Set("X");                                   // valid stays valid: nothing changed
            Check.Equal(0, errorsChanged);
            Check.Equal(0, hasErrors);

            v.Set("X", "bad");
            Check.Equal(1, errorsChanged);
            Check.Equal(1, hasErrors);
            v.Set("X", "bad");                            // the same list again
            Check.Equal(1, errorsChanged);
            v.Add("Y", "worse");                          // HasErrors stays true
            Check.Equal(2, errorsChanged);
            Check.Equal(1, hasErrors);
            v.Set("X", "other");
            Check.Equal(3, errorsChanged);
            Check.Equal(1, hasErrors);
            Check.True(v.Clear("X"));
            Check.Equal(1, hasErrors);
            v.ClearAll();                                 // one flip, one HasErrors notification
            Check.Equal(5, errorsChanged);
            Check.Equal(2, hasErrors);
            Check.False(v.HasErrors);
        }

        public void Numeric_DeletionsAreChecked()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var box = new TextBox { Language = XmlLanguage.GetLanguage("en-US") };
                NumericInputBehavior.SetIsEnabled(box, true);
                NumericInputBehavior.SetMinimum(box, 0);
                NumericInputBehavior.SetMaximum(box, 100);
                NumericInputBehavior.SetDecimalPlaces(box, 1);

                box.Text = "10.5";
                box.CaretIndex = 2;
                EditingCommands.Delete.Execute(null, box);              // "105", past the maximum
                Check.Equal("10.5", box.Text);
                box.CaretIndex = 3;
                EditingCommands.Backspace.Execute(null, box);           // same, from the other side
                Check.Equal("10.5", box.Text);
                box.Select(2, 1);
                ApplicationCommands.Cut.Execute(null, box);             // cutting the "." too
                Check.Equal("10.5", box.Text);

                box.CaretIndex = 4;
                EditingCommands.Backspace.Execute(null, box);           // "10." is fine
                Check.Equal("10.", box.Text);
                box.CaretIndex = 0;
                EditingCommands.Delete.Execute(null, box);              // "0." is fine
                Check.Equal("0.", box.Text);
                box.CaretIndex = 2;
                EditingCommands.DeletePreviousWord.Execute(null, box);  // to the start
                Check.Equal("", box.Text);
                NumericInputBehavior.SetIsEnabled(box, false);
            });
        }

        public void Numeric_NoNegativeZeroAndNoSeparatorForNegativePlaces()
        {
            Check.Equal("0.00", NumericInputBehavior.Coerce("-0.001", Rules(-0.001, -0.0005, 2)));                  // "-0.00"
            Check.False(NumericInputBehavior.IsAcceptablePartial("1.", Rules(dp: -1)));                             // no digit can follow
            Check.False(NumericInputBehavior.IsAcceptablePartial("1.5", Rules(dp: -1)));
            Check.Equal("2", NumericInputBehavior.Coerce("1.5", Rules(dp: -1)));
        }

        public void Plot_NonFiniteInputIsRejected()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var p = new PlotPresenter();
                Check.Equal("delta", ParamOf(() => p.PanBy(new Vector(double.NaN, 0))));
                Check.Equal("delta", ParamOf(() => p.PanBy(new Vector(0, double.PositiveInfinity))));
                Check.Equal("viewPoint", ParamOf(() => p.ZoomAt(new Point(double.NaN, 0), 2)));
                Check.Equal("viewPoint", ParamOf(() => PlotPresenter.ZoomAbout(Matrix.Identity, new Point(0, double.NegativeInfinity), 2)));
                Check.Equal(Matrix.Identity, p.ViewTransform);
                p.PanBy(new Vector(1e308, 0));
                p.PanBy(new Vector(1e308, 0));                  // would overflow to +inf: ignored
                Check.True(double.IsFinite(p.ViewTransform.OffsetX), p.ViewTransform.ToString(CultureInfo.InvariantCulture));
                Point back = p.ViewToContent(new Point(10, 10));
                Check.False(double.IsNaN(back.X));
            });
        }

        public void Messenger_RejectsValueTypeRecipients()
        {
            var m = new Messenger();
            int recipient = 42;
            Check.Equal("recipient", ParamOf(() => m.Register<string>(recipient, _ => { })));   // orphaned box
            Check.False(m.IsRegistered<string>(recipient));
            var reference = new object();
            m.Register<string>(reference, _ => { });
            Check.True(m.IsRegistered<string>(reference));
        }

        public void HotkeyGesture_ConstructorMatchesParse()
        {
            Check.Equal("modifiers", ParamOf(() => new HotkeyGesture((ModifierKeys)0x10 | ModifierKeys.Control, Key.F23)));
            Check.Equal("modifiers", ParamOf(() => new HotkeyGesture((ModifierKeys)(-1), Key.F23)));
            Check.Equal("key", ParamOf(() => new HotkeyGesture(ModifierKeys.Control, (Key)5000)));                 // "Ctrl+5000"
            Check.Equal("key", ParamOf(() => new HotkeyGesture(ModifierKeys.Control, Key.ImeProcessed)));
            Check.Equal("key", ParamOf(() => new HotkeyGesture(ModifierKeys.Control, Key.DeadCharProcessed)));
            var g = new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Windows | ModifierKeys.Alt | ModifierKeys.Shift, Key.F23);
            Check.Equal(g, HotkeyGesture.Parse(g.ToString()));
        }

        public void Hotkeys_ReregisteringOwnGestureIsNotBlamedOnOthers()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                using var hk = new GlobalHotkeys();
                HotkeyGesture? taken = null;
                foreach (string c in Candidates)
                    if (hk.TryRegister(HotkeyGesture.Parse(c), () => { }, out _)) { taken = HotkeyGesture.Parse(c); break; }
                Check.True(taken.HasValue, "no candidate hotkey was free");
                Check.False(hk.TryRegister(taken!.Value, () => { }, out int id));
                Check.Equal(0, id);
                var ex = Check.Throws<InvalidOperationException>(() => hk.Register(taken.Value, () => { }));       // Win32Exception
                Check.True(ex.Message.Contains("this GlobalHotkeys"), ex.Message);
                Check.Equal(1, hk.Count);
            });
        }

        public void Hotkeys_RegisterAfterDispatcherShutdownThrows()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var hk = new GlobalHotkeys();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
                try
                {
                    HotkeyGesture g = HotkeyGesture.Parse(Candidates[0]);
                    Check.Throws<ObjectDisposedException>(() => hk.TryRegister(g, () => { }, out _));               // true: a dead thread hotkey
                    Check.Equal(0, hk.Count);
                }
                finally { hk.Dispose(); }
            });
        }

        public void Theme_FollowSystemSurvivesABrokenDictionary()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tbwpf_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string good = Path.Combine(dir, "good.xaml"), bad = Path.Combine(dir, "bad.xaml");
            File.WriteAllText(good, "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'/>");
            File.WriteAllText(bad, "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Broken");
            try
            {
                FileDropBehaviorTests.RunSta(() =>
                {
                    AppTheme sys = ThemeManager.GetSystemTheme();
                    AppTheme other = sys == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
                    using var tm = new ThemeManager(new ResourceDictionary());
                    tm.Register(sys, new Uri(good)).Register(other, new Uri(good));
                    tm.Apply(AppTheme.System);
                    tm.FollowSystem = true;
                    tm.Register(sys, new Uri(bad));
                    Exception? unhandled = null, reported = null;
                    Dispatcher.CurrentDispatcher.UnhandledException += (_, e) => { unhandled = e.Exception; e.Handled = true; };
                    EventInfo? failed = typeof(ThemeManager).GetEvent("SystemThemeApplyFailed");
                    failed?.AddEventHandler(tm, new EventHandler<Exception>((_, ex) => reported = ex));
                    typeof(ThemeManager).GetMethod("OnUserPreferenceChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(tm, new object?[] { null, new UserPreferenceChangedEventArgs(UserPreferenceCategory.General) });
                    Pump();
                    Check.Null(unhandled, "the OS theme switch crashed the app: " + unhandled?.GetType().Name);
                    Check.NotNull(failed, "no SystemThemeApplyFailed event");
                    Check.True(reported is XamlParseException, reported?.GetType().Name ?? "nothing reported");
                    Check.Equal(sys, tm.Effective!.Value);
                });
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Dialogs_WindowWithoutHandleOwnsNoFileDialog()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var unshown = new Window();
                var svc = new WpfDialogService(() => unshown);
                MethodInfo? owner = typeof(WpfDialogService).GetMethod("FileDialogOwner", BindingFlags.NonPublic | BindingFlags.Instance);
                Check.NotNull(owner, "file dialogs are given the owner as is (an unshown window threw InvalidOperationException)");
                Check.Null(owner!.Invoke(svc, null));
                unshown.Close();
            });
        }

        public void BindingListener_LeavesOtherTraceSourcesAlone()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                TraceSource routed = PresentationTraceSources.RoutedEventSource;
                SourceLevels original = routed.Switch.Level;
                try
                {
                    routed.Switch.Level = SourceLevels.Verbose;
                    using (new BindingFailureListener())
                        Check.Equal(SourceLevels.Verbose, PresentationTraceSources.RoutedEventSource.Switch.Level);     // Off
                    Check.Equal(SourceLevels.Verbose, PresentationTraceSources.RoutedEventSource.Switch.Level);
                }
                finally { PresentationTraceSources.RoutedEventSource.Switch.Level = original; }
            });
        }

        public void Incremental_OneCallersTokenCancelsOnlyItsWait()
        {
            var gate = new TaskCompletionSource<IReadOnlyList<int>>();
            var ic = new IncrementalCollection<int>((skip, take, ct) => { ct.Register(() => gate.TrySetCanceled(ct)); return gate.Task; }, 10);
            var ctsA = new CancellationTokenSource();
            Task<int> a = ic.LoadMoreAsync(ctsA.Token);
            Task<int> b = ic.LoadMoreAsync(CancellationToken.None);
            ctsA.Cancel();
            Check.True(SpinUntil(() => a.IsCompleted), "a was not cancelled");
            Check.True(a.IsCanceled);
            Check.False(b.IsCompleted, "b ended with " + b.Status);
            gate.TrySetResult(new[] { 1, 2 });
            Check.True(b.Wait(5000));
            Check.Equal(2, b.Result);
            Check.Equal(2, ic.Count);

            // Every waiter cancelling still cancels the load.
            var gate2 = new TaskCompletionSource<IReadOnlyList<int>>();
            var ic2 = new IncrementalCollection<int>((skip, take, ct) => { ct.Register(() => gate2.TrySetCanceled(ct)); return gate2.Task; }, 10);
            var c1 = new CancellationTokenSource();
            var c2 = new CancellationTokenSource();
            Task<int> x = ic2.LoadMoreAsync(c1.Token), y = ic2.LoadMoreAsync(c2.Token);
            c1.Cancel();
            c2.Cancel();
            Check.True(SpinUntil(() => x.IsCompleted && y.IsCompleted && !ic2.IsLoading));
            Check.True(x.IsCanceled && y.IsCanceled, x.Status + " " + y.Status);
            Check.True(gate2.Task.IsCanceled);
            Check.True(ic2.HasMoreItems);
        }

        public void Incremental_ResetIsCompleteWhenATokenCallbackThrows()
        {
            var gate = new TaskCompletionSource<IReadOnlyList<int>>();
            int calls = 0;
            var ic = new IncrementalCollection<int>((skip, take, ct) =>
            {
                calls++;
                if (calls == 1) { ct.Register(() => throw new InvalidOperationException("callback")); return gate.Task; }
                return Task.FromResult<IReadOnlyList<int>>(new[] { 7 });
            }, 10);
            ic.Add(99);
            _ = ic.LoadMoreAsync();
            Check.Throws<AggregateException>(() => ic.ResetAsync());
            Check.Equal(0, ic.Count);                     // [99]
            Check.False(ic.IsLoading);                     // stuck true
            gate.TrySetResult(new[] { 1, 2 });             // the stale page is dropped
            Task<int> next = ic.LoadMoreAsync();
            Check.True(next.Wait(5000));
            Check.True(SpinUntil(() => !ic.IsLoading));
            Check.Equal("7", string.Join(",", ic));
        }

        private static bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, 5000);

        public void NotifyTaskCompletion_ThrowingHandlerDoesNotStopTheRest()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Exception? unhandled = null;
                Dispatcher.CurrentDispatcher.UnhandledException += (_, e) => { unhandled = e.Exception; e.Handled = true; };
                var tcs = new TaskCompletionSource<int>();
                var ntc = new NotifyTaskCompletion<int>(tcs.Task);
                var seen = new List<string?>();
                int second = 0;
                ntc.PropertyChanged += (_, e) => { seen.Add(e.PropertyName); if (e.PropertyName == "Status") throw new InvalidOperationException("handler"); };
                ntc.PropertyChanged += (_, __) => second++;
                tcs.SetResult(5);
                Pump();
                Pump();
                Check.Equal(10, seen.Count);                   // only "Status"
                Check.Equal(10, second);
                Check.True(seen.Contains(nameof(ntc.Result)) && seen.Contains(nameof(ntc.IsCompleted)));
                Check.True(unhandled is InvalidOperationException, unhandled?.GetType().Name ?? "the handler's exception was lost");
            });
        }

        private sealed class ThrowingData : IDataObject
        {
            public object GetData(string format) => throw new COMException("DV_E_FORMATETC", unchecked((int)0x80040064));
            public object GetData(Type format) => GetData(format.FullName!);
            public object GetData(string format, bool autoConvert) => GetData(format);
            public bool GetDataPresent(string format) => true;
            public bool GetDataPresent(Type format) => true;
            public bool GetDataPresent(string format, bool autoConvert) => true;
            public string[] GetFormats() => new[] { DataFormats.FileDrop };
            public string[] GetFormats(bool autoConvert) => GetFormats();
            public void SetData(object data) { }
            public void SetData(string format, object data) { }
            public void SetData(Type format, object data) { }
            public void SetData(string format, object data, bool autoConvert) { }
        }

        private static DragDropEffects RaiseDrag(UIElement element, IDataObject data, RoutedEvent routedEvent)
        {
            ConstructorInfo ctor = typeof(DragEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            object?[] args = ctor.GetParameters().Select(p =>
                p.ParameterType == typeof(IDataObject) ? data
                : p.ParameterType == typeof(DependencyObject) ? element
                : p.ParameterType == typeof(DragDropEffects) ? DragDropEffects.All
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            var e = (DragEventArgs)ctor.Invoke(args);
            e.RoutedEvent = routedEvent;
            element.RaiseEvent(e);
            return e.Effects;
        }

        public void FileDrop_RefilledDataObjectIsRefiltered()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var border = new Border();
                FileDropBehavior.SetExtensions(border, ".csv");
                FileDropBehavior.SetCommand(border, new RelayCommand<string[]>(_ => { }, f => f != null && f.Length > 0));
                var data = new DataObject(DataFormats.FileDrop, new[] { @"C:\nope\a.txt" });
                Check.Equal(DragDropEffects.None, RaiseDrag(border, data, DragDrop.PreviewDragOverEvent));
                data.SetData(DataFormats.FileDrop, new[] { @"C:\nope\a.csv" });
                Check.Equal(DragDropEffects.Copy, RaiseDrag(border, data, DragDrop.PreviewDragOverEvent));          // None: stale cache
                data.SetData(DataFormats.FileDrop, new[] { @"C:\nope\b.txt" });
                Check.Equal(DragDropEffects.None, RaiseDrag(border, data, DragDrop.PreviewDragOverEvent));
            });
        }

        public void FileDrop_FailingDropSourceIsNoFiles()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var border = new Border();
                FileDropBehavior.SetCommand(border, new RelayCommand<string[]>(_ => { }));
                Check.Equal(0, FileDropBehavior.GetFiles(new ThrowingData(), null).Length);                       // COMException
                Check.Equal(DragDropEffects.None, RaiseDrag(border, new ThrowingData(), DragDrop.PreviewDragOverEvent));
                RaiseDrag(border, new ThrowingData(), DragDrop.PreviewDropEvent);
            });
        }
    }
}
