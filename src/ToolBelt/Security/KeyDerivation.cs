// ToolBelt drop-in — also copy Security/CryptoRandom.cs.
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
        public static byte[] GenerateSalt(int length = 16)
            => length < 0 ? throw new ArgumentOutOfRangeException(nameof(length), length, "Length must not be negative.")
                          : CryptoRandom.Bytes(length);

        /// <summary>Derives a key from a password, encoded as UTF-8. A lone surrogate throws <see cref="ArgumentException"/>
        /// (the lenient encoding would make it the same password as U+FFFD).</summary>
        public static byte[] Pbkdf2(string password, byte[] salt, int iterations, int keyLength, KdfHash hash = KdfHash.Sha256)
            => Pbkdf2(Utf8(password), salt, iterations, keyLength, hash);


        public static byte[] Pbkdf2(byte[] password, byte[] salt, int iterations, int keyLength, KdfHash hash = KdfHash.Sha256)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));
            if (salt is null) throw new ArgumentNullException(nameof(salt));
            if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Iterations must be positive.");
            if (keyLength < 1) throw new ArgumentOutOfRangeException(nameof(keyLength), keyLength, "Key length must be positive.");
            // The largest byte array the runtime allows; past it the block arithmetic below would overflow.
            if (keyLength > 0x7FFFFFC7) throw new ArgumentOutOfRangeException(nameof(keyLength), keyLength, "Key length is larger than an array can hold.");
            if (hash != KdfHash.Sha256 && hash != KdfHash.Sha512)
                throw new ArgumentOutOfRangeException(nameof(hash), hash, "Unknown KdfHash value.");

            using HMAC prf = hash == KdfHash.Sha512 ? new HMACSHA512(password) : (HMAC)new HMACSHA256(password);
            int hashLen = prf.HashSize / 8;
            int blockCount = (int)(((long)keyLength + hashLen - 1) / hashLen);
            var derived = new byte[keyLength];

            var saltPlusIndex = new byte[salt.Length + 4];
            Buffer.BlockCopy(salt, 0, saltPlusIndex, 0, salt.Length);

            for (int block = 1; block <= blockCount; block++)
            {
                // INT32 big-endian block index appended to the salt for U1.
                unchecked
                {
                    saltPlusIndex[salt.Length] = (byte)(block >> 24);
                    saltPlusIndex[salt.Length + 1] = (byte)(block >> 16);
                    saltPlusIndex[salt.Length + 2] = (byte)(block >> 8);
                    saltPlusIndex[salt.Length + 3] = (byte)block;
                }

                byte[] u = prf.ComputeHash(saltPlusIndex);
                var t = (byte[])u.Clone();
                for (int iter = 1; iter < iterations; iter++)
                {
                    u = prf.ComputeHash(u);
                    for (int k = 0; k < t.Length; k++) t[k] ^= u[k];
                }
                int offset = (block - 1) * hashLen;
                Buffer.BlockCopy(t, 0, derived, offset, Math.Min(hashLen, keyLength - offset));
            }
            return derived;
        }

        // Strict UTF-8: the default encoder turns a lone surrogate into U+FFFD, so different strings would give the
        // same bytes.
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static byte[] Utf8(string password)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));
            try { return StrictUtf8.GetBytes(password); }
            catch (EncoderFallbackException ex) { throw new ArgumentException("The password contains an unpaired surrogate.", nameof(password), ex); }
        }
    }
}
