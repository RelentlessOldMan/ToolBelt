// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
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
        }

        /// <summary>All declared values of <typeparamref name="TEnum"/> (cached).</summary>
        public static IReadOnlyList<TEnum> GetValues<TEnum>() where TEnum : struct, Enum => Cache<TEnum>.Values;

        /// <summary>All declared names of <typeparamref name="TEnum"/> (cached).</summary>
        public static IReadOnlyList<string> GetNames<TEnum>() where TEnum : struct, Enum => Cache<TEnum>.Names;

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

            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var names = Cache<TEnum>.Names;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], text, comparison))
                {
                    value = Cache<TEnum>.Values[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The <see cref="DescriptionAttribute"/> text for <paramref name="value"/>, or the member name if
        /// none is present.
        /// </summary>
        public static string GetDescription<TEnum>(this TEnum value) where TEnum : struct, Enum
        {
            string name = value.ToString();
            FieldInfo? field = typeof(TEnum).GetField(name);
            var attribute = field?.GetCustomAttribute<DescriptionAttribute>();
            return attribute?.Description ?? name;
        }

        /// <summary>
        /// Decomposes a flags value into the individual non-zero declared flags it contains. For a
        /// non-flags enum this returns the single matching value (or nothing if undefined).
        /// </summary>
        public static IReadOnlyList<TEnum> GetFlags<TEnum>(this TEnum value) where TEnum : struct, Enum
        {
            var set = new List<TEnum>();
            foreach (var candidate in Cache<TEnum>.Values)
            {
                // Skip the zero value; it is "contained" by everything and is not a real flag.
                if (Convert.ToInt64(candidate) != 0 && value.HasFlag(candidate))
                    set.Add(candidate);
            }
            return set;
        }
    }
}
