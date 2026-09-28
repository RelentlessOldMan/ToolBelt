// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Time
{
    /// <summary>
    /// ISO 8601 week numbering: weeks start on Monday and week 1 is the week containing the year's first
    /// Thursday. Near a year boundary the ISO week-year can differ from the calendar year (e.g. 1 Jan may
    /// belong to week 52/53 of the previous year), which is why <see cref="GetWeekYear"/> exists.
    /// </summary>
    public static class IsoWeek
    {
        /// <summary>The ISO day of week: Monday = 1 … Sunday = 7.</summary>
        public static int GetIsoDayOfWeek(DateTime date) => ((int)date.DayOfWeek + 6) % 7 + 1;

        /// <summary>The ISO 8601 week number (1–53).</summary>
        public static int GetWeekOfYear(DateTime date)
        {
            // A date belongs to the week that contains its Thursday; the week number is that Thursday's
            // ordinal week within its year.
            DateTime thursday = date.AddDays(4 - GetIsoDayOfWeek(date));
            return (thursday.DayOfYear - 1) / 7 + 1;
        }

        /// <summary>The ISO 8601 week-year (the year that owns the week; may differ from the calendar year).</summary>
        public static int GetWeekYear(DateTime date)
        {
            DateTime thursday = date.AddDays(4 - GetIsoDayOfWeek(date));
            return thursday.Year;
        }
    }
}
