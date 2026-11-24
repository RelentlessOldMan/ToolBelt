// ToolBelt drop-in — also copy IO/CsvLine.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ToolBelt.IO
{
    /// <summary>
    /// How a delimited file is laid out — delimiter, decimal separator, header row — with presets for the variants that
    /// actually turn up (RFC 4180, European Excel's "<c>;</c> with decimal commas", tab, pipe) and <see cref="Detect"/>,
    /// which sniffs a sample: the delimiter is the candidate giving the most consistent field count across lines
    /// (quote-aware; a tie goes to the split that leaves more fields looking like numbers, so "1,5;2,5" is ';'), the
    /// decimal separator is inferred from fields that actually contain one (a "1,000"-style value is ambiguous and only
    /// counts when nothing else decides), and a header is assumed when the first row is text where later rows are numbers. <see cref="ParseNumber"/> then reads values the dialect's way.
    /// </summary>
    public sealed class CsvDialect
    {
        public CsvDialect(char delimiter = ',', char decimalSeparator = '.', bool hasHeader = true)
        {
            if (delimiter == decimalSeparator) throw new ArgumentException("The delimiter and the decimal separator must differ.", nameof(decimalSeparator));
            // A separator that a formatted number already holds (a digit, 'E', a sign, the letters of NaN and Infinity) or
            // that a field cannot carry unquoted made FormatNumber's output unreadable: 1.5 with '1' wrote "115".
            if (char.IsLetterOrDigit(decimalSeparator) || char.IsWhiteSpace(decimalSeparator) || char.IsControl(decimalSeparator)
                || char.IsSurrogate(decimalSeparator) || decimalSeparator == '+' || decimalSeparator == '-' || decimalSeparator == '"')
                throw new ArgumentException("The decimal separator cannot be a letter, digit, sign, quote, whitespace or control character.", nameof(decimalSeparator));
            if (delimiter == '"' || delimiter == '\r' || delimiter == '\n') throw new ArgumentException("Invalid delimiter.", nameof(delimiter));
            Delimiter = delimiter;
            DecimalSeparator = decimalSeparator;
            HasHeader = hasHeader;
        }

        public char Delimiter { get; }
        public char DecimalSeparator { get; }
        public bool HasHeader { get; }

        /// <summary>Comma-separated, '.' decimals, header — RFC 4180 and English-locale Excel.</summary>
        public static CsvDialect Rfc4180 { get; } = new CsvDialect(',', '.');

        /// <summary>Semicolon-separated with decimal commas — what Excel writes in most of Europe.</summary>
        public static CsvDialect ExcelEuropean { get; } = new CsvDialect(';', ',');

        public static CsvDialect Tab { get; } = new CsvDialect('\t', '.');
        public static CsvDialect Pipe { get; } = new CsvDialect('|', '.');

        public IReadOnlyList<string> ParseLine(string line) => CsvLine.Parse(line, Delimiter);

        public string FormatLine(IEnumerable<string> fields) => CsvLine.Format(fields, Delimiter);

        /// <summary>Formats a number with this dialect's decimal separator (round-trippable).</summary>
        public string FormatNumber(double value)
        {
            // .NET Framework formats -0 as "0" (and parses "-0" as +0), so the sign of zero is written, and read, here.
            if (value == 0) return BitConverter.DoubleToInt64Bits(value) < 0 ? "-0" : "0";
            string s = value.ToString("R", CultureInfo.InvariantCulture);
            // "R" can lose the last bit on .NET Framework; G17 always round-trips. The check reads it back as ParseNumber does:
            // .NET Framework's own parse is off where "R" is, so the two agreed on text that read back wrong once rounded right.
            if (!double.IsNaN(value) && (!TryParseDouble(s, out double back) || back != value))
                s = value.ToString("G17", CultureInfo.InvariantCulture);
            return DecimalSeparator == '.' ? s : s.Replace('.', DecimalSeparator);
        }

        /// <summary>Parses a number written with this dialect's decimal separator; surrounding whitespace is allowed, thousands separators are not.</summary>
        public bool TryParseNumber(string field, out double value)
        {
            if (field is null) throw new ArgumentNullException(nameof(field));
            string s = field.Trim();
            if (DecimalSeparator != '.')
            {
                if (s.IndexOf('.') >= 0) { value = 0; return false; }   // a '.' in a decimal-comma file is not ours to guess about
                s = s.Replace(DecimalSeparator, '.');
            }
            if (!TryParseDouble(s, out value)) return false;
            if (value == 0 && s.Length > 0 && s[0] == '-') value = -0.0;
            return true;
        }

        public double ParseNumber(string field)
            => TryParseNumber(field, out double v) ? v : throw new FormatException($"'{field}' is not a number in this dialect (decimal separator '{DecimalSeparator}').");

        private static readonly char[] Candidates = { ',', ';', '\t', '|' };

        /// <summary>Infers the dialect from sample lines (the first few dozen are plenty). Blank lines are ignored.</summary>
        public static CsvDialect Detect(IEnumerable<string> sampleLines)
        {
            if (sampleLines is null) throw new ArgumentNullException(nameof(sampleLines));
            var lines = sampleLines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(200).ToList();
            if (lines.Count == 0) return Rfc4180;

            // Delimiter: most lines sharing the modal field count (>1 field), then the larger share of numeric-looking fields
            // (either decimal separator), then more fields, then candidate order.
            char best = ',';
            (int Agree, double Numeric, int Fields) bestScore = (-1, 0, 0);
            foreach (char c in Candidates)
            {
                var parsed = lines.Select(l => SafeParse(l, c)).ToList();
                var modal = parsed.Select(r => r.Count).Where(n => n > 1).GroupBy(n => n)
                    .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).FirstOrDefault();
                if (modal == null) continue;
                var fields = parsed.SelectMany(r => r).Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
                double numeric = fields.Count == 0 ? 0 : (double)fields.Count(f => LooksNumeric(f, '.') || LooksNumeric(f, ',')) / fields.Count;
                var score = (modal.Count(), numeric, modal.Key);
                if (score.Item1 > bestScore.Agree
                    || (score.Item1 == bestScore.Agree && score.Item2 > bestScore.Numeric)
                    || (score.Item1 == bestScore.Agree && score.Item2 == bestScore.Numeric && score.Item3 > bestScore.Fields))
                { best = c; bestScore = score; }
            }

            var rows = lines.Select(l => SafeParse(l, best)).ToList();
            var body = rows.Skip(1).SelectMany(r => r).Select(f => f.Trim()).ToList();
            var all = rows.SelectMany(r => r).Select(f => f.Trim()).ToList();

            // Decimal separator: a comma can only be decimal when it isn't the delimiter. Only fields that contain the separator
            // are evidence (integers say nothing), and "1,000" / "1.000" could be thousands groups, so those only break a tie.
            char dec = '.';
            if (best != ',')
            {
                int comma = 0, dot = 0, commaAmbiguous = 0, dotAmbiguous = 0;
                foreach (string f in all)
                {
                    if (f.IndexOf(',') >= 0 && LooksNumeric(f, ','))
                    {
                        if (LooksLikeThousands(f, ',')) commaAmbiguous++; else comma++;
                    }
                    else if (f.IndexOf('.') >= 0 && LooksNumeric(f, '.'))
                    {
                        if (LooksLikeThousands(f, '.')) dotAmbiguous++; else dot++;
                    }
                }
                if (comma > dot || (comma == dot && commaAmbiguous > dotAmbiguous)) dec = ',';
            }

            // Header: the first row has a text field in a column that is numeric in the rest of the sample.
            bool header = true;
            if (rows.Count > 1)
            {
                var first = rows[0];
                bool anyNumericColumn = false, textOverNumber = false;
                for (int col = 0; col < first.Count; col++)
                {
                    var later = rows.Skip(1).Where(r => col < r.Count).Select(r => r[col].Trim()).Where(v => v.Length > 0).ToList();
                    if (later.Count == 0 || !later.All(v => LooksNumeric(v, dec))) continue;
                    anyNumericColumn = true;
                    if (!LooksNumeric(first[col].Trim(), dec)) textOverNumber = true;
                }
                header = !anyNumericColumn || textOverNumber;
                if (anyNumericColumn && !textOverNumber) header = false;
            }
            return new CsvDialect(best, dec, header);
        }

        private static IReadOnlyList<string> SafeParse(string line, char delimiter)
        {
            try { return CsvLine.Parse(line, delimiter); }
            catch (FormatException) { return new[] { line }; }
        }

        // "1,000" or "-12.345": one separator followed by exactly three digits, which a thousands separator also produces.
        private static bool LooksLikeThousands(string field, char sep)
        {
            int at = field.IndexOf(sep);
            if (at < 1 || at != field.LastIndexOf(sep) || field.Length - at - 1 != 3) return false;
            int start = field[0] == '-' || field[0] == '+' ? 1 : 0;
            if (at - start < 1 || at - start > 3) return false;
            for (int i = start; i < field.Length; i++)
                if (i != at && !char.IsDigit(field[i])) return false;
            return true;
        }

        private static bool LooksNumeric(string field, char dec)
        {
            if (field.Length == 0) return false;
            if (dec != '.' && field.IndexOf('.') >= 0) return false;
            string s = dec == '.' ? field : field.Replace(dec, '.');
            return TryParseDouble(s, out _);
        }

        // double.TryParse, but a number past double's range is infinity (or zero) on every runtime: .NET Framework
        // rejected "1e400", so ParseNumber threw there and Detect did not count it as numeric, where .NET Core gave
        // infinity. TryParse fails on well-formed text only out of range, so the magnitude of the digits decides it.
        private static bool TryParseDouble(string text, out double value)
        {
            if (TryFloatWord(text, out value)) return true;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
#if NETSTANDARD2_0
                if (TryNearestBinary(text, 53, out double nearest)) value = nearest;
#endif
                return true;
            }
            string s = text.Trim(' ', '\t', '\n', '\v', '\f', '\r');   // NumberStyles' white space, not Trim()'s
            int i = 0;
            bool negative = i < s.Length && s[i] == '-';
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            long magnitude = 0;          // the decimal exponent of the first nonzero digit, plus one
            bool digits = false, nonzero = false, point = false;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9')
                {
                    digits = true;
                    if (c != '0' && !nonzero) nonzero = true;
                    if (!point && nonzero) magnitude++;
                    else if (point && !nonzero) magnitude--;
                }
                else if (c == '.' && !point) point = true;
                else break;
            }
            if (!digits) return false;
            if (i < s.Length)
            {
                if (s[i] != 'e' && s[i] != 'E') return false;
                i++;
                bool negativeExponent = i < s.Length && s[i] == '-';
                if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
                if (i == s.Length) return false;
                long exponent = 0;
                for (; i < s.Length; i++)
                {
                    if (s[i] < '0' || s[i] > '9') return false;
                    if (exponent < 1000000000) exponent = exponent * 10 + (s[i] - '0');
                }
                magnitude += negativeExponent ? -exponent : exponent;
            }
            value = nonzero && magnitude > 0 ? double.PositiveInfinity : 0;
            if (negative) value = -value;
#if NETSTANDARD2_0
            if (TryNearestBinary(s, 53, out double near)) value = near;   // a number .NET Framework refused near the range's ends
#endif
            return true;
        }

        // NaN, Infinity or \u221E in any case, after an optional sign, on every runtime: .NET 8 took "nan", "-NaN" and
        // "infinity", .NET Framework only "NaN", "Infinity" and "-Infinity", so such a field bound on one and failed on the other.
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
    }
}
