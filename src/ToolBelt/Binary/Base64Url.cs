// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Binary
{
    /// <summary>
    /// Base64url encoding (RFC 4648 §5): the URL- and filename-safe alphabet where <c>+</c> becomes
    /// <c>-</c> and <c>/</c> becomes <c>_</c>, with padding <c>=</c> omitted on output. Decoding accepts
    /// only unpadded input in the URL-safe alphabet (no <c>=</c>, <c>+</c>, <c>/</c> or whitespace), as JWT
    /// requires, and rejects non-zero unused bits in the last character (so <c>QR</c> is not a second spelling of
    /// <c>QQ</c>). This is the encoding used by JWT and many web APIs.
    /// </summary>
    public static class Base64Url
    {
        /// <summary>Encodes bytes as an unpadded base64url string.</summary>
        public static string Encode(byte[] data)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));

            string standard = Convert.ToBase64String(data);

            // Trim '=' padding and swap to the URL-safe alphabet in a single pass.
            int length = standard.Length;
            while (length > 0 && standard[length - 1] == '=')
                length--;

            var chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                char c = standard[i];
                chars[i] = c switch
                {
                    '+' => '-',
                    '/' => '_',
                    _ => c,
                };
            }
            return new string(chars);
        }

        /// <summary>Decodes an unpadded base64url string. Throws <see cref="FormatException"/> on invalid input.</summary>
        public static byte[] Decode(string text)
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));

            var chars = new char[text.Length + 3]; // room for up to 2 pad chars, rounded up
            int i = 0;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                chars[i] = c switch
                {
                    '-' => '+',
                    '_' => '/',
                    '=' => throw new FormatException("base64url input must not contain padding."),
                    // Convert.FromBase64CharArray would accept these (standard alphabet, whitespace).
                    _ when (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') => c,
                    _ => throw new FormatException($"Invalid base64url character '{c}' at index {i}."),
                };
            }

            // The last character of a 2- or 3-character group carries 4 or 2 unused bits, which an encoder leaves zero.
            int unused = i % 4 == 2 ? 0xF : i % 4 == 3 ? 0x3 : 0;
            if (unused != 0 && (Sextet(text[i - 1]) & unused) != 0)
                throw new FormatException("Invalid base64url: non-zero bits after the last byte.");

            // Re-pad to a multiple of four.
            switch (i % 4)
            {
                case 0:
                    break;
                case 2:
                    chars[i++] = '=';
                    chars[i++] = '=';
                    break;
                case 3:
                    chars[i++] = '=';
                    break;
                default:
                    throw new FormatException("Invalid base64url length.");
            }

            return Convert.FromBase64CharArray(chars, 0, i);
        }

        private static int Sextet(char c)
            => c >= 'A' && c <= 'Z' ? c - 'A' : c >= 'a' && c <= 'z' ? c - 'a' + 26 : c >= '0' && c <= '9' ? c - '0' + 52 : c == '-' ? 62 : 63;
    }
}
