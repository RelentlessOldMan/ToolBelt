// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ToolBelt.Objects
{
    /// <summary>Shared reflection helpers for the object-services utilities in this namespace.</summary>
    internal static class ObjectMembers
    {
        /// <summary>True for types treated as a single comparable/printable value rather than a graph to descend into.</summary>
        public static bool IsLeaf(Type type)
        {
            if (type.IsPrimitive || type.IsEnum) return true;
            return type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Guid);
        }

        /// <summary>Readable public instance properties (no indexers), optionally followed by public fields.</summary>
        public static IEnumerable<(string Name, object? Value)> Read(object obj, bool includeFields)
        {
            Type type = obj.GetType();
            foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.CanRead && p.GetIndexParameters().Length == 0)
                    yield return (p.Name, p.GetValue(obj));
            if (includeFields)
                foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    yield return (f.Name, f.GetValue(obj));
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
