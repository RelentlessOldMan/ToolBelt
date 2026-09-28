// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Text
{
    /// <summary>
    /// Matches strings against shell-style glob patterns. Supported syntax: <c>*</c> (any run, including
    /// empty), <c>?</c> (any single character), and character classes <c>[abc]</c>, ranges <c>[a-z]</c>,
    /// and negation <c>[!abc]</c> / <c>[^abc]</c>. Everything else matches literally. Matching is
    /// whole-string (anchored). An unterminated <c>[</c> class throws <see cref="FormatException"/>.
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

        private static bool Match(string s, List<Token> tokens, bool ignoreCase)
        {
            int si = 0, ti = 0;
            int starToken = -1, starInput = 0;

            while (si < s.Length)
            {
                if (ti < tokens.Count && tokens[ti].Kind != Kind.Star && MatchOne(tokens[ti], s[si], ignoreCase))
                {
                    si++;
                    ti++;
                }
                else if (ti < tokens.Count && tokens[ti].Kind == Kind.Star)
                {
                    starToken = ti;
                    starInput = si;
                    ti++;
                }
                else if (starToken != -1)
                {
                    // Backtrack: let the last '*' swallow one more input character.
                    ti = starToken + 1;
                    starInput++;
                    si = starInput;
                }
                else
                {
                    return false;
                }
            }

            while (ti < tokens.Count && tokens[ti].Kind == Kind.Star)
                ti++;
            return ti == tokens.Count;
        }

        private static bool MatchOne(Token token, char c, bool ignoreCase)
        {
            switch (token.Kind)
            {
                case Kind.Any:
                    return true;
                case Kind.Literal:
                    return Fold(token.Literal, ignoreCase) == Fold(c, ignoreCase);
                case Kind.Class:
                    char fc = Fold(c, ignoreCase);
                    bool inSet = false;
                    foreach (var (lo, hi) in token.Ranges!)
                    {
                        if (fc >= Fold(lo, ignoreCase) && fc <= Fold(hi, ignoreCase))
                        {
                            inSet = true;
                            break;
                        }
                    }
                    return inSet != token.Negated;
                default:
                    return false;
            }
        }

        private static char Fold(char c, bool ignoreCase) => ignoreCase ? char.ToUpperInvariant(c) : c;

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
            // A ']' immediately after the (optional) negation is a literal member, not the terminator.
            if (i < pattern.Length && pattern[i] == ']')
            {
                ranges.Add((']', ']'));
                i++;
            }

            while (i < pattern.Length && pattern[i] != ']')
            {
                char lo = pattern[i];
                if (i + 2 < pattern.Length && pattern[i + 1] == '-' && pattern[i + 2] != ']')
                {
                    char hi = pattern[i + 2];
                    ranges.Add((lo <= hi ? lo : hi, lo <= hi ? hi : lo));
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
