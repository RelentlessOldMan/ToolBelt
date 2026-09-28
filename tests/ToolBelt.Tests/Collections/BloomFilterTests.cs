using System;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class BloomFilterTests
    {
        public void AddedItems_AlwaysReportPresent()
        {
            var filter = new BloomFilter(1000, 0.01);
            for (int i = 0; i < 1000; i++)
                filter.Add($"item-{i}");

            for (int i = 0; i < 1000; i++)
                Check.True(filter.MightContain($"item-{i}"), $"no false negatives: item-{i}");
        }

        public void FalsePositiveRate_WithinBound()
        {
            const int n = 2000;
            const double p = 0.01;
            var filter = new BloomFilter(n, p);
            for (int i = 0; i < n; i++)
                filter.Add($"member-{i}");

            int falsePositives = 0;
            for (int i = 0; i < n; i++)
                if (filter.MightContain($"stranger-{i}"))
                    falsePositives++;

            double rate = (double)falsePositives / n;
            // Deterministic (fixed items); allow generous headroom over the target rate.
            Check.True(rate <= p * 4, $"false-positive rate {rate} exceeded {p * 4}");
        }

        public void SizingIsReasonable()
        {
            var filter = new BloomFilter(1000, 0.01);
            Check.True(filter.HashCount >= 1);
            Check.True(filter.BitCount >= 1000);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new BloomFilter(0));
            Check.Throws<ArgumentOutOfRangeException>(() => new BloomFilter(100, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new BloomFilter(100, 1));
            Check.Throws<ArgumentNullException>(() => new BloomFilter(10).Add((string)null!));
        }

        public void HugeSizing_Throws_InsteadOfOverflowing()
        {
            // Optimal bit count would exceed int.MaxValue; must fail loudly, not wrap to a tiny filter.
            Check.Throws<ArgumentOutOfRangeException>(() => new BloomFilter(int.MaxValue, 1e-9));
        }
    }
}
