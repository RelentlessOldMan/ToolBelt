using System;
using System.IO;
using System.Linq;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class SafeFileNameTests
    {
        public void ReplacesInvalidChars()
        {
            string safe = SafeFileName.MakeSafe("a/b:c*d?.txt");
            Check.False(safe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0, "no invalid chars remain");
        }

        public void TrimsTrailingDotsAndSpaces()
        {
            Check.Equal("name", SafeFileName.MakeSafe("name.  "));
            Check.Equal("name", SafeFileName.MakeSafe("name..."));
        }

        public void ReservedNames_Escaped()
        {
            Check.Equal("_CON", SafeFileName.MakeSafe("CON"));
            Check.Equal("_con.txt", SafeFileName.MakeSafe("con.txt"));
            Check.Equal("_LPT1", SafeFileName.MakeSafe("LPT1"));
        }

        public void Truncation_CannotExposeReservedName()
        {
            // "CONsole" capped to 3 chars would naively become the reserved device name "CON".
            string safe = SafeFileName.MakeSafe("CONsole", maxLength: 3);
            int dot = safe.IndexOf('.');
            string stem = dot < 0 ? safe : safe.Substring(0, dot);
            bool reserved = string.Equals(stem, "CON", StringComparison.OrdinalIgnoreCase);
            Check.False(reserved, $"truncated result '{safe}' must not be a reserved device name");
        }

        public void EmptyResult_BecomesUnderscore()
        {
            Check.Equal("_", SafeFileName.MakeSafe(""));
            Check.Equal("_", SafeFileName.MakeSafe("   "));
        }

        public void TruncatesToMaxLength()
        {
            string safe = SafeFileName.MakeSafe(new string('a', 500), maxLength: 100);
            Check.Equal(100, safe.Length);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => SafeFileName.MakeSafe(null!));
            Check.Throws<ArgumentOutOfRangeException>(() => SafeFileName.MakeSafe("x", maxLength: 0));
        }

        // Property: output has no invalid chars, is within the length cap, and is never empty.
        public void Properties_OverRandomInput()
        {
            var invalid = Path.GetInvalidFileNameChars();
            var palette = "ab/\\:*?\"<>| .cCoOnN1".ToCharArray();
            var rng = new Random(555);
            for (int trial = 0; trial < 2000; trial++)
            {
                int len = rng.Next(0, 20);
                var sb = new StringBuilder(len);
                for (int i = 0; i < len; i++)
                    sb.Append(palette[rng.Next(palette.Length)]);

                int max = rng.Next(1, 30);
                string safe = SafeFileName.MakeSafe(sb.ToString(), maxLength: max);

                Check.True(safe.Length >= 1, $"trial {trial}: empty");
                Check.True(safe.Length <= max, $"trial {trial}: over max");
                Check.False(safe.IndexOfAny(invalid) >= 0, $"trial {trial}: invalid char in '{safe}'");
            }
        }
    }
}
