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
        /// <summary>A single trial call is allowed (others are short-circuited while it runs); success closes the circuit, failure reopens it.</summary>
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
    /// the failure count; a failure while half-open reopens it and restarts the timer. While the trial runs,
    /// other calls are short-circuited. A call that started before the circuit opened and finishes later
    /// doesn't change the state, even after the circuit has closed again, and a cancellation the caller requested
    /// isn't counted as a failure. Time is read through an injectable clock so tests are deterministic; if that clock
    /// steps back while the circuit is open, the reset timeout restarts from the new time rather than waiting out
    /// the jump. Not thread-safe.
    /// </summary>
    public sealed class CircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly TimeSpan _resetTimeout;
        private readonly Func<DateTimeOffset> _clock;

        private CircuitState _state = CircuitState.Closed;
        private int _consecutiveFailures;
        private DateTimeOffset _openedAt;
        private bool _trialInFlight;
        private int _generation; // bumped each time the circuit opens; a call counts only in the closed period it started in

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
            bool trial = _state == CircuitState.HalfOpen;
            int generation = _generation;
            if (trial)
            {
                if (_trialInFlight) throw new CircuitOpenException();
                _trialInFlight = true;
            }

            try
            {
                T result = await operation(cancellationToken).ConfigureAwait(false);
                OnSuccess(trial, generation);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // the caller gave up; that says nothing about the dependency
            }
            catch (Exception)
            {
                OnFailure(_clock(), trial, generation);
                throw;
            }
            finally
            {
                if (trial) _trialInFlight = false;
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
            if (_state != CircuitState.Open) return;
            if (now < _openedAt) _openedAt = now; // the clock stepped back: restart the timeout, don't wait out the jump
            if (now - _openedAt >= _resetTimeout)
                _state = CircuitState.HalfOpen;
        }

        // Only the trial, or a call that started in the current closed period, decides the state: a stale call that
        // started before the circuit opened must not close it, restart the timer, or reopen it once it has re-closed.
        private bool Stale(bool trial, int generation) => !trial && (_state != CircuitState.Closed || generation != _generation);

        private void OnSuccess(bool trial, int generation)
        {
            if (Stale(trial, generation)) return;
            _consecutiveFailures = 0;
            _state = CircuitState.Closed;
        }

        private void OnFailure(DateTimeOffset now, bool trial, int generation)
        {
            if (Stale(trial, generation)) return;
            _consecutiveFailures++;
            // A half-open trial that fails reopens immediately; otherwise open once the threshold is hit.
            if (trial || _consecutiveFailures >= _failureThreshold)
            {
                _state = CircuitState.Open;
                _openedAt = now;
                _generation++;
            }
        }
    }
}
