// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace ToolBelt.Diagnostics
{
    /// <summary>
    /// Conditionally compiled <em>internal consistency</em> checks: "this can only be false if my own code has a
    /// bug". Deliberately distinct from <c>ToolBelt.Guards.Guard</c>, which validates arguments at a public
    /// boundary and stays on in every build — a caller passing bad input is not a bug in your code, and an
    /// invariant failing is never the caller's fault. Keep that distinction: don't use these for argument checks.
    /// <para>
    /// The checks are compiled only when the <b>calling</b> code is built with <c>DEBUG</c> or
    /// <c>TOOLBELT_INVARIANTS</c> defined (<see cref="ConditionalAttribute"/> is honoured at the call site). In
    /// any other build the call — including evaluation of its arguments — is removed entirely, so an expensive
    /// check costs nothing in production. Define <c>TOOLBELT_INVARIANTS</c> to keep them in an optimized build.
    /// A failure throws <see cref="InvariantViolationException"/> naming the source file, line and member, rather
    /// than <c>Debug.Assert</c>'s dialog or process termination, so test runners report it like any failure.
    /// </para>
    /// </summary>
    public static class AssertInvariant
    {
        /// <summary>Throws <see cref="InvariantViolationException"/> if <paramref name="condition"/> is false.</summary>
        [Conditional("DEBUG"), Conditional("TOOLBELT_INVARIANTS")]
        public static void That(
            bool condition,
            string message,
            [CallerMemberName] string member = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            if (!condition)
                throw new InvariantViolationException(message, member, filePath, lineNumber);
        }

        /// <summary>Throws <see cref="InvariantViolationException"/> if <paramref name="value"/> is null.</summary>
        [Conditional("DEBUG"), Conditional("TOOLBELT_INVARIANTS")]
        public static void NotNull(
            object? value,
            string message,
            [CallerMemberName] string member = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            if (value is null)
                throw new InvariantViolationException(message, member, filePath, lineNumber);
        }

        /// <summary>
        /// Builds (does not throw) the exception for a code path that must be impossible — write
        /// <c>throw AssertInvariant.Unreachable("...")</c>, e.g. in a <c>switch</c> default. Unlike the checks above
        /// this is <b>not</b> compiled out: control flow needs the <c>throw</c> in every build.
        /// </summary>
        public static InvariantViolationException Unreachable(
            string message,
            [CallerMemberName] string member = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
            => new InvariantViolationException("Unreachable code reached: " + message, member, filePath, lineNumber);
    }

    /// <summary>
    /// Thrown when an internal invariant does not hold — a bug in the calling code, never bad input. It derives
    /// directly from <see cref="Exception"/> (not <see cref="ArgumentException"/> or
    /// <see cref="InvalidOperationException"/>) so handlers written for caller errors don't swallow it.
    /// </summary>
    public sealed class InvariantViolationException : Exception
    {
        public InvariantViolationException(string message, string member, string filePath, int lineNumber)
            : base(Format(message, member, filePath, lineNumber))
        {
            InvariantMessage = message;
            Member = member;
            FilePath = filePath;
            LineNumber = lineNumber;
        }

        /// <summary>The message passed to the check, without the location prefix.</summary>
        public string InvariantMessage { get; }

        /// <summary>The calling member's name.</summary>
        public string Member { get; }

        /// <summary>The calling source file path, as captured at compile time.</summary>
        public string FilePath { get; }

        /// <summary>The calling source line.</summary>
        public int LineNumber { get; }

        private static string Format(string message, string member, string filePath, int lineNumber)
        {
            string file = string.IsNullOrEmpty(filePath) ? "?" : Path.GetFileName(filePath);
            return "Invariant violated at " + file + ":" + lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " in " + (string.IsNullOrEmpty(member) ? "?" : member) + ": " + message;
        }
    }
}
