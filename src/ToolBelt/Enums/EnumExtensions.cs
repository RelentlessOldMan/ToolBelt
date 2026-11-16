// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;

namespace ToolBelt.Enums
{
    /// <summary>
    /// Allocation-light helpers over enum types: cached value/name lookups, strict name parsing, the
    /// <see cref="DescriptionAttribute"/> text, and decomposition of a <c>[Flags]</c> value into its set
    /// members.
    /// </summary>
    public static class EnumExtensions
    {
        // Per-type caches populated once on first use. GetValues/GetNames are reflective and worth caching.
        private static class Cache<TEnum> where TEnum : struct, Enum
        {
            public static readonly TEnum[] Values = (TEnum[])Enum.GetValues(typeof(TEnum));
            public static readonly string[] Names = Enum.GetNames(typeof(TEnum));
            public static readonly ReadOnlyCollection<TEnum> ValuesView = Array.AsReadOnly(Values);
            public static readonly ReadOnlyCollection<string> NamesView = Array.AsReadOnly(Names);
            public static readonly bool IsFlags = typeof(TEnum).IsDefined(typeof(FlagsAttribute), false);
        }

        // Value → description, in declaration order: for aliases (two names, one value) the first declared one with a
        // [Description] wins, else the first declared name. ToString() picks one alias, and .NET 8 and .NET Framework
        // pick different ones, so the attribute on the other was lost and the fallback name changed with the runtime.
        private static class Descriptions<TEnum> where TEnum : struct, Enum
        {
            public static readonly Dictionary<TEnum, string> Map = Build();

            private static Dictionary<TEnum, string> Build()
            {
                var map = new Dictionary<TEnum, string>();
                var described = new HashSet<TEnum>();
                foreach (FieldInfo field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    var value = (TEnum)field.GetValue(null)!;
                    var attribute = field.GetCustomAttribute<DescriptionAttribute>();
                    if (described.Contains(value)) continue;
                    if (attribute != null)
                    {
                        map[value] = attribute.Description;
                        described.Add(value);
                    }
                    else if (!map.ContainsKey(value))
                    {
                        map[value] = field.Name;
                    }
                }
                return map;
            }
        }

        /// <summary>All declared values of <typeparamref name="TEnum"/> (cached, read-only).</summary>
        public static IReadOnlyList<TEnum> GetValues<TEnum>() where TEnum : struct, Enum => Cache<TEnum>.ValuesView;

        /// <summary>All declared names of <typeparamref name="TEnum"/> (cached, read-only).</summary>
        public static IReadOnlyList<string> GetNames<TEnum>() where TEnum : struct, Enum => Cache<TEnum>.NamesView;

        /// <summary>
        /// Parses a string that must exactly match a declared member name (numeric strings and undefined
        /// values are rejected — unlike <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>).
        /// </summary>
        public static bool TryParseName<TEnum>(string? text, out TEnum value, bool ignoreCase = true)
            where TEnum : struct, Enum
        {
            value = default;
            if (string.IsNullOrEmpty(text))
                return false;

            // An exact match first: with ignoreCase, "FOO" found Foo before FOO in an enum that declares both.
            var names = Cache<TEnum>.Names;
            int passes = ignoreCase ? 2 : 1;
            for (int p = 0; p < passes; p++)
            {
                var comparison = p == 0 ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                for (int i = 0; i < names.Length; i++)
                {
                    if (string.Equals(names[i], text, comparison))
                    {
                        value = Cache<TEnum>.Values[i];
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// The <see cref="DescriptionAttribute"/> text for <paramref name="value"/>, or the member name if
        /// none is present. When several members share the value, the first declared one with a description wins,
        /// else the first declared name. An undefined value gives <see cref="Enum.ToString()"/>.
        /// </summary>
        public static string GetDescription<TEnum>(this TEnum value) where TEnum : struct, Enum
            => Descriptions<TEnum>.Map.TryGetValue(value, out string? text) ? text : value.ToString();

        /// <summary>
        /// Decomposes a <c>[Flags]</c> value into the declared single-bit members it contains (composite
        /// members such as <c>ReadWrite = Read | Write</c> and the zero member are left out). For a non-flags
        /// enum this returns the value itself when it is a declared member, or nothing if it is undefined.
        /// </summary>
        public static IReadOnlyList<TEnum> GetFlags<TEnum>(this TEnum value) where TEnum : struct, Enum
        {
            var set = new List<TEnum>();
            if (!Cache<TEnum>.IsFlags)
            {
                if (Array.IndexOf(Cache<TEnum>.Values, value) >= 0)
                    set.Add(value);
                return set;
            }
            TEnum previous = default;
            foreach (var candidate in Cache<TEnum>.Values)
            {
                if (set.Count > 0 && EqualityComparer<TEnum>.Default.Equals(candidate, previous))
                    continue; // an alias of the member just added
                if (IsSingleBit(candidate) && value.HasFlag(candidate))
                {
                    set.Add(candidate);
                    previous = candidate;
                }
            }
            return set;
        }

        private static bool IsSingleBit<TEnum>(TEnum value) where TEnum : struct, Enum
        {
            ulong bits;
            switch (Type.GetTypeCode(typeof(TEnum)))
            {
                case TypeCode.SByte: bits = unchecked((byte)Convert.ToSByte(value)); break;
                case TypeCode.Int16: bits = unchecked((ushort)Convert.ToInt16(value)); break;
                case TypeCode.Int32: bits = unchecked((uint)Convert.ToInt32(value)); break;
                case TypeCode.Int64: bits = unchecked((ulong)Convert.ToInt64(value)); break;
                default: bits = Convert.ToUInt64(value); break;
            }
            return bits != 0 && (bits & (bits - 1)) == 0;
        }
    }
}
