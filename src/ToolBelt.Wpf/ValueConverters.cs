// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
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
    /// control is checked, or <see cref="Binding.DoNothing"/> otherwise. The parameter may be the enum value itself
    /// (<c>{x:Static}</c>) or its name as a string (<c>ConverterParameter=Red</c>, as plain XAML passes it).
    /// </summary>
    public sealed class EnumToBooleanConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null) return false;
            if (parameter is string name && value is Enum)
                return TryParseName(value.GetType(), name, out object? parsed) && value.Equals(parsed);
            return value.Equals(parameter);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is bool b && b) || parameter is null) return Binding.DoNothing;
            if (parameter is string name && targetType != null)
            {
                Type enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
                if (enumType.IsEnum)
                    return TryParseName(enumType, name, out object? parsed) ? parsed : Binding.DoNothing;
            }
            return parameter;
        }

        // Names only: Enum.TryParse also took "99", writing an undefined (DayOfWeek)99 back to the source — and a
        // comma list only for a [Flags] enum: "Green,Blue" made an undefined (Color)3.
        private static bool TryParseName(Type enumType, string name, out object? value)
        {
            value = null;
            string t = name.TrimStart();
            if (t.Length == 0 || char.IsDigit(t[0]) || t[0] == '-' || t[0] == '+') return false;
            if (t.IndexOf(',') >= 0 && !enumType.IsDefined(typeof(FlagsAttribute), false)) return false;
            return Enum.TryParse(enumType, name, false, out value);
        }
    }

    /// <summary>
    /// Maps a count to a <see cref="Visibility"/>: a positive number (any numeric type), or a non-empty collection /
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
                _ when ReferenceEquals(value, DependencyProperty.UnsetValue) => false,
                int n => n > 0,
                long n => n > 0,
                uint n => n > 0,
                ulong n => n > 0,
                short n => n > 0,
                ushort n => n > 0,
                byte n => n > 0,
                sbyte n => n > 0,
                double n => n > 0,
                float n => n > 0,
                decimal n => n > 0,
                ICollection c => c.Count > 0,
                IEnumerable e => HasAny(e),
                _ => IsPositiveNumber(value) ?? true,
            };
            if (Invert) any = !any;
            return any ? Visibility.Visible : (UseHidden ? Visibility.Hidden : Visibility.Collapsed);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;

        // Half, Int128, BigInteger, nint, ...: any INumberBase<T> the switch does not name (their zero was Visible).
        private static bool? IsPositiveNumber(object value)
        {
            Type t = value.GetType();
            foreach (Type i in t.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INumberBase<>) && i.GetGenericArguments()[0] == t)
                    return (bool)PositiveMethod.MakeGenericMethod(t).Invoke(null, new[] { value })!;
            return null;
        }

        private static readonly MethodInfo PositiveMethod =
            typeof(CountToVisibilityConverter).GetMethod(nameof(Positive), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static bool Positive<T>(T v) where T : INumberBase<T> => T.IsPositive(v) && !T.IsZero(v) && !T.IsNaN(v);

        private static bool HasAny(IEnumerable sequence)
        {
            IEnumerator e = sequence.GetEnumerator();
            try { return e.MoveNext(); }
            finally { (e as IDisposable)?.Dispose(); }
        }
    }
}
