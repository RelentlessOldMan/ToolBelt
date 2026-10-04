// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// Converts a <see cref="bool"/> to a <see cref="Visibility"/>. By default true → Visible and false →
    /// Collapsed; set <see cref="UseHidden"/> to use Hidden instead of Collapsed, or <see cref="Invert"/> to
    /// swap the sense. <see cref="ConvertBack"/> maps Visible → true and anything else → false.
    /// </summary>
    public sealed class BooleanToVisibilityConverter : IValueConverter
    {
        /// <summary>Use <see cref="Visibility.Hidden"/> instead of <see cref="Visibility.Collapsed"/> for the false case.</summary>
        public bool UseHidden { get; set; }

        /// <summary>Invert the mapping (true → hidden/collapsed, false → visible).</summary>
        public bool Invert { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            if (Invert) flag = !flag;
            return flag ? Visibility.Visible : (UseHidden ? Visibility.Hidden : Visibility.Collapsed);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool visible = value is Visibility v && v == Visibility.Visible;
            return Invert ? !visible : visible;
        }
    }

    /// <summary>Negates a <see cref="bool"/> (and is its own inverse). Non-bool input is treated as false.</summary>
    public sealed class InverseBooleanConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => !(value is bool b && b);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => !(value is bool b && b);
    }

    /// <summary>
    /// Maps null (optionally also empty/whitespace strings, via <see cref="EmptyStringIsNull"/>) to a hidden
    /// visibility and non-null to Visible. <see cref="Invert"/> swaps the sense. One-way:
    /// <see cref="ConvertBack"/> returns <see cref="Binding.DoNothing"/>.
    /// </summary>
    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public bool UseHidden { get; set; }
        public bool Invert { get; set; }

        /// <summary>Treat an empty (or whitespace) string the same as null.</summary>
        public bool EmptyStringIsNull { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool present = value is not null && !(EmptyStringIsNull && value is string s && string.IsNullOrWhiteSpace(s));
            if (Invert) present = !present;
            return present ? Visibility.Visible : (UseHidden ? Visibility.Hidden : Visibility.Collapsed);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// For binding an enum to toggle/radio buttons: <see cref="Convert"/> returns true when the bound value
    /// equals the <c>ConverterParameter</c>; <see cref="ConvertBack"/> returns that parameter when the
    /// control is checked, or <see cref="Binding.DoNothing"/> otherwise.
    /// </summary>
    public sealed class EnumToBooleanConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is not null && value.Equals(parameter);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b && b ? parameter : Binding.DoNothing;
    }

    /// <summary>
    /// Maps a count to a <see cref="Visibility"/>: a positive <see cref="int"/>, or a non-empty collection /
    /// sequence, becomes Visible; null, zero, or an empty collection becomes Collapsed (or Hidden via
    /// <see cref="UseHidden"/>). <see cref="Invert"/> swaps the sense — handy for "show this when the list is
    /// empty" placeholders. One-way: <see cref="ConvertBack"/> returns <see cref="Binding.DoNothing"/>.
    /// </summary>
    public sealed class CountToVisibilityConverter : IValueConverter
    {
        public bool UseHidden { get; set; }
        public bool Invert { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool any = value switch
            {
                null => false,
                int n => n > 0,
                ICollection c => c.Count > 0,
                IEnumerable e => HasAny(e),
                _ => true,
            };
            if (Invert) any = !any;
            return any ? Visibility.Visible : (UseHidden ? Visibility.Hidden : Visibility.Collapsed);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;

        private static bool HasAny(IEnumerable sequence)
        {
            IEnumerator e = sequence.GetEnumerator();
            try { return e.MoveNext(); }
            finally { (e as IDisposable)?.Dispose(); }
        }
    }
}
