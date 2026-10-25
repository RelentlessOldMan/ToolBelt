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
    /// reconstructs the new text exactly; keeping Equal/Delete reconstructs the old.
    /// </summary>
    /// <remarks>
    /// Lines common to the start and end of both inputs are matched first, so the LCS table covers only the
    /// changed middle: O(n·m) in time and memory for a middle of n and m lines — fine for source files and
    /// typical documents, and for large inputs with a local change, but not for two large unrelated inputs. Line splitting treats <c>\r\n</c>
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

            // LCS length table over the middle: lcs[i, j] = LCS length of a[prefix + i..] and b[prefix + j..].
            // Filled from the bottom-right so the forward walk below can greedily take the direction that
            // preserves the optimum.
            int n = a.Length - prefix - suffix, m = b.Length - prefix - suffix;
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = string.Equals(a[prefix + i], b[prefix + j], StringComparison.Ordinal)
                        ? lcs[i + 1, j + 1] + 1
                        : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (string.Equals(a[prefix + x], b[prefix + y], StringComparison.Ordinal))
                {
                    result.Add(new DiffLine(DiffOp.Equal, a[prefix + x]));
                    x++; y++;
                }
                else if (lcs[x + 1, y] >= lcs[x, y + 1])
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
