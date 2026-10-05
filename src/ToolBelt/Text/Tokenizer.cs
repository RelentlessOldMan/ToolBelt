// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>Rules for <see cref="Tokenizer"/>.</summary>
    public sealed class TokenizerOptions
    {
        /// <summary>Characters that separate tokens (default: space and tab).</summary>
        public char[] Delimiters { get; set; } = { ' ', '\t' };

        /// <summary>
        /// True (default): runs of delimiters count as one and leading/trailing delimiters are ignored — whitespace style.
        /// False: every delimiter separates, so empty fields are kept — CSV style.
        /// </summary>
        public bool CollapseDelimiters { get; set; } = true;

        /// <summary>Characters that open and close a quoted section (default: double quote).</summary>
        public char[] Quotes { get; set; } = { '"' };

        /// <summary>Inside quotes, a doubled quote character means one literal quote (CSV style). Default true.</summary>
        public bool DoubledQuoteEscape { get; set; } = true;

        /// <summary>An escape character that makes the next character literal, inside or outside quotes (e.g. '\\'); none by default.</summary>
        public char? Escape { get; set; }

        /// <summary>Trim whitespace from unquoted tokens (useful with non-whitespace delimiters).</summary>
        public bool TrimUnquoted { get; set; }

        /// <summary>Whitespace-separated, double-quoted, backslash escapes — shell-like.</summary>
        public static TokenizerOptions Shell() => new TokenizerOptions { Escape = '\\', DoubledQuoteEscape = false, Quotes = new[] { '"', '\'' } };

        /// <summary>RFC 4180 CSV: every delimiter separates, doubled quotes escape, no escape character.</summary>
        public static TokenizerOptions Csv(char delimiter = ',') => new TokenizerOptions { Delimiters = new[] { delimiter }, CollapseDelimiters = false };
    }

    /// <summary>One token and where it came from in the source.</summary>
    public readonly struct Token
    {
        public Token(string value, int start, int length, bool wasQuoted)
        {
            Value = value;
            Start = start;
            Length = length;
            WasQuoted = wasQuoted;
        }

        /// <summary>The token with quotes and escapes resolved.</summary>
        public string Value { get; }

        /// <summary>Index of the token's first source character.</summary>
        public int Start { get; }

        /// <summary>Number of source characters the token spans (quotes and escapes included).</summary>
        public int Length { get; }

        /// <summary>True if any part of the token was quoted (so an empty quoted token is distinguishable from nothing).</summary>
        public bool WasQuoted { get; }

        public override string ToString() => Value;
    }

    /// <summary>
    /// A small, configurable, quote- and escape-aware splitter — the one audited primitive behind "split this line into
    /// fields" whether the rules are shell-like (<see cref="TokenizerOptions.Shell"/>) or CSV (<see cref="TokenizerOptions.Csv"/>).
    /// Quotes may appear anywhere in a token and are removed (<c>ab"c d"e</c> → <c>abc de</c>); an unterminated quote or a
    /// trailing escape is a <see cref="FormatException"/> naming the position. Tokens carry their source span, so callers
    /// can report errors precisely.
    /// </summary>
    public static class Tokenizer
    {
        public static IReadOnlyList<Token> Tokenize(string text, TokenizerOptions? options = null)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            options ??= new TokenizerOptions();
            var tokens = new List<Token>();
            var sb = new StringBuilder();
            int i = 0, n = text.Length;
            bool collapse = options.CollapseDelimiters;

            if (!collapse && n == 0) { tokens.Add(new Token("", 0, 0, false)); return tokens; }
            while (i <= n)
            {
                if (collapse)
                {
                    while (i < n && IsDelimiter(text[i], options)) i++;
                    if (i >= n) break;
                }
                int start = i;
                bool quoted = false, anyQuoted = false;
                char quote = '\0';
                sb.Clear();
                int lastNonSpace = -1; // for TrimUnquoted: length of sb up to the last non-space unquoted char
                bool leading = true;
                while (i < n)
                {
                    char c = text[i];
                    if (options.Escape is char esc && c == esc)
                    {
                        if (i + 1 >= n) throw new FormatException($"Trailing escape character at position {i}.");
                        sb.Append(text[i + 1]);
                        lastNonSpace = sb.Length;
                        leading = false;
                        i += 2;
                        continue;
                    }
                    if (quoted)
                    {
                        if (c == quote)
                        {
                            if (options.DoubledQuoteEscape && i + 1 < n && text[i + 1] == quote) { sb.Append(quote); i += 2; continue; }
                            quoted = false;
                            lastNonSpace = sb.Length;
                            i++;
                            continue;
                        }
                        sb.Append(c);
                        i++;
                        continue;
                    }
                    if (IsDelimiter(c, options)) break;
                    if (Array.IndexOf(options.Quotes, c) >= 0)
                    {
                        if (options.TrimUnquoted && leading) sb.Clear();
                        quoted = anyQuoted = true;
                        quote = c;
                        leading = false;
                        i++;
                        continue;
                    }
                    if (options.TrimUnquoted && leading && char.IsWhiteSpace(c)) { i++; continue; }
                    sb.Append(c);
                    leading = false;
                    if (!char.IsWhiteSpace(c)) lastNonSpace = sb.Length;
                    i++;
                }
                if (quoted) throw new FormatException($"Unterminated quote starting in the token at position {start}.");
                if (options.TrimUnquoted && lastNonSpace >= 0 && lastNonSpace < sb.Length) sb.Length = lastNonSpace;
                else if (options.TrimUnquoted && lastNonSpace < 0) sb.Clear();
                tokens.Add(new Token(sb.ToString(), start, i - start, anyQuoted));

                if (i >= n) break;
                i++; // skip the delimiter
                if (!collapse && i == n) { tokens.Add(new Token("", n, 0, false)); break; } // trailing delimiter: final empty field
            }
            return tokens;
        }

        /// <summary>Just the token values.</summary>
        public static string[] Split(string text, TokenizerOptions? options = null)
        {
            var tokens = Tokenize(text, options);
            var result = new string[tokens.Count];
            for (int i = 0; i < result.Length; i++) result[i] = tokens[i].Value;
            return result;
        }

        private static bool IsDelimiter(char c, TokenizerOptions o) => Array.IndexOf(o.Delimiters, c) >= 0;
    }
}
