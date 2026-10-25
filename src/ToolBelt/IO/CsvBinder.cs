// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace ToolBelt.IO
{
    /// <summary>
    /// Maps between string field arrays (as produced by <see cref="CsvLine"/>) and typed objects using a
    /// header of column names. Columns match public properties case-insensitively. Values convert with the
    /// invariant culture: numbers, bool, enums (by name or number), char, <see cref="Guid"/>, <see cref="TimeSpan"/>,
    /// <see cref="DateTime"/> and <see cref="DateTimeOffset"/> (round-trip "O" format, Kind and offset kept),
    /// <see cref="Uri"/> and strings; an empty field is null for a nullable or reference-type property, except a string,
    /// which gets "" (CSV cannot tell a null string from an empty one, so a null string reads back as "").
    /// <see cref="ToFields"/> writes values in the same forms, so a record round-trips. This mapping step is the single biggest reason people reach for a
    /// third-party CSV package, and it is very achievable BCL-only.
    /// </summary>
    public sealed class CsvBinder<T> where T : new()
    {
        private readonly List<string> _header;
        private readonly Dictionary<string, PropertyInfo> _properties;

        public CsvBinder(IEnumerable<string> header)
        {
            if (header is null) throw new ArgumentNullException(nameof(header));
            _header = new List<string>(header);
            _properties = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyInfo p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.GetIndexParameters().Length == 0)
                    _properties[p.Name] = p;
        }

        public IReadOnlyList<string> Header => _header;

        /// <summary>Creates a <typeparamref name="T"/> from a row of fields aligned to the header.</summary>
        public T Bind(IReadOnlyList<string> fields)
        {
            if (fields is null) throw new ArgumentNullException(nameof(fields));
            if (fields.Count != _header.Count)
                throw new ArgumentException($"Expected {_header.Count} fields to match the header, got {fields.Count}.");

            // Boxed once: for a struct T, SetValue on `record` itself would set a fresh boxed copy each time and lose it.
            object record = new T();
            for (int i = 0; i < _header.Count; i++)
            {
                if (!_properties.TryGetValue(_header[i], out PropertyInfo? prop) || !prop.CanWrite)
                    continue; // no matching writable property; ignore the column
                if (!TryConvert(fields[i] ?? string.Empty, prop.PropertyType, out object? value))
                    throw new FormatException($"Column '{_header[i]}': cannot convert '{fields[i]}' to {prop.PropertyType.Name}.");
                prop.SetValue(record, value);
            }
            return (T)record;
        }

        /// <summary>Extracts a row of fields from a record, in header order.</summary>
        public string[] ToFields(T record)
        {
            if (record is null) throw new ArgumentNullException(nameof(record));
            var result = new string[_header.Count];
            for (int i = 0; i < _header.Count; i++)
            {
                if (_properties.TryGetValue(_header[i], out PropertyInfo? prop) && prop.CanRead)
                    result[i] = FormatValue(prop.GetValue(record));
                else
                    result[i] = string.Empty;
            }
            return result;
        }

        private static string FormatValue(object? value) => value switch
        {
            null => string.Empty,
            DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan t => t.ToString("c", CultureInfo.InvariantCulture),
            double d => FormatDouble(d),
            float f => FormatSingle(f),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

        // "R" can lose the last bit on .NET Framework (as CsvDialect.FormatNumber notes); G17 / G9 always round-trip.
        private static string FormatDouble(double d)
        {
            string s = d.ToString("R", CultureInfo.InvariantCulture);
            if (!double.IsNaN(d) && double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture) != d)
                s = d.ToString("G17", CultureInfo.InvariantCulture);
            return s;
        }

        private static string FormatSingle(float f)
        {
            string s = f.ToString("R", CultureInfo.InvariantCulture);
            if (!float.IsNaN(f) && float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture) != f)
                s = f.ToString("G9", CultureInfo.InvariantCulture);
            return s;
        }

        private static bool TryConvert(string text, Type type, out object? value)
        {
            value = null;
            Type? underlying = Nullable.GetUnderlyingType(type);
            Type t = underlying ?? type;
            if (t == typeof(string)) { value = text; return true; }
            if (text.Length == 0 && (underlying != null || !type.IsValueType)) return true;   // empty -> null
            string s = text.Trim();
            CultureInfo inv = CultureInfo.InvariantCulture;
            try
            {
                if (t.IsEnum)
                {
                    try { value = Enum.Parse(t, s, ignoreCase: true); return true; }
                    catch (ArgumentException) { return false; }
                }
                if (t == typeof(Guid)) { bool ok = Guid.TryParse(s, out Guid g); value = g; return ok; }
                if (t == typeof(TimeSpan)) { bool ok = TimeSpan.TryParse(s, inv, out TimeSpan ts); value = ts; return ok; }
                if (t == typeof(DateTime))
                {
                    bool ok = DateTime.TryParse(s, inv, DateTimeStyles.RoundtripKind, out DateTime d);
                    value = d;
                    return ok;
                }
                if (t == typeof(DateTimeOffset))
                {
                    bool ok = DateTimeOffset.TryParse(s, inv, DateTimeStyles.None, out DateTimeOffset d);
                    value = d;
                    return ok;
                }
                if (t == typeof(Uri)) { bool ok = Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out Uri? u); value = u; return ok; }
                if (t == typeof(char)) { if (text.Length != 1) return false; value = text[0]; return true; }
                if (typeof(IConvertible).IsAssignableFrom(t))
                {
                    value = Convert.ChangeType(s, t, inv);
                    return true;
                }
                return false;
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                return false;
            }
        }
    }
}
