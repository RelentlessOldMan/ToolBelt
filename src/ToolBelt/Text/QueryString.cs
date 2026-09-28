// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// Builds and parses URL query strings. Keys and values are percent-encoded on build (via
    /// <see cref="Uri.EscapeDataString"/>) and decoded on parse, so arbitrary text round-trips. Pairs
    /// are kept in order and duplicate keys are preserved. <see cref="Format"/> and <see cref="Parse"/>
    /// are inverses.
    /// </summary>
    public static class QueryString
    {
        /// <summary>Joins key/value pairs into a query string (no leading '?').</summary>
        public static string Format(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            if (pairs is null) throw new ArgumentNullException(nameof(pairs));

            var sb = new StringBuilder();
            foreach (var pair in pairs)
            {
                if (pair.Key is null) throw new ArgumentException("Keys must not be null.", nameof(pairs));
                if (sb.Length > 0)
                    sb.Append('&');
                sb.Append(Uri.EscapeDataString(pair.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(pair.Value ?? string.Empty));
            }
            return sb.ToString();
        }

        /// <summary>Parses a query string into ordered key/value pairs. A leading '?' is tolerated.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Parse(string query)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));

            var result = new List<KeyValuePair<string, string>>();
            if (query.Length > 0 && query[0] == '?')
                query = query.Substring(1);
            if (query.Length == 0)
                return result;

            foreach (string part in query.Split('&'))
            {
                if (part.Length == 0)
                    continue;
                int eq = part.IndexOf('=');
                string key, value;
                if (eq < 0)
                {
                    key = Uri.UnescapeDataString(part);
                    value = string.Empty;
                }
                else
                {
                    key = Uri.UnescapeDataString(part.Substring(0, eq));
                    value = Uri.UnescapeDataString(part.Substring(eq + 1));
                }
                result.Add(new KeyValuePair<string, string>(key, value));
            }
            return result;
        }
    }
}
