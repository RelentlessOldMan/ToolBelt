// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Text;

namespace ToolBelt.IO
{
    /// <summary>
    /// Formats a byte buffer as a classic hex dump: an 8-digit hex offset, the bytes in lowercase hex,
    /// and an ASCII gutter where non-printable bytes render as '.'. Each line covers
    /// <c>bytesPerLine</c> bytes (default 16); the byte column is padded so gutters align on short final
    /// lines. Empty input yields an empty string.
    /// </summary>
    public static class HexDump
    {
        private const string HexDigits = "0123456789abcdef";

        public static string Format(byte[] data, int bytesPerLine = 16)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (bytesPerLine <= 0) throw new ArgumentOutOfRangeException(nameof(bytesPerLine), bytesPerLine, "Bytes per line must be positive.");
            if (data.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            for (int offset = 0; offset < data.Length; offset += bytesPerLine)
            {
                if (offset > 0)
                    sb.Append('\n');

                AppendOffset(sb, offset);
                sb.Append("  ");

                int lineLen = Math.Min(bytesPerLine, data.Length - offset);
                for (int j = 0; j < bytesPerLine; j++)
                {
                    if (j < lineLen)
                    {
                        byte b = data[offset + j];
                        sb.Append(HexDigits[b >> 4]);
                        sb.Append(HexDigits[b & 0xF]);
                        sb.Append(' ');
                    }
                    else
                    {
                        sb.Append("   "); // keep the ASCII gutter aligned on a short final line
                    }
                }

                sb.Append('|');
                for (int j = 0; j < lineLen; j++)
                {
                    byte b = data[offset + j];
                    sb.Append(b >= 0x20 && b <= 0x7E ? (char)b : '.');
                }
                sb.Append('|');
            }

            return sb.ToString();
        }

        private static void AppendOffset(StringBuilder sb, int offset)
        {
            for (int shift = 28; shift >= 0; shift -= 4)
                sb.Append(HexDigits[(offset >> shift) & 0xF]);
        }
    }
}
