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
        /// How many repeats (see <see cref="Trail"/>) a walk follows along one path before it stops descending.
        /// </summary>
        public const int MaxComputedDepth = 3;

        /// <summary>
        /// The value to walk in place of <paramref name="value"/>: a <c>Memory&lt;T&gt;</c> or <c>ReadOnlyMemory&lt;T&gt;</c>
        /// becomes an array of its contents (its own properties are only Length, IsEmpty and the unreadable Span), and a
        /// default <c>ImmutableArray&lt;T&gt;</c> or <c>ArraySegment&lt;T&gt;</c>, which throws when enumerated, becomes null.
        /// </summary>
        public static object? Normalize(object value)
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
                if (name == "System.Collections.Immutable.ImmutableArray`1"
                    && type.GetProperty("IsDefault")?.GetValue(value) is true) return null;
                if (name == "System.ArraySegment`1" && type.GetProperty("Array")?.GetValue(value) is null) return null;
            }
            return value;
        }

        /// <summary>How a member's value relates to a second read of the same member (see <see cref="Read"/>).</summary>
        public enum Origin
        {
            /// <summary>The same object again, a collection of the same items, a leaf or null: stored data.</summary>
            Stable,
            /// <summary>A struct copied out again with the same contents.</summary>
            Copy,
            /// <summary>A new object, or a struct holding new objects: made by the getter on each read.</summary>
            Fresh,
        }

        /// <summary>
        /// What a walk knows about the path to a value, to end a chain of getters that make a new object on every read
        /// (Ping.Next returning a new Pong whose Next returns a new Ping, or a struct whose property returns another
        /// struct whose property returns the first): such a chain never meets the same object twice, so no cycle check
        /// ends it. A fresh value whose type already appeared as a fresh value along the path, or a struct copy whose type
        /// already appeared in the unbroken run of struct copies above it, is a repeat, and a walk stops descending once a
        /// path has more than <see cref="MaxComputedDepth"/> repeats. Stored data never repeats: stored objects are
        /// stable, and a struct's fields cannot nest its own type.
        /// </summary>
        public sealed class Trail
        {
            private sealed class Link
            {
                public Link(Type type, Link? next) { Type = type; Next = next; }
                public readonly Type Type;
                public readonly Link? Next;
            }

            /// <summary>The trail to a walk's root.</summary>
            public static readonly Trail Start = new Trail(null, null, 0);

            private readonly Link? _fresh, _copies;

            private Trail(Link? fresh, Link? copies, int repeats)
            {
                _fresh = fresh;
                _copies = copies;
                Repeats = repeats;
            }

            /// <summary>Repeats along the path so far.</summary>
            public int Repeats { get; }

            /// <summary>True once the path has more repeats than a walk follows.</summary>
            public bool TooDeep => Repeats > MaxComputedDepth;

            /// <summary>The trail to <paramref name="value"/>, reached from here by a read of the given origin.</summary>
            public Trail Step(object? value, Origin origin)
            {
                if (value is null || IsLeaf(value.GetType()) || (origin == Origin.Stable && _copies is null)) return this;
                Type type = value.GetType();
                bool repeat = false;
                Link? fresh = _fresh, copies = null;
                if (origin == Origin.Fresh)
                {
                    repeat = Contains(fresh, type);
                    fresh = new Link(type, fresh);
                }
                else if (origin == Origin.Copy)
                {
                    repeat = Contains(_copies, type);
                    copies = new Link(type, _copies);
                }
                return new Trail(fresh, copies, Repeats + (repeat ? 1 : 0));
            }

            /// <summary>The trail of the two sides' paths together, for a walk over pairs: the more repeats, either side's types.</summary>
            public static Trail Pair(Trail parent, object? a, Origin oa, object? b, Origin ob)
            {
                Trail ta = parent.Step(a, oa), tb = parent.Step(b, ob);
                return ta.Repeats >= tb.Repeats ? ta : tb;
            }

            private static bool Contains(Link? link, Type type)
            {
                for (; link != null; link = link.Next)
                    if (link.Type == type) return true;
                return false;
            }
        }

        /// <summary>
        /// Readable public instance properties (no indexers, and none of a by-ref-like type such as Span, which reflection
        /// cannot box), optionally followed by public fields. A property is read through its public getter: an override
        /// declaring only a setter reads through the getter it inherits, and a private getter is not readable. A property
        /// hidden by a <c>new</c> one of the same name is not read; the most derived one is. A getter that throws
        /// propagates its own exception. A getter whose value is not a leaf is called a second time, to tell stored data
        /// from an object it makes on every read (the <see cref="Origin"/>, which a walk feeds to its <see cref="Trail"/>).
        /// </summary>
        public static IEnumerable<(string Name, object? Value, Origin Origin)> Read(object obj, bool includeFields)
        {
            Type type = obj.GetType();
            foreach (PropertyInfo p in Visible(type.GetProperties(BindingFlags.Public | BindingFlags.Instance)))
            {
                if (!IsReadableType(p.PropertyType)) continue;
                MethodInfo? getter = PublicAccessor(p, setter: false);
                if (getter is null) continue;
                object? value = Invoke(getter, obj);
                Origin origin = value is null || IsLeaf(value.GetType()) ? Origin.Stable : OriginOf(value, Invoke(getter, obj));
                yield return (p.Name, value, origin);
            }
            if (includeFields)
                foreach (FieldInfo f in VisibleFields(type.GetFields(BindingFlags.Public | BindingFlags.Instance)))
                    if (IsReadableType(f.FieldType))
                    {
                        object? value = f.GetValue(obj);
                        // A struct-typed field is boxed anew on each read: a copy of stored data.
                        Origin origin = value != null && f.FieldType.IsValueType && !IsLeaf(value.GetType()) ? Origin.Copy : Origin.Stable;
                        yield return (f.Name, value, origin);
                    }
        }

        // The properties left once each name keeps only its most derived declaration: reflection lists a property hidden
        // with `new` beside the one hiding it, and a walk read both under one name (two differences at one path, or the
        // base's value flattened over the derived one's). Indexers are dropped.
        private static List<PropertyInfo> Visible(PropertyInfo[] all)
        {
            var best = new Dictionary<string, int>(StringComparer.Ordinal);
            var list = new List<PropertyInfo>();
            foreach (PropertyInfo p in all)
            {
                if (p.GetIndexParameters().Length != 0) continue;
                if (!best.TryGetValue(p.Name, out int i)) { best[p.Name] = list.Count; list.Add(p); }
                else if (list[i].DeclaringType!.IsAssignableFrom(p.DeclaringType)) list[i] = p;
            }
            return InOrder(list);
        }

        private static List<FieldInfo> VisibleFields(FieldInfo[] all)
        {
            var best = new Dictionary<string, int>(StringComparer.Ordinal);
            var list = new List<FieldInfo>();
            foreach (FieldInfo f in all)
            {
                if (!best.TryGetValue(f.Name, out int i)) { best[f.Name] = list.Count; list.Add(f); }
                else if (list[i].DeclaringType!.IsAssignableFrom(f.DeclaringType)) list[i] = f;
            }
            return InOrder(list);
        }

        // A fixed order: most derived declaring type first, then declaration order. .NET Framework's reflection returned
        // members in one order on a type's first GetFields and another once its cache filled, so the two sides of a
        // compare, read one after the other, were paired member against the wrong member (a false difference).
        private static List<T> InOrder<T>(List<T> list) where T : MemberInfo
        {
            var keyed = new List<(int Depth, int Token, int Index, T Member)>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                int depth = 0;
                for (Type? t = list[i].DeclaringType; t != null; t = t.BaseType) depth++;
                keyed.Add((depth, list[i].MetadataToken, i, list[i]));
            }
            keyed.Sort((x, y) => x.Depth != y.Depth ? y.Depth.CompareTo(x.Depth)
                : x.Token != y.Token ? x.Token.CompareTo(y.Token) : x.Index.CompareTo(y.Index));
            var result = new List<T>(keyed.Count);
            foreach (var k in keyed) result.Add(k.Member);
            return result;
        }

        // Compares two reads of one getter. The same reference, a collection rebuilt over the same items, or a struct whose
        // contents are the same, is stored data (a struct is a copy of it); anything else the getter made.
        private static Origin OriginOf(object first, object? second)
        {
            if (ReferenceEquals(first, second)) return Origin.Stable;
            if (second is null || first.GetType() != second.GetType()) return Origin.Fresh;
            if (first.GetType().IsValueType) return Same(first, second, 0) ? Origin.Copy : Origin.Fresh;
            if (first is System.Collections.IEnumerable a && second is System.Collections.IEnumerable b)
            {
                List<object?> la = ToList(a), lb = ToList(b);
                if (la.Count != lb.Count) return Origin.Fresh;
                for (int i = 0; i < la.Count; i++)
                    if (!Same(la[i], lb[i], 0)) return Origin.Fresh;
                return Origin.Stable;
            }
            return Origin.Fresh;
        }

        // The same reference, an equal leaf, or two structs of one type whose fields are the same in turn.
        private static bool Same(object? a, object? b, int depth)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            Type type = a.GetType();
            if (type != b.GetType()) return false;
            if (IsLeaf(type)) return a.Equals(b);
            if (!type.IsValueType) return false;
            if (depth > 8) return true;
            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!IsReadableType(f.FieldType)) continue;
                if (!Same(f.GetValue(a), f.GetValue(b), depth + 1)) return false;
            }
            return true;
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
            // .NET Framework does not mark its own by-ref-like types with the attribute.
            if (type == typeof(TypedReference) || type.FullName == "System.ArgIterator" || type.FullName == "System.RuntimeArgumentHandle") return false;
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

        /// <summary>
        /// The items of a collection a walk descends into, and their <see cref="Origin"/>. A collection (anything with a
        /// count) holds its items: they are stable. A lazy sequence is enumerated a second time, since one stored in a
        /// property can make new objects on every pass (Kids => Select(i => new Node())), and a chain of those never
        /// meets the same object twice: its items are fresh unless both passes gave the same ones.
        /// </summary>
        public static List<object?> Items(System.Collections.IEnumerable items, out Origin origin)
        {
            List<object?> first = ToList(items);
            origin = Origin.Stable;
            if (first.Count == 0 || IsCollection(items.GetType())) return first;
            List<object?> second;
            try { second = ToList(items); }
            catch (InvalidOperationException) { origin = Origin.Fresh; return first; } // a sequence that runs only once
            catch (NotSupportedException) { origin = Origin.Fresh; return first; }
            if (second.Count != first.Count) origin = Origin.Fresh;
            else
                for (int i = 0; i < first.Count; i++)
                    if (!Same(first[i], second[i], 0)) { origin = Origin.Fresh; break; }
            return first;
        }

        private static bool IsCollection(Type type)
        {
            if (typeof(System.Collections.ICollection).IsAssignableFrom(type)) return true;
            foreach (Type i in type.GetInterfaces())
                if (i.IsGenericType)
                {
                    Type d = i.GetGenericTypeDefinition();
                    if (d == typeof(ICollection<>) || d == typeof(IReadOnlyCollection<>)) return true;
                }
            return false;
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
