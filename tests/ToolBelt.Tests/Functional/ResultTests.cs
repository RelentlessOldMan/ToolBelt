using System;
using ToolBelt.Functional;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Functional
{
    public sealed class ResultTests
    {
        public void Success_HoldsValue()
        {
            var r = Result<int>.Success(42);
            Check.True(r.IsSuccess);
            Check.False(r.IsFailure);
            Check.Equal(42, r.Value);
        }

        public void Failure_HoldsError()
        {
            var r = Result<int>.Failure("nope");
            Check.True(r.IsFailure);
            Check.Equal("nope", r.Error);
        }

        public void Value_OnFailure_Throws()
        {
            var r = Result<int>.Failure("bad");
            Check.Throws<InvalidOperationException>(() => _ = r.Value);
        }

        public void Error_OnSuccess_Throws()
        {
            var r = Result<int>.Success(1);
            Check.Throws<InvalidOperationException>(() => _ = r.Error);
        }

        public void TryGetValue_AndDefault()
        {
            Check.True(Result<int>.Success(5).TryGetValue(out int v) && v == 5);
            Check.False(Result<int>.Failure("x").TryGetValue(out _));
            Check.Equal(-1, Result<int>.Failure("x").GetValueOrDefault(-1));
        }

        public void Map_TransformsSuccess_PropagatesFailure()
        {
            Check.Equal(6, Result<int>.Success(3).Map(x => x * 2).Value);
            var mapped = Result<int>.Failure("err").Map(x => x * 2);
            Check.True(mapped.IsFailure);
            Check.Equal("err", mapped.Error);
        }

        public void Bind_Chains()
        {
            Result<int> Half(int x) => x % 2 == 0 ? Result<int>.Success(x / 2) : Result<int>.Failure("odd");
            Check.Equal(5, Result<int>.Success(10).Bind(Half).Value);
            Check.True(Result<int>.Success(7).Bind(Half).IsFailure);
            Check.True(Result<int>.Failure("start").Bind(Half).IsFailure);
        }

        public void Match_CollapsesBothCases()
        {
            Check.Equal("ok:2", Result<int>.Success(2).Match(v => $"ok:{v}", e => $"err:{e}"));
            Check.Equal("err:boom", Result<int>.Failure("boom").Match(v => $"ok:{v}", e => $"err:{e}"));
        }

        public void Try_CapturesException()
        {
            var ok = Result.Try(() => 10 / 2);
            Check.True(ok.IsSuccess);
            Check.Equal(5, ok.Value);

            var bad = Result.Try<int>(() => throw new InvalidOperationException("kaboom"));
            Check.True(bad.IsFailure);
            Check.Equal("kaboom", bad.Error);
        }

        public void Failure_NullError_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Result<int>.Failure(null!));
        }

        public void DefaultStruct_IsFailure()
        {
            Result<int> def = default;
            Check.True(def.IsFailure);
            Check.Equal("Unspecified error.", def.Error); // no error string on default
        }
    }
}
