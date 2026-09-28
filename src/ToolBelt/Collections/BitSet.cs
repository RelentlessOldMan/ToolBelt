// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A growable set of non-negative integers backed by a packed array of 64-bit words. Membership,
    /// set, and clear are O(1); the boolean set operations are O(words). Reading a bit beyond the current
    /// capacity returns false; setting one grows the backing store. Not thread-safe.
    /// </summary>
    public sealed class BitSet
    {
        private ulong[] _words;

        public BitSet(int initialCapacityBits = 64)
        {
            if (initialCapacityBits < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacityBits), initialCapacityBits, "Capacity must not be negative.");
            _words = new ulong[Math.Max(1, (initialCapacityBits + 63) / 64)];
        }

        /// <summary>Whether bit <paramref name="index"/> is set.</summary>
        public bool Get(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index must not be negative.");
            int word = index >> 6;
            return word < _words.Length && (_words[word] & (1UL << (index & 63))) != 0;
        }

        public bool this[int index] => Get(index);

        /// <summary>Sets bit <paramref name="index"/>, growing the backing store if needed.</summary>
        public void Set(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index must not be negative.");
            EnsureWord(index >> 6);
            _words[index >> 6] |= 1UL << (index & 63);
        }

        /// <summary>Clears bit <paramref name="index"/>.</summary>
        public void Clear(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index must not be negative.");
            int word = index >> 6;
            if (word < _words.Length)
                _words[word] &= ~(1UL << (index & 63));
        }

        /// <summary>Sets or clears bit <paramref name="index"/> per <paramref name="value"/>.</summary>
        public void SetTo(int index, bool value)
        {
            if (value) Set(index);
            else Clear(index);
        }

        /// <summary>The number of set bits (population count).</summary>
        public int Count
        {
            get
            {
                int count = 0;
                foreach (ulong word in _words)
                    count += PopCount(word);
                return count;
            }
        }

        /// <summary>In-place union: this ∪= other.</summary>
        public void UnionWith(BitSet other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            EnsureWord(other._words.Length - 1);
            for (int i = 0; i < other._words.Length; i++)
                _words[i] |= other._words[i];
        }

        /// <summary>In-place intersection: this ∩= other.</summary>
        public void IntersectWith(BitSet other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            for (int i = 0; i < _words.Length; i++)
                _words[i] &= i < other._words.Length ? other._words[i] : 0UL;
        }

        /// <summary>In-place difference: this ∖= other.</summary>
        public void ExceptWith(BitSet other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            int n = Math.Min(_words.Length, other._words.Length);
            for (int i = 0; i < n; i++)
                _words[i] &= ~other._words[i];
        }

        /// <summary>In-place symmetric difference: this ⊕= other.</summary>
        public void SymmetricExceptWith(BitSet other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            EnsureWord(other._words.Length - 1);
            for (int i = 0; i < other._words.Length; i++)
                _words[i] ^= other._words[i];
        }

        /// <summary>Enumerates the indices of set bits in ascending order.</summary>
        public IEnumerable<int> EnumerateSetBits()
        {
            for (int w = 0; w < _words.Length; w++)
            {
                ulong bits = _words[w];
                while (bits != 0)
                {
                    int bit = TrailingZeroCount(bits);
                    yield return (w << 6) + bit;
                    bits &= bits - 1; // clear lowest set bit
                }
            }
        }

        private void EnsureWord(int wordIndex)
        {
            if (wordIndex < _words.Length)
                return;
            int newLength = _words.Length;
            while (newLength <= wordIndex)
                newLength *= 2;
            Array.Resize(ref _words, newLength);
        }

        // SWAR population count for a 64-bit word — no BCL intrinsic on netstandard2.0.
        private static int PopCount(ulong x)
        {
            x -= (x >> 1) & 0x5555555555555555UL;
            x = (x & 0x3333333333333333UL) + ((x >> 2) & 0x3333333333333333UL);
            x = (x + (x >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((x * 0x0101010101010101UL) >> 56);
        }

        private static int TrailingZeroCount(ulong x)
        {
            if (x == 0)
                return 64;
            int n = 0;
            while ((x & 1UL) == 0)
            {
                x >>= 1;
                n++;
            }
            return n;
        }
    }
}
