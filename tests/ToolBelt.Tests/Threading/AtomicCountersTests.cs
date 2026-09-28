using System.Threading.Tasks;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    public sealed class AtomicCountersTests
    {
        public void LongBasics()
        {
            var c = new AtomicLong(10);
            Check.Equal(11L, c.Increment());
            Check.Equal(10L, c.Decrement());
            Check.Equal(15L, c.Add(5));
            Check.Equal(15L, c.Exchange(0));
            Check.Equal(0L, c.Value);
            c.Value = 99;
            Check.Equal(99L, c.Value);
            Check.Equal(99L, c.CompareExchange(100, 99));
            Check.Equal(100L, c.Value);
        }

        public void LongParallelIncrementsAreExact()
        {
            var c = new AtomicLong();
            Parallel.For(0, 100000, _ => c.Increment());
            Check.Equal(100000L, c.Value);
        }

        public void DoubleBasics()
        {
            var c = new AtomicDouble(1.5);
            Check.Close(4.0, c.Add(2.5), 1e-12);
            Check.Close(4.0, c.Value, 1e-12);
            Check.Close(4.0, c.Exchange(0), 1e-12);
            Check.Close(0.0, c.Value, 1e-12);
        }

        public void DoubleParallelAddsAreExact()
        {
            var c = new AtomicDouble();
            Parallel.For(0, 100000, _ => c.Add(1.0));
            Check.Close(100000.0, c.Value, 1e-6);
        }
    }
}
