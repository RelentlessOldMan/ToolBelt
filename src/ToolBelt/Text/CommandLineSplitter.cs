// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// Splits a command-line-style string into argument tokens. Whitespace separates tokens; single or
    /// double quotes group text (whitespace and the other quote character are literal inside), and a
    /// doubled quote of the active kind inserts one literal quote. Quotes may abut unquoted text within a
    /// single token. An unterminated quote throws <see cref="FormatException"/>.
    /// </summary>
    /// <remarks>This is a portable, well-defined grammar — it is NOT an exact reproduction of any
    /// particular OS shell's quoting/escaping rules.</remarks>
    public static class CommandLineSplitter
    {
        public static string[] Split(string commandLine)
        {
            if (commandLine is null) throw new ArgumentNullException(nameof(commandLine));

            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inToken = false;
            char quote = '\0';

            for (int i = 0; i < commandLine.Length; i++)
            {
                char c = commandLine[i];

                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        if (i + 1 < commandLine.Length && commandLine[i + 1] == quote)
                        {
                            current.Append(quote); // doubled quote -> one literal quote
                            i++;
                        }
                        else
                        {
                            quote = '\0'; // closing quote
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == ' ' || c == '\t')
                {
                    if (inToken)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        inToken = false;
                    }
                }
                else if (c == '"' || c == '\'')
                {
                    quote = c;
                    inToken = true; // opening a quote begins a token, even an empty one ("")
                }
                else
                {
                    current.Append(c);
                    inToken = true;
                }
            }

            if (quote != '\0')
                throw new FormatException("Unterminated quote in command line.");
            if (inToken)
                tokens.Add(current.ToString());

            return tokens.ToArray();
        }
    }
}
