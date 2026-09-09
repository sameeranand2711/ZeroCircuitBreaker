# ZeroCircuitBreaker V1 Public API

## Status

This file fixes the exact intended V1 public surface. All types are in namespace `ZeroCircuitBreaker`. There are no other library-defined public types, constructors, methods, properties, fields, events, delegates, or extension methods in V1. Every declared public member receives XML documentation in production code.

```csharp
namespace ZeroCircuitBreaker;

public enum CircuitState
{
    Closed = 0,
    Open = 1,
    HalfOpen = 2
}

public enum CircuitTransitionReason
{
    FailureThresholdReached = 0,
    BreakDurationElapsed = 1,
    HalfOpenProbeSucceeded = 2,
    HalfOpenProbeFailed = 3,
    ManuallyOpened = 4,
    ManuallyReset = 5
}

public readonly struct CircuitStateChange
{
    public CircuitState PreviousState { get; }

    public CircuitState CurrentState { get; }

    public CircuitTransitionReason Reason { get; }

    internal CircuitStateChange(
        CircuitState previousState,
        CircuitState currentState,
        CircuitTransitionReason reason);
}

public readonly struct CircuitBreakerSnapshot
{
    public CircuitState State { get; }

    public int ConsecutiveFailures { get; }

    public TimeSpan? RetryAfter { get; }

    internal CircuitBreakerSnapshot(
        CircuitState state,
        int consecutiveFailures,
        TimeSpan? retryAfter);
}

public readonly struct CircuitRejection
{
    public CircuitState State { get; }

    public TimeSpan? RetryAfter { get; }

    internal CircuitRejection(CircuitState state, TimeSpan? retryAfter);
}

public sealed class CircuitBreakerOptions
{
    public int FailureThreshold { get; init; } = 5;

    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(30);

    public Func<Exception, bool>? IsFailureException { get; init; }

    public Action<CircuitStateChange>? OnStateChanged { get; init; }
}

public sealed class CircuitBreakerOpenException : Exception
{
    public CircuitState State { get; }

    public TimeSpan? RetryAfter { get; }

    internal CircuitBreakerOpenException(CircuitRejection rejection);
}

public readonly struct CircuitLease
{
    public void CompleteSuccess();

    public void CompleteFailure();

    public void CompleteFailure(Exception exception);
}

public sealed class CircuitBreaker
{
    public CircuitBreaker();

    public CircuitBreaker(CircuitBreakerOptions options);

    public CircuitBreaker(CircuitBreakerOptions options, TimeProvider timeProvider);

    public void Execute(
        Action operation,
        CancellationToken cancellationToken = default);

    public TResult Execute<TResult>(
        Func<TResult> operation,
        CancellationToken cancellationToken = default);

    public TResult Execute<TResult>(
        Func<TResult> operation,
        Func<TResult, bool> isFailureResult,
        CancellationToken cancellationToken = default);

    public void Execute<TState>(
        TState state,
        Action<TState, CancellationToken> operation,
        CancellationToken cancellationToken = default);

    public TResult Execute<TState, TResult>(
        TState state,
        Func<TState, CancellationToken, TResult> operation,
        CancellationToken cancellationToken = default);

    public TResult Execute<TState, TResult>(
        TState state,
        Func<TState, CancellationToken, TResult> operation,
        Func<TResult, bool> isFailureResult,
        CancellationToken cancellationToken = default);

    public Task ExecuteTaskAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    public Task<TResult> ExecuteTaskAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    public Task<TResult> ExecuteTaskAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        Func<TResult, bool> isFailureResult,
        CancellationToken cancellationToken = default);

    public ValueTask ExecuteAsync(
        Func<CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default);

    public ValueTask<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default);

    public ValueTask<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        Func<TResult, bool> isFailureResult,
        CancellationToken cancellationToken = default);

    public ValueTask ExecuteAsync<TState>(
        TState state,
        Func<TState, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default);

    public ValueTask<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<TState, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default);

    public ValueTask<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<TState, CancellationToken, ValueTask<TResult>> operation,
        Func<TResult, bool> isFailureResult,
        CancellationToken cancellationToken = default);

    public bool TryAcquire(out CircuitLease lease);

    public bool TryAcquire(
        out CircuitLease lease,
        out CircuitRejection rejection);

    public void Open();

    public void Reset();

    public CircuitBreakerSnapshot GetSnapshot();
}
```

## Construction and validation

The parameterless constructor uses a new default `CircuitBreakerOptions` and `TimeProvider.System`. The options-only constructor uses the supplied options and `TimeProvider.System`. The third constructor is the injection seam for deterministic time.

Constructors throw:

- `ArgumentNullException` for null `options` or an explicitly supplied null `timeProvider`;
- `ArgumentOutOfRangeException` when `FailureThreshold` is less than 1;
- `ArgumentOutOfRangeException` when `BreakDuration` is not greater than `TimeSpan.Zero`.

Configuration is snapshotted during construction. Properties on the original options object are not live configuration.

## Execute families

The six synchronous overloads form three matched shapes: void, result, and result with per-call failure predicate. Each shape has a convenient overload and a state-passing overload.

Async APIs deliberately use two method names:

- `ExecuteAsync` is the ValueTask-native family. It has six overloads: void/result/result-with-predicate, each in convenient and state-passing form.
- `ExecuteTaskAsync` is the three-overload Task convenience family: void, result, and result-with-predicate.

Using separate names is intentional. If both `Func<CancellationToken, Task>` and `Func<CancellationToken, ValueTask>` were overloads of `ExecuteAsync`, a target-typed `async` lambda could be convertible to both and produce ambiguous or fragile overload resolution. V1 does not require casts or delegate-type annotations for ordinary async lambdas: callers choose Task or ValueTask semantics by method name.

The state-passing overloads pass caller state and the call cancellation token into the operation, allowing consumers to use `static` delegates without a closure. They use BCL delegate types; V1 adds no public delegate abstraction. The result predicate remains `Func<TResult, bool>` and can independently be static.

`ExecuteAsync` accepts and returns `ValueTask`/`ValueTask<TResult>` directly. `ExecuteTaskAsync` accepts and returns `Task`/`Task<TResult>` for callers whose dependency APIs are Task-based. The Task facade delegates to the same ValueTask-native admission, classification, and completion core by wrapping the operation's returned Task in a ValueTask and converting the overall core result with `AsTask()`. It never blocks and does not use `.Result`, `.Wait()`, or synchronous continuations for accounting.

The Task facade's returned Task represents the entire breaker operation, including outcome classification and circuit accounting; it is not required to be reference-equal to the protected delegate's Task. Rejection, protected-operation exceptions, and cancellation are represented by the returned Task according to normal Task semantics. Argument validation is performed before invoking protected work, consistently with the ValueTask family.

A token is passed to every async operation and every state-passing sync operation. On the convenient sync overloads, the token exists for caller-cancellation classification; a caller that needs it inside the operation may use its normal surrounding state or choose the state-passing overload.

Every delegate argument is required and null causes `ArgumentNullException` before admission. No protected delegate is invoked on rejection. Throwing APIs reject Open and HalfOpen admission with `CircuitBreakerOpenException`.

A supplied `isFailureResult` is evaluated once after a result is produced. True records a failure; false records success. The result itself is returned in either case. Without the predicate, every normally returned result is a success.

Exceptions from protected work are always rethrown unchanged after circuit accounting. Caller-requested cancellation cannot count as failure. `IsFailureException`, when non-null, replaces the default classification for other exceptions; false applies success semantics and true applies failure semantics. These rules are identical for synchronous, Task, ValueTask, and lease completion paths. V1 gives predicate delegates no callback-style exception-isolation guarantee; applications should not throw from classification predicates.

## Non-throwing acquisition

`TryAcquire(out CircuitLease lease)` returns true with an acquired lease or false with `default(CircuitLease)`. It does not throw for circuit rejection.

The two-out overload additionally returns:

- `default(CircuitRejection)` on success; callers must use the boolean to discriminate;
- `CircuitRejection(CircuitState.Open, remainingDuration)` for Open rejection, with a non-null value clamped to zero or greater;
- `CircuitRejection(CircuitState.HalfOpen, null)` while a probe is owned.

Expiry may make the acquiring caller the sole HalfOpen probe. No fairness or order is promised.

## Lease contract

`CircuitLease` is a readonly value type. An acquired lease must be completed exactly once with one of:

- `CompleteSuccess()` for a successful outcome;
- `CompleteFailure()` for an unconditional dependency failure;
- `CompleteFailure(Exception)` to apply cancellation and the configured exception classifier.

The first completion claims a shared internal gate, so copies of one lease remain duplicate-safe. A duplicate completion or completion of the default value throws `InvalidOperationException` before breaker mutation. A first completion that has become stale because the breaker crossed a generation boundary returns normally as a no-op.

`CompleteFailure(Exception)` throws `ArgumentNullException` for null. If its exception is an `OperationCanceledException` carrying a requested token, it applies success semantics; otherwise the configured/default exception rule applies. The method performs circuit accounting only and does not throw the supplied exception on the caller's behalf.

## Rejection exception

`CircuitBreakerOpenException` is created only by the throwing execution APIs; callers do not construct it. `State` is Open or HalfOpen. `RetryAfter` is non-null for Open and null for HalfOpen. Its message identifies the rejecting state and, when known, retry duration. Rejection never invokes protected work.

## State changes, callbacks, and snapshots

`Open()` and `Reset()` are unconditional logical boundaries. Each increments the generation, resets consecutive failures, and emits a callback even for Open-to-Open or Closed-to-Closed. Open restarts the break duration.

`CircuitStateChange` describes the completed boundary. The optional `OnStateChanged` callback is synchronous, runs after publication and outside transition ownership, permits reentrant breaker calls, and has its exceptions suppressed.

`GetSnapshot()` returns a stable diagnostic view. `RetryAfter` is non-null only for Open and may be zero when the duration has elapsed but no caller has attempted admission. Snapshot retrieval never changes state and its result is not valid for admission decisions.
