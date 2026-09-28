// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A bounded, thread-safe pool of reusable objects. <see cref="Rent"/> returns a pooled instance or
    /// creates one via the factory; <see cref="Return"/> optionally resets it and keeps it if the pool is
    /// below capacity (otherwise the instance is simply dropped for the GC). Useful for recycling
    /// expensive-to-allocate objects such as buffers or builders.
    /// </summary>
    public sealed class ObjectPool<T> where T : class
    {
        private readonly Func<T> _factory;
        private readonly Action<T>? _reset;
        private readonly int _maxRetained;
        private readonly Stack<T> _items = new Stack<T>();
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
                    return _items.Pop();
            }
            return _factory(); // create outside the lock; factory may be slow
        }

        /// <summary>Resets (if configured) and retains the instance, unless the pool is already full.</summary>
        public void Return(T item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            _reset?.Invoke(item); // reset outside the lock

            lock (_gate)
            {
                if (_items.Count < _maxRetained)
                    _items.Push(item);
            }
        }

        /// <summary>Drops all retained instances.</summary>
        public void Clear()
        {
            lock (_gate) _items.Clear();
        }
    }
}
