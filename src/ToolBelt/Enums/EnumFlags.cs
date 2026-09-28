// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Enums
{
    /// <summary>
    /// Generic combinators for <c>[Flags]</c> enums — add, remove, toggle, and membership tests — without
    /// per-type boilerplate. Operates on the underlying integral value (up to 64 bits signed). Intended
    /// for enums with signed integral backing (byte/short/int/long); enums backed by <c>ulong</c> values
    /// above <see cref="long.MaxValue"/> are not supported.
    /// </summary>
    public static class EnumFlags
    {
        /// <summary>Whether <paramref name="value"/> contains every bit in <paramref name="flags"/>.</summary>
        public static bool HasAllFlags<TEnum>(TEnum value, TEnum flags) where TEnum : struct, Enum
        {
            long f = ToLong(flags);
            return (ToLong(value) & f) == f;
        }

        /// <summary>Whether <paramref name="value"/> contains any bit in <paramref name="flags"/>.</summary>
        public static bool HasAnyFlags<TEnum>(TEnum value, TEnum flags) where TEnum : struct, Enum
            => (ToLong(value) & ToLong(flags)) != 0;

        /// <summary>Returns <paramref name="value"/> with <paramref name="flags"/> set.</summary>
        public static TEnum Add<TEnum>(TEnum value, TEnum flags) where TEnum : struct, Enum
            => FromLong<TEnum>(ToLong(value) | ToLong(flags));

        /// <summary>Returns <paramref name="value"/> with <paramref name="flags"/> cleared.</summary>
        public static TEnum Remove<TEnum>(TEnum value, TEnum flags) where TEnum : struct, Enum
            => FromLong<TEnum>(ToLong(value) & ~ToLong(flags));

        /// <summary>Returns <paramref name="value"/> with <paramref name="flags"/> flipped.</summary>
        public static TEnum Toggle<TEnum>(TEnum value, TEnum flags) where TEnum : struct, Enum
            => FromLong<TEnum>(ToLong(value) ^ ToLong(flags));

        /// <summary>Combines several flag values into one with a bitwise OR.</summary>
        public static TEnum Combine<TEnum>(params TEnum[] flags) where TEnum : struct, Enum
        {
            if (flags is null) throw new ArgumentNullException(nameof(flags));
            long acc = 0;
            foreach (var f in flags)
                acc |= ToLong(f);
            return FromLong<TEnum>(acc);
        }

        private static long ToLong<TEnum>(TEnum value) where TEnum : struct, Enum
            => Convert.ToInt64(value);

        private static TEnum FromLong<TEnum>(long value) where TEnum : struct, Enum
            => (TEnum)Enum.ToObject(typeof(TEnum), value);
    }
}
