// ToolBelt test infrastructure — self-contained (BCL only). Not part of the shipping library.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ToolBelt.Tests.Framework
{
    /// <summary>Thrown by <see cref="Check"/> when an assertion fails.</summary>
    public sealed class CheckFailedException : Exception
    {
        public CheckFailedException(string message) : base(message) { }
    }

    /// <summary>
    /// Minimal assertion helpers. Kept intentionally small — the house style is differential testing
    /// (grade against a naive reference over randomized trials), so these are mostly the final compare
    /// and the property/edge-case assertions, not a sprawling fluent API.
    /// </summary>
    public static class Check
    {
        public static void True(bool condition, string? message = null)
        {
            if (!condition)
                throw new CheckFailedException(message ?? "Expected condition to be true.");
        }

        public static void False(bool condition, string? message = null)
            => True(!condition, message ?? "Expected condition to be false.");

        public static void Equal<T>(T expected, T actual, string? message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new CheckFailedException(
                    (message is null ? "" : message + " — ") + $"expected <{Fmt(expected)}>, got <{Fmt(actual)}>.");
        }

        /// <summary>Compares two doubles within an absolute tolerance.</summary>
        public static void Close(double expected, double actual, double tolerance = 1e-9, string? message = null)
        {
            if (double.IsNaN(expected) && double.IsNaN(actual))
                return;
            if (Math.Abs(expected - actual) > tolerance)
                throw new CheckFailedException(
                    (message is null ? "" : message + " — ")
                    + $"expected {expected:R} ± {tolerance:R}, got {actual:R} (Δ={Math.Abs(expected - actual):R}).");
        }

        public static void Null(object? value, string? message = null)
        {
            if (value is not null)
                throw new CheckFailedException(message ?? $"Expected null, got <{Fmt(value)}>.");
        }

        public static void NotNull(object? value, string? message = null)
        {
            if (value is null)
                throw new CheckFailedException(message ?? "Expected non-null value.");
        }

        /// <summary>Asserts that <paramref name="action"/> throws an exception of type <typeparamref name="TException"/>.</summary>
        public static TException Throws<TException>(Action action, string? message = null)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException ex)
            {
                return ex;
            }
            catch (Exception other)
            {
                throw new CheckFailedException(
                    (message is null ? "" : message + " — ")
                    + $"expected {typeof(TException).Name}, but got {other.GetType().Name}: {other.Message}");
            }
            throw new CheckFailedException(
                (message is null ? "" : message + " — ") + $"expected {typeof(TException).Name}, but nothing was thrown.");
        }

        /// <summary>Async counterpart to <see cref="Throws{TException}"/>.</summary>
        public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string? message = null)
            where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException ex)
            {
                return ex;
            }
            catch (Exception other)
            {
                throw new CheckFailedException(
                    (message is null ? "" : message + " — ")
                    + $"expected {typeof(TException).Name}, but got {other.GetType().Name}: {other.Message}");
            }
            throw new CheckFailedException(
                (message is null ? "" : message + " — ") + $"expected {typeof(TException).Name}, but nothing was thrown.");
        }

        private static string Fmt(object? value) => value?.ToString() ?? "null";
    }
}
