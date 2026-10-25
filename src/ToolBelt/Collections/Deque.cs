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
        private int _version; // bumped by every mutation so an enumerator can detect one

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
            _array[Slot(_count)] = item;
            _count++;
            _version++;
        }

        public void PushFront(T item)
        {
            EnsureCapacity();
            _head = (_head - 1 + _array.Length) % _array.Length;
            _array[_head] = item;
            _count++;
            _version++;
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
            _version++;
            return true;
        }

        public bool TryPopBack(out T item)
        {
            if (_count == 0)
            {
                item = default!;
                return false;
            }

            int tail = Slot(_count - 1);
            item = _array[tail];
            _array[tail] = default!;
            _count--;
            _version++;
            return true;
        }

        public T PeekFront => _count > 0 ? _array[_head] : throw new InvalidOperationException("Deque is empty.");

        public T PeekBack => _count > 0
            ? _array[Slot(_count - 1)]
            : throw new InvalidOperationException("Deque is empty.");

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be in [0, {_count}).");
                return _array[Slot(index)];
            }
        }

        public void Clear()
        {
            Array.Clear(_array, 0, _array.Length);
            _head = 0;
            _count = 0;
            _version++;
        }

        public T[] ToArray()
        {
            var result = new T[_count];
            for (int i = 0; i < _count; i++)
                result[i] = _array[Slot(i)];
            return result;
        }

        /// <summary>Enumerates front to back. Modifying the deque during enumeration throws <see cref="InvalidOperationException"/>.</summary>
        public IEnumerator<T> GetEnumerator() => Enumerate(_version);   // version taken now, not at the first MoveNext

        private IEnumerator<T> Enumerate(int version)
        {
            for (int i = 0; i < _count; i++)
            {
                yield return _array[Slot(i)];
                if (version != _version)
                    throw new InvalidOperationException("The deque was modified during enumeration.");
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void EnsureCapacity()
        {
            if (_count < _array.Length)
                return;

            // Doubling overflowed int past 2^30 elements; cap at the largest array length, as List<T> does.
            const int MaxLength = 0x7FFFFFC7;
            if (_array.Length >= MaxLength) throw new InvalidOperationException("The deque is at the maximum array length.");
            var grown = new T[(int)Math.Min((long)_array.Length * 2, MaxLength)];
            for (int i = 0; i < _count; i++)
                grown[i] = _array[Slot(i)];
            _array = grown;
            _head = 0;
        }

        // Physical index of logical position i (0 <= i < length). _head + i could overflow int for a huge
        // array, but _head + i - length lies in (-length, length).
        private int Slot(int i)
        {
            int j = _head + i - _array.Length;
            return j >= 0 ? j : j + _array.Length;
        }
    }
}
