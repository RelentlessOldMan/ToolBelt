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
    /// Works on public instance properties and fields.
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
                    Expression member = Expression.PropertyOrField(instance, memberName);
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
                    MemberExpression member = Expression.PropertyOrField(instance, memberName);
                    Expression source = member.Type != typeof(TValue)
                        ? (Expression)Expression.Convert(value, member.Type)
                        : value;
                    Expression body = BuildAssign(member, source, memberName);
                    return Expression.Lambda<Action<TTarget, TValue>>(body, instance, value).Compile();
                });
        }

        private static Func<object, object?> BuildGetter(Type type, string memberName)
        {
            var param = Expression.Parameter(typeof(object), "instance");
            MemberExpression member = Expression.PropertyOrField(Expression.Convert(param, type), memberName);
            var box = Expression.Convert(member, typeof(object));
            return Expression.Lambda<Func<object, object?>>(box, param).Compile();
        }

        private static Action<object, object?> BuildSetter(Type type, string memberName)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var value = Expression.Parameter(typeof(object), "value");
            MemberExpression member = Expression.PropertyOrField(Expression.Convert(instance, type), memberName);
            var castValue = Expression.Convert(value, member.Type);
            Expression body = BuildAssign(member, castValue, memberName);
            return Expression.Lambda<Action<object, object?>>(body, instance, value).Compile();
        }

        private static Expression BuildAssign(MemberExpression member, Expression source, string memberName)
        {
            try
            {
                return Expression.Assign(member, source);
            }
            catch (ArgumentException ex)
            {
                // Read-only property (no setter) or init-only / readonly field.
                throw new InvalidOperationException($"Member '{memberName}' is not writable.", ex);
            }
        }
    }
}
