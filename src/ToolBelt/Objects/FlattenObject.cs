// ToolBelt drop-in — also copy Objects/ObjectMembers.cs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace ToolBelt.Objects
{
    /// <summary>
    /// Flattens an object graph into a dictionary of dotted/indexed paths to leaf values, e.g.
    /// <c>{ "user.name": "Ada", "user.roles[0]": "admin" }</c>. This is what turns an arbitrary object into
    /// something dumpable to a table, a CSV row or a structured log line, and what a configuration binder
    /// consumes. Reference cycles are broken (a repeated reference is not expanded again). A chain of getters that make a
    /// new object on every read (a struct's Perpendicular, a class's Reciprocal, a Ping whose Next is a new Pong whose
    /// Next is a new Ping) is expanded only until its types have repeated three times along the path, and recorded as-is
    /// beyond; a getter whose value is not a leaf is called twice, and a lazy sequence enumerated twice, to tell. An empty collection or dictionary, and an
    /// object with no readable members, is recorded as-is. Two values whose paths spell the same key (a member
    /// <c>A_B</c> beside <c>A.B</c> with separator "_", or dictionary keys 1 and "1") throw rather than one silently
    /// replacing the other. A leaf or null root flattens to the single key "", which <c>UnflattenObject</c> refuses: it
    /// rebuilds a tree of named members, and a root value has none. The walk uses an explicit stack, so a deep graph cannot overflow the call stack; a property
    /// getter that throws propagates its own exception. A dictionary that implements only the generic interfaces
    /// (ExpandoObject) flattens by key, a key prints as <c>PropertyPath</c> parses it back (shortest round-trip number,
    /// "O" for a date), and tasks, Lazy values, streams and delegates are recorded as-is, never read. A graph of more than
    /// 2,097,152 objects, keys of more than 2^29 characters in all, or a lazy sequence of more than 4,194,304 items
    /// throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public static class FlattenObject
    {
        public static IDictionary<string, object?> Flatten(object obj, string separator = ".", bool includeFields = false)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));
            if (string.IsNullOrEmpty(separator)) throw new ArgumentException("Separator must not be empty.", nameof(separator));

            var result = new Dictionary<string, object?>();
            long keyChars = 0;
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            // Trail: what the path followed (ObjectMembers.Trail), to end chains of made objects.
            var work = new Stack<(string Path, object? Value, ObjectMembers.Trail Trail)>();
            var children = new List<(string, object?, ObjectMembers.Trail)>();
            work.Push(("", obj, ObjectMembers.Trail.Start));
            while (work.Count > 0)
            {
                var (path, value, trail) = work.Pop();
                // Each key spells its whole path, so a deep chain's keys grow with the square of its depth: a lazily built
                // endless chain filled memory before the object budget was reached.
                keyChars += path.Length;
                if (keyChars > MaxKeyChars)
                    throw new InvalidOperationException($"The flattened keys would hold more than {MaxKeyChars.ToString("N0", CultureInfo.InvariantCulture)} characters (the graph is too deep, or built lazily without end).");
                children.Clear();
                Walk(path, value, trail, result, separator, includeFields, visited, children);
                // Push in reverse so the children are handled in order, as a recursive walk would.
                for (int i = children.Count - 1; i >= 0; i--) work.Push(children[i]);
            }
            return result;
        }

        // Records a leaf, or lists the children to walk next.
        private static void Walk(string path, object? value, ObjectMembers.Trail trail, Dictionary<string, object?> result,
            string sep, bool includeFields, HashSet<object> visited, List<(string, object?, ObjectMembers.Trail)> children)
        {
            if (value is null || trail.TooDeep)
            {
                Record(result, path, value);
                return;
            }
            object? normalized = ObjectMembers.Normalize(value);
            if (normalized is null)
            {
                Record(result, path, value); // a default ImmutableArray or ArraySegment, which cannot be enumerated
                return;
            }
            // A generic-only dictionary is walked through its IDictionary view, but recorded as itself.
            object shown = normalized is IDictionary && !(value is IDictionary) ? value : normalized;
            value = normalized;

            Type type = value.GetType();
            if (ObjectMembers.IsLeaf(type))
            {
                Record(result, path, value);
                return;
            }

            // Guard against cycles, boxes included: a boxed struct can reach itself through an object-typed member.
            if (!visited.Add(value))
            {
                Record(result, path, shown); // already expanded elsewhere; record the reference as-is
                return;
            }
            if (visited.Count > ObjectMembers.MaxObjects) throw ObjectMembers.TooManyObjects();

            int before = children.Count;
            if (value is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                    children.Add((Index(path, ObjectMembers.KeyText(entry.Key)), entry.Value,
                        trail.Step(entry.Value, ObjectMembers.Origin.Stable)));
            }
            else if (value is IEnumerable enumerable)
            {
                List<object?> items = ObjectMembers.Items(enumerable, out ObjectMembers.Origin origin);
                for (int i = 0; i < items.Count; i++)
                    children.Add((Index(path, i.ToString(CultureInfo.InvariantCulture)), items[i], trail.Step(items[i], origin)));
            }
            else
            {
                foreach (var (name, memberValue, origin) in ObjectMembers.Read(value, includeFields))
                    children.Add((path.Length == 0 ? name : path + sep + name, memberValue, trail.Step(memberValue, origin)));
            }
            // An empty collection, or an object with no members to descend into, is recorded as-is, so Unflatten keeps it.
            if (children.Count == before) Record(result, path, shown);
        }

        private static void Record(Dictionary<string, object?> result, string path, object? value)
        {
            if (result.ContainsKey(path))
                throw new InvalidOperationException($"Two values flatten to the same path '{path}' (a member name or key contains the separator or brackets, or two keys print alike).");
            result.Add(path, value);
        }

        /// <summary>The most characters the keys of one result may hold in all (each key spells its whole path).</summary>
        internal const long MaxKeyChars = 1L << 29;

        private static string Index(string path, string token) => $"{path}[{token}]";

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
