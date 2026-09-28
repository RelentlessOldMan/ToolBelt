// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Binary
{
    /// <summary>
    /// LEB128 variable-length integer encoding (as used by Protocol Buffers, DWARF, WebAssembly).
    /// Unsigned values use base-128 with a continuation bit; signed values are ZigZag-mapped first so
    /// small-magnitude negatives stay short. Decoding validates termination and rejects over-long input.
    /// </summary>
    public static class VarInt
    {
        /// <summary>Encodes an unsigned value as LEB128 (1–10 bytes).</summary>
        public static byte[] EncodeUnsigned(ulong value)
        {
            var bytes = new List<byte>(10);
            do
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value != 0)
                    b |= 0x80; // more bytes follow
                bytes.Add(b);
            }
            while (value != 0);
            return bytes.ToArray();
        }

        /// <summary>Decodes an unsigned LEB128 value starting at index 0, reporting how many bytes it consumed.</summary>
        public static ulong DecodeUnsigned(byte[] data, out int bytesRead)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            ulong result = 0;
            int shift = 0;
            int i = 0;
            while (true)
            {
                if (i >= data.Length)
                    throw new FormatException("Truncated LEB128: ran out of bytes before the terminator.");
                byte b = data[i++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
                if (shift >= 64)
                    throw new FormatException("LEB128 value is too long for a 64-bit integer.");
            }
            bytesRead = i;
            return result;
        }

        /// <summary>Encodes a signed value using ZigZag mapping followed by LEB128.</summary>
        public static byte[] EncodeSigned(long value)
        {
            ulong zigzag = (ulong)((value << 1) ^ (value >> 63));
            return EncodeUnsigned(zigzag);
        }

        /// <summary>Decodes a ZigZag+LEB128 signed value, reporting how many bytes it consumed.</summary>
        public static long DecodeSigned(byte[] data, out int bytesRead)
        {
            ulong zigzag = DecodeUnsigned(data, out bytesRead);
            return (long)(zigzag >> 1) ^ -(long)(zigzag & 1);
        }
    }
}
