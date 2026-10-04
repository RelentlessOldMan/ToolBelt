using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class ValueConvertersTests
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public void BooleanToVisibility_DefaultAndOptions()
        {
            var c = new BooleanToVisibilityConverter();
            Check.Equal(Visibility.Visible, (Visibility)c.Convert(true, typeof(Visibility), null, Ci)!);
            Check.Equal(Visibility.Collapsed, (Visibility)c.Convert(false, typeof(Visibility), null, Ci)!);

            c.UseHidden = true;
            Check.Equal(Visibility.Hidden, (Visibility)c.Convert(false, typeof(Visibility), null, Ci)!);

            c.UseHidden = false;
            c.Invert = true;
            Check.Equal(Visibility.Collapsed, (Visibility)c.Convert(true, typeof(Visibility), null, Ci)!);
            Check.Equal(Visibility.Visible, (Visibility)c.Convert(false, typeof(Visibility), null, Ci)!);
        }

        public void BooleanToVisibility_ConvertBack()
        {
            var c = new BooleanToVisibilityConverter();
            Check.Equal(true, (bool)c.ConvertBack(Visibility.Visible, typeof(bool), null, Ci)!);
            Check.Equal(false, (bool)c.ConvertBack(Visibility.Collapsed, typeof(bool), null, Ci)!);
        }

        public void InverseBoolean_Negates()
        {
            var c = new InverseBooleanConverter();
            Check.Equal(false, (bool)c.Convert(true, typeof(bool), null, Ci)!);
            Check.Equal(true, (bool)c.Convert(false, typeof(bool), null, Ci)!);
            // Its own inverse.
            Check.Equal(true, (bool)c.ConvertBack(false, typeof(bool), null, Ci)!);
        }

        public void NullToVisibility_Behaviour()
        {
            var c = new NullToVisibilityConverter();
            Check.Equal(Visibility.Visible, (Visibility)c.Convert("x", typeof(Visibility), null, Ci)!);
            Check.Equal(Visibility.Collapsed, (Visibility)c.Convert(null, typeof(Visibility), null, Ci)!);

            c.EmptyStringIsNull = true;
            Check.Equal(Visibility.Collapsed, (Visibility)c.Convert("   ", typeof(Visibility), null, Ci)!);
            Check.Equal(Visibility.Visible, (Visibility)c.Convert("hi", typeof(Visibility), null, Ci)!);

            Check.Equal(Binding.DoNothing, c.ConvertBack(Visibility.Visible, typeof(object), null, Ci));
        }

        public void EnumToBoolean_MatchesParameter()
        {
            var c = new EnumToBooleanConverter();
            Check.Equal(true, (bool)c.Convert(Day.Tue, typeof(bool), Day.Tue, Ci)!);
            Check.Equal(false, (bool)c.Convert(Day.Mon, typeof(bool), Day.Tue, Ci)!);

            // ConvertBack: checked -> parameter, unchecked -> DoNothing.
            Check.Equal(Day.Tue, (Day)c.ConvertBack(true, typeof(Day), Day.Tue, Ci)!);
            Check.Equal(Binding.DoNothing, c.ConvertBack(false, typeof(Day), Day.Tue, Ci));
        }

        public void CountToVisibility_IntAndCollectionAndInvert()
        {
            var c = new CountToVisibilityConverter();
            Check.Equal(Visibility.Visible, (Visibility)c.Convert(3, typeof(Visibility), null, Ci)!);
            Check.Equal(Visibility.Collapsed, (Visibility)c.Convert(0, typeof(Visibility), null, Ci)!);
            Check.Equal(Visibility.Collapsed, (Visibility)c.Convert(null, typeof(Visibility), null, Ci)!);

            Check.Equal(Visibility.Visible, (Visibility)c.Convert(new List<int> { 1 }, typeof(Visibility), null, Ci)!);
            Check.Equal(Visibility.Collapsed, (Visibility)c.Convert(new List<int>(), typeof(Visibility), null, Ci)!);

            c.Invert = true; // "show when empty"
            Check.Equal(Visibility.Visible, (Visibility)c.Convert(new List<int>(), typeof(Visibility), null, Ci)!);
            Check.Equal(Visibility.Collapsed, (Visibility)c.Convert(new List<int> { 1 }, typeof(Visibility), null, Ci)!);

            Check.Equal(Binding.DoNothing, c.ConvertBack(Visibility.Visible, typeof(object), null, Ci));
        }

        private enum Day { Mon, Tue, Wed }
    }
}
