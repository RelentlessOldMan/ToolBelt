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
        private int _version; // bumped by every mutation so an enumerator can detect one

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
                _start = Slot(1); // ...and advance the window
            }
            else
            {
                _buffer[Slot(_count)] = item;
                _count++;
            }
            _version++;
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
                return _buffer[Slot(index)];
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
            _start = Slot(1);
            _count--;
            _version++;
            return true;
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _start = 0;
            _count = 0;
            _version++;
        }

        public T[] ToArray()
        {
            var result = new T[_count];
            for (int i = 0; i < _count; i++)
                result[i] = _buffer[Slot(i)];
            return result;
        }

        /// <summary>Enumerates oldest to newest. Modifying the buffer during enumeration throws <see cref="InvalidOperationException"/>.</summary>
        public IEnumerator<T> GetEnumerator() => Enumerate(_version);   // version taken now, not at the first MoveNext

        private IEnumerator<T> Enumerate(int version)
        {
            for (int i = 0; i < _count; i++)
            {
                yield return _buffer[Slot(i)];
                if (version != _version)
                    throw new InvalidOperationException("The buffer was modified during enumeration.");
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // Physical index of logical position i (0 <= i <= capacity). (_start + i) % length overflowed int past a
        // capacity of 2^30, but _start + i - length lies in (-length, length].
        private int Slot(int i)
        {
            int j = _start + i - _buffer.Length;
            return j >= 0 ? j : j + _buffer.Length;
        }
    }
}
