using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>Round 102 review regressions (WPF satellite, sixth pass): each failed on the code before its fix.</summary>
    public sealed class WpfReview102Tests
    {
        private sealed class Command : ICommand
        {
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) { }
        }

        public sealed class Source { public bool On { get; set; } = true; }

        // ---------- ThemeManager ----------

        // Registering the target (or a dictionary that merges it) merged the target into itself: the next missing-key
        // lookup recursed until the process died.
        public void Theme_TheTargetCannotBeMergedIntoItself()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var target = new ResourceDictionary();
                var themes = new ThemeManager(target);
                Check.Equal("dictionary", Check.Throws<ArgumentException>(() => themes.Register(AppTheme.Light, target)).ParamName);
                var outer = new ResourceDictionary();
                var middle = new ResourceDictionary();
                outer.MergedDictionaries.Add(middle);
                middle.MergedDictionaries.Add(target);
                Check.Equal("dictionary", Check.Throws<ArgumentException>(() => themes.Register(AppTheme.Light, outer)).ParamName);

                // Merged after registration: Apply checks again and changes nothing.
                var late = new ResourceDictionary();
                themes.Register(AppTheme.Light, late);
                late.MergedDictionaries.Add(target);
                Check.Throws<InvalidOperationException>(() => themes.Apply(AppTheme.Light));
                Check.Equal(0, target.MergedDictionaries.Count);
                Check.False(target.Contains("missing"));

                var fine = new ResourceDictionary { ["k"] = 1 };   // guard
                themes.Register(AppTheme.Light, fine).Apply(AppTheme.Light);
                Check.True(target.Contains("k"));
            });
        }

        // ---------- BindingDiagnostics ----------

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference MakeBadSwitch() => new WeakReference(new TraceSwitch("ToolBelt.Review102.Bad", "test", "bogus"));

        // One switch in the app whose value doesn't parse made the constructor throw, and the leaked count left every
        // later listener with DataBindingSource Off.
        public void BindingListener_ABadSwitchElsewhereDoesNotStopIt()
        {
            WeakReference bad = MakeBadSwitch();
            try
            {
                using (var listener = new BindingFailureListener())
                    Check.True(PresentationTraceSources.DataBindingSource.Switch.Level != SourceLevels.Off, "first listener: level Off");
                using (var second = new BindingFailureListener())
                    Check.True(PresentationTraceSources.DataBindingSource.Switch.Level != SourceLevels.Off, "second listener: level Off");
            }
            finally
            {
                // The switch list holds weak references: once collected, the bad switch can't reach later tests.
                for (int i = 0; i < 10 && bad.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            }
        }

        public void BindingListener_AMalformedFormatIsRecordedNotThrown()
        {
            // Called directly: through the source, its DefaultTraceListener formats (and throws) first.
            using var listener = new BindingFailureListener();
            listener.TraceEvent(null, "System.Windows.Data", TraceEventType.Error, 1, "{1} bad {", "x");
            Check.True(listener.Failures.Any(f => f.Message.Contains("bad")), string.Join(" | ", listener.Failures.Select(f => f.Message)));
        }

        public void DebugConverter_ANullLogIsRefused()
        {
            Check.Equal("value", Check.Throws<ArgumentNullException>(() => new DebugConverter { Log = null! }).ParamName);
            var lines = new StringBuilder();
            var converter = new DebugConverter { Log = s => lines.Append(s) };   // guard
            converter.Convert(1, typeof(int), null, CultureInfo.InvariantCulture);
            Check.True(lines.Length > 0);
        }

        // ---------- ValidationRules ----------

        public void Rules_NullNamesAreNamedAndNothingIsAddedFirst()
        {
            var set = new ValidationRuleSet();
            Check.Equal("otherProperties", Check.Throws<ArgumentException>(() => set.For("A", () => 1).DependsOn("B", null!)).ParamName);
            Check.Equal(0, set.Dependents("B").Count(), "B was registered before the null threw");
            Check.Equal("propertyName", Check.Throws<ArgumentNullException>(() => set.Dependents(null!)).ParamName);
            set.For("A", () => 1).DependsOn("B");   // guard
            Check.Equal("A", string.Join(",", set.Dependents("B")));
        }

        // Comparer<double> orders NaN below everything, so a NaN minimum was ignored and a NaN value passed.
        public void Rules_ANanRangeBoundIsRefused()
        {
            var set = new ValidationRuleSet();
            Check.Equal("min", Check.Throws<ArgumentOutOfRangeException>(() => set.For("X", () => 1.0).Range(double.NaN, 5)).ParamName);
            Check.Equal("max", Check.Throws<ArgumentOutOfRangeException>(() => set.For("Y", () => 1.0).Range(0, double.NaN)).ParamName);
            Check.Equal("min", Check.Throws<ArgumentOutOfRangeException>(() => set.For("Z", () => 1f).Range(float.NaN, 5f)).ParamName);
            set.For("W", () => 1.0).Range(0, 5);   // guard
        }

        // ---------- GlobalHotkeys ----------

        public void Hotkeys_ANullGestureNamesGesture()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                using var hotkeys = new GlobalHotkeys();
                Check.Equal("gesture", Check.Throws<ArgumentNullException>(() => hotkeys.Register((string)null!, () => { })).ParamName);
            });
        }

        // ---------- DialogService ----------

        // WpfDialogService threw (no ParamName) for an odd filter where RecordingDialogService accepted it.
        public void Dialogs_AnOddFilterIsRefusedByTheOptions()
        {
            foreach (string bad in new[] { "abc", "Images|*.png|Text" })
                Check.Equal("value", Check.Throws<ArgumentException>(() => new FileDialogOptions { Filter = bad }).ParamName);
            foreach (string? good in new[] { null, "", "Images|*.png", "Images|*.png|Text|*.txt" })
                Check.Equal(good, new FileDialogOptions { Filter = good }.Filter);
        }

        // ---------- PlotPresenter ----------

        public void Plot_FitMatrixIsIdentityForNonFiniteSizes()
        {
            var bad = new[]
            {
                (new Size(double.PositiveInfinity, 1), new Size(100, 100)),
                (new Size(double.NaN, 1), new Size(100, 100)),
                (new Size(10, 10), new Size(double.PositiveInfinity, 100)),
                (new Size(10, 10), new Size(100, double.NaN)),
                (new Size(5e-324, 5e-324), new Size(100, 100)),   // the scale overflows
            };
            foreach (var (content, viewport) in bad)
                Check.Equal(Matrix.Identity, PlotPresenter.FitMatrix(content, viewport), $"{content} in {viewport}");
            Check.Equal(2.0, PlotPresenter.FitMatrix(new Size(50, 50), new Size(100, 100)).M11);   // guard
        }

        // ---------- FileDropBehavior ----------

        // Saving a bound AllowDrop attached the binding to the save slot, which threw "NonShareable ... already used".
        public void FileDrop_ABoundAllowDropSurvivesAttachAndDetach()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var box = new TextBox();
                var source = new Source { On = false };
                BindingOperations.SetBinding(box, UIElement.AllowDropProperty, new Binding(nameof(Source.On)) { Source = source });
                FileDropBehavior.SetCommand(box, new Command());
                Check.True(box.AllowDrop, "attached but not taking drops");
                FileDropBehavior.SetCommand(box, null);
                Check.True(BindingOperations.GetBindingExpression(box, UIElement.AllowDropProperty) != null, "the binding was lost");
                Check.False(box.AllowDrop);
            });
        }

        // A DynamicResource AllowDrop came back as a local value (or ClearValue threw), losing the resource reference.
        public void FileDrop_ADynamicResourceAllowDropComesBack()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var box = new TextBox();
                box.Resources["AD"] = false;
                box.SetResourceReference(UIElement.AllowDropProperty, "AD");
                FileDropBehavior.SetCommand(box, new Command());
                Check.True(box.AllowDrop);
                FileDropBehavior.SetCommand(box, null);
                Check.False(box.AllowDrop);
                box.Resources["AD"] = true;
                Check.True(box.AllowDrop, "the resource reference was replaced by a local value");
            });
        }

        // ---------- NumericInputBehavior ----------

        // With no shown value in range, the doc says Minimum rounded: "0.2" or "0.3" came out depending on the input.
        public void Numeric_NoShownValueInRangeGivesMinimumRounded()
        {
            var rules = new NumericInputRules { Minimum = 0.24, Maximum = 0.26, DecimalPlaces = 1, Culture = CultureInfo.InvariantCulture };
            foreach (string input in new[] { "0", "0.24", "0.25", "0.26", "1", "-5", "99" })
                Check.Equal("0.2", NumericInputBehavior.Coerce(input, rules), input);
            var wide = new NumericInputRules { Minimum = 0.2, Maximum = 0.36, DecimalPlaces = 1, Culture = CultureInfo.InvariantCulture };
            Check.Equal("0.3", NumericInputBehavior.Coerce("0.29", wide));   // guard: a shown value fits
        }

        // Enter and Tab go through editing commands, not text input, so AcceptsReturn/AcceptsTab inserted them unchecked.
        public void Numeric_EnterAndTabInsertNothing()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                // The editing commands act only on a shown, focused box.
                var window = new Window { Width = 200, Height = 100, ShowInTaskbar = false, Left = -3000, Top = -3000 };
                try
                {
                    foreach (RoutedUICommand command in new[] { EditingCommands.EnterParagraphBreak, EditingCommands.EnterLineBreak, EditingCommands.TabForward })
                    {
                        foreach (bool numeric in new[] { false, true })
                        {
                            var box = new TextBox { Language = XmlLanguage.GetLanguage("en-US"), AcceptsReturn = true, AcceptsTab = true, Text = "5" };
                            if (numeric)
                            {
                                NumericInputBehavior.SetIsEnabled(box, true);
                                NumericInputBehavior.SetMaximum(box, 100);
                            }
                            window.Content = box;
                            if (!window.IsVisible) window.Show();
                            window.UpdateLayout();
                            Check.True(box.Focus(), $"{command.Name}: no focus");
                            box.CaretIndex = 1;
                            command.Execute(null, box);
                            if (numeric) Check.Equal("5", box.Text, command.Name);
                            else if (command != EditingCommands.EnterLineBreak)   // a TextBox ignores a line break
                                Check.True(box.Text != "5", $"{command.Name}: the guard box did not change");
                        }
                    }
                }
                finally { window.Close(); }
            });
        }

        // ---------- Messenger ----------

        // A's stale index entry kept its slot unless a Send had pruned it in between: "AB" or "BA" for the same calls.
        public void Messenger_AReRegistrationGoesLastWhetherOrNotASendRan()
        {
            foreach (bool sendBetween in new[] { false, true })
            {
                var messenger = new Messenger();
                var order = new StringBuilder();
                object a = new object(), b = new object();
                messenger.Register<int>(a, _ => order.Append('A'));
                messenger.Register<string>(a, _ => { });
                messenger.Register<int>(b, _ => order.Append('B'));
                messenger.Unregister<int>(a);
                if (sendBetween) { messenger.Send(0); order.Clear(); }
                messenger.Register<int>(a, _ => order.Append('A'));
                messenger.Send(1);
                Check.Equal("BA", order.ToString(), $"send between: {sendBetween}");
                GC.KeepAlive(a); GC.KeepAlive(b);
            }
        }
    }
}
