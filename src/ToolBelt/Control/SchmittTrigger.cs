// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Control
{
    /// <summary>
    /// A comparator with hysteresis: turns on when the input reaches the high threshold and only turns off again when it
    /// falls to the low threshold, so a noisy signal hovering near one level doesn't chatter on and off. The thermostat,
    /// level-switch and alarm-with-deadband building block. Not thread-safe.
    /// </summary>
    public sealed class SchmittTrigger
    {
        public SchmittTrigger(double lowThreshold, double highThreshold, bool initialState = false)
        {
            if (double.IsNaN(lowThreshold) || double.IsNaN(highThreshold) || !(lowThreshold <= highThreshold))
                throw new ArgumentException("The low threshold must not exceed the high threshold.");
            Low = lowThreshold;
            High = highThreshold;
            State = initialState;
        }

        public double Low { get; }
        public double High { get; }

        /// <summary>The current output.</summary>
        public bool State { get; private set; }

        /// <summary>True when the last <see cref="Update"/> changed <see cref="State"/>.</summary>
        public bool Changed { get; private set; }

        /// <summary>Feeds one input sample and returns the (possibly switched) state. NaN leaves the state alone.</summary>
        public bool Update(double input)
        {
            bool next = State ? !(input <= Low) : input >= High;
            Changed = next != State;
            State = next;
            return State;
        }

        public void Reset(bool state = false)
        {
            State = state;
            Changed = false;
        }

        /// <summary>The state after each sample of <paramref name="samples"/>, starting from <paramref name="initialState"/>.</summary>
        public static bool[] Apply(IReadOnlyList<double> samples, double lowThreshold, double highThreshold, bool initialState = false)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            var trigger = new SchmittTrigger(lowThreshold, highThreshold, initialState);
            var result = new bool[samples.Count];
            for (int i = 0; i < result.Length; i++) result[i] = trigger.Update(samples[i]);
            return result;
        }
    }
}
