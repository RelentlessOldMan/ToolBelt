// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace ToolBelt.Objects
{
    /// <summary>A single difference between two object graphs, located by a dotted/indexed path.</summary>
    public sealed class Difference
    {
        internal Difference(string path, object? oldValue, object? newValue)
        {
            Path = path;
            OldValue = oldValue;
            NewValue = newValue;
        }

        public string Path { get; }
        public object? OldValue { get; }
        public object? NewValue { get; }

        public override string ToString() => $"{Path}: {OldValue ?? "null"} -> {NewValue ?? "null"}";
    }

    /// <summary>
    /// Enumerates the differences between two instances as a list of (path, old value, new value),
    /// descending into nested objects, collections and dictionaries. The engine behind change logs, audit
    /// trails and "what did the operator actually modify" reports, with the nesting and collection handling
    /// hand-rolled versions omit.
    /// </summary>
    public static class PropertyDiff
    {
        public static IReadOnlyList<Difference> Compare(object? a, object? b, ObjectComparisonOptions? options = null)
        {
            var result = new List<Difference>();
            Diff("", a, b, options ?? new ObjectComparisonOptions(), result,
                 new HashSet<(object, object)>(ObjectMembers.ReferencePairComparer.Instance));
            return result;
        }

        private static void Diff(string path, object? a, object? b, ObjectComparisonOptions opt,
            List<Difference> result, HashSet<(object, object)> visiting)
        {
            if (ReferenceEquals(a, b)) return;
            if (a is null || b is null) { result.Add(new Difference(path, a, b)); return; }

            Type ta = a.GetType(), tb = b.GetType();

            if (ObjectMembers.IsLeaf(ta) || ObjectMembers.IsLeaf(tb))
            {
                if (!LeafEquals(a, b, opt)) result.Add(new Difference(path, a, b));
                return;
            }

            if (a is IDictionary da && b is IDictionary db) { DiffDictionary(path, da, db, opt, result, visiting); return; }
            if (a is IEnumerable ea && b is IEnumerable eb) { DiffSequence(path, ea, eb, opt, result, visiting); return; }

            if (ta != tb) { result.Add(new Difference(path, a, b)); return; }

            var pair = (a, b);
            if (!visiting.Add(pair)) return; // cycle
            try
            {
                using var ma = ObjectMembers.Read(a, opt.IncludeFields).GetEnumerator();
                using var mb = ObjectMembers.Read(b, opt.IncludeFields).GetEnumerator();
                while (ma.MoveNext() & mb.MoveNext())
                    Diff(Join(path, ma.Current.Name), ma.Current.Value, mb.Current.Value, opt, result, visiting);
            }
            finally
            {
                visiting.Remove(pair);
            }
        }

        private static void DiffDictionary(string path, IDictionary a, IDictionary b, ObjectComparisonOptions opt,
            List<Difference> result, HashSet<(object, object)> visiting)
        {
            var keys = new List<object>();
            foreach (var k in a.Keys) keys.Add(k);
            foreach (var k in b.Keys) if (!a.Contains(k)) keys.Add(k);

            foreach (var key in keys)
            {
                string p = $"{path}[{Convert.ToString(key, CultureInfo.InvariantCulture)}]";
                bool inA = a.Contains(key), inB = b.Contains(key);
                if (inA && inB) Diff(p, a[key], b[key], opt, result, visiting);
                else result.Add(new Difference(p, inA ? a[key] : null, inB ? b[key] : null));
            }
        }

        private static void DiffSequence(string path, IEnumerable a, IEnumerable b, ObjectComparisonOptions opt,
            List<Difference> result, HashSet<(object, object)> visiting)
        {
            var la = new List<object?>();
            var lb = new List<object?>();
            foreach (var x in a) la.Add(x);
            foreach (var x in b) lb.Add(x);

            int max = Math.Max(la.Count, lb.Count);
            for (int i = 0; i < max; i++)
            {
                string p = $"{path}[{i}]";
                if (i >= la.Count) result.Add(new Difference(p, null, lb[i]));
                else if (i >= lb.Count) result.Add(new Difference(p, la[i], null));
                else Diff(p, la[i], lb[i], opt, result, visiting);
            }
        }

        private static bool LeafEquals(object a, object b, ObjectComparisonOptions opt)
        {
            if (opt.FloatTolerance > 0)
            {
                if (a is double da && b is double db) return Math.Abs(da - db) <= opt.FloatTolerance;
                if (a is float fa && b is float fb) return Math.Abs(fa - fb) <= opt.FloatTolerance;
            }
            return a.Equals(b);
        }

        private static string Join(string path, string name) => path.Length == 0 ? name : path + "." + name;
    }
}
