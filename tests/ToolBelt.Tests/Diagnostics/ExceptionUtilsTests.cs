using System;
using System.IO;
using System.Linq;
using ToolBelt.Diagnostics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Diagnostics
{
    public sealed class ExceptionUtilsTests
    {
        public void RootCauseFollowsInnerChain()
        {
            var root = new InvalidOperationException("root");
            var wrapped = new Exception("outer", new Exception("middle", root));
            Check.True(ReferenceEquals(root, ExceptionUtils.RootCause(wrapped)));
        }

        public void RootCauseThroughAggregate()
        {
            var root = new FormatException("bad");
            var agg = new AggregateException(new Exception("wrap", root));
            Check.True(ReferenceEquals(root, ExceptionUtils.RootCause(agg)));
        }

        public void FlattenExpandsAggregate()
        {
            var a = new Exception("a");
            var b = new Exception("b");
            var flat = ExceptionUtils.Flatten(new AggregateException(a, b));
            Check.True(flat.Contains(a) && flat.Contains(b));
            Check.Equal(3, flat.Count); // the aggregate itself + a + b
        }

        public void DescribeIncludesTypeAndMessage()
        {
            string text = ExceptionUtils.Describe(new InvalidOperationException("boom"), includeStackTrace: false);
            Check.True(text.Contains("InvalidOperationException"), text);
            Check.True(text.Contains("boom"), text);
        }

        public void ClassifiesTransientVsPermanent()
        {
            Check.True(ExceptionUtils.IsTransient(new TimeoutException()));
            Check.True(ExceptionUtils.IsTransient(new IOException("disk hiccup")));
            Check.True(ExceptionUtils.IsTransient(new Exception("wrap", new TimeoutException()))); // nested
            Check.False(ExceptionUtils.IsTransient(new ArgumentNullException("x")));
            Check.False(ExceptionUtils.IsTransient(new OperationCanceledException()));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => ExceptionUtils.RootCause(null!));
            Check.Throws<ArgumentNullException>(() => ExceptionUtils.IsTransient(null!));
        }
    }
}
