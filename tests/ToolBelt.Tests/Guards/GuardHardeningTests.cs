using System;
using ToolBelt.Guards;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Guards
{
    /// <summary>Edge cases for <see cref="Guard"/> — lives alongside the type per the *HardeningTests convention.</summary>
    public sealed class GuardHardeningTests
    {
        public void Positive_Throws_OnMinValue()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Guard.Positive(int.MinValue));
        }

        public void NonNegative_Allows_MaxValue()
        {
            Check.Equal(long.MaxValue, Guard.NonNegative(long.MaxValue));
        }

        public void Index_Throws_OnNegative_ViaUnsignedTrick()
        {
            // -1 reinterpreted as uint is huge, so a single unsigned compare catches negative indices too.
            Check.Throws<ArgumentOutOfRangeException>(() => Guard.Index(-1, 10));
        }

        public void Index_Allows_LastValidSlot()
        {
            Check.Equal(9, Guard.Index(9, 10));
        }

        public void InRange_Allows_ExactBounds()
        {
            Check.Equal(0, Guard.InRange(0, 0, 10));
            Check.Equal(10, Guard.InRange(10, 0, 10));
        }

        public void NotNullOrEmpty_String_PreservesWhitespaceOnlyContent()
        {
            // Whitespace is non-empty; only NotNullOrWhiteSpace should reject it.
            Check.Equal("  ", Guard.NotNullOrEmpty("  "));
        }
    }
}
