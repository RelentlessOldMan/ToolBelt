using System;
using ToolBelt.Runtime;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Runtime
{
    public sealed class ArrayPoolScopeTests
    {
        public void Rent_ExposesRequestedLength()
        {
            using var scope = ArrayPoolScope.Rent<int>(100);
            Check.Equal(100, scope.Length);
            Check.True(scope.Array.Length >= 100, "pooled array may be longer");
            Check.True(scope.IsPooled, "net8 target rents from the shared pool");
            Check.Equal(100, scope.Segment.Count);
            Check.Equal(0, scope.Segment.Offset);
            Check.Equal(100, scope.Span.Length);
            Check.Equal(100, scope.Memory.Length);
        }

        public void Indexer_IsBoundedByLength_NotArrayLength()
        {
            using var scope = ArrayPoolScope.Rent<int>(10);
            scope[9] = 42;
            Check.Equal(42, scope[9]);
            // The underlying array is likely 16 long, but index 10 is outside the usable region.
            Check.Throws<ArgumentOutOfRangeException>(() => scope[10] = 1);
            Check.Throws<ArgumentOutOfRangeException>(() => { _ = scope[-1]; });
        }

        public void Span_WritesThroughToArray()
        {
            using var scope = ArrayPoolScope.Rent<int>(5);
            scope.Span.Fill(3);
            for (int i = 0; i < 5; i++) Check.Equal(3, scope.Array[i]);
        }

        public void Dispose_IsIdempotent_AndBlocksUseAfterReturn()
        {
            var scope = ArrayPoolScope.Rent<int>(64);
            scope.Dispose();
            scope.Dispose(); // must be harmless
            Check.True(scope.IsDisposed);
            Check.Throws<ObjectDisposedException>(() => { _ = scope.Array; });
            Check.Throws<ObjectDisposedException>(() => { _ = scope.Span; });
            Check.Throws<ObjectDisposedException>(() => { _ = scope[0]; });
        }

        public void DoubleDispose_NeverReturnsTheSameArrayTwice()
        {
            // If a double Dispose returned the array twice, the pool would hand it to two renters at once.
            var first = ArrayPoolScope.Rent<long>(3000);
            first.Dispose();
            first.Dispose();

            using var b = ArrayPoolScope.Rent<long>(3000);
            using var c = ArrayPoolScope.Rent<long>(3000);
            Check.False(ReferenceEquals(b.Array, c.Array), "two live scopes must never share an array");
        }

        public void ClearOnReturn_ZeroesTheBuffer()
        {
            var scope = ArrayPoolScope.Rent<int>(2000, clearOnReturn: true);
            int[] array = scope.Array;
            for (int i = 0; i < array.Length; i++) array[i] = 7;
            scope.Dispose();
            // Inspect the (now returned) array through our own reference: every slot must be cleared.
            foreach (int v in array) Check.Equal(0, v);
        }

        public void ZeroLength_IsEmptyAndUnpooled()
        {
            using var scope = ArrayPoolScope.Rent<string>(0);
            Check.Equal(0, scope.Length);
            Check.Equal(0, scope.Array.Length);
            Check.False(scope.IsPooled);
        }

        public void NegativeLength_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => ArrayPoolScope.Rent<int>(-1));
        }
    }
}
