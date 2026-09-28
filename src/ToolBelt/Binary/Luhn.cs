// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Binary
{
    /// <summary>
    /// The Luhn (mod-10) checksum used to validate card numbers, IMEIs, and similar identifiers. Works on
    /// strings of ASCII digits. Non-digit characters cause <see cref="FormatException"/>.
    /// </summary>
    public static class Luhn
    {
        /// <summary>Whether <paramref name="digits"/> (payload plus trailing check digit) satisfies the Luhn check.</summary>
        public static bool IsValid(string digits)
        {
            if (digits is null) throw new ArgumentNullException(nameof(digits));
            if (digits.Length == 0) return false;

            int sum = 0;
            bool doubleIt = false; // rightmost digit (the check digit) is not doubled
            for (int i = digits.Length - 1; i >= 0; i--)
            {
                int d = ToDigit(digits[i]);
                if (doubleIt)
                {
                    d *= 2;
                    if (d > 9) d -= 9;
                }
                sum += d;
                doubleIt = !doubleIt;
            }
            return sum % 10 == 0;
        }

        /// <summary>The check digit (0-9) that should be appended to <paramref name="payload"/>.</summary>
        public static int ComputeCheckDigit(string payload)
        {
            if (payload is null) throw new ArgumentNullException(nameof(payload));

            int sum = 0;
            bool doubleIt = true; // the appended check digit will sit at the non-doubled position
            for (int i = payload.Length - 1; i >= 0; i--)
            {
                int d = ToDigit(payload[i]);
                if (doubleIt)
                {
                    d *= 2;
                    if (d > 9) d -= 9;
                }
                sum += d;
                doubleIt = !doubleIt;
            }
            return (10 - sum % 10) % 10;
        }

        /// <summary>Appends the computed check digit to <paramref name="payload"/>, yielding a Luhn-valid string.</summary>
        public static string AppendCheckDigit(string payload)
        {
            int check = ComputeCheckDigit(payload);
            return payload + (char)('0' + check);
        }

        private static int ToDigit(char c)
        {
            if (c < '0' || c > '9')
                throw new FormatException($"'{c}' is not a digit.");
            return c - '0';
        }
    }
}
