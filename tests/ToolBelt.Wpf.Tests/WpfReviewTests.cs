using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>Round 16 review regressions (WPF satellite).</summary>
    public sealed class WpfReviewTests
    {
        private static NumericInputRules Rules(double min = double.NegativeInfinity, double max = double.PositiveInfinity, int dp = 0)
            => new NumericInputRules { Minimum = min, Maximum = max, DecimalPlaces = dp, Culture = CultureInfo.InvariantCulture };

        // ---------- AsyncRelayCommand ----------

        public void AsyncRelayCommand_RaisesCanExecuteChangedOnTheUiThread()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                int uiThread = Environment.CurrentManagedThreadId;
                var raisedOn = new List<int>();
                var cmd = new AsyncRelayCommand(() => Task.Delay(20));
                cmd.CanExecuteChanged += (_, __) => raisedOn.Add(Environment.CurrentManagedThreadId);
                var frame = new DispatcherFrame();
                Exception? error = null;
                dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { await cmd.ExecuteAsync(); }
                    catch (Exception ex) { error = ex; }
                    finally { frame.Continue = false; }
                }));
                Dispatcher.PushFrame(frame);
                if (error != null) throw error;
                Check.Equal(2, raisedOn.Count);
                Check.True(raisedOn.TrueForAll(id => id == uiThread), "CanExecuteChanged must be raised on the UI thread");
            });
        }

        public void AsyncRelayCommand_ThrowingCanExecuteChangedHandler_DoesNotLeaveItExecuting()
        {
            var cmd = new AsyncRelayCommand(() => Task.CompletedTask);
            bool thrown = false;
            cmd.CanExecuteChanged += (_, __) => { if (!thrown) { thrown = true; throw new InvalidOperationException("handler"); } };
            Check.ThrowsAsync<InvalidOperationException>(() => cmd.ExecuteAsync()).GetAwaiter().GetResult();
            Check.False(cmd.IsExecuting);
            Check.True(cmd.CanExecute(null));
        }

        // ---------- NumericInputBehavior ----------

        public void NumericInput_ManyDecimalPlaces_DoesNotThrow()
        {
            Check.Equal("1.50000000000000000000", NumericInputBehavior.Coerce("1.5", Rules(dp: 20)));
            Check.Equal("2.50000000000000000000", NumericInputBehavior.StepValue("1.5", +1, 1, Rules(dp: 20)));
        }

        public void NumericInput_NonPositiveStep_IsIgnoredByStepBy()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                foreach (double step in new[] { 0.0, -1.0, double.NaN })
                {
                    var box = new TextBox { Text = "5" };
                    NumericInputBehavior.SetStep(box, step);
                    NumericInputBehavior.StepBy(box, +1);
                    Check.Equal("5", box.Text);
                }
            });
        }

        public void NumericInput_HugeDigitRun_IsRejected()
        {
            string digits = new string('9', 400);
            Check.False(NumericInputBehavior.IsAcceptablePartial(digits, Rules()));
            Check.Null(NumericInputBehavior.Coerce(digits, Rules()));
        }

        public void NumericInput_HalfUnitStep_StillMoves()
        {
            Check.Equal("4", NumericInputBehavior.StepValue("5", -1, 0.5, Rules()));
            Check.Equal("6", NumericInputBehavior.StepValue("5", +1, 0.5, Rules()));
            Check.Equal("-6", NumericInputBehavior.StepValue("-5", -1, 0.5, Rules()));
        }

        public void NumericInput_PositiveTextHopelessForNegativeRange_IsRejected()
        {
            var r = Rules(min: -100, max: -10);
            Check.True(NumericInputBehavior.IsAcceptablePartial("5", r));      // "-5…" is still reachable
            Check.True(NumericInputBehavior.IsAcceptablePartial("50", r));
            Check.False(NumericInputBehavior.IsAcceptablePartial("500", r));   // even "-500" is below the minimum
            Check.False(NumericInputBehavior.IsAcceptablePartial("50000", r));
        }

        // ---------- IncrementalCollection ----------

        public void IncrementalCollection_ResetFromCollectionChanged_DropsTheStalePage()
        {
            int calls = 0;
            var col = new IncrementalCollection<int>((skip, take, ct) =>
            {
                calls++;
                IReadOnlyList<int> page = calls == 1 ? new[] { 1, 2, 3 } : (IReadOnlyList<int>)new[] { 10, 11 };
                return Task.FromResult(page);
            }, pageSize: 3);
            bool resetDone = false;
            col.CollectionChanged += (_, __) => { if (!resetDone) { resetDone = true; _ = col.ResetAsync(); } };
            _ = col.LoadMoreAsync();
            Check.Equal("10,11", string.Join(",", col));
            Check.False(col.HasMoreItems);
        }

        // ---------- validation ----------

        public void RegexValidationRule_Timeout_IsInvalidNotAnException()
        {
            var rule = new RegexValidationRule { Pattern = "(a+)+b" };
            var result = rule.Validate(new string('a', 40) + "!", CultureInfo.InvariantCulture);
            Check.False(result.IsValid);
        }

        public void RegexValidationRule_BadPattern_RejectedBySetter()
        {
            var rule = new RegexValidationRule();
            var ex = Check.Throws<ArgumentException>(() => rule.Pattern = "(");
            Check.Equal("value", ex.ParamName);
        }

        public void RuleBuilderMatches_Timeout_IsAFailureNotAnException()
        {
            string text = new string('a', 40) + "!";
            var set = new ValidationRuleSet();
            set.For("V", () => text).Matches("(a+)+b", "bad");
            Check.Equal("bad", string.Join("|", set.Validate("V")));
        }

        // ---------- converters ----------

        public void EnumToBoolean_StringParameterFromXaml()
        {
            var c = new EnumToBooleanConverter();
            Check.Equal(true, c.Convert(DayOfWeek.Monday, typeof(bool), "Monday", CultureInfo.InvariantCulture));
            Check.Equal(false, c.Convert(DayOfWeek.Monday, typeof(bool), "Tuesday", CultureInfo.InvariantCulture));
            Check.Equal(DayOfWeek.Tuesday, c.ConvertBack(true, typeof(DayOfWeek), "Tuesday", CultureInfo.InvariantCulture));
            Check.Equal(DayOfWeek.Tuesday, c.ConvertBack(true, typeof(DayOfWeek?), "Tuesday", CultureInfo.InvariantCulture));
        }

        public void CountToVisibility_OtherNumericZeros_AreCollapsed()
        {
            var c = new CountToVisibilityConverter();
            foreach (object zero in new object[] { 0L, 0.0, 0u, (short)0, (byte)0, 0m, 0f })
                Check.Equal(Visibility.Collapsed, c.Convert(zero, typeof(Visibility), null, CultureInfo.InvariantCulture));
            foreach (object one in new object[] { 3L, 0.5, 2u, 1m })
                Check.Equal(Visibility.Visible, c.Convert(one, typeof(Visibility), null, CultureInfo.InvariantCulture));
        }

        // ---------- FileDropBehavior ----------

        public void FileDrop_FilterExcludesFoldersEvenWithAMatchingExtension()
        {
            string root = Path.Combine(Path.GetTempPath(), "tbwpf_" + Guid.NewGuid().ToString("N"));
            string folder = Path.Combine(root, "x.csv");
            string file = Path.Combine(root, "y.csv");
            Directory.CreateDirectory(folder);
            File.WriteAllText(file, "a");
            try
            {
                FileDropBehaviorTests.RunSta(() =>
                {
                    var data = new DataObject(DataFormats.FileDrop, new[] { folder, file });
                    Check.Equal(file, string.Join("|", FileDropBehavior.GetFiles(data, ".csv")));
                });
            }
            finally { Directory.Delete(root, true); }
        }

        public void FileDrop_StarDotStar_MatchesEveryFile()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { @"C:\nope\a.txt", @"C:\nope\b.png", @"C:\nope\README" });
                Check.Equal(3, FileDropBehavior.GetFiles(data, "*.*").Length);
            });
        }

        // ---------- PlotPresenter ----------

        private static BitmapSource Bitmap(int w, int h)
            => BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, new byte[w * h * 4], w * 4);

        private static bool FitPending(PlotPresenter p)
            => (bool)typeof(PlotPresenter).GetField("_fitPending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(p)!;

        private static void SetFitPending(PlotPresenter p, bool v)
            => typeof(PlotPresenter).GetField("_fitPending", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(p, v);

        public void PlotPresenter_SameSizeSourceChange_KeepsTheView()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var p = new PlotPresenter { Source = Bitmap(4, 4) };
                Check.True(FitPending(p));
                SetFitPending(p, false);                       // as after the first render
                p.Source = Bitmap(4, 4);                       // e.g. a live plot's next frame
                Check.False(FitPending(p));
                p.Source = Bitmap(8, 4);                       // a different figure size refits
                Check.True(FitPending(p));
            });
        }

        public void PlotPresenter_WheelFactor_RejectsNonPositive()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var p = new PlotPresenter();
                foreach (double bad in new[] { 0.0, -1.2, double.NaN, double.PositiveInfinity })
                {
                    var ex = Check.Throws<ArgumentOutOfRangeException>(() => p.WheelFactor = bad);
                    Check.Equal("value", ex.ParamName);
                }
            });
        }

        public void PlotPresenter_ZoomAbout_NeverProducesASingularView()
        {
            var view = new Matrix(1e-3, 0, 0, 1e-3, 0, 0);               // invertible
            Check.True(view.HasInverse);
            Matrix m = PlotPresenter.ZoomAbout(view, new Point(5, 5), 1e-6);  // minZoom 0: a 1e-9 scale WPF cannot invert
            Check.True(m.HasInverse);
        }

        // ---------- ThemeManager ----------

        public void ThemeManager_FailedApply_LeavesRequestedUnchanged()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var tm = new ThemeManager(new ResourceDictionary()).Register(AppTheme.Light, new ResourceDictionary());
                tm.Apply(AppTheme.Light);
                Check.Throws<InvalidOperationException>(() => tm.Apply(AppTheme.Dark));
                Check.Equal(AppTheme.Light, tm.Requested);
                Check.Equal(AppTheme.Light, tm.Effective);
            });
        }

        public void ThemeManager_SameDictionaryForBoth_StillTracksEffective()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var shared = new ResourceDictionary();
                var tm = new ThemeManager(new ResourceDictionary()).Register(AppTheme.Light, shared).Register(AppTheme.Dark, shared);
                var events = new List<AppTheme>();
                tm.ThemeChanged += (_, t) => events.Add(t);
                tm.Apply(AppTheme.Light);
                tm.Apply(AppTheme.Dark);
                Check.Equal(AppTheme.Dark, tm.Effective);
                Check.Equal("Light,Dark", string.Join(",", events));
            });
        }
    }
}
