// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Identifiers
{
    /// <summary>
    /// Generates ULIDs — 128-bit, lexicographically sortable identifiers rendered as 26 Crockford
    /// Base32 characters. The high 48 bits are the millisecond timestamp (first 10 chars); the low 80
    /// bits are randomness (last 16 chars). Within a single millisecond the randomness is incremented so
    /// ids stay strictly increasing (monotonic). Time and randomness are injectable for deterministic
    /// tests. Thread-safe. (Crockford encoding is inlined so this file stands alone.)
    /// </summary>
    public sealed class UlidGenerator
    {
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        private static readonly DateTimeOffset Epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly Func<DateTimeOffset> _clock;
        private readonly Random _random;
        private readonly object _gate = new object();

        private long _lastTimestamp = -1;
        private readonly byte[] _lastRandom = new byte[10];

        public UlidGenerator(Func<DateTimeOffset>? clock = null, Random? random = null)
        {
            _clock = clock ?? (static () => DateTimeOffset.UtcNow);
            _random = random ?? new Random();
        }

        /// <summary>Produces the next ULID string. Strictly greater (ordinally) than the previous one from this instance.</summary>
        public string NewUlid()
        {
            lock (_gate)
            {
                long now = (long)(_clock() - Epoch).TotalMilliseconds;
                if (now < 0)
                    throw new InvalidOperationException("The clock is before the Unix epoch.");

                if (now > _lastTimestamp)
                {
                    _lastTimestamp = now;
                    _random.NextBytes(_lastRandom);
                }
                else
                {
                    // Same or earlier ms: keep the timestamp and bump randomness to preserve monotonicity.
                    if (IncrementRandom(_lastRandom))
                        _lastTimestamp++; // randomness overflowed; carry into the timestamp
                }

                return Encode(_lastTimestamp, _lastRandom);
            }
        }

        /// <summary>Extracts the millisecond timestamp encoded in a ULID string.</summary>
        public static long DecodeTimestamp(string ulid)
        {
            if (ulid is null) throw new ArgumentNullException(nameof(ulid));
            if (ulid.Length != 26) throw new FormatException("A ULID must be 26 characters.");

            long timestamp = 0;
            for (int i = 0; i < 10; i++)
            {
                int value = DecodeChar(ulid[i]);
                timestamp = timestamp * 32 + value;
            }
            return timestamp;
        }

        private static string Encode(long timestamp, byte[] randomness)
        {
            var chars = new char[26];

            // Timestamp: 48 bits into 10 chars (top 2 bits are zero).
            long t = timestamp;
            for (int i = 9; i >= 0; i--)
            {
                chars[i] = Alphabet[(int)(t & 0x1F)];
                t >>= 5;
            }

            // Randomness: 80 bits into 16 chars, 5 bits at a time, MSB-first.
            int bitPos = 0;
            for (int c = 0; c < 16; c++)
            {
                chars[10 + c] = Alphabet[Read5(randomness, bitPos)];
                bitPos += 5;
            }

            return new string(chars);
        }

        private static int Read5(byte[] bytes, int bitPos)
        {
            int value = 0;
            for (int k = 0; k < 5; k++)
            {
                int b = bitPos + k;
                int bit = (bytes[b / 8] >> (7 - (b % 8))) & 1;
                value = (value << 1) | bit;
            }
            return value;
        }

        private static bool IncrementRandom(byte[] r)
        {
            for (int i = r.Length - 1; i >= 0; i--)
            {
                if (r[i] == 0xFF)
                {
                    r[i] = 0;
                }
                else
                {
                    r[i]++;
                    return false;
                }
            }
            return true; // full overflow
        }

        private static int DecodeChar(char c)
        {
            char u = char.ToUpperInvariant(c);
            if (u == 'O') u = '0';
            if (u == 'I' || u == 'L') u = '1';
            int index = Alphabet.IndexOf(u);
            if (index < 0)
                throw new FormatException($"Invalid ULID character '{c}'.");
            return index;
        }
    }
}
