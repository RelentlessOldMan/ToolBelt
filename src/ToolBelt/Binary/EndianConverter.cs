// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Binary
{
    /// <summary>
    /// Reads and writes 16/32/64-bit unsigned integers to a byte buffer in an explicit byte order,
    /// independent of the host machine's endianness (unlike <see cref="BitConverter"/>). Every method
    /// validates that the requested span lies within the buffer.
    /// </summary>
    public static class EndianConverter
    {
        public static ushort ReadUInt16BigEndian(byte[] buffer, int offset)
        {
            CheckRange(buffer, offset, 2);
            return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        }

        public static ushort ReadUInt16LittleEndian(byte[] buffer, int offset)
        {
            CheckRange(buffer, offset, 2);
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        public static void WriteUInt16BigEndian(byte[] buffer, int offset, ushort value)
        {
            CheckRange(buffer, offset, 2);
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)value;
        }

        public static void WriteUInt16LittleEndian(byte[] buffer, int offset, ushort value)
        {
            CheckRange(buffer, offset, 2);
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        public static uint ReadUInt32BigEndian(byte[] buffer, int offset)
        {
            CheckRange(buffer, offset, 4);
            return ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16)
                 | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
        }

        public static uint ReadUInt32LittleEndian(byte[] buffer, int offset)
        {
            CheckRange(buffer, offset, 4);
            return buffer[offset] | ((uint)buffer[offset + 1] << 8)
                 | ((uint)buffer[offset + 2] << 16) | ((uint)buffer[offset + 3] << 24);
        }

        public static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
        {
            CheckRange(buffer, offset, 4);
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        public static void WriteUInt32LittleEndian(byte[] buffer, int offset, uint value)
        {
            CheckRange(buffer, offset, 4);
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        public static ulong ReadUInt64BigEndian(byte[] buffer, int offset)
        {
            CheckRange(buffer, offset, 8);
            ulong hi = ReadUInt32BigEndian(buffer, offset);
            ulong lo = ReadUInt32BigEndian(buffer, offset + 4);
            return (hi << 32) | lo;
        }

        public static ulong ReadUInt64LittleEndian(byte[] buffer, int offset)
        {
            CheckRange(buffer, offset, 8);
            ulong lo = ReadUInt32LittleEndian(buffer, offset);
            ulong hi = ReadUInt32LittleEndian(buffer, offset + 4);
            return (hi << 32) | lo;
        }

        public static void WriteUInt64BigEndian(byte[] buffer, int offset, ulong value)
        {
            CheckRange(buffer, offset, 8);
            WriteUInt32BigEndian(buffer, offset, (uint)(value >> 32));
            WriteUInt32BigEndian(buffer, offset + 4, (uint)value);
        }

        public static void WriteUInt64LittleEndian(byte[] buffer, int offset, ulong value)
        {
            CheckRange(buffer, offset, 8);
            WriteUInt32LittleEndian(buffer, offset, (uint)value);
            WriteUInt32LittleEndian(buffer, offset + 4, (uint)(value >> 32));
        }

        private static void CheckRange(byte[] buffer, int offset, int size)
        {
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset + size > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset), offset,
                    $"Reading/writing {size} bytes at offset {offset} exceeds the buffer length {buffer.Length}.");
        }
    }
}
