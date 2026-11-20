// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Numerics;

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

        /// <summary>
        /// Formats a byte count, e.g. 1536 → "1.50 KiB" (binary) or "1.54 KB" (decimal), with 0 to 99 decimals. The value is
        /// rounded from the exact quotient, half away from zero, the same on every runtime.
        /// </summary>
        public static string Format(long bytes, bool binary = true, int decimals = 2)
        {
            // .NET Framework reads "F100" as a custom format: 1536 with 100 decimals gave "F102 KiB".
            if (decimals < 0 || decimals > 99)
                throw new ArgumentOutOfRangeException(nameof(decimals), decimals, "Decimals must be from 0 to 99.");

            string sign = bytes < 0 ? "-" : "";
            ulong magnitude = bytes < 0 ? (ulong)(-(bytes + 1)) + 1 : (ulong)bytes;   // long.MinValue too
            ulong @base = binary ? 1024UL : 1000UL;
            string[] units = binary ? BinaryUnits : DecimalUnits;

            int unit = 0;
            ulong divisor = 1;
            while (unit < units.Length - 1 && magnitude / divisor >= @base)
            {
                divisor *= @base;
                unit++;
            }
            if (unit == 0)
                return sign + magnitude.ToString(CultureInfo.InvariantCulture) + " " + units[0]; // whole bytes, no decimals

            // In integers, not double: dividing a double by 1000 per unit put exact midpoints on either side (1.125 EB came
            // out "1.12" on .NET 8 and "1.13" on .NET Framework, whose "F" also rounds through 15 digits), and long.MaxValue
            // with 15 decimals printed 9.223372036854773 EiB for …776.
            BigInteger scale = BigInteger.Pow(10, decimals);
            BigInteger rounded = Scaled(magnitude, divisor, scale);
            // 1048575 B is 1023.999 KiB, which rounds to "1024.00 KiB": carry into the next unit instead.
            if (unit < units.Length - 1 && rounded >= scale * @base)
            {
                divisor *= @base;
                unit++;
                rounded = Scaled(magnitude, divisor, scale);
            }
            string digits = rounded.ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
            string text = decimals == 0 ? digits : digits.Substring(0, digits.Length - decimals) + "." + digits.Substring(digits.Length - decimals);
            return sign + text + " " + units[unit];
        }

        // magnitude / divisor in units of 1/scale, rounded half up.
        private static BigInteger Scaled(ulong magnitude, ulong divisor, BigInteger scale)
        {
            BigInteger q = BigInteger.DivRem(magnitude * scale, divisor, out BigInteger r);
            return 2 * r >= divisor ? q + 1 : q;
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

            // "[sign]digits[.digits]", at least one digit, the commas already checked.
            int at = numberPart.Length > 0 && (numberPart[0] == '+' || numberPart[0] == '-') ? 1 : 0;
            bool negative = at == 1 && numberPart[0] == '-';
            string body = numberPart.Substring(at).Replace(",", "");
            int point = body.IndexOf('.');
            string whole = point < 0 ? body : body.Substring(0, point);
            string fraction = point < 0 ? "" : body.Substring(point + 1);
            if (whole.Length + fraction.Length == 0 || fraction.IndexOf('.') >= 0)
                return false;

            decimal multiplier = UnitMultiplier(unitPart);
            if (multiplier < 0)
                return false;

            // Exactly, in integers: through a double, byte counts past 2^53 lost their low digits; through decimal, which
            // rounds to 28 or 29 digits, "0.49999999999999999999999999999999" became 0.5 and then 1 byte.
            BigInteger numerator = BigInteger.Parse("0" + whole + fraction, NumberStyles.None, CultureInfo.InvariantCulture) * new BigInteger(multiplier);
            BigInteger denominator = BigInteger.Pow(10, fraction.Length);
            BigInteger scaled = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
            if (2 * remainder >= denominator)
                scaled += 1;                                    // half away from zero
            if (scaled > (negative ? BigInteger.One << 63 : long.MaxValue))
                return false;
            bytes = negative ? (long)-scaled : (long)scaled;
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
        private static decimal UnitMultiplier(string unit)
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

            decimal multiplier = 1;
            for (int k = 0; k <= exponent; k++) multiplier *= binary ? 1024m : 1000m;
            return multiplier;
        }
    }
}
