using System;
using System.Globalization;
using System.Security.Cryptography;
using ToolBelt.Net;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;
using ToolBelt.Time;

namespace ToolBelt.Tests.Time
{
    // Round 166 review regressions (Security, net, time, tenth pass): each failed on the code before its fix.
    public sealed class SecurityNetTimeReview10Tests
    {
        // ---- CronSchedule across a fall-back that crosses midnight ----

        // A field: "*", "*/k" or one number.
        private static bool FieldMatches(string field, int value)
        {
            if (field == "*") return true;
            if (field.StartsWith("*/", StringComparison.Ordinal)) return value % int.Parse(field.Substring(2), CultureInfo.InvariantCulture) == 0;
            return value == int.Parse(field, CultureInfo.InvariantCulture);
        }

        // The reference for a schedule whose minute field starts with '*' (it runs at every matching instant, in both
        // passes): the first UTC minute after `after` whose wall time in the zone matches, walked minute by minute. Never
        // restricts both day fields.
        private static DateTimeOffset? Reference(string expr, DateTimeOffset after, TimeZoneInfo zone, int days)
        {
            string[] f = expr.Split(' ');
            long t = after.UtcTicks - after.UtcTicks % TimeSpan.TicksPerMinute + TimeSpan.TicksPerMinute;
            for (long end = t + days * TimeSpan.TicksPerDay; t < end; t += TimeSpan.TicksPerMinute)
            {
                var utc = new DateTime(t, DateTimeKind.Utc);
                TimeSpan offset = zone.GetUtcOffset(utc);
                var wall = new DateTime(t + offset.Ticks);
                if (FieldMatches(f[0], wall.Minute) && FieldMatches(f[1], wall.Hour) && FieldMatches(f[2], wall.Day)
                    && FieldMatches(f[3], wall.Month) && FieldMatches(f[4], (int)wall.DayOfWeek))
                    return new DateTimeOffset(wall, offset);
            }
            return null;
        }

        // A zone that falls back at `fallAt` (wall time, on the first Sunday of November) by `delta`, Newfoundland-like
        // when fallAt is 00:01.
        private static TimeZoneInfo FallBackZone(TimeSpan baseOffset, TimeSpan delta, TimeSpan springAt, TimeSpan fallAt)
        {
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2030, 12, 31), delta,
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1).Add(springAt), 3, 2, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1).Add(fallAt), 11, 1, DayOfWeek.Sunday));
            return TimeZoneInfo.CreateCustomTimeZone("r166", baseOffset, "r166", "r166", "r166d", new[] { rule });
        }

        // The day-jump looked only at days after the current wall date, so a fall-back that moved the clock back across
        // midnight onto a matching day was leapt over: `* 23 * * 6` ran a week late and `*/5 * 6 11 *` a year late.
        public void Cron_AFallBackAcrossMidnightOntoAMatchingDayIsNotSkipped()
        {
            var zone = FallBackZone(TimeSpan.FromHours(-3.5), TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            var after = new DateTimeOffset(2010, 11, 6, 23, 59, 30, TimeSpan.FromHours(-2.5));
            Check.Equal(new DateTimeOffset(2010, 11, 6, 23, 1, 0, TimeSpan.FromHours(-3.5)),
                new CronSchedule("* 23 * * 6").GetNextOccurrence(after, zone), "weekly");
            Check.Equal(new DateTimeOffset(2010, 11, 6, 23, 5, 0, TimeSpan.FromHours(-3.5)),
                new CronSchedule("*/5 * 6 11 *").GetNextOccurrence(after, zone), "yearly");

            var rng = new Random(166);
            for (int trial = 0; trial < 300; trial++)
            {
                var baseOffset = TimeSpan.FromMinutes(rng.Next(-12 * 4, 12 * 4 + 1) * 15);
                var delta = TimeSpan.FromMinutes(30 * rng.Next(1, 5));
                var fallAt = TimeSpan.FromMinutes(rng.Next(0, 4) == 0 ? rng.Next(0, 24 * 60) : rng.Next(0, 120));
                var z = FallBackZone(baseOffset, delta, TimeSpan.FromHours(2), fallAt);
                int year = rng.Next(2001, 2029);
                // The fall-back instant: the first Sunday of November at fallAt, daylight time.
                var nov1 = new DateTime(year, 11, 1);
                var sunday = nov1.AddDays(((int)DayOfWeek.Sunday - (int)nov1.DayOfWeek + 7) % 7);
                var fall = new DateTimeOffset(sunday.Add(fallAt), baseOffset + delta);
                var start = fall.AddMinutes(-rng.Next(0, 4 * 60));
                string expr = rng.Next(3) switch
                {
                    0 => "* " + rng.Next(20, 24).ToString(CultureInfo.InvariantCulture) + " * * " + rng.Next(0, 7).ToString(CultureInfo.InvariantCulture),
                    1 => "*/" + rng.Next(1, 16).ToString(CultureInfo.InvariantCulture) + " * " + fall.AddDays(-rng.Next(0, 3)).Day.ToString(CultureInfo.InvariantCulture) + " * *",
                    _ => "* * * * " + ((int)fall.AddDays(-rng.Next(0, 2)).DayOfWeek).ToString(CultureInfo.InvariantCulture),
                };
                DateTimeOffset? expected = Reference(expr, start, z, 40);
                if (expected is null) continue;
                DateTimeOffset got = new CronSchedule(expr).GetNextOccurrence(start, z);
                Check.Equal(expected.Value.UtcTicks, got.UtcTicks, "trial " + trial + ": " + expr + " after " + start.ToString("o", CultureInfo.InvariantCulture) + " got " + got.ToString("o", CultureInfo.InvariantCulture) + " expected " + expected.Value.ToString("o", CultureInfo.InvariantCulture));
                Check.Equal(expected.Value.Offset, got.Offset, "trial " + trial + " offset");
            }
        }

        // ---- Hashing.CreateIncremental ----

        // A default name threw ArgumentNullException naming hashAlgorithm (ArgumentException with a bare resource key on
        // .NET Framework), and an unknown one CryptographicException.
        public void CreateIncremental_RefusesABadAlgorithmByName()
        {
            var empty = Check.Throws<ArgumentException>(() => Hashing.CreateIncremental(default(HashAlgorithmName)));
            Check.Equal("algorithm", empty.ParamName, "default name");
            Check.True(empty.GetType() == typeof(ArgumentException), "type " + empty.GetType());
            var unknown = Check.Throws<ArgumentException>(() => Hashing.CreateIncremental(new HashAlgorithmName("FOO")));
            Check.Equal("algorithm", unknown.ParamName, "unknown name");
            using (var h = Hashing.CreateIncremental())
            {
                h.AppendData(new byte[] { 1, 2, 3 });
                Check.Equal(Hashing.ToHex(Hashing.Sha256(new byte[] { 1, 2, 3 })), Hashing.ToHex(h.GetHashAndReset()), "default is SHA-256");
            }
            using (var h = Hashing.CreateIncremental(HashAlgorithmName.SHA512))
            {
                h.AppendData(new byte[] { 9 });
                Check.Equal(Hashing.ToHex(Hashing.Sha512(new byte[] { 9 })), Hashing.ToHex(h.GetHashAndReset()), "SHA-512");
            }
        }

        // ---- TcpLineClient ----

        // A blank host went to DNS and failed as SocketException "No such host is known"; PortCheck and HostInfo refuse it.
        public void TcpLineClient_RefusesABlankHost()
        {
            foreach (string host in new[] { " ", "\t", "  \r\n" })
            {
                var ex = Check.Throws<ArgumentException>(() => new TcpLineClient(host, 80, TimeSpan.FromSeconds(5)).Dispose());
                Check.Equal("host", ex.ParamName, "host '" + host + "'");
            }
        }
    }
}
