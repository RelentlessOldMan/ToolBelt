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
    /// single-quoted values are literal. Unquoted values are trimmed and may carry a trailing
    /// <c>#</c> comment. Later keys override earlier ones.
    /// </summary>
    public static class DotEnv
    {
        public static IReadOnlyDictionary<string, string> Parse(string content)
        {
            if (content is null)
                throw new ArgumentNullException(nameof(content));

            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string rawLine in content.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (line.StartsWith("export ", StringComparison.Ordinal))
                    line = line.Substring("export ".Length).TrimStart();

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
            if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
                return Unescape(raw.Substring(1, raw.Length - 2));
            if (raw.Length >= 2 && raw[0] == '\'' && raw[raw.Length - 1] == '\'')
                return raw.Substring(1, raw.Length - 2); // literal

            // Unquoted: strip an inline comment, then trim.
            int hash = raw.IndexOf('#');
            if (hash >= 0)
                raw = raw.Substring(0, hash);
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
