// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Text
{
    /// <summary>
    /// String similarity based on character n-grams. <see cref="Dice"/> is the Sørensen–Dice coefficient
    /// over the n-gram multiset (counts with multiplicity); <see cref="Jaccard"/> is the Jaccard index
    /// over the distinct n-gram set. Both return a value in [0, 1] (1 = identical). Two strings too short
    /// to yield any n-gram are treated as similar (1.0) only if they are equal. Grams are taken over code points: a
    /// surrogate pair is one character (a lone surrogate counts as one of its own).
    /// </summary>
    public static class NGramSimilarity
    {
        /// <summary>Sørensen–Dice coefficient over n-gram multisets.</summary>
        public static double Dice(string a, string b, int n = 2)
        {
            Validate(a, b, n);
            int[] pa = CodePoints(a), pb = CodePoints(b);
            int totalA = Math.Max(0, pa.Length - n + 1), totalB = Math.Max(0, pb.Length - n + 1);
            if (totalA + totalB == 0)
                return a == b ? 1.0 : 0.0;
            if (totalA == 0 || totalB == 0)
                return 0.0;

            int[] id = GramClasses(pa, pb, n, out int classes);
            var countA = new int[classes];
            for (int i = 0; i < totalA; i++) countA[id[i]]++;
            long intersection = 0;
            for (int j = 0; j < totalB; j++)
            {
                int c = id[pa.Length + j];
                if (countA[c] > 0) { countA[c]--; intersection++; }
            }
            return 2.0 * intersection / ((long)totalA + totalB);
        }

        /// <summary>Jaccard index over the set of distinct n-grams.</summary>
        public static double Jaccard(string a, string b, int n = 2)
        {
            Validate(a, b, n);
            int[] pa = CodePoints(a), pb = CodePoints(b);
            int totalA = Math.Max(0, pa.Length - n + 1), totalB = Math.Max(0, pb.Length - n + 1);
            if (totalA + totalB == 0)
                return a == b ? 1.0 : 0.0;
            if (totalA == 0 || totalB == 0)
                return 0.0;

            int[] id = GramClasses(pa, pb, n, out int classes);
            var seen = new byte[classes];   // bit 1: in a, bit 2: in b
            for (int i = 0; i < totalA; i++) seen[id[i]] |= 1;
            for (int j = 0; j < totalB; j++) seen[id[pa.Length + j]] |= 2;
            long intersection = 0, union = 0;
            foreach (byte f in seen)
            {
                if (f != 0) union++;
                if (f == 3) intersection++;
            }
            return (double)intersection / union;
        }

        // An exact class id for the n-gram at each position of a + b (equal grams, equal ids; a gram that runs off the end
        // of a or b gets an id that is never read), by rank doubling: the ranks of the length-2k substrings are the
        // renumbered pairs of length-k ranks, sorted by two counting passes, so it costs O((|a| + |b|) log n) whatever the
        // text. Grams were told apart by a rolling hash mod 2^64, and Thue–Morse blocks share one for any base: 2 million
        // such characters took 3.5 minutes, and repetitive text compared every repeat in full.
        private static int[] GramClasses(int[] a, int[] b, int n, out int classes)
        {
            long total = (long)a.Length + b.Length;
            if (total > int.MaxValue - 2)
                throw new ArgumentException("The two strings together are too long to compare.", nameof(b));
            int length = (int)total;

            // Rank 0: the code points, numbered densely in order.
            var chars = new int[length];
            Array.Copy(a, 0, chars, 0, a.Length);
            Array.Copy(b, 0, chars, a.Length, b.Length);
            var distinct = (int[])chars.Clone();
            Array.Sort(distinct);
            int d = 0;
            for (int i = 0; i < distinct.Length; i++)
                if (i == 0 || distinct[i] != distinct[i - 1]) distinct[d++] = distinct[i];
            var rank = new int[length];
            for (int i = 0; i < length; i++) rank[i] = Array.BinarySearch(distinct, 0, d, chars[i]);
            classes = d;

            var next = new int[length];
            var order = new int[length];
            var sorted = new int[length];
            var bucket = new int[length + 2];
            int span = 1;
            while (span <= n / 2)
            {
                classes = Combine(rank, span, classes, next, order, sorted, bucket);
                int[] t = rank; rank = next; next = t;
                span *= 2;
            }
            if (span < n)
            {
                // Two overlapping length-span pieces cover the gram exactly.
                classes = Combine(rank, n - span, classes, next, order, sorted, bucket);
                rank = next;
            }
            return rank;
        }

        // next[i] = the dense rank of the pair (rank[i], rank[i + offset]), with "past the end" below every rank.
        private static int Combine(int[] rank, int offset, int classes, int[] next, int[] order, int[] sorted, int[] bucket)
        {
            int length = rank.Length;
            // Counting sort by the second key (0 = past the end, else rank + 1), then stably by the first.
            Array.Clear(bucket, 0, classes + 2);
            for (int i = 0; i < length; i++) bucket[Second(rank, i, offset) + 1]++;
            for (int k = 1; k < classes + 2; k++) bucket[k] += bucket[k - 1];
            for (int i = 0; i < length; i++) order[bucket[Second(rank, i, offset)]++] = i;
            Array.Clear(bucket, 0, classes + 2);
            for (int i = 0; i < length; i++) bucket[rank[i] + 1]++;
            for (int k = 1; k < classes + 1; k++) bucket[k] += bucket[k - 1];
            foreach (int i in order) sorted[bucket[rank[i]]++] = i;

            int dense = -1, prevFirst = -1, prevSecond = -1;
            foreach (int i in sorted)
            {
                int first = rank[i], second = Second(rank, i, offset);
                if (first != prevFirst || second != prevSecond) { dense++; prevFirst = first; prevSecond = second; }
                next[i] = dense;
            }
            return dense + 1;
        }

        // The code points of s. Grams were UTF-16 units, so two emoji from one surrogate block shared a "gram" (their high
        // surrogate): Jaccard("x\U0001F600", "x\U0001F601", 1) was 0.5, not 1/3. A lone surrogate stays a unit of its own
        // (its value is below every astral code point, so it never equals one).
        private static int[] CodePoints(string s)
        {
            int count = 0;
            for (int i = 0; i < s.Length; i++, count++)
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            var points = new int[count];
            int k = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    points[k++] = char.ConvertToUtf32(s[i], s[i + 1]);
                    i++;
                }
                else points[k++] = s[i];
            }
            return points;
        }

        private static int Second(int[] rank, int i, int offset) => i + offset < rank.Length ? rank[i + offset] + 1 : 0;

        private static void Validate(string a, string b, int n)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), n, "n must be at least 1.");
        }
    }
}
