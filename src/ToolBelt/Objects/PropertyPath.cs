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
    /// does. A value that does not fit an array's element type throws <see cref="ArgumentException"/>, as does null for a
    /// value-type member or element that is not nullable. A member name straight after ']' ("A[0]B") is an error. A property of a by-ref-like type (Span),
    /// which reflection cannot box, throws <see cref="ArgumentException"/> naming the segment. An index in quotes
    /// (<c>["a]b"]</c>) runs to its closing quote, so a key may hold ']' or '.'; quotes are removed only in a matching pair,
    /// and a doubled quote inside stands for one (<c>["say ""it's"""]</c>).
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
                // Reflection stored the default (0) for null, where a value of another type throws.
                if (value is null && p.PropertyType.IsValueType && Nullable.GetUnderlyingType(p.PropertyType) is null)
                    throw new ArgumentException($"Segment '{_name}' is a {p.PropertyType.Name}, which cannot hold null (path '{fullPath}').", nameof(value));
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
                    // Array.SetValue stores the default (0) for null in an int[]; a List<int> throws.
                    if (value is null && target is Array held && held.GetType().GetElementType() is Type element
                        && element.IsValueType && Nullable.GetUnderlyingType(element) is null)
                        throw new ArgumentException($"The elements of {target.GetType().Name} cannot hold null (path '{fullPath}').", nameof(value));
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
                    // An exact name first: with keys A and a, ignoring case alone made "[a]" read and write A.
                    if (underlying.IsEnum)
                    {
                        try { return Enum.Parse(underlying, token, ignoreCase: false); }
                        catch (ArgumentException) { return Enum.Parse(underlying, token, ignoreCase: true); }
                    }
                    if (underlying == typeof(Guid)) return Guid.Parse(token);
                    if (underlying == typeof(double)) return double.Parse(token, NumberStyles.Float, inv);
                    if (underlying == typeof(float)) return float.Parse(token, NumberStyles.Float, inv);
                    if (underlying == typeof(decimal)) return decimal.Parse(token, NumberStyles.Float, inv);
                    if (underlying == typeof(DateTime)) return DateTime.Parse(token, inv, DateTimeStyles.RoundtripKind);
                    if (underlying == typeof(DateTimeOffset)) return DateTimeOffset.Parse(token, inv, DateTimeStyles.RoundtripKind);
                    if (underlying == typeof(TimeSpan)) return TimeSpan.Parse(token, inv);
                    if (underlying == typeof(Version)) return Version.Parse(token);
                    if (underlying == typeof(Uri)) return new Uri(token, UriKind.RelativeOrAbsolute);
                    // ChangeType converts only IConvertible types, so a key KeyText printed of DateOnly, TimeOnly, Half,
                    // BigInteger, Int128, nint or a tuple could not be read back: such a type is read by its own Parse.
                    if (!typeof(IConvertible).IsAssignableFrom(underlying))
                    {
                        if (TryTuple(underlying, token, fullPath, out object? tuple)) return tuple!;
                        if (TryParseMethod(underlying, token, out object? parsed)) return parsed!;
                    }
                    return Convert.ChangeType(token, underlying, inv);
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException || ex is ArgumentException)
                {
                    throw new ArgumentException($"Key '[{token}]' does not convert to {keyType.Name} (path '{fullPath}').", "path", ex);
                }
            }

            // "(a, b)" as KeyText writes a Tuple or ValueTuple, each item read as a key of its type. An item whose own text
            // holds ", " or a parenthesis that does not balance cannot be split and does not convert.
            private static bool TryTuple(Type type, string token, string fullPath, out object? value)
            {
                value = null;
                if (!type.IsGenericType) return false;
                string? name = type.GetGenericTypeDefinition().FullName;
                if (name == null || !(name.StartsWith("System.ValueTuple`", StringComparison.Ordinal) || name.StartsWith("System.Tuple`", StringComparison.Ordinal)))
                    return false;
                Type[] itemTypes = type.GetGenericArguments();
                if (token.Length < 2 || token[0] != '(' || token[token.Length - 1] != ')')
                    throw new FormatException("A tuple key is written \"(a, b)\".");
                var parts = new List<string>();
                int depth = 0, start = 1;
                for (int i = 1; i < token.Length - 1; i++)
                {
                    char c = token[i];
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (depth == 0 && c == ',' && i + 1 < token.Length - 1 && token[i + 1] == ' ')
                    {
                        parts.Add(token.Substring(start, i - start));
                        start = i + 2;
                        i++;
                    }
                }
                parts.Add(token.Substring(start, token.Length - 1 - start));
                if (depth != 0 || parts.Count != itemTypes.Length)
                    throw new FormatException($"The tuple key has {parts.Count} items where {itemTypes.Length} were expected.");
                var items = new object?[parts.Count];
                for (int i = 0; i < items.Length; i++) items[i] = ConvertKey(itemTypes[i], parts[i], fullPath);
                value = Activator.CreateInstance(type, items);
                return true;
            }

            // A public static Parse of the type: (string, NumberStyles, IFormatProvider) with integer or, for Half, floating
            // styles (the bare form takes thousands separators), else (string, IFormatProvider), else (string).
            private static bool TryParseMethod(Type type, string token, out object? value)
            {
                value = null;
                var inv = CultureInfo.InvariantCulture;
                BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                object?[] args;
                MethodInfo? parse = type.GetMethod("Parse", flags, null, new[] { typeof(string), typeof(NumberStyles), typeof(IFormatProvider) }, null);
                if (parse != null)
                    args = new object?[] { token, type.FullName == "System.Half" ? NumberStyles.Float : NumberStyles.Integer, inv };
                else if ((parse = type.GetMethod("Parse", flags, null, new[] { typeof(string), typeof(IFormatProvider) }, null)) != null)
                    args = new object?[] { token, inv };
                else if ((parse = type.GetMethod("Parse", flags, null, new[] { typeof(string) }, null)) != null)
                    args = new object?[] { token };
                else return false;
                if (parse.ReturnType != type) return false;
                try { value = parse.Invoke(null, args); }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                }
                return true;
            }
        }

        // The quoted key that starts with the quote at open, as Flatten writes one: it runs to the next quote of the same
        // kind that is not doubled, and a doubled one stands for itself. The index of the closing quote, or -1.
        private static int ReadQuoted(string path, int open, out string key)
        {
            char q = path[open];
            var sb = new System.Text.StringBuilder();
            for (int i = open + 1; i < path.Length; i++)
            {
                if (path[i] != q) { sb.Append(path[i]); continue; }
                if (i + 1 < path.Length && path[i + 1] == q) { sb.Append(q); i++; continue; }
                key = sb.ToString();
                return i;
            }
            key = "";
            return -1;
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
                        // A quoted key runs to its closing quote, so it may hold ']' or '.', and a doubled quote is one.
                        int end = ReadQuoted(path, open, out token);
                        if (end < 0) throw new ArgumentException($"Unclosed quote in path '{path}'.", nameof(path));
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
                    // "Arr[0]X" was read as "Arr[0].X".
                    if (i < path.Length && path[i] != '.' && path[i] != '[')
                        throw new ArgumentException($"Path '{path}' needs '.' or '[' after ']' at position {i}.", nameof(path));
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
