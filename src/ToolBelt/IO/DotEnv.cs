// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.IO
{
    /// <summary>
    /// Parses <c>.env</c>-style configuration text into key/value pairs. Blank lines and <c>#</c> comment
    /// lines are ignored, an optional <c>export</c> prefix is stripped, and values may be single- or
    /// double-quoted. Double-quoted values honor <c>\n</c>, <c>\t</c>, <c>\"</c>, and <c>\\</c> escapes;
    /// single-quoted values are literal; a quoted value may be followed by a <c>#</c> comment. Unquoted values
    /// are trimmed, and a <c>#</c> starts a comment only at the start of the value or after whitespace (so
    /// <c>URL=http://x/#frag</c> keeps its fragment). A leading byte order mark is ignored, and <c>export</c> may be
    /// followed by any whitespace. Later keys override earlier ones.
    /// </summary>
    public static class DotEnv
    {
        public static IReadOnlyDictionary<string, string> Parse(string content)
        {
            if (content is null)
                throw new ArgumentNullException(nameof(content));

            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            if (content.Length > 0 && content[0] == '\uFEFF')
                content = content.Substring(1);

            foreach (string rawLine in content.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (line.Length > 6 && line.StartsWith("export", StringComparison.Ordinal) && char.IsWhiteSpace(line[6]))
                    line = line.Substring(6).TrimStart();

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue; // no key, or no '=' — skip

                string key = line.Substring(0, eq).Trim();
                if (key.Length == 0)
                    continue;

                string value = ParseValue(line.Substring(eq + 1).Trim());
                result[key] = value;
            }

            return result;
        }

        private static string ParseValue(string raw)
        {
            if (raw.Length >= 2 && (raw[0] == '"' || raw[0] == '\''))
            {
                char quote = raw[0];
                int close = 1;
                while (close < raw.Length && raw[close] != quote)
                    close += quote == '"' && raw[close] == '\\' ? 2 : 1;   // skip an escaped character
                if (close < raw.Length)
                {
                    string rest = raw.Substring(close + 1).Trim();
                    if (rest.Length == 0 || rest[0] == '#')
                    {
                        string inner = raw.Substring(1, close - 1);
                        return quote == '"' ? Unescape(inner) : inner;   // single quotes are literal
                    }
                }
                // No closing quote, or text after it: treat the whole value as unquoted.
            }

            // Unquoted: strip an inline comment (a '#' at the start or after whitespace), then trim.
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '#' && (i == 0 || char.IsWhiteSpace(raw[i - 1])))
                {
                    raw = raw.Substring(0, i);
                    break;
                }
            }
            return raw.Trim();
        }

        private static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char next = s[++i];
                    sb.Append(next switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '"' => '"',
                        '\\' => '\\',
                        _ => next,
                    });
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
