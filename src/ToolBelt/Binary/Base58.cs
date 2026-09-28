// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace ToolBelt.Binary
{
    /// <summary>
    /// Base58 encoding using the Bitcoin alphabet (no <c>0</c>, <c>O</c>, <c>I</c>, or <c>l</c>, to avoid
    /// visual ambiguity). Leading zero bytes are preserved as leading <c>'1'</c> characters. Uses
    /// <see cref="BigInteger"/> for the base conversion.
    /// </summary>
    public static class Base58
    {
        private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        private static readonly sbyte[] Reverse = BuildReverse();

        public static string Encode(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0) return string.Empty;

            int leadingZeros = 0;
            while (leadingZeros < data.Length && data[leadingZeros] == 0)
                leadingZeros++;

            // Interpret the bytes as a big-endian unsigned integer.
            var littleEndianUnsigned = new byte[data.Length + 1];
            for (int i = 0; i < data.Length; i++)
                littleEndianUnsigned[i] = data[data.Length - 1 - i];
            // trailing zero byte (already zero) keeps the BigInteger non-negative
            var number = new BigInteger(littleEndianUnsigned);

            var digits = new List<char>();
            while (number > 0)
            {
                number = BigInteger.DivRem(number, 58, out BigInteger remainder);
                digits.Add(Alphabet[(int)remainder]);
            }
            for (int i = 0; i < leadingZeros; i++)
                digits.Add('1');

            digits.Reverse();
            return new string(digits.ToArray());
        }

        public static byte[] Decode(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (text.Length == 0) return Array.Empty<byte>();

            int leadingOnes = 0;
            while (leadingOnes < text.Length && text[leadingOnes] == '1')
                leadingOnes++;

            BigInteger number = BigInteger.Zero;
            foreach (char c in text)
            {
                int index = c < Reverse.Length ? Reverse[c] : -1; // O(1) lookup instead of scanning the alphabet
                if (index < 0)
                    throw new FormatException($"Invalid Base58 character '{c}'.");
                number = number * 58 + index;
            }

            // BigInteger.ToByteArray is little-endian and may carry a sign byte; convert to big-endian magnitude.
            byte[] little = number.ToByteArray();
            int significant = little.Length;
            while (significant > 0 && little[significant - 1] == 0)
                significant--;

            var result = new byte[leadingOnes + significant];
            for (int i = 0; i < significant; i++)
                result[leadingOnes + i] = little[significant - 1 - i];
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
