using System;
using System.ComponentModel;
using System.Linq;
using ToolBelt.Enums;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Enums
{
    public sealed class EnumExtensionsTests
    {
        private enum Color
        {
            Red,
            [Description("Bright Green")] Green,
            Blue,
        }

        [Flags]
        private enum Perm
        {
            None = 0,
            Read = 1,
            Write = 2,
            Execute = 4,
        }

        public void GetValuesAndNames()
        {
            Check.True(EnumExtensions.GetValues<Color>().SequenceEqual(new[] { Color.Red, Color.Green, Color.Blue }));
            Check.True(EnumExtensions.GetNames<Color>().SequenceEqual(new[] { "Red", "Green", "Blue" }));
        }

        public void TryParseName_IsCaseInsensitiveByDefault()
        {
            Check.True(EnumExtensions.TryParseName<Color>("green", out var c) && c == Color.Green);
            Check.True(EnumExtensions.TryParseName<Color>("BLUE", out var b) && b == Color.Blue);
        }

        public void TryParseName_CaseSensitive_Respected()
        {
            Check.False(EnumExtensions.TryParseName<Color>("green", out _, ignoreCase: false));
            Check.True(EnumExtensions.TryParseName<Color>("Green", out _, ignoreCase: false));
        }

        public void TryParseName_RejectsNumbersAndUnknown()
        {
            Check.False(EnumExtensions.TryParseName<Color>("1", out _));       // numeric string rejected
            Check.False(EnumExtensions.TryParseName<Color>("Purple", out _));  // undefined name
            Check.False(EnumExtensions.TryParseName<Color>(null, out _));
            Check.False(EnumExtensions.TryParseName<Color>("", out _));
        }

        public void GetDescription_UsesAttributeOrFallsBackToName()
        {
            Check.Equal("Bright Green", Color.Green.GetDescription());
            Check.Equal("Red", Color.Red.GetDescription()); // no attribute → member name
        }

        public void GetFlags_DecomposesSetBits()
        {
            var flags = (Perm.Read | Perm.Execute).GetFlags();
            Check.True(flags.SequenceEqual(new[] { Perm.Read, Perm.Execute }));
        }

        public void GetFlags_ExcludesZeroValue()
        {
            var flags = Perm.None.GetFlags();
            Check.Equal(0, flags.Count); // None (0) is never reported as a set flag
        }

        public void GetFlags_AllBits()
        {
            var flags = (Perm.Read | Perm.Write | Perm.Execute).GetFlags();
            Check.True(flags.SequenceEqual(new[] { Perm.Read, Perm.Write, Perm.Execute }));
        }
    }
}
