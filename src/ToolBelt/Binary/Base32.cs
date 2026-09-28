// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Binary
{
    /// <summary>
    /// RFC 4648 Base32 encoding and decoding (the standard <c>A–Z 2–7</c> alphabet). Encoding pads to a
    /// multiple of eight characters with <c>'='</c> by default. Decoding is case-insensitive, tolerates
    /// padding and interior whitespace, and rejects any other character.
    /// </summary>
    public static class Base32
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        // Reverse lookup: char -> 5-bit value, or -1 if not in the alphabet. Covers both cases lazily-free
        // by folding case at decode time.
        private static readonly sbyte[] Reverse = BuildReverse();

        /// <summary>Encodes bytes to a Base32 string. When <paramref name="padding"/> is false, trailing '=' is omitted.</summary>
        public static string Encode(byte[] data, bool padding = true)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length == 0)
                return string.Empty;

            var sb = new StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = 0;
            int bits = 0;

            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    sb.Append(Alphabet[(buffer >> bits) & 0x1F]);
                }
            }

            if (bits > 0)
            {
                // Left-align the remaining bits and pad the low bits with zero.
                sb.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
            }

            if (padding)
            {
                while (sb.Length % 8 != 0)
                    sb.Append('=');
            }

            return sb.ToString();
        }

        /// <summary>Decodes a Base32 string. Throws <see cref="FormatException"/> on an out-of-alphabet character.</summary>
        public static byte[] Decode(string text)
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));

            var bytes = new List<byte>(text.Length * 5 / 8 + 1);
            int buffer = 0;
            int bits = 0;

            foreach (char c in text)
            {
                if (c == '=')
                    break; // padding: only ever trails, so stop
                if (char.IsWhiteSpace(c))
                    continue;

                int value = c < Reverse.Length ? Reverse[c] : -1;
                if (value < 0)
                    throw new FormatException($"Invalid Base32 character '{c}'.");

                buffer = (buffer << 5) | value;
                bits += 5;
                if (bits >= 8)
                {
                    bits -= 8;
                    bytes.Add((byte)((buffer >> bits) & 0xFF));
                }
            }

            return bytes.ToArray();
        }

        private static sbyte[] BuildReverse()
        {
            var table = new sbyte[128];
            for (int i = 0; i < table.Length; i++)
                table[i] = -1;
            for (int i = 0; i < Alphabet.Length; i++)
            {
                char upper = Alphabet[i];
                table[upper] = (sbyte)i;
                table[char.ToLowerInvariant(upper)] = (sbyte)i;
            }
            return table;
        }
    }
}
