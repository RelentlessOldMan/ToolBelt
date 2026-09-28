// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Security.Cryptography;
using System.Text;

namespace ToolBelt.Security
{
    /// <summary>
    /// Keyed-hash message authentication (HMAC) over the SHA-2 family, with a constant-time
    /// <see cref="Verify"/>. Authentication is what makes a message tamper-evident; verifying a tag with an
    /// ordinary equality check would leak it byte-by-byte via timing, so <see cref="Verify"/> uses
    /// <see cref="ConstantTime"/>.
    /// </summary>
    public static class Hmac
    {
        public static byte[] Sha256(byte[] key, byte[] data) => Compute(new HMACSHA256(RequireKey(key)), data);
        public static byte[] Sha256(byte[] key, string text) => Sha256(key, Utf8(text));

        public static byte[] Sha384(byte[] key, byte[] data) => Compute(new HMACSHA384(RequireKey(key)), data);
        public static byte[] Sha512(byte[] key, byte[] data) => Compute(new HMACSHA512(RequireKey(key)), data);

        /// <summary>Recomputes the HMAC-SHA-256 and compares it to <paramref name="expectedMac"/> in constant time.</summary>
        public static bool Verify(byte[] key, byte[] data, byte[] expectedMac)
        {
            if (expectedMac is null) throw new ArgumentNullException(nameof(expectedMac));
            return ConstantTime.Equals(Sha256(key, data), expectedMac);
        }

        private static byte[] Compute(HMAC hmac, byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            using (hmac) return hmac.ComputeHash(data);
        }

        private static byte[] RequireKey(byte[] key) => key ?? throw new ArgumentNullException(nameof(key));

        private static byte[] Utf8(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Encoding.UTF8.GetBytes(text);
        }
    }
}
