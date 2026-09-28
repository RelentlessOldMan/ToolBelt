using System;
using ToolBelt.Functional;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Functional
{
    public sealed class EitherTests
    {
        public void Left_And_Right_Basics()
        {
            var left = Either<string, int>.FromLeft("err");
            Check.True(left.IsLeft);
            Check.False(left.IsRight);
            Check.Equal("err", left.Left);

            var right = Either<string, int>.FromRight(42);
            Check.True(right.IsRight);
            Check.Equal(42, right.Right);
        }

        public void WrongSide_Throws()
        {
            var left = Either<string, int>.FromLeft("e");
            Check.Throws<InvalidOperationException>(() => _ = left.Right);
            var right = Either<string, int>.FromRight(1);
            Check.Throws<InvalidOperationException>(() => _ = right.Left);
        }

        public void TryGet()
        {
            var right = Either<string, int>.FromRight(7);
            Check.True(right.TryGetRight(out int v) && v == 7);
            Check.False(right.TryGetLeft(out _));
        }

        public void Match()
        {
            Check.Equal("L:e", Either<string, int>.FromLeft("e").Match(l => $"L:{l}", r => $"R:{r}"));
            Check.Equal("R:9", Either<string, int>.FromRight(9).Match(l => $"L:{l}", r => $"R:{r}"));
        }

        public void MapLeft_And_MapRight()
        {
            var left = Either<int, string>.FromLeft(3);
            Check.Equal(6, left.MapLeft(x => x * 2).Left);
            Check.True(left.MapRight(s => s + "!").IsLeft); // right map passes left through

            var right = Either<int, string>.FromRight("hi");
            Check.Equal("hi!", right.MapRight(s => s + "!").Right);
            Check.True(right.MapLeft(x => x * 2).IsRight); // left map passes right through
        }

        public void ChangingLeftType_PreservesRight()
        {
            Either<int, string> right = Either<int, string>.FromRight("keep");
            Either<bool, string> mapped = right.MapLeft(x => x > 0);
            Check.True(mapped.IsRight);
            Check.Equal("keep", mapped.Right);
        }

        public void FactoryHelpers()
        {
            Check.True(Either.FromLeft<string, int>("x").IsLeft);
            Check.True(Either.FromRight<string, int>(1).IsRight);
        }

        public void DefaultStruct_IsLeft()
        {
            Either<string, int> def = default;
            Check.True(def.IsLeft);
        }
    }
}
