// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Diagnostics
{
    /// <summary>
    /// Helpers for making exceptions actionable: walk to the root cause, flatten aggregate/inner chains into
    /// a list, format a readable report (with the data dictionary and a trimmed stack), and classify a
    /// failure as transient vs permanent. The classifier is a sensible default failure predicate for the
    /// retry and circuit-breaker helpers, which otherwise require the caller to supply one.
    /// </summary>
    public static class ExceptionUtils
    {
        /// <summary>The deepest inner exception (following <see cref="AggregateException"/> and InnerException).</summary>
        public static Exception RootCause(Exception exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));
            Exception current = exception;
            while (true)
            {
                if (current is AggregateException agg && agg.InnerExceptions.Count > 0) current = agg.InnerExceptions[0];
                else if (current.InnerException != null) current = current.InnerException;
                else return current;
            }
        }

        /// <summary>All exceptions in the tree, outermost first (aggregate members and inner chains expanded).</summary>
        public static IReadOnlyList<Exception> Flatten(Exception exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));
            var result = new List<Exception>();
            var queue = new Queue<Exception>();
            queue.Enqueue(exception);
            while (queue.Count > 0)
            {
                Exception e = queue.Dequeue();
                result.Add(e);
                if (e is AggregateException agg)
                    foreach (Exception inner in agg.InnerExceptions) queue.Enqueue(inner);
                else if (e.InnerException != null)
                    queue.Enqueue(e.InnerException);
            }
            return result;
        }

        /// <summary>A readable multi-line report: each exception's type, message, data entries, and (optionally) stack.</summary>
        public static string Describe(Exception exception, bool includeStackTrace = true)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));
            var sb = new StringBuilder();
            int depth = 0;
            foreach (Exception e in Flatten(exception))
            {
                string indent = new string(' ', depth * 2);
                sb.Append(indent).Append(e.GetType().FullName).Append(": ").AppendLine(e.Message);
                foreach (DictionaryEntry entry in e.Data)
                    sb.Append(indent).Append("  data[").Append(entry.Key).Append("] = ").AppendLine(Convert.ToString(entry.Value));
                if (includeStackTrace && !string.IsNullOrEmpty(e.StackTrace))
                    sb.Append(indent).Append("  at ").AppendLine((e.StackTrace ?? string.Empty).Trim());
                depth++;
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// True if the failure is likely transient (worth retrying): timeouts, I/O, socket and HTTP request
        /// failures. Programmer errors (argument, null-reference, invalid-operation) and cancellation are not.
        /// </summary>
        public static bool IsTransient(Exception exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));
            foreach (Exception e in Flatten(exception))
            {
                if (e is OperationCanceledException) continue; // cancellation is not a transient fault
                if (e is TimeoutException || e is System.IO.IOException) return true;
                string typeName = e.GetType().FullName ?? string.Empty;
                if (typeName == "System.Net.Sockets.SocketException"
                    || typeName == "System.Net.Http.HttpRequestException"
                    || typeName == "System.Net.WebException")
                    return true;
            }
            return false;
        }
    }
}
