// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ToolBelt.Wpf
{
    /// <summary>The numeric constraints applied by <see cref="NumericInputBehavior"/>.</summary>
    public sealed class NumericInputRules
    {
        public double Minimum { get; set; } = double.NegativeInfinity;
        public double Maximum { get; set; } = double.PositiveInfinity;

        /// <summary>
        /// Digits allowed after the decimal separator; 0 means whole numbers only. Values round to at most 15; up to 99 are
        /// shown, as zeros past the 15th.
        /// </summary>
        public int DecimalPlaces { get; set; }

        /// <summary>Supplies the decimal separator and negative sign; defaults to the current culture.</summary>
        public CultureInfo? Culture { get; set; }

        internal NumberFormatInfo Format => (Culture ?? CultureInfo.CurrentCulture).NumberFormat;
        internal bool AllowsNegative => Minimum < 0;
    }

    /// <summary>
    /// Attached behavior that makes a <see cref="TextBox"/> numeric:
    /// <c>&lt;TextBox tb:NumericInputBehavior.IsEnabled="True" tb:NumericInputBehavior.Minimum="0"
    /// tb:NumericInputBehavior.Maximum="100" tb:NumericInputBehavior.DecimalPlaces="1"/&gt;</c>.
    /// Keystrokes and pastes that cannot lead to a valid number are rejected (a lone "-" or a trailing separator are
    /// allowed mid-edit, and so is a value still below a positive minimum, since more digits may follow — but not one
    /// already beyond a limit that more typing could only push further). Up/Down arrows and the mouse wheel (while
    /// focused) step by <c>Step</c>, clamped; on losing focus the text is clamped, rounded and reformatted. The decimal
    /// separator comes from the TextBox's <c>Language</c> (set <c>xml:lang</c> on the window), or from the user's current
    /// culture when no Language was set anywhere — so "1,5" works on a German machine either way. An ASCII "-" is accepted
    /// even where the culture's minus sign is U+2212. The rules are public pure
    /// functions (<see cref="IsAcceptablePartial"/>, <see cref="Coerce"/>, <see cref="StepValue"/>) and
    /// <see cref="StepBy"/>/<see cref="Normalize"/> drive a box from code (e.g. spin buttons).
    /// </summary>
    public static class NumericInputBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(NumericInputBehavior), new PropertyMetadata(false, OnIsEnabledChanged));
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.RegisterAttached(
            "Minimum", typeof(double), typeof(NumericInputBehavior), new PropertyMetadata(double.NegativeInfinity));
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.RegisterAttached(
            "Maximum", typeof(double), typeof(NumericInputBehavior), new PropertyMetadata(double.PositiveInfinity));
        public static readonly DependencyProperty DecimalPlacesProperty = DependencyProperty.RegisterAttached(
            "DecimalPlaces", typeof(int), typeof(NumericInputBehavior), new PropertyMetadata(0));
        public static readonly DependencyProperty StepProperty = DependencyProperty.RegisterAttached(
            "Step", typeof(double), typeof(NumericInputBehavior), new PropertyMetadata(1.0));

        public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject o, bool v) => o.SetValue(IsEnabledProperty, v);
        public static double GetMinimum(DependencyObject o) => (double)o.GetValue(MinimumProperty);
        public static void SetMinimum(DependencyObject o, double v) => o.SetValue(MinimumProperty, v);
        public static double GetMaximum(DependencyObject o) => (double)o.GetValue(MaximumProperty);
        public static void SetMaximum(DependencyObject o, double v) => o.SetValue(MaximumProperty, v);
        public static int GetDecimalPlaces(DependencyObject o) => (int)o.GetValue(DecimalPlacesProperty);
        public static void SetDecimalPlaces(DependencyObject o, int v) => o.SetValue(DecimalPlacesProperty, v);
        public static double GetStep(DependencyObject o) => (double)o.GetValue(StepProperty);
        public static void SetStep(DependencyObject o, double v) => o.SetValue(StepProperty, v);

        // ---------- pure rules ----------

        /// <summary>True if <paramref name="text"/> is a valid number or a valid prefix of one under the rules.</summary>
        public static bool IsAcceptablePartial(string text, NumericInputRules rules)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (rules is null) throw new ArgumentNullException(nameof(rules));
            if (text.Length == 0) return true;
            NumberFormatInfo nf = rules.Format;
            text = AsciiMinus(text, nf);
            string neg = nf.NegativeSign, sep = nf.NumberDecimalSeparator;

            int i = 0;
            bool negative = false;
            if (text.StartsWith(neg, StringComparison.Ordinal))
            {
                if (!rules.AllowsNegative) return false;
                negative = true;
                i = neg.Length;
                if (i == text.Length) return true; // just the sign so far
            }

            int intDigits = 0, fracDigits = 0;
            bool sawSep = false;
            while (i < text.Length)
            {
                if (string.CompareOrdinal(text, i, sep, 0, sep.Length) == 0)
                {
                    if (sawSep || rules.DecimalPlaces == 0) return false;
                    sawSep = true;
                    i += sep.Length;
                    continue;
                }
                char ch = text[i];
                if (ch < '0' || ch > '9') return false;
                if (sawSep) { if (++fracDigits > rules.DecimalPlaces) return false; }
                else intDigits++;
                i++;
            }
            if (intDigits == 0 && fracDigits == 0) return true; // e.g. "-," or "," mid-edit

            // A value already past a limit can only move further away as more digits are typed.
            double v = ParseLoose(text, nf);
            if (double.IsInfinity(v)) return false; // more digits than a double holds
            // A positive value above a negative maximum is still reachable by typing a sign in front, unless even its
            // negation is below the minimum.
            if (!negative && v > rules.Maximum && (rules.Maximum >= 0 || -v < rules.Minimum)) return false;
            if (negative && rules.Minimum <= 0 && v < rules.Minimum) return false;
            return true;
        }

        /// <summary>
        /// Parses, clamps to [Minimum, Maximum], rounds to <see cref="NumericInputRules.DecimalPlaces"/> and formats; the
        /// result always lies in the range (a bound with more decimals than are shown clamps to the nearest shown value
        /// inside it), and Minimum wins when it exceeds Maximum. Returns null when the text holds no number (empty, a lone
        /// sign, a value too large for a double, ...).
        /// </summary>
        public static string? Coerce(string text, NumericInputRules rules)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (rules is null) throw new ArgumentNullException(nameof(rules));
            if (!double.TryParse(AsciiMinus(text, rules.Format), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, rules.Format, out double v))
                return null;
            if (double.IsInfinity(v) || double.IsNaN(v)) return null;
            return Format(Clamp(Round(v, rules), rules), rules);
        }

        /// <summary>
        /// Adds <paramref name="direction"/> × <paramref name="step"/> to the value in <paramref name="text"/> (an empty or
        /// unparsable box starts from 0, or the nearest limit if 0 is out of range), clamps and formats. A step smaller
        /// than the displayed precision still moves the value by one unit of the last decimal place.
        /// </summary>
        public static string StepValue(string text, int direction, double step, NumericInputRules rules)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (rules is null) throw new ArgumentNullException(nameof(rules));
            if (!(step > 0) || double.IsInfinity(step)) throw new ArgumentOutOfRangeException(nameof(step), step, "Step must be positive.");
            double v = 0;
            if (double.TryParse(AsciiMinus(text, rules.Format), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, rules.Format, out double parsed)
                && !double.IsInfinity(parsed) && !double.IsNaN(parsed))
            {
                int sign = Math.Sign(direction);
                double start = Round(parsed, rules);
                v = Round(parsed + sign * step, rules);
                if (double.IsInfinity(v)) v = sign * double.MaxValue;     // stepping past double.MaxValue read "∞"
                // A step under half a display unit would round straight back (5 − 0.5 → 4.5 → 5): move one unit instead.
                if (sign != 0 && v == start) v = Round(start + sign * Math.Pow(10, -RoundingDigits(rules)), rules);
            }
            return Format(Clamp(v, rules), rules);
        }

        // ---------- driving a TextBox ----------

        /// <summary>
        /// Steps the box's value up (+1) or down (−1) by its <c>Step</c>, leaving the caret at the end. A <c>Step</c> that is not a
        /// positive finite number does nothing (the arrow keys and wheel call this, so a bad binding must not crash input).
        /// </summary>
        public static void StepBy(TextBox box, int direction)
        {
            if (box is null) throw new ArgumentNullException(nameof(box));
            double step = GetStep(box);
            if (!(step > 0) || double.IsInfinity(step)) return;
            box.Text = StepValue(box.Text ?? "", direction, step, RulesFor(box));
            box.CaretIndex = box.Text.Length;
        }

        /// <summary>Clamps, rounds and reformats the box's text (what happens when it loses focus).</summary>
        public static void Normalize(TextBox box)
        {
            if (box is null) throw new ArgumentNullException(nameof(box));
            string? coerced = Coerce(box.Text ?? "", RulesFor(box));
            if (coerced != null && coerced != box.Text) box.Text = coerced;
        }

        /// <summary>The rules currently attached to <paramref name="box"/>, with its Language's culture.</summary>
        public static NumericInputRules RulesFor(TextBox box)
        {
            if (box is null) throw new ArgumentNullException(nameof(box));
            CultureInfo culture;
            // WPF's default Language is en-US whatever the OS culture; only honour it when someone actually set it.
            bool languageSet = DependencyPropertyHelper.GetValueSource(box, FrameworkElement.LanguageProperty).BaseValueSource != BaseValueSource.Default;
            if (!languageSet) culture = CultureInfo.CurrentCulture;
            else
            {
                try { culture = box.Language.GetSpecificCulture(); }
                catch (InvalidOperationException) { culture = CultureInfo.CurrentCulture; }
            }
            return new NumericInputRules
            {
                Minimum = GetMinimum(box),
                Maximum = GetMaximum(box),
                DecimalPlaces = Math.Max(0, GetDecimalPlaces(box)),
                Culture = culture,
            };
        }

        // ---------- wiring ----------

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TextBox box)) return;
            box.PreviewTextInput -= OnPreviewTextInput;
            box.PreviewKeyDown -= OnPreviewKeyDown;
            box.PreviewMouseWheel -= OnPreviewMouseWheel;
            box.LostKeyboardFocus -= OnLostFocus;
            DataObject.RemovePastingHandler(box, OnPasting);
            if ((bool)e.NewValue)
            {
                box.PreviewTextInput += OnPreviewTextInput;
                box.PreviewKeyDown += OnPreviewKeyDown;
                box.PreviewMouseWheel += OnPreviewMouseWheel;
                box.LostKeyboardFocus += OnLostFocus;
                DataObject.AddPastingHandler(box, OnPasting);
            }
        }

        private static string Proposed(TextBox box, string insert)
        {
            string text = box.Text ?? "";
            int start = Math.Min(box.SelectionStart, text.Length);
            int length = Math.Min(box.SelectionLength, text.Length - start);
            return text.Substring(0, start) + insert + text.Substring(start + length);
        }

        private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox box && !IsAcceptablePartial(Proposed(box, e.Text), RulesFor(box)))
                e.Handled = true;
        }

        private static void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!(sender is TextBox box)) return;
            string? pasted = e.DataObject.GetDataPresent(DataFormats.UnicodeText)
                ? e.DataObject.GetData(DataFormats.UnicodeText) as string
                : null;
            if (pasted is null) { e.CancelCommand(); return; }
            string trimmed = pasted.Trim();
            if (!IsAcceptablePartial(Proposed(box, trimmed), RulesFor(box))) { e.CancelCommand(); return; }
            if (trimmed != pasted) e.DataObject = new DataObject(DataFormats.UnicodeText, trimmed);   // paste what was checked
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is TextBox box) || box.IsReadOnly) return;
            switch (e.Key)
            {
                case Key.Up: StepBy(box, +1); e.Handled = true; break;
                case Key.Down: StepBy(box, -1); e.Handled = true; break;
                case Key.Space: e.Handled = true; break; // Space never reaches PreviewTextInput in a TextBox
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is TextBox box && !box.IsReadOnly && box.IsKeyboardFocusWithin && e.Delta != 0)
            {
                StepBy(box, Math.Sign(e.Delta));
                e.Handled = true;
            }
        }

        private static void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox box) Normalize(box);
        }

        // Math.Round takes at most 15 digits; more decimal places are still displayed (as zeros).
        private static int RoundingDigits(NumericInputRules r) => Math.Min(15, Math.Max(0, r.DecimalPlaces));

        private static double Round(double v, NumericInputRules r) => Math.Round(v, RoundingDigits(r), MidpointRounding.AwayFromZero);

        // Clamps to the bounds moved inward to the shown precision — clamping to a raw 0.25 at one decimal formatted back
        // out of range as "0.2" — and to finite values, so the text never reads "∞". Minimum wins an empty range.
        private static double Clamp(double v, NumericInputRules r)
        {
            double min = Finite(r.Minimum), max = Finite(r.Maximum);
            double lo = Inward(min, r, up: true), hi = Inward(max, r, up: false);
            if (lo > hi) { lo = min; hi = Math.Max(min, max); }      // no shown value fits (or Minimum > Maximum)
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private static double Finite(double bound) => Math.Max(-double.MaxValue, Math.Min(double.MaxValue, bound));

        private static double Inward(double bound, NumericInputRules r, bool up)
        {
            double g = Round(bound, r);
            if (up ? g >= bound : g <= bound) return g;
            double unit = Math.Pow(10, -RoundingDigits(r));
            return Round(up ? g + unit : g - unit, r);
        }

        private static string Format(double v, NumericInputRules r)
        {
            if (v == 0) v = 0; // no "-0"
            // Digits past the 15th are zeros ("F20" printed binary noise: 0.1 gave "0.10000000000000000555"), and at most
            // 99 are shown (a DecimalPlaces of a billion made losing focus throw FormatException).
            int digits = RoundingDigits(r), shown = Math.Min(99, Math.Max(0, r.DecimalPlaces));
            string s = v.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), r.Format);
            return shown > digits ? s + new string('0', shown - digits) : s;
        }

        private static double ParseLoose(string text, NumberFormatInfo nf)
        {
            string t = text.EndsWith(nf.NumberDecimalSeparator, StringComparison.Ordinal)
                ? text.Substring(0, text.Length - nf.NumberDecimalSeparator.Length)
                : text;
            return double.TryParse(AsciiMinus(t, nf), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, nf, out double v) ? v : 0;
        }

        // Cultures whose minus sign is U+2212 (sv-SE, nb-NO, … under ICU) still get "-" from the keyboard.
        private static string AsciiMinus(string text, NumberFormatInfo nf)
            => nf.NegativeSign != "-" && text.StartsWith("-", StringComparison.Ordinal) ? nf.NegativeSign + text.Substring(1) : text;
    }
}
