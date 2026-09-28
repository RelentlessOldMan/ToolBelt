using System;
using System.Collections.Generic;
using ToolBelt.Guards;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Guards
{
    public sealed class GuardTests
    {
        public void NotNull_ReturnsValue_WhenNotNull()
        {
            var obj = "hello";
            Check.Equal(obj, Guard.NotNull(obj));
        }

        public void NotNull_Throws_WhenNull()
        {
            string? value = null;
            Check.Throws<ArgumentNullException>(() => Guard.NotNull(value));
        }

        public void NotNull_NullableStruct_UnwrapsValue()
        {
            int? value = 42;
            Check.Equal(42, Guard.NotNull(value));
        }

        public void NotNullOrEmpty_String_Throws_OnEmpty()
        {
            Check.Throws<ArgumentException>(() => Guard.NotNullOrEmpty(""));
        }

        public void NotNullOrWhiteSpace_Throws_OnWhitespace()
        {
            Check.Throws<ArgumentException>(() => Guard.NotNullOrWhiteSpace("   "));
        }

        public void NotNullOrEmpty_Collection_ReturnsValue()
        {
            var list = new List<int> { 1, 2, 3 };
            Check.Equal(3, Guard.NotNullOrEmpty(list).Count);
        }

        public void Positive_Throws_OnZero()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Guard.Positive(0));
        }

        public void NonNegative_Allows_Zero()
        {
            Check.Equal(0, Guard.NonNegative(0));
        }

        public void InRange_Throws_OutsideBounds()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Guard.InRange(11, 0, 10));
        }

        public void Index_Throws_WhenEqualToCount()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Guard.Index(5, 5));
        }

        public void ParamName_IsCapturedFromExpression()
        {
            string? theArgument = null;
            var ex = Check.Throws<ArgumentNullException>(() => Guard.NotNull(theArgument));
            Check.Equal("theArgument", ex.ParamName);
        }
    }
}
