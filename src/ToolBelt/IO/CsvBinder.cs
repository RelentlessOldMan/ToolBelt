// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ToolBelt.Objects;

namespace ToolBelt.IO
{
    /// <summary>
    /// Maps between string field arrays (as produced by <see cref="CsvLine"/>) and typed objects using a
    /// header of column names. Columns match public properties case-insensitively; values convert through
    /// <see cref="TypeUtils.TryConvert"/>. This mapping step is the single biggest reason people reach for a
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

            var record = new T();
            for (int i = 0; i < _header.Count; i++)
            {
                if (!_properties.TryGetValue(_header[i], out PropertyInfo? prop) || !prop.CanWrite)
                    continue; // no matching writable property; ignore the column
                if (!TypeUtils.TryConvert(fields[i], prop.PropertyType, out object? value))
                    throw new FormatException($"Column '{_header[i]}': cannot convert '{fields[i]}' to {TypeUtils.FriendlyName(prop.PropertyType)}.");
                prop.SetValue(record, value);
            }
            return record;
        }

        /// <summary>Extracts a row of fields from a record, in header order.</summary>
        public string[] ToFields(T record)
        {
            if (record is null) throw new ArgumentNullException(nameof(record));
            var result = new string[_header.Count];
            for (int i = 0; i < _header.Count; i++)
            {
                if (_properties.TryGetValue(_header[i], out PropertyInfo? prop) && prop.CanRead)
                    result[i] = Convert.ToString(prop.GetValue(record), CultureInfo.InvariantCulture) ?? string.Empty;
                else
                    result[i] = string.Empty;
            }
            return result;
        }
    }
}
