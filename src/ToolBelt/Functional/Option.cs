// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Functional
{
    /// <summary>
    /// An optional value: either <c>Some(value)</c> or <c>None</c>. A type-safe alternative to null that
    /// forces the empty case to be handled. Reading <see cref="Value"/> on <c>None</c> throws — use
    /// <see cref="TryGetValue"/>, <see cref="GetValueOrDefault"/>, or <see cref="Match{TOut}"/>. The
    /// <c>default</c> value of the struct is <c>None</c>.
    /// </summary>
    public readonly struct Option<T>
    {
        private readonly bool _hasValue;
        private readonly T _value;

        private Option(bool hasValue, T value)
        {
            _hasValue = hasValue;
            _value = value;
        }

        /// <summary>Creates an option containing <paramref name="value"/>.</summary>
        public static Option<T> Some(T value) => new Option<T>(true, value);

        /// <summary>The empty option.</summary>
        public static Option<T> None => default;

        /// <summary>Whether a value is present.</summary>
        public bool HasValue => _hasValue;

        /// <summary>Whether the option is empty.</summary>
        public bool IsNone => !_hasValue;

        /// <summary>The value if present; throws <see cref="InvalidOperationException"/> if None.</summary>
        public T Value => _hasValue
            ? _value
            : throw new InvalidOperationException("Option is None; there is no value.");

        /// <summary>Gets the value if present.</summary>
        public bool TryGetValue(out T value)
        {
            value = _hasValue ? _value : default!;
            return _hasValue;
        }

        /// <summary>The value if present, otherwise <paramref name="fallback"/>.</summary>
        public T GetValueOrDefault(T fallback = default!) => _hasValue ? _value : fallback;

        /// <summary>Transforms the value if present; stays None otherwise.</summary>
        public Option<TOut> Map<TOut>(Func<T, TOut> map)
        {
            if (map is null) throw new ArgumentNullException(nameof(map));
            return _hasValue ? Option<TOut>.Some(map(_value)) : Option<TOut>.None;
        }

        /// <summary>Chains another option-producing operation if present; stays None otherwise.</summary>
        public Option<TOut> Bind<TOut>(Func<T, Option<TOut>> bind)
        {
            if (bind is null) throw new ArgumentNullException(nameof(bind));
            return _hasValue ? bind(_value) : Option<TOut>.None;
        }

        /// <summary>Keeps the value only if it satisfies <paramref name="predicate"/>; otherwise None.</summary>
        public Option<T> Where(Func<T, bool> predicate)
        {
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));
            return _hasValue && predicate(_value) ? this : None;
        }

        /// <summary>Collapses both cases into a single value.</summary>
        public TOut Match<TOut>(Func<T, TOut> onSome, Func<TOut> onNone)
        {
            if (onSome is null) throw new ArgumentNullException(nameof(onSome));
            if (onNone is null) throw new ArgumentNullException(nameof(onNone));
            return _hasValue ? onSome(_value) : onNone();
        }

        public override string ToString() => _hasValue ? $"Some({_value})" : "None";
    }

    /// <summary>Factory helpers for <see cref="Option{T}"/> with type inference.</summary>
    public static class Option
    {
        /// <summary>Creates a Some (type inferred from <paramref name="value"/>).</summary>
        public static Option<T> Some<T>(T value) => Option<T>.Some(value);

        /// <summary>The empty option of <typeparamref name="T"/>.</summary>
        public static Option<T> None<T>() => Option<T>.None;

        /// <summary>None if the reference is null, otherwise Some.</summary>
        public static Option<T> FromNullable<T>(T? value) where T : class
            => value is null ? Option<T>.None : Option<T>.Some(value);

        /// <summary>None if the nullable has no value, otherwise Some.</summary>
        public static Option<T> FromNullable<T>(T? value) where T : struct
            => value.HasValue ? Option<T>.Some(value.Value) : Option<T>.None;
    }
}
