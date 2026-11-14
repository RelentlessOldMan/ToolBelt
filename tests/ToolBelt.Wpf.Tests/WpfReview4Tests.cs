using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>Round 73 review regressions (WPF satellite, fourth pass).</summary>
    public sealed class WpfReview4Tests
    {
        private static NumericInputRules Rules(double min = double.NegativeInfinity, double max = double.PositiveInfinity, int dp = 0)
            => new NumericInputRules { Minimum = min, Maximum = max, DecimalPlaces = dp, Culture = CultureInfo.InvariantCulture };

        private static bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, 5000);

        private static DragEventArgs RaiseDrag(UIElement element, IDataObject data, RoutedEvent routedEvent)
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
            return e;
        }

        private sealed class Vm
        {
            public double Value { get; set; }
        }

        // ---------- NumericInputBehavior ----------

        public void Numeric_BoundBoxUsesTheBindingsCulture()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var previous = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                    var box = new TextBox();
                    box.SetBinding(TextBox.TextProperty, new Binding(nameof(Vm.Value)) { Source = new Vm() });
                    // The binding reads with WPF's en-US default Language: "1,5" would become 15.
                    Check.Equal(".", NumericInputBehavior.RulesFor(box).Culture!.NumberFormat.NumberDecimalSeparator);
                    var german = new TextBox();
                    german.SetBinding(TextBox.TextProperty, new Binding(nameof(Vm.Value)) { Source = new Vm(), ConverterCulture = new CultureInfo("de-DE") });
                    Check.Equal(",", NumericInputBehavior.RulesFor(german).Culture!.NumberFormat.NumberDecimalSeparator);
                    Check.Equal(",", NumericInputBehavior.RulesFor(new TextBox()).Culture!.NumberFormat.NumberDecimalSeparator);   // unbound: current culture
                }
                finally { CultureInfo.CurrentCulture = previous; }
            });
        }

        public void Numeric_OvertypeModeCannotBeTurnedOn()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var box = new TextBox { Language = XmlLanguage.GetLanguage("en-US") };
                NumericInputBehavior.SetIsEnabled(box, true);
                EditingCommands.ToggleInsert.Execute(null, box);
                object editor = typeof(TextBoxBase).GetProperty("TextEditor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(box)!;
                FieldInfo overtype = editor.GetType().GetField("_overtypeMode", BindingFlags.NonPublic | BindingFlags.Instance)!;
                Check.False((bool)overtype.GetValue(editor)!, "overtype mode was turned on");
                NumericInputBehavior.SetIsEnabled(box, false);
                EditingCommands.ToggleInsert.Execute(null, box);
                Check.True((bool)overtype.GetValue(editor)!, "a plain box still toggles");
            });
        }

        public void Numeric_TextDropsAreRefused()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var box = new TextBox();
                NumericInputBehavior.SetIsEnabled(box, true);
                DragEventArgs over = RaiseDrag(box, new DataObject(DataFormats.UnicodeText, "abc"), DragDrop.PreviewDragOverEvent);
                Check.True(over.Handled, "a text drag was not refused");
                Check.Equal(DragDropEffects.None, over.Effects);
                Check.True(RaiseDrag(box, new DataObject(DataFormats.UnicodeText, "abc"), DragDrop.PreviewDropEvent).Handled);
                Check.False(RaiseDrag(box, new DataObject(DataFormats.FileDrop, new[] { @"C:\nope\a.csv" }), DragDrop.PreviewDragOverEvent).Handled);
            });
        }

        private sealed class ThrowingText : IDataObject
        {
            public object GetData(string format) => throw new COMException("DV_E_FORMATETC", unchecked((int)0x80040064));
            public object GetData(Type format) => GetData(format.FullName!);
            public object GetData(string format, bool autoConvert) => GetData(format);
            public bool GetDataPresent(string format) => true;
            public bool GetDataPresent(Type format) => true;
            public bool GetDataPresent(string format, bool autoConvert) => true;
            public string[] GetFormats() => new[] { DataFormats.UnicodeText };
            public string[] GetFormats(bool autoConvert) => GetFormats();
            public void SetData(object data) { }
            public void SetData(string format, object data) { }
            public void SetData(Type format, object data) { }
            public void SetData(string format, object data, bool autoConvert) { }
        }

        public void Numeric_PasteFromAFailingSourceIsCancelled()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var box = new TextBox();
                NumericInputBehavior.SetIsEnabled(box, true);
                var e = new DataObjectPastingEventArgs(new ThrowingText(), false, DataFormats.UnicodeText);
                box.RaiseEvent(e);                                                       // COMException
                Check.True(e.CommandCancelled);
            });
        }

        public void Numeric_EmptyNegativeSignCultureStillTakesDigits()
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NegativeSign = "";
            var rules = new NumericInputRules { Minimum = 0, Culture = culture };
            Check.True(NumericInputBehavior.IsAcceptablePartial("5", rules), "every keystroke was refused");
            Check.Equal("5", NumericInputBehavior.Coerce("5", rules));
            var signed = new NumericInputRules { Minimum = -10, Culture = culture };
            Check.True(NumericInputBehavior.IsAcceptablePartial("-5", signed));
        }

        public void Numeric_StepMovesFromTwoToTheFiftyThird()
        {
            Check.Equal("9007199254740994", NumericInputBehavior.StepValue("9007199254740992", +1, 1, Rules()));   // stuck
            Check.Equal("-9007199254740994", NumericInputBehavior.StepValue("-9007199254740992", -1, 1, Rules()));
            string huge = NumericInputBehavior.StepValue("1" + new string('0', 300), +1, 1, Rules());
            Check.True(double.Parse(huge, CultureInfo.InvariantCulture) > 1e300, huge);
        }

        // ---------- FileDropBehavior ----------

        public void FileDrop_InnermostTargetDecides()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var ran = new List<string>();
                var inner = new Border();
                var outer = new Border { Child = inner };
                FileDropBehavior.SetExtensions(outer, ".csv");
                FileDropBehavior.SetCommand(outer, new RelayCommand<string[]>(_ => ran.Add("outer"), f => f != null && f.Length > 0));
                FileDropBehavior.SetExtensions(inner, ".png");
                FileDropBehavior.SetCommand(inner, new RelayCommand<string[]>(_ => ran.Add("inner"), f => f != null && f.Length > 0));

                var png = new DataObject(DataFormats.FileDrop, new[] { @"C:\nope\a.png" });
                Check.Equal(DragDropEffects.Copy, RaiseDrag(inner, png, DragDrop.PreviewDragOverEvent).Effects);    // None: outer refused it
                RaiseDrag(inner, png, DragDrop.PreviewDropEvent);
                Check.Equal("inner", string.Join(",", ran));

                ran.Clear();
                var csv = new DataObject(DataFormats.FileDrop, new[] { @"C:\nope\a.csv" });
                Check.Equal(DragDropEffects.None, RaiseDrag(inner, csv, DragDrop.PreviewDragOverEvent).Effects);    // the inner filter decides
                RaiseDrag(inner, csv, DragDrop.PreviewDropEvent);
                Check.Equal("", string.Join(",", ran));                                                              // outer ran
                RaiseDrag(outer, csv, DragDrop.PreviewDropEvent);                                                    // dropped on the outer one itself
                Check.Equal("outer", string.Join(",", ran));
            });
        }

        // ---------- validation ----------

        private sealed class Entity : ValidationObservableObject
        {
            public void Set(string name, params string[] errors) => SetErrors(name, errors);
        }

        public void Validation_GetErrorsEmptyMeansObjectLevel()
        {
            var m = new Entity();
            m.Set("Name", "required");
            Check.Equal(0, m.GetErrors(null).Cast<object>().Count());                // every property's errors
            Check.Equal(0, m.GetErrors("").Cast<object>().Count());
            m.Set("", "the whole thing is wrong");
            Check.Equal("the whole thing is wrong", string.Join("|", m.GetErrors("").Cast<object>()));
            Check.Equal("the whole thing is wrong", string.Join("|", m.GetErrors(null).Cast<object>()));
            Check.Equal("required", string.Join("|", m.GetErrors("Name").Cast<object>()));
            Check.True(m.HasErrors);
        }

        public void Regex_PatternCannotEscapeTheWholeValueAnchor()
        {
            Check.Throws<ArgumentException>(() => new RegexValidationRule { Pattern = "a)|(b" });                 // accepted, then matched "abc"
            Check.Throws<ArgumentException>(() => new RegexValidationRule { Pattern = @"\d+)|(?:" });
            Check.Throws<ArgumentException>(() => new ValidationRuleSet().For("Code", () => "abc").Matches("a)|(b"));

            var commented = new RegexValidationRule { Pattern = @"(?x) \d+   # digits only" };                    // rejected as invalid
            Check.True(commented.Validate("123", CultureInfo.InvariantCulture).IsValid);
            Check.False(commented.Validate("123a", CultureInfo.InvariantCulture).IsValid);
            var set = new ValidationRuleSet();
            string value = "12";
            set.For("Code", () => value).Matches(@"(?x) \d+ # digits");
            Check.Equal(0, set.Validate("Code").Count);
            value = "12x";
            Check.Equal(1, set.Validate("Code").Count);
        }

        // ---------- ThemeManager ----------

        public void Theme_UndefinedThemeIsRejected()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var tm = new ThemeManager(new ResourceDictionary())
                    .Register(AppTheme.Light, new ResourceDictionary())
                    .Register(AppTheme.Dark, new ResourceDictionary());
                var ex = Check.Throws<ArgumentOutOfRangeException>(() => tm.Apply((AppTheme)42));                 // applied Light as "42"
                Check.Equal("theme", ex.ParamName);
                Check.Null(tm.Effective);
                Check.Equal("theme", Check.Throws<ArgumentOutOfRangeException>(() => ThemeManager.CreateDefaultPalette((AppTheme)42)).ParamName);
            });
        }

        public void Theme_UriThemeAdoptsTheCopyTheAppMerged()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tbwpf_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string light = Path.Combine(dir, "Light.xaml");
            File.WriteAllText(light, "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" "
                + "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><SolidColorBrush x:Key=\"OnlyLight\" Color=\"Red\"/></ResourceDictionary>");
            try
            {
                FileDropBehaviorTests.RunSta(() =>
                {
                    var uri = new Uri(light);
                    var target = new ResourceDictionary();
                    target.MergedDictionaries.Add(new ResourceDictionary { Source = uri });                       // as App.xaml would
                    var dark = new ResourceDictionary { ["DarkKey"] = "D" };
                    var tm = new ThemeManager(target).Register(AppTheme.Light, uri).Register(AppTheme.Dark, dark);
                    tm.Apply(AppTheme.Light);
                    Check.Equal(1, target.MergedDictionaries.Count);                                               // 2: a second copy
                    tm.Apply(AppTheme.Dark);
                    Check.Equal(1, target.MergedDictionaries.Count);
                    Check.Null(target["OnlyLight"]);                                                               // the App.xaml copy survived
                    Check.Equal("D", target["DarkKey"]);
                });
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        // ---------- GlobalHotkeys ----------

        private static readonly string[] Candidates =
        {
            "Ctrl+Alt+Shift+F24", "Ctrl+Alt+Shift+F23", "Ctrl+Alt+Shift+F22", "Ctrl+Alt+Win+F21", "Ctrl+Alt+Win+F20",
        };

        public void HotkeyGesture_LineFeedIsNotAHotkey()
        {
            Check.False(HotkeyGesture.TryParse("Ctrl+LineFeed", out _));                                         // parsed; TryRegister then threw
            Check.Equal("key", Check.Throws<ArgumentException>(() => new HotkeyGesture(ModifierKeys.Control, Key.LineFeed)).ParamName);
            Check.True(HotkeyGesture.TryParse("Ctrl+Enter", out _));
        }

        public void Hotkeys_IdsStayInTheApplicationRange()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                using var hk = new GlobalHotkeys();
                typeof(GlobalHotkeys).GetField("_nextId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(hk, 0xBFFF);
                var ids = new List<int>();
                foreach (string c in Candidates)
                {
                    if (hk.TryRegister(HotkeyGesture.Parse(c), () => { }, out int id)) ids.Add(id);
                    if (ids.Count == 2) break;
                }
                Check.Equal(2, ids.Count, "two candidate hotkeys were not free");
                Check.Equal(0xBFFF, ids[0]);
                Check.Equal(1, ids[1]);                                                                             // 0xC000, past the app range
            });
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public void Hotkeys_DisposeAfterShutdownLeavesTheThreadsOtherHotkeys()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var hk = new GlobalHotkeys();
                int ours = -1;
                foreach (string c in Candidates)
                    if (hk.TryRegister(HotkeyGesture.Parse(c), () => { }, out ours)) break;
                Check.True(ours > 0, "no candidate hotkey was free");
                Dispatcher.CurrentDispatcher.InvokeShutdown();                                                      // the window and its hotkeys go
                bool foreign = false;
                foreach (uint vk in new uint[] { 0x87, 0x86, 0x85, 0x84 })                                         // F24..F21, Ctrl+Alt+Shift
                    if (RegisterHotKey(IntPtr.Zero, ours, 0x2 | 0x1 | 0x4, vk)) { foreign = true; break; }
                Check.True(foreign, "no thread hotkey was free");
                try
                {
                    hk.Dispose();                                                                                   // UnregisterHotKey(NULL, id)
                    Check.True(UnregisterHotKey(IntPtr.Zero, ours), "another thread hotkey with the same id was removed");
                }
                finally { UnregisterHotKey(IntPtr.Zero, ours); }
            });
        }

        // ---------- IncrementalCollection ----------

        public void Incremental_LoadersOwnCancellationIsAFailure()
        {
            var ic = new IncrementalCollection<int>((s, t, ct) => Task.FromException<IReadOnlyList<int>>(new OperationCanceledException("timeout")), 10);
            Task<int> load = ic.LoadMoreAsync();
            Check.True(SpinUntil(() => load.IsCompleted));
            Check.True(load.IsFaulted, load.Status.ToString());                                                    // Canceled
            Check.True(ic.LastError is OperationCanceledException);
            Check.True(ic.HasMoreItems);
        }

        public void Incremental_ResetCompletesWhenACollectionChangedHandlerThrows()
        {
            var gate = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            var ic = new IncrementalCollection<int>((s, t, ct) =>
            {
                if (++calls == 1) { ct.Register(() => gate.TrySetCanceled(ct)); return gate.Task; }
                return Task.FromResult<IReadOnlyList<int>>(new[] { 7 });
            }, 10);
            ic.Add(99);
            _ = ic.LoadMoreAsync();
            Check.True(ic.IsLoading);
            NotifyCollectionChangedEventHandler handler = (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) throw new InvalidOperationException("handler"); };
            ic.CollectionChanged += handler;
            Check.Equal("handler", Check.Throws<InvalidOperationException>(() => ic.ResetAsync()).Message);
            Check.False(ic.IsLoading);                                                                              // stuck true
            Check.True(ic.HasMoreItems);
            Check.True(SpinUntil(() => gate.Task.IsCanceled), "the old load was not cancelled");
            ic.CollectionChanged -= handler;
            Task<int> next = ic.LoadMoreAsync();
            Check.True(next.Wait(5000));
            Check.True(SpinUntil(() => !ic.IsLoading));
            Check.Equal("7", string.Join(",", ic));
        }

        public void Incremental_GenerationWrapsInsteadOfOverflowing()
        {
            var ic = new IncrementalCollection<int>((s, t, ct) => Task.FromResult<IReadOnlyList<int>>(new[] { 1 }), 10);
            typeof(IncrementalCollection<int>).GetField("_generation", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(ic, int.MaxValue);
            Task<int> t = ic.ResetAsync();                                                                          // OverflowException (checked)
            Check.True(t.Wait(5000));
            Check.Equal(1, t.Result);
            Check.Equal(1, ic.Count);
        }

        // A caller joining while the last waiter's cancellation was between its count reaching zero and its cancel was
        // cancelled without asking. Stress: the window is a few instructions wide.
        public void Incremental_JoiningWhileTheLastWaiterCancelsIsNeverCancelled()
        {
            int cancelledJoins = 0;
            for (int i = 0; i < 3000; i++)
            {
                var gates = new ConcurrentQueue<TaskCompletionSource<IReadOnlyList<int>>>();
                var ic = new IncrementalCollection<int>((s, t, ct) =>
                {
                    var g = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
                    ct.Register(() => g.TrySetCanceled(ct));
                    gates.Enqueue(g);
                    return g.Task;
                }, 10);
                var cts = new CancellationTokenSource();
                _ = ic.LoadMoreAsync(cts.Token);
                using var go = new ManualResetEventSlim();
                Task canceller = Task.Run(() => { go.Wait(); cts.Cancel(); });
                go.Set();
                Thread.SpinWait(i % 64);
                Task<int> joined = ic.LoadMoreAsync(CancellationToken.None);
                canceller.Wait();
                Check.True(SpinUntil(() =>
                {
                    foreach (var g in gates) g.TrySetResult(new[] { 1 });
                    return joined.IsCompleted;
                }), "iteration " + i + " never finished");
                if (joined.IsCanceled) cancelledJoins++;
            }
            Check.Equal(0, cancelledJoins);
        }

        // ---------- AsyncRelayCommand ----------

        public async Task AsyncCommand_NullTaskIsAnInvalidOperation()
        {
            var cmd = new AsyncRelayCommand(() => null!);
            var ex = await Check.ThrowsAsync<InvalidOperationException>(() => cmd.ExecuteAsync());                 // NullReferenceException
            Check.True(ex.Message.Contains("null"), ex.Message);
            Check.False(cmd.IsExecuting);
        }

        // ---------- DialogService ----------

        public void Dialogs_DefaultOwnerOnASecondaryUiThreadIsNone()
        {
            Application? app = Application.Current;
            if (app is null)
            {
                FileDropBehaviorTests.RunSta(() => _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
                app = Application.Current;
            }
            Check.NotNull(app);
            MethodInfo owner = typeof(WpfDialogService).GetMethod("DefaultOwner", BindingFlags.NonPublic | BindingFlags.Static)!;
            FileDropBehaviorTests.RunSta(() =>
            {
                var window = new Window();                                                                          // this thread's own window
                try { Check.Null(owner.Invoke(null, null)); }                                                       // threw: another thread owns them
                finally { window.Close(); }
            });
        }

        // ---------- BindingFailureListener ----------

        public void BindingListener_TraceDataIsReported()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                using var listener = new BindingFailureListener();
                TraceSource source = PresentationTraceSources.DataBindingSource;
                source.TraceData(TraceEventType.Critical, 9101, "critical failure");                                // dropped
                Check.True(listener.Failures.Any(f => f.Code == 9101), "a critical event was dropped");
                source.TraceData(TraceEventType.Warning, 9102, "a warning");
                Check.False(listener.Failures.Any(f => f.Code == 9102), "warnings are off by default");
                listener.IncludeWarnings = true;
                source.TraceData(TraceEventType.Warning, 9103, "a warning", "with", 2, "parts");
                BindingFailure w = listener.Failures.Single(f => f.Code == 9103);
                Check.True(w.Message.Contains("with") && w.Message.Contains("parts"), w.Message);
            });
        }

        public void BindingListener_KeepsABoundedHistory()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                using var listener = new BindingFailureListener();
                PropertyInfo max = typeof(BindingFailureListener).GetProperty("MaxFailures")!;                      // no limit
                Check.NotNull(max);
                max.SetValue(listener, 3);
                for (int i = 0; i < 5; i++) listener.TraceEvent(null, "test", TraceEventType.Error, 9200 + i, "failure " + i);
                Check.Equal("9202,9203,9204", string.Join(",", listener.Failures.Select(f => f.Code)));
                Check.Equal(2L, (long)typeof(BindingFailureListener).GetProperty("DroppedFailures")!.GetValue(listener)!);
                Check.Equal("value", Check.Throws<TargetInvocationException>(() => max.SetValue(listener, -1)).InnerException is ArgumentOutOfRangeException a ? a.ParamName : "?");

                listener.Write(new string('x', 200_000));                                                           // never ends a line
                var pending = (System.Text.StringBuilder)typeof(BindingFailureListener).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(listener)!;
                Check.True(pending.Length <= 1 << 16, pending.Length.ToString(CultureInfo.InvariantCulture));
            });
        }

        // Two threads disposing one listener at once both passed the flag and took the shared count down twice.
        public void BindingListener_ConcurrentDisposeCountsOnce()
        {
            FieldInfo active = typeof(BindingFailureListener).GetField("_activeListeners", BindingFlags.NonPublic | BindingFlags.Static)!;
            using var keeper = new BindingFailureListener();
            int baseline = (int)active.GetValue(null)!;
            for (int i = 0; i < 500; i++)
            {
                var listener = new BindingFailureListener();
                using var barrier = new Barrier(2);
                Task a = Task.Run(() => { barrier.SignalAndWait(); listener.Dispose(); });
                Task b = Task.Run(() => { barrier.SignalAndWait(); listener.Dispose(); });
                Task.WaitAll(a, b);
                Check.Equal(baseline, (int)active.GetValue(null)!, "iteration " + i);
            }
        }
    }
}
