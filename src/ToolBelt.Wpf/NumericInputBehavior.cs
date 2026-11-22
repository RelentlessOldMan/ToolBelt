// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.Globalization;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;

namespace ToolBelt.Wpf
{
    /// <summary>The numeric constraints applied by <see cref="NumericInputBehavior"/>.</summary>
    public sealed class NumericInputRules
    {
        private double _minimum = double.NegativeInfinity, _maximum = double.PositiveInfinity;

        // A NaN bound compared false: the clamp and stepping ignored it while the keystroke filter refused a minus sign, so
        // "-5" could not be typed yet StepValue and Coerce produced it. Rejected, as WPF's RangeBase does.
        public double Minimum
        {
            get => _minimum;
            set => _minimum = double.IsNaN(value) ? throw new ArgumentOutOfRangeException(nameof(value), value, "The minimum must not be NaN.") : value;
        }

        public double Maximum
        {
            get => _maximum;
            set => _maximum = double.IsNaN(value) ? throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum must not be NaN.") : value;
        }

        /// <summary>
        /// Digits allowed after the decimal separator; 0 means whole numbers only. Values round to at most 15; up to 99 are
        /// shown, as zeros past the 15th.
        /// </summary>
        public int DecimalPlaces { get; set; }

        /// <summary>Supplies the decimal separator and negative sign; defaults to the current culture.</summary>
        public CultureInfo? Culture { get; set; }

        // A custom culture with an empty minus sign made every text "start with the sign", so every keystroke was refused.
        internal NumberFormatInfo Format
        {
            get
            {
                NumberFormatInfo nf = (Culture ?? CultureInfo.CurrentCulture).NumberFormat;
                if (nf.NegativeSign.Length != 0) return nf;
                var copy = (NumberFormatInfo)nf.Clone();
                copy.NegativeSign = "-";
                return copy;
            }
        }
        internal bool AllowsNegative => Minimum < 0;
    }

    /// <summary>
    /// Attached behavior that makes a <see cref="TextBox"/> numeric:
    /// <c>&lt;TextBox tb:NumericInputBehavior.IsEnabled="True" tb:NumericInputBehavior.Minimum="0"
    /// tb:NumericInputBehavior.Maximum="100" tb:NumericInputBehavior.DecimalPlaces="1"/&gt;</c>.
    /// Keystrokes, pastes and deletions that cannot lead to a valid number are rejected (a lone "-" or a trailing separator
    /// are allowed mid-edit, and so is a value still below a positive minimum, since more digits may follow — but not one
    /// already beyond a limit that more typing could only push further): deleting the "." of "10.5" under a maximum of
    /// 100 is refused. Backspace or Delete inside a multi-character minus sign removes the whole sign, and Ctrl+Backspace /
    /// Ctrl+Delete delete to the start / end of the text. Up/Down arrows and the mouse wheel (while
    /// focused) step by <c>Step</c>, clamped; on losing focus the text is clamped, rounded and reformatted. The decimal
    /// separator is the one the box's value is read with: when <c>Text</c> is bound, the binding's <c>ConverterCulture</c>
    /// or else the box's <c>Language</c> (WPF's default is en-US whatever the machine, so set <c>xml:lang</c> on the
    /// window for local separators); when it is not bound, the Language if one was set, else the user's current culture.
    /// An ASCII "-" is accepted even where the culture's minus sign is U+2212. Insert (overtype mode) is disabled, since
    /// overtyping replaces characters the checks do not see, and text dragged onto the box is refused (paste it
    /// instead). The rules are public pure
    /// functions (<see cref="IsAcceptablePartial"/>, <see cref="Coerce"/>, <see cref="StepValue"/>) and
    /// <see cref="StepBy"/>/<see cref="Normalize"/> drive a box from code (e.g. spin buttons).
    /// </summary>
    public static class NumericInputBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(NumericInputBehavior), new PropertyMetadata(false, OnIsEnabledChanged));
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.RegisterAttached(
            "Minimum", typeof(double), typeof(NumericInputBehavior), new PropertyMetadata(double.NegativeInfinity), NotNaN);
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.RegisterAttached(
            "Maximum", typeof(double), typeof(NumericInputBehavior), new PropertyMetadata(double.PositiveInfinity), NotNaN);

        private static bool NotNaN(object value) => !double.IsNaN((double)value);
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
                    if (sawSep || rules.DecimalPlaces <= 0) return false;   // Coerce treats a negative count as 0
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
            // The limits are those Coerce and StepValue clamp to, so what they write can be edited: with Minimum above
            // Maximum, or no shown value in range ([0.25, 0.26] at one decimal), they wrote a value refused here.
            FilterBounds(rules, out double min, out double max);
            // A positive value above a negative maximum is still reachable by typing a sign in front, unless even its
            // negation is below the minimum.
            if (!negative && v > max && (max >= 0 || -v < min)) return false;
            if (negative && min <= 0 && v < min) return false;
            if (sawSep)
            {
                // After the separator the integer part is fixed: the most the value can grow is to its digits with 9s
                // up to the last place. "1.5" was accepted for [5, 10] and became "5.00" on losing focus. Past 30 the 9s move
                // the parsed double no more (it holds 17 digits); a DecimalPlaces of int.MaxValue built a string of that many
                // on every keystroke and threw OutOfMemoryException.
                double most = Math.Abs(ParseLoose(text + new string('9', Math.Min(30, rules.DecimalPlaces - fracDigits)), nf));
                double least = Math.Abs(v);
                bool positiveReaches = least <= max && most >= min;
                bool negativeReaches = rules.AllowsNegative && -least >= min && -most <= max;
                if (negative ? !negativeReaches : !positiveReaches && !negativeReaches) return false;
            }
            return true;
        }

        /// <summary>
        /// Parses, clamps to [Minimum, Maximum], rounds to <see cref="NumericInputRules.DecimalPlaces"/> and formats; the
        /// result lies in the range whenever some value at the shown precision does (a bound with more decimals than are
        /// shown clamps to the nearest shown value inside it); when none does ([0.25, 0.26] at one decimal), Minimum rounded
        /// to the shown precision is used, which lies just outside. Minimum wins when it exceeds Maximum. Returns null when the text holds no number (empty, a lone
        /// sign, a value too large for a double, ...).
        /// </summary>
        public static string? Coerce(string text, NumericInputRules rules)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (rules is null) throw new ArgumentNullException(nameof(rules));
            if (!double.TryParse(AsciiMinus(text, rules.Format), BoundStyles, rules.Format, out double v))
                return null;
            if (double.IsInfinity(v) || double.IsNaN(v)) return null;
            return Format(Clamp(Round(v, rules), rules), rules);
        }

        // A bound double is shown by WPF in exponent form when small ("1E-05"): read as no number, a step went to 0.
        private const NumberStyles BoundStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

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
            if (double.TryParse(AsciiMinus(text, rules.Format), BoundStyles, rules.Format, out double parsed)
                && !double.IsInfinity(parsed) && !double.IsNaN(parsed))
            {
                int sign = Math.Sign(direction);
                double start = Round(parsed, rules);
                // Stepping past double.MaxValue read "∞"; clamped only after rounding, at DecimalPlaces above 0 the rounding
                // parsed "Infinity.00" and threw FormatException out of the arrow-key and mouse-wheel handlers.
                double sum = parsed + sign * step;
                v = Round(double.IsInfinity(sum) ? sign * double.MaxValue : sum, rules);
                // A step under half a display unit would round straight back (5 − 0.5 → 4.5 → 5): move to the next shown
                // value instead, rounding the neighbouring double toward the step. Adding a unit in binary and rounding to
                // nearest stuck where a unit is one or two ulps (-48183132800358.88 + 0.01 rounded back to …358.88). Past
                // ±double.MaxValue the neighbour is infinite, which Clamp brings back.
                if (sign != 0 && v == start)
                {
                    double next = sign > 0 ? Math.BitIncrement(start) : Math.BitDecrement(start);
                    v = double.IsInfinity(next) ? next : Rounded(next, RoundingDigits(rules), sign, out _);
                }
                // From 2^53 up a whole unit is below the spacing of doubles, so the value never moved: take the next double.
                if (sign != 0 && v == start) v = sign > 0 ? Math.BitIncrement(start) : Math.BitDecrement(start);
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

        /// <summary>
        /// Clamps, rounds and reformats the box's text (what happens when it loses focus); text with no digits (a lone sign
        /// or separator) is cleared.
        /// </summary>
        public static void Normalize(TextBox box)
        {
            if (box is null) throw new ArgumentNullException(nameof(box));
            string text = box.Text ?? "";
            string? coerced = Coerce(text, RulesFor(box));
            // A lone sign or separator ("-", ".", "-.") is accepted while typing but holds no number: it stayed in the box.
            if (coerced is null && text.Length > 0 && !HasDigit(text)) coerced = "";
            if (coerced != null && coerced != box.Text) box.Text = coerced;
        }

        /// <summary>
        /// The rules currently attached to <paramref name="box"/>, with the culture its value is read with: a binding on
        /// <c>Text</c> converts with its <c>ConverterCulture</c> or else the box's Language (en-US unless set); an unbound
        /// box uses the Language if one was set, else the current culture.
        /// </summary>
        public static NumericInputRules RulesFor(TextBox box)
        {
            if (box is null) throw new ArgumentNullException(nameof(box));
            CultureInfo? culture = null;
            BindingBase? binding = BindingOperations.GetBindingBase(box, TextBox.TextProperty);
            if (binding is Binding single) culture = single.ConverterCulture;
            else if (binding is MultiBinding multi) culture = multi.ConverterCulture;
            if (culture is null)
            {
                // A bound box under de-DE accepted "1,5", which the binding read with WPF's en-US default as 15. Unbound,
                // WPF's default Language means nothing, so only a Language someone actually set is honoured.
                bool languageSet = binding != null
                    || DependencyPropertyHelper.GetValueSource(box, FrameworkElement.LanguageProperty).BaseValueSource != BaseValueSource.Default;
                if (!languageSet) culture = CultureInfo.CurrentCulture;
                else
                {
                    try { culture = box.Language.GetSpecificCulture(); }
                    catch (InvalidOperationException) { culture = CultureInfo.CurrentCulture; }
                }
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
            box.PreviewDragEnter -= OnPreviewDrag;
            box.PreviewDragOver -= OnPreviewDrag;
            box.PreviewDrop -= OnPreviewDrag;
            DataObject.RemovePastingHandler(box, OnPasting);
            CommandManager.RemovePreviewExecutedHandler(box, OnPreviewExecuted);
            if ((bool)e.NewValue)
            {
                CommandManager.AddPreviewExecutedHandler(box, OnPreviewExecuted);
                box.PreviewDragEnter += OnPreviewDrag;
                box.PreviewDragOver += OnPreviewDrag;
                box.PreviewDrop += OnPreviewDrag;
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
            // TextBox inserts only what MaxLength leaves room for: a paste of "1." into "5" with MaxLength 2 was checked
            // as "1.5" and became 15.
            if (box.MaxLength > 0)
            {
                int room = Math.Max(0, box.MaxLength - (text.Length - length));
                if (insert.Length > room) insert = insert.Substring(0, room);
            }
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
            string? pasted;
            // A foreign data source may advertise text and then fail to render it (DV_E_FORMATETC): the COMException
            // escaped the paste.
            try
            {
                pasted = e.DataObject.GetDataPresent(DataFormats.UnicodeText)
                    ? e.DataObject.GetData(DataFormats.UnicodeText) as string
                    : null;
            }
            catch (ExternalException) { pasted = null; }
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

        // Dropped text (from another app, or dragged within the box) went in unchecked. Text drops are refused; dropped
        // files are left to whoever handles them.
        private static void OnPreviewDrag(object sender, DragEventArgs e)
        {
            bool text;
            try { text = !e.Data.GetDataPresent(DataFormats.FileDrop) && (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text)); }
            catch (ExternalException) { text = true; }
            if (!text) return;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        // Deletions never reach PreviewTextInput: "10.5" with the "." deleted became "105", past a maximum of 100.
        private static void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (!(sender is TextBox box) || box.IsReadOnly) return;
            ICommand command = e.Command;
            // Overtype mode replaced the character after the caret, but the checks modelled an insertion: "1.5" with "5"
            // overtyped after the "1" was checked as "15.5" under a maximum of 100 and became "155".
            if (command == EditingCommands.ToggleInsert) { e.Handled = true; return; }
            // With AcceptsReturn or AcceptsTab, Enter and Tab inserted "\r\n" and "\t" through these commands, unchecked.
            if (command == EditingCommands.EnterParagraphBreak || command == EditingCommands.EnterLineBreak
                || command == EditingCommands.TabForward || command == EditingCommands.TabBackward) { e.Handled = true; return; }
            bool cut = command == ApplicationCommands.Cut, back = command == EditingCommands.Backspace, del = command == EditingCommands.Delete;
            bool prevWord = command == EditingCommands.DeletePreviousWord, nextWord = command == EditingCommands.DeleteNextWord;
            if (!(cut || back || del || prevWord || nextWord)) return;
            string text = box.Text ?? "";
            int start = Math.Min(box.SelectionStart, text.Length);
            int length = Math.Min(box.SelectionLength, text.Length - start);
            int a = start, b = start + length;
            if (length == 0)
            {
                if (cut) return;
                if (back) { if (a == 0) return; a--; if (a > 0 && char.IsLowSurrogate(text[a]) && char.IsHighSurrogate(text[a - 1])) a--; }
                else if (del) { if (b == text.Length) return; b++; if (b < text.Length && char.IsLowSurrogate(text[b]) && char.IsHighSurrogate(text[b - 1])) b++; }
                else if (prevWord) a = 0;
                else b = text.Length;
                if (a == b) return;
            }
            NumericInputRules rules = RulesFor(box);
            int defaultA = a, defaultB = b;
            if (!cut)
            {
                // Part of a multi-character sign (a bidi mark plus "-") would leave a fragment no number can start with.
                string sign = rules.Format.NegativeSign;
                if (sign.Length > 1 && text.StartsWith(sign, StringComparison.Ordinal) && a < sign.Length)
                {
                    a = 0;
                    b = Math.Max(b, sign.Length);
                }
            }
            string proposed = text.Substring(0, a) + text.Substring(b);
            // A box already holding text the rules refuse (a bound 3.14159 at two decimals, "1E-05") could not be edited
            // down: every deletion still left it unacceptable. Deleting from it is allowed; losing focus coerces it.
            if (!IsAcceptablePartial(proposed, rules) && IsAcceptablePartial(text, rules)) { e.Handled = true; return; }
            if (prevWord || nextWord || a != defaultA || b != defaultB)
            {
                box.Select(a, b - a);          // delete exactly the range that was checked (kept on the undo stack)
                box.SelectedText = "";
                box.CaretIndex = a;
                e.Handled = true;
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

        private static double Round(double v, NumericInputRules r) => Rounded(v, RoundingDigits(r), 0, out _);

        // v to d decimal places, as the decimal it reads as (its shortest round-trip form), half away from zero (way 0),
        // up (way 1) or down (way −1); the text holds the digits with d decimals, no sign. Math.Round scales by 10^d, which
        // moved values once v·10^d passed 2^53 (Coerce gave −2124553058354439.0000000000 above a maximum of
        // −2124553058354439.25) and was not idempotent (−4.3076650725247685 at 16 places gave …7700, then …7710 on the
        // next lost focus). Rounding the shortest decimal is monotone and leaves an already-rounded value alone.
        private static double Rounded(double v, int d, int way, out string text)
        {
            bool negative = v < 0 || (v == 0 && BitConverter.DoubleToInt64Bits(v) < 0);
            string s = Math.Abs(v).ToString("R", CultureInfo.InvariantCulture);   // "123.45", "1E-05", "1.2345E+20"
            int exp = 0, e = s.IndexOf('E');
            if (e >= 0)
            {
                exp = int.Parse(s.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                s = s.Substring(0, e);
            }
            int dot = s.IndexOf('.');
            string digits = dot < 0 ? s : s.Remove(dot, 1);
            int point = (dot < 0 ? s.Length : dot) + exp;   // the value is 0.digits × 10^point
            int keep = point + d;
            bool away = false;   // round the magnitude up
            bool dropped = false;
            if (keep < 0)
            {
                dropped = digits.Trim('0').Length > 0;
                digits = "";
                point = -d;
                keep = 0;
            }
            else if (keep < digits.Length)
            {
                away = digits[keep] >= '5';
                dropped = digits.Substring(keep).Trim('0').Length > 0;
                digits = digits.Substring(0, keep);
            }
            if (way != 0) away = dropped && (way > 0) != negative;
            var buf = new System.Text.StringBuilder(digits.PadRight(keep, '0'));
            if (away)
            {
                int i = buf.Length - 1;
                while (i >= 0 && buf[i] == '9') buf[i--] = '0';
                if (i >= 0) buf[i]++;
                else { buf.Insert(0, '1'); point++; }
            }
            string all = buf.ToString();
            string whole = point > 0 ? all.Substring(0, point).TrimStart('0') : "";
            string frac = point > 0 ? all.Substring(point) : new string('0', -point) + all;
            if (whole.Length == 0) whole = "0";
            text = d > 0 ? whole + "." + frac : whole;
            double magnitude = double.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            return negative ? -magnitude : magnitude;
        }

        // Clamps to the bounds moved inward to the shown precision — clamping to a raw 0.25 at one decimal formatted back
        // out of range as "0.2" — and to finite values, so the text never reads "∞". Minimum wins an empty range.
        private static bool HasDigit(string s)
        {
            foreach (char c in s) if (c >= '0' && c <= '9') return true;
            return false;
        }

        // The widest of the range and what Clamp maps its ends to (Minimum when it exceeds Maximum, or a rounded bound).
        private static void FilterBounds(NumericInputRules r, out double min, out double max)
        {
            min = Math.Min(r.Minimum, Clamp(r.Minimum, r));
            max = Math.Max(r.Maximum, Clamp(r.Maximum, r));
        }

        private static double Clamp(double v, NumericInputRules r)
        {
            double min = Finite(r.Minimum), max = Finite(r.Maximum);
            double lo = Inward(min, r, up: true), hi = Inward(max, r, up: false);
            // No shown value fits (or Minimum > Maximum): Minimum rounded, as documented. Clamping to the raw range gave
            // "0.2" or "0.3" for [0.24, 0.26] at one decimal, depending on the input.
            if (lo > hi) return Round(min, r);
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private static double Finite(double bound) => Math.Max(-double.MaxValue, Math.Min(double.MaxValue, bound));

        // The nearest shown value on the inner side of the bound. Stepping a unit from the rounded bound and rounding again
        // could land back outside it ([957144.2231336349, …] at ten places gave 957144.2231336348).
        private static double Inward(double bound, NumericInputRules r, bool up) => Rounded(bound, RoundingDigits(r), up ? 1 : -1, out _);

        private static string Format(double v, NumericInputRules r)
        {
            // The sign goes on a value below zero once rounded (-0.001 at two places printed "-0.00"). The text is the rounded decimal itself
            // ("F" printed the double's binary value, a unit off the rounded decimal past about 16 significant digits).
            // Digits past the 15th are zeros ("F20" printed binary noise: 0.1 gave "0.10000000000000000555"), and at most
            // 99 are shown (a DecimalPlaces of a billion made losing focus throw FormatException).
            int digits = RoundingDigits(r), shown = Math.Min(99, Math.Max(0, r.DecimalPlaces));
            v = Rounded(v, digits, 0, out string text);
            int sep = text.IndexOf('.');
            string s = (v < 0 ? r.Format.NegativeSign : "")   // no "-0"
                + (sep < 0 ? text : text.Substring(0, sep) + r.Format.NumberDecimalSeparator + text.Substring(sep + 1));
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
