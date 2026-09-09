# ZeroCircuitBreaker

ZeroCircuitBreaker is a focused, thread-safe circuit breaker for .NET 8. It protects calls to one logical dependency by counting consecutive handled failures, rejecting work while Open, and admitting one probe after the configured break duration.

The production library uses only the .NET Base Class Library. It has no retries, timeouts, fallback, dependency-injection integration, logging integration, timers, or background workers.

## Installation

The package targets `net8.0`. Once the release candidate is published, add it with:

```powershell
dotnet add package ZeroCircuitBreaker --version 1.0.0-rc.1
```

Until publication, reference the project directly from this repository:

```powershell
dotnet add path\to\YourProject.csproj reference src\ZeroCircuitBreaker\ZeroCircuitBreaker.csproj
```

## Create and reuse a breaker

Create one long-lived `CircuitBreaker` for each logical dependency. Do not create a new breaker for every call: its state and consecutive-failure history are intentionally shared across calls.

```csharp
using ZeroCircuitBreaker;

var breaker = new CircuitBreaker(new CircuitBreakerOptions
{
    FailureThreshold = 5,
    BreakDuration = TimeSpan.FromSeconds(30)
});
```

Those values are also the defaults. Options are validated and snapshotted by the constructor; changing or reusing the options object does not reconfigure an existing breaker. A custom BCL `TimeProvider` can be supplied through the constructor overload that accepts options and a time provider when deterministic time is needed.

## Execute protected work

### ValueTask-native async work

`ExecuteAsync` is the ValueTask-native family:

```csharp
static ValueTask<int> ReadValueAsync(CancellationToken cancellationToken) =>
    ValueTask.FromResult(42);

var value = await breaker.ExecuteAsync(
    static token => ReadValueAsync(token),
    cancellationToken);
```

It accepts and returns `ValueTask` or `ValueTask<TResult>` directly. Prefer this family when the protected API is naturally ValueTask-based.

### Task-based async work

`ExecuteTaskAsync` is the Task convenience family:

```csharp
using var response = await breaker.ExecuteTaskAsync(
    token => httpClient.GetAsync(requestUri, token),
    cancellationToken);
```

The separate name is intentional: it avoids ambiguous overload resolution between Task- and ValueTask-returning async lambdas. The returned Task completes only after protected work and circuit accounting have completed.

### Synchronous work

```csharp
static int ReadValue() => 42;

var value = breaker.Execute(static () => ReadValue());
breaker.Execute(static () => Console.WriteLine("dependency call completed"));
```

All throwing execution APIs reject Open or occupied HalfOpen calls with `CircuitBreakerOpenException`. Rejected protected work is never invoked. Exceptions from admitted protected work are accounted for and then rethrown unchanged.

### State-passing delegates

State-passing overloads make it possible to use static delegates without capturing a closure:

```csharp
using var response = await breaker.ExecuteAsync(
    (Client: httpClient, Uri: requestUri),
    static (state, token) =>
        new ValueTask<HttpResponseMessage>(state.Client.GetAsync(state.Uri, token)),
    cancellationToken);
```

Equivalent synchronous state-passing overloads are also available. The state and cancellation token are passed directly to the operation.

## Result-based failures

For dependencies that report failure as a result rather than an exception, supply the per-call `isFailureResult` predicate:

```csharp
using var response = await breaker.ExecuteTaskAsync(
    token => httpClient.GetAsync(requestUri, token),
    static result => (int)result.StatusCode >= 500,
    cancellationToken);
```

The predicate is evaluated once. A `true` result records a handled failure; `false` records success. The dependency result is returned in either case. Without a predicate, every normally returned result is a success.

## Exception filtering and cancellation

By default, exceptions other than caller-requested cancellation count as dependency failures. `IsFailureException` replaces that default classification for non-cancellation exceptions:

```csharp
var breaker = new CircuitBreaker(new CircuitBreakerOptions
{
    FailureThreshold = 3,
    IsFailureException = static exception =>
        exception is HttpRequestException or TimeoutException
});
```

For `Execute`, `ExecuteAsync`, and `ExecuteTaskAsync`, an `OperationCanceledException` is treated as caller cancellation when the cancellation token supplied to the breaker call has been requested. It applies success semantics, bypasses `IsFailureException`, and is still propagated to the caller. An `OperationCanceledException` without requested caller cancellation follows the configured/default exception classification.

For `CircuitLease.CompleteFailure(exception)`, a cancellation exception carrying a requested token likewise applies success semantics. Classification predicates should not throw; V1 does not isolate exceptions thrown by them.

## Lifecycle

The public states are:

- `Closed`: operations are admitted. Handled failures increment a consecutive count; success resets it. Reaching the threshold opens the circuit.
- `Open`: operations are rejected immediately. `RetryAfter` reports the remaining break duration.
- `HalfOpen`: after the break duration expires, the next eligible caller atomically becomes the single probe owner. Every other caller is rejected while that probe is in progress.

Expiry is evaluated only when a caller attempts admission. There are no timers or background transitions. Probe success closes the circuit; probe failure opens it for a fresh break duration. An Open rejection has a non-null `RetryAfter`; an occupied HalfOpen rejection has `RetryAfter == null`.

`GetSnapshot()` returns the observed state, consecutive failures, and diagnostic retry-after value. A snapshot never triggers a transition and must not be used to decide whether work may be admitted; call an execution method or `TryAcquire` for that decision.

## Non-throwing admission with `TryAcquire`

Use `TryAcquire` when you need to control invocation yourself or avoid exceptions for normal rejection:

```csharp
if (!breaker.TryAcquire(out var lease, out var rejection))
{
    Console.WriteLine($"Rejected in {rejection.State}; retry after {rejection.RetryAfter}");
    return;
}

string result;
try
{
    result = await dependency.ReadAsync(cancellationToken);
}
catch (Exception exception)
{
    lease.CompleteFailure(exception);
    throw;
}

lease.CompleteSuccess();
Console.WriteLine(result);
```

An acquired `CircuitLease` must be completed exactly once:

- `CompleteSuccess()` records success.
- `CompleteFailure()` records an unconditional dependency failure.
- `CompleteFailure(Exception)` applies cancellation and configured exception classification; it does not throw the supplied exception for you.

`CircuitLease` is a value type, but copies share one completion gate. Completing a lease or any copy more than once, or completing `default(CircuitLease)`, throws `InvalidOperationException` without changing breaker state. A first completion that became stale because a newer state generation superseded it returns normally as a no-op and cannot corrupt the newer circuit.

## Manual control and callbacks

```csharp
breaker.Open(); // Open, reset failures, and restart BreakDuration
breaker.Reset(); // Closed and reset failures
```

Both methods establish a new logical generation from any state. Repeated `Open()` and repeated `Reset()` calls do so as well, making older in-flight completions stale.

An optional synchronous state-change callback runs after each transition has been published:

```csharp
var breaker = new CircuitBreaker(new CircuitBreakerOptions
{
    OnStateChanged = static change =>
        Console.WriteLine($"{change.PreviousState} -> {change.CurrentState}: {change.Reason}")
});
```

The callback may re-enter the breaker. Callback exceptions are suppressed and do not roll back or corrupt the transition.

## Thread-safety guarantees

`CircuitBreaker` is thread-safe and uses atomic, generation-aware transitions. In particular:

- at most one HalfOpen probe is admitted;
- Open and occupied HalfOpen states never execute rejected protected work;
- manual boundaries supersede older in-flight operations;
- stale or duplicate lease completion cannot mutate the active generation;
- callbacks run after publication and outside transition ownership;
- snapshots expose only public, consistently published states.

Admission is atomic, but there is no fairness guarantee and no protected-work execution-order guarantee. The breaker does not queue callers.

## Performance evidence

Agent 07 measured the concurrency-approved implementation with BenchmarkDotNet 0.15.2 and `MemoryDiagnoser` on .NET 8.0.29, Windows 11, and an AMD Ryzen 3 3250U. These are environment-specific measurements, not throughput guarantees or performance targets.

| Scenario | Mean | Allocated / operation |
|---|---:|---:|
| Direct invocation | 0.7144 ns | 0 B |
| Closed success | 31.1653 ns | 0 B |
| Closed handled failure | 47.6943 ns | 0 B |
| Task-based execution | 83.2654 ns | 144 B |
| ValueTask-based execution | 49.4914 ns | 0 B |
| Captured lambda | 43.2720 ns | 64 B |
| Static/state-passing delegate | 28.0211 ns | 0 B |
| Snapshot capture | 8.8194 ns | 0 B |
| Open `TryAcquire` rejection | 46.94 ns | 0 B |
| Open throwing rejection | 18,128.94 ns | 800 B |
| Threshold transition | 533.84 ns | 104 B |
| Open-to-HalfOpen transition | 132.71 ns | 160 B |
| HalfOpen contention rejection | 73.55 ns | 0 B |
| State change without callback | 76.24 ns | 104 B |
| State change with callback | 71.58 ns | 104 B |

The callback confidence intervals overlapped, so that run found no measurable callback-specific overhead or allocation. See [the benchmark report](docs/internal/benchmark-report.md) for methodology, ratios, Gen0 activity, environment, and reproduction commands.

## V1 scope

ZeroCircuitBreaker V1 is deliberately only a circuit breaker. It does not provide:

- retries, built-in timeouts, fallback, or hedging;
- bulkheads, rate limiting, sliding windows, or percentage thresholds;
- distributed or persisted state;
- multiple HalfOpen probes or adaptive break durations;
- dynamic configuration or reload;
- built-in logging, OpenTelemetry, or other telemetry integration;
- dependency-injection packages, registries, managers, or factories;
- background workers, background tasks, or timers.

Compose separate policies outside this library when your application needs them.

## Current release status

The V1 implementation, API review, regression and stress suites, concurrency validation, benchmarks, consumer documentation, sample applications, NuGet metadata, package inspection, and clean external-consumer validation have passed. The independent final review scored the repository 96/100. Version `1.0.0-rc.1` is the first release candidate for consumer validation before the stable `1.0.0` release.

Validated package artifacts are available under `artifacts/agent12-remediation`. Publication to NuGet is a separate external release action and has not been performed by this workflow.

## License

ZeroCircuitBreaker is available under the [MIT License](LICENSE).
