// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Binary
{
    /// <summary>
    /// Adler-32 checksum (as used by zlib). Faster than CRC-32 but weaker. Provides a one-shot
    /// <see cref="Compute(byte[])"/> that defers the modulo across blocks for speed, plus an incremental
    /// <see cref="Adler32Hasher"/>; both agree bit-for-bit.
    /// </summary>
    public static class Adler32
    {
        private const uint ModAdler = 65521; // largest prime below 2^16
        private const int NMax = 5552;        // max bytes before the sums could overflow a uint

        /// <summary>Computes the Adler-32 of the whole array.</summary>
        public static uint Compute(byte[] data)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));

            uint a = 1, b = 0;
            int i = 0;
            int n = data.Length;
            while (i < n)
            {
                int block = Math.Min(NMax, n - i);
                for (int k = 0; k < block; k++)
                {
                    a += data[i++];
                    b += a;
                }
                a %= ModAdler;
                b %= ModAdler;
            }
            return (b << 16) | a;
        }
    }

    /// <summary>Incremental Adler-32. Feed bytes with <see cref="Append(byte[])"/>, then read <see cref="Value"/>.</summary>
    public sealed class Adler32Hasher
    {
        private const uint ModAdler = 65521;
        private uint _a = 1;
        private uint _b;

        public void Append(byte[] data)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            Append(data, 0, data.Length);
        }

        public void Append(byte[] data, int offset, int count)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count), "The segment lies outside the array.");

            for (int i = offset; i < offset + count; i++)
            {
                _a = (_a + data[i]) % ModAdler;
                _b = (_b + _a) % ModAdler;
            }
        }

        public uint Value => (_b << 16) | _a;

        public void Reset()
        {
            _a = 1;
            _b = 0;
        }
    }
}
