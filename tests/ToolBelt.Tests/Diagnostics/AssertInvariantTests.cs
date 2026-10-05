using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using ToolBelt.Diagnostics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Diagnostics
{
    public sealed class AssertInvariantTests
    {
        public void That_True_DoesNotThrow()
        {
            AssertInvariant.That(1 + 1 == 2, "arithmetic");
            AssertInvariant.NotNull(new object(), "present");
        }

        public void That_False_ThrowsWithCallerLocation()
        {
            var ex = Check.Throws<InvariantViolationException>(() => AssertInvariant.That(false, "count went negative"));
            Check.Equal("count went negative", ex.InvariantMessage);
            Check.True(ex.FilePath.EndsWith("AssertInvariantTests.cs", StringComparison.OrdinalIgnoreCase), ex.FilePath);
            Check.True(ex.LineNumber > 0, "line captured");
            Check.True(ex.Message.StartsWith("Invariant violated at AssertInvariantTests.cs:", StringComparison.Ordinal), ex.Message);
            Check.True(ex.Message.EndsWith(": count went negative", StringComparison.Ordinal), ex.Message);
        }

        public void That_CapturesCallingMember()
        {
            var ex = Check.Throws<InvariantViolationException>(() => Helper());
            Check.Equal(nameof(Helper), ex.Member);
        }

        public void NotNull_Null_Throws()
        {
            var ex = Check.Throws<InvariantViolationException>(() => AssertInvariant.NotNull(null, "cache entry missing"));
            Check.Equal("cache entry missing", ex.InvariantMessage);
        }

        public void Unreachable_BuildsButDoesNotThrow()
        {
            InvariantViolationException built = AssertInvariant.Unreachable("unknown state 7");
            Check.True(built.Message.Contains("Unreachable code reached: unknown state 7"), built.Message);
            Check.Throws<InvariantViolationException>(() => Classify(7));
            Check.Equal("one", Classify(1));
        }

        public void Violation_IsNotACallerErrorType()
        {
            // Handlers for bad input (ArgumentException / InvalidOperationException) must not swallow bugs,
            // so the type derives straight from Exception.
            Check.Equal(typeof(Exception), typeof(InvariantViolationException).BaseType);
            Check.False(typeof(ArgumentException).IsAssignableFrom(typeof(InvariantViolationException)));
            Check.False(typeof(InvalidOperationException).IsAssignableFrom(typeof(InvariantViolationException)));
        }

        public void Checks_AreConditionallyCompiled_UnreachableIsNot()
        {
            string[] SymbolsOf(string name) => typeof(AssertInvariant).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!
                .GetCustomAttributes<ConditionalAttribute>().Select(a => a.ConditionString).OrderBy(s => s, StringComparer.Ordinal).ToArray();

            Check.Equal("DEBUG,TOOLBELT_INVARIANTS", string.Join(",", SymbolsOf(nameof(AssertInvariant.That))));
            Check.Equal("DEBUG,TOOLBELT_INVARIANTS", string.Join(",", SymbolsOf(nameof(AssertInvariant.NotNull))));
            Check.Equal(0, SymbolsOf(nameof(AssertInvariant.Unreachable)).Length);
        }

        private static void Helper() => AssertInvariant.That(false, "from helper");

        private static string Classify(int kind) => kind switch
        {
            1 => "one",
            _ => throw AssertInvariant.Unreachable("unknown kind " + kind),
        };
    }
}
