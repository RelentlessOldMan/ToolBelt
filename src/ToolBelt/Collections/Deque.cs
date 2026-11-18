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
        private int _version; // bumped by every mutation so an enumerator can detect one (wraps: a checked build threw at 2^31)

        public Deque() : this(4) { }

        public Deque(int initialCapacity)
        {
            // Past Array.MaxLength the allocation threw OutOfMemoryException.
            if (initialCapacity < 0 || initialCapacity > 0x7FFFFFC7)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity), initialCapacity, "Capacity must be in [0, Array.MaxLength] (0x7FFFFFC7).");
            try { _array = new T[Math.Max(4, initialCapacity)]; }
            catch (OutOfMemoryException) when (PastFrameworkLimit(initialCapacity))
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity), initialCapacity, "Capacity is past .NET Framework's array limit (0x7FEFFFFF for this type).");
            }
        }

        // .NET Framework caps an array of elements wider than a byte at 0x7FEFFFFF (Array.MaxLength, 0x7FFFFFC7, is .NET's),
        // and whether T is one byte is not known here, so a length in between is tried. Its OutOfMemoryException there
        // (refused before anything is allocated) is the length limit, not memory.
        private static bool PastFrameworkLimit(int length) => length > 0x7FEFFFFF
            && System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal);

        public int Count => _count;

        public void PushBack(T item)
        {
            EnsureCapacity();
            _array[Slot(_count)] = item;
            _count++;
            unchecked { _version++; }
        }

        public void PushFront(T item)
        {
            EnsureCapacity();
            _head = _head == 0 ? _array.Length - 1 : _head - 1; // _head - 1 + length overflowed int past 2^30
            _array[_head] = item;
            _count++;
            unchecked { _version++; }
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
            unchecked { _version++; }
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
            unchecked { _version++; }
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
                if (unchecked((uint)index >= (uint)_count))
                    throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be in [0, {_count}).");
                return _array[Slot(index)];
            }
        }

        public void Clear()
        {
            // Only the occupied slots: wiping the whole array made Clear cost the largest size ever held.
            int first = Math.Min(_count, _array.Length - _head);
            Array.Clear(_array, _head, first);
            Array.Clear(_array, 0, _count - first);
            _head = 0;
            _count = 0;
            unchecked { _version++; }
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
            // The check comes before each step and after the last, so emptying the deque before the first MoveNext throws too.
            for (int i = 0; ; i++)
            {
                if (version != _version)
                    throw new InvalidOperationException("The deque was modified during enumeration.");
                if (i >= _count) yield break;
                yield return _array[Slot(i)];
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
            int target = (int)Math.Min((long)_array.Length * 2, MaxLength);
            T[] grown;
            try { grown = new T[target]; }
            catch (OutOfMemoryException) when (PastFrameworkLimit(target))
            {
                // .NET Framework's limit for this T: grow to it, or report the deque full as on .NET.
                if (_array.Length >= 0x7FEFFFFF) throw new InvalidOperationException("The deque is at the maximum array length.");
                grown = new T[0x7FEFFFFF];
            }
            for (int i = 0; i < _count; i++)
                grown[i] = _array[Slot(i)];
            _array = grown;
            _head = 0;
        }

        // Physical index of logical position i (0 <= i < length). _head + i could overflow int for a huge array, and so
        // did _head + i - length, which adds first (an OverflowException in a checked build); length - i does not.
        private int Slot(int i)
        {
            int j = _head - (_array.Length - i);
            return j >= 0 ? j : j + _array.Length;
        }
    }
}
