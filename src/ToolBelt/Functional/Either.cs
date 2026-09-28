// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Functional
{
    /// <summary>
    /// A value that is exactly one of two types — <typeparamref name="TLeft"/> or
    /// <typeparamref name="TRight"/>. Commonly used with the left side as an error/alternate and the
    /// right side as the primary value. Reading the wrong side throws; use <see cref="Match{T}"/> or the
    /// <c>TryGet</c> methods. The <c>default</c> value is a Left holding <c>default(TLeft)</c>.
    /// </summary>
    public readonly struct Either<TLeft, TRight>
    {
        private readonly bool _isRight;
        private readonly TLeft _left;
        private readonly TRight _right;

        private Either(bool isRight, TLeft left, TRight right)
        {
            _isRight = isRight;
            _left = left;
            _right = right;
        }

        /// <summary>Creates a Left value.</summary>
        public static Either<TLeft, TRight> FromLeft(TLeft value) => new Either<TLeft, TRight>(false, value, default!);

        /// <summary>Creates a Right value.</summary>
        public static Either<TLeft, TRight> FromRight(TRight value) => new Either<TLeft, TRight>(true, default!, value);

        /// <summary>Whether this holds the Left type.</summary>
        public bool IsLeft => !_isRight;

        /// <summary>Whether this holds the Right type.</summary>
        public bool IsRight => _isRight;

        /// <summary>The Left value; throws <see cref="InvalidOperationException"/> if this is a Right.</summary>
        public TLeft Left => _isRight
            ? throw new InvalidOperationException("Either is Right; there is no Left value.")
            : _left;

        /// <summary>The Right value; throws <see cref="InvalidOperationException"/> if this is a Left.</summary>
        public TRight Right => _isRight
            ? _right
            : throw new InvalidOperationException("Either is Left; there is no Right value.");

        /// <summary>Gets the Left value if this is a Left.</summary>
        public bool TryGetLeft(out TLeft value)
        {
            value = _isRight ? default! : _left;
            return !_isRight;
        }

        /// <summary>Gets the Right value if this is a Right.</summary>
        public bool TryGetRight(out TRight value)
        {
            value = _isRight ? _right : default!;
            return _isRight;
        }

        /// <summary>Collapses both cases into a single value.</summary>
        public T Match<T>(Func<TLeft, T> onLeft, Func<TRight, T> onRight)
        {
            if (onLeft is null) throw new ArgumentNullException(nameof(onLeft));
            if (onRight is null) throw new ArgumentNullException(nameof(onRight));
            return _isRight ? onRight(_right) : onLeft(_left);
        }

        /// <summary>Transforms the Left value if present; passes a Right through unchanged.</summary>
        public Either<TOut, TRight> MapLeft<TOut>(Func<TLeft, TOut> map)
        {
            if (map is null) throw new ArgumentNullException(nameof(map));
            return _isRight
                ? Either<TOut, TRight>.FromRight(_right)
                : Either<TOut, TRight>.FromLeft(map(_left));
        }

        /// <summary>Transforms the Right value if present; passes a Left through unchanged.</summary>
        public Either<TLeft, TOut> MapRight<TOut>(Func<TRight, TOut> map)
        {
            if (map is null) throw new ArgumentNullException(nameof(map));
            return _isRight
                ? Either<TLeft, TOut>.FromRight(map(_right))
                : Either<TLeft, TOut>.FromLeft(_left);
        }

        public override string ToString() => _isRight ? $"Right({_right})" : $"Left({_left})";
    }

    /// <summary>
    /// Factory helpers for <see cref="Either{TLeft, TRight}"/>. Named <c>FromLeft</c>/<c>FromRight</c> to
    /// match the struct's own factories (the plain names <c>Left</c>/<c>Right</c> are taken by the
    /// accessor properties).
    /// </summary>
    public static class Either
    {
        /// <summary>Creates a Left value.</summary>
        public static Either<TLeft, TRight> FromLeft<TLeft, TRight>(TLeft value) => Either<TLeft, TRight>.FromLeft(value);

        /// <summary>Creates a Right value.</summary>
        public static Either<TLeft, TRight> FromRight<TLeft, TRight>(TRight value) => Either<TLeft, TRight>.FromRight(value);
    }
}
