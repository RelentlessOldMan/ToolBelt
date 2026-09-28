using System;
using System.Security.Cryptography;
using System.Text;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Security
{
    public sealed class KeyDerivationTests
    {
        // Published PBKDF2-HMAC-SHA256 vectors (password="password", salt="salt", dkLen=32).
        public void KnownVectors()
        {
            byte[] salt = Encoding.ASCII.GetBytes("salt");
            Check.Equal("120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b",
                Hashing.ToHex(KeyDerivation.Pbkdf2("password", salt, 1, 32)));
            Check.Equal("ae4d0c95af6b46d32d0adff928f06dd02a303f8ef3c251dfd6e2d85a95474c43",
                Hashing.ToHex(KeyDerivation.Pbkdf2("password", salt, 2, 32)));
        }

        // Differential: our hand-rolled PBKDF2 must match the framework's Rfc2898DeriveBytes (SHA-256).
        public void MatchesFrameworkRfc2898()
        {
            var rng = new Random(51);
            for (int t = 0; t < 50; t++)
            {
                var pw = new byte[rng.Next(1, 20)];
                var salt = new byte[rng.Next(8, 24)];
                rng.NextBytes(pw); rng.NextBytes(salt);
                int iterations = rng.Next(1, 500);
                int keyLength = rng.Next(1, 80); // spans multiple output blocks

                byte[] mine = KeyDerivation.Pbkdf2(pw, salt, iterations, keyLength, KdfHash.Sha256);
                using var framework = new Rfc2898DeriveBytes(pw, salt, iterations, HashAlgorithmName.SHA256);
                byte[] theirs = framework.GetBytes(keyLength);

                Check.True(mine.AsSpan().SequenceEqual(theirs), $"t{t}: iter={iterations} len={keyLength}");
            }
        }

        public void Sha512MatchesFramework()
        {
            var pw = Encoding.UTF8.GetBytes("correct horse battery staple");
            var salt = Encoding.ASCII.GetBytes("some-salt-value");
            byte[] mine = KeyDerivation.Pbkdf2(pw, salt, 250, 64, KdfHash.Sha512);
            using var framework = new Rfc2898DeriveBytes(pw, salt, 250, HashAlgorithmName.SHA512);
            Check.True(mine.AsSpan().SequenceEqual(framework.GetBytes(64)));
        }

        public void GenerateSalt()
        {
            Check.Equal(16, KeyDerivation.GenerateSalt().Length);
            Check.Equal(32, KeyDerivation.GenerateSalt(32).Length);
            Check.False(KeyDerivation.GenerateSalt().AsSpan().SequenceEqual(KeyDerivation.GenerateSalt()));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => KeyDerivation.Pbkdf2((string)null!, new byte[8], 1, 16));
            Check.Throws<ArgumentOutOfRangeException>(() => KeyDerivation.Pbkdf2("p", new byte[8], 0, 16));
            Check.Throws<ArgumentOutOfRangeException>(() => KeyDerivation.Pbkdf2("p", new byte[8], 1, 0));
        }
    }
}
