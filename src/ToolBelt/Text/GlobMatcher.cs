// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Text
{
    /// <summary>
    /// Matches strings against shell-style glob patterns. Supported syntax: <c>*</c> (any run, including
    /// empty), <c>?</c> (any single character), and character classes <c>[abc]</c>, ranges <c>[a-z]</c>,
    /// and negation <c>[!abc]</c> / <c>[^abc]</c>. Everything else matches literally. Matching is
    /// whole-string (anchored). An unterminated <c>[</c> class throws <see cref="FormatException"/>. A reversed range
    /// such as <c>[z-a]</c> is empty (as in POSIX), and a <c>]</c> first in a class is a member that may start a range
    /// (<c>[]-a]</c>). With <c>ignoreCase</c>, characters are equal when the invariant upper- and lower-case mappings
    /// link them, directly or through each other ('ſ', 's' and 'S' all match), and a character matches a class when any
    /// of those is in a range as written. The mappings are the runtime's: .NET Framework's leave 'ſ', the Kelvin sign and
    /// some others alone, so there they match only themselves. Characters are UTF-16 code units, so <c>?</c> matches one half of a surrogate pair. Time is linear in
    /// the input for short patterns: O(n·m/64) at worst, plus O(m) for each distinct input character.
    /// </summary>
    public static class GlobMatcher
    {
        private enum Kind { Literal, Any, Star, Class }

        private readonly struct Token
        {
            public readonly Kind Kind;
            public readonly char Literal;
            public readonly bool Negated;
            public readonly (char Lo, char Hi)[]? Ranges;

            public Token(Kind kind, char literal = '\0', bool negated = false, (char, char)[]? ranges = null)
            {
                Kind = kind;
                Literal = literal;
                Negated = negated;
                Ranges = ranges;
            }
        }

        /// <summary>Returns whether <paramref name="input"/> matches <paramref name="pattern"/> in full.</summary>
        public static bool IsMatch(string input, string pattern, bool ignoreCase = false)
        {
            if (input is null)
                throw new ArgumentNullException(nameof(input));
            if (pattern is null)
                throw new ArgumentNullException(nameof(pattern));

            var tokens = Parse(pattern);
            return Match(input, tokens, ignoreCase);
        }

        // Backtracking on the last star was O(n·m): "*" + 20,000 'a's + "b" against 200,000 'a's took 14 s. The stars
        // split the pattern into fixed-length segments; the first and last are anchored, and each middle one is taken at
        // its leftmost occurrence (always safe, since every token matches exactly one character), found with a
        // bit-parallel scan: O(n·m/64) plus O(m) for each distinct input character.
        private static bool Match(string s, List<Token> tokens, bool ignoreCase)
        {
            var segments = new List<(int Start, int Length)>();
            int segStart = 0;
            for (int t = 0; t <= tokens.Count; t++)
            {
                if (t < tokens.Count && tokens[t].Kind != Kind.Star) continue;
                segments.Add((segStart, t - segStart));
                segStart = t + 1;
            }
            (int Start, int Length) first = segments[0], last = segments[segments.Count - 1];
            if (segments.Count == 1) return s.Length == first.Length && MatchAt(s, 0, tokens, first, ignoreCase);
            if (first.Length > s.Length - last.Length) return false;
            if (!MatchAt(s, 0, tokens, first, ignoreCase) || !MatchAt(s, s.Length - last.Length, tokens, last, ignoreCase)) return false;
            int pos = first.Length, limit = s.Length - last.Length;
            for (int g = 1; g < segments.Count - 1; g++)
            {
                pos = FindLeftmost(s, pos, limit, tokens, segments[g], ignoreCase);
                if (pos < 0) return false;
            }
            return true;
        }

        private static bool MatchAt(string s, int at, List<Token> tokens, (int Start, int Length) segment, bool ignoreCase)
        {
            for (int j = 0; j < segment.Length; j++)
                if (!MatchOne(tokens[segment.Start + j], s[at + j], ignoreCase)) return false;
            return true;
        }

        // Shift-And: bit j of the state is set when the segment's first j + 1 tokens match the input ending here. Returns
        // the index just past the leftmost occurrence inside s[from, limit), or -1.
        private static int FindLeftmost(string s, int from, int limit, List<Token> tokens, (int Start, int Length) segment, bool ignoreCase)
        {
            int k = segment.Length, words = (k + 63) >> 6;
            int topWord = (k - 1) >> 6;
            ulong topBit = 1UL << ((k - 1) & 63);
            var state = new ulong[words];
            var masks = new Dictionary<char, ulong[]>();
            for (int i = from; i < limit; i++)
            {
                char c = s[i];
                if (!masks.TryGetValue(c, out ulong[]? mask))
                {
                    mask = new ulong[words];
                    for (int j = 0; j < k; j++)
                        if (MatchOne(tokens[segment.Start + j], c, ignoreCase)) mask[j >> 6] |= 1UL << (j & 63);
                    if ((long)masks.Count * words > 1 << 20) masks.Clear();   // keep the cache to about 8 MB
                    masks[c] = mask;
                }
                ulong carry = 1;
                for (int w = 0; w < words; w++)
                {
                    ulong next = state[w] >> 63;
                    state[w] = ((state[w] << 1) | carry) & mask[w];
                    carry = next;
                }
                if ((state[topWord] & topBit) != 0) return i + 1;
            }
            return -1;
        }

        private static bool MatchOne(Token token, char c, bool ignoreCase)
        {
            switch (token.Kind)
            {
                case Kind.Any:
                    return true;
                case Kind.Literal:
                    char p = token.Literal;
                    return c == p || (ignoreCase && CaseClasses.Value.Root[c] == CaseClasses.Value.Root[p]);
                case Kind.Class:
                    // Ranges are compared as written (folding the endpoints would turn [Z-a] into the empty range
                    // Z..A); case-insensitivity instead asks whether any character of c's case class is in them.
                    bool inSet = InRanges(token.Ranges!, c) || (ignoreCase && InCaseClass(token.Ranges!, c));
                    return inSet != token.Negated;
                default:
                    return false;
            }
        }

        private static bool InCaseClass((char Lo, char Hi)[] ranges, char c)
        {
            if (CaseClasses.Value.Members.TryGetValue(CaseClasses.Value.Root[c], out char[]? members))
                foreach (char m in members)
                    if (InRanges(ranges, m)) return true;
            return false;
        }

        // Characters linked by the runtime's invariant upper- and lower-case mappings, closed transitively: 'ſ', 's' and
        // 'S'; 'k', 'K' and the Kelvin sign; 'ǅ', 'Ǆ' and 'ǆ'. One step of the mapping was not symmetric, so 'ſ' matched
        // "S" but not "s", and 'k' matched the Kelvin sign but 'K' did not.
        private static readonly Lazy<(char[] Root, Dictionary<char, char[]> Members)> CaseClasses = new Lazy<(char[], Dictionary<char, char[]>)>(() =>
        {
            var root = new char[char.MaxValue + 1];
            for (int i = 0; i <= char.MaxValue; i++) root[i] = (char)i;
            char Find(char x)
            {
                while (root[x] != x) x = root[x] = root[root[x]];
                return x;
            }
            void Union(char x, char y)
            {
                char rx = Find(x), ry = Find(y);
                if (rx != ry) root[Math.Max(rx, ry)] = (char)Math.Min(rx, ry);
            }
            for (int i = 0; i <= char.MaxValue; i++)
            {
                char x = (char)i;
                if (char.IsSurrogate(x)) continue;
                Union(x, char.ToUpperInvariant(x));
                Union(x, char.ToLowerInvariant(x));
            }
            var groups = new Dictionary<char, List<char>>();
            for (int i = 0; i <= char.MaxValue; i++)
            {
                char r = root[i] = Find((char)i);
                if (r == i) continue;
                if (!groups.TryGetValue(r, out List<char>? list)) groups[r] = list = new List<char> { r };
                list.Add((char)i);
            }
            var members = new Dictionary<char, char[]>(groups.Count);
            foreach (var g in groups) members[g.Key] = g.Value.ToArray();
            return (root, members);
        });

        private static bool InRanges((char Lo, char Hi)[] ranges, char c)
        {
            foreach (var (lo, hi) in ranges)
                if (c >= lo && c <= hi)
                    return true;
            return false;
        }

        private static List<Token> Parse(string pattern)
        {
            var tokens = new List<Token>(pattern.Length);
            int i = 0;
            while (i < pattern.Length)
            {
                char c = pattern[i];
                switch (c)
                {
                    case '*':
                        // Collapse consecutive stars — they are equivalent to one.
                        if (tokens.Count == 0 || tokens[tokens.Count - 1].Kind != Kind.Star)
                            tokens.Add(new Token(Kind.Star));
                        i++;
                        break;
                    case '?':
                        tokens.Add(new Token(Kind.Any));
                        i++;
                        break;
                    case '[':
                        tokens.Add(ParseClass(pattern, ref i));
                        break;
                    default:
                        tokens.Add(new Token(Kind.Literal, c));
                        i++;
                        break;
                }
            }
            return tokens;
        }

        private static Token ParseClass(string pattern, ref int i)
        {
            int start = i;
            i++; // skip '['

            bool negated = false;
            if (i < pattern.Length && (pattern[i] == '!' || pattern[i] == '^'))
            {
                negated = true;
                i++;
            }

            var ranges = new List<(char, char)>();
            // A ']' immediately after the (optional) negation is a member, not the terminator, and can start a range:
            // "[]-a]" was read as the three members ']', '-' and 'a'.
            bool first = true;
            while (i < pattern.Length && (pattern[i] != ']' || first))
            {
                first = false;
                char lo = pattern[i];
                if (i + 2 < pattern.Length && pattern[i + 1] == '-' && pattern[i + 2] != ']')
                {
                    // A reversed range was swapped, so "[z-a]" matched every lower-case letter; POSIX makes it empty.
                    char hi = pattern[i + 2];
                    if (lo <= hi) ranges.Add((lo, hi));
                    i += 3;
                }
                else
                {
                    ranges.Add((lo, lo));
                    i++;
                }
            }

            if (i >= pattern.Length)
                throw new FormatException($"Unterminated character class starting at index {start}.");

            i++; // skip ']'
            return new Token(Kind.Class, negated: negated, ranges: ranges.ToArray());
        }
    }
}
