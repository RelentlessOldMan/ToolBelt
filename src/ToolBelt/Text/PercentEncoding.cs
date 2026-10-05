// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// RFC 3986 percent-encoding over UTF-8: every byte except the unreserved set (A–Z a–z 0–9 - . _ ~) and any extra
    /// <c>safe</c> characters becomes %XX (upper-case hex). Decoding is strict — a malformed escape or invalid UTF-8 is a
    /// <see cref="FormatException"/>, never silently corrupted text — with a <see cref="TryDecode"/> companion. The
    /// <c>form</c> variants follow application/x-www-form-urlencoded (space ↔ '+').
    /// </summary>
    public static class PercentEncoding
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

        /// <summary>Encodes <paramref name="text"/>; characters in <paramref name="safe"/> (e.g. "/") are left as they are.</summary>
        public static string Encode(string text, string? safe = null, bool spaceAsPlus = false)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            byte[] bytes = StrictUtf8.GetBytes(text);
            var sb = new StringBuilder(bytes.Length);
            foreach (byte b in bytes)
            {
                char c = (char)b;
                if (b < 0x80 && (IsUnreserved(c) || (safe != null && safe.IndexOf(c) >= 0))) sb.Append(c);
                else if (spaceAsPlus && b == (byte)' ') sb.Append('+');
                else sb.Append('%').Append(Hex[b >> 4]).Append(Hex[b & 15]);
            }
            return sb.ToString();
        }

        /// <summary>Form encoding (space as '+').</summary>
        public static string EncodeForm(string text) => Encode(text, null, spaceAsPlus: true);

        /// <summary>Decodes %XX escapes (either hex case); with <paramref name="plusAsSpace"/>, '+' becomes a space.</summary>
        public static string Decode(string text, bool plusAsSpace = false)
        {
            if (TryDecodeCore(text ?? throw new ArgumentNullException(nameof(text)), plusAsSpace, out string? result, out string? error)) return result!;
            throw new FormatException(error);
        }

        public static string DecodeForm(string text) => Decode(text, plusAsSpace: true);

        /// <summary>Decodes, returning false (and null) instead of throwing on malformed input.</summary>
        public static bool TryDecode(string text, out string? result, bool plusAsSpace = false)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return TryDecodeCore(text, plusAsSpace, out result, out _);
        }

        private static bool TryDecodeCore(string text, bool plusAsSpace, out string? result, out string? error)
        {
            result = null;
            error = null;
            var bytes = new System.Collections.Generic.List<byte>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '%')
                {
                    if (i + 2 >= text.Length) { error = $"Incomplete escape at position {i}."; return false; }
                    int hi = HexValue(text[i + 1]), lo = HexValue(text[i + 2]);
                    if (hi < 0 || lo < 0) { error = $"Invalid escape '%{text[i + 1]}{text[i + 2]}' at position {i}."; return false; }
                    bytes.Add((byte)(hi * 16 + lo));
                    i += 2;
                }
                else if (plusAsSpace && c == '+') bytes.Add((byte)' ');
                else if (c < 0x80) bytes.Add((byte)c);
                else bytes.AddRange(StrictUtf8.GetBytes(c.ToString()));
            }
            try { result = StrictUtf8.GetString(bytes.ToArray()); return true; }
            catch (DecoderFallbackException) { error = "The decoded bytes are not valid UTF-8."; return false; }
        }

        private const string Hex = "0123456789ABCDEF";

        private static bool IsUnreserved(char c)
            => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '.' || c == '_' || c == '~';

        private static int HexValue(char c)
            => c >= '0' && c <= '9' ? c - '0' : (c >= 'a' && c <= 'f' ? c - 'a' + 10 : (c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1));
    }
}
