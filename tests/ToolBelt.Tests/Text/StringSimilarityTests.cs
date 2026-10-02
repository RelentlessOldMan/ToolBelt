using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class StringSimilarityTests
    {
        // A deterministic scorer: higher = closer length. Lets us assert exact ranking.
        private static readonly Func<string, string, double> ByLength =
            (a, b) => -Math.Abs(a.Length - b.Length);

        public void BestMatch_PicksHighestScore()
        {
            var m = StringSimilarity.BestMatch("abcd", new[] { "x", "yy", "zzzz", "www" }, ByLength);
            Check.True(m.HasValue);
            Check.Equal("zzzz", m!.Value.Value); // same length as "abcd" -> score 0, the max
            Check.Equal(0.0, m.Value.Score);
        }

        public void BestMatch_TieGoesToFirstSeen()
        {
            // "pp" and "qq" both length 2 -> equal score; first-seen ("pp") must win.
            var m = StringSimilarity.BestMatch("ab", new[] { "pp", "qq" }, ByLength);
            Check.Equal("pp", m!.Value.Value);
        }

        public void BestMatch_EmptyCandidates_ReturnsNull()
        {
            var m = StringSimilarity.BestMatch("ab", Array.Empty<string>(), ByLength);
            Check.False(m.HasValue);
        }

        public void BestMatch_MinScore_FiltersOutEverything()
        {
            // All candidates differ in length from "a" (score < 0); minScore 0 excludes them all.
            var m = StringSimilarity.BestMatch("a", new[] { "bb", "ccc" }, ByLength, minScore: 0.0);
            Check.False(m.HasValue);
        }

        public void TopMatches_DescendingAndCapped()
        {
            var top = StringSimilarity.TopMatches(
                "abcd", new[] { "x", "yy", "zzzz", "www", "qqqq" }, ByLength, count: 2);
            Check.Equal(2, top.Count);
            // "zzzz" and "qqqq" both length 4 -> top two, in first-seen order.
            Check.Equal("zzzz", top[0].Value);
            Check.Equal("qqqq", top[1].Value);
            // Non-increasing scores.
            Check.True(top[0].Score >= top[1].Score);
        }

        public void TopMatches_IsStableOnTies()
        {
            // All equal length -> all tie; order must equal input order.
            var top = StringSimilarity.TopMatches(
                "ab", new[] { "11", "22", "33", "44" }, ByLength, count: 4);
            Check.True(top.Select(t => t.Value).SequenceEqual(new[] { "11", "22", "33", "44" }));
        }

        public void TopMatches_CountZero_IsEmpty()
        {
            var top = StringSimilarity.TopMatches("ab", new[] { "x" }, ByLength, count: 0);
            Check.Equal(0, top.Count);
        }

        public void TopMatches_MinScoreFilters()
        {
            var top = StringSimilarity.TopMatches(
                "ab", new[] { "cd", "eee", "f" }, ByLength, count: 10, minScore: 0.0);
            // Only "cd" (length 2) scores 0; the rest are negative.
            Check.Equal(1, top.Count);
            Check.Equal("cd", top[0].Value);
        }

        public void RealisticDidYouMean_WithPrefixScorer()
        {
            // Common-prefix ratio scorer, the kind a caller would actually inject.
            Func<string, string, double> prefix = (a, b) =>
            {
                int n = Math.Min(a.Length, b.Length), i = 0;
                while (i < n && a[i] == b[i]) i++;
                return (double)i / Math.Max(1, Math.Max(a.Length, b.Length));
            };
            var m = StringSimilarity.BestMatch("recieve", new[] { "deceive", "receive", "retrieve" }, prefix);
            Check.Equal("receive", m!.Value.Value); // shares "rec" prefix
        }

        public void Validation_Throws()
        {
            Check.Throws<ArgumentNullException>(() => StringSimilarity.BestMatch(null!, new[] { "a" }, ByLength));
            Check.Throws<ArgumentNullException>(() => StringSimilarity.BestMatch("a", null!, ByLength));
            Check.Throws<ArgumentNullException>(() => StringSimilarity.BestMatch("a", new[] { "a" }, null!));
            Check.Throws<ArgumentException>(() => StringSimilarity.BestMatch("a", new string[] { null! }, ByLength));
            Check.Throws<ArgumentOutOfRangeException>(() => StringSimilarity.TopMatches("a", new[] { "a" }, ByLength, -1));
        }
    }
}
