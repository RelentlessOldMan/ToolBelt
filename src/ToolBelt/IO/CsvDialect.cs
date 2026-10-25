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
            if (delimiter == decimalSeparator) throw new ArgumentException("The delimiter and the decimal separator must differ.");
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
            string s = value.ToString("R", CultureInfo.InvariantCulture);
            // "R" can lose the last bit on .NET Framework; G17 always round-trips.
            if (!double.IsNaN(value) && double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture) != value)
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
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }
    }
}
