// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Functional
{
    /// <summary>
    /// Wraps a pure function so each distinct argument is computed at most once and cached. The returned
    /// delegates are NOT thread-safe unless created via the <c>ThreadSafe</c> overloads; use those when the
    /// memoized function may be invoked concurrently.
    /// </summary>
    public static class Memoize
    {
        /// <summary>Memoizes a one-argument function (single-threaded).</summary>
        public static Func<TArg, TResult> Function<TArg, TResult>(
            Func<TArg, TResult> function, IEqualityComparer<TArg>? comparer = null)
            where TArg : notnull
        {
            if (function is null) throw new ArgumentNullException(nameof(function));
            var cache = new Dictionary<TArg, TResult>(comparer);
            return arg =>
            {
                if (!cache.TryGetValue(arg, out var value))
                {
                    value = function(arg);
                    cache[arg] = value;
                }
                return value;
            };
        }

        /// <summary>Memoizes a parameterless factory, computing the value lazily on first call.</summary>
        public static Func<TResult> Factory<TResult>(Func<TResult> factory)
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            bool computed = false;
            TResult value = default!;
            return () =>
            {
                if (!computed)
                {
                    value = factory();
                    computed = true;
                }
                return value;
            };
        }

        /// <summary>
        /// Memoizes a one-argument function with a lock so concurrent callers share one cache safely. To
        /// avoid holding the lock across a slow computation, the function may run more than once for the
        /// same argument under contention, but all callers observe a single cached result thereafter.
        /// </summary>
        public static Func<TArg, TResult> ThreadSafe<TArg, TResult>(
            Func<TArg, TResult> function, IEqualityComparer<TArg>? comparer = null)
            where TArg : notnull
        {
            if (function is null) throw new ArgumentNullException(nameof(function));
            var cache = new Dictionary<TArg, TResult>(comparer);
            var gate = new object();
            return arg =>
            {
                lock (gate)
                {
                    if (cache.TryGetValue(arg, out var existing))
                        return existing;
                }

                var value = function(arg); // compute outside the lock; may be slow

                lock (gate)
                {
                    if (cache.TryGetValue(arg, out var raced))
                        return raced; // another thread won; keep its value for reference-stability
                    cache[arg] = value;
                    return value;
                }
            };
        }
    }
}
