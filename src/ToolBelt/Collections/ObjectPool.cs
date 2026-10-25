// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A bounded, thread-safe pool of reusable objects. <see cref="Rent"/> returns a pooled instance or
    /// creates one via the factory; <see cref="Return"/> keeps it (resetting it first, if configured) when
    /// the pool is below capacity, and otherwise drops it for the GC without resetting it. Returning an
    /// instance that is already pooled is a no-op, so a double return cannot hand one instance to two
    /// renters. Useful for recycling expensive-to-allocate objects such as buffers or builders.
    /// </summary>
    public sealed class ObjectPool<T> where T : class
    {
        private readonly Func<T> _factory;
        private readonly Action<T>? _reset;
        private readonly int _maxRetained;
        private readonly Stack<T> _items = new Stack<T>();
        private readonly HashSet<T> _pooled = new HashSet<T>(ReferenceComparer.Instance); // same set as _items
        private readonly HashSet<T> _returning = new HashSet<T>(ReferenceComparer.Instance); // being reset by a Return
        private readonly object _gate = new object();

        /// <param name="factory">Creates a new instance when the pool is empty.</param>
        /// <param name="reset">Optional cleanup applied on return before an instance is retained.</param>
        /// <param name="maxRetained">Maximum instances to keep; extra returns are discarded. Default 32.</param>
        public ObjectPool(Func<T> factory, Action<T>? reset = null, int maxRetained = 32)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (maxRetained < 1) throw new ArgumentOutOfRangeException(nameof(maxRetained), maxRetained, "Capacity must be positive.");
            _reset = reset;
            _maxRetained = maxRetained;
        }

        /// <summary>Number of instances currently retained in the pool.</summary>
        public int Count
        {
            get { lock (_gate) return _items.Count; }
        }

        /// <summary>Returns a pooled instance, or a freshly created one if the pool is empty.</summary>
        public T Rent()
        {
            lock (_gate)
            {
                if (_items.Count > 0)
                {
                    var pooled = _items.Pop();
                    _pooled.Remove(pooled);
                    return pooled;
                }
            }
            // Create outside the lock; the factory may be slow.
            return _factory() ?? throw new InvalidOperationException("The pool's factory returned null.");
        }

        /// <summary>Resets (if configured) and retains the instance, unless the pool is already full.</summary>
        public void Return(T item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            lock (_gate)
            {
                // A Return of the same instance still resetting counts as pooled: otherwise both pushed it, one
                // after a Rent took the first, and two renters shared it.
                if (_items.Count >= _maxRetained || _pooled.Contains(item) || !_returning.Add(item))
                    return; // dropped (or already pooled): don't reset it
            }

            try
            {
                _reset?.Invoke(item); // reset outside the lock
            }
            catch
            {
                lock (_gate) _returning.Remove(item);
                throw;
            }

            lock (_gate)
            {
                _returning.Remove(item);
                if (_items.Count < _maxRetained && _pooled.Add(item))
                    _items.Push(item);
            }
        }

        /// <summary>Drops all retained instances.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _items.Clear();
                _pooled.Clear();
            }
        }

        // Identity, not T's Equals: two equal-but-distinct instances are separate pool entries.
        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
