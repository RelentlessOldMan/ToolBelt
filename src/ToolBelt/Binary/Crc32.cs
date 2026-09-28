// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Binary
{
    /// <summary>
    /// CRC-32 as used by zlib / PNG / Ethernet (reflected polynomial <c>0xEDB88320</c>, initial and
    /// final XOR of <c>0xFFFFFFFF</c>). Provides a one-shot <see cref="Compute(byte[])"/> and an
    /// incremental <see cref="Crc32Hasher"/> for streaming; both produce identical results.
    /// </summary>
    public static class Crc32
    {
        internal static readonly uint[] Table = BuildTable();

        /// <summary>Computes the CRC-32 of the whole array.</summary>
        public static uint Compute(byte[] data)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            return Compute(data, 0, data.Length);
        }

        /// <summary>Computes the CRC-32 of a segment.</summary>
        public static uint Compute(byte[] data, int offset, int count)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count), "The segment lies outside the array.");

            var hasher = new Crc32Hasher();
            hasher.Append(data, offset, count);
            return hasher.Value;
        }

        private static uint[] BuildTable()
        {
            const uint polynomial = 0xEDB88320u;
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ polynomial : crc >> 1;
                table[i] = crc;
            }
            return table;
        }
    }

    /// <summary>Incremental CRC-32. Feed bytes with <see cref="Append(byte[])"/>, then read <see cref="Value"/>.</summary>
    public sealed class Crc32Hasher
    {
        private uint _crc = 0xFFFFFFFFu;

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

            uint crc = _crc;
            var table = Crc32.Table;
            for (int i = offset; i < offset + count; i++)
                crc = (crc >> 8) ^ table[(crc ^ data[i]) & 0xFF];
            _crc = crc;
        }

        /// <summary>The CRC-32 of everything appended so far (with the final XOR applied).</summary>
        public uint Value => _crc ^ 0xFFFFFFFFu;

        /// <summary>Resets to the initial state so the instance can be reused.</summary>
        public void Reset() => _crc = 0xFFFFFFFFu;
    }
}
