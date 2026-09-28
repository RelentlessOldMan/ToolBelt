using System;
using ToolBelt.Functional;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Functional
{
    public sealed class OptionTests
    {
        public void Some_HoldsValue()
        {
            var o = Option<int>.Some(7);
            Check.True(o.HasValue);
            Check.False(o.IsNone);
            Check.Equal(7, o.Value);
        }

        public void None_HasNoValue()
        {
            var o = Option<int>.None;
            Check.True(o.IsNone);
            Check.Throws<InvalidOperationException>(() => _ = o.Value);
        }

        public void DefaultStruct_IsNone()
        {
            Option<string> def = default;
            Check.True(def.IsNone);
        }

        public void TryGetValue_AndDefault()
        {
            Check.True(Option<int>.Some(3).TryGetValue(out int v) && v == 3);
            Check.False(Option<int>.None.TryGetValue(out _));
            Check.Equal(-1, Option<int>.None.GetValueOrDefault(-1));
        }

        public void Map_And_Bind()
        {
            Check.Equal(20, Option<int>.Some(10).Map(x => x * 2).Value);
            Check.True(Option<int>.None.Map(x => x * 2).IsNone);

            Option<int> Half(int x) => x % 2 == 0 ? Option<int>.Some(x / 2) : Option<int>.None;
            Check.Equal(5, Option<int>.Some(10).Bind(Half).Value);
            Check.True(Option<int>.Some(7).Bind(Half).IsNone);
        }

        public void Where_Filters()
        {
            Check.True(Option<int>.Some(4).Where(x => x > 3).HasValue);
            Check.True(Option<int>.Some(2).Where(x => x > 3).IsNone);
            Check.True(Option<int>.None.Where(x => true).IsNone);
        }

        public void Match_CollapsesBothCases()
        {
            Check.Equal("some:9", Option<int>.Some(9).Match(v => $"some:{v}", () => "none"));
            Check.Equal("none", Option<int>.None.Match(v => $"some:{v}", () => "none"));
        }

        public void FromNullable_Reference()
        {
            Check.True(Option.FromNullable("x").HasValue);
            Check.True(Option.FromNullable((string?)null).IsNone);
        }

        public void FromNullable_Struct()
        {
            Check.True(Option.FromNullable((int?)5).HasValue);
            Check.True(Option.FromNullable((int?)null).IsNone);
        }
    }
}
