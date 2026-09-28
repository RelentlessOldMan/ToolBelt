// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A fixed-capacity ring buffer. Once full, each <see cref="Add"/> overwrites the oldest element.
    /// Indexing and enumeration run oldest-to-newest (index 0 is the oldest retained element). Backed by
    /// a single array with no per-item allocation. Not thread-safe.
    /// </summary>
    public sealed class CircularBuffer<T> : IEnumerable<T>
    {
        private readonly T[] _buffer;
        private int _start; // index of the oldest element
        private int _count;

        public CircularBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            _buffer = new T[capacity];
        }

        public int Capacity => _buffer.Length;

        public int Count => _count;

        public bool IsFull => _count == _buffer.Length;

        public bool IsEmpty => _count == 0;

        /// <summary>Appends an element, overwriting the oldest one if the buffer is already full.</summary>
        public void Add(T item)
        {
            if (_count == _buffer.Length)
            {
                _buffer[_start] = item;             // overwrite oldest...
                _start = (_start + 1) % _buffer.Length; // ...and advance the window
            }
            else
            {
                _buffer[(_start + _count) % _buffer.Length] = item;
                _count++;
            }
        }

        /// <summary>The oldest retained element (index 0 in enumeration order).</summary>
        public T Oldest => _count > 0 ? this[0] : throw new InvalidOperationException("Buffer is empty.");

        /// <summary>The most recently added element.</summary>
        public T Newest => _count > 0 ? this[_count - 1] : throw new InvalidOperationException("Buffer is empty.");

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be in [0, {_count}).");
                return _buffer[(_start + index) % _buffer.Length];
            }
        }

        /// <summary>Removes and returns the oldest element. Returns false if the buffer is empty.</summary>
        public bool TryRemoveOldest(out T item)
        {
            if (_count == 0)
            {
                item = default!;
                return false;
            }

            item = _buffer[_start];
            _buffer[_start] = default!; // release reference
            _start = (_start + 1) % _buffer.Length;
            _count--;
            return true;
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _start = 0;
            _count = 0;
        }

        public T[] ToArray()
        {
            var result = new T[_count];
            for (int i = 0; i < _count; i++)
                result[i] = _buffer[(_start + i) % _buffer.Length];
            return result;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
                yield return _buffer[(_start + i) % _buffer.Length];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
