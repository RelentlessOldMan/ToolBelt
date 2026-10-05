// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ToolBelt.Objects
{
    /// <summary>
    /// The inverse of <c>FlattenObject.Flatten</c>: rebuilds a nested tree from path keys such as
    /// <c>user.name</c>, <c>user.roles[0]</c>, <c>limits[max]</c>. Objects become <see cref="Dictionary{TKey,TValue}"/>
    /// (string → object?) in first-seen key order; a node whose children are all bracketed indices 0…n−1 becomes a
    /// <see cref="List{T}"/> of object?; any other bracketed key is a dictionary key. The result is shaped for JSON
    /// serialization or a config binder. Conflicting paths (<c>a = 1</c> and <c>a.b = 2</c>) throw, naming both.
    /// Keys containing the separator or brackets cannot be represented — the same limit Flatten has.
    /// </summary>
    public static class UnflattenObject
    {
        private sealed class Node
        {
            public readonly Dictionary<string, Node> Children = new Dictionary<string, Node>(StringComparer.Ordinal);
            public readonly List<string> Order = new List<string>();
            public readonly List<bool> Indexed = new List<bool>();
            public bool HasValue;
            public object? Value;
            public string? ValuePath;
        }

        public static Dictionary<string, object?> Unflatten(IEnumerable<KeyValuePair<string, object?>> flat, string separator = ".")
        {
            if (flat is null) throw new ArgumentNullException(nameof(flat));
            if (string.IsNullOrEmpty(separator)) throw new ArgumentException("Separator must not be empty.", nameof(separator));
            var root = new Node();
            foreach (var kv in flat)
            {
                if (string.IsNullOrEmpty(kv.Key)) throw new FormatException("Empty path.");
                Node node = root;
                string walked = "";
                foreach (var (segment, indexed) in Parse(kv.Key, separator))
                {
                    if (node.HasValue) throw new InvalidOperationException($"Path '{kv.Key}' conflicts with value at '{node.ValuePath}'.");
                    if (!node.Children.TryGetValue(segment, out Node? child))
                    {
                        child = new Node();
                        node.Children[segment] = child;
                        node.Order.Add(segment);
                        node.Indexed.Add(indexed);
                    }
                    walked = walked.Length == 0 ? segment : walked + (indexed ? "[" + segment + "]" : separator + segment);
                    node = child;
                }
                if (node.Children.Count > 0) throw new InvalidOperationException($"Value at '{kv.Key}' conflicts with nested paths under it.");
                if (node.HasValue) throw new InvalidOperationException($"Duplicate path '{kv.Key}'.");
                node.HasValue = true;
                node.Value = kv.Value;
                node.ValuePath = kv.Key;
            }
            return (Dictionary<string, object?>)Build(root, isRoot: true)!;
        }

        private static object? Build(Node node, bool isRoot = false)
        {
            if (node.HasValue) return node.Value;
            bool list = !isRoot && node.Order.Count > 0 && node.Indexed.All(i => i) && IsDenseIndexRange(node.Order);
            if (list)
            {
                var items = new object?[node.Order.Count];
                foreach (string key in node.Order) items[int.Parse(key, CultureInfo.InvariantCulture)] = Build(node.Children[key]);
                return items.ToList();
            }
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (string key in node.Order) dict[key] = Build(node.Children[key]);
            return dict;
        }

        private static bool IsDenseIndexRange(List<string> keys)
        {
            var seen = new bool[keys.Count];
            foreach (string k in keys)
            {
                if (k.Length == 0 || (k.Length > 1 && k[0] == '0') || !k.All(char.IsDigit)) return false;
                if (!int.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out int i) || i >= keys.Count || seen[i]) return false;
                seen[i] = true;
            }
            return true;
        }

        // "a.b[0][key].c" → (a,false) (b,false) (0,true) (key,true) (c,false)
        private static IEnumerable<(string Segment, bool Indexed)> Parse(string path, string sep)
        {
            var result = new List<(string, bool)>();
            var sb = new StringBuilder();
            int i = 0;
            bool expectName = true;
            while (i < path.Length)
            {
                if (path[i] == '[')
                {
                    if (sb.Length > 0) { result.Add((sb.ToString(), false)); sb.Clear(); }
                    else if (expectName && result.Count > 0) throw new FormatException($"Empty segment in '{path}'.");
                    int close = path.IndexOf(']', i + 1);
                    if (close < 0) throw new FormatException($"Unclosed '[' in '{path}'.");
                    result.Add((path.Substring(i + 1, close - i - 1), true));
                    i = close + 1;
                    expectName = false;
                    continue;
                }
                if (string.CompareOrdinal(path, i, sep, 0, sep.Length) == 0)
                {
                    if (sb.Length > 0) { result.Add((sb.ToString(), false)); sb.Clear(); }
                    else if (expectName) throw new FormatException($"Empty segment in '{path}'.");
                    i += sep.Length;
                    expectName = true;
                    continue;
                }
                if (path[i] == ']') throw new FormatException($"Unexpected ']' in '{path}'.");
                sb.Append(path[i++]);
                expectName = false;
            }
            if (sb.Length > 0) result.Add((sb.ToString(), false));
            else if (expectName) throw new FormatException($"Path '{path}' ends with a separator.");
            return result;
        }
    }
}
