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

        /// <summary>Digits allowed after the decimal separator; 0 means whole numbers only.</summary>
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
            if (!negative && rules.Maximum >= 0 && v > rules.Maximum) return false;
            if (negative && rules.Minimum <= 0 && v < rules.Minimum) return false;
            return true;
        }

        /// <summary>
        /// Parses, clamps to [Minimum, Maximum], rounds to <see cref="NumericInputRules.DecimalPlaces"/> and formats.
        /// Returns null when the text holds no number (empty, a lone sign, ...).
        /// </summary>
        public static string? Coerce(string text, NumericInputRules rules)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (rules is null) throw new ArgumentNullException(nameof(rules));
            if (!double.TryParse(AsciiMinus(text, rules.Format), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, rules.Format, out double v))
                return null;
            return Format(Clamp(Math.Round(v, Math.Max(0, rules.DecimalPlaces), MidpointRounding.AwayFromZero), rules), rules);
        }

        /// <summary>
        /// Adds <paramref name="direction"/> × <paramref name="step"/> to the value in <paramref name="text"/> (an empty or
        /// unparsable box starts from 0, or the nearest limit if 0 is out of range), clamps and formats.
        /// </summary>
        public static string StepValue(string text, int direction, double step, NumericInputRules rules)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (rules is null) throw new ArgumentNullException(nameof(rules));
            if (!(step > 0) || double.IsInfinity(step)) throw new ArgumentOutOfRangeException(nameof(step), step, "Step must be positive.");
            double v = double.TryParse(AsciiMinus(text, rules.Format), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, rules.Format, out double parsed)
                ? parsed + Math.Sign(direction) * step
                : 0;
            v = Math.Round(v, Math.Max(0, rules.DecimalPlaces), MidpointRounding.AwayFromZero);
            return Format(Clamp(v, rules), rules);
        }

        // ---------- driving a TextBox ----------

        /// <summary>Steps the box's value up (+1) or down (−1) by its <c>Step</c>, selecting the result.</summary>
        public static void StepBy(TextBox box, int direction)
        {
            if (box is null) throw new ArgumentNullException(nameof(box));
            box.Text = StepValue(box.Text ?? "", direction, GetStep(box), RulesFor(box));
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

        private static double Clamp(double v, NumericInputRules r) => v < r.Minimum ? r.Minimum : (v > r.Maximum ? r.Maximum : v);

        private static string Format(double v, NumericInputRules r)
        {
            if (v == 0) v = 0; // no "-0"
            return v.ToString("F" + Math.Max(0, r.DecimalPlaces).ToString(CultureInfo.InvariantCulture), r.Format);
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
