// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;

namespace ToolBelt.Security
{
    /// <summary>
    /// A thin, opinionated password-hashing layer over PBKDF2-HMAC-SHA-256: <see cref="Hash"/> produces a
    /// self-describing encoded string (algorithm, iterations, salt, digest), <see cref="Verify"/> checks a
    /// password in constant time, and <see cref="NeedsRehash"/> reports when a stored hash used weaker
    /// parameters than current policy — so callers can transparently upgrade it on the next successful login.
    /// That upgrade check is the part hand-rolled versions never have.
    /// </summary>
    public static class PasswordHasher
    {
        private const string Prefix = "PBKDF2-SHA256";
        private const int SaltLength = 16;
        private const int KeyLength = 32;

        /// <summary>The default iteration count new hashes are created with. Raise it over time as hardware improves.</summary>
        public const int DefaultIterations = 100_000;

        /// <summary>Hashes a password into an encoded string of the form <c>PBKDF2-SHA256$iterations$saltB64$hashB64</c>.</summary>
        public static string Hash(string password, int iterations = DefaultIterations)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));
            if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Iterations must be positive.");

            byte[] salt = KeyDerivation.GenerateSalt(SaltLength);
            byte[] key = KeyDerivation.Pbkdf2(password, salt, iterations, KeyLength, KdfHash.Sha256);
            return string.Join("$", Prefix, iterations.ToString(CultureInfo.InvariantCulture),
                Convert.ToBase64String(salt), Convert.ToBase64String(key));
        }

        /// <summary>Verifies a password against an encoded hash in constant time. Returns false on mismatch.</summary>
        public static bool Verify(string password, string encoded)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));
            var parsed = Parse(encoded);
            byte[] candidate = KeyDerivation.Pbkdf2(password, parsed.Salt, parsed.Iterations, parsed.Hash.Length, KdfHash.Sha256);
            return ConstantTime.Equals(candidate, parsed.Hash);
        }

        /// <summary>True if the stored hash used fewer iterations than <paramref name="currentIterations"/> (upgrade it on next login).</summary>
        public static bool NeedsRehash(string encoded, int currentIterations = DefaultIterations)
        {
            return Parse(encoded).Iterations < currentIterations;
        }

        private static (int Iterations, byte[] Salt, byte[] Hash) Parse(string encoded)
        {
            if (encoded is null) throw new ArgumentNullException(nameof(encoded));
            string[] parts = encoded.Split('$');
            if (parts.Length != 4 || parts[0] != Prefix)
                throw new FormatException("Encoded hash is not a recognized PBKDF2-SHA256 string.");
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations) || iterations < 1)
                throw new FormatException("Encoded hash has an invalid iteration count.");
            try
            {
                return (iterations, Convert.FromBase64String(parts[2]), Convert.FromBase64String(parts[3]));
            }
            catch (FormatException ex)
            {
                throw new FormatException("Encoded hash has an invalid salt or digest.", ex);
            }
        }
    }
}
