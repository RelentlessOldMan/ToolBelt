using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>
    /// Round 139 review regressions (WPF satellite, eighth pass): each failed on the code before its fix.
    /// </summary>
    public sealed class WpfReview139Tests
    {
        private static NumericInputRules Rules(double min, double max, int places)
            => new NumericInputRules { Minimum = min, Maximum = max, DecimalPlaces = places, Culture = CultureInfo.InvariantCulture };

        // ---------- NumericInputBehavior ----------

        // WPF shows a small bound double as "1E-05"; read as no number, a step started from 0.
        public void NumericInput_StepsAndCoercesExponentText()
        {
            Check.Equal("0.00002", NumericInputBehavior.StepValue("1E-05", +1, 0.00001, Rules(-1, 1, 5)));
            Check.Equal("-0.0000001", NumericInputBehavior.StepValue("-2E-07", +1, 1e-7, Rules(-1, 1, 7)));
            Check.Equal("0.00001", NumericInputBehavior.Coerce("1E-05", Rules(-1, 1, 5)));
            Check.Equal("100", NumericInputBehavior.Coerce("1.5e3", Rules(0, 100, 0)));
            Check.Equal(null, NumericInputBehavior.Coerce("1e400", Rules(double.NegativeInfinity, double.PositiveInfinity, 0)));
        }

        // A bound 3.14159 at two decimals refused every deletion, since each left too many decimals.
        public void NumericInput_ABoxHoldingRefusedTextCanBeEditedDown()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var box = new TextBox { Language = XmlLanguage.GetLanguage("en-US") };
                NumericInputBehavior.SetIsEnabled(box, true);
                NumericInputBehavior.SetMinimum(box, -1000);
                NumericInputBehavior.SetMaximum(box, 1000);
                NumericInputBehavior.SetDecimalPlaces(box, 2);
                box.Text = "3.14159";
                box.CaretIndex = box.Text.Length;
                EditingCommands.Backspace.Execute(null, box);
                Check.Equal("3.1415", box.Text);
                box.CaretIndex = 0;
                EditingCommands.Delete.Execute(null, box);
                Check.Equal(".1415", box.Text);

                // Guard: in an acceptable box a deletion that would make it unacceptable is still refused.
                box.Text = "10.5";
                NumericInputBehavior.SetMinimum(box, 0);         // no sign to type, so "105" cannot become "-105"
                NumericInputBehavior.SetMaximum(box, 100);
                box.CaretIndex = 3;
                EditingCommands.Backspace.Execute(null, box);   // "105"
                Check.Equal("10.5", box.Text);
            });
        }

        // ---------- ValidationRuleSet ----------

        public void Matches_ABadPatternNamesThePattern()
        {
            var ex = Check.Throws<ArgumentException>(() => new ValidationRuleSet().For("Code", () => "abc").Matches("abc\\"));
            Check.Equal("pattern", ex.ParamName);
        }

        // ---------- PlotPresenter ----------

        // A fit clamped to a 1e308 zoom stored infinite offsets, after which every pan and zoom was refused.
        public void PlotPresenter_AClampedFitStaysFinite()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var p = new PlotPresenter { MaxZoom = 1e308, MinZoom = 1e308 };
                p.Source = PlotPresenter.BitmapFromRgba(10, 10, new byte[400]);
                p.Measure(new Size(200, 100));
                p.Arrange(new Rect(0, 0, 200, 100));
                p.FitToView();
                Matrix v = p.ViewTransform;
                Check.True(double.IsFinite(v.OffsetX) && double.IsFinite(v.OffsetY), v.ToString());
                p.MinZoom = 1;
                p.MaxZoom = 10;
                p.FitToView();
                Matrix before = p.ViewTransform;
                p.PanBy(new Vector(5, 5));
                Check.True(p.ViewTransform != before, "the pan was refused");
            });
        }
    }
}
