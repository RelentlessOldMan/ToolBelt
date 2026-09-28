// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Text;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Converts non-negative integers to and from positional (radix) string representations, either in a
    /// standard base 2..36 (digits 0-9a-z, case-insensitive on decode) or against a caller-supplied
    /// alphabet. This is integer radix conversion — distinct from the byte-oriented encoders in
    /// <c>ToolBelt.Binary</c> (Base32/Base58/Base64).
    /// </summary>
    public static class BaseConverter
    {
        private const string StandardDigits = "0123456789abcdefghijklmnopqrstuvwxyz";

        /// <summary>Formats <paramref name="value"/> in the given <paramref name="radix"/> (2..36).</summary>
        public static string ToBase(long value, int radix)
        {
            if (radix < 2 || radix > 36)
                throw new ArgumentOutOfRangeException(nameof(radix), radix, "Radix must be between 2 and 36.");
            return ToBase(value, StandardDigits.Substring(0, radix));
        }

        /// <summary>Formats <paramref name="value"/> using an explicit digit alphabet (length is the base).</summary>
        public static string ToBase(long value, string alphabet)
        {
            ValidateAlphabet(alphabet);
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be non-negative.");
            if (value == 0) return alphabet[0].ToString();

            int radix = alphabet.Length;
            var sb = new StringBuilder();
            while (value > 0)
            {
                sb.Append(alphabet[(int)(value % radix)]);
                value /= radix;
            }
            // digits were produced least-significant-first; reverse in place
            for (int i = 0, j = sb.Length - 1; i < j; i++, j--)
            {
                char tmp = sb[i];
                sb[i] = sb[j];
                sb[j] = tmp;
            }
            return sb.ToString();
        }

        /// <summary>Parses a string in the given <paramref name="radix"/> (2..36), case-insensitively.</summary>
        public static long FromBase(string text, int radix)
        {
            if (radix < 2 || radix > 36)
                throw new ArgumentOutOfRangeException(nameof(radix), radix, "Radix must be between 2 and 36.");
            return FromBase(text is null ? null! : text.ToLowerInvariant(), StandardDigits.Substring(0, radix));
        }

        /// <summary>Parses a string against an explicit digit alphabet (case-sensitive).</summary>
        public static long FromBase(string text, string alphabet)
        {
            ValidateAlphabet(alphabet);
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (text.Length == 0) throw new FormatException("Value must not be empty.");

            int radix = alphabet.Length;
            long result = 0;
            foreach (char c in text)
            {
                int digit = alphabet.IndexOf(c);
                if (digit < 0)
                    throw new FormatException($"'{c}' is not a valid digit for base {radix}.");
                checked { result = result * radix + digit; }
            }
            return result;
        }

        private static void ValidateAlphabet(string alphabet)
        {
            if (alphabet is null) throw new ArgumentNullException(nameof(alphabet));
            if (alphabet.Length < 2) throw new ArgumentException("Alphabet must have at least 2 digits.", nameof(alphabet));
        }
    }
}
