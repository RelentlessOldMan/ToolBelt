using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class ObjectPoolTests
    {
        public void RentCreatesWhenEmpty()
        {
            int created = 0;
            var pool = new ObjectPool<List<int>>(() => { created++; return new List<int>(); });
            var a = pool.Rent();
            Check.NotNull(a);
            Check.Equal(1, created);
        }

        public void ReturnThenRentReuses()
        {
            var pool = new ObjectPool<List<int>>(() => new List<int>());
            var a = pool.Rent();
            pool.Return(a);
            Check.Equal(1, pool.Count);
            var b = pool.Rent();
            Check.True(ReferenceEquals(a, b)); // same instance reused
            Check.Equal(0, pool.Count);
        }

        public void ResetAppliedOnReturn()
        {
            var pool = new ObjectPool<List<int>>(() => new List<int>(), reset: l => l.Clear());
            var a = pool.Rent();
            a.Add(1); a.Add(2);
            pool.Return(a);
            Check.Equal(0, a.Count); // reset cleared it
        }

        public void CapacityBounded()
        {
            var pool = new ObjectPool<object>(() => new object(), maxRetained: 2);
            pool.Return(new object());
            pool.Return(new object());
            pool.Return(new object()); // over capacity -> dropped
            Check.Equal(2, pool.Count);
        }

        public void Clear()
        {
            var pool = new ObjectPool<object>(() => new object());
            pool.Return(new object());
            pool.Clear();
            Check.Equal(0, pool.Count);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => new ObjectPool<object>(null!));
            Check.Throws<ArgumentOutOfRangeException>(() => new ObjectPool<object>(() => new object(), maxRetained: 0));
            var pool = new ObjectPool<object>(() => new object());
            Check.Throws<ArgumentNullException>(() => pool.Return(null!));
        }

        // Concurrency: hammer rent/return from many tasks; pool must never exceed capacity or corrupt.
        public void Property_ThreadSafe()
        {
            var pool = new ObjectPool<object>(() => new object(), maxRetained: 8);
            Parallel.For(0, 10000, _ =>
            {
                var item = pool.Rent();
                pool.Return(item);
            });
            Check.True(pool.Count <= 8, "pool exceeded capacity under contention");
        }
    }
}
