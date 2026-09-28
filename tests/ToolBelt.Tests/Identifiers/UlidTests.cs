using System;
using System.Collections.Generic;
using ToolBelt.Identifiers;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Identifiers
{
    public sealed class UlidTests
    {
        private sealed class FakeClock
        {
            public DateTimeOffset Now = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public Func<DateTimeOffset> Func => () => Now;
            public void Advance(TimeSpan by) => Now += by;
        }

        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public void HasCorrectFormat()
        {
            var gen = new UlidGenerator(new FakeClock().Func, new Random(1));
            string ulid = gen.NewUlid();
            Check.Equal(26, ulid.Length);
            foreach (char c in ulid)
                Check.True(Alphabet.IndexOf(c) >= 0, $"'{c}' not in Crockford alphabet");
        }

        public void TimestampDecodes()
        {
            var clock = new FakeClock();
            var gen = new UlidGenerator(clock.Func, new Random(1));
            long expected = (long)(clock.Now - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalMilliseconds;
            string ulid = gen.NewUlid();
            Check.Equal(expected, UlidGenerator.DecodeTimestamp(ulid));
        }

        public void MonotonicWithinSameMillisecond()
        {
            var gen = new UlidGenerator(new FakeClock().Func, new Random(42));
            string previous = gen.NewUlid();
            for (int i = 0; i < 5000; i++)
            {
                string next = gen.NewUlid();
                Check.True(string.CompareOrdinal(next, previous) > 0,
                    $"ulid {next} not ordinally greater than {previous}");
                previous = next;
            }
        }

        public void AdvancingClock_IncreasesTimestampPortion()
        {
            var clock = new FakeClock();
            var gen = new UlidGenerator(clock.Func, new Random(7));
            string first = gen.NewUlid();
            clock.Advance(TimeSpan.FromSeconds(1));
            string second = gen.NewUlid();
            Check.True(UlidGenerator.DecodeTimestamp(second) > UlidGenerator.DecodeTimestamp(first));
            Check.True(string.CompareOrdinal(second, first) > 0);
        }

        public void UlidsAreUnique()
        {
            var gen = new UlidGenerator(new FakeClock().Func, new Random(99));
            var seen = new HashSet<string>();
            for (int i = 0; i < 10000; i++)
                Check.True(seen.Add(gen.NewUlid()), "duplicate ULID");
        }

        public void Decode_InvalidLength_Throws()
        {
            Check.Throws<FormatException>(() => UlidGenerator.DecodeTimestamp("TOOSHORT"));
        }
    }
}
