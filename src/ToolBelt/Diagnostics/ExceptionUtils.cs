// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        /// <summary>
        /// The deepest inner exception (following the first member of an <see cref="AggregateException"/>, else
        /// InnerException). A cyclic chain (only possible through reflection) stops where it closes.
        /// </summary>
        public static Exception RootCause(Exception exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));
            var seen = new HashSet<Exception>(ReferenceComparer.Instance);
            Exception current = exception;
            while (seen.Add(current))
            {
                Exception? next = current is AggregateException agg && agg.InnerExceptions.Count > 0
                    ? agg.InnerExceptions[0]
                    : current.InnerException;
                if (next == null) return current;
                current = next;
            }
            return current;
        }

        /// <summary>
        /// All exceptions in the tree, level by level, outermost first (aggregate members and inner chains expanded).
        /// Each instance appears once, so a shared or cyclic inner exception does not repeat.
        /// </summary>
        public static IReadOnlyList<Exception> Flatten(Exception exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));
            var result = new List<Exception>();
            var seen = new HashSet<Exception>(ReferenceComparer.Instance);
            var queue = new Queue<Exception>();
            queue.Enqueue(exception);
            while (queue.Count > 0)
            {
                Exception e = queue.Dequeue();
                if (!seen.Add(e)) continue;
                result.Add(e);
                foreach (Exception child in Children(e)) queue.Enqueue(child);
            }
            return result;
        }

        private static string Safe(Func<string?> read)
        {
            try { return read() ?? string.Empty; }
            catch (Exception ex) { return "<" + ex.GetType().Name + " reading the text>"; }
        }

        private static IEnumerable<Exception> Children(Exception e)
        {
            if (e is AggregateException agg)
                return agg.InnerExceptions;
            return e.InnerException != null ? new[] { e.InnerException } : Array.Empty<Exception>();
        }

        /// <summary>
        /// A readable multi-line report: each exception's type, message, data entries, and (optionally) stack, as a
        /// tree — each exception indented two spaces under the one that wraps it (to a depth of 32). A message or data
        /// entry whose text cannot be read is shown as a placeholder.
        /// </summary>
        public static string Describe(Exception exception, bool includeStackTrace = true)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));
            var sb = new StringBuilder();
            var seen = new HashSet<Exception>(ReferenceComparer.Instance);
            var stack = new Stack<(Exception Exception, int Depth)>();
            stack.Push((exception, 0));
            while (stack.Count > 0)
            {
                var (e, depth) = stack.Pop();
                if (!seen.Add(e)) continue;
                var children = new List<Exception>(Children(e));
                for (int i = children.Count - 1; i >= 0; i--) stack.Push((children[i], depth + 1));

                // Capped: an indent per level made a chain thousands deep quadratic in size.
                string indent = new string(' ', Math.Min(depth, 32) * 2);
                Exception current = e;
                sb.Append(indent).Append(e.GetType().FullName).Append(": ").AppendLine(Safe(() => current.Message));
                try
                {
                    // A Data value whose ToString threw took the whole report down.
                    foreach (DictionaryEntry entry in e.Data)
                        sb.Append(indent).Append("  data[").Append(Safe(() => Convert.ToString(entry.Key))).Append("] = ")
                          .AppendLine(Safe(() => Convert.ToString(entry.Value)));
                }
                catch (Exception ex) { sb.Append(indent).AppendLine("  data: <" + ex.GetType().Name + ">"); }
                string? trace = includeStackTrace ? Safe(() => current.StackTrace) : null;
                if (!string.IsNullOrEmpty(trace))
                {
                    // The trace's frames already start with "at"; each one was written as "at at" or not indented.
                    foreach (string frame in trace!.Split('\n'))
                    {
                        string line = frame.Trim();
                        if (line.Length > 0) sb.Append(indent).Append("  ").AppendLine(line);
                    }
                }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// True if the failure is likely transient (worth retrying): timeouts, I/O, socket and HTTP request
        /// failures. Programmer errors (argument, null-reference, invalid-operation), cancellation, and I/O failures
        /// that a retry cannot fix (file, directory or drive not found, path too long, end of stream, a failed
        /// assembly load) are not.
        /// </summary>
        public static bool IsTransient(Exception exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));
            // Every member is looked at: returning on the first one made (FileNotFound, Timeout) and (Timeout,
            // FileNotFound) answer differently. A failure a retry cannot fix anywhere in it makes it not transient.
            bool transient = false;
            foreach (Exception e in Flatten(exception))
            {
                if (e is OperationCanceledException) continue; // cancellation is not a transient fault
                if (e is TimeoutException) transient = true;
                else if (e is System.IO.IOException)
                {
                    if (IsPermanentIo(e)) return false;
                    transient = true;
                }
                else
                {
                    string typeName = e.GetType().FullName ?? string.Empty;
                    if (typeName == "System.Net.Sockets.SocketException"
                        || typeName == "System.Net.Http.HttpRequestException"
                        || typeName == "System.Net.WebException")
                        transient = true;
                }
            }
            return transient;
        }

        private static bool IsPermanentIo(Exception e)
            => e is System.IO.FileNotFoundException
               || e is System.IO.DirectoryNotFoundException
               || e is System.IO.DriveNotFoundException
               || e is System.IO.PathTooLongException
               || e is System.IO.EndOfStreamException
               || e is System.IO.FileLoadException;

        private sealed class ReferenceComparer : IEqualityComparer<Exception>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(Exception? x, Exception? y) => ReferenceEquals(x, y);
            public int GetHashCode(Exception obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
