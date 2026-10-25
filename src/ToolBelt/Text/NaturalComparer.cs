// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Text
{
    /// <summary>
    /// Compares strings in "natural" order, so embedded digit runs sort by numeric value rather than
    /// lexically ("img2" &lt; "img10"). Leading zeros do not change a number's value; only when two strings
    /// are otherwise equal do they break the tie, fewer zeros first ("x1" &lt; "x01" &lt; "x2"), so distinct
    /// strings never compare equal and a sorted set keeps them all. Nulls sort before all non-null values.
    /// </summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        /// <summary>Case-sensitive (ordinal) natural comparer.</summary>
        public static readonly NaturalComparer Ordinal = new NaturalComparer(ignoreCase: false);

        /// <summary>Case-insensitive (ordinal) natural comparer.</summary>
        public static readonly NaturalComparer OrdinalIgnoreCase = new NaturalComparer(ignoreCase: true);

        private readonly bool _ignoreCase;

        public NaturalComparer(bool ignoreCase) => _ignoreCase = ignoreCase;

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            int i = 0, j = 0, tieBreak = 0;
            while (i < x.Length && j < y.Length)
            {
                char cx = x[i], cy = y[j];
                bool dx = IsDigit(cx), dy = IsDigit(cy);

                if (dx && dy)
                {
                    int cmp = CompareNumberRuns(x, ref i, y, ref j, ref tieBreak);
                    if (cmp != 0)
                        return cmp;
                    // Equal magnitude: both runs consumed, continue with the tails.
                }
                else
                {
                    int cmp = CompareChar(cx, cy);
                    if (cmp != 0)
                        return cmp;
                    i++;
                    j++;
                }
            }

            // Whatever remains: the longer tail is greater. Otherwise the first difference in leading zeros decides.
            int tail = (x.Length - i).CompareTo(y.Length - j);
            return tail != 0 ? tail : tieBreak;
        }

        private static int CompareNumberRuns(string x, ref int i, string y, ref int j, ref int tieBreak)
        {
            // Skip leading zeros (they carry no value, but remember the first run where their count differs).
            int xZeros = i, yZeros = j;
            while (i < x.Length && x[i] == '0') i++;
            while (j < y.Length && y[j] == '0') j++;
            xZeros = i - xZeros;
            yZeros = j - yZeros;
            if (tieBreak == 0 && xZeros != yZeros)
                tieBreak = xZeros < yZeros ? -1 : 1;

            int xStart = i, yStart = j;
            while (i < x.Length && IsDigit(x[i])) i++;
            while (j < y.Length && IsDigit(y[j])) j++;

            int xLen = i - xStart, yLen = j - yStart;
            if (xLen != yLen)
                return xLen < yLen ? -1 : 1; // more significant digits ⇒ larger number

            for (int k = 0; k < xLen; k++)
            {
                if (x[xStart + k] != y[yStart + k])
                    return x[xStart + k] < y[yStart + k] ? -1 : 1;
            }
            return 0;
        }

        private int CompareChar(char a, char b)
        {
            if (_ignoreCase)
            {
                a = char.ToUpperInvariant(a);
                b = char.ToUpperInvariant(b);
            }
            return a == b ? 0 : (a < b ? -1 : 1);
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';
    }
}
