// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Concurrent;
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
    /// boxing entirely. Note that, like plain reflection, a setter invoked on a <b>value-type</b> instance
    /// mutates only the boxed/copied instance, not the original variable — these accessors are intended for
    /// reference types.</para>
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
                        member = Expression.Convert(member, typeof(TValue));
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
                        ? (Expression)Expression.Convert(value, member.Type)
                        : value;
                    Expression body = Expression.Assign(member, source);
                    return Expression.Lambda<Action<TTarget, TValue>>(body, instance, value).Compile();
                });
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
            MemberExpression member = Member(Expression.Convert(instance, type), type, memberName, write: true);
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
            for (Type? t = type; t != null; t = t.BaseType)
            {
                foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (p.Name != memberName || p.GetIndexParameters().Length != 0) continue;
                    if (write ? p.GetSetMethod() is null : p.GetGetMethod() is null)
                        throw new InvalidOperationException($"Member '{memberName}' is not {(write ? "writable" : "readable")}.");
                    return Expression.Property(instance, p);
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
    }
}
