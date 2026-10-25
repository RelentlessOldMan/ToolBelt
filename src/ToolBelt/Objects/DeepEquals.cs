// ToolBelt drop-in — also copy Objects/ObjectMembers.cs.
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
    /// <para>
    /// Sets (<see cref="ISet{T}"/>) compare as sets, using the first set's element comparer; multi-dimensional arrays
    /// must have the same shape; a struct's public fields are always compared, since many structs keep their value there
    /// (Quaternion, Matrix4x4, value tuples), and a struct with no readable members compares with its own Equals. A
    /// <see cref="System.IO.FileSystemInfo"/> compares by type and full path (its Root, Parent and Directory getters
    /// return a new object every call, which no cycle check can end).
    /// The walk uses an explicit stack, so a deep graph (a long linked list) cannot overflow the call stack. A property
    /// getter that throws propagates its exception.
    /// </para>
    /// </summary>
    public static class DeepEquals
    {
        public static bool Equals(object? a, object? b, ObjectComparisonOptions? options = null)
        {
            var opt = options ?? new ObjectComparisonOptions();
            // Pairs already taken up are assumed equal: any difference reachable from them is found through their
            // first visit. That also ends cycles, through objects and collections alike.
            var seen = new HashSet<(object, object)>(ObjectMembers.ReferencePairComparer.Instance);
            var work = new Stack<(object? A, object? B)>();
            work.Push((a, b));
            while (work.Count > 0)
            {
                var (x, y) = work.Pop();
                if (!Step(x, y, opt, seen, work)) return false;
            }
            return true;
        }

        // Compares what can be decided at this pair and pushes the child pairs; false on a difference.
        private static bool Step(object? a, object? b, ObjectComparisonOptions opt,
            HashSet<(object, object)> seen, Stack<(object?, object?)> work)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            a = ObjectMembers.Normalize(a);
            b = ObjectMembers.Normalize(b);

            Type ta = a.GetType(), tb = b.GetType();
            if (ObjectMembers.IsLeaf(ta) || ObjectMembers.IsLeaf(tb))
                return LeafEquals(a, b, opt);

            if (!ta.IsValueType && !tb.IsValueType && !seen.Add((a, b))) return true;

            if (a is IDictionary da && b is IDictionary db)
            {
                if (da.Count != db.Count) return false;
                foreach (DictionaryEntry entry in da)
                {
                    if (!db.Contains(entry.Key)) return false;
                    work.Push((entry.Value, db[entry.Key]));
                }
                return true;
            }

            bool? setsEqual = ObjectMembers.SetEquals(a, b);
            if (setsEqual.HasValue) return setsEqual.Value;
            if (!ObjectMembers.SameArrayShape(a, b)) return false;

            if (a is IEnumerable ea && b is IEnumerable eb)
            {
                var la = ObjectMembers.ToList(ea);
                var lb = ObjectMembers.ToList(eb);
                if (la.Count != lb.Count) return false;
                for (int i = la.Count - 1; i >= 0; i--) work.Push((la[i], lb[i]));
                return true;
            }

            if (ta != tb) return false;

            // A struct's public fields are always read: Quaternion and Matrix4x4 keep their value there, and their
            // properties alone (IsIdentity) made every pair of quaternions equal.

            var ma = new List<(string Name, object? Value)>(ObjectMembers.Read(a, opt.IncludeFields || ta.IsValueType));
            var mb = new List<(string Name, object? Value)>(ObjectMembers.Read(b, opt.IncludeFields || ta.IsValueType));
            if (ma.Count == 0 && ta.IsValueType) return a.Equals(b);
            for (int i = ma.Count - 1; i >= 0; i--) work.Push((ma[i].Value, mb[i].Value));
            return true;
        }

        internal static bool LeafEquals(object a, object b, ObjectComparisonOptions opt)
        {
            // FileSystemInfo does not override Equals: two instances for one path were never equal.
            if (a is System.IO.FileSystemInfo fsa && b is System.IO.FileSystemInfo fsb)
                return a.GetType() == b.GetType() && string.Equals(fsa.FullName, fsb.FullName, StringComparison.Ordinal);
            if (opt.FloatTolerance > 0)
            {
                // Equals first: infinities and NaN equal themselves, but their difference is NaN.
                if (a is double da && b is double db) return da.Equals(db) || Math.Abs(da - db) <= opt.FloatTolerance;
                if (a is float fa && b is float fb) return fa.Equals(fb) || Math.Abs(fa - fb) <= opt.FloatTolerance;
            }
            return a.Equals(b);
        }
    }
}
