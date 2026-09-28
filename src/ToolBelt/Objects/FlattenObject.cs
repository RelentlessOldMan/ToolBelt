// ToolBelt drop-in — fully self-contained (BCL only).
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
    /// consumes. Reference cycles are broken (a repeated reference is not expanded again).
    /// </summary>
    public static class FlattenObject
    {
        public static IDictionary<string, object?> Flatten(object obj, string separator = ".", bool includeFields = false)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));
            if (string.IsNullOrEmpty(separator)) throw new ArgumentException("Separator must not be empty.", nameof(separator));

            var result = new Dictionary<string, object?>();
            Walk("", obj, result, separator, includeFields,
                 new HashSet<object>(ReferenceEqualityComparer.Instance));
            return result;
        }

        private static void Walk(string path, object? value, IDictionary<string, object?> result,
            string sep, bool includeFields, HashSet<object> visited)
        {
            if (value is null)
            {
                result[path] = null;
                return;
            }

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
                    Walk(Index(path, Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? ""), entry.Value, result, sep, includeFields, visited);
                return;
            }

            if (value is IEnumerable enumerable)
            {
                int i = 0;
                foreach (var item in enumerable)
                    Walk(Index(path, i++.ToString(CultureInfo.InvariantCulture)), item, result, sep, includeFields, visited);
                return;
            }

            bool any = false;
            foreach (var (name, memberValue) in ObjectMembers.Read(value, includeFields))
            {
                any = true;
                Walk(path.Length == 0 ? name : path + sep + name, memberValue, result, sep, includeFields, visited);
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
