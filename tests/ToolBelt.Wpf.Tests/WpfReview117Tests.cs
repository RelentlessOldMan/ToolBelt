using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>
    /// Round 117 review regressions (WPF satellite, seventh pass): each failed on the code before its fix, but the guards.
    /// </summary>
    public sealed class WpfReview117Tests
    {
        // ---------- IncrementalCollection ----------

        private static Func<int, int, System.Threading.CancellationToken, Task<IReadOnlyList<int>>> Pages(Func<int> calls, int size)
            => async (skip, take, _) =>
            {
                await Task.Yield();
                calls();
                return Enumerable.Range(skip, size).ToArray();
            };

        // A handler that loaded more as IsLoading went false joined the load that had just finished: it got that page's count
        // and the loader never ran again.
        public async Task Incremental_LoadingMoreAsLoadingEndsFetchesTheNextPage()
        {
            int calls = 0;
            var c = new IncrementalCollection<int>(Pages(() => calls++, 10), pageSize: 10);
            Task<int>? next = null;
            ((INotifyPropertyChanged)c).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(c.IsLoading) && !c.IsLoading && next == null) next = c.LoadMoreAsync();
            };
            Check.Equal(10, await c.LoadMoreAsync());
            Check.NotNull(next);
            Check.Equal(10, await next!);
            Check.Equal(2, calls);
            Check.Equal(20, c.Count);
            Check.True(c.SequenceEqual(Enumerable.Range(0, 20)));
            Check.False(c.IsLoading);
        }

        // A retry from a LastError handler got the first load's exception back, and the loader was not called again.
        public async Task Incremental_ARetryFromLastErrorLoadsAgain()
        {
            int calls = 0;
            var c = new IncrementalCollection<int>(async (skip, take, _) =>
            {
                await Task.Yield();
                if (calls++ == 0) throw new TimeoutException("first");
                return Enumerable.Range(skip, take).ToArray();
            }, pageSize: 5);
            Task<int>? retry = null;
            ((INotifyPropertyChanged)c).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(c.LastError) && c.LastError != null && retry == null) retry = c.LoadMoreAsync();
            };
            await Check.ThrowsAsync<TimeoutException>(() => c.LoadMoreAsync());
            Check.NotNull(retry);
            Check.Equal(5, await retry!);
            Check.Equal(2, calls);
            Check.Null(c.LastError);
            Check.False(c.IsLoading);
        }

        // A HasMoreItems handler that loaded more after the last (short) page joined it and got its 3 back, not 0.
        public async Task Incremental_LoadingMoreAfterTheLastPageGivesNothing()
        {
            int calls = 0;
            var c = new IncrementalCollection<int>(Pages(() => calls++, 3), pageSize: 10);
            Task<int>? next = null;
            ((INotifyPropertyChanged)c).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(c.HasMoreItems) && next == null) next = c.LoadMoreAsync();
            };
            Check.Equal(3, await c.LoadMoreAsync());
            Check.NotNull(next);
            Check.Equal(0, await next!);
            Check.Equal(1, calls);
            Check.False(c.HasMoreItems);
        }

        // Guard: a CollectionChanged handler while the page is still being appended joins that load (no second fetch over
        // the same offset), and the end state is raised as before.
        public async Task Incremental_ACollectionChangedHandlerStillJoinsTheLoad()
        {
            int calls = 0;
            var c = new IncrementalCollection<int>(Pages(() => calls++, 4), pageSize: 4);
            var joined = new List<Task<int>>();
            c.CollectionChanged += (_, e) => joined.Add(c.LoadMoreAsync());
            var changed = new List<string>();
            ((INotifyPropertyChanged)c).PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
            Check.Equal(4, await c.LoadMoreAsync());
            Check.Equal(4, joined.Count);
            foreach (var t in joined) Check.Equal(4, await t);
            Check.Equal(1, calls);
            Check.True(changed.Contains(nameof(c.IsLoading)));
            Check.False(c.IsLoading);
        }

        // ---------- Messenger ----------

        private sealed class A { }
        private sealed class B { }

        // Unregistering one type from a recipient with others scanned the type's whole index under the messenger's lock:
        // unregistering n such recipients was quadratic (40000 took 8.7 s, stalling every Send).
        public void Messenger_UnregisteringManyRecipientsIsLinear()
        {
            const int n = 60000;
            var m = new Messenger();
            var recipients = Enumerable.Range(0, n).Select(_ => new object()).ToArray();
            foreach (var r in recipients)
            {
                m.Register<A>(r, _ => { });
                m.Register<B>(r, _ => { });
            }
            var sw = Stopwatch.StartNew();
            foreach (var r in recipients) Check.True(m.Unregister<A>(r));
            sw.Stop();
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(3), "took " + sw.Elapsed);
            int got = 0;
            m.Send(new A());
            m.Register<B>(recipients[0], _ => got++);
            m.Send(new B());
            Check.Equal(1, got);
            GC.KeepAlive(recipients);
        }

        // Guard: delivery order (first registration for the type, re-registrations last) against a naive model over
        // random registers, unregisters, sends and clears.
        public void Messenger_DeliveryOrderMatchesANaiveModel()
        {
            var rng = new Random(117);
            for (int trial = 0; trial < 200; trial++)
            {
                var m = new Messenger();
                var recipients = Enumerable.Range(0, 6).Select(_ => new object()).ToArray();
                var modelA = new List<(int who, int id)>();
                var modelB = new List<(int who, int id)>();
                var got = new List<int>();
                int nextId = 0;
                for (int step = 0; step < 40; step++)
                {
                    int who = rng.Next(recipients.Length);
                    bool a = rng.Next(2) == 0;
                    var model = a ? modelA : modelB;
                    switch (rng.Next(6))
                    {
                        case 0: case 1:
                        {
                            int id = nextId++;
                            if (a) m.Register<A>(recipients[who], _ => got.Add(id)); else m.Register<B>(recipients[who], _ => got.Add(id));
                            // A recipient's handlers for a type go together, at its first registration's place.
                            int at = model.FindLastIndex(x => x.who == who);
                            if (at < 0) model.Add((who, id)); else model.Insert(at + 1, (who, id));
                            break;
                        }
                        case 2:
                        {
                            bool had = model.Any(x => x.who == who);
                            Check.Equal(had, a ? m.Unregister<A>(recipients[who]) : m.Unregister<B>(recipients[who]));
                            model.RemoveAll(x => x.who == who);
                            break;
                        }
                        case 3:
                        {
                            bool had = modelA.Concat(modelB).Any(x => x.who == who);
                            Check.Equal(had, m.UnregisterAll(recipients[who]));
                            modelA.RemoveAll(x => x.who == who);
                            modelB.RemoveAll(x => x.who == who);
                            break;
                        }
                        default:
                        {
                            got.Clear();
                            if (a) m.Send(new A()); else m.Send(new B());
                            Check.Equal(string.Join(",", model.Select(x => x.id)), string.Join(",", got), $"trial {trial} step {step}");
                            break;
                        }
                    }
                }
                GC.KeepAlive(recipients);
            }
        }

        // ---------- Numeric bounds ----------

        // A NaN bound compared false, so it silently stopped applying: Minimum NaN, Maximum 10 passed "-1e300".
        public void Rules_RangeRuleRefusesANanBound()
        {
            var rule = new NumericRangeValidationRule();
            Check.Equal("value", Check.Throws<ArgumentOutOfRangeException>(() => rule.Minimum = double.NaN).ParamName);
            Check.Equal("value", Check.Throws<ArgumentOutOfRangeException>(() => rule.Maximum = double.NaN).ParamName);
            Check.Equal(double.MinValue, rule.Minimum);
            rule.Minimum = 0;
            rule.Maximum = 10;
            Check.True(rule.Validate("5", CultureInfo.InvariantCulture).IsValid);
            Check.False(rule.Validate("-1e300", CultureInfo.InvariantCulture).IsValid);
            // An empty range, as documented, passes nothing.
            rule.Minimum = 11;
            Check.False(rule.Validate("10.5", CultureInfo.InvariantCulture).IsValid);
        }

        // A NaN Minimum refused a typed "-5" while StepValue and Coerce produced negatives; a NaN Maximum was no bound.
        public void Numeric_RulesRefuseANanBound()
        {
            var r = new NumericInputRules();
            Check.Equal("value", Check.Throws<ArgumentOutOfRangeException>(() => r.Minimum = double.NaN).ParamName);
            Check.Equal("value", Check.Throws<ArgumentOutOfRangeException>(() => r.Maximum = double.NaN).ParamName);
            Check.Equal(double.NegativeInfinity, r.Minimum);
            Check.Equal(double.PositiveInfinity, r.Maximum);
            r.Minimum = 0;
            r.Maximum = 10;
            Check.Equal("0", NumericInputBehavior.StepValue("0", -1, 1, r));
            Check.Equal("10", NumericInputBehavior.Coerce("50", r));
        }

        public void Numeric_AttachedBoundsRefuseNaN()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var box = new TextBox();
                Check.Throws<ArgumentException>(() => NumericInputBehavior.SetMinimum(box, double.NaN));
                Check.Throws<ArgumentException>(() => NumericInputBehavior.SetMaximum(box, double.NaN));
                Check.Equal(double.NegativeInfinity, NumericInputBehavior.GetMinimum(box));
                NumericInputBehavior.SetMinimum(box, -5);
                NumericInputBehavior.SetMaximum(box, double.PositiveInfinity);
                Check.Equal(-5.0, NumericInputBehavior.GetMinimum(box));
            });
        }

        // ---------- HotkeyGesture ----------

        // KeyConverter's case-insensitive fallback folded look-alikes: "Ctrl+" and a long s registered a system-wide Ctrl+S.
        public void Hotkeys_LookAlikeLettersAreNotKeys()
        {
            char longS = (char)0x017F, kelvin = (char)0x212A, fullwidthA = (char)0xFF21, dotlessI = (char)0x0131;
            foreach (string text in new[]
            {
                "Ctrl+" + longS, "Ctrl+" + longS + "pace", "Alt+" + kelvin, "Ctrl+" + fullwidthA, "Ctrl+" + dotlessI,
                "Ctrl+F" + (char)0x00A0, "Ctrl" + (char)0x2009 + "+S", (char)0x017F + "hift+A",
            })
                Check.False(HotkeyGesture.TryParse(text, out _), string.Join(" ", text.Select(ch => ((int)ch).ToString("X4"))));
            // Guards: ASCII names in any case still parse.
            Check.True(HotkeyGesture.TryParse("Ctrl+S", out var s) && s.Key == System.Windows.Input.Key.S);
            Check.True(HotkeyGesture.TryParse("ctrl+space", out var sp) && sp.Key == System.Windows.Input.Key.Space);
            Check.True(HotkeyGesture.TryParse("Win + Shift + k", out var k) && k.Key == System.Windows.Input.Key.K);
        }

        // ---------- FileDropBehavior ----------

        // "*." (the Windows filter for no extension) matched only names ending in a dot, so nothing.
        public void FileDrop_StarDotMeansNoExtension()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                IDataObject data = new DataObject(DataFormats.FileDrop, new[] { @"C:\in\README", @"C:\in\a.csv", @"C:\in\Makefile", @"C:\in\b.tar.gz" });
                Check.Equal(@"C:\in\README|C:\in\Makefile", string.Join("|", FileDropBehavior.GetFiles(data, "*.")));
                Check.Equal(@"C:\in\README|C:\in\Makefile", string.Join("|", FileDropBehavior.GetFiles(data, ".")));
                Check.Equal(@"C:\in\README|C:\in\a.csv|C:\in\Makefile", string.Join("|", FileDropBehavior.GetFiles(data, ".csv;*.")));
                // Guards: ordinary and multi-part extensions.
                Check.Equal(@"C:\in\b.tar.gz", string.Join("|", FileDropBehavior.GetFiles(data, "tar.gz")));
                Check.Equal(4, FileDropBehavior.GetFiles(data, "*.*").Length);
            });
        }
    }
}
