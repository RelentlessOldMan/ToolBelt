// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Text;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A counting Bloom filter: like a <see cref="BloomFilter"/> but each slot is a small saturating counter
    /// instead of a single bit, which lets items be <see cref="Remove(byte[])"/>d. <see cref="MightContain(byte[])"/>
    /// still never returns a false negative for an item that is currently present, and may return a false
    /// positive at a rate bounded by the size chosen at construction. Sizing and double-hashing match
    /// <see cref="BloomFilter"/>; the counter store and hashing are inlined so this file stands alone. Not thread-safe.
    /// </summary>
    /// <remarks>
    /// Two inherent caveats of the counting variant (both documented rather than silently wrong):
    /// <list type="bullet">
    /// <item>Counters saturate at 255. A slot that reaches the maximum can no longer be decremented safely,
    /// so after extreme over-population a <see cref="Remove(byte[])"/> may leave a slot high. This is the
    /// standard counting-Bloom trade-off; size the filter for the real load.</item>
    /// <item>Removing an item that was never added (or whose slots collide with other items) can introduce a
    /// false negative for a different item. Only remove items you actually added.</item>
    /// </list>
    /// </remarks>
    public sealed class CountingBloomFilter
    {
        private const byte MaxCount = byte.MaxValue;

        private readonly byte[] _counts;
        private readonly int _slotCount;
        private readonly int _hashCount;

        /// <summary>Builds a filter sized for <paramref name="expectedItems"/> at target false-positive rate <paramref name="falsePositiveRate"/>.</summary>
        public CountingBloomFilter(int expectedItems, double falsePositiveRate = 0.01)
        {
            if (expectedItems <= 0)
                throw new ArgumentOutOfRangeException(nameof(expectedItems), expectedItems, "Expected items must be positive.");
            if (falsePositiveRate <= 0 || falsePositiveRate >= 1)
                throw new ArgumentOutOfRangeException(nameof(falsePositiveRate), falsePositiveRate, "Rate must be in (0, 1).");

            double ln2 = Math.Log(2);
            // Size in double first; a huge capacity at a tiny rate can exceed int.MaxValue, and an unchecked
            // (int) cast would wrap. Reject rather than build a broken filter (matches BloomFilter).
            double optimalSlots = Math.Ceiling(-expectedItems * Math.Log(falsePositiveRate) / (ln2 * ln2));
            if (optimalSlots > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(expectedItems),
                    "The requested capacity and false-positive rate would require more than int.MaxValue slots.");
            _slotCount = Math.Max(1, (int)optimalSlots);
            _hashCount = Math.Max(1, (int)Math.Round((double)_slotCount / expectedItems * ln2));
            _counts = new byte[_slotCount];
        }

        /// <summary>The number of counter slots.</summary>
        public int SlotCount => _slotCount;

        /// <summary>The number of hash probes per item.</summary>
        public int HashCount => _hashCount;

        /// <summary>Adds an item, incrementing its counter slots (saturating at 255).</summary>
        public void Add(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            uint h1 = Fnv(data, 2166136261u);
            uint h2 = Fnv(data, 2166136261u ^ 0x9E3779B9u);
            for (int i = 0; i < _hashCount; i++)
            {
                int slot = (int)(unchecked(h1 + (uint)i * h2) % (uint)_slotCount);
                if (_counts[slot] < MaxCount) _counts[slot]++;
            }
        }

        /// <summary>Adds the UTF-8 bytes of <paramref name="text"/>.</summary>
        public void Add(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            Add(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>
        /// Removes an item by decrementing its counter slots. Returns <c>true</c> if the item was present
        /// (every slot non-zero) before the removal; a <c>false</c> result leaves the filter untouched.
        /// Saturated (255) slots are left in place to avoid corrupting other items — see the type remarks.
        /// </summary>
        public bool Remove(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (!MightContain(data)) return false; // never decrement below a membership we don't have
            uint h1 = Fnv(data, 2166136261u);
            uint h2 = Fnv(data, 2166136261u ^ 0x9E3779B9u);
            for (int i = 0; i < _hashCount; i++)
            {
                int slot = (int)(unchecked(h1 + (uint)i * h2) % (uint)_slotCount);
                if (_counts[slot] > 0 && _counts[slot] < MaxCount) _counts[slot]--;
            }
            return true;
        }

        /// <summary>Removes the UTF-8 bytes of <paramref name="text"/>.</summary>
        public bool Remove(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Remove(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>Reports whether the item may be present. Never a false negative for a currently-added item.</summary>
        public bool MightContain(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            uint h1 = Fnv(data, 2166136261u);
            uint h2 = Fnv(data, 2166136261u ^ 0x9E3779B9u);
            for (int i = 0; i < _hashCount; i++)
            {
                int slot = (int)(unchecked(h1 + (uint)i * h2) % (uint)_slotCount);
                if (_counts[slot] == 0)
                    return false;
            }
            return true;
        }

        /// <summary>Reports whether the UTF-8 bytes of <paramref name="text"/> may be present.</summary>
        public bool MightContain(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return MightContain(Encoding.UTF8.GetBytes(text));
        }

        // FNV-1a with a configurable offset basis, giving two independent-enough hashes for double hashing.
        private static uint Fnv(byte[] data, uint offset)
        {
            uint hash = offset;
            foreach (byte b in data)
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
