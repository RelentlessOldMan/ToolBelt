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
    /// consumes. Reference cycles are broken (a repeated reference is not expanded again). A property that returns a new
    /// object of its owner's type on every read (a struct's Perpendicular, a class's Reciprocal) is expanded at most three
    /// levels along a path, and recorded as-is beyond. An empty collection or dictionary adds no key. The walk uses an
    /// explicit stack, so a deep graph cannot overflow the call stack; a property getter that throws propagates its own
    /// exception.
    /// </summary>
    public static class FlattenObject
    {
        public static IDictionary<string, object?> Flatten(object obj, string separator = ".", bool includeFields = false)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));
            if (string.IsNullOrEmpty(separator)) throw new ArgumentException("Separator must not be empty.", nameof(separator));

            var result = new Dictionary<string, object?>();
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            // Computed: how many computed members (ObjectMembers.Read) the path followed.
            var work = new Stack<(string Path, object? Value, int Computed)>();
            var children = new List<(string, object?, int)>();
            work.Push(("", obj, 0));
            while (work.Count > 0)
            {
                var (path, value, computed) = work.Pop();
                children.Clear();
                Walk(path, value, computed, result, separator, includeFields, visited, children);
                // Push in reverse so the children are handled in order, as a recursive walk would.
                for (int i = children.Count - 1; i >= 0; i--) work.Push(children[i]);
            }
            return result;
        }

        // Records a leaf, or lists the children to walk next.
        private static void Walk(string path, object? value, int computed, IDictionary<string, object?> result,
            string sep, bool includeFields, HashSet<object> visited, List<(string, object?, int)> children)
        {
            if (value is null || computed > ObjectMembers.MaxComputedDepth)
            {
                result[path] = value;
                return;
            }
            value = ObjectMembers.Normalize(value);

            Type type = value.GetType();
            if (ObjectMembers.IsLeaf(type))
            {
                result[path] = value;
                return;
            }

            // Guard against cycles for reference types.
            if (!type.IsValueType && !visited.Add(value))
            {
                result[path] = value; // already expanded elsewhere; record the reference as-is
                return;
            }

            if (value is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                    children.Add((Index(path, Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? ""), entry.Value, computed));
                return;
            }

            if (value is IEnumerable enumerable)
            {
                int i = 0;
                foreach (var item in enumerable)
                    children.Add((Index(path, i++.ToString(CultureInfo.InvariantCulture)), item, computed));
                return;
            }

            bool any = false;
            foreach (var (name, memberValue, isComputed) in ObjectMembers.Read(value, includeFields))
            {
                any = true;
                children.Add((path.Length == 0 ? name : path + sep + name, memberValue, computed + (isComputed ? 1 : 0)));
            }
            if (!any) result[path] = value; // no members to descend into; record as-is
        }

        private static string Index(string path, string token) => $"{path}[{token}]";

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
