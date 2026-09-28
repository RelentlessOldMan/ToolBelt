// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ToolBelt.Runtime
{
    /// <summary>
    /// Records named milestones from a start instant so "why does this take so long to become responsive?"
    /// is answerable with data instead of guesses. The clock is injectable for deterministic tests. Not
    /// thread-safe (startup is typically single-threaded).
    /// </summary>
    public sealed class StartupTiming
    {
        private readonly Func<DateTimeOffset> _clock;
        private readonly DateTimeOffset _start;
        private readonly List<(string Name, TimeSpan Elapsed)> _milestones = new List<(string, TimeSpan)>();

        public StartupTiming(Func<DateTimeOffset>? clock = null)
        {
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _start = _clock();
        }

        /// <summary>Records a milestone with the time elapsed since construction.</summary>
        public void Mark(string milestone)
        {
            if (milestone is null) throw new ArgumentNullException(nameof(milestone));
            _milestones.Add((milestone, _clock() - _start));
        }

        /// <summary>The recorded milestones with cumulative elapsed time, in order.</summary>
        public IReadOnlyList<(string Name, TimeSpan Elapsed)> Milestones => _milestones;

        /// <summary>Total time from construction to the last milestone (or now if none).</summary>
        public TimeSpan Total => _milestones.Count > 0 ? _milestones[_milestones.Count - 1].Elapsed : _clock() - _start;

        /// <summary>A readable report: each milestone with cumulative and per-step deltas.</summary>
        public string Report()
        {
            var sb = new StringBuilder();
            TimeSpan previous = TimeSpan.Zero;
            foreach (var (name, elapsed) in _milestones)
            {
                double stepMs = (elapsed - previous).TotalMilliseconds;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,8:F1} ms (+{1,7:F1} ms)  {2}",
                    elapsed.TotalMilliseconds, stepMs, name));
                previous = elapsed;
            }
            return sb.ToString().TrimEnd();
        }
    }
}
