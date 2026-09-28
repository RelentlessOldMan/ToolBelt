// ToolBelt drop-in — fully self-contained (BCL only).
using System.Threading;

namespace ToolBelt.Threading
{
    /// <summary>
    /// A lock-free 64-bit integer counter using interlocked operations. Directly needed wherever many
    /// threads tally the same value (a metrics registry, throughput counters).
    /// </summary>
    public sealed class AtomicLong
    {
        private long _value;

        public AtomicLong(long initial = 0) => _value = initial;

        public long Value
        {
            get => Interlocked.Read(ref _value);
            set => Interlocked.Exchange(ref _value, value);
        }

        public long Increment() => Interlocked.Increment(ref _value);
        public long Decrement() => Interlocked.Decrement(ref _value);
        public long Add(long amount) => Interlocked.Add(ref _value, amount);
        public long Exchange(long newValue) => Interlocked.Exchange(ref _value, newValue);
        public long CompareExchange(long newValue, long comparand) => Interlocked.CompareExchange(ref _value, newValue, comparand);

        public override string ToString() => Value.ToString();
    }

    /// <summary>
    /// A lock-free double counter. <see cref="Add"/> uses a compare-and-swap loop because the framework has
    /// no interlocked add for <see cref="double"/>.
    /// </summary>
    public sealed class AtomicDouble
    {
        private double _value;

        public AtomicDouble(double initial = 0) => _value = initial;

        public double Value
        {
            get => Interlocked.CompareExchange(ref _value, 0d, 0d); // atomic read without mutation
            set => Interlocked.Exchange(ref _value, value);
        }

        public double Add(double amount)
        {
            while (true)
            {
                double current = Interlocked.CompareExchange(ref _value, 0d, 0d);
                double next = current + amount;
                if (Interlocked.CompareExchange(ref _value, next, current) == current)
                    return next;
            }
        }

        public double Exchange(double newValue) => Interlocked.Exchange(ref _value, newValue);
        public double CompareExchange(double newValue, double comparand) => Interlocked.CompareExchange(ref _value, newValue, comparand);

        public override string ToString() => Value.ToString();
    }
}
