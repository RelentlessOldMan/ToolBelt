// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// Adjacent pairs of a sequence — (a,b), (b,c), (c,d) — the shape of "differences between consecutive samples",
    /// "time between events" and "edges of a polyline". Lazy and single-pass, so it works on streams. A sequence with
    /// fewer than two items yields nothing. (<c>Enumerable.Zip(source, source.Skip(1))</c> enumerates twice; this doesn't.)
    /// </summary>
    public static class PairwiseExtensions
    {
        public static IEnumerable<(T Previous, T Current)> Pairwise<T>(this IEnumerable<T> source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            return Iterate(source);

            static IEnumerable<(T, T)> Iterate(IEnumerable<T> s)
            {
                using var e = s.GetEnumerator();
                if (!e.MoveNext()) yield break;
                T previous = e.Current;
                while (e.MoveNext())
                {
                    T current = e.Current;
                    yield return (previous, current);
                    previous = current;
                }
            }
        }

        /// <summary>Applies <paramref name="selector"/> to each adjacent pair, e.g. <c>times.Pairwise((a, b) =&gt; b - a)</c>.</summary>
        public static IEnumerable<TResult> Pairwise<T, TResult>(this IEnumerable<T> source, Func<T, T, TResult> selector)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (selector is null) throw new ArgumentNullException(nameof(selector));
            return Iterate(source, selector);

            static IEnumerable<TResult> Iterate(IEnumerable<T> s, Func<T, T, TResult> f)
            {
                foreach (var (a, b) in s.Pairwise()) yield return f(a, b);
            }
        }
    }
}
