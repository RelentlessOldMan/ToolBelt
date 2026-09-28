using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolBelt.Functional;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Functional
{
    public sealed class MemoizeTests
    {
        public void Function_ComputesOncePerArg()
        {
            int calls = 0;
            var square = Memoize.Function<int, int>(x => { calls++; return x * x; });

            Check.Equal(9, square(3));
            Check.Equal(9, square(3));
            Check.Equal(16, square(4));
            Check.Equal(2, calls); // one per distinct arg
        }

        public void Function_CustomComparer()
        {
            int calls = 0;
            var upper = Memoize.Function<string, string>(
                s => { calls++; return s.ToUpperInvariant(); },
                StringComparer.OrdinalIgnoreCase);

            Check.Equal("ABC", upper("abc"));
            Check.Equal("ABC", upper("ABC")); // same key under comparer
            Check.Equal(1, calls);
        }

        public void Factory_ComputesOnce()
        {
            int calls = 0;
            var lazy = Memoize.Factory(() => { calls++; return 42; });
            Check.Equal(42, lazy());
            Check.Equal(42, lazy());
            Check.Equal(1, calls);
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Memoize.Function<int, int>(null!));
            Check.Throws<ArgumentNullException>(() => Memoize.Factory<int>(null!));
            Check.Throws<ArgumentNullException>(() => Memoize.ThreadSafe<int, int>(null!));
        }

        // ThreadSafe: concurrent callers all observe a single, consistent cached result per key.
        public void ThreadSafe_ConsistentUnderContention()
        {
            var memo = Memoize.ThreadSafe<int, object>(_ => new object());
            var results = new object[1000];
            Parallel.For(0, 1000, i => results[i] = memo(0)); // all ask for key 0
            Check.True(results.All(r => ReferenceEquals(r, results[0])), "all callers must see the same instance");
        }
    }
}
