// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace ToolBelt.Objects
{
    /// <summary>
    /// Cached custom-attribute lookups for types and members. Every reflection-based feature performs the
    /// same attribute retrievals repeatedly and attribute retrieval is not cheap, so the results are cached
    /// per (member, attribute type, inherit). Thread-safe. <see cref="Type"/> derives from
    /// <see cref="MemberInfo"/>, so the same methods serve types and members. <c>inherit</c> also applies to properties
    /// and events (which <see cref="MemberInfo.GetCustomAttributes(Type, bool)"/> ignores for them). Each call returns
    /// its own copies of the cached attributes, as reflection does, so setting a property on one cannot change what
    /// later lookups see (the copies are shallow: an array an attribute holds is still shared).
    /// </summary>
    public static class AttributeCache
    {
        private static readonly ConcurrentDictionary<(MemberInfo Member, Type Attribute, bool Inherit), Attribute[]> Cache
            = new ConcurrentDictionary<(MemberInfo, Type, bool), Attribute[]>();

        /// <summary>The single attribute of type <typeparamref name="TAttribute"/>, or null if absent.</summary>
        public static TAttribute? Get<TAttribute>(MemberInfo member, bool inherit = true) where TAttribute : Attribute
        {
            var all = GetAll<TAttribute>(member, inherit);
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>All attributes of type <typeparamref name="TAttribute"/> on the member.</summary>
        public static System.Collections.Generic.IReadOnlyList<TAttribute> GetAll<TAttribute>(MemberInfo member, bool inherit = true)
            where TAttribute : Attribute
        {
            Attribute[] cached = Lookup<TAttribute>(member, inherit);
            // Cached as Attribute[]; project to the requested type.
            var typed = new TAttribute[cached.Length];
            for (int i = 0; i < cached.Length; i++) typed[i] = (TAttribute)Copy(cached[i]);
            return typed;
        }

        private static readonly Func<object, object> Copy = (Func<object, object>)Delegate.CreateDelegate(typeof(Func<object, object>),
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!);

        /// <summary>True if the member carries at least one <typeparamref name="TAttribute"/>.</summary>
        public static bool Has<TAttribute>(MemberInfo member, bool inherit = true) where TAttribute : Attribute
            => Lookup<TAttribute>(member, inherit).Length > 0;

        private static Attribute[] Lookup<TAttribute>(MemberInfo member, bool inherit) where TAttribute : Attribute
        {
            if (member is null) throw new ArgumentNullException(nameof(member));
            return Cache.GetOrAdd((member, typeof(TAttribute), inherit),
                key => Attribute.GetCustomAttributes(key.Member, key.Attribute, key.Inherit));
        }
    }
}
