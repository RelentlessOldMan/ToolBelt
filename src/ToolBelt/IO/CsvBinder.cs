// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace ToolBelt.IO
{
    /// <summary>
    /// Maps between string field arrays (as produced by <see cref="CsvLine"/>) and typed objects using a
    /// header of column names. Columns match public properties case-insensitively, an exact-case name first (a column
    /// that matches two properties differing only in case, neither exactly, throws <see cref="ArgumentException"/>); a
    /// property hidden with <c>new</c> is the derived one. Values convert with the
    /// invariant culture: numbers, bool, enums (by name or number), char, <see cref="Guid"/>, <see cref="TimeSpan"/>,
    /// <see cref="DateTime"/> and <see cref="DateTimeOffset"/> (round-trip "O" format, Kind and offset kept; a
    /// <see cref="DateTimeOffset"/> written without an offset is UTC), <see cref="Uri"/> and strings. A floating-point
    /// field takes '.' as its decimal point and no thousands separator ("1,5" is refused, not read as 15), and one too
    /// large for its type is a signed infinity for float and double (on every runtime) and refused for decimal;
    /// an empty field is null for a nullable or reference-type property, except a string,
    /// which gets "" (CSV cannot tell a null string from an empty one, so a null string reads back as "").
    /// <see cref="ToFields"/> writes values in the same forms, so a record round-trips. This mapping step is the single biggest reason people reach for a
    /// third-party CSV package, and it is very achievable BCL-only.
    /// </summary>
    public sealed class CsvBinder<T> where T : new()
    {
        private readonly List<string> _header;
        private readonly PropertyInfo?[] _columns;   // the property each header column binds to, or null

        public CsvBinder(IEnumerable<string> header)
        {
            if (header is null) throw new ArgumentNullException(nameof(header));
            _header = new List<string>(header);
            // One property per name, the most derived: a property hidden with `new` was bound (or written) as the base one,
            // whichever GetProperties listed last. Then a column takes the property of its exact name, or the only one that
            // matches ignoring case: with Name and NAME both declared, "name" picked one by listing order.
            var byName = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
            foreach (PropertyInfo p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length != 0) continue;
                if (!byName.TryGetValue(p.Name, out PropertyInfo? seen) || p.DeclaringType!.IsSubclassOf(seen.DeclaringType!))
                    byName[p.Name] = p;
            }
            _columns = new PropertyInfo?[_header.Count];
            for (int i = 0; i < _header.Count; i++)
            {
                string? column = _header[i];
                if (column is null) continue;
                if (byName.TryGetValue(column, out PropertyInfo? exact)) { _columns[i] = exact; continue; }
                foreach (PropertyInfo p in byName.Values)
                {
                    if (!string.Equals(p.Name, column, StringComparison.OrdinalIgnoreCase)) continue;
                    if (_columns[i] != null)
                        throw new ArgumentException($"Column '{column}' matches both '{_columns[i]!.Name}' and '{p.Name}'.", nameof(header));
                    _columns[i] = p;
                }
            }
        }

        public IReadOnlyList<string> Header => _header;

        /// <summary>Creates a <typeparamref name="T"/> from a row of fields aligned to the header.</summary>
        public T Bind(IReadOnlyList<string> fields)
        {
            if (fields is null) throw new ArgumentNullException(nameof(fields));
            if (fields.Count != _header.Count)
                throw new ArgumentException($"Expected {_header.Count} fields to match the header, got {fields.Count}.", nameof(fields));

            // Boxed once: for a struct T, SetValue on `record` itself would set a fresh boxed copy each time and lose it.
            object record = new T();
            for (int i = 0; i < _header.Count; i++)
            {
                PropertyInfo? prop = _columns[i];
                if (prop is null || !prop.CanWrite)
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
                PropertyInfo? prop = _columns[i];
                if (prop != null && prop.CanRead)
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

        // "R" can lose the last bit on .NET Framework (as CsvDialect.FormatNumber notes); G17 / G9 always round-trip. There
        // -0 also formats as "0" (and "-0" parses as +0), so the sign of zero is written, and read, here.
        private static string FormatDouble(double d)
        {
            if (d == 0) return BitConverter.DoubleToInt64Bits(d) < 0 ? "-0" : "0";
            string s = d.ToString("R", CultureInfo.InvariantCulture);
            if (!double.IsNaN(d) && ReadBack(s, 53) != d)
                s = d.ToString("G17", CultureInfo.InvariantCulture);
            return s;
        }

        private static string FormatSingle(float f)
        {
            if (f == 0) return 1 / f < 0 ? "-0" : "0";
            string s = f.ToString("R", CultureInfo.InvariantCulture);
            if (!float.IsNaN(f) && (float)ReadBack(s, 24) != f)
                s = f.ToString("G9", CultureInfo.InvariantCulture);
            return s;
        }

        // NaN, Infinity or \u221E in any case, after an optional sign, on every runtime: .NET 8 took "nan", "-NaN" and
        // "infinity", .NET Framework only "NaN", "Infinity" and "-Infinity", so such a field bound on one and failed on the other.
        // The text read back as TryConvert does: .NET Framework's own parse agreed with "R" on text the nearest value differs from.
        private static double ReadBack(string s, int precision)
        {
            if (TryFloatWord(s, out double named)) return named;
#if NETSTANDARD2_0
            if (TryNearestBinary(s, precision, out double nearest)) return nearest;
#endif
            return precision == 53 ? double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture) : float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static bool TryFloatWord(string text, out double value)
        {
            string t = text.Trim();
            bool minus = t.Length > 0 && t[0] == '-';
            string w = t.Length > 0 && (t[0] == '-' || t[0] == '+') ? t.Substring(1) : t;
            if (w.Equals("Infinity", StringComparison.OrdinalIgnoreCase) || w == "\u221E")
            {
                value = minus ? double.NegativeInfinity : double.PositiveInfinity;
                return true;
            }
            value = double.NaN;
            return w.Equals("NaN", StringComparison.OrdinalIgnoreCase);
        }

#if NETSTANDARD2_0
        // The double nearest the decimal in text, ties to even, for text the invariant parse accepts (or a well-formed
        // number past its range); precision is 53 for a double, 24 for a float. .NET Framework's parse is not correctly
        // rounded: long decimals came out a unit in the last place off ("9.8598758830020559e-205"), and a float parsed
        // through a double rounded twice. False when the text holds no digits (a word: NaN, Infinity), left to the caller.
        // CsvDialect, CsvBinder, ConfigBinder and EnvironmentVariables hold this same block: change one, change all four.
        private static bool TryNearestBinary(string text, int precision, out double value)
        {
            value = 0;
            int i = 0, n = text.Length;
            while (i < n && (text[i] == ' ' || (text[i] >= '\t' && text[i] <= '\r'))) i++;
            bool negative = false;
            if (i < n && (text[i] == '+' || text[i] == '-')) { negative = text[i] == '-'; i++; }
            var kept = new System.Text.StringBuilder();
            long exp10 = 0;                         // the value is kept * 10^exp10, before the exponent part
            bool any = false, point = false, sticky = false;
            for (; i < n; i++)
            {
                char c = text[i];
                if (c == '.' && !point) { point = true; continue; }
                if (c < '0' || c > '9') break;
                any = true;
                if (kept.Length == 0 && c == '0') { if (point) exp10--; continue; }
                // 800 significant digits decide the rounding (a midpoint has at most 767); the rest only say "above".
                if (kept.Length < 800) { kept.Append(c); if (point) exp10--; }
                else { if (c != '0') sticky = true; if (!point) exp10++; }
            }
            if (!any) return false;
            if (i < n && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                bool minus = i < n && text[i] == '-';
                if (i < n && (text[i] == '+' || text[i] == '-')) i++;
                long e = 0;
                for (; i < n && text[i] >= '0' && text[i] <= '9'; i++) if (e < 100000000) e = e * 10 + (text[i] - '0');
                exp10 += minus ? -e : e;
            }
            double magnitude = 0;
            if (kept.Length > 0)
            {
                long top = exp10 + kept.Length;     // the value is below 10^top and at least 10^(top - 1)
                if (top > 310) magnitude = double.PositiveInfinity;
                else if (top >= -330)
                {
                    var digits = System.Numerics.BigInteger.Parse(kept.ToString(), NumberStyles.None, CultureInfo.InvariantCulture);
                    if (sticky) { digits = digits * 10 + 1; exp10--; }
                    magnitude = NearestBinary(digits, (int)exp10, precision);
                }
            }
            value = negative ? -magnitude : magnitude;
            return true;
        }

        // digits * 10^exp10 to the nearest value of the given precision, ties to even; infinity past the largest finite.
        private static double NearestBinary(System.Numerics.BigInteger digits, int exp10, int precision)
        {
            int least = precision == 53 ? -1074 : -149;      // the exponent of the smallest subnormal
            int limit = precision == 53 ? 1024 : 128;        // 2^limit is past the largest finite value
            System.Numerics.BigInteger num = digits, den = System.Numerics.BigInteger.One;
            if (exp10 >= 0) num *= System.Numerics.BigInteger.Pow(10, exp10);
            else den = System.Numerics.BigInteger.Pow(10, -exp10);
            long e = Math.Max(least, BinaryLength(num) - BinaryLength(den) - precision);
            while (true)
            {
                // q = floor(value / 2^e), with precision bits unless e is the subnormal floor.
                System.Numerics.BigInteger d = e > 0 ? den << (int)e : den;
                System.Numerics.BigInteger q = System.Numerics.BigInteger.DivRem(e < 0 ? num << (int)-e : num, d, out System.Numerics.BigInteger r);
                long bits = BinaryLength(q);
                if (bits > precision) { e++; continue; }
                if (bits < precision && e > least) { e--; continue; }
                int c = (r << 1).CompareTo(d);
                if (c > 0 || (c == 0 && !q.IsEven)) q += 1;
                if (BinaryLength(q) > precision) { q >>= 1; e++; }
                if (e + precision > limit) return double.PositiveInfinity;
                double m = (long)q;                          // exact: below 2^53
                // Scaling by a power of two is exact when the result is representable; a subnormal one goes in two steps.
                return e < -1022 ? m * PowerOfTwo((int)e + 64) * PowerOfTwo(-64) : m * PowerOfTwo((int)e);
            }
        }

        private static long BinaryLength(System.Numerics.BigInteger x)
        {
            byte[] b = x.ToByteArray();
            int top = b.Length - 1;
            while (top > 0 && b[top] == 0) top--;
            int bits = 0;
            for (int v = b[top]; v != 0; v >>= 1) bits++;
            return top * 8L + bits;
        }

        private static double PowerOfTwo(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
#endif

        private static bool TryConvert(string text, Type type, out object? value)
        {
            value = null;
            Type? underlying = Nullable.GetUnderlyingType(type);
            Type t = underlying ?? type;
            if (t == typeof(string)) { value = text; return true; }
            // Before the trim: a char? holding ' ' or a tab was written as itself but read back as null.
            if (t == typeof(char))
            {
                if (text.Length == 0 && underlying != null) return true;
                if (text.Length != 1) return false;
                value = text[0];
                return true;
            }
            string s = text.Trim();
            // Empty -> null. After the trim, as values are read: " 5 " was 5 but "  " was an error, not null.
            if (s.Length == 0 && (underlying != null || !type.IsValueType)) return true;
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
                    // No offset in the text took the machine's local one, so the value depended on where it was read.
                    bool ok = DateTimeOffset.TryParse(s, inv, DateTimeStyles.AssumeUniversal, out DateTimeOffset d);
                    value = d;
                    return ok;
                }
                // The untrimmed text, as for a string: a relative Uri " rel " came back as "rel" (an absolute one trims itself).
                if (t == typeof(Uri)) { bool ok = Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out Uri? u); value = u; return ok; }
                // Convert.ChangeType allowed thousands separators in these, so "1,5" bound as 15; and an overflow gave
                // infinity on .NET Core but FormatException on .NET Framework.
                if (t == typeof(double) || t == typeof(float))
                {
                    if (TryFloatWord(s, out double named)) { value = t == typeof(double) ? named : (object)(float)named; return true; }
                    double d;
                    try { d = double.Parse(s, NumberStyles.Float, inv); }
                    catch (OverflowException) { d = s.Length > 0 && s[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity; }
#if NETSTANDARD2_0
                    if (TryNearestBinary(s, 53, out double nearest)) d = nearest;
#endif
                    if (d == 0 && s.Length > 0 && s[0] == '-') d = -0.0;
                    if (t == typeof(double)) { value = d; return true; }
                    float f;
                    try { f = float.Parse(s, NumberStyles.Float, inv); }
                    catch (OverflowException) { f = d < 0 ? float.NegativeInfinity : float.PositiveInfinity; }
#if NETSTANDARD2_0
                    // .NET Framework's float parse went through a double, rounding twice ("16777217.0000000001" gave 16777216).
                    if (TryNearestBinary(s, 24, out double nearestFloat)) f = (float)nearestFloat;
#endif
                    if (f == 0 && s.Length > 0 && s[0] == '-') f = -0.0f;
                    value = f;
                    return true;
                }
                if (t == typeof(decimal)) { bool ok = decimal.TryParse(s, NumberStyles.Float, inv, out decimal m); value = m; return ok; }
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
