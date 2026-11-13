// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Text;

namespace ToolBelt.Identifiers
{
    /// <summary>
    /// Generates compact, URL-safe random identifiers (NanoID-style). The default alphabet is the 64
    /// URL-safe characters <c>A–Z a–z 0–9 _ -</c> and the default size is 21.
    /// </summary>
    /// <remarks>
    /// Randomness comes from the supplied <see cref="System.Random"/> (or a default one), which is
    /// <b>NOT cryptographically secure</b>. Use these ids for compact uniqueness (keys, correlation ids),
    /// not for security tokens, passwords, or anything that must be unguessable. Instances are not
    /// thread-safe (they share the underlying <see cref="System.Random"/>).
    /// </remarks>
    public sealed class NanoId
    {
        /// <summary>The default URL-safe 64-character alphabet.</summary>
        public const string UrlSafeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-";

        private readonly Random _random;
        private readonly string _alphabet;
        private readonly int _defaultSize;

        public NanoId(Random? random = null, string? alphabet = null, int defaultSize = 21)
        {
            _alphabet = alphabet ?? UrlSafeAlphabet;
            if (_alphabet.Length < 2 || _alphabet.Length > 256)
                throw new ArgumentException("Alphabet must have between 2 and 256 characters.", nameof(alphabet));
            // A repeated character was silently drawn more often, and a surrogate half made ids that are not valid UTF-16.
            for (int i = 0; i < _alphabet.Length; i++)
            {
                if (char.IsSurrogate(_alphabet[i]))
                    throw new ArgumentException($"alphabet[{i}] is a surrogate; the alphabet must be single UTF-16 characters.", nameof(alphabet));
                if (_alphabet.IndexOf(_alphabet[i], i + 1) >= 0)
                    throw new ArgumentException($"'{_alphabet[i]}' appears more than once in the alphabet.", nameof(alphabet));
            }
            if (defaultSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(defaultSize), defaultSize, "Default size must be 1 to 0x3FFFFFDF (the longest string).");
#if NETSTANDARD2_0
            // On .NET Framework, new Random() seeds from the tick count, so instances created close together would repeat.
            _random = random ?? new CryptoBackedRandom();
#else
            _random = random ?? new Random();
#endif
            _defaultSize = defaultSize;
        }

        /// <summary>Generates an id of the configured default size.</summary>
        public string New() => New(_defaultSize);

        private const int MaxStringLength = 0x3FFFFFDF;

        /// <summary>Generates an id of the given size.</summary>
        public string New(int size)
        {
            // Past the longest string .NET can hold the id was built and then threw OutOfMemoryException.
            if (size <= 0 || size > MaxStringLength)
                throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be 1 to 0x3FFFFFDF (the longest string).");
            var chars = new char[size];
            for (int i = 0; i < size; i++)
                chars[i] = _alphabet[_random.Next(_alphabet.Length)];
            return new string(chars);
        }
#if NETSTANDARD2_0
        // A Random drawing from the OS generator. Seeding new Random(int) gave only 2^31 streams on .NET Framework, so
        // separate instances repeated whole id streams (about one pair in 2^32).
        private sealed class CryptoBackedRandom : Random
        {
            private static readonly System.Security.Cryptography.RandomNumberGenerator Rng = System.Security.Cryptography.RandomNumberGenerator.Create();

            protected override double Sample()
            {
                var b = new byte[8];
                Rng.GetBytes(b);
                return (BitConverter.ToUInt64(b, 0) >> 11) * (1.0 / (1UL << 53));
            }

            public override int Next() => (int)(Sample() * int.MaxValue);
            public override int Next(int minValue, int maxValue) => minValue + (int)(Sample() * ((long)maxValue - minValue));
            public override double NextDouble() => Sample();
            public override void NextBytes(byte[] buffer) => Rng.GetBytes(buffer);
        }
#endif
    }
}
