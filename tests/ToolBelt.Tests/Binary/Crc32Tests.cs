using System;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class Crc32Tests
    {
        public void KnownVector_CheckString()
        {
            // The canonical CRC-32 check value for the ASCII string "123456789".
            uint crc = Crc32.Compute(Encoding.ASCII.GetBytes("123456789"));
            Check.Equal(0xCBF43926u, crc);
        }

        public void EmptyInput_IsZero()
        {
            Check.Equal(0u, Crc32.Compute(Array.Empty<byte>()));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Crc32.Compute(null!));
        }

        public void Segment_OutOfRange_Throws()
        {
            var data = new byte[4];
            Check.Throws<ArgumentOutOfRangeException>(() => Crc32.Compute(data, 2, 5));
        }

        // Differential: the table-based implementation must equal a dead-simple bitwise reference over
        // random buffers.
        public void Differential_MatchesBitwiseReference()
        {
            var rng = new Random(0xC0FFEE);
            for (int trial = 0; trial < 2000; trial++)
            {
                var data = new byte[rng.Next(0, 64)];
                rng.NextBytes(data);
                Check.Equal(Bitwise(data), Crc32.Compute(data), $"trial {trial} (len {data.Length})");
            }
        }

        // Streaming property: appending in arbitrary chunks equals hashing the whole buffer at once.
        public void Streaming_EqualsOneShot()
        {
            var rng = new Random(555);
            for (int trial = 0; trial < 500; trial++)
            {
                var data = new byte[rng.Next(0, 100)];
                rng.NextBytes(data);

                var hasher = new Crc32Hasher();
                int pos = 0;
                while (pos < data.Length)
                {
                    int chunk = Math.Min(data.Length - pos, rng.Next(1, 8));
                    hasher.Append(data, pos, chunk);
                    pos += chunk;
                }

                Check.Equal(Crc32.Compute(data), hasher.Value, $"trial {trial}");
            }
        }

        public void Reset_ReusesInstance()
        {
            var hasher = new Crc32Hasher();
            hasher.Append(Encoding.ASCII.GetBytes("123456789"));
            Check.Equal(0xCBF43926u, hasher.Value);
            hasher.Reset();
            Check.Equal(0u, hasher.Value); // back to empty-input result
        }

        // Textbook bit-at-a-time CRC-32, obviously correct.
        private static uint Bitwise(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
