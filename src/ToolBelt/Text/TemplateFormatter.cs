// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// Substitutes <c>{name}</c> placeholders in a template from a value lookup. Literal braces are written
    /// by doubling: <c>{{</c> and <c>}}</c>. Placeholder names are trimmed of surrounding whitespace. An
    /// unknown placeholder throws unless a default-value provider is supplied; a malformed template (an
    /// unmatched or empty brace) always throws <see cref="FormatException"/>.
    /// </summary>
    public static class TemplateFormatter
    {
        public static string Format(string template, IReadOnlyDictionary<string, string> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            return Format(template, name =>
                values.TryGetValue(name, out var v)
                    ? v
                    : throw new KeyNotFoundException($"No value supplied for placeholder '{name}'."));
        }

        public static string Format(string template, Func<string, string> resolve)
        {
            if (template is null) throw new ArgumentNullException(nameof(template));
            if (resolve is null) throw new ArgumentNullException(nameof(resolve));

            var sb = new StringBuilder(template.Length);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];

                if (c == '{')
                {
                    if (i + 1 < template.Length && template[i + 1] == '{') // escaped "{{"
                    {
                        sb.Append('{');
                        i += 2;
                        continue;
                    }

                    int end = template.IndexOf('}', i + 1);
                    if (end < 0)
                        throw new FormatException("Unclosed '{' in template.");
                    string name = template.Substring(i + 1, end - i - 1).Trim();
                    if (name.Length == 0)
                        throw new FormatException("Empty placeholder '{}' in template.");
                    sb.Append(resolve(name));
                    i = end + 1;
                }
                else if (c == '}')
                {
                    if (i + 1 < template.Length && template[i + 1] == '}') // escaped "}}"
                    {
                        sb.Append('}');
                        i += 2;
                        continue;
                    }
                    throw new FormatException("Unescaped '}' in template (use '}}' for a literal brace).");
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }
    }
}
