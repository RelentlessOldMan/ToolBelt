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

        public void NotNull_NullableStruct_Throws_WhenNull()
        {
            int? value = null;
            Check.Throws<ArgumentNullException>(() => Guard.NotNull(value));
        }

        public void NotNullOrEmpty_String_Throws_OnNull()
        {
            // null vs empty are distinct branches (ArgumentNullException vs ArgumentException).
            Check.Throws<ArgumentNullException>(() => Guard.NotNullOrEmpty((string?)null));
        }

        public void NotNullOrWhiteSpace_Throws_OnNull_And_Returns_WhenValid()
        {
            Check.Throws<ArgumentNullException>(() => Guard.NotNullOrWhiteSpace(null));
            Check.Equal("ok", Guard.NotNullOrWhiteSpace("ok")); // the accept branch
        }

        public void NotNullOrEmpty_Collection_Throws_OnNullAndEmpty()
        {
            Check.Throws<ArgumentNullException>(() => Guard.NotNullOrEmpty((int[]?)null));
            Check.Throws<ArgumentException>(() => Guard.NotNullOrEmpty(new int[0]));
        }

        public void Positive_Int_ReturnsValue_WhenPositive()
        {
            Check.Equal(7, Guard.Positive(7)); // the accept branch (throw branch already covered)
        }

        public void Positive_Long_BothBranches()
        {
            Check.Equal(3L, Guard.Positive(3L));
            Check.Throws<ArgumentOutOfRangeException>(() => Guard.Positive(0L));
        }

        public void NonNegative_Int_Throws_OnNegative()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Guard.NonNegative(-1));
        }

        public void NonNegative_Long_Throws_OnNegative()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Guard.NonNegative(-1L));
        }

        public void InRange_Throws_BelowLowerBound()
        {
            // Existing tests only hit value > max; this exercises the value < min half of the condition.
            Check.Throws<ArgumentOutOfRangeException>(() => Guard.InRange(-1, 0, 10));
        }
    }
}
