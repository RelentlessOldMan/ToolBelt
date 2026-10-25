// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace ToolBelt.Objects
{
    /// <summary>
    /// Fast member access by name backed by compiled expression trees instead of reflection. The first call
    /// for a given (type, member) compiles and caches a delegate; subsequent calls reuse it, so repeated
    /// access runs at roughly field-access speed — far faster than <see cref="PropertyInfo.GetValue(object)"/>.
    /// Works on public instance properties and fields, matched by exact (case-sensitive) name; a property whose
    /// setter is not public, or a read-only field, is not writable. Writing null to a value-type member stores its default.
    ///
    /// <para>The <c>object</c>-based accessors box value-type members. The strongly-typed
    /// <see cref="Getter{TTarget, TValue}(string)"/> / <see cref="Setter{TTarget, TValue}(string)"/> avoid
    /// boxing entirely. On a <b>value-type</b> instance the <c>object</c>-based setter writes into the box it is
    /// given (as <see cref="PropertyInfo.SetValue(object, object)"/> does), not into the variable the box was copied
    /// from; the typed setter takes its struct by value, so its write is lost — these accessors are intended for
    /// reference types. On an interface type, members inherited from base interfaces are found.</para>
    /// </summary>
    public static class ExpressionAccessor
    {
        private static readonly ConcurrentDictionary<(Type Type, string Member), Func<object, object?>> Getters
            = new ConcurrentDictionary<(Type, string), Func<object, object?>>();
        private static readonly ConcurrentDictionary<(Type Type, string Member), Action<object, object?>> Setters
            = new ConcurrentDictionary<(Type, string), Action<object, object?>>();
        private static readonly ConcurrentDictionary<(Type Target, Type Value, string Member), Delegate> TypedGetters
            = new ConcurrentDictionary<(Type, Type, string), Delegate>();
        private static readonly ConcurrentDictionary<(Type Target, Type Value, string Member), Delegate> TypedSetters
            = new ConcurrentDictionary<(Type, Type, string), Delegate>();

        /// <summary>Returns a cached compiled getter that reads <paramref name="memberName"/> from an instance of <paramref name="type"/>.</summary>
        public static Func<object, object?> Getter(Type type, string memberName)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (memberName is null) throw new ArgumentNullException(nameof(memberName));
            return Getters.GetOrAdd((type, memberName), key => BuildGetter(key.Type, key.Member));
        }

        /// <summary>Returns a cached compiled setter that writes <paramref name="memberName"/> on an instance of <paramref name="type"/>.</summary>
        public static Action<object, object?> Setter(Type type, string memberName)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (memberName is null) throw new ArgumentNullException(nameof(memberName));
            return Setters.GetOrAdd((type, memberName), key => BuildSetter(key.Type, key.Member));
        }

        /// <summary>Reads <paramref name="memberName"/> from <paramref name="instance"/> using the instance's runtime type.</summary>
        public static object? GetValue(object instance, string memberName)
        {
            if (instance is null) throw new ArgumentNullException(nameof(instance));
            return Getter(instance.GetType(), memberName)(instance);
        }

        /// <summary>Writes <paramref name="value"/> to <paramref name="memberName"/> on <paramref name="instance"/> using the instance's runtime type.</summary>
        public static void SetValue(object instance, string memberName, object? value)
        {
            if (instance is null) throw new ArgumentNullException(nameof(instance));
            Setter(instance.GetType(), memberName)(instance, value);
        }

        /// <summary>
        /// Returns a cached, boxing-free getter for <typeparamref name="TValue"/> member
        /// <paramref name="memberName"/> on <typeparamref name="TTarget"/>.
        /// </summary>
        public static Func<TTarget, TValue> Getter<TTarget, TValue>(string memberName)
        {
            if (memberName is null) throw new ArgumentNullException(nameof(memberName));
            return (Func<TTarget, TValue>)TypedGetters.GetOrAdd(
                (typeof(TTarget), typeof(TValue), memberName),
                _ =>
                {
                    var instance = Expression.Parameter(typeof(TTarget), "instance");
                    Expression member = Member(instance, typeof(TTarget), memberName, write: false);
                    if (member.Type != typeof(TValue))
                        member = ConvertTo(member, typeof(TValue), memberName);
                    return Expression.Lambda<Func<TTarget, TValue>>(member, instance).Compile();
                });
        }

        /// <summary>
        /// Returns a cached, boxing-free setter for <typeparamref name="TValue"/> member
        /// <paramref name="memberName"/> on <typeparamref name="TTarget"/>.
        /// </summary>
        public static Action<TTarget, TValue> Setter<TTarget, TValue>(string memberName)
        {
            if (memberName is null) throw new ArgumentNullException(nameof(memberName));
            return (Action<TTarget, TValue>)TypedSetters.GetOrAdd(
                (typeof(TTarget), typeof(TValue), memberName),
                _ =>
                {
                    var instance = Expression.Parameter(typeof(TTarget), "instance");
                    var value = Expression.Parameter(typeof(TValue), "value");
                    MemberExpression member = Member(instance, typeof(TTarget), memberName, write: true);
                    Expression source = member.Type != typeof(TValue)
                        ? ConvertTo(value, member.Type, memberName)
                        : value;
                    // A null TValue (object, say) into a non-nullable value-type member: unboxing threw
                    // NullReferenceException; store the default, as the untyped setter does.
                    if (!typeof(TValue).IsValueType && member.Type.IsValueType && Nullable.GetUnderlyingType(member.Type) is null)
                        source = Expression.Condition(
                            Expression.Equal(value, Expression.Constant(null, typeof(TValue))), Expression.Default(member.Type), source);
                    Expression body = Expression.Assign(member, source);
                    return Expression.Lambda<Action<TTarget, TValue>>(body, instance, value).Compile();
                });
        }

        // A conversion between the member's type and TValue; none existing is the caller's type argument at fault.
        private static Expression ConvertTo(Expression value, Type type, string memberName)
        {
            try { return Expression.Convert(value, type); }
            catch (InvalidOperationException ex)
            {
                throw new ArgumentException($"Member '{memberName}' does not convert between {value.Type.Name} and {type.Name}.", "TValue", ex);
            }
        }

        private static Func<object, object?> BuildGetter(Type type, string memberName)
        {
            var param = Expression.Parameter(typeof(object), "instance");
            MemberExpression member = Member(Expression.Convert(param, type), type, memberName, write: false);
            var box = Expression.Convert(member, typeof(object));
            return Expression.Lambda<Func<object, object?>>(box, param).Compile();
        }

        private static Action<object, object?> BuildSetter(Type type, string memberName)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var value = Expression.Parameter(typeof(object), "value");
            // Unbox, not Convert, for a struct: Convert copied it out, so the write never reached the box.
            Expression target = type.IsValueType ? Expression.Unbox(instance, type) : Expression.Convert(instance, type);
            MemberExpression member = Member(target, type, memberName, write: true);
            Expression castValue = Expression.Convert(value, member.Type);
            if (member.Type.IsValueType && Nullable.GetUnderlyingType(member.Type) is null)
            {
                // Unboxing null would throw NullReferenceException; store default instead, as reflection does.
                castValue = Expression.Condition(
                    Expression.Equal(value, Expression.Constant(null)), Expression.Default(member.Type), castValue);
            }
            Expression body = Expression.Assign(member, castValue);
            return Expression.Lambda<Action<object, object?>>(body, instance, value).Compile();
        }

        // The public instance property (most derived, not an indexer) or else field named exactly memberName.
        private static MemberExpression Member(Expression instance, Type type, string memberName, bool write)
        {
            // An interface has no BaseType: its inherited members are on the interfaces it extends.
            var search = new List<Type>();
            for (Type? t = type; t != null; t = t.BaseType) search.Add(t);
            if (type.IsInterface) search.AddRange(type.GetInterfaces());
            foreach (Type t in search)
            {
                foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (p.Name != memberName || p.GetIndexParameters().Length != 0) continue;
                    MethodInfo? accessor = PublicAccessor(p, setter: write);
                    if (accessor is null)
                        throw new InvalidOperationException($"Member '{memberName}' is not {(write ? "writable" : "readable")}.");
                    return Expression.Property(instance, accessor);
                }
            }
            FieldInfo? f = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (f != null)
            {
                if (write && (f.IsInitOnly || f.IsLiteral))
                    throw new InvalidOperationException($"Member '{memberName}' is not writable.");
                return Expression.Field(instance, f);
            }
            throw new ArgumentException($"{type.Name} has no public instance property or field named '{memberName}'.", nameof(memberName));
        }

        // The public get (or set) accessor of p. An override that declares only the other accessor inherits this one from
        // the property it overrides, which reflection does not report on the override; a non-public accessor counts as none.
        private static MethodInfo? PublicAccessor(PropertyInfo p, bool setter)
        {
            for (PropertyInfo? q = p; q != null; q = OverriddenProperty(q))
            {
                MethodInfo? m = setter ? q.GetSetMethod(true) : q.GetGetMethod(true);
                if (m != null) return m.IsPublic ? m : null;
            }
            return null;
        }

        private static PropertyInfo? OverriddenProperty(PropertyInfo p)
        {
            MethodInfo? accessor = p.GetGetMethod(true) ?? p.GetSetMethod(true);
            if (accessor is null || accessor.GetBaseDefinition().DeclaringType == accessor.DeclaringType) return null;
            for (Type? t = p.DeclaringType!.BaseType; t != null; t = t.BaseType)
                foreach (PropertyInfo q in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (q.Name == p.Name && q.GetIndexParameters().Length == 0) return q;
            return null;
        }
    }
}
