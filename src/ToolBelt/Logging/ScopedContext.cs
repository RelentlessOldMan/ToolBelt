// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Threading;

namespace ToolBelt.Logging
{
    /// <summary>
    /// An ambient, flow-local scope label (a category or correlation id) pushed for a <c>using</c> block, so
    /// nested operations tag their log messages automatically without threading a logger parameter through
    /// every call. Nested pushes combine with a <c>/</c> separator. Backed by <see cref="AsyncLocal{T}"/>, so
    /// the value flows across <c>await</c> boundaries and stays isolated per asynchronous flow.
    /// <see cref="Logger"/> reads <see cref="Current"/> and folds it into each event's category.
    /// </summary>
    public static class ScopedContext
    {
        private static readonly AsyncLocal<string?> Ambient = new AsyncLocal<string?>();

        /// <summary>The current combined scope, or null if none is active.</summary>
        public static string? Current => Ambient.Value;

        /// <summary>Pushes a scope for the lifetime of the returned token; disposing restores the previous scope.</summary>
        public static IDisposable Push(string scope)
        {
            if (scope is null) throw new ArgumentNullException(nameof(scope));
            string? previous = Ambient.Value;
            Ambient.Value = string.IsNullOrEmpty(previous) ? scope : previous + "/" + scope;
            return new Popper(previous);
        }

        private sealed class Popper : IDisposable
        {
            private readonly string? _previous;
            private bool _disposed;
            public Popper(string? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Ambient.Value = _previous;
            }
        }
    }
}
