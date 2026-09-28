// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Binary
{
    /// <summary>
    /// Crockford's Base32 encoding — a human-friendly base-32 alphabet
    /// (<c>0-9 A-Z</c> excluding I, L, O, U) designed to survive transcription. Encoding is uppercase
    /// with no padding; decoding is case-insensitive, treats <c>I</c>/<c>L</c> as <c>1</c> and <c>O</c>
    /// as <c>0</c>, and ignores hyphens used as visual separators.
    /// </summary>
    public static class CrockfordBase32
    {
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        private static readonly sbyte[] Reverse = BuildReverse();

        /// <summary>Encodes bytes to an unpadded Crockford Base32 string.</summary>
        public static string Encode(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0) return string.Empty;

            var sb = new StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = 0, bits = 0;
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
                sb.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
            return sb.ToString();
        }

        /// <summary>Decodes a Crockford Base32 string. Throws <see cref="FormatException"/> on an invalid symbol.</summary>
        public static byte[] Decode(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));

            var bytes = new List<byte>(text.Length * 5 / 8 + 1);
            int buffer = 0, bits = 0;
            foreach (char raw in text)
            {
                if (raw == '-')
                    continue; // visual separator

                char c = Normalize(raw);
                int value = c < Reverse.Length ? Reverse[c] : -1;
                if (value < 0)
                    throw new FormatException($"Invalid Crockford Base32 character '{raw}'.");

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

        private static char Normalize(char c)
        {
            c = char.ToUpperInvariant(c);
            return c switch
            {
                'O' => '0',
                'I' => '1',
                'L' => '1',
                _ => c,
            };
        }

        private static sbyte[] BuildReverse()
        {
            var table = new sbyte[128];
            for (int i = 0; i < table.Length; i++)
                table[i] = -1;
            for (int i = 0; i < Alphabet.Length; i++)
                table[Alphabet[i]] = (sbyte)i;
            return table;
        }
    }
}
