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
    /// digit ("x_y_z" → "xYZ" → "xYz", "is_a_b" → "IsAB" → "is_ab", "md5_x" → "md5X" → "md5x"), or ends in a letter
    /// with no case ('ĸ', '中': "中_x" → "中X" → "中x"). Casing is invariant, with additions so every style's output is
    /// properly cased: 'İ' lowers to 'i', 'ı' uppers to 'I', and a lower-case letter with no one-letter capital takes
    /// Unicode's full upper and title case ('ß' → "SS" and "Ss", 'ﬁ' → "FI" and "Fi", 'ŉ' → "ʼN"), so the Turkish dotted
    /// and dotless i, 'ß' and the ligatures don't survive a trip through an upper-case style. Nor do the lower-case letters
    /// whose capital lowers to another letter: final 'ς' and long 'ſ' come back as 'σ' and 's' ("λόγος" → "ΛΌΓΟΣ" →
    /// "λόγοσ"), as do the Greek symbol forms ('ϐ' → 'β'), 'ẛ', U+1FBE and the combining ypogegrammeni U+0345, whose
    /// capital is 'Ι' (it comes back as 'ι'). Such a letter is lower-case
    /// at the start of a word or after the word's one capital ("Bß" in "BßX" is a word), and otherwise continues the run
    /// it is in ("STRAßE" is one word); for the rule above it is not a lower-case letter. A letter with no case of its
    /// own continues the word it is in; a digraph such as 'ǆ' is titlecased ('ǅ').
    /// Which characters are letters, and their case, come from the runtime's Unicode tables: .NET Framework's are Unicode 8,
    /// so a letter added since (Syriac Malayalam, Greek 'Ϳ') is a separator there and a word character on .NET 8. Its
    /// invariant casing also lacks older mappings ('ς', 'µ', 'ſ', the Kelvin sign, Cherokee); those are filled in from
    /// .NET 8's, where the target is a letter there.
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
            int capitals = 0; // the capitals in the current word so far, while it has nothing else
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

                // 'ß', 'ﬁ': lower-case with no one-letter capital, so an upper-case style spells them out ("SS", "FI").
                bool noCapital = len == 1 && NoCapital.IndexOf(value[i]) >= 0;
                if (upper || lower || digit || current.Length == 0)
                {
                    prevUpper = upper;
                    // A word starting with one is lower-case: camelCase "ßBa" read back as one word, so "ß-Ba" → "ßBa" →
                    // "ßba". Only these: 'ĸ' has no capital at all, so CONSTANT_CASE "ĸA" (for "ĸa") split into ĸ | A.
                    prevLower = lower || (current.Length == 0 && noCapital);
                    prevDigit = digit;
                }
                else if (noCapital && prevUpper && capitals == 1)
                {
                    // After the word's one capital it is lower-case too: PascalCase "BßX" (bß | x) read back as one word.
                    prevUpper = false;
                    prevLower = true;
                }
                capitals = upper && (current.Length == 0 || capitals > 0) ? capitals + 1 : 0;
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

        // 'ß' (and the other letters with no one-letter capital) counts as lower-case here unless a capital follows it, so
        // "XBß" (PascalCase for x | bß) ends the acronym while "STRAßE" stays one word.
        private static bool LowerFollows(string s, int i)
        {
            i = NextBase(s, i);
            if (i >= s.Length) return false;
            if (IsCasedLower(s, i, Len(s, i))) return true;
            return NoCapital.IndexOf(s[i]) >= 0 && !UpperFollows(s, i + 1);
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
            // 'ŉ' titles to "ʼN", whose capital comes after the apostrophe: "ʼNa" in Title Case read back as "ʼna".
            int lead = 0;
            while (lead < word.Length - 1 && word[lead] == '\u02BC') lead++;
            // The first letter may be a surrogate pair (Deseret, say), which char.ToUpperInvariant(word[0]) left alone.
            int first = char.IsSurrogatePair(word, lead) ? 2 : 1;
            return word.Substring(0, lead) + Title(word.Substring(lead, first)) + Lower(word.Substring(lead + first));
        }

        // A letter's title case: the digraphs have their own ("ǆemal" gave "Ǆemal", not "ǅemal"), and 'ß' is "Ss".
        private static string Title(string letter)
        {
            if (letter.Length == 1)
            {
                int digraph = DigraphUpper(letter[0]);
                if (digraph >= 0) return ((char)(digraph + 1)).ToString();
                int k = NoCapital.IndexOf(letter[0]);
                if (k >= 0) return NoCapitalTitle[k];
            }
            return Upper(letter);
        }

#if !NET5_0_OR_GREATER
        // .NET Framework's invariant casing lacks mappings far older than its Unicode 8 letter tables: 'ς' and 'µ' stayed
        // lower-case in CONSTANT_CASE ("λόγος" gave "ΛΌΓΟς"), the Kelvin sign upper-case in snake_case, and the combining
        // ypogegrammeni U+0345 stayed a mark where .NET 8 gives 'Ι' ("a\u0345b" in CONSTANT_CASE). These are .NET 8's
        // simple mappings that .NET Framework leaves alone, Cherokee by range; one applies only where the runtime left the
        // character unchanged and knows the target as a letter (Georgian Mtavruli, Unicode 11, is not one there).
        private const string UpperFrom =
            "\u00B5\u017F\u01C5\u01C8\u01CB\u01F2\u023F\u0240\u0252\u025C\u0261\u0265\u0266\u026A\u026C\u0282"
            + "\u0287\u029D\u029E\u0345\u03C2\u03D0\u03D1\u03D5\u03D6\u03F0\u03F1\u03F3\u03F5\u0525\u0527\u0529\u052B"
            + "\u052D\u052F\u1D8E\u1E9B\u1FBE\u2CEC\u2CEE\u2CF3\u2D27\u2D2D\uA661\uA699\uA69B\uA791\uA793\uA794"
            + "\uA797\uA799\uA79B\uA79D\uA79F\uA7A1\uA7A3\uA7A5\uA7A7\uA7A9\uA7B5\uA7B7\uAB53";
        private const string UpperTo =
            "\u039C\u0053\u01C4\u01C7\u01CA\u01F1\u2C7E\u2C7F\u2C70\uA7AB\uA7AC\uA78D\uA7AA\uA7AE\uA7AD\uA7C5"
            + "\uA7B1\uA7B2\uA7B0\u0399\u03A3\u0392\u0398\u03A6\u03A0\u039A\u03A1\u037F\u0395\u0524\u0526\u0528\u052A"
            + "\u052C\u052E\uA7C6\u1E60\u0399\u2CEB\u2CED\u2CF2\u10C7\u10CD\uA660\uA698\uA69A\uA790\uA792\uA7C4"
            + "\uA796\uA798\uA79A\uA79C\uA79E\uA7A0\uA7A2\uA7A4\uA7A6\uA7A8\uA7B4\uA7B6\uA7B3";
        private const string LowerFrom =
            "\u01C5\u01C8\u01CB\u01F2\u037F\u03F4\u0524\u0526\u0528\u052A\u052C\u052E\u10C7\u10CD\u1E9E\u2126"
            + "\u212A\u212B\u2C70\u2C7E\u2C7F\u2CEB\u2CED\u2CF2\uA660\uA698\uA69A\uA78D\uA790\uA792\uA796\uA798"
            + "\uA79A\uA79C\uA79E\uA7A0\uA7A2\uA7A4\uA7A6\uA7A8\uA7AA\uA7AB\uA7AC\uA7AD\uA7B0\uA7B1\uA7B2\uA7B3"
            + "\uA7B4\uA7B6";
        private const string LowerTo =
            "\u01C6\u01C9\u01CC\u01F3\u03F3\u03B8\u0525\u0527\u0529\u052B\u052D\u052F\u2D27\u2D2D\u00DF\u03C9"
            + "\u006B\u00E5\u0252\u023F\u0240\u2CEC\u2CEE\u2CF3\uA661\uA699\uA69B\u0265\uA791\uA793\uA797\uA799"
            + "\uA79B\uA79D\uA79F\uA7A1\uA7A3\uA7A5\uA7A7\uA7A9\u0266\u025C\u0261\u026C\u029E\u0287\u029D\uAB53"
            + "\uA7B5\uA7B7";

        private static char MissingCase(char c, bool upper)
        {
            if (upper)
            {
                if (c >= '\uAB70' && c <= '\uABBF') return (char)(c - 0xAB70 + 0x13A0);
                if (c >= '\u13F8' && c <= '\u13FD') return (char)(c - 8);
                int k = UpperFrom.IndexOf(c);
                return k >= 0 ? UpperTo[k] : c;
            }
            if (c >= '\u13A0' && c <= '\u13EF') return (char)(c - 0x13A0 + 0xAB70);
            if (c >= '\u13F0' && c <= '\u13F5') return (char)(c + 8);
            int j = LowerFrom.IndexOf(c);
            return j >= 0 ? LowerTo[j] : c;
        }
#endif

        // The runtime's invariant casing, with the mappings .NET Framework lacks filled in.
        private static string FillCaseGaps(string s, bool upper)
        {
#if !NET5_0_OR_GREATER
            char[]? chars = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i], t = MissingCase(c, upper);
                if (t == c || !char.IsLetter(t)) continue;
                if ((upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c)) != c) continue;
                chars ??= s.ToCharArray();
                chars[i] = t;
            }
            if (chars != null) s = new string(chars);
#endif
            return s;
        }

        private static string Lower(string s) => SetDigraphs(FillCaseGaps(s.ToLowerInvariant().Replace('\u0130', 'i'), false), 2);

        private static string Upper(string s)
        {
            s = FillCaseGaps(s.ToUpperInvariant().Replace('\u0131', 'I'), true);
            if (s.IndexOfAny(NoCapitalChars) >= 0)
            {
                var sb = new StringBuilder(s.Length + 8);
                foreach (char c in s)
                {
                    int k = NoCapital.IndexOf(c);
                    if (k >= 0) sb.Append(NoCapitalUpper[k]); else sb.Append(c);
                }
                s = sb.ToString();
            }
            return SetDigraphs(s, 0);
        }

        // Lower-case letters with no one-letter capital, from Unicode's SpecialCasing (invariant casing leaves them as they
        // are), with their full upper and title forms: "ﬁle" in CONSTANT_CASE stayed "ﬁLE", read back as two words.
        private const string NoCapital =
            "\u00DF\u0149\u01F0\u0390\u03B0\u0587\u1E96\u1E97\u1E98\u1E99\u1E9A\u1F50\u1F52\u1F54\u1F56\u1FB2"
            + "\u1FB4\u1FB6\u1FB7\u1FC2\u1FC4\u1FC6\u1FC7\u1FD2\u1FD3\u1FD6\u1FD7\u1FE2\u1FE3\u1FE4\u1FE6\u1FE7"
            + "\u1FF2\u1FF4\u1FF6\u1FF7\uFB00\uFB01\uFB02\uFB03\uFB04\uFB05\uFB06\uFB13\uFB14\uFB15\uFB16\uFB17";

        private static readonly string[] NoCapitalUpper =
        {
            "\u0053\u0053", "\u02BC\u004E", "\u004A\u030C", "\u0399\u0308\u0301", "\u03A5\u0308\u0301", "\u0535\u0552",
            "\u0048\u0331", "\u0054\u0308", "\u0057\u030A", "\u0059\u030A", "\u0041\u02BE", "\u03A5\u0313",
            "\u03A5\u0313\u0300", "\u03A5\u0313\u0301", "\u03A5\u0313\u0342", "\u1FBA\u0399", "\u0386\u0399",
            "\u0391\u0342", "\u0391\u0342\u0399", "\u1FCA\u0399", "\u0389\u0399", "\u0397\u0342", "\u0397\u0342\u0399",
            "\u0399\u0308\u0300", "\u0399\u0308\u0301", "\u0399\u0342", "\u0399\u0308\u0342", "\u03A5\u0308\u0300",
            "\u03A5\u0308\u0301", "\u03A1\u0313", "\u03A5\u0342", "\u03A5\u0308\u0342", "\u1FFA\u0399", "\u038F\u0399",
            "\u03A9\u0342", "\u03A9\u0342\u0399", "\u0046\u0046", "\u0046\u0049", "\u0046\u004C", "\u0046\u0046\u0049",
            "\u0046\u0046\u004C", "\u0053\u0054", "\u0053\u0054", "\u0544\u0546", "\u0544\u0535", "\u0544\u053B",
            "\u054E\u0546", "\u0544\u053D"
        };

        private static readonly string[] NoCapitalTitle =
        {
            "\u0053\u0073", "\u02BC\u004E", "\u004A\u030C", "\u0399\u0308\u0301", "\u03A5\u0308\u0301", "\u0535\u0582",
            "\u0048\u0331", "\u0054\u0308", "\u0057\u030A", "\u0059\u030A", "\u0041\u02BE", "\u03A5\u0313",
            "\u03A5\u0313\u0300", "\u03A5\u0313\u0301", "\u03A5\u0313\u0342", "\u1FBA\u0345", "\u0386\u0345",
            "\u0391\u0342", "\u0391\u0342\u0345", "\u1FCA\u0345", "\u0389\u0345", "\u0397\u0342", "\u0397\u0342\u0345",
            "\u0399\u0308\u0300", "\u0399\u0308\u0301", "\u0399\u0342", "\u0399\u0308\u0342", "\u03A5\u0308\u0300",
            "\u03A5\u0308\u0301", "\u03A1\u0313", "\u03A5\u0342", "\u03A5\u0308\u0342", "\u1FFA\u0345", "\u038F\u0345",
            "\u03A9\u0342", "\u03A9\u0342\u0345", "\u0046\u0066", "\u0046\u0069", "\u0046\u006C", "\u0046\u0066\u0069",
            "\u0046\u0066\u006C", "\u0053\u0074", "\u0053\u0074", "\u0544\u0576", "\u0544\u0565", "\u0544\u056B",
            "\u054E\u0576", "\u0544\u056D"
        };

        private static readonly char[] NoCapitalChars = NoCapital.ToCharArray();

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
            => char.IsLower(s, i) && NoCapital.IndexOf(s[i]) < 0 && Upper(s.Substring(i, len)) != s.Substring(i, len);

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
