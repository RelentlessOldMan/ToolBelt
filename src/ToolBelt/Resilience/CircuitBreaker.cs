// ToolBelt drop-in — self-contained except for Guards/Guard.cs (copy that file too).
using System;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Guards;

namespace ToolBelt.Resilience
{
    /// <summary>The state of a <see cref="CircuitBreaker"/>.</summary>
    public enum CircuitState
    {
        /// <summary>Calls flow through; failures are counted.</summary>
        Closed,
        /// <summary>Calls fail fast without invoking the operation, until the reset timeout elapses.</summary>
        Open,
        /// <summary>A single trial call is allowed; success closes the circuit, failure reopens it.</summary>
        HalfOpen,
    }

    /// <summary>Thrown by <see cref="CircuitBreaker"/> when the circuit is open and a call is short-circuited.</summary>
    public sealed class CircuitOpenException : Exception
    {
        public CircuitOpenException() : base("The circuit is open; the call was short-circuited.") { }
    }

    /// <summary>
    /// A circuit breaker: after <c>failureThreshold</c> consecutive failures it "opens" and fails fast
    /// for <c>resetTimeout</c>, then allows one trial call ("half-open"). A success closes it and resets
    /// the failure count; a failure while half-open reopens it and restarts the timer. Time is read
    /// through an injectable clock so tests are deterministic. Not thread-safe.
    /// </summary>
    public sealed class CircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly TimeSpan _resetTimeout;
        private readonly Func<DateTimeOffset> _clock;

        private CircuitState _state = CircuitState.Closed;
        private int _consecutiveFailures;
        private DateTimeOffset _openedAt;

        public CircuitBreaker(int failureThreshold, TimeSpan resetTimeout, Func<DateTimeOffset>? clock = null)
        {
            _failureThreshold = Guard.Positive(failureThreshold);
            if (resetTimeout < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(resetTimeout), resetTimeout, "Reset timeout must not be negative.");
            _resetTimeout = resetTimeout;
            _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        }

        /// <summary>The current state, accounting for any elapsed reset timeout.</summary>
        public CircuitState State
        {
            get
            {
                ApplyElapsedTimeout(_clock());
                return _state;
            }
        }

        public int ConsecutiveFailures => _consecutiveFailures;

        /// <summary>Runs <paramref name="operation"/> under the breaker, returning its result.</summary>
        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            Guard.NotNull(operation);

            var now = _clock();
            ApplyElapsedTimeout(now);

            if (_state == CircuitState.Open)
                throw new CircuitOpenException();

            try
            {
                T result = await operation(cancellationToken).ConfigureAwait(false);
                OnSuccess();
                return result;
            }
            catch (Exception)
            {
                OnFailure(_clock());
                throw;
            }
        }

        /// <summary>Runs an operation with no return value under the breaker.</summary>
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            Guard.NotNull(operation);
            return ExecuteAsync(async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        private void ApplyElapsedTimeout(DateTimeOffset now)
        {
            if (_state == CircuitState.Open && now - _openedAt >= _resetTimeout)
                _state = CircuitState.HalfOpen;
        }

        private void OnSuccess()
        {
            _consecutiveFailures = 0;
            _state = CircuitState.Closed;
        }

        private void OnFailure(DateTimeOffset now)
        {
            _consecutiveFailures++;
            // A half-open trial that fails reopens immediately; otherwise open once the threshold is hit.
            if (_state == CircuitState.HalfOpen || _consecutiveFailures >= _failureThreshold)
            {
                _state = CircuitState.Open;
                _openedAt = now;
            }
        }
    }
}
