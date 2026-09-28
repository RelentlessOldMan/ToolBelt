// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// Converts between integers in [1, 3999] and standard (subtractive) Roman numerals. Values outside
    /// that range have no standard representation and throw.
    /// </summary>
    public static class RomanNumerals
    {
        private static readonly (int Value, string Symbol)[] Table =
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"),  (90, "XC"),  (50, "L"),  (40, "XL"),
            (10, "X"),   (9, "IX"),   (5, "V"),   (4, "IV"),
            (1, "I"),
        };

        public static string ToRoman(int value)
        {
            if (value < 1 || value > 3999)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Roman numerals cover 1..3999.");

            var sb = new StringBuilder();
            foreach (var (v, symbol) in Table)
            {
                while (value >= v)
                {
                    sb.Append(symbol);
                    value -= v;
                }
            }
            return sb.ToString();
        }

        public static int FromRoman(string roman)
        {
            if (roman is null) throw new ArgumentNullException(nameof(roman));
            if (roman.Length == 0) throw new FormatException("Roman numeral must not be empty.");

            string s = roman.ToUpperInvariant();
            int total = 0, prev = 0;
            for (int i = s.Length - 1; i >= 0; i--)
            {
                int cur = SymbolValue(s[i]);
                if (cur < prev) total -= cur;   // subtractive (e.g. IV, IX)
                else { total += cur; prev = cur; }
            }

            // Canonical round-trip guards against malformed inputs like "IIII" or "IC".
            if (total < 1 || total > 3999 || ToRoman(total) != s)
                throw new FormatException($"'{roman}' is not a valid Roman numeral.");
            return total;
        }

        public static bool TryFromRoman(string roman, out int value)
        {
            try { value = FromRoman(roman); return true; }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentNullException)
            {
                value = 0;
                return false;
            }
        }

        private static int SymbolValue(char c)
        {
            switch (c)
            {
                case 'I': return 1;
                case 'V': return 5;
                case 'X': return 10;
                case 'L': return 50;
                case 'C': return 100;
                case 'D': return 500;
                case 'M': return 1000;
                default: throw new FormatException($"'{c}' is not a Roman numeral symbol.");
            }
        }
    }
}
