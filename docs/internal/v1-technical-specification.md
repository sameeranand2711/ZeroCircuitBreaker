# ZeroCircuitBreaker V1 Technical Specification

## Purpose
ZeroCircuitBreaker is a lightweight, thread-safe .NET circuit breaker focused exclusively on circuit breaking.

Core goals:
- BCL-only / zero third-party runtime dependencies
- `net8.0`
- lock-free atomic state transitions
- low overhead on the healthy path
- generation-aware stale-operation protection
- single half-open probe
- sync + async APIs
- ValueTask-native async path
- low-level non-throwing lease API
- deterministic concurrency behavior
- no background workers/timers
- no retries, fallback, bulkhead, rate limiting, sliding windows, DI integration, logging framework integration, OpenTelemetry, or dynamic config in V1

## Public Types
- `CircuitBreaker`
- `CircuitBreakerOptions`
- `CircuitLease`
- `CircuitState`
- `CircuitStateChange`
- `CircuitTransitionReason`
- `CircuitBreakerSnapshot`
- `CircuitRejection`
- `CircuitBreakerOpenException`

## States
`Closed`, `Open`, `HalfOpen`.

### Closed
- Operations allowed.
- Handled failures increment consecutive failure count.
- Success resets consecutive failures.
- Reaching threshold transitions to Open.

### Open
- Operations rejected immediately.
- No protected work invoked.
- After `BreakDuration`, next caller may atomically transition Open -> HalfOpen.

### HalfOpen
- Exactly one probe owns the state.
- Other callers are rejected.
- Probe success -> Closed.
- Probe failure -> Open.

## Defaults
- `FailureThreshold = 5`
- `BreakDuration = 30 seconds`

## Failure Classification
- Caller-requested cancellation does not count as dependency failure.
- Other exceptions count as failures by default.
- Optional `IsFailureException` replaces default non-cancellation exception classification.
- Result-based failures are supported per call through `isFailureResult`.

## Concurrency Model
Preferred internal state:
- packed state + generation in one `long`
- separate failure count
- separate open timestamp
- CAS for meaningful transitions
- generation increments at all logical state boundaries
- stale completions cannot mutate newer generations
- no fairness guarantee
- no execution-order guarantee
- no `lock`, `SemaphoreSlim`, `Monitor`, or equivalent on the hot path

## Manual Control
`Open()`:
- any state -> Open
- failure count reset
- break duration restarted
- generation incremented

`Reset()`:
- any state -> Closed
- failure count reset
- generation incremented

Repeated Open/Reset also create a new generation.

## Time
Use BCL `TimeProvider`, defaulting to `TimeProvider.System`.
No timers or background tasks.

## API Levels
1. Convenient `Execute` / `ExecuteAsync`
2. Allocation-conscious state-passing overloads using static delegates
3. Maximum-control `TryAcquire(out CircuitLease)` and rejection overload

## Lease
- value type
- completed with `CompleteSuccess()`, `CompleteFailure()`, or `CompleteFailure(Exception)`
- contract: complete once
- stale/duplicate completions must not corrupt current state

## Rejection
Throwing APIs use `CircuitBreakerOpenException`.
Non-throwing path returns `CircuitRejection`.
Open exposes known `RetryAfter`; HalfOpen uses null RetryAfter.

## Callbacks
- optional synchronous `OnStateChanged`
- run only after transition completes
- run outside synchronization
- reentrancy allowed
- callback exceptions isolated from breaker correctness

## Diagnostics
`GetSnapshot()` returns state, consecutive failures, retry-after.
Snapshot is diagnostic only and must not be used for admission decisions.

## V1 Non-Goals
- retries
- timeout policies
- fallback
- hedging
- bulkheads
- rate limiting
- sliding windows
- percentage thresholds
- distributed/persisted state
- multiple half-open probes
- adaptive break durations
- built-in logging
- OpenTelemetry
- DI package
- registries/managers/factories
- background workers
- dynamic configuration reload

## Testing Requirements
- deterministic state transition tests
- deterministic race tests using Barrier / ManualResetEventSlim / TaskCompletionSource / similar
- stale generation tests
- lease misuse tests
- callback reentrancy tests
- cancellation classification tests
- result failure tests
- stress tests
- Release build validation

## Benchmark Requirements
BenchmarkDotNet:
- direct baseline
- Closed success
- Closed failure
- threshold transition
- Open throwing rejection
- Open TryAcquire rejection
- HalfOpen transition/contention
- Task vs ValueTask
- captured vs static delegate
- snapshot
- callback
- memory diagnostics

Performance optimizations require before/after benchmark evidence.

## Release Definition
V1 is release-ready only when:
- all behavior documented
- all tests pass
- concurrency/stress gates pass
- benchmark and allocation evidence exists
- samples compile and run
- package has zero third-party runtime dependencies
- Release build is clean
- NuGet metadata is complete
