using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>Regressions from review round 182 (WPF satellite, eleventh pass): each test failed before its fix.</summary>
    public sealed class WpfReview182Tests
    {
        private static NumericInputRules Rules(double min, double max, int dp)
            => new NumericInputRules { Minimum = min, Maximum = max, DecimalPlaces = dp, Culture = CultureInfo.InvariantCulture };

        // current * (target / current) missed the clamped target by an ulp: about 2% of zooms left the scale just outside
        // [minZoom, maxZoom] (0.019999999999999997 under a minimum of 0.02).
        public void ZoomAbout_StaysInsideTheBounds()
        {
            Matrix m = PlotPresenter.ZoomAbout(Matrix.Identity, new Point(3, 4), 0.1498362241004891, 0.02, 50);
            m = PlotPresenter.ZoomAbout(m, new Point(3, 4), 0.020374749376283564, 0.02, 50);
            Check.True(m.M11 >= 0.02 && m.M22 >= 0.02, m.M11.ToString("R", CultureInfo.InvariantCulture));
            var rng = new Random(182);
            for (int t = 0; t < 20000; t++)
            {
                double min = Math.Pow(10, rng.NextDouble() * 4 - 3), max = min * (1 + rng.NextDouble() * 1000);
                double s = min + rng.NextDouble() * (max - min);
                var view = new Matrix(s, 0, 0, s, rng.NextDouble() * 100, rng.NextDouble() * 100);
                double factor = Math.Pow(10, rng.NextDouble() * 8 - 4);
                Matrix z = PlotPresenter.ZoomAbout(view, new Point(rng.NextDouble() * 500, rng.NextDouble() * 500), factor, min, max);
                Check.True(z.M11 >= min && z.M11 <= max && z.M22 >= min && z.M22 <= max,
                    $"trial {t}: {z.M11:R} in [{min:R}, {max:R}]");
            }
        }

        // Calls was the live list: a reference taken earlier grew with later calls, and a cast to List could clear it.
        public void RecordingDialogService_Calls_IsASnapshot()
        {
            var d = new RecordingDialogService().EnqueueConfirm(true).EnqueueConfirm(false);
            d.Confirm("one");
            IReadOnlyList<DialogCall> before = d.Calls;
            d.Confirm("two");
            Check.Equal(1, before.Count);
            Check.Equal(2, d.Calls.Count);
            Check.True(!(d.Calls is List<DialogCall>), "the live list");
            if (d.Calls is ICollection<DialogCall> c && !c.IsReadOnly) c.Clear();
            Check.Equal(2, d.Calls.Count);
        }

        // As object or IComparable, strings compared in the current culture: "chyba" failed ["a", "d"] under cs-CZ, and
        // "B" passed ["a", "c"] under en-US; as string both were ordinal.
        public void Range_StringsAsObject_CompareOrdinally()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                foreach (string name in new[] { "cs-CZ", "en-US", "da-DK" })
                {
                    CultureInfo.CurrentCulture = new CultureInfo(name);
                    object value = "chyba";
                    var set = new ValidationRuleSet();
                    set.For<object>("O", () => value).Range("a", "d");
                    IComparable cv = "chyba";
                    set.For<IComparable>("C", () => cv).Range("a", "d");
                    Check.Equal(0, set.Validate("O").Count, name + " object chyba");
                    Check.Equal(0, set.Validate("C").Count, name + " IComparable chyba");
                    value = "B";
                    cv = "B";
                    var upper = new ValidationRuleSet();
                    upper.For<object>("O", () => value).Range("a", "c");
                    upper.For<IComparable>("C", () => cv).Range("a", "c");
                    Check.Equal(1, upper.Validate("O").Count, name + " object B");
                    Check.Equal(1, upper.Validate("C").Count, name + " IComparable B");
                }
                // Non-strings still use their own order.
                object n = 5;
                var ints = new ValidationRuleSet();
                ints.For<object>("N", () => n).Range(1, 9);
                Check.Equal(0, ints.Validate("N").Count);
                n = 10;
                Check.Equal(1, ints.Validate("N").Count);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        // Past the separator the integer part is fixed, yet "1.5" and "0." were accepted for [5, 10] at two decimals (and
        // "1.5" became "5.00" on losing focus). Graded against every completion of the text, over random ranges.
        public void Partial_WithASeparator_MustBeAbleToReachTheRange()
        {
            Check.True(!NumericInputBehavior.IsAcceptablePartial("1.5", Rules(5, 10, 2)));
            Check.True(!NumericInputBehavior.IsAcceptablePartial("0.", Rules(5, 10, 2)));
            Check.True(!NumericInputBehavior.IsAcceptablePartial("-1.5", Rules(-10, -5, 2)));
            Check.True(NumericInputBehavior.IsAcceptablePartial("5.5", Rules(5, 10, 2)));
            Check.True(NumericInputBehavior.IsAcceptablePartial("4.", Rules(4.99, 10, 2)));
            var rng = new Random(1820);
            for (int t = 0; t < 20000; t++)
            {
                int dp = 1 + rng.Next(2);
                double a = rng.Next(-2000, 2001) / 100.0, b = rng.Next(-2000, 2001) / 100.0;
                double min = Math.Min(a, b), max = Math.Max(a, b);
                bool neg = rng.Next(3) == 0;
                string ints = Digits(rng, rng.Next(3));
                int f = rng.Next(dp + 1);
                string text = (neg ? "-" : "") + ints + "." + Digits(rng, f);
                if (ints.Length + f == 0) continue;
                // A sign typed in front can still rescue a positive text only when the whole range is negative.
                bool expected = false;
                if (!neg || min < 0)
                    foreach (string c in Completions(text, dp - f))
                    {
                        double v = double.Parse(c, CultureInfo.InvariantCulture);
                        if (v >= min && v <= max || !neg && max < 0 && -v >= min && -v <= max) expected = true;
                    }
                Check.Equal(expected, NumericInputBehavior.IsAcceptablePartial(text, Rules(min, max, dp)),
                    $"\"{text}\" in [{min}, {max}] at {dp}");
            }
        }

        private static string Digits(Random rng, int n)
        {
            var c = new char[n];
            for (int i = 0; i < n; i++) c[i] = (char)('0' + rng.Next(10));
            return new string(c);
        }

        private static IEnumerable<string> Completions(string text, int more)
        {
            yield return text.EndsWith(".", StringComparison.Ordinal) ? text + "0" : text;
            if (more <= 0) yield break;
            for (int d = 0; d < 10; d++)
                foreach (string c in Completions(text + (char)('0' + d), more - 1)) yield return c;
        }

        // What Coerce and StepValue write was refused as input, so it could not be edited: "10" for Minimum 10 above
        // Maximum 5, and "0.3" for [0.25, 0.26] at one decimal.
        public void CoercedAndSteppedText_IsAcceptedInput()
        {
            Check.Equal("10", NumericInputBehavior.Coerce("7", Rules(10, 5, 0)));
            Check.True(NumericInputBehavior.IsAcceptablePartial("10", Rules(10, 5, 0)));
            Check.Equal("0.3", NumericInputBehavior.Coerce("0.25", Rules(0.25, 0.26, 1)));
            Check.True(NumericInputBehavior.IsAcceptablePartial("0.3", Rules(0.25, 0.26, 1)));
            var rng = new Random(1821);
            for (int t = 0; t < 20000; t++)
            {
                int dp = rng.Next(3);
                double a = rng.Next(-20000, 20001) / 1000.0, b = rng.Next(-20000, 20001) / 1000.0;
                if (rng.Next(2) == 0) { double lo = Math.Min(a, b); b = Math.Max(a, b); a = lo; }   // half inverted
                NumericInputRules r = Rules(a, b, dp);
                string input = (rng.NextDouble() * 60 - 30).ToString("R", CultureInfo.InvariantCulture);
                string c = NumericInputBehavior.Coerce(input, r)!;
                Check.True(NumericInputBehavior.IsAcceptablePartial(c, r), $"Coerce \"{c}\" in [{a}, {b}] at {dp}");
                string s = NumericInputBehavior.StepValue(c, rng.Next(2) * 2 - 1, 0.5, r);
                Check.True(NumericInputBehavior.IsAcceptablePartial(s, r), $"Step \"{s}\" in [{a}, {b}] at {dp}");
            }
        }

        // A lone sign or separator is accepted while typing, but Coerce reads no number in it and Normalize left it.
        public void Normalize_ClearsTextWithNoDigits()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                foreach (string text in new[] { "-", ".", "-." })
                {
                    var box = new TextBox { Language = XmlLanguage.GetLanguage("en-US") };
                    NumericInputBehavior.SetMinimum(box, -10);
                    NumericInputBehavior.SetMaximum(box, 10);
                    NumericInputBehavior.SetDecimalPlaces(box, 2);
                    box.Text = text;
                    NumericInputBehavior.Normalize(box);
                    Check.Equal("", box.Text, text);
                }
            });
        }
    }
}
