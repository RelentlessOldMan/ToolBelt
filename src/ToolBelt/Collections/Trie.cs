// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Linq;
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
            return Remove(_root, key, 0);
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

        private static void Collect(Node node, StringBuilder path, List<string> results)
        {
            if (node.IsWord)
                results.Add(path.ToString());

            foreach (char c in node.Children.Keys.OrderBy(k => k))
            {
                path.Append(c);
                Collect(node.Children[c], path, results);
                path.Length--;
            }
        }

        private bool Remove(Node node, string key, int depth)
        {
            if (depth == key.Length)
            {
                if (!node.IsWord)
                    return false;
                node.IsWord = false;
                Count--;
                return true;
            }

            char c = key[depth];
            if (!node.Children.TryGetValue(c, out var child))
                return false;

            bool removed = Remove(child, key, depth + 1);
            if (removed && !child.IsWord && child.Children.Count == 0)
                node.Children.Remove(c); // prune dead branch
            return removed;
        }
    }
}
