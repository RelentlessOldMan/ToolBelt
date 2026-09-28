// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Security.Cryptography;
using System.Text;

namespace ToolBelt.Security
{
    /// <summary>The PRF hash used by PBKDF2.</summary>
    public enum KdfHash
    {
        Sha256,
        Sha512,
    }

    /// <summary>
    /// Password-based key derivation (PBKDF2, RFC 8018) over HMAC-SHA-256/512, implemented directly over the
    /// framework HMAC primitives so the chosen hash behaves identically on every target (the older target's
    /// built-in <c>Rfc2898DeriveBytes</c> only supports SHA-1). Use a high iteration count and a random salt
    /// from <see cref="GenerateSalt"/>. For storing login passwords, prefer the opinionated
    /// <c>PasswordHasher</c> which encodes the parameters and detects outdated ones.
    /// </summary>
    public static class KeyDerivation
    {
        /// <summary>Generates a cryptographically random salt.</summary>
        public static byte[] GenerateSalt(int length = 16) => CryptoRandom.Bytes(length);

        public static byte[] Pbkdf2(string password, byte[] salt, int iterations, int keyLength, KdfHash hash = KdfHash.Sha256)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));
            return Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, keyLength, hash);
        }

        public static byte[] Pbkdf2(byte[] password, byte[] salt, int iterations, int keyLength, KdfHash hash = KdfHash.Sha256)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));
            if (salt is null) throw new ArgumentNullException(nameof(salt));
            if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Iterations must be positive.");
            if (keyLength < 1) throw new ArgumentOutOfRangeException(nameof(keyLength), keyLength, "Key length must be positive.");

            using HMAC prf = hash == KdfHash.Sha512 ? new HMACSHA512(password) : (HMAC)new HMACSHA256(password);
            int hashLen = prf.HashSize / 8;
            int blockCount = (keyLength + hashLen - 1) / hashLen;
            var derived = new byte[blockCount * hashLen];

            var saltPlusIndex = new byte[salt.Length + 4];
            Buffer.BlockCopy(salt, 0, saltPlusIndex, 0, salt.Length);

            for (int block = 1; block <= blockCount; block++)
            {
                // INT32 big-endian block index appended to the salt for U1.
                saltPlusIndex[salt.Length] = (byte)(block >> 24);
                saltPlusIndex[salt.Length + 1] = (byte)(block >> 16);
                saltPlusIndex[salt.Length + 2] = (byte)(block >> 8);
                saltPlusIndex[salt.Length + 3] = (byte)block;

                byte[] u = prf.ComputeHash(saltPlusIndex);
                var t = (byte[])u.Clone();
                for (int iter = 1; iter < iterations; iter++)
                {
                    u = prf.ComputeHash(u);
                    for (int k = 0; k < t.Length; k++) t[k] ^= u[k];
                }
                Buffer.BlockCopy(t, 0, derived, (block - 1) * hashLen, hashLen);
            }

            if (derived.Length == keyLength) return derived;
            var result = new byte[keyLength];
            Buffer.BlockCopy(derived, 0, result, 0, keyLength);
            return result;
        }
    }
}
