using System;
using System.Text;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class LevenshteinDistanceTests
    {
        public void KnownDistances()
        {
            Check.Equal(3, LevenshteinDistance.Distance("kitten", "sitting"));
            Check.Equal(3, LevenshteinDistance.Distance("Saturday", "Sunday"));
            Check.Equal(0, LevenshteinDistance.Distance("same", "same"));
            Check.Equal(4, LevenshteinDistance.Distance("", "abcd"));
        }

        public void Similarity_Bounds()
        {
            Check.Close(1.0, LevenshteinDistance.Similarity("abc", "abc"));
            Check.Close(1.0, LevenshteinDistance.Similarity("", ""));
            Check.Close(0.0, LevenshteinDistance.Similarity("abc", "xyz"));
        }

        // Differential: grade the rolling-buffer implementation against a dead-simple full-matrix
        // reference over hundreds of random pairs from a tiny alphabet (forces edits and matches).
        public void Differential_MatchesFullMatrixReference()
        {
            var rng = new Random(4242);
            for (int trial = 0; trial < 2000; trial++)
            {
                string a = RandomString(rng, maxLen: 9);
                string b = RandomString(rng, maxLen: 9);

                int actual = LevenshteinDistance.Distance(a, b);
                int expected = FullMatrix(a, b);
                Check.Equal(expected, actual, $"trial {trial}: '{a}' vs '{b}'");
            }
        }

        public void Properties_HoldOverRandomTrials()
        {
            var rng = new Random(555);
            for (int trial = 0; trial < 1000; trial++)
            {
                string a = RandomString(rng, maxLen: 9);
                string b = RandomString(rng, maxLen: 9);

                int d = LevenshteinDistance.Distance(a, b);

                Check.Equal(0, LevenshteinDistance.Distance(a, a), "distance to self is 0");
                Check.Equal(d, LevenshteinDistance.Distance(b, a), "symmetry");
                Check.True(d >= Math.Abs(a.Length - b.Length), "distance >= length difference");
                Check.True(d <= Math.Max(a.Length, b.Length), "distance <= longer length");
            }
        }

        private static string RandomString(Random rng, int maxLen)
        {
            int len = rng.Next(0, maxLen + 1);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
                sb.Append((char)('a' + rng.Next(0, 3))); // alphabet {a,b,c}
            return sb.ToString();
        }

        // Textbook O(n*m) matrix — deliberately naive so it is obviously correct.
        private static int FullMatrix(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }

            return d[a.Length, b.Length];
        }
    }
}
