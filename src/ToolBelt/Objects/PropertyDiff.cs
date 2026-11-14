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

        /// <summary>"Path: old -> new", values in the invariant culture (a double in its shortest round-trip form).</summary>
        public override string ToString() => $"{Path}: {Text(OldValue)} -> {Text(NewValue)}";

        // The current culture printed 1.5 as "1,5", and .NET Framework printed 0.3 and 0.1 + 0.2 alike.
        private static string Text(object? value) => value switch
        {
            null => "null",
            double _ => ObjectMembers.KeyText(value),
            float _ => ObjectMembers.KeyText(value),
            IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
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
    /// DirectoryInfo compares by type and full path. A pair of objects reached along several paths is compared once, on
    /// the first path that reaches it, and its differences are reported there only (reporting them on every path made the
    /// output, and the time, grow exponentially with the depth of a graph whose nodes share children). A chain of getters that make a new object on every read
    /// (a struct's Perpendicular, a class's Reciprocal, a Ping whose Next is a new Pong whose Next is a new Ping) never
    /// meets the same object twice, so it is followed only until its types have repeated three times along the path. A
    /// getter whose value is not a leaf is called twice, and a lazy sequence enumerated twice, to tell made objects from stored ones. The walk uses an explicit stack, so a deep graph cannot
    /// overflow the call stack. A property getter that throws propagates its own exception. A dictionary that implements
    /// only the generic interfaces (ExpandoObject) compares by key, and a key prints as <c>PropertyPath</c> parses it
    /// back. Tasks, Lazy values, streams and delegates compare with their own Equals, never read. A graph of more than
    /// 2,097,152 object pairs, or a lazy sequence of more than 4,194,304 items, throws
    /// <see cref="InvalidOperationException"/>.
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

        private enum Kind { Visit, Report }

        private struct Frame
        {
            public Kind Kind;
            public PathNode? Path;
            public object? A;
            public object? B;
            public ObjectMembers.Trail Trail; // for a Visit: what the path followed (ObjectMembers.Trail)
        }

        public static IReadOnlyList<Difference> Compare(object? a, object? b, ObjectComparisonOptions? options = null)
        {
            var opt = options ?? new ObjectComparisonOptions();
            var result = new List<Difference>();
            // Pairs already taken up: a pair met again (a cycle, or a shared object along another path) is not walked
            // again. Only pairs found equal were skipped before, so a shared pair that differed was walked and reported
            // once per path: exponential in the depth of a graph whose nodes share children.
            var seen = new HashSet<(object, object)>(ObjectMembers.ReferencePairComparer.Instance);
            var work = new Stack<Frame>();
            work.Push(new Frame { Kind = Kind.Visit, A = a, B = b, Trail = ObjectMembers.Trail.Start });
            var children = new List<Frame>();

            while (work.Count > 0)
            {
                Frame f = work.Pop();
                switch (f.Kind)
                {
                    case Kind.Report:
                        result.Add(new Difference(f.Path?.ToString() ?? "", f.A, f.B));
                        break;
                    default:
                        children.Clear();
                        Visit(f.Path, f.A, f.B, f.Trail, opt, seen, children);
                        // Push in reverse so the children are handled in order, as a recursive walk would.
                        for (int i = children.Count - 1; i >= 0; i--) work.Push(children[i]);
                        break;
                }
            }
            return result;
        }

        // Emits, in order, the frames that handle this pair: reports and child visits.
        private static void Visit(PathNode? path, object? a, object? b, ObjectMembers.Trail trail, ObjectComparisonOptions opt,
            HashSet<(object, object)> seen, List<Frame> output)
        {
            if (ReferenceEquals(a, b)) return;
            if (a is null || b is null) { output.Add(Report(path, a, b)); return; }
            if (trail.TooDeep) return;
            object? na0 = ObjectMembers.Normalize(a), nb0 = ObjectMembers.Normalize(b);
            if (na0 is null || nb0 is null)
            {
                if (na0 != null || nb0 != null) output.Add(Report(path, a, b));
                return;
            }
            object na = na0, nb = nb0;

            Type ta = na.GetType(), tb = nb.GetType();
            if (ObjectMembers.IsLeaf(ta) || ObjectMembers.IsLeaf(tb))
            {
                if (!DeepEquals.LeafEquals(na, nb, opt)) output.Add(Report(path, a, b));
                return;
            }

            // Boxes too: a boxed struct can reach itself through an object-typed member.
            if (!seen.Add((na, nb))) return;
            if (seen.Count > ObjectMembers.MaxObjects) throw ObjectMembers.TooManyObjects();

            if (na is IDictionary da && nb is IDictionary db)
            {
                var keys = new List<object>();
                foreach (var k in da.Keys) keys.Add(k);
                foreach (var k in db.Keys) if (!da.Contains(k)) keys.Add(k);

                foreach (var key in keys)
                {
                    var p = new PathNode(path, ObjectMembers.KeyText(key), indexed: true);
                    bool inA = da.Contains(key), inB = db.Contains(key);
                    if (inA && inB) output.Add(new Frame { Kind = Kind.Visit, Path = p, A = da[key], B = db[key], Trail = ObjectMembers.Trail.Pair(trail, da[key], ObjectMembers.Origin.Stable, db[key], ObjectMembers.Origin.Stable) });
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
                var la = ObjectMembers.Items(ea, out ObjectMembers.Origin oa);
                var lb = ObjectMembers.Items(eb, out ObjectMembers.Origin ob);
                int max = Math.Max(la.Count, lb.Count);
                for (int i = 0; i < max; i++)
                {
                    var p = new PathNode(path, i.ToString(CultureInfo.InvariantCulture), indexed: true);
                    if (i >= la.Count) output.Add(Report(p, null, lb[i]));
                    else if (i >= lb.Count) output.Add(Report(p, la[i], null));
                    else output.Add(new Frame { Kind = Kind.Visit, Path = p, A = la[i], B = lb[i], Trail = ObjectMembers.Trail.Pair(trail, la[i], oa, lb[i], ob) });
                }
            }
            else if (ta != tb)
            {
                output.Add(Report(path, a, b));
            }
            else
            {
                var ma = new List<(string Name, object? Value, ObjectMembers.Origin Origin)>(ObjectMembers.Read(na, opt.IncludeFields || ta.IsValueType));
                var mb = new List<(string Name, object? Value, ObjectMembers.Origin Origin)>(ObjectMembers.Read(nb, opt.IncludeFields || ta.IsValueType));
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
                            Trail = ObjectMembers.Trail.Pair(trail, ma[i].Value, ma[i].Origin, mb[i].Value, mb[i].Origin),
                        });
                }
            }
        }

        private static Frame Report(PathNode? path, object? a, object? b) => new Frame { Kind = Kind.Report, Path = path, A = a, B = b };
    }
}
