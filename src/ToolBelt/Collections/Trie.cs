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
        private sealed class Node
        {
            public readonly Dictionary<char, Node> Children = new Dictionary<char, Node>();
            public bool IsWord;
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
                Keys = new char[node.Children.Count];
                node.Children.Keys.CopyTo(Keys, 0);
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
            foreach (char c in key)
            {
                if (!node.Children.TryGetValue(c, out var next))
                {
                    next = new Node();
                    node.Children[c] = next;
                }
                node = next;
            }

            if (!node.IsWord)
            {
                node.IsWord = true;
                Count++;
            }
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
            return node is not null && (node.IsWord || node.Children.Count > 0);
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

            // Iterative (a recursive walk overflows the stack on a very long key): record the path, then
            // prune now-empty nodes from the bottom up.
            var path = new Node[key.Length + 1];
            path[0] = _root;
            for (int i = 0; i < key.Length; i++)
            {
                if (!path[i].Children.TryGetValue(key[i], out var next))
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
                if (node.IsWord || node.Children.Count > 0)
                    break;
                path[d - 1].Children.Remove(key[d - 1]); // prune dead branch
            }
            return true;
        }

        private Node? Walk(string s)
        {
            var node = _root;
            foreach (char c in s)
            {
                if (!node.Children.TryGetValue(c, out var next))
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
                var child = top.Node.Children[c];
                path.Append(c);
                if (child.IsWord)
                    results.Add(path.ToString());
                stack.Add(new Frame(child));
            }
        }
    }
}
