// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A prefix tree (trie) storing a set of string keys, with fast exact-match, has-any-prefix, and
    /// all-keys-with-a-given-prefix queries. Removing keys prunes now-empty branches so that a reachable
    /// node always leads to at least one stored key. Not thread-safe.
    /// </summary>
    public sealed class Trie
    {
        // A node keeps one child inline and makes a dictionary only at its second: a dictionary per node held about
        // 250 bytes per character (one 2,000,000-character key retained 476 MB).
        private sealed class Node
        {
            private char _onlyKey;
            private Node? _only;
            private Dictionary<char, Node>? _many;
            public bool IsWord;

            public int Count => _many is not null ? _many.Count : _only is null ? 0 : 1;

            public bool TryGetValue(char c, out Node next)
            {
                if (_many is not null) return _many.TryGetValue(c, out next!);
                next = _only!;
                return _only is not null && _onlyKey == c;
            }

            public void Add(char c, Node child)
            {
                if (_many is not null) { _many.Add(c, child); return; }
                if (_only is null) { _onlyKey = c; _only = child; return; }
                var many = new Dictionary<char, Node> { [_onlyKey] = _only, [c] = child };   // built before it replaces the inline child
                _many = many;
                _only = null;
            }

            public void Remove(char c)
            {
                if (_many is not null) _many.Remove(c);
                else if (_only is not null && _onlyKey == c) _only = null;
            }

            public void CopyKeysTo(char[] keys)
            {
                if (_many is not null) _many.Keys.CopyTo(keys, 0);
                else if (_only is not null) keys[0] = _onlyKey;
            }
        }

        // One level of the explicit depth-first stack in Collect.
        private sealed class Frame
        {
            public readonly Node Node;
            public readonly char[] Keys;
            public int Next;

            public Frame(Node node)
            {
                Node = node;
                Keys = new char[node.Count];
                node.CopyKeysTo(Keys);
                Array.Sort(Keys); // ordinal
            }
        }

        private readonly Node _root = new Node();

        /// <summary>Number of distinct keys stored.</summary>
        public int Count { get; private set; }

        /// <summary>Adds a key. A duplicate add is a no-op.</summary>
        public void Add(string key)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));

            var node = _root;
            int i = 0;
            for (; i < key.Length && node.TryGetValue(key[i], out var next); i++)
                node = next;
            if (i == key.Length)
            {
                if (!node.IsWord)
                {
                    node.IsWord = true;
                    Count++;
                }
                return;
            }

            // Build the new branch from its end and link it last, so an Add that runs out of memory part way leaves
            // nothing reachable (StartsWith answered true for prefixes of a key never stored).
            var tail = new Node { IsWord = true };
            for (int j = key.Length - 1; j > i; j--)
            {
                var parent = new Node();
                parent.Add(key[j], tail);
                tail = parent;
            }
            node.Add(key[i], tail);
            Count++;
        }

        /// <summary>Whether <paramref name="key"/> is stored exactly.</summary>
        public bool Contains(string key)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));
            var node = Walk(key);
            return node is not null && node.IsWord;
        }

        /// <summary>Whether any stored key begins with <paramref name="prefix"/> (an exact key counts).</summary>
        public bool StartsWith(string prefix)
        {
            if (prefix is null)
                throw new ArgumentNullException(nameof(prefix));
            var node = Walk(prefix);
            // A pruned trie never keeps empty non-word nodes, so "has a word below" reduces to this.
            return node is not null && (node.IsWord || node.Count > 0);
        }

        /// <summary>All stored keys beginning with <paramref name="prefix"/>, in ascending ordinal order.</summary>
        public IReadOnlyList<string> WithPrefix(string prefix)
        {
            if (prefix is null)
                throw new ArgumentNullException(nameof(prefix));

            var results = new List<string>();
            var start = Walk(prefix);
            if (start is not null)
                Collect(start, new StringBuilder(prefix), results);
            return results;
        }

        /// <summary>Removes a key. Returns whether it was present.</summary>
        public bool Remove(string key)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));

            // Walk first without allocating: an absent 1e8-character key allocated an 800 MB path.
            var probe = _root;
            for (int i = 0; i < key.Length; i++)
                if (!probe.TryGetValue(key[i], out probe))
                    return false;
            if (!probe.IsWord)
                return false;

            // Iterative (a recursive walk overflows the stack on a very long key): record the path, then
            // prune now-empty nodes from the bottom up.
            var path = new Node[key.Length + 1];
            path[0] = _root;
            for (int i = 0; i < key.Length; i++)
            {
                if (!path[i].TryGetValue(key[i], out var next))
                    return false;
                path[i + 1] = next;
            }

            var end = path[key.Length];
            if (!end.IsWord)
                return false;
            end.IsWord = false;
            Count--;

            for (int d = key.Length; d > 0; d--)
            {
                var node = path[d];
                if (node.IsWord || node.Count > 0)
                    break;
                path[d - 1].Remove(key[d - 1]); // prune dead branch
            }
            return true;
        }

        private Node? Walk(string s)
        {
            var node = _root;
            foreach (char c in s)
            {
                if (!node.TryGetValue(c, out var next))
                    return null;
                node = next;
            }
            return node;
        }

        // Depth-first, children in ascending ordinal order, so results come out sorted. Iterative: a
        // recursive walk overflows the stack on a very long key.
        private static void Collect(Node start, StringBuilder path, List<string> results)
        {
            if (start.IsWord)
                results.Add(path.ToString());

            var stack = new List<Frame> { new Frame(start) };
            while (stack.Count > 0)
            {
                var top = stack[stack.Count - 1];
                if (top.Next == top.Keys.Length)
                {
                    stack.RemoveAt(stack.Count - 1);
                    if (stack.Count > 0)
                        path.Length--; // drop the character that led to the finished node
                    continue;
                }

                char c = top.Keys[top.Next++];
                top.Node.TryGetValue(c, out var child);
                path.Append(c);
                if (child.IsWord)
                    results.Add(path.ToString());
                stack.Add(new Frame(child));
            }
        }
    }
}
