using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>Round 82 review regressions (WPF satellite, fifth pass).</summary>
    public sealed class WpfReview82Tests
    {
        private static bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, 5000);

        private static DragEventArgs RaiseDrag(DependencyObject element, IDataObject data, RoutedEvent routedEvent, DragDropEffects allowed)
        {
            ConstructorInfo ctor = typeof(DragEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            object?[] args = ctor.GetParameters().Select(p =>
                p.ParameterType == typeof(IDataObject) ? data
                : p.ParameterType == typeof(DependencyObject) ? element
                : p.ParameterType == typeof(DragDropEffects) ? allowed
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            var e = (DragEventArgs)ctor.Invoke(args);
            e.RoutedEvent = routedEvent;
            ((IInputElement)element).RaiseEvent(e);
            return e;
        }

        private sealed class Command : ICommand
        {
            public int Runs;
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => Runs++;
        }

        private static IDataObject Files(params string[] paths) => new DataObject(DataFormats.FileDrop, paths);

        // ---------- BindingDiagnostics ----------

        // Refresh() runs Trace.Refresh(): the app's own sources and switches went back to their defaults, and a listener
        // it added to Trace.Listeners was gone.
        public void BindingDiagnostics_KeepsTheAppsTraceLevelsAndListeners()
        {
            var source = new TraceSource("ToolBelt.Review82.App", SourceLevels.All);
            source.Switch.Level = SourceLevels.Warning;
            var traceSwitch = new TraceSwitch("ToolBelt.Review82.Switch", "test", "Verbose") { Level = TraceLevel.Error };
            var mine = new DefaultTraceListener { Name = "Review82" };
            Trace.Listeners.Add(mine);
            try
            {
                using (new BindingFailureListener())
                {
                    Check.Equal(SourceLevels.Warning, source.Switch.Level);
                    Check.Equal(TraceLevel.Error, traceSwitch.Level);
                    Check.True(Trace.Listeners.Contains(mine), "the app's Trace listener was removed");
                }
            }
            finally { Trace.Listeners.Remove(mine); }
            GC.KeepAlive(source);
            GC.KeepAlive(traceSwitch);
        }

        // At the cap every failure shifted the whole list down by one.
        public void BindingDiagnostics_AFullListDropsTheOldestInConstantTime()
        {
            using var listener = new BindingFailureListener { MaxFailures = 200_000 };
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 400_000; i++)
                listener.WriteLine("System.Windows.Data Error: 40 : failure " + i.ToString(CultureInfo.InvariantCulture));
            watch.Stop();
            Check.Equal(200_000, listener.Failures.Count);
            Check.Equal(200_000L, listener.DroppedFailures);
            Check.True(listener.Failures[0].Message.EndsWith(" 200000", StringComparison.Ordinal), listener.Failures[0].Message);
            Check.True(watch.Elapsed < TimeSpan.FromSeconds(20), "took " + watch.Elapsed);
        }

        public void BindingFailureException_NullFailureIsRejected()
        {
            var ex = Check.Throws<ArgumentNullException>(() => new BindingFailureException(null!));
            Check.Equal("failure", ex.ParamName);
        }

        // ---------- IncrementalCollection ----------

        // A caller arriving while an abandoned load wound down waited for it with no regard for its own token.
        public void Incremental_CallerBehindAnAbandonedLoadHonoursItsToken()
        {
            var gate = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            var c = new IncrementalCollection<int>((skip, take, ct) =>
                Interlocked.Increment(ref calls) == 1 ? gate.Task : Task.FromResult<IReadOnlyList<int>>(new[] { 1 }), 10);
            using var first = new CancellationTokenSource();
            Task<int> a = c.LoadMoreAsync(first.Token);
            first.Cancel();                              // the only waiter leaves: the load is cancelling, but its loader ignores that
            using var second = new CancellationTokenSource();
            Task<int> b = c.LoadMoreAsync(second.Token);
            second.Cancel();
            Check.True(SpinUntil(() => b.IsCompleted), "the second caller's cancel did nothing while the old load ran");
            Check.True(b.IsCanceled);
            gate.SetResult(Array.Empty<int>());
            Check.True(SpinUntil(() => a.IsCompleted));
        }

        // ---------- ValidationRules ----------

        private sealed class Form : RuleValidatedObject
        {
            private int _a = 5, _b = 5, _c = 5;
            public bool Armed;

            public Form()
            {
                Rules.For(nameof(B), () => B).Must(b => Armed ? throw new InvalidOperationException("rule bug") : b >= A, "B < A").DependsOn(nameof(A));
                Rules.For(nameof(C), () => C).Must(c => c >= A, "C < A").DependsOn(nameof(A));
            }

            public int A { get => _a; set => SetAndValidate(ref _a, value); }
            public int B { get => _b; set => SetAndValidate(ref _b, value); }
            public int C { get => _c; set => SetAndValidate(ref _c, value); }
            public void SetC(int value) => _c = value;
        }

        // One throwing rule left every property after it with stale errors.
        public void Rules_AThrowingRuleStillValidatesTheOthers()
        {
            var f = new Form();
            f.SetC(0);
            f.A = 5;                                                  // C (0) < A (5): C has an error
            Check.Equal(1, f.GetErrors(nameof(Form.C)).Cast<object>().Count());
            f.Armed = true;
            Check.Throws<InvalidOperationException>(() => f.A = 0);
            Check.Equal(0, f.GetErrors(nameof(Form.C)).Cast<object>().Count());   // C (0) >= A (0) now

            var g = new Form();
            g.SetC(0);
            g.Armed = true;
            Check.Throws<InvalidOperationException>(() => g.ValidateAll());
            Check.Equal(1, g.GetErrors(nameof(Form.C)).Cast<object>().Count());   // ValidateAll stopped before C
        }

        public void Rules_StringRangeIsOrdinal()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                foreach (string name in new[] { "cs-CZ", "da-DK", "en-US" })
                {
                    CultureInfo.CurrentCulture = new CultureInfo(name);
                    string value = "chyba";
                    var set = new ValidationRuleSet();
                    set.For("S", () => value).Range("a", "d");
                    Check.Equal(0, set.Validate("S").Count, name + " " + value);
                    value = "aa";
                    Check.Equal(0, set.Validate("S").Count, name + " " + value);
                    value = "e";
                    Check.Equal(1, set.Validate("S").Count, name + " " + value);
                }
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        public void Rules_NegativeLengthsAreRejected()
        {
            var set = new ValidationRuleSet();
            Check.Equal("length", Check.Throws<ArgumentOutOfRangeException>(() => set.For("S", () => "").MaxLength(-1)).ParamName);
            Check.Equal("length", Check.Throws<ArgumentOutOfRangeException>(() => set.For("S", () => "").MinLength(-5)).ParamName);
            set.For("S", () => "").MaxLength(0).MinLength(0);
            Check.Equal(0, set.Validate("S").Count);
        }

        // AllowThousands let "1,5" through as 15 under en-US; WPF's DoubleConverter then failed to convert it.
        public void NumericRangeRule_GroupSeparatorsAreNotANumber()
        {
            var rule = new NumericRangeValidationRule();
            var en = new CultureInfo("en-US");
            Check.False(rule.Validate("1,5", en).IsValid);
            Check.False(rule.Validate("5,", en).IsValid);
            Check.True(rule.Validate("1.5e3", en).IsValid);
            Check.True(rule.Validate("1,5", new CultureInfo("de-DE")).IsValid);
        }

        public void RegexRule_DefaultPatternAcceptsMultiLineText()
        {
            var rule = new RegexValidationRule();
            Check.True(rule.Validate("a\nb", CultureInfo.InvariantCulture).IsValid);
            Check.True(rule.Validate("a\r\nb\n", CultureInfo.InvariantCulture).IsValid);
        }

        private sealed class Errors : ValidationObservableObject
        {
            public void Set(string name, params string[] errors) => SetErrors(name, errors);
        }

        public void SetErrors_NullEntryIsRejected()
        {
            var e = new Errors();
            Check.Equal("errors", Check.Throws<ArgumentException>(() => e.Set("P", "ok", null!)).ParamName);
            Check.False(e.HasErrors);
        }

        // ---------- NotifyTaskCompletion ----------

        // Completion resumed on the creating dispatcher, and one that had shut down never ran it.
        public void NotifyTask_CompletesAfterTheCreatingDispatcherShutsDown()
        {
            var tcs = new TaskCompletionSource<int>();
            NotifyTaskCompletion<int>? watcher = null;
            int raised = 0;
            var thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                watcher = new NotifyTaskCompletion<int>(tcs.Task);
                watcher.PropertyChanged += (s, e) => raised++;
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            tcs.SetResult(1);
            Check.True(watcher!.Completion.Wait(5000), "Completion never completed");
            Check.Equal(1, watcher.Result);
            Check.Equal(0, raised);
        }

        public void NotifyTask_RaisesOnTheCreatingDispatcher()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext? previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                try
                {
                    var tcs = new TaskCompletionSource<int>();
                    var watcher = new NotifyTaskCompletion<int>(tcs.Task);
                    var threads = new List<int>();
                    watcher.PropertyChanged += (s, e) => threads.Add(Environment.CurrentManagedThreadId);
                    Task.Run(() => tcs.SetResult(7));
                    var frame = new DispatcherFrame();
                    watcher.Completion.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
                    Dispatcher.PushFrame(frame);
                    Check.True(threads.Count > 0);
                    Check.True(threads.All(t => t == Environment.CurrentManagedThreadId), "raised off the UI thread");
                    Check.Equal(7, watcher.Result);
                }
                finally { SynchronizationContext.SetSynchronizationContext(previous); }
            });
        }

        // ---------- Messenger ----------

        // Send walked every recipient of every message type to find the few for one type.
        public void Messenger_SendCostsOnlyThatTypesRecipients()
        {
            var m = new Messenger();
            var keep = new List<object>();
            for (int i = 0; i < 200_000; i++)
            {
                var r = new object();
                keep.Add(r);
                m.Register<string>(r, _ => { });
            }
            var target = new object();
            int hits = 0;
            m.Register<int>(target, _ => hits++);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 300; i++) m.Send(i);
            watch.Stop();
            Check.Equal(300, hits);
            Check.True(watch.Elapsed < TimeSpan.FromSeconds(1), "300 sends took " + watch.Elapsed);
            GC.KeepAlive(keep);
        }

        public void Messenger_IndexKeepsOrderAndNeverDeliversTwice()
        {
            var m = new Messenger();
            var log = new List<string>();
            object a = new object(), b = new object();
            m.Register<int>(a, _ => { });
            m.Register<string>(b, s => log.Add("b" + s));
            m.Register<string>(a, s => log.Add("a" + s));
            m.Send("1");
            Check.Equal("b1,a1", string.Join(",", log));
            log.Clear();
            m.Unregister<string>(a);
            m.Register<string>(a, s => log.Add("a" + s));      // re-registered before any Send pruned it
            m.UnregisterAll(b);
            m.Register<string>(b, s => log.Add("b" + s));
            m.Send("2");
            Check.Equal("a2,b2", string.Join(",", log));
            log.Clear();
            m.Clear();
            m.Send("3");
            Check.Equal(0, log.Count);
            m.Register<string>(a, s => log.Add("a" + s));
            m.Send("4");
            Check.Equal("a4", string.Join(",", log));
        }

        public void Messenger_IndexDropsCollectedRecipients()
        {
            var m = new Messenger();
            WeakReference[] dead = RegisterGarbage(m, 1000);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Check.True(dead.All(w => !w.IsAlive), "the index kept recipients alive");
            var live = new object();
            int hits = 0;
            m.Register<string>(live, _ => hits++);
            m.Send("x");
            Check.Equal(1, hits);
        }

        private static WeakReference[] RegisterGarbage(Messenger m, int count)
        {
            var refs = new WeakReference[count];
            for (int i = 0; i < count; i++)
            {
                var r = new object();
                m.Register<string>(r, _ => GC.KeepAlive(r));
                refs[i] = new WeakReference(r);
            }
            return refs;
        }

        // ---------- FileDropBehavior ----------

        public void FileDrop_NoFilterSkipsNullsAndCopies()
        {
            string[] raw = { "a.txt", null!, "b.txt" };
            string[] files = FileDropBehavior.GetFiles(new DataObject(DataFormats.FileDrop, raw), null);
            Check.Equal("a.txt|b.txt", string.Join("|", files));
            Check.False(ReferenceEquals(raw, files));
        }

        // Copy was shown when the source allowed only Move, and a drop returned every allowed effect, Move included.
        public void FileDrop_EffectsAreASubsetOfTheAllowed()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var target = new Border();
                var command = new Command();
                FileDropBehavior.SetCommand(target, command);
                Check.Equal(DragDropEffects.None, RaiseDrag(target, Files("a.txt"), UIElement.PreviewDragOverEvent, DragDropEffects.Move).Effects);
                Check.Equal(DragDropEffects.Copy, RaiseDrag(target, Files("a.txt"), UIElement.PreviewDragOverEvent, DragDropEffects.All).Effects);
                DragEventArgs drop = RaiseDrag(target, Files("a.txt"), UIElement.PreviewDropEvent, DragDropEffects.Copy | DragDropEffects.Move);
                Check.Equal(1, command.Runs);
                Check.Equal(DragDropEffects.Copy, drop.Effects);
                DragEventArgs moveOnly = RaiseDrag(target, Files("a.txt"), UIElement.PreviewDropEvent, DragDropEffects.Move);
                Check.Equal(1, command.Runs);
                Check.Equal(DragDropEffects.None, moveOnly.Effects);
            });
        }

        // Writing back the effective AllowDrop made it local: the element stopped inheriting it.
        public void FileDrop_ClearingRestoresAnInheritedAllowDrop()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var child = new Border();
                var parent = new Border { Child = child };
                FileDropBehavior.SetCommand(child, new Command());
                Check.True(child.AllowDrop);
                FileDropBehavior.SetCommand(child, null);
                Check.True(ReferenceEquals(DependencyProperty.UnsetValue, child.ReadLocalValue(UIElement.AllowDropProperty)), "AllowDrop left local");
                parent.AllowDrop = true;
                Check.True(child.AllowDrop);

                var local = new Border { AllowDrop = true };
                FileDropBehavior.SetCommand(local, new Command());
                FileDropBehavior.SetCommand(local, null);
                Check.Equal(true, local.ReadLocalValue(UIElement.AllowDropProperty));
            });
        }

        // A Command on a Run got no handlers, yet made the outer target step aside: nobody took the drop.
        public void FileDrop_ContentElementTargetTakesTheDrop()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var run = new Run("drop here");
                var text = new TextBlock(run);
                var outer = new Border { Child = text };
                Command outerCommand = new Command(), runCommand = new Command();
                FileDropBehavior.SetCommand(outer, outerCommand);
                FileDropBehavior.SetCommand(run, runCommand);
                Check.True(run.AllowDrop);
                DragEventArgs e = RaiseDrag(run, Files("a.txt"), UIElement.PreviewDropEvent, DragDropEffects.All);
                Check.Equal(1, runCommand.Runs);
                Check.Equal(0, outerCommand.Runs);
                Check.True(e.Handled);
            });
        }

        // ---------- NumericInputBehavior ----------

        // TextBox inserts only what MaxLength leaves room for: "1." into "5" was checked as "1.5" and became 15.
        public void Numeric_PasteIsCheckedAsMaxLengthTruncatesIt()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                CultureInfo previous = CultureInfo.CurrentCulture;
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                try
                {
                    var box = new TextBox { MaxLength = 2, Text = "5" };
                    NumericInputBehavior.SetIsEnabled(box, true);
                    NumericInputBehavior.SetMaximum(box, 9);
                    NumericInputBehavior.SetDecimalPlaces(box, 1);
                    box.CaretIndex = 0;
                    var e = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, "1."), false, DataFormats.UnicodeText);
                    box.RaiseEvent(e);
                    Check.True(e.CommandCancelled, "15 > 9 got in");
                    box.CaretIndex = 1;
                    var ok = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, ".25"), false, DataFormats.UnicodeText);
                    box.Text = "";
                    box.RaiseEvent(ok);
                    Check.False(ok.CommandCancelled, ".2 fits");
                }
                finally { CultureInfo.CurrentCulture = previous; }
            });
        }

        // ---------- PlotPresenter ----------

        public void Plot_MaxZoomBelowMinZoomReadsAsMinZoom()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var p = new PlotPresenter { MinZoom = 5, MaxZoom = 1 };
                Check.Equal(5.0, p.MaxZoom);
                var q = new PlotPresenter { MaxZoom = 200, MinZoom = 100 };   // either order
                Check.Equal(100.0, q.MinZoom);
                Check.Equal(200.0, q.MaxZoom);
            });
        }

        public void Plot_ZoomAboutRejectsBadBounds()
        {
            var view = Matrix.Identity;
            var at = new Point(1, 1);
            Check.Equal("minZoom", Check.Throws<ArgumentOutOfRangeException>(() => PlotPresenter.ZoomAbout(view, at, 2, double.NaN, 10)).ParamName);
            Check.Equal("maxZoom", Check.Throws<ArgumentOutOfRangeException>(() => PlotPresenter.ZoomAbout(view, at, 2, 1, double.NaN)).ParamName);
            Check.Equal("maxZoom", Check.Throws<ArgumentOutOfRangeException>(() => PlotPresenter.ZoomAbout(view, at, 2, 5, 1)).ParamName);
            Check.Equal(2.0, PlotPresenter.ZoomAbout(view, at, 2, 1, 1e9).M11);
        }

        // ---------- ValueConverters ----------

        public void CountToVisibility_FalseIsCollapsed()
        {
            var c = new CountToVisibilityConverter();
            Check.Equal(Visibility.Collapsed, c.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture));
            Check.Equal(Visibility.Visible, c.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture));
            Check.Equal(Visibility.Visible, new CountToVisibilityConverter { Invert = true }.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture));
        }

        // ---------- ThemeManager ----------

        // Dispatcher.CurrentDispatcher made a dispatcher nothing pumped, so following the system theme silently did nothing.
        public void Theme_OffAUiThreadDoesNotMakeADispatcher()
        {
            if (Application.Current != null) return;   // the application's dispatcher is used then
            Exception? failure = null;
            bool madeDispatcher = true;
            var thread = new Thread(() =>
            {
                try
                {
                    var tm = new ThemeManager(new ResourceDictionary());
                    madeDispatcher = Dispatcher.FromThread(Thread.CurrentThread) != null;
                    failure = Check.Throws<InvalidOperationException>(() => tm.FollowSystem = true);
                    tm.Dispose();
                }
                catch (Exception ex) { failure = ex; madeDispatcher = true; }
            });
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            thread.Join();
            Check.False(madeDispatcher, "a dispatcher was made on an MTA thread: " + failure);
            Check.True(failure is InvalidOperationException, "" + failure);
        }
    }
}
