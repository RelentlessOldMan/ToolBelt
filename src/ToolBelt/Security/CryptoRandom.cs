// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Security.Cryptography;
using System.Text;

namespace ToolBelt.Security
{
    /// <summary>Character classes a generated password must include.</summary>
    [Flags]
    public enum PasswordClasses
    {
        Lower = 1,
        Upper = 2,
        Digit = 4,
        Symbol = 8,
        All = Lower | Upper | Digit | Symbol,
    }

    /// <summary>
    /// Cryptographically secure random values: raw bytes, URL-safe tokens, numeric codes and passwords with
    /// guaranteed character-class coverage. Uses the framework CSPRNG with unbiased selection (rejection
    /// sampling). This is NOT the seeded, reproducible generator — use <c>ToolBelt.Numerics.DeterministicRandom</c>
    /// for simulations, and this for anything secret or unguessable.
    /// </summary>
    public static class CryptoRandom
    {
        private const string Lowers = "abcdefghijklmnopqrstuvwxyz";
        private const string Uppers = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string Digits = "0123456789";
        private const string Symbols = "!@#$%^&*()-_=+[]{}";

        /// <summary>Returns <paramref name="count"/> cryptographically random bytes.</summary>
        public static byte[] Bytes(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be non-negative.");
            var buffer = new byte[count];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(buffer);
            return buffer;
        }

        /// <summary>A URL-safe random token of the requested character length (base64url alphabet).</summary>
        public static string Token(int length)
        {
            if (length < 1) throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be positive.");
            // Each base64 char carries 6 uniform bits, so the characters are unbiased over the 64-char alphabet.
            int byteCount = (length * 6 + 7) / 8;
            string encoded = Convert.ToBase64String(Bytes(byteCount))
                .Replace('+', '-').Replace('/', '_').Replace("=", string.Empty);
            return encoded.Substring(0, length);
        }

        /// <summary>A numeric code of the requested number of digits (leading zeros preserved).</summary>
        public static string NumericCode(int digits)
        {
            if (digits < 1) throw new ArgumentOutOfRangeException(nameof(digits), digits, "Digits must be positive.");
            using var rng = RandomNumberGenerator.Create();
            var sb = new StringBuilder(digits);
            for (int i = 0; i < digits; i++) sb.Append((char)('0' + NextIndex(rng, 10)));
            return sb.ToString();
        }

        /// <summary>
        /// A random password of the given length containing at least one character from each requested class.
        /// </summary>
        public static string Password(int length, PasswordClasses classes = PasswordClasses.All)
        {
            if (classes == 0) throw new ArgumentException("At least one character class is required.", nameof(classes));
            var pools = BuildPools(classes);
            if (length < pools.Count)
                throw new ArgumentOutOfRangeException(nameof(length), length, $"Length must be at least {pools.Count} to include each class.");

            string all = string.Concat(pools.ToArray());
            using var rng = RandomNumberGenerator.Create();
            var chars = new char[length];

            // Guarantee one from each required class, then fill the rest from the combined pool.
            for (int i = 0; i < pools.Count; i++) chars[i] = pools[i][NextIndex(rng, pools[i].Length)];
            for (int i = pools.Count; i < length; i++) chars[i] = all[NextIndex(rng, all.Length)];

            // Fisher-Yates shuffle so the guaranteed characters are not always at the front.
            for (int i = length - 1; i > 0; i--)
            {
                int j = NextIndex(rng, i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }
            return new string(chars);
        }

        private static System.Collections.Generic.List<string> BuildPools(PasswordClasses classes)
        {
            var pools = new System.Collections.Generic.List<string>();
            if ((classes & PasswordClasses.Lower) != 0) pools.Add(Lowers);
            if ((classes & PasswordClasses.Upper) != 0) pools.Add(Uppers);
            if ((classes & PasswordClasses.Digit) != 0) pools.Add(Digits);
            if ((classes & PasswordClasses.Symbol) != 0) pools.Add(Symbols);
            return pools;
        }

        /// <summary>Unbiased index in [0, max) via rejection sampling (works on both target frameworks).</summary>
        private static int NextIndex(RandomNumberGenerator rng, int max)
        {
            uint limit = uint.MaxValue - (uint.MaxValue % (uint)max);
            var buffer = new byte[4];
            uint value;
            do
            {
                rng.GetBytes(buffer);
                value = BitConverter.ToUInt32(buffer, 0);
            }
            while (value >= limit);
            return (int)(value % (uint)max);
        }
    }
}
