// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Binary
{
    /// <summary>
    /// Base64url encoding (RFC 4648 §5): the URL- and filename-safe alphabet where <c>+</c> becomes
    /// <c>-</c> and <c>/</c> becomes <c>_</c>, with padding <c>=</c> omitted on output. Decoding accepts
    /// input with or without padding. This is the encoding used by JWT and many web APIs.
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

        /// <summary>Decodes a base64url string (padding optional). Throws <see cref="FormatException"/> on invalid input.</summary>
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
                    _ => c,
                };
            }

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
    }
}
