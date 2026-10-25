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
    /// then rejoined in the requested style. Conversions are idempotent per style and stable across styles, except
    /// that adjacent one-letter words become a run of capitals in camelCase or PascalCase ("x_y_z" → "xYZ"), which
    /// reads back as one acronym ("xYZ" → "xYz", "is_a_b" → "IsAB" → "is_ab").
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

                bool upper = char.IsUpper(value, i);
                if (current.Length > 0)
                {
                    // lower/digit -> upper : a camelCase hump ("fooBar" => foo | Bar)
                    bool humpBoundary = upper && (prevLower || prevDigit);

                    // UPPER UPPER lower : end of an acronym ("HTTPServer" => HTTP | Server)
                    bool acronymBoundary = upper && prevUpper
                        && i + len < value.Length && char.IsLower(value, i + len);

                    if (humpBoundary || acronymBoundary)
                        Flush(current, words);
                }

                current.Append(value, i, len);
                prevUpper = upper;
                prevLower = char.IsLower(value, i);
                prevDigit = char.IsDigit(value, i);
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
                sb.Append(i == 0 ? words[i].ToLowerInvariant() : Capitalize(words[i]));
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
            return word.Substring(0, first).ToUpperInvariant() + word.Substring(first).ToLowerInvariant();
        }

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
                sb.Append(upper ? words[i].ToUpperInvariant() : words[i].ToLowerInvariant());
            }
            return sb.ToString();
        }
    }
}
