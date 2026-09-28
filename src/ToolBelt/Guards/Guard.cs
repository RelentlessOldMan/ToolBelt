// ToolBelt drop-in — fully self-contained (BCL only). Shared primitive: copy this file to use Guard elsewhere.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ToolBelt.Guards
{
    /// <summary>
    /// Lightweight argument-validation helpers. This is the one blessed shared primitive of ToolBelt:
    /// other drop-in files may take a dependency on <see cref="Guard"/>, and their first line says so.
    /// </summary>
    /// <remarks>
    /// The reference-type null check (<c>NotNull&lt;T&gt;</c> where <c>T : class</c>) carries a class
    /// constraint, so a generic method with a non-nullable value-type key cannot route its null check
    /// through here — it
    /// must throw inline instead. This is a deliberate, documented sharp edge.
    /// </remarks>
    public static class Guard
    {
        /// <summary>Throws <see cref="ArgumentNullException"/> if <paramref name="value"/> is null; otherwise returns it.</summary>
        public static T NotNull<T>(T? value, [CallerArgumentExpression("value")] string? paramName = null)
            where T : class
        {
            if (value is null)
                throw new ArgumentNullException(paramName);
            return value;
        }

        /// <summary>Throws if a nullable value type is null; otherwise returns the unwrapped value.</summary>
        public static T NotNull<T>(T? value, [CallerArgumentExpression("value")] string? paramName = null)
            where T : struct
        {
            if (value is null)
                throw new ArgumentNullException(paramName);
            return value.Value;
        }

        /// <summary>Throws if the string is null or empty; otherwise returns it.</summary>
        public static string NotNullOrEmpty(string? value, [CallerArgumentExpression("value")] string? paramName = null)
        {
            if (value is null)
                throw new ArgumentNullException(paramName);
            if (value.Length == 0)
                throw new ArgumentException("Value must not be empty.", paramName);
            return value;
        }

        /// <summary>Throws if the string is null, empty, or all-whitespace; otherwise returns it.</summary>
        public static string NotNullOrWhiteSpace(string? value, [CallerArgumentExpression("value")] string? paramName = null)
        {
            if (value is null)
                throw new ArgumentNullException(paramName);
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value must not be empty or whitespace.", paramName);
            return value;
        }

        /// <summary>Throws if the collection is null or contains no elements; otherwise returns it.</summary>
        public static IReadOnlyCollection<T> NotNullOrEmpty<T>(
            IReadOnlyCollection<T>? value,
            [CallerArgumentExpression("value")] string? paramName = null)
        {
            if (value is null)
                throw new ArgumentNullException(paramName);
            if (value.Count == 0)
                throw new ArgumentException("Collection must not be empty.", paramName);
            return value;
        }

        /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> unless <paramref name="value"/> is &gt; 0.</summary>
        public static int Positive(int value, [CallerArgumentExpression("value")] string? paramName = null)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(paramName, value, "Value must be positive.");
            return value;
        }

        /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> unless <paramref name="value"/> is &gt; 0.</summary>
        public static long Positive(long value, [CallerArgumentExpression("value")] string? paramName = null)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(paramName, value, "Value must be positive.");
            return value;
        }

        /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is &lt; 0.</summary>
        public static int NonNegative(int value, [CallerArgumentExpression("value")] string? paramName = null)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(paramName, value, "Value must not be negative.");
            return value;
        }

        /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is &lt; 0.</summary>
        public static long NonNegative(long value, [CallerArgumentExpression("value")] string? paramName = null)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(paramName, value, "Value must not be negative.");
            return value;
        }

        /// <summary>
        /// Throws <see cref="ArgumentOutOfRangeException"/> unless
        /// <paramref name="min"/> &lt;= <paramref name="value"/> &lt;= <paramref name="max"/> (inclusive).
        /// </summary>
        public static int InRange(int value, int min, int max, [CallerArgumentExpression("value")] string? paramName = null)
        {
            if (value < min || value > max)
                throw new ArgumentOutOfRangeException(paramName, value, $"Value must be in [{min}, {max}].");
            return value;
        }

        /// <summary>
        /// Throws <see cref="ArgumentOutOfRangeException"/> unless <paramref name="index"/> is a valid index
        /// into a sequence of length <paramref name="count"/> (i.e. 0 &lt;= index &lt; count).
        /// </summary>
        public static int Index(int index, int count, [CallerArgumentExpression("index")] string? paramName = null)
        {
            if ((uint)index >= (uint)count)
                throw new ArgumentOutOfRangeException(paramName, index, $"Index must be in [0, {count}).");
            return index;
        }
    }
}

#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    // Polyfill so [CallerArgumentExpression] resolves on netstandard2.0. The modern C# compiler honors the
    // attribute by name; the type just has to exist. Internal so it never leaks from the assembly.
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute : Attribute
    {
        public CallerArgumentExpressionAttribute(string parameterName) => ParameterName = parameterName;
        public string ParameterName { get; }
    }
}
#endif
