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
    /// where it differs, but a pair found equal is not walked again. The walk uses an explicit stack, so a deep graph
    /// cannot overflow the call stack. A property getter that throws propagates its exception.
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
        }

        public static IReadOnlyList<Difference> Compare(object? a, object? b, ObjectComparisonOptions? options = null)
        {
            var opt = options ?? new ObjectComparisonOptions();
            var result = new List<Difference>();
            // Pairs on the current path; a pair met again below itself is a cycle and is not descended.
            var visiting = new HashSet<(object, object)>(ObjectMembers.ReferencePairComparer.Instance);
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
                        if (result.Count == f.Reported) equal.Add((f.A!, f.B!));
                        break;
                    case Kind.Report:
                        result.Add(new Difference(f.Path?.ToString() ?? "", f.A, f.B));
                        break;
                    default:
                        children.Clear();
                        Visit(f.Path, f.A, f.B, opt, visiting, equal, result.Count, children);
                        // Push in reverse so the children are handled in order, as a recursive walk would.
                        for (int i = children.Count - 1; i >= 0; i--) work.Push(children[i]);
                        break;
                }
            }
            return result;
        }

        // Emits, in order, the frames that handle this pair: reports, child visits, and an exit for a pair it enters.
        private static void Visit(PathNode? path, object? a, object? b, ObjectComparisonOptions opt,
            HashSet<(object, object)> visiting, HashSet<(object, object)> equal, int reported, List<Frame> output)
        {
            if (ReferenceEquals(a, b)) return;
            if (a is null || b is null) { output.Add(Report(path, a, b)); return; }
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
                if (!visiting.Add((na, nb))) return; // cycle
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
                    if (inA && inB) output.Add(new Frame { Kind = Kind.Visit, Path = p, A = da[key], B = db[key] });
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
                    else output.Add(new Frame { Kind = Kind.Visit, Path = p, A = la[i], B = lb[i] });
                }
            }
            else if (ta != tb)
            {
                output.Add(Report(path, a, b));
            }
            else
            {
                var ma = new List<(string Name, object? Value)>(ObjectMembers.Read(na, opt.IncludeFields || ta.IsValueType));
                var mb = new List<(string Name, object? Value)>(ObjectMembers.Read(nb, opt.IncludeFields || ta.IsValueType));
                if (ma.Count == 0 && ta.IsValueType)
                {
                    if (!na.Equals(nb)) output.Add(Report(path, a, b));
                }
                else
                {
                    for (int i = 0; i < ma.Count; i++)
                        output.Add(new Frame { Kind = Kind.Visit, Path = new PathNode(path, ma[i].Name, indexed: false), A = ma[i].Value, B = mb[i].Value });
                }
            }

            if (tracked) output.Add(new Frame { Kind = Kind.Exit, A = na, B = nb, Reported = reported });
        }

        private static Frame Report(PathNode? path, object? a, object? b) => new Frame { Kind = Kind.Report, Path = path, A = a, B = b };
    }
}
