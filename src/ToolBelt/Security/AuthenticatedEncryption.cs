// ToolBelt drop-in — fully self-contained (BCL only).
// Authenticated encryption wraps the framework's AES-GCM primitive, which is unavailable on
// netstandard2.0; the whole file compiles out there (same precedent as the net8.0-only JSON helpers).
#if !NETSTANDARD2_0
using System;
using System.Security.Cryptography;
using System.Text;

namespace ToolBelt.Security
{
    /// <summary>
    /// Authenticated encryption with associated data (AEAD) over the framework's audited AES-GCM. Every
    /// operation both encrypts and authenticates: decryption fails loudly (a <see cref="CryptographicException"/>)
    /// if the ciphertext, nonce, tag, or associated data has been altered, so you never act on forged plaintext.
    /// <para>
    /// The convenience <see cref="Encrypt(byte[], byte[], byte[])"/>/<see cref="Decrypt(byte[], byte[], byte[])"/>
    /// pair generates a fresh random 96-bit nonce per message and returns a single self-describing blob laid out
    /// as <c>nonce ‖ ciphertext ‖ tag</c> — just store or transmit the bytes. A nonce must NEVER repeat under the
    /// same key, which is why this generates one for you; the detached overloads take an explicit nonce only for
    /// interop and known-answer testing. Keys are raw AES keys of 16, 24, or 32 bytes (AES-128/192/256) — derive
    /// them from a password with <see cref="KeyDerivation"/>, not by hashing a passphrase directly.
    /// </para>
    /// </summary>
    public static class AuthenticatedEncryption
    {
        /// <summary>The nonce (IV) size AES-GCM requires, in bytes (96 bits).</summary>
        public const int NonceSize = 12;

        /// <summary>The authentication tag size this type produces, in bytes (128 bits, the maximum).</summary>
        public const int TagSize = 16;

        // ---------- combined self-describing blob: nonce ‖ ciphertext ‖ tag ----------

        /// <summary>
        /// Encrypts <paramref name="plaintext"/> under <paramref name="key"/> with a fresh random nonce and
        /// returns <c>nonce ‖ ciphertext ‖ tag</c>. Pass the same <paramref name="associatedData"/> (additional
        /// data that is authenticated but not encrypted, e.g. a header or version tag) to <see cref="Decrypt"/>.
        /// </summary>
        public static byte[] Encrypt(byte[] key, byte[] plaintext, byte[]? associatedData = null)
        {
            if (plaintext is null) throw new ArgumentNullException(nameof(plaintext));
            ValidateKey(key);

            var message = new byte[NonceSize + plaintext.Length + TagSize];
            FillNonce(message); // first NonceSize bytes

            var nonce = new ReadOnlySpan<byte>(message, 0, NonceSize);
            var ciphertext = new Span<byte>(message, NonceSize, plaintext.Length);
            var tag = new Span<byte>(message, NonceSize + plaintext.Length, TagSize);

            using var gcm = new AesGcm(key, TagSize);
            gcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            return message;
        }

        /// <summary>Encrypts a UTF-8 string. See <see cref="Encrypt(byte[], byte[], byte[])"/>.</summary>
        public static byte[] Encrypt(byte[] key, string plaintext, byte[]? associatedData = null)
        {
            if (plaintext is null) throw new ArgumentNullException(nameof(plaintext));
            return Encrypt(key, Encoding.UTF8.GetBytes(plaintext), associatedData);
        }

        /// <summary>
        /// Verifies and decrypts a blob produced by <see cref="Encrypt(byte[], byte[], byte[])"/>. Throws
        /// <see cref="CryptographicException"/> if authentication fails (wrong key, tampered bytes, or mismatched
        /// <paramref name="associatedData"/>); on success returns the original plaintext bytes.
        /// </summary>
        public static byte[] Decrypt(byte[] key, byte[] message, byte[]? associatedData = null)
        {
            if (message is null) throw new ArgumentNullException(nameof(message));
            ValidateKey(key);
            if (message.Length < NonceSize + TagSize)
                throw new ArgumentException("Message is too short to contain a nonce and tag.", nameof(message));

            int plainLength = message.Length - NonceSize - TagSize;
            var nonce = new ReadOnlySpan<byte>(message, 0, NonceSize);
            var ciphertext = new ReadOnlySpan<byte>(message, NonceSize, plainLength);
            var tag = new ReadOnlySpan<byte>(message, NonceSize + plainLength, TagSize);
            var plaintext = new byte[plainLength];

            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }

        /// <summary>Verifies, decrypts, and decodes a UTF-8 string. See <see cref="Decrypt"/>.</summary>
        public static string DecryptToString(byte[] key, byte[] message, byte[]? associatedData = null)
            => Encoding.UTF8.GetString(Decrypt(key, message, associatedData));

        // ---------- detached form (explicit nonce, separate tag) for interop and known-answer tests ----------

        /// <summary>
        /// Encrypts with a caller-supplied nonce, writing the ciphertext and the authentication tag to separate
        /// arrays. The nonce MUST be <see cref="NonceSize"/> bytes and MUST be unique per key. Prefer
        /// <see cref="Encrypt(byte[], byte[], byte[])"/> unless you need the wire format of another AES-GCM
        /// implementation.
        /// </summary>
        public static void EncryptDetached(byte[] key, byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag, byte[]? associatedData = null)
        {
            ValidateKey(key);
            ValidateNonce(nonce);
            if (plaintext is null) throw new ArgumentNullException(nameof(plaintext));
            if (ciphertext is null) throw new ArgumentNullException(nameof(ciphertext));
            if (tag is null) throw new ArgumentNullException(nameof(tag));
            if (ciphertext.Length != plaintext.Length)
                throw new ArgumentException("Ciphertext buffer must be the same length as the plaintext.", nameof(ciphertext));
            ValidateTagLength(tag.Length, nameof(tag));

            using var gcm = new AesGcm(key, tag.Length);
            gcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }

        /// <summary>
        /// Verifies and decrypts a detached ciphertext/tag pair with a caller-supplied nonce, writing the
        /// plaintext to <paramref name="plaintext"/>. Throws <see cref="CryptographicException"/> if the tag does
        /// not authenticate.
        /// </summary>
        public static void DecryptDetached(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag, byte[] plaintext, byte[]? associatedData = null)
        {
            ValidateKey(key);
            ValidateNonce(nonce);
            if (ciphertext is null) throw new ArgumentNullException(nameof(ciphertext));
            if (tag is null) throw new ArgumentNullException(nameof(tag));
            if (plaintext is null) throw new ArgumentNullException(nameof(plaintext));
            if (plaintext.Length != ciphertext.Length)
                throw new ArgumentException("Plaintext buffer must be the same length as the ciphertext.", nameof(plaintext));
            ValidateTagLength(tag.Length, nameof(tag));

            using var gcm = new AesGcm(key, tag.Length);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        }

        // ---------- helpers ----------

        /// <summary>Generates a fresh random nonce suitable for the detached overloads.</summary>
        public static byte[] GenerateNonce() => CryptoRandom.Bytes(NonceSize);

        private static void FillNonce(byte[] destination)
        {
            using var rng = RandomNumberGenerator.Create();
            var nonce = new byte[NonceSize];
            rng.GetBytes(nonce);
            Buffer.BlockCopy(nonce, 0, destination, 0, NonceSize);
        }

        private static void ValidateKey(byte[] key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (key.Length != 16 && key.Length != 24 && key.Length != 32)
                throw new ArgumentException("Key must be 16, 24, or 32 bytes (AES-128/192/256).", nameof(key));
        }

        private static void ValidateNonce(byte[] nonce)
        {
            if (nonce is null) throw new ArgumentNullException(nameof(nonce));
            if (nonce.Length != NonceSize)
                throw new ArgumentException($"Nonce must be exactly {NonceSize} bytes.", nameof(nonce));
        }

        private static void ValidateTagLength(int length, string paramName)
        {
            // AES-GCM admits 12–16 byte tags; shorter tags weaken authentication, so we never produce them.
            if (length < 12 || length > 16)
                throw new ArgumentException("Tag must be 12 to 16 bytes.", paramName);
        }
    }
}
#endif
