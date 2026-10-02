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
    /// means more similar (e.g. <c>JaroWinkler.Similarity</c>, or <c>1.0 / (1 + LevenshteinDistance.Between(a, b))</c>)
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
                if (score < minScore) continue;
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

            // Collect qualifying matches tagged with their input index, then do a STABLE sort: descending by
            // score, and by original index when scores tie. List.Sort is not stable, so the index is the
            // explicit tie-breaker that reproduces first-seen order.
            var scored = new List<(StringMatch match, int index)>();
            int i = 0;
            foreach (string candidate in candidates)
            {
                if (candidate is null) throw new ArgumentException("Candidates must not contain null.", nameof(candidates));
                double score = scorer(target, candidate);
                if (score >= minScore)
                    scored.Add((new StringMatch(candidate, score), i));
                i++;
            }

            scored.Sort((x, y) =>
            {
                int byScore = y.match.Score.CompareTo(x.match.Score); // descending
                return byScore != 0 ? byScore : x.index.CompareTo(y.index); // ascending index on ties
            });

            int take = Math.Min(count, scored.Count);
            var result = new StringMatch[take];
            for (int k = 0; k < take; k++)
                result[k] = scored[k].match;
            return result;
        }
    }
}
