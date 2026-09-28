using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class TrieTests
    {
        public void ContainsAndStartsWith()
        {
            var trie = new Trie();
            trie.Add("cat");
            trie.Add("car");
            trie.Add("cart");

            Check.True(trie.Contains("cat"));
            Check.False(trie.Contains("ca"));   // prefix, not a stored word
            Check.True(trie.StartsWith("ca"));
            Check.True(trie.StartsWith("car"));
            Check.False(trie.StartsWith("dog"));
        }

        public void WithPrefix_ReturnsSortedMatches()
        {
            var trie = new Trie();
            foreach (var w in new[] { "car", "cart", "cat", "dog" })
                trie.Add(w);
            Check.True(trie.WithPrefix("ca").SequenceEqual(new[] { "car", "cart", "cat" }));
            Check.True(trie.WithPrefix("").SequenceEqual(new[] { "car", "cart", "cat", "dog" }));
        }

        public void DuplicateAdd_IsNoOp()
        {
            var trie = new Trie();
            trie.Add("x");
            trie.Add("x");
            Check.Equal(1, trie.Count);
        }

        public void Remove_PrunesAndUpdatesQueries()
        {
            var trie = new Trie();
            trie.Add("car");
            trie.Add("cart");
            Check.True(trie.Remove("cart"));
            Check.True(trie.Contains("car"));
            Check.False(trie.Contains("cart"));
            Check.False(trie.StartsWith("cart"));
            Check.Equal(1, trie.Count);
            Check.False(trie.Remove("nope"));
        }

        public void EmptyPrefix_OnEmptyTrie()
        {
            var trie = new Trie();
            Check.False(trie.StartsWith("")); // no words at all
            Check.Equal(0, trie.WithPrefix("").Count);
        }

        public void Null_Throws()
        {
            var trie = new Trie();
            Check.Throws<ArgumentNullException>(() => trie.Add(null!));
            Check.Throws<ArgumentNullException>(() => trie.Contains(null!));
        }

        // Differential: mirror random add/remove ops against a HashSet and compare membership, count, and
        // prefix queries after each op.
        public void Differential_MatchesHashSetModel()
        {
            var rng = new Random(1993);
            for (int trial = 0; trial < 200; trial++)
            {
                var trie = new Trie();
                var model = new HashSet<string>();
                var words = new List<string>();
                for (int i = 0; i < 30; i++)
                    words.Add(RandomWord(rng, "abc", maxLen: 4));

                for (int op = 0; op < 150; op++)
                {
                    string w = words[rng.Next(words.Count)];
                    if (rng.Next(2) == 0)
                    {
                        trie.Add(w);
                        model.Add(w);
                    }
                    else
                    {
                        Check.Equal(model.Remove(w), trie.Remove(w), $"trial {trial} op {op}: remove '{w}'");
                    }

                    Check.Equal(model.Count, trie.Count, $"trial {trial} op {op}: count");
                    Check.Equal(model.Contains(w), trie.Contains(w), $"trial {trial} op {op}: contains '{w}'");

                    string prefix = RandomWord(rng, "abc", maxLen: 2);
                    Check.Equal(model.Any(x => x.StartsWith(prefix, StringComparison.Ordinal)),
                        trie.StartsWith(prefix), $"trial {trial} op {op}: startsWith '{prefix}'");

                    var expected = model.Where(x => x.StartsWith(prefix, StringComparison.Ordinal))
                        .OrderBy(x => x, StringComparer.Ordinal).ToArray();
                    Check.True(expected.SequenceEqual(trie.WithPrefix(prefix)),
                        $"trial {trial} op {op}: withPrefix '{prefix}'");
                }
            }
        }

        private static string RandomWord(Random rng, string palette, int maxLen)
        {
            int len = rng.Next(0, maxLen + 1);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
                sb.Append(palette[rng.Next(palette.Length)]);
            return sb.ToString();
        }
    }
}
