// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;

namespace ToolBelt.Objects
{
    /// <summary>Options controlling structural equality and diffing.</summary>
    public sealed class ObjectComparisonOptions
    {
        /// <summary>Absolute tolerance for comparing <see cref="double"/>/<see cref="float"/> values. Default 0 (exact).</summary>
        public double FloatTolerance { get; set; }

        /// <summary>Also compare public instance fields, not just properties. Default false.</summary>
        public bool IncludeFields { get; set; }
    }

    /// <summary>
    /// Structural (deep) equality over object graphs: descends public properties (optionally fields),
    /// compares collections and dictionaries by content, handles reference cycles, and supports a
    /// floating-point tolerance. The twin of a deep cloner — clone an object, assert deep-equal, mutate one
    /// member, assert not-equal, and the two features validate each other.
    /// </summary>
    public static class DeepEquals
    {
        public static bool Equals(object? a, object? b, ObjectComparisonOptions? options = null)
            => Compare(a, b, options ?? new ObjectComparisonOptions(),
                       new HashSet<(object, object)>(ObjectMembers.ReferencePairComparer.Instance));

        private static bool Compare(object? a, object? b, ObjectComparisonOptions opt, HashSet<(object, object)> visiting)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;

            Type ta = a.GetType(), tb = b.GetType();

            if (ObjectMembers.IsLeaf(ta) || ObjectMembers.IsLeaf(tb))
                return LeafEquals(a, b, opt);

            if (a is IDictionary da && b is IDictionary db)
                return DictionaryEquals(da, db, opt, visiting);

            if (a is IEnumerable ea && b is IEnumerable eb)
                return SequenceEquals(ea, eb, opt, visiting);

            if (ta != tb) return false;

            var pair = (a, b);
            if (!visiting.Add(pair)) return true; // already comparing this pair on the stack (cycle)
            try
            {
                using var ma = ObjectMembers.Read(a, opt.IncludeFields).GetEnumerator();
                using var mb = ObjectMembers.Read(b, opt.IncludeFields).GetEnumerator();
                while (ma.MoveNext() & mb.MoveNext())
                    if (!Compare(ma.Current.Value, mb.Current.Value, opt, visiting))
                        return false;
                return true;
            }
            finally
            {
                visiting.Remove(pair);
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

        private static bool DictionaryEquals(IDictionary a, IDictionary b, ObjectComparisonOptions opt, HashSet<(object, object)> visiting)
        {
            if (a.Count != b.Count) return false;
            foreach (DictionaryEntry entry in a)
            {
                if (!b.Contains(entry.Key)) return false;
                if (!Compare(entry.Value, b[entry.Key], opt, visiting)) return false;
            }
            return true;
        }

        private static bool SequenceEquals(IEnumerable a, IEnumerable b, ObjectComparisonOptions opt, HashSet<(object, object)> visiting)
        {
            IEnumerator ea = a.GetEnumerator(), eb = b.GetEnumerator();
            try
            {
                while (true)
                {
                    bool na = ea.MoveNext(), nb = eb.MoveNext();
                    if (na != nb) return false;      // different lengths
                    if (!na) return true;            // both ended
                    if (!Compare(ea.Current, eb.Current, opt, visiting)) return false;
                }
            }
            finally
            {
                (ea as IDisposable)?.Dispose();
                (eb as IDisposable)?.Dispose();
            }
        }
    }
}
