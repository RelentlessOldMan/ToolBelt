using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>
    /// Round 158 review regressions (WPF satellite, ninth pass): each failed on the code before its fix.
    /// </summary>
    public sealed class WpfReview158Tests
    {
        private static NumericInputRules Rules(double min, double max, int places)
            => new NumericInputRules { Minimum = min, Maximum = max, DecimalPlaces = places, Culture = CultureInfo.InvariantCulture };

        private static double Read(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        // A random value of 1 to 17 significant digits, from 1e-6 to 1e17 in size.
        private static double Draw(Random rng)
        {
            double v = (rng.NextDouble() + 0.1) * Math.Pow(10, rng.Next(-6, 18));
            v = double.Parse(v.ToString("E" + rng.Next(0, 17).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return rng.Next(2) == 0 ? -v : v;
        }

        // ---------- NumericInputBehavior ----------

        // Math.Round(v, d) moved v once v·10^d passed 2^53, and the inward step from a rounded bound was not re-checked:
        // the result lay outside the range though a shown value fit.
        public void NumericInput_CoerceStaysInTheRange()
        {
            string a = NumericInputBehavior.Coerce("0", Rules(-1e17, -2124553058354439.25, 10))!;
            Check.True(Read(a) <= -2124553058354439.25, a);
            string b = NumericInputBehavior.Coerce("0", Rules(957144.2231336349, 1e16, 10))!;
            Check.True(Read(b) >= 957144.2231336349, b);

            var rng = new Random(158);
            int fitting = 0;
            for (int trial = 0; trial < 40000; trial++)
            {
                double x = Draw(rng), y = Draw(rng);
                double min = Math.Min(x, y), max = Math.Max(x, y);
                int d = rng.Next(0, 16);
                var rules = Rules(min, max, d);
                // Some shown value fits when the range is wider than a unit, or holds a value with d places.
                if (max - min <= Math.Pow(10, -d) * 1.5) continue;
                fitting++;
                string text = Draw(rng).ToString("R", CultureInfo.InvariantCulture);
                string c = NumericInputBehavior.Coerce(text, rules)!;
                double v = Read(c);
                Check.True(v >= min && v <= max, $"[{min:R}, {max:R}] d={d} {text} -> {c}");
                string s = NumericInputBehavior.StepValue(text, rng.Next(2) * 2 - 1, Math.Pow(10, -d), rules);
                v = Read(s);
                Check.True(v >= min && v <= max, $"step [{min:R}, {max:R}] d={d} {text} -> {s}");
            }
            Check.True(fitting > 30000, fitting.ToString(CultureInfo.InvariantCulture));
        }

        // Coerce was not idempotent, so a box drifted a unit each time it lost focus: -4.3076650725247685 at 16 places gave
        // -4.3076650725247700, then -4.3076650725247710.
        public void NumericInput_CoerceIsIdempotentAndRoundsTheDecimal()
        {
            var wide = Rules(double.NegativeInfinity, double.PositiveInfinity, 16);
            string once = NumericInputBehavior.Coerce("-4.3076650725247685", wide)!;
            Check.Equal(once, NumericInputBehavior.Coerce(once, wide));

            var rng = new Random(1158);
            for (int trial = 0; trial < 40000; trial++)
            {
                double x = Draw(rng);
                int d = rng.Next(0, 17);
                var rules = Rules(double.NegativeInfinity, double.PositiveInfinity, d);
                string text = x.ToString("R", CultureInfo.InvariantCulture);
                string c = NumericInputBehavior.Coerce(text, rules)!;
                Check.Equal(c, NumericInputBehavior.Coerce(c, rules), $"{text} d={d}");
                // Against decimal arithmetic on the value as written, where decimal holds it exactly.
                if (Math.Abs(x) < 1e9 && d <= 12)
                {
                    decimal exact = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    decimal expected = Math.Round(exact, Math.Min(d, 15), MidpointRounding.AwayFromZero);
                    Check.Equal(Read(expected.ToString(CultureInfo.InvariantCulture)), Read(c), $"{text} d={d} -> {c}");   // (double)decimal is not correctly rounded
                }
            }
        }

        // ---------- NotifyTaskCompletion ----------

        // A worker thread carrying a UI thread's context has no dispatcher of its own; the continuation went to the UI
        // dispatcher, which had shut down, and Completion never completed.
        public void NotifyTask_AWorkerCarryingADeadDispatchersContextStillCompletes()
        {
            Dispatcher? ui = null;
            using var ready = new ManualResetEventSlim();
            var uiThread = new Thread(() =>
            {
                ui = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            });
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.IsBackground = true;
            uiThread.Start();
            ready.Wait();

            var tcs = new TaskCompletionSource<int>();
            NotifyTaskCompletion<int>? watcher = null;
            var worker = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(ui!));
                watcher = new NotifyTaskCompletion<int>(tcs.Task);
            });
            worker.Start();
            worker.Join();

            ui!.InvokeShutdown();
            uiThread.Join();
            tcs.SetResult(1);
            Check.True(watcher!.Completion.Wait(10000), "Completion never completed");
            Check.Equal(1, watcher.Result);
        }

        // Guard: on a live dispatcher reached through a carried context, the notifications are raised on the UI thread.
        public void NotifyTask_AWorkerCarryingALiveContextRaisesOnItsDispatcher()
        {
            Dispatcher? ui = null;
            using var ready = new ManualResetEventSlim();
            var uiThread = new Thread(() =>
            {
                ui = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            });
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.IsBackground = true;
            uiThread.Start();
            ready.Wait();
            try
            {
                var tcs = new TaskCompletionSource<int>();
                NotifyTaskCompletion<int>? watcher = null;
                var worker = new Thread(() =>
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(ui!));
                    watcher = new NotifyTaskCompletion<int>(tcs.Task);
                });
                worker.Start();
                worker.Join();
                Thread? raisedOn = null;
                watcher!.PropertyChanged += (_, _) => raisedOn = Thread.CurrentThread;
                tcs.SetResult(2);
                Check.True(watcher.Completion.Wait(10000));
                Check.True(ReferenceEquals(uiThread, raisedOn), "raised off the UI thread");
            }
            finally
            {
                ui!.InvokeShutdown();
                uiThread.Join();
            }
        }

        // ---------- GlobalHotkeys ----------

        // "Ctrl+Ctrl+K" read as Ctrl+K.
        public void Hotkey_ARepeatedModifierIsRefused()
        {
            foreach (string bad in new[] { "Ctrl+Ctrl+K", "Ctrl+Control+K", "alt+Shift+ALT+F5", "Win+Windows+A", "Shift+shift+1" })
                Check.False(HotkeyGesture.TryParse(bad, out _), bad);
            Check.True(HotkeyGesture.TryParse("Ctrl+Alt+Shift+Win+K", out var g));
            Check.Equal(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows, g.Modifiers);
        }

        // A null callback for a gesture this instance held threw InvalidOperationException ("already registered").
        public void Hotkey_ANullCallbackIsNamedEvenForAHeldGesture()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                using var hk = new GlobalHotkeys();
                HotkeyGesture? held = null;
                foreach (string c in new[] { "Ctrl+Alt+Shift+F24", "Ctrl+Alt+Shift+F23", "Ctrl+Alt+Shift+Win+F22" })
                    if (hk.TryRegister(HotkeyGesture.Parse(c), () => { }, out _)) { held = HotkeyGesture.Parse(c); break; }
                Check.True(held.HasValue, "no candidate hotkey was free");
                var e = Check.Throws<ArgumentNullException>(() => hk.Register(held!.Value, null!));
                Check.Equal("callback", e.ParamName);
                Check.Throws<InvalidOperationException>(() => hk.Register(held!.Value, () => { }));
            });
        }
    }
}
