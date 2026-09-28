// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Functional
{
    /// <summary>
    /// The outcome of an operation that either produces a value or fails with an error message. A
    /// lightweight alternative to throwing for expected failures. Reading <see cref="Value"/> on a
    /// failure (or <see cref="Error"/> on a success) throws — branch on <see cref="IsSuccess"/>, use
    /// <see cref="TryGetValue"/>, or <see cref="Match{TOut}"/> instead.
    /// </summary>
    public readonly struct Result<T>
    {
        private readonly bool _ok;
        private readonly T _value;
        private readonly string? _error;

        private Result(bool ok, T value, string? error)
        {
            _ok = ok;
            _value = value;
            _error = error;
        }

        /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
        public static Result<T> Success(T value) => new Result<T>(true, value, null);

        /// <summary>Creates a failed result carrying an error message.</summary>
        public static Result<T> Failure(string error)
        {
            if (error is null)
                throw new ArgumentNullException(nameof(error));
            return new Result<T>(false, default!, error);
        }

        /// <summary>Whether the operation succeeded.</summary>
        public bool IsSuccess => _ok;

        /// <summary>Whether the operation failed.</summary>
        public bool IsFailure => !_ok;

        /// <summary>The value on success; throws <see cref="InvalidOperationException"/> on failure.</summary>
        public T Value => _ok
            ? _value
            : throw new InvalidOperationException($"Result is a failure: {_error ?? "unspecified error"}.");

        /// <summary>The error message on failure; throws <see cref="InvalidOperationException"/> on success.</summary>
        public string Error => _ok
            ? throw new InvalidOperationException("Result is a success; there is no error.")
            : _error ?? "Unspecified error.";

        /// <summary>Gets the value if this is a success.</summary>
        public bool TryGetValue(out T value)
        {
            value = _ok ? _value : default!;
            return _ok;
        }

        /// <summary>The value if successful, otherwise <paramref name="fallback"/>.</summary>
        public T GetValueOrDefault(T fallback = default!) => _ok ? _value : fallback;

        /// <summary>Transforms the value if successful; propagates the error otherwise.</summary>
        public Result<TOut> Map<TOut>(Func<T, TOut> map)
        {
            if (map is null) throw new ArgumentNullException(nameof(map));
            return _ok ? Result<TOut>.Success(map(_value)) : Result<TOut>.Failure(Error);
        }

        /// <summary>Chains another result-producing operation if successful; propagates the error otherwise.</summary>
        public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind)
        {
            if (bind is null) throw new ArgumentNullException(nameof(bind));
            return _ok ? bind(_value) : Result<TOut>.Failure(Error);
        }

        /// <summary>Collapses both cases into a single value.</summary>
        public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, TOut> onFailure)
        {
            if (onSuccess is null) throw new ArgumentNullException(nameof(onSuccess));
            if (onFailure is null) throw new ArgumentNullException(nameof(onFailure));
            return _ok ? onSuccess(_value) : onFailure(Error);
        }

        public override string ToString() => _ok ? $"Success({_value})" : $"Failure({_error})";
    }

    /// <summary>Factory helpers for <see cref="Result{T}"/> with type inference.</summary>
    public static class Result
    {
        /// <summary>Creates a successful result (type inferred from <paramref name="value"/>).</summary>
        public static Result<T> Success<T>(T value) => Result<T>.Success(value);

        /// <summary>Creates a failed result of <typeparamref name="T"/> with an error message.</summary>
        public static Result<T> Failure<T>(string error) => Result<T>.Failure(error);

        /// <summary>Runs <paramref name="func"/>, capturing any thrown exception's message as a failure.</summary>
        public static Result<T> Try<T>(Func<T> func)
        {
            if (func is null) throw new ArgumentNullException(nameof(func));
            try
            {
                return Result<T>.Success(func());
            }
            catch (Exception ex)
            {
                return Result<T>.Failure(ex.Message);
            }
        }
    }
}
