using System;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class Crc16Tests
    {
        public void KnownVector()
        {
            // CRC-16/CCITT-FALSE check value for "123456789" is 0x29B1.
            Check.Equal((ushort)0x29B1, Crc16.Compute(Encoding.ASCII.GetBytes("123456789")));
        }

        public void EmptyInput_IsInitialValue()
        {
            Check.Equal((ushort)0xFFFF, Crc16.Compute(Array.Empty<byte>()));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Crc16.Compute(null!));
        }

        // Streaming property: chunked appends equal the one-shot computation.
        public void Streaming_EqualsOneShot()
        {
            var rng = new Random(16);
            for (int trial = 0; trial < 500; trial++)
            {
                var data = new byte[rng.Next(0, 100)];
                rng.NextBytes(data);

                var hasher = new Crc16Hasher();
                int pos = 0;
                while (pos < data.Length)
                {
                    int chunk = Math.Min(data.Length - pos, rng.Next(1, 8));
                    hasher.Append(data, pos, chunk);
                    pos += chunk;
                }
                Check.Equal(Crc16.Compute(data), hasher.Value, $"trial {trial}");
            }
        }

        public void Reset_ReusesInstance()
        {
            var hasher = new Crc16Hasher();
            hasher.Append(Encoding.ASCII.GetBytes("123456789"));
            Check.Equal((ushort)0x29B1, hasher.Value);
            hasher.Reset();
            Check.Equal((ushort)0xFFFF, hasher.Value);
        }
    }
}
