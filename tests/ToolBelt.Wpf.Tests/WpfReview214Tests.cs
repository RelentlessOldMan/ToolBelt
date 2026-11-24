using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>Regressions from review round 214 (WPF satellite, thirteenth pass): each test failed before its fix.</summary>
    public sealed class WpfReview214Tests
    {
        private static ResourceDictionary Dict(string key, object value) => new ResourceDictionary { [key] = value };

        // ---------- ThemeManager ----------

        // An app that merged both themes kept the other behind the slot: Light reported Light and showed Dark, and after
        // Dark the list held two Darks, so Light could never show again.
        public void ThemeManager_BothThemesMerged_SwitchesForGood()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var target = new ResourceDictionary();
                var light = Dict("Theme", "light");
                var dark = Dict("Theme", "dark");
                var other = Dict("Other", 1);
                target.MergedDictionaries.Add(light);
                target.MergedDictionaries.Add(other);
                target.MergedDictionaries.Add(dark);
                var tm = new ThemeManager(target).Register(AppTheme.Light, light).Register(AppTheme.Dark, dark);
                foreach (AppTheme t in new[] { AppTheme.Light, AppTheme.Dark, AppTheme.Light, AppTheme.Dark, AppTheme.Light })
                {
                    tm.Apply(t);
                    Check.Equal(t, tm.Effective);
                    Check.Equal(t == AppTheme.Light ? "light" : "dark", target["Theme"], t.ToString());
                    Check.Equal(2, target.MergedDictionaries.Count);
                    Check.Equal(1, target["Other"]);
                }
            });
        }

        // Created on an STA thread that then ended, FollowSystem was accepted and its re-applies queued to a dispatcher
        // nothing would pump, for the life of the process.
        public void ThemeManager_DispatcherThreadEnded_FollowSystemRefusedOrTurnsOff()
        {
            ThemeManager? before = null, after = null;
            var made = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var t = new Thread(() =>
            {
                before = new ThemeManager(new ResourceDictionary()).Register(AppTheme.Light, Dict("K", 1)).Register(AppTheme.Dark, Dict("K", 2));
                after = new ThemeManager(new ResourceDictionary()).Register(AppTheme.Light, Dict("K", 1)).Register(AppTheme.Dark, Dict("K", 2));
                after.FollowSystem = true;
                made.Set();
                release.Wait(10000);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Check.True(made.Wait(10000));
            release.Set();
            Check.True(t.Join(10000));
            try
            {
                var ex = Check.Throws<InvalidOperationException>(() => before!.FollowSystem = true);
                Check.False(before!.FollowSystem);
                // On when the thread ended: the next setting change turns it off rather than queue work nothing runs.
                MethodInfo handler = typeof(ThemeManager).GetMethod("OnUserPreferenceChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
                handler.Invoke(after, new object[] { this, new UserPreferenceChangedEventArgs(UserPreferenceCategory.General) });
                Check.False(after!.FollowSystem);
            }
            finally
            {
                before?.Dispose();
                after?.Dispose();
            }
        }

        // ---------- PlotPresenter.ZoomAbout ----------

        // The signed M11 was the scale and M11 and M22 were clamped apart: a y-up view was clamped to a positive scale
        // (flipped, anchor lost) or refused, and a non-uniform view lost its anchor. Graded against the exact zoom over
        // random mirrored and non-uniform views.
        public void ZoomAbout_MirroredAndNonUniformViews_KeepSignsAndAnchor()
        {
            Matrix yUp = new Matrix(1, 0, 0, -1, 0, 100);
            Check.Equal(new Matrix(2, 0, 0, -2, -10, 110), PlotPresenter.ZoomAbout(yUp, new Point(10, 90), 2));
            Check.Equal(new Matrix(2, 0, 0, -2, -10, 110), PlotPresenter.ZoomAbout(yUp, new Point(10, 90), 2, 0.02, 64));
            var rng = new Random(214);
            for (int trial = 0; trial < 20000; trial++)
            {
                double sx = Math.Pow(10, rng.NextDouble() * 4 - 2) * (rng.Next(2) == 0 ? 1 : -1);
                double sy = (rng.Next(3) == 0 ? Math.Abs(sx) : Math.Pow(10, rng.NextDouble() * 4 - 2)) * (rng.Next(2) == 0 ? 1 : -1);
                var view = new Matrix(sx, 0, 0, sy, rng.NextDouble() * 1000 - 500, rng.NextDouble() * 1000 - 500);
                var p = new Point(rng.NextDouble() * 800, rng.NextDouble() * 600);
                double factor = Math.Pow(2, rng.NextDouble() * 6 - 3);
                double min = 0.05, max = 20;
                Matrix z = PlotPresenter.ZoomAbout(view, p, factor, min, max);
                double f = Math.Clamp(Math.Abs(sx) * factor, min, max) / Math.Abs(sx);
                string at = view + " at " + p + " by " + factor;
                Check.True(Math.Sign(z.M11) == Math.Sign(sx) && Math.Sign(z.M22) == Math.Sign(sy), at + " -> " + z);
                Check.True(Math.Abs(z.M11) >= min && Math.Abs(z.M11) <= max, at + " -> " + z);
                Check.True(Math.Abs(z.M11 - sx * f) <= 1e-12 * Math.Abs(sx * f), at + " -> " + z);
                Check.True(Math.Abs(z.M22 - sy * f) <= 1e-12 * Math.Abs(sy * f), at + " -> " + z);
                Matrix inv = view; inv.Invert();
                Point before = inv.Transform(p);
                Point back = z.Transform(before);
                Check.True(Math.Abs(back.X - p.X) < 1e-7 && Math.Abs(back.Y - p.Y) < 1e-7, at + " -> " + back);
            }
        }

        // ---------- CountToVisibilityConverter ----------

        // Peeking with MoveNext used up the first item of a one-shot source, so the ItemsControl bound to it lost data.
        public void CountToVisibility_OneShotSequence_KeepsItsItems()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                _ = System.Windows.Threading.Dispatcher.CurrentDispatcher;   // a UI thread, as a binding's
                var c = new CountToVisibilityConverter();
                var once = new OneShot(new[] { 1, 2, 3 });
                Check.Equal(Visibility.Visible, c.Convert(once, typeof(Visibility), null, CultureInfo.InvariantCulture));
                var list = new ListBox { ItemsSource = once };
                Check.Equal(3, list.Items.Count);
                Check.Equal(Visibility.Collapsed, c.Convert(new OneShot(new int[0]), typeof(Visibility), null, CultureInfo.InvariantCulture));
                Check.Equal(Visibility.Collapsed, c.Convert(Filter(new int[0]), typeof(Visibility), null, CultureInfo.InvariantCulture));
                Check.Equal(Visibility.Visible, c.Convert(Filter(new[] { 4 }), typeof(Visibility), null, CultureInfo.InvariantCulture));
            });
        }

        private static IEnumerable<int> Filter(int[] items)
        {
            foreach (int i in items) yield return i;
        }

        // '\0' was Collapsed though a char is not a count ('a' was Visible), and a database null was Visible.
        public void CountToVisibility_CharAndDbNull()
        {
            var c = new CountToVisibilityConverter();
            object? Show(object v) => c.Convert(v, typeof(Visibility), null, CultureInfo.InvariantCulture);
            Check.Equal(Visibility.Visible, Show('\0'));
            Check.Equal(Visibility.Visible, Show('a'));
            Check.Equal(Visibility.Collapsed, Show(DBNull.Value));
            Check.Equal(Visibility.Visible, new CountToVisibilityConverter { Invert = true }.Convert(DBNull.Value, typeof(Visibility), null, CultureInfo.InvariantCulture));
        }

        // A sequence that gives its items to the first enumeration only (a data reader, a consuming enumerable).
        private sealed class OneShot : IEnumerable
        {
            private readonly Queue<int> _items;
            public OneShot(int[] items) => _items = new Queue<int>(items);
            public IEnumerator GetEnumerator()
            {
                while (_items.Count > 0) yield return _items.Dequeue();
            }
        }

        // ---------- IncrementalCollection ----------

        // A loader that returned a null Task threw a bare NullReferenceException, which LastError then showed.
        public async Task IncrementalCollection_NullTask_IsInvalidOperation()
        {
            var c = new IncrementalCollection<int>((s, n, ct) => null!, 5);
            await Check.ThrowsAsync<InvalidOperationException>(() => c.LoadMoreAsync());
            Check.True(c.LastError is InvalidOperationException, c.LastError?.GetType().Name ?? "null");
            Check.False(c.IsLoading);
        }
    }
}
