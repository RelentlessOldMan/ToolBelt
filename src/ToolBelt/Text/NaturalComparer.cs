// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Text
{
    /// <summary>
    /// Compares strings in "natural" order, so embedded digit runs sort by numeric value rather than
    /// lexically ("img2" &lt; "img10"). Leading zeros do not change a number's value ("x01" and "x1"
    /// compare equal at that run). Nulls sort before all non-null values.
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

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                char cx = x[i], cy = y[j];
                bool dx = IsDigit(cx), dy = IsDigit(cy);

                if (dx && dy)
                {
                    int cmp = CompareNumberRuns(x, ref i, y, ref j);
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

            // Whatever remains: the longer tail is greater.
            return (x.Length - i).CompareTo(y.Length - j);
        }

        private static int CompareNumberRuns(string x, ref int i, string y, ref int j)
        {
            // Skip leading zeros (they carry no value).
            while (i < x.Length && x[i] == '0') i++;
            while (j < y.Length && y[j] == '0') j++;

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
