// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace ToolBelt.Objects
{
    /// <summary>Shared reflection helpers for the object-services utilities in this namespace.</summary>
    internal static class ObjectMembers
    {
        /// <summary>
        /// True for types treated as a single comparable/printable value rather than a graph to descend into: primitives,
        /// enums, string, decimal, the date/time types, Guid, any other formattable struct (BigInteger, Complex, DateOnly…,
        /// whose public properties do not determine the value), Uri, reflection objects (Type, MemberInfo, Assembly,
        /// Module), whose properties throw for many instances, and FileInfo / DirectoryInfo, whose Root, Parent and
        /// Directory getters build a new object on every call (a walk never met the same reference twice and never ended),
        /// and IPAddress / EndPoint, whose ScopeId or Address getters throw for some addresses.
        /// </summary>
        public static bool IsLeaf(Type type)
        {
            if (type.IsPrimitive || type.IsEnum) return true;
            return type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Guid)
                || (type.IsValueType && typeof(IFormattable).IsAssignableFrom(type))
                || type == typeof(Uri)
                || typeof(FileSystemInfo).IsAssignableFrom(type)
                || typeof(MemberInfo).IsAssignableFrom(type)
                || typeof(Assembly).IsAssignableFrom(type)
                || typeof(Module).IsAssignableFrom(type)
                || typeof(System.Net.IPAddress).IsAssignableFrom(type)
                || typeof(System.Net.EndPoint).IsAssignableFrom(type);
        }

        /// <summary>
        /// How many computed members (see <see cref="Read"/>) a walk follows along one path before it stops descending.
        /// </summary>
        public const int MaxComputedDepth = 3;

        /// <summary>
        /// The value to walk in place of <paramref name="value"/>: a <c>Memory&lt;T&gt;</c> or <c>ReadOnlyMemory&lt;T&gt;</c>
        /// becomes an array of its contents (its own properties are only Length, IsEmpty and the unreadable Span).
        /// </summary>
        public static object Normalize(object value)
        {
            Type type = value.GetType();
            if (type.IsValueType && type.IsGenericType)
            {
                string? name = type.GetGenericTypeDefinition().FullName;
                if (name == "System.Memory`1" || name == "System.ReadOnlyMemory`1")
                {
                    MethodInfo? toArray = type.GetMethod("ToArray", Type.EmptyTypes);
                    if (toArray != null) return toArray.Invoke(value, null)!;
                }
            }
            return value;
        }

        /// <summary>
        /// Readable public instance properties (no indexers, and none of a by-ref-like type such as Span, which reflection
        /// cannot box), optionally followed by public fields. A property is read through its public getter: an override
        /// declaring only a setter reads through the getter it inherits, and a private getter is not readable. A getter that
        /// throws propagates its own exception. <c>Computed</c> marks a property whose value is a new object of the owner's
        /// own type: a struct (which cannot contain itself) or a reference that a second read does not return again, such
        /// as a Perpendicular or Reciprocal property. Following those never meets the same object twice, so a walk follows
        /// at most <see cref="MaxComputedDepth"/> of them along a path.
        /// </summary>
        public static IEnumerable<(string Name, object? Value, bool Computed)> Read(object obj, bool includeFields)
        {
            Type type = obj.GetType();
            foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length != 0 || !IsReadableType(p.PropertyType)) continue;
                MethodInfo? getter = PublicAccessor(p, setter: false);
                if (getter is null) continue;
                object? value = Invoke(getter, obj);
                bool computed = value != null && value.GetType() == type
                    && (type.IsValueType || !ReferenceEquals(value, Invoke(getter, obj)));
                yield return (p.Name, value, computed);
            }
            if (includeFields)
                foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (IsReadableType(f.FieldType))
                        yield return (f.Name, f.GetValue(obj), false);
        }

        // Calls a getter, rethrowing what it threw rather than the TargetInvocationException reflection wraps it in.
        private static object? Invoke(MethodInfo getter, object obj)
        {
            try { return getter.Invoke(obj, null); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
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

        private static bool IsReadableType(Type type)
        {
            if (type.IsByRef || type.IsPointer) return false;
            if (!type.IsValueType) return true;
            foreach (object a in type.GetCustomAttributes(false))
                if (a.GetType().FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") return false;
            return true;
        }

        /// <summary>
        /// Whether two sets hold the same elements (by the first set's comparer), or null unless both implement the same
        /// <see cref="ISet{T}"/>.
        /// </summary>
        public static bool? SetEquals(object a, object b)
        {
            foreach (Type i in a.GetType().GetInterfaces())
            {
                if (!i.IsGenericType || i.GetGenericTypeDefinition() != typeof(ISet<>) || !i.IsInstanceOfType(b)) continue;
                MethodInfo setEquals = i.GetMethod(nameof(ISet<int>.SetEquals))!;
                return (bool)setEquals.Invoke(a, new[] { b })!;
            }
            return null;
        }

        /// <summary>False if either value is a multi-dimensional array and the other is not an array of the same shape.</summary>
        public static bool SameArrayShape(object a, object b)
        {
            var xa = a as Array;
            var xb = b as Array;
            bool multi = (xa != null && xa.Rank > 1) || (xb != null && xb.Rank > 1);
            if (!multi) return true;
            if (xa is null || xb is null || xa.Rank != xb.Rank) return false;
            for (int d = 0; d < xa.Rank; d++)
                if (xa.GetLength(d) != xb.GetLength(d)) return false;
            return true;
        }

        /// <summary>The items of a sequence, materialized.</summary>
        public static List<object?> ToList(System.Collections.IEnumerable items)
        {
            var list = new List<object?>();
            foreach (object? item in items) list.Add(item);
            return list;
        }

        /// <summary>A set keyed by reference identity, for cycle detection over object pairs.</summary>
        public sealed class ReferencePairComparer : IEqualityComparer<(object, object)>
        {
            public static readonly ReferencePairComparer Instance = new ReferencePairComparer();

            public bool Equals((object, object) x, (object, object) y)
                => ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);

            public int GetHashCode((object, object) pair)
                => unchecked(RuntimeHelpers.GetHashCode(pair.Item1) * 397 ^ RuntimeHelpers.GetHashCode(pair.Item2));
        }
    }
}
