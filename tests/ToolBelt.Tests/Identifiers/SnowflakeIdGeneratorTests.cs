using System;
using System.Collections.Generic;
using ToolBelt.Identifiers;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Identifiers
{
    public sealed class SnowflakeIdGeneratorTests
    {
        private sealed class FakeClock
        {
            public DateTimeOffset Now = new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero);
            public Func<DateTimeOffset> Func => () => Now;
            public void Advance(TimeSpan by) => Now += by;
        }

        public void IdsAreStrictlyIncreasing_AndUnique()
        {
            var clock = new FakeClock();
            var gen = new SnowflakeIdGenerator(machineId: 1, clock: clock.Func);
            var seen = new HashSet<long>();

            long previous = long.MinValue;
            for (int i = 0; i < 10000; i++) // > 4096 forces sequence overflow into the next ms
            {
                long id = gen.NextId();
                Check.True(id > previous, $"id {id} not greater than {previous}");
                Check.True(seen.Add(id), $"duplicate id {id}");
                previous = id;
            }
        }

        public void Decode_RoundTripsComponents()
        {
            var clock = new FakeClock();
            var epoch = SnowflakeIdGenerator.DefaultEpoch;
            var gen = new SnowflakeIdGenerator(machineId: 42, epoch: epoch, clock: clock.Func);

            long expectedMs = (long)(clock.Now - epoch).TotalMilliseconds;
            long id = gen.NextId();
            var (ts, machine, seq) = SnowflakeIdGenerator.Decode(id);

            Check.Equal(expectedMs, ts);
            Check.Equal(42, machine);
            Check.Equal(0, seq);
        }

        public void SequenceIncrementsWithinSameMillisecond()
        {
            var clock = new FakeClock();
            var gen = new SnowflakeIdGenerator(machineId: 0, clock: clock.Func);
            Check.Equal(0, SnowflakeIdGenerator.Decode(gen.NextId()).Sequence);
            Check.Equal(1, SnowflakeIdGenerator.Decode(gen.NextId()).Sequence);
            Check.Equal(2, SnowflakeIdGenerator.Decode(gen.NextId()).Sequence);
        }

        public void AdvancingClock_ResetsSequence()
        {
            var clock = new FakeClock();
            var gen = new SnowflakeIdGenerator(machineId: 0, clock: clock.Func);
            gen.NextId();
            gen.NextId();
            clock.Advance(TimeSpan.FromMilliseconds(5));
            Check.Equal(0, SnowflakeIdGenerator.Decode(gen.NextId()).Sequence);
        }

        public void InvalidMachineId_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new SnowflakeIdGenerator(1024));
            Check.Throws<ArgumentOutOfRangeException>(() => new SnowflakeIdGenerator(-1));
        }

        public void ClockBeforeEpoch_Throws()
        {
            var early = new FakeClock { Now = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero) };
            var gen = new SnowflakeIdGenerator(0, clock: early.Func);
            Check.Throws<InvalidOperationException>(() => gen.NextId());
        }
    }
}
