// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// Converts identifiers between the common casing conventions. Input may be in any style — the string
    /// is first tokenized into words (splitting on separators, camelCase humps, and acronym boundaries),
    /// then rejoined in the requested style. A capital starts a word after a lower-case letter, and after a digit or
    /// another capital when a lower-case letter follows it ("v2Beta" → v2 | Beta, "HTTPServer" → HTTP | Server, but
    /// "A1B" is one word). Conversions are idempotent per style and stable across styles, except where camelCase and
    /// PascalCase cannot show a boundary: a word starting with a digit joins the word before it ("v_1" → "v1"), and so
    /// does a word whose second character is not a lower-case letter when the word before is one letter or ends in a
    /// digit ("x_y_z" → "xYZ" → "xYz", "is_a_b" → "IsAB" → "is_ab", "md5_x" → "md5X" → "md5x"). Casing is invariant, with three
    /// additions so every style's output is properly cased: 'İ' lowers to 'i', 'ı' uppers to 'I' and 'ß' to "SS" ("Ss" at
    /// the start of a word), so the Turkish dotted and dotless i and the 'ß' don't survive a trip through an upper-case
    /// style. A letter with no case of its own continues the word it is in; a digraph such as 'ǆ' is titlecased ('ǅ').
    /// </summary>
    public static class CaseConverter
    {
        /// <summary>Splits arbitrary text into its component words. Public so callers can build custom joins.</summary>
        public static IReadOnlyList<string> ToWords(string value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            var words = new List<string>();
            var current = new StringBuilder();

            // Walks code points (a surrogate pair is one letter); combining marks stay with the letter before them.
            bool prevUpper = false, prevLower = false, prevDigit = false;
            for (int i = 0; i < value.Length;)
            {
                int len = char.IsSurrogatePair(value, i) ? 2 : 1;
                var category = CharUnicodeInfo.GetUnicodeCategory(value, i);

                if (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark
                    || category == UnicodeCategory.EnclosingMark)
                {
                    if (current.Length > 0)
                        current.Append(value, i, len);
                    i += len;
                    continue;
                }

                if (!char.IsLetterOrDigit(value, i))
                {
                    Flush(current, words); // separator run
                    i += len;
                    continue;
                }

                // Upper and lower mean "has the other case": a letter without one (ß had no invariant upper case, İ no
                // lower) split "STRAßE" into STR | Aß | E. Such a letter continues the run it is in.
                bool upper = IsCasedUpper(value, i, len);
                bool lower = !upper && IsCasedLower(value, i, len);
                bool digit = char.IsDigit(value, i);
                if (current.Length > 0)
                {
                    // lower -> upper : a camelCase hump ("fooBar" => foo | Bar). After a digit only when a lower-case
                    // letter follows ("md5Hash" => md5 | Hash): "A1B", CONSTANT_CASE for "a1b", read back as A1 | B.
                    bool humpBoundary = upper && (prevLower || (prevDigit && LowerFollows(value, i + len)));

                    // UPPER UPPER lower : end of an acronym ("HTTPServer" => HTTP | Server)
                    bool acronymBoundary = upper && prevUpper && LowerFollows(value, i + len);

                    if (humpBoundary || acronymBoundary)
                        Flush(current, words);
                }

                if (upper || lower || digit || current.Length == 0)
                {
                    prevUpper = upper;
                    // A word starting with 'ß' (no capital of its own) is lower-case: camelCase "ßBa" read back as one
                    // word, so "ß-Ba" → "ßBa" → "ßba".
                    prevLower = lower || (current.Length == 0 && !upper && !digit && char.IsLower(value, i));
                    prevDigit = digit;
                }
                current.Append(value, i, len);
                i += len;
            }

            Flush(current, words);
            return words;
        }

        /// <summary>camelCase — first word lower, subsequent words capitalized, no separators.</summary>
        public static string ToCamelCase(string value)
        {
            var words = ToWords(value);
            var sb = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
                sb.Append(i == 0 ? Lower(words[i]) : Capitalize(words[i]));
            return sb.ToString();
        }

        /// <summary>PascalCase — every word capitalized, no separators.</summary>
        public static string ToPascalCase(string value)
        {
            var sb = new StringBuilder();
            foreach (var word in ToWords(value))
                sb.Append(Capitalize(word));
            return sb.ToString();
        }

        /// <summary>snake_case — lowercase words joined by underscores.</summary>
        public static string ToSnakeCase(string value) => JoinLower(ToWords(value), '_');

        /// <summary>kebab-case — lowercase words joined by hyphens.</summary>
        public static string ToKebabCase(string value) => JoinLower(ToWords(value), '-');

        /// <summary>CONSTANT_CASE — uppercase words joined by underscores.</summary>
        public static string ToConstantCase(string value) => JoinUpper(ToWords(value), '_');

        /// <summary>Title Case — every word capitalized, joined by single spaces.</summary>
        public static string ToTitleCase(string value)
        {
            var words = ToWords(value);
            var sb = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(Capitalize(words[i]));
            }
            return sb.ToString();
        }

        // The next letter's index, past combining marks: in decomposed text "XMLÉcole" the É's accent hid the lower-case
        // letter after it, so the acronym never ended.
        private static int NextBase(string s, int i)
        {
            while (i < s.Length)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(s, i);
                if (category != UnicodeCategory.NonSpacingMark && category != UnicodeCategory.SpacingCombiningMark
                    && category != UnicodeCategory.EnclosingMark) break;
                i += char.IsSurrogatePair(s, i) ? 2 : 1;
            }
            return i;
        }

        private static int Len(string s, int i) => char.IsSurrogatePair(s, i) ? 2 : 1;

        // 'ß' counts as lower-case here unless a capital follows it, so "XBß" (PascalCase for x | bß) ends the acronym
        // while "STRAßE" stays one word.
        private static bool LowerFollows(string s, int i)
        {
            i = NextBase(s, i);
            if (i >= s.Length) return false;
            if (IsCasedLower(s, i, Len(s, i))) return true;
            return s[i] == '\u00DF' && !UpperFollows(s, i + 1);
        }

        private static bool UpperFollows(string s, int i)
        {
            i = NextBase(s, i);
            return i < s.Length && IsCasedUpper(s, i, Len(s, i));
        }

        private static void Flush(StringBuilder current, List<string> words)
        {
            if (current.Length == 0)
                return;
            words.Add(current.ToString());
            current.Clear();
        }

        private static string Capitalize(string word)
        {
            if (word.Length == 0)
                return word;
            // The first letter may be a surrogate pair (Deseret, say), which char.ToUpperInvariant(word[0]) left alone.
            int first = char.IsSurrogatePair(word, 0) ? 2 : 1;
            return Title(word.Substring(0, first)) + Lower(word.Substring(first));
        }

        // A letter's title case: the digraphs have their own ("ǆemal" gave "Ǆemal", not "ǅemal"), and 'ß' is "Ss".
        private static string Title(string letter)
        {
            if (letter.Length == 1)
            {
                int digraph = DigraphUpper(letter[0]);
                if (digraph >= 0) return ((char)(digraph + 1)).ToString();
                if (letter[0] == '\u00DF') return "Ss";
            }
            return Upper(letter);
        }

        private static string Lower(string s) => SetDigraphs(s.ToLowerInvariant().Replace('\u0130', 'i'), 2);

        private static string Upper(string s) => SetDigraphs(s.ToUpperInvariant().Replace('\u0131', 'I').Replace("\u00DF", "SS"), 0);

        // The digraphs come in threes (upper, title, lower: Ǆ ǅ ǆ). .NET Framework's invariant casing leaves the title
        // one as it is, so they are mapped here: offset 0 is the upper, 2 the lower.
        private static string SetDigraphs(string s, int offset)
        {
            char[]? chars = null;
            for (int i = 0; i < s.Length; i++)
            {
                int upper = DigraphUpper(s[i]);
                if (upper >= 0 && s[i] != upper + offset)
                    (chars ??= s.ToCharArray())[i] = (char)(upper + offset);
            }
            return chars is null ? s : new string(chars);
        }

        private static int DigraphUpper(char c)
            => c >= '\u01C4' && c <= '\u01CC' ? '\u01C4' + (c - '\u01C4') / 3 * 3 : (c >= '\u01F1' && c <= '\u01F3' ? '\u01F1' : -1);

        // A titlecase letter ('ǅ') counts as upper: it starts a word.
        private static bool IsCasedUpper(string s, int i, int len)
            => (char.IsUpper(s, i) && Lower(s.Substring(i, len)) != s.Substring(i, len))
               || CharUnicodeInfo.GetUnicodeCategory(s, i) == UnicodeCategory.TitlecaseLetter;

        // 'ß' has no capital letter of its own ("SS" is two), so it continues its run: "STRAßE" is one word.
        private static bool IsCasedLower(string s, int i, int len)
            => char.IsLower(s, i) && s[i] != '\u00DF' && Upper(s.Substring(i, len)) != s.Substring(i, len);

        private static string JoinLower(IReadOnlyList<string> words, char separator)
            => Join(words, separator, upper: false);

        private static string JoinUpper(IReadOnlyList<string> words, char separator)
            => Join(words, separator, upper: true);

        private static string Join(IReadOnlyList<string> words, char separator, bool upper)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) sb.Append(separator);
                sb.Append(upper ? Upper(words[i]) : Lower(words[i]));
            }
            return sb.ToString();
        }
    }
}
