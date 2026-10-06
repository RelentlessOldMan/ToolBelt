using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>Regression tests for the 2026-10-05 review findings in the WPF satellite.</summary>
    public sealed class UiReviewRegressionTests
    {
        public void Numeric_AsciiMinusWhereTheCultureUsesU2212()
        {
            var sv = new CultureInfo("sv-SE");
            var rules = new NumericInputRules { Minimum = -100, Maximum = 100, DecimalPlaces = 1, Culture = sv };
            if (sv.NumberFormat.NegativeSign == "-") return;                            // NLS (not ICU) data: nothing to test
            Check.True(NumericInputBehavior.IsAcceptablePartial("-", rules));
            Check.True(NumericInputBehavior.IsAcceptablePartial("-5", rules));
            Check.Equal(sv.NumberFormat.NegativeSign + "5,0", NumericInputBehavior.Coerce("-5", rules));
            Check.Equal(sv.NumberFormat.NegativeSign + "4,0", NumericInputBehavior.StepValue("-5", +1, 1, rules));
        }

        public void Numeric_CultureFallsBackToCurrentWhenLanguageUnset()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var previous = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                    var box = new TextBox();
                    Check.Equal(",", NumericInputBehavior.RulesFor(box).Culture!.NumberFormat.NumberDecimalSeparator);   // WPF's en-US default ignored
                    box.Language = XmlLanguage.GetLanguage("en-US");                         // explicitly set: honoured
                    Check.Equal(".", NumericInputBehavior.RulesFor(box).Culture!.NumberFormat.NumberDecimalSeparator);
                }
                finally { CultureInfo.CurrentCulture = previous; }
            });
        }

        public void Incremental_SynchronousLoaderReentrancyDoesNotDuplicate()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var source = Enumerable.Range(0, 13).ToList();
                var c = new IncrementalCollection<int>((skip, take, ct) =>
                    Task.FromResult<IReadOnlyList<int>>(source.Skip(skip).Take(take).ToList()), pageSize: 5);
                // A handler that asks for more as soon as items arrive (what a virtualizing list does when it scrolls).
                c.CollectionChanged += (_, __) => c.LoadMoreAsync();
                c.LoadMoreAsync().Wait();
                for (int i = 0; i < 10 && c.HasMoreItems; i++) c.LoadMoreAsync().Wait();
                Check.Equal(string.Join(",", source), string.Join(",", c));
            });
        }

        public void Incremental_StaleFailureIsDropped()
        {
            var gate = new TaskCompletionSource<IReadOnlyList<int>>();
            int calls = 0;
            var c = new IncrementalCollection<int>((skip, take, ct) =>
                Interlocked.Increment(ref calls) == 1 ? gate.Task : Task.FromResult<IReadOnlyList<int>>(new[] { 1, 2 }), pageSize: 5);
            Task<int> first = c.LoadMoreAsync();
            Task<int> reset = c.ResetAsync();
            gate.SetException(new InvalidOperationException("server error for a page nobody wants"));
            Check.Equal(0, first.Result);                                                 // not thrown at the old caller
            Check.Equal(2, reset.Result);
            Check.Null(c.LastError);
        }

        public void FileDrop_RestoresAllowDropWhenCleared()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var box = new TextBox { AllowDrop = false };
                FileDropBehavior.SetCommand(box, new RelayCommand<object>(_ => { }));
                Check.True(box.AllowDrop);
                FileDropBehavior.SetCommand(box, null);
                Check.False(box.AllowDrop, "clearing the command restores the previous AllowDrop");
            });
        }

        public void BindingListener_KeepsLevelAllAndRefCounts()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var source = PresentationTraceSources.DataBindingSource;
                var original = source.Switch.Level;
                try
                {
                    source.Switch.Level = SourceLevels.All;
                    using (new BindingFailureListener()) Check.Equal(SourceLevels.All, source.Switch.Level);
                    Check.Equal(SourceLevels.All, source.Switch.Level);

                    source.Switch.Level = SourceLevels.Off;
                    var a = new BindingFailureListener();
                    var b = new BindingFailureListener();
                    a.Dispose();                                                            // out of order
                    Check.True((source.Switch.Level & SourceLevels.Warning) == SourceLevels.Warning, "b still needs warnings");
                    b.Dispose();
                    Check.Equal(SourceLevels.Off, source.Switch.Level);
                }
                finally { source.Switch.Level = original; }
            });
        }
    }
}
