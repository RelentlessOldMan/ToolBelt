using System;
using System.Collections.Generic;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class SequenceEqualityComparerTests
    {
        // ---------- ordered ----------

        public void Ordered_EqualWhenSameElementsSameOrder()
        {
            var cmp = SequenceEqualityComparer<int>.Default;
            Check.True(cmp.Equals(new[] { 1, 2, 3 }, new List<int> { 1, 2, 3 }));
            Check.False(cmp.Equals(new[] { 1, 2, 3 }, new[] { 3, 2, 1 }));
            Check.False(cmp.Equals(new[] { 1, 2 }, new[] { 1, 2, 3 }));
            Check.False(cmp.Equals(new[] { 1, 2, 3 }, new[] { 1, 2 }));
        }

        public void Ordered_EqualSequencesShareHash()
        {
            var cmp = SequenceEqualityComparer<string>.Default;
            Check.Equal(cmp.GetHashCode(new[] { "a", "b" }), cmp.GetHashCode(new List<string> { "a", "b" }));
        }

        public void Ordered_NullHandling()
        {
            var cmp = SequenceEqualityComparer<int>.Default;
            Check.True(cmp.Equals(null, null));
            Check.False(cmp.Equals(null, Array.Empty<int>()));
            Check.False(cmp.Equals(Array.Empty<int>(), null));
            Check.True(cmp.Equals(Array.Empty<int>(), new List<int>()));
            Check.Throws<ArgumentNullException>(() => cmp.GetHashCode(null!));
        }

        public void Ordered_WorksAsDictionaryKey()
        {
            var map = new Dictionary<int[], string>(SequenceEqualityComparer<int>.Default)
            {
                [new[] { 1, 2, 3 }] = "found",
            };
            // A different array instance with the same contents resolves to the same entry.
            Check.Equal("found", map[new[] { 1, 2, 3 }]);
            Check.False(map.ContainsKey(new[] { 1, 2 }));
            Check.False(map.ContainsKey(new[] { 3, 2, 1 }));
        }

        public void Ordered_HonorsCustomElementComparer()
        {
            var cmp = SequenceComparer.Ordered<string>(StringComparer.OrdinalIgnoreCase);
            Check.True(cmp.Equals(new[] { "Foo", "BAR" }, new[] { "foo", "bar" }));
            Check.Equal(cmp.GetHashCode(new[] { "Foo" }), cmp.GetHashCode(new[] { "FOO" }));
        }

        public void Ordered_HandlesNullElements()
        {
            var cmp = SequenceEqualityComparer<string?>.Default;
            Check.True(cmp.Equals(new string?[] { "a", null, "b" }, new string?[] { "a", null, "b" }));
            Check.False(cmp.Equals(new string?[] { "a", null }, new string?[] { "a", "b" }));
        }

        // ---------- unordered (multiset) ----------

        public void Unordered_IgnoresOrderButNotMultiplicity()
        {
            var cmp = MultisetEqualityComparer<int>.Default;
            Check.True(cmp.Equals(new[] { 1, 2, 2 }, new[] { 2, 1, 2 }));
            Check.False(cmp.Equals(new[] { 1, 2, 2 }, new[] { 1, 2 }));     // fewer 2s
            Check.False(cmp.Equals(new[] { 1, 2, 2 }, new[] { 1, 1, 2 }));  // different multiplicities
            Check.True(cmp.Equals(Array.Empty<int>(), new List<int>()));
        }

        public void Unordered_PermutationsShareHash()
        {
            var cmp = MultisetEqualityComparer<int>.Default;
            Check.Equal(cmp.GetHashCode(new[] { 1, 2, 3 }), cmp.GetHashCode(new[] { 3, 1, 2 }));
            // Different multiplicity should (almost always) hash differently thanks to the count term.
            Check.False(cmp.GetHashCode(new[] { 1, 1 }) == cmp.GetHashCode(new[] { 1 }));
        }

        public void Unordered_NullHandling()
        {
            var cmp = MultisetEqualityComparer<int>.Default;
            Check.True(cmp.Equals(null, null));
            Check.False(cmp.Equals(null, Array.Empty<int>()));
            Check.Throws<ArgumentNullException>(() => cmp.GetHashCode(null!));
        }

        public void Unordered_HandlesNullElements()
        {
            var cmp = MultisetEqualityComparer<string?>.Default;
            Check.True(cmp.Equals(new string?[] { "a", null, null }, new string?[] { null, "a", null }));
            Check.False(cmp.Equals(new string?[] { "a", null }, new string?[] { "a", "a" }));
            Check.False(cmp.Equals(new string?[] { null }, new string?[] { null, null }));
        }

        public void Unordered_WorksAsDictionaryKey()
        {
            var map = new Dictionary<int[], string>(MultisetEqualityComparer<int>.Default)
            {
                [new[] { 1, 2, 3 }] = "set",
            };
            Check.Equal("set", map[new[] { 3, 2, 1 }]);
            Check.False(map.ContainsKey(new[] { 1, 2 }));
        }

        public void Unordered_HonorsCustomElementComparer()
        {
            var cmp = SequenceComparer.Unordered<string>(StringComparer.OrdinalIgnoreCase);
            Check.True(cmp.Equals(new[] { "A", "b" }, new[] { "B", "a" }));
        }
    }
}
