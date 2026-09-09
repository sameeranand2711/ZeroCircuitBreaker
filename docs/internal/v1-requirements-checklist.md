# ZeroCircuitBreaker V1 Requirements Checklist

Source of truth: `docs/internal/v1-technical-specification.md`.

This document is the V1 scope and verification checklist. An unchecked item remains to be implemented or verified by its responsible workflow stage; it does not authorize behavior beyond the authoritative specification.

## Platform and Dependency Constraints

- [ ] Target `net8.0`.
- [ ] Production/runtime code uses only the .NET Base Class Library.
- [ ] The shipped NuGet package has zero third-party runtime dependencies.
- [ ] The implementation is a circuit breaker only.
- [ ] The healthy/Closed path has low overhead.
- [ ] State transitions are thread-safe, deterministic under the specified concurrency rules, and lock-free/atomic.
- [ ] No `lock`, `SemaphoreSlim`, `Monitor`, or equivalent synchronization is used on the hot path.
- [ ] Sync and async APIs are provided.
- [ ] The async path is `ValueTask`-native.
- [ ] No background worker, background task, or timer drives state changes.

## Public Types

- [ ] `CircuitBreaker` is public.
- [ ] `CircuitBreakerOptions` is public.
- [ ] `CircuitLease` is public.
- [ ] `CircuitState` is public.
- [ ] `CircuitStateChange` is public.
- [ ] `CircuitTransitionReason` is public.
- [ ] `CircuitBreakerSnapshot` is public.
- [ ] `CircuitRejection` is public.
- [ ] `CircuitBreakerOpenException` is public.
- [ ] No additional public surface is introduced unless another explicit V1 requirement needs it.

## Configuration and Time

- [ ] `FailureThreshold` defaults to `5`.
- [ ] `BreakDuration` defaults to 30 seconds.
- [ ] Time is obtained from BCL `TimeProvider`.
- [ ] The default time provider is `TimeProvider.System`.
- [ ] Break-duration expiry is evaluated on demand, without timers or background tasks.

## States and Admission

- [ ] `CircuitState` represents exactly `Closed`, `Open`, and `HalfOpen`.
- [ ] Closed admits protected operations.
- [ ] Open rejects protected operations immediately and never invokes the protected work.
- [ ] Once `BreakDuration` has elapsed, the next eligible caller can atomically transition Open to HalfOpen.
- [ ] HalfOpen has exactly one probe owner.
- [ ] HalfOpen rejects every caller other than its one probe owner.
- [ ] There is no fairness guarantee.
- [ ] There is no execution-order guarantee.

## Consecutive Failure Semantics

- [ ] A handled failure in Closed increments the consecutive failure count.
- [ ] A success in Closed resets the consecutive failure count.
- [ ] Reaching `FailureThreshold` transitions Closed to Open.
- [ ] Failures are consecutive, not a sliding-window or percentage calculation.
- [ ] A successful HalfOpen probe transitions to Closed.
- [ ] A failed HalfOpen probe transitions to Open.

## Exception Classification and Cancellation

- [ ] Caller-requested cancellation does not count as a dependency failure.
- [ ] Other exceptions count as dependency failures by default.
- [ ] Optional `IsFailureException` replaces the default classification for non-cancellation exceptions.
- [ ] Exception-based completion follows the configured classification without allowing caller-requested cancellation to count as a dependency failure.

## Result Failures

- [ ] Result-returning calls can supply a per-call `isFailureResult` predicate.
- [ ] A result classified as failure follows the applicable failure transition/counting semantics.
- [ ] A result not classified as failure follows the applicable success transition/reset semantics.

## Atomic State, Generation, and Stale Completions

- [ ] State and generation are packed together in one `long`.
- [ ] The failure count is stored separately.
- [ ] The Open timestamp is stored separately.
- [ ] Meaningful transitions use compare-and-swap operations.
- [ ] The generation increments at every logical state boundary.
- [ ] Every Open-to-HalfOpen transition increments the generation.
- [ ] Every automatic transition to Open or Closed increments the generation.
- [ ] Every manual `Open()` and `Reset()` call increments the generation, including repeated calls that name the current state.
- [ ] An operation completion from an older generation cannot mutate the current generation's state or failure count.
- [ ] A stale completion cannot reopen, close, or otherwise corrupt newer state.
- [ ] Concurrent transition outcomes are deterministic under these admission and generation rules.

## Manual Control

- [ ] `Open()` transitions any current state to Open.
- [ ] `Open()` resets the failure count.
- [ ] `Open()` restarts the break duration.
- [ ] `Open()` increments the generation even when already Open.
- [ ] `Reset()` transitions any current state to Closed.
- [ ] `Reset()` resets the failure count.
- [ ] `Reset()` increments the generation even when already Closed.
- [ ] In-flight completions made stale by `Open()` or `Reset()` cannot mutate the new generation.

## API Levels

- [ ] Convenient synchronous `Execute` APIs are provided.
- [ ] Convenient asynchronous `ExecuteAsync` APIs are provided.
- [ ] Allocation-conscious state-passing overloads using static delegates are provided.
- [ ] The maximum-control API provides `TryAcquire(out CircuitLease)`.
- [ ] The maximum-control API provides a `TryAcquire` rejection overload that returns rejection information without throwing.

## Lease Semantics

- [ ] `CircuitLease` is a value type.
- [ ] An acquired lease can be completed with `CompleteSuccess()`.
- [ ] An acquired lease can be completed with `CompleteFailure()`.
- [ ] An acquired lease can be completed with `CompleteFailure(Exception)`.
- [ ] The caller contract requires each lease to be completed exactly once.
- [ ] Duplicate lease completions cannot corrupt current breaker state.
- [ ] Stale lease completions cannot corrupt current breaker state.

## Rejection Semantics

- [ ] Throwing APIs reject with `CircuitBreakerOpenException`.
- [ ] A throwing rejection does not invoke protected work.
- [ ] The non-throwing acquisition path reports rejection with `CircuitRejection`.
- [ ] An Open rejection exposes a known `RetryAfter`.
- [ ] A HalfOpen rejection exposes a null `RetryAfter`.

## State-Change Callbacks

- [ ] `CircuitBreakerOptions` supports an optional synchronous `OnStateChanged` callback.
- [ ] A callback runs only after its state transition has completed.
- [ ] A callback runs outside synchronization.
- [ ] Callback reentrancy is allowed.
- [ ] A callback exception is isolated and cannot compromise breaker correctness.

## Snapshots and Diagnostics

- [ ] `GetSnapshot()` returns the current state.
- [ ] `GetSnapshot()` returns the consecutive failure count.
- [ ] `GetSnapshot()` returns retry-after information.
- [ ] A snapshot is diagnostic only.
- [ ] Snapshot data is never used to make admission decisions.

## Tests

- [ ] Deterministic state-transition tests cover Closed, Open, and HalfOpen behavior.
- [ ] Deterministic race tests use `Barrier`, `ManualResetEventSlim`, `TaskCompletionSource`, or similar coordination rather than timing assumptions.
- [ ] Stale-generation tests cover completions crossing logical state boundaries.
- [ ] Lease-misuse tests cover stale and duplicate completion safety.
- [ ] Callback reentrancy tests verify correctness.
- [ ] Callback exception tests verify correctness isolation.
- [ ] Cancellation-classification tests distinguish caller-requested cancellation from dependency failures.
- [ ] Result-failure tests cover per-call `isFailureResult` behavior.
- [ ] Stress tests exercise concurrency safety.
- [ ] Release build validation passes.

## Benchmarks and Performance Evidence

- [ ] Benchmarks use BenchmarkDotNet.
- [ ] A direct-call baseline is measured.
- [ ] Closed success is measured.
- [ ] Closed failure is measured.
- [ ] The threshold transition is measured.
- [ ] Open throwing rejection is measured.
- [ ] Open `TryAcquire` rejection is measured.
- [ ] HalfOpen transition/contention is measured.
- [ ] `Task` versus `ValueTask` is measured.
- [ ] Captured versus static delegate calls are measured.
- [ ] Snapshot retrieval is measured.
- [ ] Callback execution is measured.
- [ ] Memory diagnostics are enabled and reported.
- [ ] Every performance optimization is supported by recorded before/after benchmark evidence.

## Documentation, Samples, Packaging, and Release

- [ ] All V1 behavior is documented.
- [ ] Consumer samples are created only at the sample-application workflow stage.
- [ ] Consumer samples compile and run.
- [ ] All tests pass.
- [ ] Concurrency and stress gates pass.
- [ ] Benchmark and allocation evidence exists.
- [ ] The Release build is clean.
- [ ] The package contains no third-party runtime dependency.
- [ ] NuGet metadata is complete.
- [ ] V1 is not declared release-ready until every release criterion above passes.

## V1 Non-Goals / Scope Firewall

The following capabilities must not be implemented in V1:

- [ ] No retries.
- [ ] No timeout policies or built-in timeouts.
- [ ] No fallbacks.
- [ ] No hedging.
- [ ] No bulkheads.
- [ ] No rate limiting.
- [ ] No sliding windows.
- [ ] No percentage thresholds.
- [ ] No distributed or persisted state.
- [ ] No dynamic configuration or dynamic configuration reload.
- [ ] No built-in logging.
- [ ] No telemetry integration, including OpenTelemetry.
- [ ] No dependency-injection package or DI integration.
- [ ] No registries.
- [ ] No managers.
- [ ] No factories.
- [ ] No background workers, background tasks, or timers.
- [ ] No adaptive break durations.
- [ ] No multiple HalfOpen probes.

## Specification-Guard Gate

- Coverage result: **PASS**
- Every normative V1 requirement in the authoritative specification is represented above.
- Every V1 non-goal and every scope-firewall exclusion is explicit above.
- Genuine contradictions found: **None**.
