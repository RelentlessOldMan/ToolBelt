using System;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class Adler32Tests
    {
        public void KnownVector()
        {
            // Adler-32 of ASCII "Wikipedia" is 0x11E60398.
            Check.Equal(0x11E60398u, Adler32.Compute(Encoding.ASCII.GetBytes("Wikipedia")));
        }

        public void EmptyInput_IsOne()
        {
            Check.Equal(1u, Adler32.Compute(Array.Empty<byte>()));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Adler32.Compute(null!));
        }

        // Differential: the block-deferred modulo implementation must equal a mod-every-byte reference,
        // including across the NMax (5552-byte) block boundary.
        public void Differential_MatchesPerByteReference()
        {
            var rng = new Random(0xAD1E);
            foreach (int size in new[] { 0, 1, 55, 5551, 5552, 5553, 12000 })
            {
                var data = new byte[size];
                rng.NextBytes(data);
                Check.Equal(PerByte(data), Adler32.Compute(data), $"size {size}");
            }

            for (int trial = 0; trial < 1000; trial++)
            {
                var data = new byte[rng.Next(0, 200)];
                rng.NextBytes(data);
                Check.Equal(PerByte(data), Adler32.Compute(data), $"trial {trial}");
            }
        }

        // Streaming property: chunked appends equal the one-shot computation.
        public void Streaming_EqualsOneShot()
        {
            var rng = new Random(77);
            for (int trial = 0; trial < 500; trial++)
            {
                var data = new byte[rng.Next(0, 100)];
                rng.NextBytes(data);

                var hasher = new Adler32Hasher();
                int pos = 0;
                while (pos < data.Length)
                {
                    int chunk = Math.Min(data.Length - pos, rng.Next(1, 8));
                    hasher.Append(data, pos, chunk);
                    pos += chunk;
                }
                Check.Equal(Adler32.Compute(data), hasher.Value, $"trial {trial}");
            }
        }

        public void Reset_ReusesInstance()
        {
            var hasher = new Adler32Hasher();
            hasher.Append(Encoding.ASCII.GetBytes("Wikipedia"));
            Check.Equal(0x11E60398u, hasher.Value);
            hasher.Reset();
            Check.Equal(1u, hasher.Value);
        }

        private static uint PerByte(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }
    }
}
