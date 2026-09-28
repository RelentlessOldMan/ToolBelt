using System;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class CartesianProductTests
    {
        public void TwoSequences_OdometerOrder()
        {
            var product = CartesianProduct.Of(new[] { 1, 2 }, new[] { 10, 20, 30 }).ToList();
            Check.Equal(6, product.Count);
            Check.True(product[0].SequenceEqual(new[] { 1, 10 }));
            Check.True(product[1].SequenceEqual(new[] { 1, 20 })); // last varies fastest
            Check.True(product[5].SequenceEqual(new[] { 2, 30 }));
        }

        public void EmptyFactor_YieldsNothing()
        {
            Check.Equal(0, CartesianProduct.Of(new[] { 1, 2 }, Array.Empty<int>()).Count());
        }

        public void NoSequences_YieldsSingleEmpty()
        {
            var product = CartesianProduct.Of<int>().ToList();
            Check.Equal(1, product.Count);
            Check.Equal(0, product[0].Length);
        }

        public void SingleSequence()
        {
            var product = CartesianProduct.Of(new[] { 1, 2, 3 }).ToList();
            Check.Equal(3, product.Count);
            Check.True(product[2].SequenceEqual(new[] { 3 }));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => CartesianProduct.Of<int>((System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<int>>)null!).ToList());
        }

        // Property: count == product of factor sizes, and every tuple's i-th element comes from factor i.
        public void Property_CountAndMembership()
        {
            var rng = new Random(77);
            for (int trial = 0; trial < 500; trial++)
            {
                int factorCount = rng.Next(1, 5);
                var factors = new int[factorCount][];
                long expected = 1;
                for (int f = 0; f < factorCount; f++)
                {
                    int len = rng.Next(1, 5);
                    factors[f] = Enumerable.Range(f * 100, len).ToArray();
                    expected *= len;
                }

                var product = CartesianProduct.Of(factors).ToList();
                Check.Equal((int)expected, product.Count, $"trial {trial}: count");
                foreach (var tuple in product)
                    for (int f = 0; f < factorCount; f++)
                        Check.True(Array.IndexOf(factors[f], tuple[f]) >= 0, $"trial {trial}: element {f} from factor");
            }
        }
    }
}
