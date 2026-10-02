using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class TextDiffTests
    {
        public void IdenticalText_IsAllEqual()
        {
            var d = TextDiff.Lines("a\nb\nc", "a\nb\nc");
            Check.True(d.All(l => l.Op == DiffOp.Equal));
            Check.Equal(3, d.Count);
        }

        public void DisjointText_IsAllDeletesThenInserts()
        {
            var d = TextDiff.Lines("a\nb", "x\ny");
            Check.Equal(4, d.Count); // 2 deletes + 2 inserts, no shared line
            Check.Equal(2, d.Count(l => l.Op == DiffOp.Delete));
            Check.Equal(2, d.Count(l => l.Op == DiffOp.Insert));
        }

        public void EmptyOld_IsAllInserts()
        {
            var d = TextDiff.Lines("", "a\nb");
            Check.Equal(2, d.Count);
            Check.True(d.All(l => l.Op == DiffOp.Insert));
        }

        public void EmptyNew_IsAllDeletes()
        {
            var d = TextDiff.Lines("a\nb", "");
            Check.Equal(2, d.Count);
            Check.True(d.All(l => l.Op == DiffOp.Delete));
        }

        public void CrLf_IsNormalized()
        {
            var d = TextDiff.Lines("a\r\nb", "a\nb");
            Check.True(d.All(l => l.Op == DiffOp.Equal)); // line terminators don't count as a difference
        }

        public void Format_UsesExpectedMarkers()
        {
            var d = TextDiff.Lines("keep\nold", "keep\nnew");
            string text = TextDiff.Format(d);
            Check.True(text.Contains("  keep"));
            Check.True(text.Contains("- old"));
            Check.True(text.Contains("+ new"));
        }

        // --- Property tests over randomized line lists (the real coverage) ---

        public void Property_Reconstruction()
        {
            var r = new DeterministicRandom(42);
            for (int trial = 0; trial < 500; trial++)
            {
                string[] a = RandomLines(r);
                string[] b = RandomLines(r);
                var diff = TextDiff.Lines(string.Join("\n", a), string.Join("\n", b));

                // Equal+Insert must rebuild the new side; Equal+Delete must rebuild the old side.
                var rebuiltNew = diff.Where(l => l.Op != DiffOp.Delete).Select(l => l.Text);
                var rebuiltOld = diff.Where(l => l.Op != DiffOp.Insert).Select(l => l.Text);
                Check.True(rebuiltNew.SequenceEqual(b), $"new reconstruction failed at trial {trial}");
                Check.True(rebuiltOld.SequenceEqual(a), $"old reconstruction failed at trial {trial}");
            }
        }

        public void Property_EqualCountMatchesLcs()
        {
            var r = new DeterministicRandom(99);
            for (int trial = 0; trial < 500; trial++)
            {
                string[] a = RandomLines(r);
                string[] b = RandomLines(r);
                var diff = TextDiff.Lines(string.Join("\n", a), string.Join("\n", b));

                int equalCount = diff.Count(l => l.Op == DiffOp.Equal);
                // Independent forward-DP LCS reference (opposite orientation to the implementation).
                Check.Equal(LcsForward(a, b), equalCount, $"trial {trial}");
            }
        }

        public void Validation_Throws()
        {
            Check.Throws<ArgumentNullException>(() => TextDiff.Lines(null!, "x"));
            Check.Throws<ArgumentNullException>(() => TextDiff.Lines("x", null!));
            Check.Throws<ArgumentNullException>(() => TextDiff.Format(null!));
        }

        private static string[] RandomLines(DeterministicRandom r)
        {
            int n = r.Next(0, 8);
            var lines = new string[n];
            for (int i = 0; i < n; i++)
                lines[i] = ((char)('a' + r.Next(0, 4))).ToString(); // tiny alphabet -> frequent matches
            return lines;
        }

        private static int LcsForward(string[] a, string[] b)
        {
            int n = a.Length, m = b.Length;
            var dp = new int[n + 1, m + 1];
            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                    dp[i, j] = string.Equals(a[i - 1], b[j - 1], StringComparison.Ordinal)
                        ? dp[i - 1, j - 1] + 1
                        : Math.Max(dp[i - 1, j], dp[i, j - 1]);
            return dp[n, m];
        }
    }
}
