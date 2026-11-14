// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Text
{
    /// <summary>What happened to one element in a diff.</summary>
    public enum DiffOp
    {
        /// <summary>Present unchanged in both sides.</summary>
        Equal,
        /// <summary>Present only in the new side (added).</summary>
        Insert,
        /// <summary>Present only in the old side (removed).</summary>
        Delete,
    }

    /// <summary>One step of an edit script: an operation and the line it applies to.</summary>
    public readonly struct DiffLine
    {
        public DiffLine(DiffOp op, string text)
        {
            Op = op;
            Text = text;
        }

        public DiffOp Op { get; }
        public string Text { get; }

        public override string ToString()
        {
            char marker = Op == DiffOp.Insert ? '+' : Op == DiffOp.Delete ? '-' : ' ';
            return marker + " " + Text;
        }
    }

    /// <summary>
    /// A minimal line-based text diff. Produces the shortest edit script of deletions and insertions (no
    /// substitutions) that turns the old text into the new one, computed from a longest-common-subsequence
    /// table. Walking the result and keeping the <see cref="DiffOp.Equal"/>/<see cref="DiffOp.Insert"/> lines
    /// reconstructs the new text's lines (joined with <c>\n</c>: <c>\r\n</c> terminators are not kept); keeping
    /// Equal/Delete reconstructs the old.
    /// </summary>
    /// <remarks>
    /// Lines common to the start and end of both inputs are matched first, so the LCS table covers only the
    /// changed middle: O(n·m) in time, and n·m bits of memory, for a middle of n and m lines — fine for source files and
    /// typical documents, and for large inputs with a local change, but not for two large unrelated inputs: a middle of
    /// more than 2^33 cells (about 92,000 lines each) throws <see cref="ArgumentException"/>. Line splitting treats <c>\r\n</c>
    /// and <c>\n</c> as separators and does not retain the terminators.
    /// </remarks>
    public static class TextDiff
    {
        /// <summary>Diffs two blocks of text line by line.</summary>
        public static IReadOnlyList<DiffLine> Lines(string oldText, string newText)
        {
            if (oldText is null) throw new ArgumentNullException(nameof(oldText));
            if (newText is null) throw new ArgumentNullException(nameof(newText));

            string[] a = SplitLines(oldText);
            string[] b = SplitLines(newText);

            // A common prefix and suffix are always part of some longest common subsequence, so match them
            // directly and run the table only over the middle.
            int prefix = 0;
            while (prefix < a.Length && prefix < b.Length && string.Equals(a[prefix], b[prefix], StringComparison.Ordinal))
                prefix++;
            int suffix = 0;
            while (suffix < a.Length - prefix && suffix < b.Length - prefix
                   && string.Equals(a[a.Length - 1 - suffix], b[b.Length - 1 - suffix], StringComparison.Ordinal))
                suffix++;

            var result = new List<DiffLine>(a.Length + b.Length);
            for (int k = 0; k < prefix; k++)
                result.Add(new DiffLine(DiffOp.Equal, a[k]));

            // LCS lengths over the middle, lcs[i, j] = LCS length of a[prefix + i..] and b[prefix + j..], filled from the
            // bottom-right two rows at a time. The forward walk below only needs, per cell, whether dropping a's line
            // keeps the optimum (lcs[i + 1, j] >= lcs[i, j + 1]), so that is kept as one bit: the full int table took
            // 1.5 GB for 20,000 unrelated lines a side, and past 2^31 cells could not be allocated at all.
            int n = a.Length - prefix - suffix, m = b.Length - prefix - suffix;
            long cells = (long)n * m;
            if (cells > 1L << 33)
                throw new ArgumentException($"The texts differ in {n} and {m} lines; diffing them needs more than 2^33 table cells.", nameof(newText));
            var down = new ulong[(cells + 63) >> 6];
            var next = new int[m + 1];
            var cur = new int[m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    cur[j] = string.Equals(a[prefix + i], b[prefix + j], StringComparison.Ordinal)
                        ? next[j + 1] + 1
                        : Math.Max(next[j], cur[j + 1]);
                    if (next[j] >= cur[j + 1])
                    {
                        long bit = (long)i * m + j;
                        down[bit >> 6] |= 1UL << (int)(bit & 63);
                    }
                }
                int[] t = next; next = cur; cur = t;
            }

            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (string.Equals(a[prefix + x], b[prefix + y], StringComparison.Ordinal))
                {
                    result.Add(new DiffLine(DiffOp.Equal, a[prefix + x]));
                    x++; y++;
                }
                else if ((down[((long)x * m + y) >> 6] & (1UL << (int)(((long)x * m + y) & 63))) != 0)
                {
                    // Dropping a[x] keeps (or ties) the optimum — emit it as a deletion.
                    result.Add(new DiffLine(DiffOp.Delete, a[prefix + x]));
                    x++;
                }
                else
                {
                    result.Add(new DiffLine(DiffOp.Insert, b[prefix + y]));
                    y++;
                }
            }
            while (x < n) result.Add(new DiffLine(DiffOp.Delete, a[prefix + x++]));
            while (y < m) result.Add(new DiffLine(DiffOp.Insert, b[prefix + y++]));

            for (int k = a.Length - suffix; k < a.Length; k++)
                result.Add(new DiffLine(DiffOp.Equal, a[k]));
            return result;
        }

        /// <summary>Renders a diff as a unified-ish <c>+</c>/<c>-</c>/space-prefixed text block.</summary>
        public static string Format(IReadOnlyList<DiffLine> diff)
        {
            if (diff is null) throw new ArgumentNullException(nameof(diff));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < diff.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(diff[i].ToString());
            }
            return sb.ToString();
        }

        private static string[] SplitLines(string text)
        {
            if (text.Length == 0) return Array.Empty<string>();
            return text.Replace("\r\n", "\n").Split('\n');
        }
    }
}
