using System;
using System.Linq;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class VarIntTests
    {
        public void KnownUnsignedVectors()
        {
            Check.True(VarInt.EncodeUnsigned(0).SequenceEqual(new byte[] { 0x00 }));
            Check.True(VarInt.EncodeUnsigned(127).SequenceEqual(new byte[] { 0x7F }));
            Check.True(VarInt.EncodeUnsigned(128).SequenceEqual(new byte[] { 0x80, 0x01 }));
            Check.True(VarInt.EncodeUnsigned(300).SequenceEqual(new byte[] { 0xAC, 0x02 }));
        }

        public void KnownZigZagVectors()
        {
            Check.True(VarInt.EncodeSigned(0).SequenceEqual(new byte[] { 0x00 }));
            Check.True(VarInt.EncodeSigned(-1).SequenceEqual(new byte[] { 0x01 }));
            Check.True(VarInt.EncodeSigned(1).SequenceEqual(new byte[] { 0x02 }));
            Check.True(VarInt.EncodeSigned(-2).SequenceEqual(new byte[] { 0x03 }));
        }

        public void DecodeReportsBytesRead()
        {
            var encoded = VarInt.EncodeUnsigned(300);
            ulong v = VarInt.DecodeUnsigned(encoded, out int read);
            Check.Equal(300UL, v);
            Check.Equal(2, read);
        }

        public void Truncated_Throws()
        {
            Check.Throws<FormatException>(() => VarInt.DecodeUnsigned(new byte[] { 0x80 }, out _)); // continuation with no follow-up
        }

        public void TooLong_Throws()
        {
            var overlong = Enumerable.Repeat((byte)0x80, 11).ToArray(); // never terminates within 64 bits
            Check.Throws<FormatException>(() => VarInt.DecodeUnsigned(overlong, out _));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => VarInt.DecodeUnsigned(null!, out _));
        }

        public void RoundTrip_Unsigned()
        {
            var rng = new Random(128);
            for (int i = 0; i < 5000; i++)
            {
                ulong value = ((ulong)(uint)rng.Next() << 32) | (uint)rng.Next();
                Check.Equal(value, VarInt.DecodeUnsigned(VarInt.EncodeUnsigned(value), out _), $"value {value}");
            }
            Check.Equal(ulong.MaxValue, VarInt.DecodeUnsigned(VarInt.EncodeUnsigned(ulong.MaxValue), out _));
        }

        public void RoundTrip_Signed()
        {
            var rng = new Random(129);
            for (int i = 0; i < 5000; i++)
            {
                long value = ((long)rng.Next() << 32) | (uint)rng.Next();
                Check.Equal(value, VarInt.DecodeSigned(VarInt.EncodeSigned(value), out _), $"value {value}");
            }
            Check.Equal(long.MinValue, VarInt.DecodeSigned(VarInt.EncodeSigned(long.MinValue), out _));
            Check.Equal(long.MaxValue, VarInt.DecodeSigned(VarInt.EncodeSigned(long.MaxValue), out _));
        }
    }
}
