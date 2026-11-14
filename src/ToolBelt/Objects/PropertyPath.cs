// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace ToolBelt.Objects
{
    /// <summary>
    /// Reads and writes a value through a dotted member path with optional indexers, for example
    /// <c>order.lines[2].price</c> or <c>settings["theme"]</c>. On failure it throws an error identifying
    /// the exact path segment that could not be resolved, rather than a null-reference somewhere inside.
    /// The single-level by-name accessor is the trivial special case.
    /// <para>
    /// A missing dictionary key throws <see cref="KeyNotFoundException"/>; an index token that does not convert to the
    /// dictionary's key type, or a list index out of range, throws <see cref="ArgumentException"/>. <see cref="TryGet"/>
    /// returns false for all of these; an exception thrown by a property getter itself still propagates. Setting through a
    /// struct-valued member writes the modified copy back, so <c>Set(holder, "Point.X", 5)</c> changes the holder. A struct
    /// root is written in the box it is passed in, as <see cref="PropertyInfo.SetValue(object, object)"/> does: keep that
    /// box, since a struct variable passed directly is boxed as a copy the caller never sees. A property overridden with
    /// only one accessor keeps the other it inherits; a non-public accessor does not count. A dictionary that implements
    /// only <c>IDictionary&lt;K, V&gt;</c> or <c>IReadOnlyDictionary&lt;K, V&gt;</c> (ExpandoObject) is indexed like any
    /// dictionary. A key converts to the dictionary's key type with the invariant culture and no thousands separators:
    /// numbers, enums, Guid, char, bool, DateTime and DateTimeOffset (any invariant form, including round-trip "O"),
    /// TimeSpan, Version and Uri. An array whose lower bound is not 0 is indexed from its lower bound. A name that a
    /// public field hides (a <c>new</c> field over a base property) is not a property. An empty segment ("A..B", ".A",
    /// "A.") is an error. An exception thrown by a list's or dictionary's own indexer propagates as itself, as a getter's
    /// does. A value that does not fit an array's element type throws <see cref="ArgumentException"/>. A property of a by-ref-like type (Span),
    /// which reflection cannot box, throws <see cref="ArgumentException"/> naming the segment. An index in quotes
    /// (<c>["a]b"]</c>) runs to its closing quote, so a key may hold ']' or '.'; quotes are removed only in a matching pair.
    /// </para>
    /// </summary>
    public static class PropertyPath
    {
        public static object? Get(object root, string path)
        {
            try { return GetCore(root, path); }
            catch (MemberThrew ex) { ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
        }

        // An accessor's own exception, carried past TryGet's filter and rethrown as itself.
        private sealed class MemberThrew : Exception
        {
            public MemberThrew(Exception inner) : base(inner.Message, inner) { }
        }

        private static object? Call(MethodInfo accessor, object target, object?[]? args)
        {
            try { return accessor.Invoke(target, args); }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { throw new MemberThrew(ex.InnerException); }
        }

        private static object? GetCore(object root, string path)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));
            var steps = Parse(path);
            object? current = root;
            for (int i = 0; i < steps.Count; i++)
            {
                if (current is null)
                    throw new InvalidOperationException($"Path '{path}' hit a null before segment '{steps[i]}'.");
                current = steps[i].Read(current, path);
            }
            return current;
        }

        public static bool TryGet(object root, string path, out object? value)
        {
            try { value = GetCore(root, path); return true; }
            catch (MemberThrew ex) { ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException
                                       || ex is FormatException || ex is OverflowException)
            {
                value = null;
                return false;
            }
        }

        public static void Set(object root, string path, object? value)
        {
            try { SetCore(root, path, value); }
            catch (MemberThrew ex) { ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
        }

        private static void SetCore(object root, string path, object? value)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));
            var steps = Parse(path);
            // targets[i] is the object segment i is read from (or, for the last segment, written to).
            var targets = new object[steps.Count];
            object? target = root;
            for (int i = 0; i < steps.Count - 1; i++)
            {
                if (target is null)
                    throw new InvalidOperationException($"Path '{path}' hit a null before segment '{steps[i]}'.");
                targets[i] = target;
                target = steps[i].Read(target, path);
            }
            if (target is null)
                throw new InvalidOperationException($"Path '{path}' hit a null before the final segment.");
            targets[steps.Count - 1] = target;
            steps[steps.Count - 1].Write(target, value, path);

            // A struct read along the way is a boxed copy: store each modified copy back into its parent.
            for (int i = steps.Count - 2; i >= 0 && targets[i + 1].GetType().IsValueType; i--)
                steps[i].Write(targets[i], targets[i + 1], path);
        }

        // ---- Path parsing ----

        private abstract class Step
        {
            public abstract object? Read(object target, string fullPath);
            public abstract void Write(object target, object? value, string fullPath);
        }

        private sealed class MemberStep : Step
        {
            private readonly string _name;
            public MemberStep(string name) => _name = name;
            public override string ToString() => _name;

            public override object? Read(object target, string fullPath)
            {
                PropertyInfo? p = FindProperty(target.GetType(), _name);
                if (p is null)
                    throw new ArgumentException($"Segment '{_name}' is not a public property of {target.GetType().Name} (path '{fullPath}').", "path");
                MethodInfo? getter = PublicAccessor(p, setter: false);
                if (getter is null)
                    throw new ArgumentException($"Segment '{_name}' is not a readable property of {target.GetType().Name} (path '{fullPath}').", "path");
                if (IsByRefLike(p.PropertyType))
                    throw new ArgumentException($"Segment '{_name}' is a {p.PropertyType.Name}, which reflection cannot read (path '{fullPath}').", "path");
                return Call(getter, target, null);
            }

            public override void Write(object target, object? value, string fullPath)
            {
                PropertyInfo? p = FindProperty(target.GetType(), _name);
                MethodInfo? setter = p is null ? null : PublicAccessor(p, setter: true);
                if (setter is null)
                    throw new ArgumentException($"Segment '{_name}' is not a writable property of {target.GetType().Name} (path '{fullPath}').", "path");
                if (IsByRefLike(p!.PropertyType))
                    throw new ArgumentException($"Segment '{_name}' is a {p.PropertyType.Name}, which reflection cannot write (path '{fullPath}').", "path");
                Call(setter, target, new[] { value });
            }

            private static bool IsByRefLike(Type type)
            {
                if (type.IsByRef || type.IsPointer) return true;
                if (!type.IsValueType) return false;
                // .NET Framework does not mark its own by-ref-like types with the attribute.
                if (type == typeof(TypedReference) || type.FullName == "System.ArgIterator" || type.FullName == "System.RuntimeArgumentHandle") return true;
                foreach (object a in type.GetCustomAttributes(false))
                    if (a.GetType().FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") return true;
                return false;
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

            // The most derived public, non-indexed property of that name (a "new" property hides its base's, which
            // GetProperty would report as ambiguous).
            private static PropertyInfo? FindProperty(Type type, string name)
            {
                for (Type? t = type; t != null; t = t.BaseType)
                {
                    foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        if (p.Name == name && p.GetIndexParameters().Length == 0) return p;
                    // A `new` field hides a base property of its name, as in C#: the hidden property was read and written.
                    if (t.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) != null) return null;
                }
                return null;
            }
        }

        private sealed class IndexStep : Step
        {
            private readonly string _token;
            public IndexStep(string token) => _token = token;
            public override string ToString() => $"[{_token}]";

            // The collection's own code (an indexer, Contains) is user code: what it throws propagates as itself, past TryGet's
            // filter, as a getter's exception does. Its InvalidOperationException or ArgumentException made TryGet return false.
            private static T User<T>(Func<T> call)
            {
                try { return call(); }
                catch (MemberThrew) { throw; }
                catch (Exception ex) { throw new MemberThrew(ex); }
            }

            public override object? Read(object target, string fullPath)
            {
                if (target is IDictionary dict)
                {
                    object key = ConvertKey(KeyType(dict.GetType()), _token, fullPath);
                    if (!User(() => dict.Contains(key)))
                        throw new KeyNotFoundException($"Key '[{_token}]' is not in the dictionary (path '{fullPath}').");
                    return User(() => dict[key]);
                }
                if (GenericDictionary(target.GetType()) is Type generic)
                {
                    object key = ConvertKey(generic.GetGenericArguments()[0], _token, fullPath);
                    var args = new object?[] { key, null };
                    if (!(bool)Call(generic.GetMethod("TryGetValue")!, target, args)!)
                        throw new KeyNotFoundException($"Key '[{_token}]' is not in the dictionary (path '{fullPath}').");
                    return args[1];
                }
                if (target is Array array && array.Rank == 1)
                {
                    int index = ParseIndex(array.GetLowerBound(0), array.Length, fullPath);
                    return array.GetValue(index);
                }
                if (target is IList list && !(target is Array))
                {
                    int index = ParseIndex(0, User(() => list.Count), fullPath);
                    return User(() => list[index]);
                }
                throw new ArgumentException($"Segment '[{_token}]' requires a list, a one-dimensional array or a dictionary, got {target.GetType().Name} (path '{fullPath}').", "path");
            }

            public override void Write(object target, object? value, string fullPath)
            {
                if (target is IDictionary dict) { dict[ConvertKey(KeyType(dict.GetType()), _token, fullPath)] = value; return; }
                if (GenericDictionary(target.GetType()) is Type generic)
                {
                    if (generic.GetGenericTypeDefinition() != typeof(IDictionary<,>))
                        throw new ArgumentException($"Segment '[{_token}]' is a read-only dictionary (path '{fullPath}').", "path");
                    Type[] kv = generic.GetGenericArguments();
                    object key = ConvertKey(kv[0], _token, fullPath);
                    if (value is null ? kv[1].IsValueType && Nullable.GetUnderlyingType(kv[1]) == null : !kv[1].IsInstanceOfType(value))
                        throw new ArgumentException($"The value does not fit the values of {target.GetType().Name} (path '{fullPath}').", nameof(value));
                    Call(generic.GetProperty("Item")!.GetSetMethod()!, target, new[] { key, value });
                    return;
                }
                if (target is IList list && (!(target is Array a) || a.Rank == 1))
                {
                    int index = target is Array arr ? ParseIndex(arr.GetLowerBound(0), arr.Length, fullPath) : ParseIndex(0, list.Count, fullPath);
                    try
                    {
                        if (target is Array one) one.SetValue(value, index);
                        else list[index] = value;
                    }
                    catch (Exception ex) when (ex is InvalidCastException || ex is ArrayTypeMismatchException)
                    {
                        throw new ArgumentException($"The value does not fit the elements of {target.GetType().Name} (path '{fullPath}').", nameof(value), ex);
                    }
                    return;
                }
                throw new ArgumentException($"Segment '[{_token}]' requires a list, a one-dimensional array or a dictionary, got {target.GetType().Name} (path '{fullPath}').", "path");
            }

            // Range-checked here: an array threw IndexOutOfRangeException, which is not an ArgumentException and escaped
            // TryGet. An array with lower bound 5 is indexed from 5 (it checked 0..Count-1, then the array threw).
            private int ParseIndex(int lower, int count, string fullPath)
            {
                if (!int.TryParse(_token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                    throw new ArgumentException($"Index '[{_token}]' is not an integer (path '{fullPath}').", "path");
                if (index < lower || (long)index - lower >= count)
                    throw new ArgumentOutOfRangeException("path", index, $"Index '[{_token}]' is out of range for {count} item(s) from {lower} (path '{fullPath}').");
                return index;
            }

            // The key type of the IDictionary<TKey, TValue> a non-generic dictionary also implements, or object.
            private static Type KeyType(Type type)
            {
                foreach (Type i in type.GetInterfaces())
                    if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                        return i.GetGenericArguments()[0];
                return typeof(object);
            }

            // IDictionary<K, V>, else IReadOnlyDictionary<K, V>, for a dictionary that is not an IDictionary.
            private static Type? GenericDictionary(Type type)
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

            private static object ConvertKey(Type keyType, string token, string fullPath)
            {
                if (keyType == typeof(string) || keyType == typeof(object)) return token;
                Type underlying = Nullable.GetUnderlyingType(keyType) ?? keyType;
                var inv = CultureInfo.InvariantCulture;
                try
                {
                    // ChangeType took thousands separators for a floating key ("1,5" read key 15) and had no route to
                    // TimeSpan, DateTimeOffset, Version or Uri.
                    if (underlying.IsEnum) return Enum.Parse(underlying, token, ignoreCase: true);
                    if (underlying == typeof(Guid)) return Guid.Parse(token);
                    if (underlying == typeof(double)) return double.Parse(token, NumberStyles.Float, inv);
                    if (underlying == typeof(float)) return float.Parse(token, NumberStyles.Float, inv);
                    if (underlying == typeof(decimal)) return decimal.Parse(token, NumberStyles.Float, inv);
                    if (underlying == typeof(DateTime)) return DateTime.Parse(token, inv, DateTimeStyles.RoundtripKind);
                    if (underlying == typeof(DateTimeOffset)) return DateTimeOffset.Parse(token, inv, DateTimeStyles.RoundtripKind);
                    if (underlying == typeof(TimeSpan)) return TimeSpan.Parse(token, inv);
                    if (underlying == typeof(Version)) return Version.Parse(token);
                    if (underlying == typeof(Uri)) return new Uri(token, UriKind.RelativeOrAbsolute);
                    return Convert.ChangeType(token, underlying, inv);
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException || ex is ArgumentException)
                {
                    throw new ArgumentException($"Key '[{token}]' does not convert to {keyType.Name} (path '{fullPath}').", "path", ex);
                }
            }
        }

        private static List<Step> Parse(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path must not be empty.", nameof(path));

            var steps = new List<Step>();
            int i = 0;
            while (i < path.Length)
            {
                if (path[i] == '.')
                {
                    // "A..B", ".A" and "A." named an empty member and were read as "A.B" and "A".
                    if (i == 0 || i + 1 >= path.Length || path[i + 1] == '.' || path[i + 1] == '[')
                        throw new ArgumentException($"Path '{path}' has an empty segment at position {i}.", nameof(path));
                    i++;
                    continue;
                }
                if (path[i] == '[')
                {
                    int open = i + 1;
                    while (open < path.Length && path[open] == ' ') open++;
                    string token;
                    int close;
                    if (open < path.Length && (path[open] == '"' || path[open] == '\''))
                    {
                        // A quoted key runs to its closing quote, so it may hold ']' or '.'.
                        int end = path.IndexOf(path[open], open + 1);
                        if (end < 0) throw new ArgumentException($"Unclosed quote in path '{path}'.", nameof(path));
                        token = path.Substring(open + 1, end - open - 1);
                        close = end + 1;
                        while (close < path.Length && path[close] == ' ') close++;
                        if (close >= path.Length || path[close] != ']')
                            throw new ArgumentException($"Expected ']' after the quoted key in path '{path}'.", nameof(path));
                    }
                    else
                    {
                        close = path.IndexOf(']', i);
                        if (close < 0) throw new ArgumentException($"Unclosed '[' in path '{path}'.", nameof(path));
                        token = path.Substring(i + 1, close - i - 1).Trim();
                    }
                    steps.Add(new IndexStep(token));
                    i = close + 1;
                }
                else
                {
                    int j = i;
                    while (j < path.Length && path[j] != '.' && path[j] != '[') j++;
                    steps.Add(new MemberStep(path.Substring(i, j - i)));
                    i = j;
                }
            }
            if (steps.Count == 0) throw new ArgumentException("Path must not be empty.", nameof(path));
            return steps;
        }
    }
}
