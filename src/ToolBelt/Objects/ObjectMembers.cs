// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
        /// and IPAddress / EndPoint, whose ScopeId or Address getters throw for some addresses. Also leaves, compared by
        /// their own Equals and never read: tasks, ValueTask and Lazy (reading Result or Value blocked forever on a pending
        /// task, or ran the factory), wait handles, cancellation tokens, threads and delegates, and streams, readers and
        /// writers (a MemoryStream's ReadTimeout throws).
        /// </summary>
        public static bool IsLeaf(Type type)
        {
            if (type.IsPrimitive || type.IsEnum) return true;
            if (IsOpaque(type)) return true;
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

        private static bool IsOpaque(Type type)
        {
            if (typeof(System.Threading.Tasks.Task).IsAssignableFrom(type)
                || typeof(System.Threading.WaitHandle).IsAssignableFrom(type)
                || type == typeof(System.Threading.CancellationToken)
                || typeof(System.Threading.Thread).IsAssignableFrom(type)
                || typeof(Delegate).IsAssignableFrom(type)
                || typeof(Stream).IsAssignableFrom(type)
                || typeof(TextReader).IsAssignableFrom(type)
                || typeof(TextWriter).IsAssignableFrom(type)) return true;
            string? name = type.FullName;
            if (name == "System.Threading.Tasks.ValueTask") return true;
            if (type.IsGenericType)
            {
                string? def = type.GetGenericTypeDefinition().FullName;
                if (def == "System.Threading.Tasks.ValueTask`1" || def == "System.Lazy`1" || def == "System.Lazy`2") return true;
            }
            return false;
        }

        /// <summary>
        /// The most objects one walk expands (a lazily built chain, Next => _next ??= new Node(), is stored data that never
        /// ends), and the most items it takes from one lazy sequence (an endless generator): past either, the walk throws
        /// <see cref="InvalidOperationException"/> rather than running until memory is gone.
        /// </summary>
        public const int MaxObjects = 1 << 21, MaxLazyItems = 1 << 22;

        /// <summary>The error for a walk past <see cref="MaxObjects"/>.</summary>
        public static InvalidOperationException TooManyObjects()
            => new InvalidOperationException($"The object graph has more than {MaxObjects.ToString("N0", CultureInfo.InvariantCulture)} objects to walk (it may be built lazily without end).");

        /// <summary>
        /// The text of a dictionary key in a path, the way <c>PropertyPath</c> parses it back: floating values in their
        /// shortest round-trip form (.NET Framework's default printed 15 digits, so 0.3 and 0.1 + 0.2 spelled one key),
        /// dates and offsets in the round-trip "O" form (the default dropped fractions of a second), TimeSpan in "c", and
        /// anything else invariant.
        /// </summary>
        public static string KeyText(object? key)
        {
            switch (key)
            {
                case null: return "";
                case double d:
                {
                    // .NET Framework prints -0.0 as "0"; .NET Core as "-0".
                    if (d == 0 && double.IsNegativeInfinity(1 / d)) return "-0";
                    string r = d.ToString("R", CultureInfo.InvariantCulture);
                    return double.IsNaN(d) || double.Parse(r, NumberStyles.Float, CultureInfo.InvariantCulture).Equals(d) ? r : d.ToString("G17", CultureInfo.InvariantCulture);
                }
                case float f:
                {
                    if (f == 0 && float.IsNegativeInfinity(1 / f)) return "-0";
                    string r = f.ToString("R", CultureInfo.InvariantCulture);
                    return float.IsNaN(f) || float.Parse(r, NumberStyles.Float, CultureInfo.InvariantCulture).Equals(f) ? r : f.ToString("G9", CultureInfo.InvariantCulture);
                }
                case DateTime dt: return dt.ToString("O", CultureInfo.InvariantCulture);
                case DateTimeOffset dto: return dto.ToString("O", CultureInfo.InvariantCulture);
                case TimeSpan ts: return ts.ToString("c", CultureInfo.InvariantCulture);
                default: return Convert.ToString(key, CultureInfo.InvariantCulture) ?? "";
            }
        }
        /// <summary>
        /// How many repeats (see <see cref="Trail"/>) a walk follows along one path before it stops descending.
        /// </summary>
        public const int MaxComputedDepth = 3;

        /// <summary>
        /// The value to walk in place of <paramref name="value"/>: a <c>Memory&lt;T&gt;</c> or <c>ReadOnlyMemory&lt;T&gt;</c>
        /// becomes an array of its contents (its own properties are only Length, IsEmpty and the unreadable Span), and a
        /// default <c>ImmutableArray&lt;T&gt;</c> or <c>ArraySegment&lt;T&gt;</c>, which throws when enumerated, becomes null.
        /// A dictionary that implements only the generic <c>IDictionary&lt;K, V&gt;</c> or
        /// <c>IReadOnlyDictionary&lt;K, V&gt;</c> (ExpandoObject) becomes an <see cref="IDictionary"/> view of itself, the
        /// same view each time: it was walked as a sequence of key-value pairs, in order, so two Expandos holding the same
        /// members in another order differed.
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
                    if (toArray != null) return Call(toArray, value, null)!;
                }
                if (name == "System.Collections.Immutable.ImmutableArray`1"
                    && type.GetProperty("IsDefault") is PropertyInfo isDefault && Call(isDefault.GetGetMethod()!, value, null) is true) return null;
                if (name == "System.ArraySegment`1" && type.GetProperty("Array") is PropertyInfo array && Call(array.GetGetMethod()!, value, null) is null) return null;
            }
            if (!(value is IDictionary) && GenericDictionary.Interface(type) != null)
                return Views.GetValue(value, v => new GenericDictionary(v));
            return value;
        }

        private static readonly ConditionalWeakTable<object, GenericDictionary> Views = new ConditionalWeakTable<object, GenericDictionary>();

        // A read-only IDictionary over a dictionary that implements only IDictionary<K, V> or IReadOnlyDictionary<K, V>,
        // looking keys up with that dictionary's own comparer.
        private sealed class GenericDictionary : IDictionary
        {
            private readonly object _source;
            private readonly MethodInfo _containsKey, _tryGetValue;
            private readonly PropertyInfo _count, _key, _value;
            private readonly Type _keyType;

            public GenericDictionary(object source)
            {
                _source = source;
                Type i = Interface(source.GetType())!;
                Type[] args = i.GetGenericArguments();
                _keyType = args[0];
                _containsKey = i.GetMethod("ContainsKey")!;
                _tryGetValue = i.GetMethod("TryGetValue")!;
                Type collection = typeof(IReadOnlyCollection<>).MakeGenericType(typeof(KeyValuePair<,>).MakeGenericType(args));
                if (!collection.IsInstanceOfType(source)) collection = typeof(ICollection<>).MakeGenericType(typeof(KeyValuePair<,>).MakeGenericType(args));
                _count = collection.GetProperty("Count")!;
                Type pair = typeof(KeyValuePair<,>).MakeGenericType(args);
                _key = pair.GetProperty("Key")!;
                _value = pair.GetProperty("Value")!;
            }

            // IDictionary<K, V> first, then IReadOnlyDictionary<K, V>; the first of either kind the type lists.
            public static Type? Interface(Type type)
            {
                Type? readOnly = null;
                foreach (Type i in type.GetInterfaces())
                {
                    if (!i.IsGenericType) continue;
                    Type d = i.GetGenericTypeDefinition();
                    if (d == typeof(IDictionary<,>)) return i;
                    if (d == typeof(IReadOnlyDictionary<,>) && readOnly is null) readOnly = i;
                }
                return readOnly;
            }

            private bool Fits(object key) => _keyType.IsInstanceOfType(key);

            public bool Contains(object key) => Fits(key) && (bool)Call(_containsKey, _source, new[] { key })!;

            public object? this[object key]
            {
                get
                {
                    if (!Fits(key)) return null;
                    var args = new object?[] { key, null };
                    return (bool)Call(_tryGetValue, _source, args)! ? args[1] : null;
                }
                set => throw new NotSupportedException();
            }

            public int Count => (int)Call(_count.GetGetMethod()!, _source, null)!;

            public IDictionaryEnumerator GetEnumerator()
            {
                var entries = new List<DictionaryEntry>();
                foreach (object? pair in (IEnumerable)_source)
                    entries.Add(new DictionaryEntry(Call(_key.GetGetMethod()!, pair!, null)!, Call(_value.GetGetMethod()!, pair!, null)));
                return new Entries(entries);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public ICollection Keys
            {
                get
                {
                    var keys = new List<object>();
                    var e = GetEnumerator();
                    while (e.MoveNext()) keys.Add(e.Key);
                    return keys;
                }
            }

            public ICollection Values
            {
                get
                {
                    var values = new List<object?>();
                    var e = GetEnumerator();
                    while (e.MoveNext()) values.Add(e.Value);
                    return values;
                }
            }

            public bool IsReadOnly => true;
            public bool IsFixedSize => true;
            public bool IsSynchronized => false;
            public object SyncRoot => this;
            public void Add(object key, object? value) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public void Remove(object key) => throw new NotSupportedException();
            public void CopyTo(Array array, int index) => throw new NotSupportedException();

            private sealed class Entries : IDictionaryEnumerator
            {
                private readonly List<DictionaryEntry> _entries;
                private int _i = -1;
                public Entries(List<DictionaryEntry> entries) => _entries = entries;
                public bool MoveNext() => ++_i < _entries.Count;
                public void Reset() => _i = -1;
                public DictionaryEntry Entry => _entries[_i];
                public object Current => Entry;
                public object Key => Entry.Key;
                public object? Value => Entry.Value;
            }
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

            // A generic type repeats by its definition: W<T>.Next returning a new W<W<T>> made a new type at every level, so
            // the chain never repeated and never ended.
            private static bool Contains(Link? link, Type type)
            {
                Type def = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
                for (; link != null; link = link.Next)
                    if (link.Type == type || (link.Type.IsGenericType && link.Type.GetGenericTypeDefinition() == def)) return true;
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
            List<PropertyInfo> properties = Visible(type.GetProperties(BindingFlags.Public | BindingFlags.Instance));
            List<FieldInfo> fields = includeFields ? VisibleFields(type.GetFields(BindingFlags.Public | BindingFlags.Instance)) : new List<FieldInfo>();
            if (fields.Count > 0)
            {
                // A field and a property of one name (a `new` field hiding a base property, or the reverse) are one member,
                // the most derived, as in C#: both were read under one name (a duplicate path, two differences at one path).
                var fieldByName = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                foreach (FieldInfo f in fields) fieldByName[f.Name] = f;
                var hiddenFields = new HashSet<string>(StringComparer.Ordinal);
                properties = properties.FindAll(p =>
                {
                    if (!fieldByName.TryGetValue(p.Name, out FieldInfo? f)) return true;
                    if (f.DeclaringType != p.DeclaringType && p.DeclaringType!.IsAssignableFrom(f.DeclaringType)) return false;
                    hiddenFields.Add(f.Name);
                    return true;
                });
                fields = fields.FindAll(f => !hiddenFields.Contains(f.Name));
            }
            foreach (PropertyInfo p in properties)
            {
                if (!IsReadableType(p.PropertyType)) continue;
                MethodInfo? getter = PublicAccessor(p, setter: false);
                if (getter is null) continue;
                object? value = Invoke(getter, obj);
                Origin origin = value is null || IsLeaf(value.GetType()) ? Origin.Stable : OriginOf(value, Invoke(getter, obj));
                yield return (p.Name, value, origin);
            }
            foreach (FieldInfo f in fields)
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
        private static object? Invoke(MethodInfo getter, object obj) => Call(getter, obj, null);

        private static object? Call(MethodInfo method, object obj, object?[]? args)
        {
            try { return method.Invoke(obj, args); }
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
                // A throwing comparer escaped as TargetInvocationException rather than its own exception.
                return (bool)Call(setEquals, a, new[] { b })!;
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

        /// <summary>The items of a sequence, materialized; past <see cref="MaxLazyItems"/> from a lazy one, an error.</summary>
        public static List<object?> ToList(System.Collections.IEnumerable items)
        {
            var list = new List<object?>();
            // An endless generator was read until memory ran out. A collection is finite: it knows its count.
            bool lazy = !IsCollection(items.GetType());
            foreach (object? item in items)
            {
                if (lazy && list.Count == MaxLazyItems)
                    throw new InvalidOperationException($"A lazy sequence ({items.GetType().Name}) gave more than {MaxLazyItems.ToString("N0", CultureInfo.InvariantCulture)} items (it may never end).");
                list.Add(item);
            }
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
