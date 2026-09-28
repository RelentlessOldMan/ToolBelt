// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Binary
{
    /// <summary>
    /// CRC-16/CCITT-FALSE checksum (polynomial 0x1021, initial value 0xFFFF, no reflection, no final
    /// XOR) — the variant used by XMODEM-style protocols and many embedded devices. Provides a one-shot
    /// <see cref="Compute(byte[])"/> and an incremental <see cref="Crc16Hasher"/>; both agree.
    /// </summary>
    public static class Crc16
    {
        internal const ushort Polynomial = 0x1021;
        internal const ushort InitialValue = 0xFFFF;

        /// <summary>Computes the CRC-16/CCITT-FALSE of the whole array.</summary>
        public static ushort Compute(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            var hasher = new Crc16Hasher();
            hasher.Append(data, 0, data.Length);
            return hasher.Value;
        }
    }

    /// <summary>Incremental CRC-16/CCITT-FALSE. Feed bytes with <see cref="Append(byte[])"/>, then read <see cref="Value"/>.</summary>
    public sealed class Crc16Hasher
    {
        private ushort _crc = Crc16.InitialValue;

        public void Append(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            Append(data, 0, data.Length);
        }

        public void Append(byte[] data, int offset, int count)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count), "The segment lies outside the array.");

            ushort crc = _crc;
            for (int i = offset; i < offset + count; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ Crc16.Polynomial) : (ushort)(crc << 1);
            }
            _crc = crc;
        }

        /// <summary>The CRC-16 of everything appended so far.</summary>
        public ushort Value => _crc;

        /// <summary>Resets to the initial state so the instance can be reused.</summary>
        public void Reset() => _crc = Crc16.InitialValue;
    }
}
