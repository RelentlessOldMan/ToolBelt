// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.Globalization;
using System.Windows.Data;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// An <see cref="IMultiValueConverter"/> that ANDs several bound booleans: returns true only when every
    /// value is <c>true</c> (a non-bool or null value counts as false). Pair it with a <c>MultiBinding</c>
    /// to enable a control only when several conditions hold. One-way — <see cref="ConvertBack"/> returns null.
    /// </summary>
    public sealed class BooleanAndConverter : IMultiValueConverter
    {
        public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is null)
                return false;
            foreach (object? value in values)
                if (!(value is bool b && b))
                    return false;
            return true;
        }

        public object?[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => null;
    }

    /// <summary>
    /// An <see cref="IMultiValueConverter"/> that ORs several bound booleans: returns true when any value is
    /// <c>true</c> (non-bool or null counts as false). One-way — <see cref="ConvertBack"/> returns null.
    /// </summary>
    public sealed class BooleanOrConverter : IMultiValueConverter
    {
        public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is null)
                return false;
            foreach (object? value in values)
                if (value is bool b && b)
                    return true;
            return false;
        }

        public object?[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => null;
    }
}
