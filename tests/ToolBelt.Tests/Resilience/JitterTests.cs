using System;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Resilience
{
    public sealed class JitterTests
    {
        private static Func<double> Fixed(double value) => () => value;

        public void None_ReturnsDelayUnchanged()
        {
            var delay = TimeSpan.FromSeconds(3);
            Check.Equal(delay, Jitter.Apply(delay, JitterMode.None, Fixed(0.5)));
            // None ignores the random source entirely — a null source is fine.
            Check.Equal(delay, Jitter.Apply(delay, JitterMode.None, null!));
        }

        public void Full_ScalesByRandom()
        {
            var delay = TimeSpan.FromMilliseconds(1000);
            Check.Close(0, Jitter.Apply(delay, JitterMode.Full, Fixed(0)).TotalMilliseconds, 1e-9);
            Check.Close(250, Jitter.Apply(delay, JitterMode.Full, Fixed(0.25)).TotalMilliseconds, 1e-9);
            Check.Close(990, Jitter.Apply(delay, JitterMode.Full, Fixed(0.99)).TotalMilliseconds, 1e-9);
        }

        public void Equal_HalfPlusRandomHalf()
        {
            var delay = TimeSpan.FromMilliseconds(1000);
            Check.Close(500, Jitter.Apply(delay, JitterMode.Equal, Fixed(0)).TotalMilliseconds, 1e-9);
            Check.Close(750, Jitter.Apply(delay, JitterMode.Equal, Fixed(0.5)).TotalMilliseconds, 1e-9);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Jitter.Apply(TimeSpan.FromSeconds(-1), JitterMode.Full, Fixed(0.5)));
            Check.Throws<ArgumentNullException>(() => Jitter.Apply(TimeSpan.FromSeconds(1), JitterMode.Full, null!));
            Check.Throws<ArgumentOutOfRangeException>(() => Jitter.Apply(TimeSpan.FromSeconds(1), JitterMode.Full, Fixed(1.0)));
        }

        // Property: Full stays within [0, delay]; Equal within [delay/2, delay], over many random draws.
        public void Properties_OverRandomDraws()
        {
            var rng = new Random(31337);
            var delay = TimeSpan.FromMilliseconds(1000);
            for (int i = 0; i < 5000; i++)
            {
                double r = rng.NextDouble(); // [0, 1)
                double full = Jitter.Apply(delay, JitterMode.Full, () => r).TotalMilliseconds;
                double equal = Jitter.Apply(delay, JitterMode.Equal, () => r).TotalMilliseconds;

                Check.True(full >= 0 && full <= 1000 + 1e-9, $"full {full} out of range");
                Check.True(equal >= 500 - 1e-9 && equal <= 1000 + 1e-9, $"equal {equal} out of range");
            }
        }
    }
}
