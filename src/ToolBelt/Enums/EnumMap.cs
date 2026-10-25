// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ToolBelt.Enums
{
    /// <summary>
    /// A dense map from every declared member of an enum to a value, backed by a flat array indexed by
    /// the member's position, so get/set are O(1) with no hashing. Aliases (two names for one value)
    /// share a single slot. Every enum member always has a slot
    /// (initialized to a default or from a factory), so this is a total function over the enum rather
    /// than a sparse dictionary. Not thread-safe.
    /// </summary>
    public sealed class EnumMap<TEnum, TValue> : IEnumerable<KeyValuePair<TEnum, TValue>>
        where TEnum : struct, Enum
    {
        // Cached once per closed generic: the distinct enum values and a value->slot lookup.
        private static class Meta
        {
            public static readonly TEnum[] Values = DistinctValues();
            public static readonly ReadOnlyCollection<TEnum> Keys = Array.AsReadOnly(Values);
            public static readonly Dictionary<TEnum, int> Index = BuildIndex();

            // Enum.GetValues lists an aliased value once per name; a map needs one slot per value.
            private static TEnum[] DistinctValues()
            {
                var all = (TEnum[])Enum.GetValues(typeof(TEnum));
                var seen = new HashSet<TEnum>();
                var distinct = new List<TEnum>(all.Length);
                foreach (var value in all)
                    if (seen.Add(value))
                        distinct.Add(value);
                return distinct.ToArray();
            }

            private static Dictionary<TEnum, int> BuildIndex()
            {
                var index = new Dictionary<TEnum, int>(Values.Length);
                for (int i = 0; i < Values.Length; i++)
                    index[Values[i]] = i;
                return index;
            }
        }

        private readonly TValue[] _values;

        /// <summary>Creates a map with every member initialized to <paramref name="defaultValue"/>.</summary>
        public EnumMap(TValue defaultValue = default!)
        {
            _values = new TValue[Meta.Values.Length];
            for (int i = 0; i < _values.Length; i++)
                _values[i] = defaultValue;
        }

        /// <summary>Creates a map with every member initialized from <paramref name="factory"/>.</summary>
        public EnumMap(Func<TEnum, TValue> factory)
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            _values = new TValue[Meta.Values.Length];
            for (int i = 0; i < _values.Length; i++)
                _values[i] = factory(Meta.Values[i]);
        }

        /// <summary>Number of distinct enum values (the fixed slot count).</summary>
        public int Count => _values.Length;

        /// <summary>All distinct enum values (the keys), in ascending order of their unsigned underlying value.</summary>
        public IReadOnlyList<TEnum> Keys => Meta.Keys;

        /// <summary>Gets or sets the value for a member. Throws for a value that is not a declared member.</summary>
        public TValue this[TEnum key]
        {
            get => _values[IndexOf(key)];
            set => _values[IndexOf(key)] = value;
        }

        /// <summary>Gets the value for a member; false only if <paramref name="key"/> is not a declared member.</summary>
        public bool TryGetValue(TEnum key, out TValue value)
        {
            if (Meta.Index.TryGetValue(key, out int i))
            {
                value = _values[i];
                return true;
            }
            value = default!;
            return false;
        }

        public IEnumerator<KeyValuePair<TEnum, TValue>> GetEnumerator()
        {
            for (int i = 0; i < _values.Length; i++)
                yield return new KeyValuePair<TEnum, TValue>(Meta.Values[i], _values[i]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static int IndexOf(TEnum key)
        {
            if (!Meta.Index.TryGetValue(key, out int i))
                throw new ArgumentOutOfRangeException(nameof(key), key, "Value is not a declared member of the enum.");
            return i;
        }
    }
}
