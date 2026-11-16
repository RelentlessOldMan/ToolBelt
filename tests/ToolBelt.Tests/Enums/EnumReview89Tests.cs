using System.ComponentModel;
using ToolBelt.Enums;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Enums
{
    // Round 89 review regressions (Text & small types, fifth pass).
    public sealed class EnumReview89Tests
    {
        private enum Ali { [Description("Primary")] Alpha = 1, Beta = 1, Gamma = 2 }

        private enum Ali2 { Alpha = 1, [Description("Second")] Beta = 1, Gamma = 2 }

        private enum Ali3 { Alpha = 1, Beta = 1 }

        private enum Dup { Foo = 1, FOO = 2 }

        public void GetDescription_AnAliasKeepsItsDescriptionOnEveryRuntime()
        {
            Check.Equal("Primary", Ali.Alpha.GetDescription());   // .NET Framework's ToString picked Beta and lost it
            Check.Equal("Primary", Ali.Beta.GetDescription());
            Check.Equal("Second", Ali2.Alpha.GetDescription());   // was "Alpha"
            Check.Equal("Alpha", Ali3.Beta.GetDescription());     // the first declared name, not ToString's pick
            Check.Equal("Gamma", Ali.Gamma.GetDescription());
            Check.Equal("7", ((Ali)7).GetDescription());
        }

        [System.Flags]
        private enum Wide : ulong { None = 0, Low = 1, Top = 1UL << 63 }

        public void EnumFlags_UlongTopBitWorks()
        {
            // Convert.ToInt64 threw OverflowException for any value with bit 63 set.
            Check.True(EnumFlags.HasAnyFlags(Wide.Top | Wide.Low, Wide.Top));
            Check.True(EnumFlags.HasAllFlags(Wide.Top | Wide.Low, Wide.Top | Wide.Low));
            Check.False(EnumFlags.HasAllFlags(Wide.Low, Wide.Top | Wide.Low));
            Check.Equal(Wide.Top | Wide.Low, EnumFlags.Add(Wide.Low, Wide.Top));
            Check.Equal(Wide.Top, EnumFlags.Remove(Wide.Top | Wide.Low, Wide.Low));
            Check.Equal(Wide.Low, EnumFlags.Toggle(Wide.Top | Wide.Low, Wide.Top));
            Check.Equal(Wide.Top | Wide.Low, EnumFlags.Combine(Wide.Top, Wide.Low));
        }

        public void TryParseName_AnExactMatchWinsOverACaseInsensitiveOne()
        {
            Check.True(EnumExtensions.TryParseName("FOO", out Dup d));
            Check.Equal(Dup.FOO, d);                               // was Foo
            Check.True(EnumExtensions.TryParseName("Foo", out d));
            Check.Equal(Dup.Foo, d);
            Check.True(EnumExtensions.TryParseName("foo", out d));
            Check.Equal(Dup.Foo, d);
            Check.False(EnumExtensions.TryParseName("foo", out d, ignoreCase: false));
        }
    }
}
