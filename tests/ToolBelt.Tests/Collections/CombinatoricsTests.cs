using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class CombinatoricsTests
    {
        public void Combinations_Known()
        {
            var combos = Combinatorics.Combinations(new[] { 1, 2, 3, 4 }, 2).ToList();
            Check.Equal(6, combos.Count);
            Check.True(combos[0].SequenceEqual(new[] { 1, 2 }));
            Check.True(combos[5].SequenceEqual(new[] { 3, 4 }));
            // All are index-increasing (order preserved) and distinct.
            foreach (var c in combos)
                Check.True(c[0] < c[1], "index-increasing");
        }

        public void Combinations_EdgeK()
        {
            Check.Equal(1, Combinatorics.Combinations(new[] { 1, 2, 3 }, 0).Count());   // one empty combo
            Check.Equal(0, Combinatorics.Combinations(new[] { 1, 2 }, 3).Count());       // k > n
            Check.Equal(1, Combinatorics.Combinations(new[] { 1, 2, 3 }, 3).Count());    // the whole set
        }

        public void Permutations_Known()
        {
            var perms = Combinatorics.Permutations(new[] { 1, 2, 3 }).ToList();
            Check.Equal(6, perms.Count);
            // Each is a rearrangement of the same multiset, and all are distinct.
            var distinct = new HashSet<string>();
            foreach (var p in perms)
            {
                Check.True(p.OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3 }), "same multiset");
                Check.True(distinct.Add(string.Join(",", p)), "distinct");
            }
        }

        public void Permutations_Empty_YieldsOne()
        {
            Check.Equal(1, Combinatorics.Permutations(Array.Empty<int>()).Count());
            Check.Equal(1, Combinatorics.Permutations(new[] { 42 }).Count());
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Combinatorics.Combinations<int>(null!, 1).ToList());
            Check.Throws<ArgumentOutOfRangeException>(() => Combinatorics.Combinations(new[] { 1 }, -1).ToList());
        }

        // Property: counts match C(n,k) and n! over a range of sizes.
        public void Property_CountsMatchFormulas()
        {
            for (int n = 0; n <= 7; n++)
            {
                var items = Enumerable.Range(1, n).ToArray();
                Check.Equal((int)Factorial(n), Combinatorics.Permutations(items).Count(), $"n!={n}");
                for (int k = 0; k <= n; k++)
                    Check.Equal((int)Choose(n, k), Combinatorics.Combinations(items, k).Count(), $"C({n},{k})");
            }
        }

        private static long Factorial(int n) { long f = 1; for (int i = 2; i <= n; i++) f *= i; return f; }
        private static long Choose(int n, int k) => Factorial(n) / (Factorial(k) * Factorial(n - k));
    }
}
