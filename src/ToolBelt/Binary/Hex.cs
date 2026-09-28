// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Binary
{
    /// <summary>
    /// Hexadecimal encoding and decoding of byte arrays. Encoding is lowercase by default; decoding is
    /// case-insensitive and strict — the input length must be even and every character a hex digit,
    /// otherwise <see cref="FormatException"/> is thrown (no whitespace or separators are tolerated).
    /// </summary>
    public static class Hex
    {
        private const string LowerDigits = "0123456789abcdef";
        private const string UpperDigits = "0123456789ABCDEF";

        /// <summary>Encodes bytes as a hex string (lowercase unless <paramref name="upperCase"/> is set).</summary>
        public static string Encode(byte[] data, bool upperCase = false)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));

            string digits = upperCase ? UpperDigits : LowerDigits;
            var chars = new char[data.Length * 2];
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                chars[2 * i] = digits[b >> 4];
                chars[2 * i + 1] = digits[b & 0xF];
            }
            return new string(chars);
        }

        /// <summary>Decodes a hex string. Throws <see cref="FormatException"/> on odd length or a non-hex character.</summary>
        public static byte[] Decode(string hex)
        {
            if (hex is null)
                throw new ArgumentNullException(nameof(hex));
            if ((hex.Length & 1) != 0)
                throw new FormatException("Hex input must have an even number of characters.");

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int hi = FromNibble(hex[2 * i]);
                int lo = FromNibble(hex[2 * i + 1]);
                bytes[i] = (byte)((hi << 4) | lo);
            }
            return bytes;
        }

        private static int FromNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            throw new FormatException($"Invalid hex character '{c}'.");
        }
    }
}
