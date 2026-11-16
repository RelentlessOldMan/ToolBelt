// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ToolBelt.Text
{
    /// <summary>A candidate paired with its similarity score against a target.</summary>
    public readonly struct StringMatch
    {
        public StringMatch(string value, double score)
        {
            Value = value;
            Score = score;
        }

        /// <summary>The candidate string.</summary>
        public string Value { get; }

        /// <summary>Its score under the supplied scorer (higher is more similar).</summary>
        public double Score { get; }

        public override string ToString() => Value + " (" + Score.ToString("0.###", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// "Did you mean …?" ranking: pick the best (or top-N) candidate for a target string using a pluggable
    /// similarity scorer. The scorer is injected — pass any <c>(a, b) =&gt; score</c> where a higher score
    /// means more similar (e.g. <c>(a, b) =&gt; JaroWinkler.Similarity(a, b)</c>, or <c>1.0 / (1 + LevenshteinDistance.Distance(a, b))</c>)
    /// — so this file stays self-contained and does not depend on any sibling. Ordering is stable: among equal
    /// scores, the candidate seen first in the input wins.
    /// </summary>
    public static class StringSimilarity
    {
        /// <summary>
        /// Returns the highest-scoring candidate for <paramref name="target"/>, or <c>null</c> if the list is
        /// empty or no candidate scores at least <paramref name="minScore"/>.
        /// </summary>
        public static StringMatch? BestMatch(
            string target,
            IEnumerable<string> candidates,
            Func<string, string, double> scorer,
            double minScore = double.NegativeInfinity)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (candidates is null) throw new ArgumentNullException(nameof(candidates));
            if (scorer is null) throw new ArgumentNullException(nameof(scorer));

            bool found = false;
            string bestValue = string.Empty;
            double bestScore = double.NegativeInfinity;

            foreach (string candidate in candidates)
            {
                if (candidate is null) throw new ArgumentException("Candidates must not contain null.", nameof(candidates));
                double score = scorer(target, candidate);
                if (double.IsNaN(score) || score < minScore) continue;      // a NaN score must not win by default
                // Strict > keeps the first-seen candidate on ties (stable).
                if (!found || score > bestScore)
                {
                    found = true;
                    bestValue = candidate;
                    bestScore = score;
                }
            }

            return found ? new StringMatch(bestValue, bestScore) : (StringMatch?)null;
        }

        /// <summary>
        /// Returns up to <paramref name="count"/> candidates scoring at least <paramref name="minScore"/>,
        /// ordered by descending score (ties broken by input order). An empty list if nothing qualifies.
        /// </summary>
        public static IReadOnlyList<StringMatch> TopMatches(
            string target,
            IEnumerable<string> candidates,
            Func<string, string, double> scorer,
            int count,
            double minScore = double.NegativeInfinity)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (candidates is null) throw new ArgumentNullException(nameof(candidates));
            if (scorer is null) throw new ArgumentNullException(nameof(scorer));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Count must not be negative.");
            if (count == 0) return Array.Empty<StringMatch>();

            // Keep the best `count` so far in a heap whose root is the worst of them, ordered by score and then by input
            // index (so ties keep first-seen order). Every qualifying candidate was kept and sorted, so asking for the top
            // 10 of 10 million held all 10 million.
            var heap = new List<(StringMatch Match, long Index)>();
            long i = 0;
            foreach (string candidate in candidates)
            {
                if (candidate is null) throw new ArgumentException("Candidates must not contain null.", nameof(candidates));
                double score = scorer(target, candidate);
                if (!double.IsNaN(score) && (score >= minScore || double.IsNaN(minScore)))   // as BestMatch: a NaN minScore filters nothing
                {
                    var item = (new StringMatch(candidate, score), i);
                    if (heap.Count < count)
                    {
                        heap.Add(item);
                        SiftUp(heap, heap.Count - 1);
                    }
                    else if (Better(item, heap[0]))
                    {
                        heap[0] = item;
                        SiftDown(heap, 0, heap.Count);
                    }
                }
                i++;
            }

            // Pop the worst to the back each time: the array ends up best first.
            for (int end = heap.Count - 1; end > 0; end--)
            {
                var t = heap[0]; heap[0] = heap[end]; heap[end] = t;
                SiftDown(heap, 0, end);
            }
            var result = new StringMatch[heap.Count];
            for (int k = 0; k < heap.Count; k++)
                result[k] = heap[k].Match;
            return result;
        }

        private static bool Better((StringMatch Match, long Index) x, (StringMatch Match, long Index) y)
            => x.Match.Score > y.Match.Score || (x.Match.Score == y.Match.Score && x.Index < y.Index);

        private static void SiftUp(List<(StringMatch Match, long Index)> heap, int k)
        {
            while (k > 0)
            {
                int parent = (k - 1) >> 1;
                if (!Better(heap[parent], heap[k])) break;
                var t = heap[parent]; heap[parent] = heap[k]; heap[k] = t;
                k = parent;
            }
        }

        private static void SiftDown(List<(StringMatch Match, long Index)> heap, int k, int count)
        {
            while (true)
            {
                int worst = k, l = 2 * k + 1, r = l + 1;
                if (l < count && Better(heap[worst], heap[l])) worst = l;
                if (r < count && Better(heap[worst], heap[r])) worst = r;
                if (worst == k) return;
                var t = heap[worst]; heap[worst] = heap[k]; heap[k] = t;
                k = worst;
            }
        }
    }
}
