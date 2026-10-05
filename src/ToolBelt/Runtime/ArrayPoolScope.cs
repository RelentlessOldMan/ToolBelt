// ToolBelt drop-in — fully self-contained (BCL only).
using System;
#if !NETSTANDARD2_0
using System.Buffers;
#endif

namespace ToolBelt.Runtime
{
    /// <summary>Factory for <see cref="ArrayPoolScope{T}"/> (lets the element type be stated once).</summary>
    public static class ArrayPoolScope
    {
        /// <summary>
        /// Rents a scratch array of at least <paramref name="length"/> elements; dispose the scope (with <c>using</c>)
        /// to return it. Set <paramref name="clearOnReturn"/> when the buffer held anything sensitive.
        /// </summary>
        public static ArrayPoolScope<T> Rent<T>(int length, bool clearOnReturn = false)
            => new ArrayPoolScope<T>(length, clearOnReturn);
    }

    /// <summary>
    /// A self-returning rented array: the scratch-buffer pattern without the bookkeeping. On targets with a shared
    /// array pool it rents from <c>ArrayPool&lt;T&gt;.Shared</c> and returns on <see cref="Dispose"/>; on
    /// netstandard2.0 (no in-box pool) it simply allocates, so calling code is identical everywhere.
    /// <para>
    /// Two rules come with pooling. First, <see cref="Array"/> may be <b>longer</b> than requested and may hold
    /// <b>stale data</b> from a previous renter — use <see cref="Length"/>, <see cref="Segment"/> or
    /// <c>Span</c>, and never assume zeroes. Second, never keep a reference to the array after disposal: another
    /// renter may already own it. To make that second mistake loud, every accessor throws
    /// <see cref="ObjectDisposedException"/> once the scope is disposed, and disposal is idempotent so a double
    /// <c>Dispose</c> can never return the same array to the pool twice (which would hand it to two renters at
    /// once). It is a class rather than a struct for exactly that reason — a copied struct could double-return.
    /// Not thread-safe; one owner at a time.
    /// </para>
    /// </summary>
    public sealed class ArrayPoolScope<T> : IDisposable
    {
        private T[]? _array;
        private readonly bool _clearOnReturn;

        internal ArrayPoolScope(int length, bool clearOnReturn)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be non-negative.");
            Length = length;
            _clearOnReturn = clearOnReturn;
#if NETSTANDARD2_0
            _array = length == 0 ? new T[0] : new T[length];
            IsPooled = false;
#else
            _array = length == 0 ? System.Array.Empty<T>() : ArrayPool<T>.Shared.Rent(length);
            IsPooled = length != 0;
#endif
        }

        /// <summary>The requested length — the usable region is <c>[0, Length)</c>.</summary>
        public int Length { get; }

        /// <summary>True if the array came from (and will return to) a shared pool; false when it was simply allocated.</summary>
        public bool IsPooled { get; }

        /// <summary>True once <see cref="Dispose"/> has run.</summary>
        public bool IsDisposed => _array is null;

        /// <summary>The underlying array. May be longer than <see cref="Length"/> and may contain stale data.</summary>
        public T[] Array => _array ?? throw new ObjectDisposedException(nameof(ArrayPoolScope<T>));

        /// <summary>The usable region <c>[0, Length)</c> as a segment (works on every target).</summary>
        public ArraySegment<T> Segment => new ArraySegment<T>(Array, 0, Length);

#if !NETSTANDARD2_0
        /// <summary>The usable region <c>[0, Length)</c> as a span.</summary>
        public Span<T> Span => new Span<T>(Array, 0, Length);

        /// <summary>The usable region <c>[0, Length)</c> as memory.</summary>
        public Memory<T> Memory => new Memory<T>(Array, 0, Length);
#endif

        /// <summary>Gets or sets an element of the usable region (bounds-checked against <see cref="Length"/>).</summary>
        public T this[int index]
        {
            get
            {
                T[] array = Array;
                if ((uint)index >= (uint)Length) throw new ArgumentOutOfRangeException(nameof(index));
                return array[index];
            }
            set
            {
                T[] array = Array;
                if ((uint)index >= (uint)Length) throw new ArgumentOutOfRangeException(nameof(index));
                array[index] = value;
            }
        }

        /// <summary>Returns the array to the pool (if pooled). Safe to call more than once.</summary>
        public void Dispose()
        {
            T[]? array = _array;
            if (array is null)
                return;
            _array = null;
#if NETSTANDARD2_0
            if (_clearOnReturn)
                System.Array.Clear(array, 0, array.Length);
#else
            if (IsPooled)
                ArrayPool<T>.Shared.Return(array, _clearOnReturn);
            else if (_clearOnReturn)
                System.Array.Clear(array, 0, array.Length);
#endif
        }
    }
}
