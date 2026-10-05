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
    /// (quote-aware), the decimal separator is inferred from numeric-looking fields, and a header is assumed when the first
    /// row is text where later rows are numbers. <see cref="ParseNumber"/> then reads values the dialect's way.
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

            // Delimiter: most lines sharing the modal field count (>1 field), then more fields, then candidate order.
            char best = ',';
            (int Agree, int Fields) bestScore = (-1, 0);
            foreach (char c in Candidates)
            {
                var counts = lines.Select(l => SafeCount(l, c)).ToList();
                var modal = counts.Where(n => n > 1).GroupBy(n => n).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).FirstOrDefault();
                if (modal == null) continue;
                var score = (modal.Count(), modal.Key);
                if (score.Item1 > bestScore.Agree || (score.Item1 == bestScore.Agree && score.Item2 > bestScore.Fields)) { best = c; bestScore = score; }
            }

            var rows = lines.Select(l => SafeParse(l, best)).ToList();
            var body = rows.Skip(1).SelectMany(r => r).Select(f => f.Trim()).ToList();
            var all = rows.SelectMany(r => r).Select(f => f.Trim()).ToList();

            // Decimal separator: a comma can only be decimal when it isn't the delimiter.
            char dec = '.';
            if (best != ',')
            {
                int commaDecimals = all.Count(f => LooksNumeric(f, ','));
                int dotDecimals = all.Count(f => LooksNumeric(f, '.') && f.IndexOf('.') >= 0);
                if (commaDecimals > dotDecimals && all.Any(f => f.IndexOf(',') >= 0 && LooksNumeric(f, ','))) dec = ',';
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

        private static int SafeCount(string line, char delimiter) => SafeParse(line, delimiter).Count;

        private static IReadOnlyList<string> SafeParse(string line, char delimiter)
        {
            try { return CsvLine.Parse(line, delimiter); }
            catch (FormatException) { return new[] { line }; }
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
