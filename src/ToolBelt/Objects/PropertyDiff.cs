// ToolBelt drop-in — also copy Objects/ObjectMembers.cs and Objects/DeepEquals.cs (for ObjectComparisonOptions).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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
    /// <para>
    /// Sets (<see cref="ISet{T}"/>) that differ are reported as one difference at the set's path; multi-dimensional
    /// arrays of different shapes likewise; a struct's public fields are always compared (Quaternion, Matrix4x4 and value
    /// tuples keep their value there), and a struct with no readable members compares with its own Equals. A FileInfo or
    /// DirectoryInfo compares by type and full path. An object reached along several paths is reported on each path
    /// where it differs, but a pair found equal is not walked again. A property that returns a new object of its owner's
    /// type on every read (a struct's Perpendicular, a class's Reciprocal) is followed at most three levels along a path,
    /// since such a chain never meets the same object twice. The walk uses an explicit stack, so a deep graph cannot
    /// overflow the call stack. A property getter that throws propagates its own exception.
    /// </para>
    /// </summary>
    public static class PropertyDiff
    {
        // A path built lazily from its parent, so a deep walk does not copy ever-longer strings at every level.
        private sealed class PathNode
        {
            public PathNode(PathNode? parent, string segment, bool indexed)
            {
                Parent = parent;
                Segment = segment;
                Indexed = indexed;
            }

            public PathNode? Parent { get; }
            public string Segment { get; }
            public bool Indexed { get; }

            public override string ToString()
            {
                var parts = new List<PathNode>();
                for (PathNode? n = this; n != null; n = n.Parent) parts.Add(n);
                var sb = new StringBuilder();
                for (int i = parts.Count - 1; i >= 0; i--)
                {
                    PathNode n = parts[i];
                    if (n.Indexed) sb.Append('[').Append(n.Segment).Append(']');
                    else
                    {
                        if (sb.Length > 0) sb.Append('.');
                        sb.Append(n.Segment);
                    }
                }
                return sb.ToString();
            }
        }

        private enum Kind { Visit, Report, Exit }

        private struct Frame
        {
            public Kind Kind;
            public PathNode? Path;
            public object? A;
            public object? B;
            public int Reported; // for an Exit: the number of differences when the pair was entered
            public int Computed; // for a Visit: the computed members (ObjectMembers.Read) the path followed
        }

        public static IReadOnlyList<Difference> Compare(object? a, object? b, ObjectComparisonOptions? options = null)
        {
            var opt = options ?? new ObjectComparisonOptions();
            var result = new List<Difference>();
            // Pairs on the current path, with their depth among them; a pair met again below itself is a cycle and is not
            // descended. lows[d] is the shallowest depth a cycle from inside the open pair at depth d led back to.
            var visiting = new Dictionary<(object, object), int>(ObjectMembers.ReferencePairComparer.Instance);
            var lows = new List<int>();
            // Pairs whose walk found no difference. A shared object met again along another path is skipped: re-walking
            // it took time exponential in the depth of a graph whose nodes share children (equal diamond chains).
            var equal = new HashSet<(object, object)>(ObjectMembers.ReferencePairComparer.Instance);
            var work = new Stack<Frame>();
            work.Push(new Frame { Kind = Kind.Visit, A = a, B = b });
            var children = new List<Frame>();

            while (work.Count > 0)
            {
                Frame f = work.Pop();
                switch (f.Kind)
                {
                    case Kind.Exit:
                        visiting.Remove((f.A!, f.B!));
                        int low = lows[lows.Count - 1];
                        lows.RemoveAt(lows.Count - 1);
                        if (low < lows.Count)
                        {
                            // A cycle led back above this pair, so its walk stopped there: another path into the pair
                            // can still find differences, and it is not known equal.
                            if (lows.Count > 0) lows[lows.Count - 1] = Math.Min(lows[lows.Count - 1], low);
                        }
                        else if (result.Count == f.Reported) equal.Add((f.A!, f.B!));
                        break;
                    case Kind.Report:
                        result.Add(new Difference(f.Path?.ToString() ?? "", f.A, f.B));
                        break;
                    default:
                        children.Clear();
                        Visit(f.Path, f.A, f.B, f.Computed, opt, visiting, lows, equal, result.Count, children);
                        // Push in reverse so the children are handled in order, as a recursive walk would.
                        for (int i = children.Count - 1; i >= 0; i--) work.Push(children[i]);
                        break;
                }
            }
            return result;
        }

        // Emits, in order, the frames that handle this pair: reports, child visits, and an exit for a pair it enters.
        private static void Visit(PathNode? path, object? a, object? b, int computed, ObjectComparisonOptions opt,
            Dictionary<(object, object), int> visiting, List<int> lows, HashSet<(object, object)> equal, int reported,
            List<Frame> output)
        {
            if (ReferenceEquals(a, b)) return;
            if (a is null || b is null) { output.Add(Report(path, a, b)); return; }
            if (computed > ObjectMembers.MaxComputedDepth) return;
            object na = ObjectMembers.Normalize(a), nb = ObjectMembers.Normalize(b);

            Type ta = na.GetType(), tb = nb.GetType();
            if (ObjectMembers.IsLeaf(ta) || ObjectMembers.IsLeaf(tb))
            {
                if (!DeepEquals.LeafEquals(na, nb, opt)) output.Add(Report(path, a, b));
                return;
            }

            bool tracked = !ta.IsValueType && !tb.IsValueType;
            if (tracked)
            {
                if (equal.Contains((na, nb))) return; // already walked, and equal
                if (visiting.TryGetValue((na, nb), out int depth))
                {
                    // A cycle: not descended. Mark the open pair above as cut short at that depth.
                    lows[lows.Count - 1] = Math.Min(lows[lows.Count - 1], depth);
                    return;
                }
                visiting.Add((na, nb), lows.Count);
                lows.Add(int.MaxValue);
            }

            if (na is IDictionary da && nb is IDictionary db)
            {
                var keys = new List<object>();
                foreach (var k in da.Keys) keys.Add(k);
                foreach (var k in db.Keys) if (!da.Contains(k)) keys.Add(k);

                foreach (var key in keys)
                {
                    var p = new PathNode(path, Convert.ToString(key, CultureInfo.InvariantCulture) ?? "", indexed: true);
                    bool inA = da.Contains(key), inB = db.Contains(key);
                    if (inA && inB) output.Add(new Frame { Kind = Kind.Visit, Path = p, A = da[key], B = db[key], Computed = computed });
                    else output.Add(Report(p, inA ? da[key] : null, inB ? db[key] : null));
                }
            }
            else if (ObjectMembers.SetEquals(na, nb) is bool setsEqual)
            {
                if (!setsEqual) output.Add(Report(path, a, b));
            }
            else if (!ObjectMembers.SameArrayShape(na, nb))
            {
                output.Add(Report(path, a, b));
            }
            else if (na is IEnumerable ea && nb is IEnumerable eb)
            {
                var la = ObjectMembers.ToList(ea);
                var lb = ObjectMembers.ToList(eb);
                int max = Math.Max(la.Count, lb.Count);
                for (int i = 0; i < max; i++)
                {
                    var p = new PathNode(path, i.ToString(CultureInfo.InvariantCulture), indexed: true);
                    if (i >= la.Count) output.Add(Report(p, null, lb[i]));
                    else if (i >= lb.Count) output.Add(Report(p, la[i], null));
                    else output.Add(new Frame { Kind = Kind.Visit, Path = p, A = la[i], B = lb[i], Computed = computed });
                }
            }
            else if (ta != tb)
            {
                output.Add(Report(path, a, b));
            }
            else
            {
                var ma = new List<(string Name, object? Value, bool Computed)>(ObjectMembers.Read(na, opt.IncludeFields || ta.IsValueType));
                var mb = new List<(string Name, object? Value, bool Computed)>(ObjectMembers.Read(nb, opt.IncludeFields || ta.IsValueType));
                if (ma.Count == 0 && ta.IsValueType)
                {
                    if (!na.Equals(nb)) output.Add(Report(path, a, b));
                }
                else
                {
                    for (int i = 0; i < ma.Count; i++)
                        output.Add(new Frame
                        {
                            Kind = Kind.Visit, Path = new PathNode(path, ma[i].Name, indexed: false), A = ma[i].Value, B = mb[i].Value,
                            Computed = computed + (ma[i].Computed || mb[i].Computed ? 1 : 0),
                        });
                }
            }

            if (tracked) output.Add(new Frame { Kind = Kind.Exit, A = na, B = nb, Reported = reported });
        }

        private static Frame Report(PathNode? path, object? a, object? b) => new Frame { Kind = Kind.Report, Path = path, A = a, B = b };
    }
}
