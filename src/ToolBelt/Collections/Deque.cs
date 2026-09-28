// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A double-ended queue: amortized O(1) push and pop at both ends, O(1) indexing. Backed by a
    /// growable circular array that doubles when full. Indexing and enumeration run front-to-back
    /// (index 0 is the front). Not thread-safe.
    /// </summary>
    public sealed class Deque<T> : IEnumerable<T>
    {
        private T[] _array;
        private int _head;  // index of the front element
        private int _count;

        public Deque() : this(4) { }

        public Deque(int initialCapacity)
        {
            if (initialCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity), initialCapacity, "Capacity must not be negative.");
            _array = new T[Math.Max(4, initialCapacity)];
        }

        public int Count => _count;

        public void PushBack(T item)
        {
            EnsureCapacity();
            _array[(_head + _count) % _array.Length] = item;
            _count++;
        }

        public void PushFront(T item)
        {
            EnsureCapacity();
            _head = (_head - 1 + _array.Length) % _array.Length;
            _array[_head] = item;
            _count++;
        }

        public bool TryPopFront(out T item)
        {
            if (_count == 0)
            {
                item = default!;
                return false;
            }

            item = _array[_head];
            _array[_head] = default!;
            _head = (_head + 1) % _array.Length;
            _count--;
            return true;
        }

        public bool TryPopBack(out T item)
        {
            if (_count == 0)
            {
                item = default!;
                return false;
            }

            int tail = (_head + _count - 1) % _array.Length;
            item = _array[tail];
            _array[tail] = default!;
            _count--;
            return true;
        }

        public T PeekFront => _count > 0 ? _array[_head] : throw new InvalidOperationException("Deque is empty.");

        public T PeekBack => _count > 0
            ? _array[(_head + _count - 1) % _array.Length]
            : throw new InvalidOperationException("Deque is empty.");

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be in [0, {_count}).");
                return _array[(_head + index) % _array.Length];
            }
        }

        public void Clear()
        {
            Array.Clear(_array, 0, _array.Length);
            _head = 0;
            _count = 0;
        }

        public T[] ToArray()
        {
            var result = new T[_count];
            for (int i = 0; i < _count; i++)
                result[i] = _array[(_head + i) % _array.Length];
            return result;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
                yield return _array[(_head + i) % _array.Length];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void EnsureCapacity()
        {
            if (_count < _array.Length)
                return;

            var grown = new T[_array.Length * 2];
            for (int i = 0; i < _count; i++)
                grown[i] = _array[(_head + i) % _array.Length];
            _array = grown;
            _head = 0;
        }
    }
}
