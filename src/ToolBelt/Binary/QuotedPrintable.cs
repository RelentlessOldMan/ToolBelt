// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Binary
{
    /// <summary>
    /// Quoted-printable encoding (RFC 2045 §6.7): the "mostly readable" transfer encoding used by email and
    /// MIME. Printable ASCII passes through unchanged; everything else — plus <c>=</c> itself, and any space
    /// or tab that would fall at the end of a line — becomes an <c>=XX</c> hex escape. Output is wrapped with
    /// soft line breaks (<c>=\r\n</c>) so no line exceeds 76 characters. Encoding is byte-exact and
    /// round-trips: <c>Decode(Encode(bytes))</c> returns the original bytes.
    /// </summary>
    public static class QuotedPrintable
    {
        private const string Hex = "0123456789ABCDEF";

        // Leave headroom under the 76-char RFC limit: a line holds at most this many content characters before
        // a soft break, which guarantees room for a trailing-whitespace =XX expansion (+2) and the soft '='
        // without ever exceeding 76.
        private const int MaxLineContent = 73;

        /// <summary>Encodes bytes as a quoted-printable string with soft line wrapping.</summary>
        public static string Encode(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            var output = new StringBuilder();
            var line = new StringBuilder();

            foreach (byte b in data)
            {
                string token = Represent(b);
                if (line.Length + token.Length > MaxLineContent)
                {
                    EncodeTrailingWhitespace(line);
                    output.Append(line).Append("=\r\n");
                    line.Clear();
                }
                line.Append(token);
            }

            EncodeTrailingWhitespace(line);
            output.Append(line);
            return output.ToString();
        }

        /// <summary>Decodes a quoted-printable string back to the original bytes. Throws on a malformed escape.</summary>
        public static byte[] Decode(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));

            var bytes = new List<byte>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '=')
                {
                    // Soft line break: '=' immediately followed by a line ending is a continuation — drop it.
                    if (i + 1 < text.Length && text[i + 1] == '\n') { i += 1; continue; }
                    if (i + 1 < text.Length && text[i + 1] == '\r')
                    {
                        if (i + 2 < text.Length && text[i + 2] == '\n') { i += 2; continue; } // =\r\n
                        i += 1; continue;                                                      // =\r (lenient)
                    }
                    // Otherwise an =XX hex escape.
                    if (i + 2 >= text.Length)
                        throw new FormatException("Truncated '=' escape in quoted-printable input.");
                    int hi = HexValue(text[i + 1]);
                    int lo = HexValue(text[i + 2]);
                    if (hi < 0 || lo < 0)
                        throw new FormatException($"Invalid '=' escape '={text[i + 1]}{text[i + 2]}' in quoted-printable input.");
                    bytes.Add((byte)((hi << 4) | lo));
                    i += 2;
                }
                else
                {
                    bytes.Add((byte)c);
                }
            }
            return bytes.ToArray();
        }

        // The quoted-printable token for one byte: a literal char where allowed, otherwise =XX.
        private static string Represent(byte b)
        {
            // Printable ASCII (33..126) except '=' passes through; space and tab pass through here and are
            // fixed up only if they land at end-of-line (EncodeTrailingWhitespace).
            if ((b >= 33 && b <= 126 && b != (byte)'=') || b == (byte)' ' || b == (byte)'\t')
                return ((char)b).ToString();
            return "=" + Hex[b >> 4] + Hex[b & 0xF];
        }

        // A space or tab at the very end of a line would be stripped by mail transports, so RFC 2045 requires
        // it be escaped. Only the final character can be at end-of-line here.
        private static void EncodeTrailingWhitespace(StringBuilder line)
        {
            if (line.Length == 0) return;
            char last = line[line.Length - 1];
            if (last == ' ' || last == '\t')
            {
                byte b = (byte)last;
                line.Length -= 1;
                line.Append('=').Append(Hex[b >> 4]).Append(Hex[b & 0xF]);
            }
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10; // lenient on decode
            return -1;
        }
    }
}
