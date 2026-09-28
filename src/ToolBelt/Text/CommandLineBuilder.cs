// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// Quotes and joins arguments into a single command-line string that <see cref="CommandLineSplitter"/>
    /// parses back exactly. The round-trip <c>Split(Join(args)) == args</c> is the contract, verified by
    /// property tests over arguments containing spaces, quotes and empty strings — the cases hand-rolled
    /// quoting reliably gets wrong.
    /// </summary>
    public static class CommandLineBuilder
    {
        /// <summary>Quotes a single argument if it needs it (whitespace, quotes, or empty), doubling embedded quotes.</summary>
        public static string Quote(string argument)
        {
            if (argument is null) throw new ArgumentNullException(nameof(argument));
            if (argument.Length > 0 && !NeedsQuoting(argument))
                return argument;

            var sb = new StringBuilder(argument.Length + 2);
            sb.Append('"');
            foreach (char c in argument)
            {
                if (c == '"') sb.Append('"'); // double an embedded quote so the splitter reads it literally
                sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>Joins arguments into one command line, quoting each as needed.</summary>
        public static string Join(IEnumerable<string> arguments)
        {
            if (arguments is null) throw new ArgumentNullException(nameof(arguments));
            var sb = new StringBuilder();
            bool first = true;
            foreach (string arg in arguments)
            {
                if (!first) sb.Append(' ');
                sb.Append(Quote(arg));
                first = false;
            }
            return sb.ToString();
        }

        private static bool NeedsQuoting(string s)
        {
            foreach (char c in s)
                if (c == ' ' || c == '\t' || c == '"' || c == '\'')
                    return true;
            return false;
        }
    }
}
