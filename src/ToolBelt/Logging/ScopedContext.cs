// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace ToolBelt.Logging
{
    /// <summary>
    /// An ambient, flow-local scope label (a category or correlation id) pushed for a <c>using</c> block, so
    /// nested operations tag their log messages automatically without threading a logger parameter through
    /// every call. Nested pushes combine with a <c>/</c> separator. Backed by <see cref="AsyncLocal{T}"/>, so
    /// the value flows across <c>await</c> boundaries and stays isolated per asynchronous flow.
    /// The library's <c>Logger</c> reads <see cref="Current"/> and folds it into each event's category.
    /// </summary>
    public static class ScopedContext
    {
        private static readonly AsyncLocal<Node?> Ambient = new AsyncLocal<Node?>();

        /// <summary>The current combined scope, or null if none is active.</summary>
        public static string? Current => Ambient.Value?.Text;

        /// <summary>
        /// Pushes a scope for the lifetime of the returned token; disposing restores the previous scope. An empty
        /// scope leaves the current one unchanged. Tokens disposed out of order never bring back a disposed scope: an
        /// outer token disposed first takes effect when the scopes inside it end. A push costs the same at any depth;
        /// the combined text is built when <see cref="Current"/> is first read.
        /// </summary>
        public static IDisposable Push(string scope)
        {
            if (scope is null) throw new ArgumentNullException(nameof(scope));
            if (scope.Length == 0) return new Popper(null);
            var node = new Node(scope, Ambient.Value);
            Ambient.Value = node;
            return new Popper(node);
        }

        // One pushed scope. Disposed is shared by every flow that inherited the node. Each push stored the whole combined
        // text, so 20,000 nested pushes held 1.9 GB; the text is now built on first read, from the nearest ancestor that
        // has one, without recursion.
        private sealed class Node
        {
            private readonly string _scope;
            private string? _text;
            public readonly Node? Parent;
            public volatile bool Disposed;

            public Node(string scope, Node? parent) { _scope = scope; Parent = parent; }

            public string Text
            {
                get
                {
                    string? text = _text;
                    if (text != null) return text;
                    var chain = new List<Node>();
                    Node? n = this;
                    string? prefix = null;
                    for (; n != null; n = n.Parent)
                    {
                        prefix = n._text;
                        if (prefix != null) break;
                        chain.Add(n);
                    }
                    var sb = new StringBuilder(prefix);
                    for (int i = chain.Count - 1; i >= 0; i--)
                    {
                        if (sb.Length > 0) sb.Append('/');
                        sb.Append(chain[i]._scope);
                    }
                    return _text = sb.ToString();
                }
            }
        }

        private sealed class Popper : IDisposable
        {
            private readonly Node? _node;
            private int _disposed;
            public Popper(Node? node) => _node = node;

            public void Dispose()
            {
                if (_node is null || Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _node.Disposed = true;
                // Restoring the saved previous value unconditionally brought back a scope already disposed (push A,
                // push B, dispose A, dispose B left A current). Only the innermost live scope pops, to its nearest
                // live ancestor.
                Node? current = Ambient.Value;
                if (current != _node) return;
                Node? up = current.Parent;
                while (up != null && up.Disposed) up = up.Parent;
                Ambient.Value = up;
            }
        }
    }
}
