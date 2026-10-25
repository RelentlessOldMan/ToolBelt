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
            if (defaultSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(defaultSize), defaultSize, "Default size must be positive.");
#if NETSTANDARD2_0
            // On .NET Framework, new Random() seeds from the tick count, so instances created close together would repeat.
            _random = random ?? new Random(Guid.NewGuid().GetHashCode());
#else
            _random = random ?? new Random();
#endif
            _defaultSize = defaultSize;
        }

        /// <summary>Generates an id of the configured default size.</summary>
        public string New() => New(_defaultSize);

        /// <summary>Generates an id of the given size.</summary>
        public string New(int size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be positive.");
            var chars = new char[size];
            for (int i = 0; i < size; i++)
                chars[i] = _alphabet[_random.Next(_alphabet.Length)];
            return new string(chars);
        }
    }
}
