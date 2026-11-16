// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Binary
{
    /// <summary>
    /// Base58 encoding using the Bitcoin alphabet (no <c>0</c>, <c>O</c>, <c>I</c>, or <c>l</c>, to avoid
    /// visual ambiguity). Leading zero bytes are preserved as leading <c>'1'</c> characters. The base conversion is
    /// quadratic in the length both ways (about a twentieth of a second at 20 KB and a second or two at 100 KB): it suits keys,
    /// hashes and IDs, not bulk data. Decoding untrusted text, pass a <c>maxLength</c> to bound the work.
    /// </summary>
    public static class Base58
    {
        private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        private static readonly sbyte[] Reverse = BuildReverse();

        // Five digits per pass over 32-bit limbs (58^5 < 2^32): BigInteger divided by 58^10 as a long missed its one-limb
        // fast path: 100 KB took about 7 s to encode and decode, now about 1.5 s.
        private const uint FiftyEightToTheFive = 656356768;

        public static string Encode(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0) return string.Empty;

            int leadingZeros = 0;
            while (leadingZeros < data.Length && data[leadingZeros] == 0)
                leadingZeros++;

            // The rest as a big-endian unsigned integer in 32-bit limbs, most significant first.
            int n = data.Length - leadingZeros;
            var limbs = new uint[(n + 3) / 4];
            for (int i = 0; i < n; i++)
            {
                int fromEnd = n - 1 - i;
                limbs[limbs.Length - 1 - fromEnd / 4] |= (uint)data[leadingZeros + i] << (8 * (fromEnd % 4));
            }

            var digits = new StringBuilder((int)Math.Min((long)n * 138 / 100 + 1 + leadingZeros, int.MaxValue));
            int first = 0;
            while (first < limbs.Length && limbs[first] == 0) first++;
            while (first < limbs.Length)
            {
                ulong rem = 0;
                for (int i = first; i < limbs.Length; i++)
                {
                    ulong cur = (rem << 32) | limbs[i];
                    limbs[i] = (uint)(cur / FiftyEightToTheFive);
                    rem = cur % FiftyEightToTheFive;
                }
                while (first < limbs.Length && limbs[first] == 0) first++;
                // Five digits, least significant first; the most significant group drops its leading zeros.
                for (int k = 0; k < 5 && (rem > 0 || first < limbs.Length); k++)
                {
                    digits.Append(Alphabet[(int)(rem % 58)]);
                    rem /= 58;
                }
            }
            digits.Append('1', leadingZeros);

            var chars = new char[digits.Length];
            for (int i = 0; i < chars.Length; i++) chars[i] = digits[chars.Length - 1 - i];
            return new string(chars);
        }

        public static byte[] Decode(string text) => Decode(text, int.MaxValue);

        /// <summary>
        /// Decodes <paramref name="text"/>, refusing text longer than <paramref name="maxLength"/> characters with a
        /// <see cref="FormatException"/> before any work (decoding is quadratic in the length).
        /// </summary>
        public static byte[] Decode(string text, int maxLength)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (maxLength < 0) throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "Must be non-negative.");
            if (text.Length > maxLength) throw new FormatException($"Base58 text of {text.Length} characters is longer than {maxLength}.");
            if (text.Length == 0) return Array.Empty<byte>();

            int leadingOnes = 0;
            while (leadingOnes < text.Length && text[leadingOnes] == '1')
                leadingOnes++;

            // The value in 32-bit limbs, least significant first, taking up to five digits per pass.
            var limbs = new List<uint>((int)Math.Min((long)text.Length * 6 / 32 + 2, int.MaxValue));
            for (int at = 0; at < text.Length;)
            {
                int count = Math.Min(5, text.Length - at);
                uint group = 0, scale = 1;
                for (int k = 0; k < count; k++, at++)
                {
                    char c = text[at];
                    int index = c < Reverse.Length ? Reverse[c] : -1;     // O(1) lookup instead of scanning the alphabet
                    if (index < 0)
                        throw new FormatException($"Invalid Base58 character '{c}'.");
                    group = group * 58 + (uint)index;
                    scale *= 58;
                }
                ulong carry = group;
                for (int i = 0; i < limbs.Count; i++)
                {
                    ulong cur = (ulong)limbs[i] * scale + carry;
                    limbs[i] = unchecked((uint)cur);
                    carry = cur >> 32;
                }
                if (carry > 0) limbs.Add((uint)carry);
            }

            int significant = limbs.Count * 4;
            while (significant > 0 && (limbs[(significant - 1) / 4] >> (8 * ((significant - 1) % 4)) & 0xFF) == 0)
                significant--;
            var result = new byte[leadingOnes + significant];
            for (int i = 0; i < significant; i++)
                result[result.Length - 1 - i] = unchecked((byte)(limbs[i / 4] >> (8 * (i % 4))));
            return result;
        }

        private static sbyte[] BuildReverse()
        {
            var table = new sbyte[128];
            for (int i = 0; i < table.Length; i++)
                table[i] = -1;
            for (int i = 0; i < Alphabet.Length; i++)
                table[Alphabet[i]] = (sbyte)i;
            return table;
        }
    }
}
