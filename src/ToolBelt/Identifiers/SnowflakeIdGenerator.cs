// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Identifiers
{
    /// <summary>
    /// Generates 64-bit, roughly time-ordered identifiers in the style of Twitter's Snowflake. The layout
    /// is: 1 unused sign bit, 41 bits of milliseconds since a custom epoch, 10 bits of machine id, and 12
    /// bits of per-millisecond sequence. IDs from one generator are strictly increasing. Time is read
    /// through an injectable clock, so tests are deterministic. Thread-safe.
    /// </summary>
    public sealed class SnowflakeIdGenerator
    {
        private const int MachineBits = 10;
        private const int SequenceBits = 12;
        private const long MaxMachineId = (1L << MachineBits) - 1;   // 1023
        private const long SequenceMask = (1L << SequenceBits) - 1;  // 4095

        /// <summary>Default epoch: 2020-01-01 UTC. 41 bits of ms give ~69 years of range from here.</summary>
        public static readonly DateTimeOffset DefaultEpoch = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly long _machineId;
        private readonly DateTimeOffset _epoch;
        private readonly Func<DateTimeOffset> _clock;
        private readonly object _gate = new object();

        private long _lastTimestamp = -1;
        private long _sequence;

        public SnowflakeIdGenerator(int machineId, DateTimeOffset? epoch = null, Func<DateTimeOffset>? clock = null)
        {
            if (machineId < 0 || machineId > MaxMachineId)
                throw new ArgumentOutOfRangeException(nameof(machineId), machineId, $"Machine id must be in [0, {MaxMachineId}].");
            _machineId = machineId;
            _epoch = epoch ?? DefaultEpoch;
            _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        }

        /// <summary>Produces the next id. Strictly greater than every id this instance returned before.</summary>
        public long NextId()
        {
            lock (_gate)
            {
                long now = CurrentMillis();
                if (now < _lastTimestamp)
                    now = _lastTimestamp; // clock went backwards: hold steady rather than emit a smaller id

                if (now == _lastTimestamp)
                {
                    _sequence = (_sequence + 1) & SequenceMask;
                    if (_sequence == 0)
                        now = _lastTimestamp + 1; // sequence exhausted this ms: borrow the next
                }
                else
                {
                    _sequence = 0;
                }

                _lastTimestamp = now;
                return (now << (MachineBits + SequenceBits))
                       | (_machineId << SequenceBits)
                       | _sequence;
            }
        }

        /// <summary>Splits an id into its (millisecondsSinceEpoch, machineId, sequence) parts.</summary>
        public static (long TimestampMs, int MachineId, int Sequence) Decode(long id)
        {
            long timestamp = id >> (MachineBits + SequenceBits);
            int machineId = (int)((id >> SequenceBits) & MaxMachineId);
            int sequence = (int)(id & SequenceMask);
            return (timestamp, machineId, sequence);
        }

        private long CurrentMillis()
        {
            long ms = (long)(_clock() - _epoch).TotalMilliseconds;
            if (ms < 0)
                throw new InvalidOperationException("The clock is before the configured epoch.");
            return ms;
        }
    }
}
