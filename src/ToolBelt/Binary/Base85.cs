// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Binary
{
    /// <summary>
    /// Ascii85 (btoa-style) binary-to-text encoding: each 4-byte group becomes 5 printable characters in
    /// the range '!'..'u'. A short final group is padded and emitted as <c>bytes+1</c> characters, so the
    /// original length is recovered exactly. No <c>z</c> shorthand or <c>&lt;~ ~&gt;</c> delimiters.
    /// </summary>
    public static class Base85
    {
        public static string Encode(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0) return string.Empty;

            var sb = new StringBuilder(data.Length * 5 / 4 + 5);
            int i = 0;
            while (i < data.Length)
            {
                int n = Math.Min(4, data.Length - i);
                uint value = 0;
                for (int j = 0; j < 4; j++)
                    value = (value << 8) | (j < n ? data[i + j] : (uint)0);

                var digits = new char[5];
                uint temp = value;
                for (int k = 4; k >= 0; k--)
                {
                    digits[k] = (char)('!' + temp % 85);
                    temp /= 85;
                }
                sb.Append(digits, 0, n + 1); // full group -> 5 chars; partial -> n+1
                i += 4;
            }
            return sb.ToString();
        }

        public static byte[] Decode(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (text.Length == 0) return Array.Empty<byte>();

            var bytes = new List<byte>(text.Length * 4 / 5 + 4);
            int i = 0;
            while (i < text.Length)
            {
                int n = Math.Min(5, text.Length - i);
                if (n == 1)
                    throw new FormatException("A trailing Ascii85 group must have at least two characters.");

                ulong value = 0;
                for (int j = 0; j < 5; j++)
                {
                    int digit = j < n ? text[i + j] - '!' : 84; // pad short groups with 'u'
                    if (digit < 0 || digit > 84)
                        throw new FormatException($"Invalid Ascii85 character at index {i + j}.");
                    value = value * 85 + (uint)digit;
                }
                if (value > uint.MaxValue)
                    throw new FormatException("Ascii85 group overflows 32 bits.");

                uint v = (uint)value;
                for (int k = 0; k < n - 1; k++) // full group -> 4 bytes; partial -> n-1
                    bytes.Add((byte)(v >> (24 - k * 8)));
                i += 5;
            }
            return bytes.ToArray();
        }
    }
}
