using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Markup;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class NumericInputBehaviorTests
    {
        private static NumericInputRules Rules(double min = double.NegativeInfinity, double max = double.PositiveInfinity, int dp = 0, string culture = "")
            => new NumericInputRules { Minimum = min, Maximum = max, DecimalPlaces = dp, Culture = CultureInfo.GetCultureInfo(culture) };

        public void Partial_AcceptsValidPrefixes()
        {
            var r = Rules(-10, 100, 2);
            foreach (string ok in new[] { "", "-", "-5", "12.3", "12.34", "1.", ".", "-.", "0", "100" })
                Check.True(NumericInputBehavior.IsAcceptablePartial(ok, r), $"'{ok}' should be accepted");
        }

        public void Partial_RejectsWhatCannotBecomeValid()
        {
            var r = Rules(-10, 100, 2);
            foreach (string bad in new[] { "12.345", "1.2.3", "abc", "1a", "150", "-11", "--1", " 1", "1e3" })
                Check.False(NumericInputBehavior.IsAcceptablePartial(bad, r), $"'{bad}' should be rejected");
        }

        public void Partial_BelowAPositiveMinimumIsStillTypeable()
        {
            // Typing "15" with a minimum of 10 passes through "1".
            Check.True(NumericInputBehavior.IsAcceptablePartial("1", Rules(10, 99)));
        }

        public void Partial_SignAndDecimalsFollowTheRules()
        {
            Check.False(NumericInputBehavior.IsAcceptablePartial("-", Rules(0, 10)));     // no negatives allowed
            Check.False(NumericInputBehavior.IsAcceptablePartial("1.5", Rules(dp: 0)));   // integers only
        }

        public void Partial_UsesTheCultureSeparator()
        {
            var de = Rules(dp: 2, culture: "de-DE");
            Check.True(NumericInputBehavior.IsAcceptablePartial("1,5", de));
            Check.False(NumericInputBehavior.IsAcceptablePartial("1.5", de));
        }

        public void Coerce_ClampsRoundsAndFormats()
        {
            var r = Rules(-10, 100, 2);
            Check.Equal("100.00", NumericInputBehavior.Coerce("150", r));
            Check.Equal("3.14", NumericInputBehavior.Coerce("3.14159", r));
            Check.Equal("-10.00", NumericInputBehavior.Coerce("-99", r));
            Check.Equal("0.00", NumericInputBehavior.Coerce("-0.001", r));                // no "-0.00"
            Check.Equal("2.50", NumericInputBehavior.Coerce("2.5", r));
            Check.Null(NumericInputBehavior.Coerce("", r));
            Check.Null(NumericInputBehavior.Coerce("-", r));
            Check.Equal("1,50", NumericInputBehavior.Coerce("1,5", Rules(dp: 2, culture: "de-DE")));
        }

        public void StepValue_AddsClampsAndStartsFromZero()
        {
            Check.Equal("6", NumericInputBehavior.StepValue("5", +1, 1, Rules()));
            Check.Equal("100.0", NumericInputBehavior.StepValue("99.5", +1, 1, Rules(max: 100, dp: 1)));
            Check.Equal("-10", NumericInputBehavior.StepValue("-10", -1, 1, Rules(min: -10)));
            Check.Equal("0", NumericInputBehavior.StepValue("", +1, 1, Rules()));
            Check.Equal("10", NumericInputBehavior.StepValue("", +1, 1, Rules(min: 10)));
            Check.Equal("0.3", NumericInputBehavior.StepValue("0.2", +1, 0.1, Rules(dp: 1))); // no 0.30000000000000004
            Check.Throws<ArgumentOutOfRangeException>(() => NumericInputBehavior.StepValue("1", 1, 0, Rules()));
        }

        public void TextBox_StepNormalizeAndLanguage()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var box = new TextBox { Language = XmlLanguage.GetLanguage("en-US") };
                NumericInputBehavior.SetIsEnabled(box, true);
                NumericInputBehavior.SetMinimum(box, 0);
                NumericInputBehavior.SetMaximum(box, 100);
                NumericInputBehavior.SetStep(box, 5);

                box.Text = "7";
                NumericInputBehavior.StepBy(box, +1);
                Check.Equal("12", box.Text);
                NumericInputBehavior.StepBy(box, -1);
                NumericInputBehavior.StepBy(box, -1);
                NumericInputBehavior.StepBy(box, -1);
                Check.Equal("0", box.Text);                                               // clamped at the minimum

                box.Text = "250";
                NumericInputBehavior.Normalize(box);
                Check.Equal("100", box.Text);

                box.Language = XmlLanguage.GetLanguage("de-DE");
                Check.Equal(",", NumericInputBehavior.RulesFor(box).Culture!.NumberFormat.NumberDecimalSeparator);
                NumericInputBehavior.SetIsEnabled(box, false);                            // detaches cleanly
            });
        }
    }
}
