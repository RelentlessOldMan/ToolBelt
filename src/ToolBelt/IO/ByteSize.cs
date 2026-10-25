// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;

namespace ToolBelt.IO
{
    /// <summary>
    /// Formats byte counts as human-readable strings and parses them back. Supports both binary units
    /// (base 1024: KiB, MiB, …) and decimal units (base 1000: KB, MB, …). Parsing is lenient: units are
    /// case-insensitive, the space is optional, and "KB"/"K" are decimal while "KiB" is binary.
    /// </summary>
    public static class ByteSize
    {
        private static readonly string[] BinaryUnits = { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
        private static readonly string[] DecimalUnits = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };

        /// <summary>Formats a byte count, e.g. 1536 → "1.50 KiB" (binary) or "1.54 KB" (decimal).</summary>
        public static string Format(long bytes, bool binary = true, int decimals = 2)
        {
            if (decimals < 0)
                throw new ArgumentOutOfRangeException(nameof(decimals), decimals, "Decimals must not be negative.");

            string sign = bytes < 0 ? "-" : "";
            // Use double magnitude; long.MinValue negates safely through double.
            double magnitude = Math.Abs((double)bytes);
            double @base = binary ? 1024d : 1000d;
            string[] units = binary ? BinaryUnits : DecimalUnits;

            int unit = 0;
            double value = magnitude;
            while (value >= @base && unit < units.Length - 1)
            {
                value /= @base;
                unit++;
            }
            // 1048575 B is 1023.999 KiB, which rounds to "1024.00 KiB": carry into the next unit instead.
            if (unit > 0 && unit < units.Length - 1 && Math.Round(value, Math.Min(decimals, 15)) >= @base)
            {
                value /= @base;
                unit++;
            }

            if (unit == 0)
                return $"{sign}{(long)magnitude} {units[0]}"; // whole bytes, no decimals
            return sign + value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
                   + " " + units[unit];
        }

        /// <summary>Parses a human-readable size. Throws <see cref="FormatException"/> on invalid input.</summary>
        public static long Parse(string text)
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));
            if (!TryParse(text, out long bytes))
                throw new FormatException($"'{text}' is not a valid byte size.");
            return bytes;
        }

        /// <summary>
        /// Non-throwing parse. A bare number is treated as bytes. The decimal point is '.'; a ',' is accepted only as a
        /// thousands separator in whole groups of three ("1,000 KB"), so a decimal comma ("1,5 KB") is rejected rather
        /// than read as 15.
        /// </summary>
        public static bool TryParse(string text, out long bytes)
        {
            bytes = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();
            int i = 0;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
                i++;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == ','))
                i++;

            string numberPart = text.Substring(0, i).Trim();
            string unitPart = text.Substring(i).Trim();
            if (numberPart.IndexOf(',') >= 0 && !ThousandsGrouped(numberPart))
                return false;

            if (!double.TryParse(numberPart, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out double number))
                return false;

            double multiplier = UnitMultiplier(unitPart);
            if (multiplier < 0)
                return false;

            double scaled = Math.Round(number * multiplier, MidpointRounding.AwayFromZero);
            // An out-of-range double -> long conversion is unchecked in C# and yields a garbage value
            // (often long.MinValue). Reject instead of returning a wrong result. 2^63 is the first double
            // above long.MaxValue; -2^63 == long.MinValue exactly and is allowed.
            const double twoPow63 = 9223372036854775808.0;
            if (double.IsNaN(scaled) || scaled >= twoPow63 || scaled < -twoPow63)
                return false;
            bytes = (long)scaled;
            return true;
        }

        // "[sign]d{1,3}(,ddd)+[.digits]": commas only between whole groups of three before the point.
        private static bool ThousandsGrouped(string number)
        {
            int i = number.Length > 0 && (number[0] == '+' || number[0] == '-') ? 1 : 0;
            int point = number.IndexOf('.');
            string whole = point < 0 ? number.Substring(i) : number.Substring(i, point - i);
            if (point >= 0 && number.IndexOf(',', point) >= 0)
                return false;
            string[] groups = whole.Split(',');
            if (groups[0].Length < 1 || groups[0].Length > 3)
                return false;
            for (int g = 1; g < groups.Length; g++)
                if (groups[g].Length != 3)
                    return false;
            return true;
        }

        // Returns the byte multiplier for a unit suffix, or -1 if unrecognized.
        private static double UnitMultiplier(string unit)
        {
            unit = unit.Trim().ToUpperInvariant();
            if (unit.Length == 0 || unit == "B")
                return 1;

            if (unit.EndsWith("B", StringComparison.Ordinal))
                unit = unit.Substring(0, unit.Length - 1);

            bool binary = false;
            if (unit.EndsWith("I", StringComparison.Ordinal))
            {
                binary = true;
                unit = unit.Substring(0, unit.Length - 1);
            }

            if (unit.Length != 1)
                return -1;

            int exponent = "KMGTPE".IndexOf(unit[0]);
            if (exponent < 0)
                return -1;

            return Math.Pow(binary ? 1024d : 1000d, exponent + 1);
        }
    }
}
