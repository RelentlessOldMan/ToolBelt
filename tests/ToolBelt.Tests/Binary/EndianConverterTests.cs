using System;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class EndianConverterTests
    {
        public void ReadKnownBytes()
        {
            byte[] b = { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 };
            Check.Equal((ushort)0x1234, EndianConverter.ReadUInt16BigEndian(b, 0));
            Check.Equal((ushort)0x3412, EndianConverter.ReadUInt16LittleEndian(b, 0));
            Check.Equal(0x12345678u, EndianConverter.ReadUInt32BigEndian(b, 0));
            Check.Equal(0x78563412u, EndianConverter.ReadUInt32LittleEndian(b, 0));
            Check.Equal(0x123456789ABCDEF0uL, EndianConverter.ReadUInt64BigEndian(b, 0));
            Check.Equal(0xF0DEBC9A78563412uL, EndianConverter.ReadUInt64LittleEndian(b, 0));
        }

        public void OffsetIsHonored()
        {
            byte[] b = { 0x00, 0x00, 0xAB, 0xCD };
            Check.Equal((ushort)0xABCD, EndianConverter.ReadUInt16BigEndian(b, 2));
        }

        public void OutOfRange_Throws()
        {
            byte[] b = new byte[3];
            Check.Throws<ArgumentOutOfRangeException>(() => EndianConverter.ReadUInt32BigEndian(b, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => EndianConverter.ReadUInt16BigEndian(b, 2));
            Check.Throws<ArgumentNullException>(() => EndianConverter.ReadUInt16BigEndian(null!, 0));
        }

        // Property: write-then-read round-trips for every size and both byte orders.
        public void RoundTrip_AllSizesAndOrders()
        {
            var rng = new Random(0xE);
            var buf = new byte[8];
            for (int i = 0; i < 5000; i++)
            {
                ushort u16 = (ushort)rng.Next(0, 65536);
                EndianConverter.WriteUInt16BigEndian(buf, 0, u16);
                Check.Equal(u16, EndianConverter.ReadUInt16BigEndian(buf, 0));
                EndianConverter.WriteUInt16LittleEndian(buf, 0, u16);
                Check.Equal(u16, EndianConverter.ReadUInt16LittleEndian(buf, 0));

                uint u32 = (uint)rng.Next() ^ ((uint)rng.Next() << 1);
                EndianConverter.WriteUInt32BigEndian(buf, 0, u32);
                Check.Equal(u32, EndianConverter.ReadUInt32BigEndian(buf, 0));
                EndianConverter.WriteUInt32LittleEndian(buf, 0, u32);
                Check.Equal(u32, EndianConverter.ReadUInt32LittleEndian(buf, 0));

                ulong u64 = ((ulong)(uint)rng.Next() << 32) | (uint)rng.Next();
                EndianConverter.WriteUInt64BigEndian(buf, 0, u64);
                Check.Equal(u64, EndianConverter.ReadUInt64BigEndian(buf, 0));
                EndianConverter.WriteUInt64LittleEndian(buf, 0, u64);
                Check.Equal(u64, EndianConverter.ReadUInt64LittleEndian(buf, 0));
            }
        }

        // Cross-check: big-endian and little-endian reads of the same bytes are byte-reversals.
        public void BigAndLittle_AreByteReversed()
        {
            var rng = new Random(0xF);
            var buf = new byte[4];
            for (int i = 0; i < 1000; i++)
            {
                rng.NextBytes(buf);
                uint be = EndianConverter.ReadUInt32BigEndian(buf, 0);
                uint le = EndianConverter.ReadUInt32LittleEndian(buf, 0);
                uint reversed = ((be & 0xFF) << 24) | ((be & 0xFF00) << 8) | ((be >> 8) & 0xFF00) | (be >> 24);
                Check.Equal(reversed, le, $"i={i}");
            }
        }
    }
}
