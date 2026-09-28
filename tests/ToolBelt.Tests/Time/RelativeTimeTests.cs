using System;
using ToolBelt.Time;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Time
{
    public sealed class RelativeTimeTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        public void JustNow_WithinFiveSeconds()
        {
            Check.Equal("just now", RelativeTime.Format(Now, Now));
            Check.Equal("just now", RelativeTime.Format(Now - TimeSpan.FromSeconds(4), Now));
            Check.Equal("just now", RelativeTime.Format(Now + TimeSpan.FromSeconds(4), Now));
        }

        public void PastBuckets()
        {
            Check.Equal("30 seconds ago", RelativeTime.Format(Now - TimeSpan.FromSeconds(30), Now));
            Check.Equal("1 minute ago", RelativeTime.Format(Now - TimeSpan.FromSeconds(90), Now));
            Check.Equal("2 hours ago", RelativeTime.Format(Now - TimeSpan.FromHours(2), Now));
            Check.Equal("3 days ago", RelativeTime.Format(Now - TimeSpan.FromDays(3), Now));
            Check.Equal("2 months ago", RelativeTime.Format(Now - TimeSpan.FromDays(75), Now));
            Check.Equal("1 year ago", RelativeTime.Format(Now - TimeSpan.FromDays(400), Now));
        }

        public void FutureBuckets()
        {
            Check.Equal("in 45 seconds", RelativeTime.Format(Now + TimeSpan.FromSeconds(45), Now));
            Check.Equal("in 2 hours", RelativeTime.Format(Now + TimeSpan.FromHours(2), Now));
            Check.Equal("in 5 days", RelativeTime.Format(Now + TimeSpan.FromDays(5), Now));
        }

        public void SingularVsPlural()
        {
            Check.Equal("1 hour ago", RelativeTime.Format(Now - TimeSpan.FromHours(1), Now));
            Check.Equal("2 hours ago", RelativeTime.Format(Now - TimeSpan.FromHours(2), Now));
        }

        public void ClockOverload_UsesInjectedClock()
        {
            var fixedNow = Now;
            string text = RelativeTime.Format(Now - TimeSpan.FromMinutes(5), () => fixedNow);
            Check.Equal("5 minutes ago", text);
        }
    }
}
