using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    public sealed class ScopedContextTests
    {
        public void NoScopeByDefault()
        {
            Check.Equal(null, ScopedContext.Current);
        }

        public void PushAndRestore()
        {
            using (ScopedContext.Push("request-42"))
            {
                Check.Equal("request-42", ScopedContext.Current);
            }
            Check.Equal(null, ScopedContext.Current);
        }

        public void NestedScopesCombine()
        {
            using (ScopedContext.Push("outer"))
            {
                Check.Equal("outer", ScopedContext.Current);
                using (ScopedContext.Push("inner"))
                {
                    Check.Equal("outer/inner", ScopedContext.Current);
                }
                Check.Equal("outer", ScopedContext.Current); // inner popped
            }
        }

        public void LoggerFoldsScopeIntoCategory()
        {
            var captured = new List<LogEvent>();
            using var logger = new Logger("svc", LogLevel.Trace);
            logger.AddSink(new DelegateSink(captured.Add));

            logger.Info("no scope");
            using (ScopedContext.Push("op-1"))
                logger.Info("with scope");

            Check.Equal("svc", captured[0].Category);
            Check.Equal("svc/op-1", captured[1].Category);
        }

        public void ScopeWithEmptyCategoryLoggerUsesScopeAlone()
        {
            var captured = new List<LogEvent>();
            using var logger = new Logger(minimumLevel: LogLevel.Trace);
            logger.AddSink(new DelegateSink(captured.Add));
            using (ScopedContext.Push("only-scope"))
                logger.Info("x");
            Check.Equal("only-scope", captured[0].Category);
        }

        public async Task FlowsAcrossAwait()
        {
            using (ScopedContext.Push("flow"))
            {
                await Task.Yield();
                Check.Equal("flow", ScopedContext.Current); // AsyncLocal flows across the await
            }
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => ScopedContext.Push(null!));
        }
    }
}
