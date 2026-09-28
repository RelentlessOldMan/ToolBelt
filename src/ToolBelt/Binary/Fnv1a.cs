// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Text;

namespace ToolBelt.Binary
{
    /// <summary>
    /// The FNV-1a non-cryptographic hash (Fowler–Noll–Vo), in 32- and 64-bit widths. Fast and simple,
    /// suitable for hash tables and checksums — not for security. String overloads hash the UTF-8 bytes,
    /// so <c>Hash32(s)</c> equals <c>Hash32(Encoding.UTF8.GetBytes(s))</c>.
    /// </summary>
    public static class Fnv1a
    {
        private const uint Offset32 = 2166136261;
        private const uint Prime32 = 16777619;
        private const ulong Offset64 = 14695981039346656037;
        private const ulong Prime64 = 1099511628211;

        public static uint Hash32(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            uint hash = Offset32;
            foreach (byte b in data)
            {
                hash ^= b;
                hash *= Prime32;
            }
            return hash;
        }

        public static ulong Hash64(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            ulong hash = Offset64;
            foreach (byte b in data)
            {
                hash ^= b;
                hash *= Prime64;
            }
            return hash;
        }

        /// <summary>Hashes the UTF-8 bytes of <paramref name="text"/>.</summary>
        public static uint Hash32(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Hash32(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>Hashes the UTF-8 bytes of <paramref name="text"/>.</summary>
        public static ulong Hash64(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Hash64(Encoding.UTF8.GetBytes(text));
        }
    }
}
