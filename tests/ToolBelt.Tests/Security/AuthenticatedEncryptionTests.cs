using System;
using System.Security.Cryptography;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Security
{
    public sealed class AuthenticatedEncryptionTests
    {
        // Canonical AES-GCM test vectors from McGrew & Viega, "The Galois/Counter Mode of Operation (GCM)".

        // Test Case 2: zero key, zero nonce, a single zero plaintext block, no associated data.
        public void GcmKnownVector_NoAssociatedData()
        {
            byte[] key = Hex.Decode("00000000000000000000000000000000");
            byte[] nonce = Hex.Decode("000000000000000000000000");
            byte[] plaintext = Hex.Decode("00000000000000000000000000000000");

            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[AuthenticatedEncryption.TagSize];
            AuthenticatedEncryption.EncryptDetached(key, nonce, plaintext, ciphertext, tag);

            Check.Equal("0388dace60b6a392f328c2b971b2fe78", Hashing.ToHex(ciphertext));
            Check.Equal("ab6e47d42cec13bdf53a67b21257bddf", Hashing.ToHex(tag));
        }

        // Test Case 4: AES-128 with a 60-byte plaintext and 20 bytes of associated data.
        public void GcmKnownVector_WithAssociatedData()
        {
            byte[] key = Hex.Decode("feffe9928665731c6d6a8f9467308308");
            byte[] nonce = Hex.Decode("cafebabefacedbaddecaf888");
            byte[] plaintext = Hex.Decode(
                "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a72" +
                "1c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39");
            byte[] aad = Hex.Decode("feedfacedeadbeeffeedfacedeadbeefabaddad2");

            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[AuthenticatedEncryption.TagSize];
            AuthenticatedEncryption.EncryptDetached(key, nonce, plaintext, ciphertext, tag, aad);

            Check.Equal(
                "42831ec2217774244b7221b784d0d49ce3aa212f2c02a4e035c17e2329aca12e" +
                "21d514b25466931c7d8f6a5aac84aa051ba30b396a0aac973d58e091",
                Hashing.ToHex(ciphertext));
            Check.Equal("5bc94fbc3221a5db94fae95ae7121a47", Hashing.ToHex(tag));

            // The detached decrypt recovers the plaintext given the same nonce, tag, and AAD.
            var recovered = new byte[ciphertext.Length];
            AuthenticatedEncryption.DecryptDetached(key, nonce, ciphertext, tag, recovered, aad);
            Check.Equal(Hashing.ToHex(plaintext), Hashing.ToHex(recovered));
        }

        public void RoundTrip_String()
        {
            byte[] key = CryptoRandom.Bytes(32);
            const string message = "attack at dawn — ☕";
            byte[] sealed_ = AuthenticatedEncryption.Encrypt(key, message);
            Check.Equal(message, AuthenticatedEncryption.DecryptToString(key, sealed_));
        }

        public void RoundTrip_Bytes_AllKeySizes()
        {
            foreach (int keyLen in new[] { 16, 24, 32 })
            {
                byte[] key = CryptoRandom.Bytes(keyLen);
                byte[] plaintext = CryptoRandom.Bytes(37);
                byte[] message = AuthenticatedEncryption.Encrypt(key, plaintext);
                byte[] recovered = AuthenticatedEncryption.Decrypt(key, message);
                Check.Equal(Hashing.ToHex(plaintext), Hashing.ToHex(recovered));
            }
        }

        public void SealedLayout_IsNonceCiphertextTag()
        {
            byte[] key = CryptoRandom.Bytes(16);
            byte[] plaintext = CryptoRandom.Bytes(10);
            byte[] message = AuthenticatedEncryption.Encrypt(key, plaintext);
            Check.Equal(AuthenticatedEncryption.NonceSize + plaintext.Length + AuthenticatedEncryption.TagSize, message.Length);
        }

        public void FreshNoncePerCall_ProducesDifferentBlobs()
        {
            byte[] key = CryptoRandom.Bytes(16);
            byte[] plaintext = Encoding.ASCII.GetBytes("same plaintext");
            byte[] a = AuthenticatedEncryption.Encrypt(key, plaintext);
            byte[] b = AuthenticatedEncryption.Encrypt(key, plaintext);
            Check.False(Hashing.ToHex(a) == Hashing.ToHex(b), "nonce should differ per call");
            // Both still decrypt to the same plaintext.
            Check.Equal("same plaintext", Encoding.ASCII.GetString(AuthenticatedEncryption.Decrypt(key, a)));
            Check.Equal("same plaintext", Encoding.ASCII.GetString(AuthenticatedEncryption.Decrypt(key, b)));
        }

        public void AssociatedData_MustMatchToDecrypt()
        {
            byte[] key = CryptoRandom.Bytes(16);
            byte[] plaintext = Encoding.ASCII.GetBytes("payload");
            byte[] aad = Encoding.ASCII.GetBytes("v1-header");
            byte[] message = AuthenticatedEncryption.Encrypt(key, plaintext, aad);

            Check.Equal("payload", Encoding.ASCII.GetString(AuthenticatedEncryption.Decrypt(key, message, aad)));
            // Wrong (or missing) associated data must fail authentication.
            Check.Throws<CryptographicException>(() => AuthenticatedEncryption.Decrypt(key, message, Encoding.ASCII.GetBytes("v2-header")));
            Check.Throws<CryptographicException>(() => AuthenticatedEncryption.Decrypt(key, message));
        }

        public void TamperedBytes_FailAuthentication()
        {
            byte[] key = CryptoRandom.Bytes(16);
            byte[] message = AuthenticatedEncryption.Encrypt(key, Encoding.ASCII.GetBytes("integrity matters"));

            foreach (int i in new[] { 0, AuthenticatedEncryption.NonceSize, message.Length - 1 })
            {
                var tampered = (byte[])message.Clone();
                tampered[i] ^= 0xFF;
                Check.Throws<CryptographicException>(() => AuthenticatedEncryption.Decrypt(key, tampered));
            }
        }

        public void WrongKey_FailsAuthentication()
        {
            byte[] key = CryptoRandom.Bytes(16);
            byte[] message = AuthenticatedEncryption.Encrypt(key, Encoding.ASCII.GetBytes("secret"));
            byte[] otherKey = CryptoRandom.Bytes(16);
            Check.Throws<CryptographicException>(() => AuthenticatedEncryption.Decrypt(otherKey, message));
        }

        public void EmptyPlaintext_RoundTrips()
        {
            byte[] key = CryptoRandom.Bytes(16);
            byte[] message = AuthenticatedEncryption.Encrypt(key, Array.Empty<byte>());
            Check.Equal(AuthenticatedEncryption.NonceSize + AuthenticatedEncryption.TagSize, message.Length);
            Check.Equal(0, AuthenticatedEncryption.Decrypt(key, message).Length);
        }

        public void InvalidArguments_Throw()
        {
            byte[] good = CryptoRandom.Bytes(16);
            Check.Throws<ArgumentException>(() => AuthenticatedEncryption.Encrypt(CryptoRandom.Bytes(17), Array.Empty<byte>()));
            Check.Throws<ArgumentNullException>(() => AuthenticatedEncryption.Encrypt(null!, Array.Empty<byte>()));
            Check.Throws<ArgumentNullException>(() => AuthenticatedEncryption.Encrypt(good, (byte[])null!));
            // Too short to hold a nonce + tag.
            Check.Throws<ArgumentException>(() => AuthenticatedEncryption.Decrypt(good, new byte[AuthenticatedEncryption.NonceSize]));
            // Detached: wrong nonce length.
            Check.Throws<ArgumentException>(() =>
                AuthenticatedEncryption.EncryptDetached(good, new byte[8], Array.Empty<byte>(), Array.Empty<byte>(), new byte[16]));
        }

        public void GenerateNonce_HasCorrectLength()
        {
            Check.Equal(AuthenticatedEncryption.NonceSize, AuthenticatedEncryption.GenerateNonce().Length);
        }
    }
}
