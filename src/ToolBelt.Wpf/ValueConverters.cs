// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

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
    /// <see cref="UseHidden"/>). <see cref="DBNull"/> counts as null. A bool counts as itself; any other object (not a
    /// number or a sequence: a char, a TimeSpan, an enum value) counts as present, so is Visible. A sequence without a
    /// count is checked through its WPF default view on a dispatcher thread, so a one-shot source (a data reader) keeps
    /// its items for an ItemsControl bound to it; off a dispatcher it is enumerated. A number that is not real (a Complex with an imaginary part) has no sign: it is Visible
    /// unless it is zero or has a NaN part. A string is a sequence of characters, so "0" is Visible: bind the number itself. <see cref="Invert"/> swaps the sense — handy for "show this when the list is
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
                DBNull => false,   // a database null was Visible
                char => true,      // a char is a character, not a count: '\0' was Collapsed and 'a' Visible
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
                // Complex.IsNaN is false for (Infinity, NaN), so the generic check below showed it: a NaN part hides it.
                Complex z => !double.IsNaN(z.Real) && !double.IsNaN(z.Imaginary) && (z.Imaginary != 0 || z.Real > 0),
                bool b => b,   // false showed the element (a HasItems mis-bound to the count converter)
                ICollection c => c.Count > 0,
                string s => s.Length > 0,
                System.ComponentModel.ICollectionView v => !v.IsEmpty,
                IEnumerable e => ReadOnlyCount(e) is int n ? n > 0 : HasAnyKeepingItems(e),
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

        // A number that is not real (a Complex with an imaginary part) has no sign: it shows when it is not zero. IsPositive
        // looked at the real part only, so Complex(0, 5) was Collapsed.
        private static bool Positive<T>(T v) where T : INumberBase<T>
            => !T.IsZero(v) && !T.IsNaN(v) && (T.IsPositive(v) || !T.IsRealNumber(v));

        private static int? ReadOnlyCount(IEnumerable sequence)
        {
            foreach (Type i in sequence.GetType().GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IReadOnlyCollection<>))
                    return (int)i.GetProperty("Count")!.GetValue(sequence)!;
            return null;
        }

        // Peeking with MoveNext used up the first item of a one-shot source (a DbDataReader, a consuming enumerable), so
        // the ItemsControl bound to it lost data. WPF snapshots such a source once in its default view, which an
        // ItemsControl bound to the same source shares: ask the view.
        private static bool HasAnyKeepingItems(IEnumerable sequence)
        {
            if (Dispatcher.FromThread(System.Threading.Thread.CurrentThread) is null) return HasAny(sequence);
            IEnumerator? e = sequence.GetEnumerator();   // a broken GetEnumerator: WPF's view would throw
            if (e is null) return false;
            (e as IDisposable)?.Dispose();
            System.ComponentModel.ICollectionView? view = CollectionViewSource.GetDefaultView(sequence);
            return view is null ? HasAny(sequence) : !view.IsEmpty;
        }

        private static bool HasAny(IEnumerable sequence)
        {
            IEnumerator? e = sequence.GetEnumerator();
            if (e is null) return false;   // a broken GetEnumerator threw NullReferenceException from the converter
            try { return e.MoveNext(); }
            finally { (e as IDisposable)?.Dispose(); }
        }
    }
}
