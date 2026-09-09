# ZeroCircuitBreaker V1 Benchmark Report

## Scope and conclusion

Agent 07 measured the Agent 06-approved implementation without changing production code. All benchmark scenarios required by the V1 specification were executed with BenchmarkDotNet and `MemoryDiagnoser`.

The results establish measured timing and allocation characteristics; they do not define performance targets. No correctness compromise, public API change, or complexity-increasing optimization is justified by this run. The healthy synchronous paths, ValueTask path, state-passing path, snapshot path, and non-throwing rejection path had no managed allocation detected by MemoryDiagnoser. Costs that do allocate have explicit allocation-free or lower-allocation alternatives where the accepted API provides one: `TryAcquire` instead of exception rejection, ValueTask instead of Task, and state-passing static delegates instead of per-call captured lambdas.

Agent 07 result: **PASS; no Agent 05 remediation recommended.**

## Reproduction environment

- BenchmarkDotNet 0.15.2
- Windows 11 10.0.26200.9168
- AMD Ryzen 3 3250U, 2 physical / 4 logical cores
- .NET SDK 10.0.101
- Benchmark runtime: .NET 8.0.29, x64 RyuJIT, AVX2
- GC: concurrent workstation
- Build: Release, `net8.0`
- Standard job: one launch, three warmup iterations, eight measurement iterations
- Transition job: one launch, five warmup iterations, fifteen measurement iterations, one invocation containing 32,768 measured operations

Run from the repository root:

```powershell
dotnet restore benchmarks\ZeroCircuitBreaker.Benchmarks\ZeroCircuitBreaker.Benchmarks.csproj
dotnet build benchmarks\ZeroCircuitBreaker.Benchmarks\ZeroCircuitBreaker.Benchmarks.csproj --configuration Release --no-restore
dotnet run --project benchmarks\ZeroCircuitBreaker.Benchmarks\ZeroCircuitBreaker.Benchmarks.csproj --configuration Release --no-build -- --filter "*"
```

BenchmarkDotNet's generated CSV, GitHub Markdown, and HTML reports are under `BenchmarkDotNet.Artifacts/results`.

## Methodology

- The direct baseline calls the same non-inlinable dependency method used through the Closed synchronous breaker path.
- Closed handled failure uses result classification and `FailureThreshold = int.MaxValue`, so it measures Closed failure accounting without exception construction or an accidental threshold transition.
- Task and ValueTask benchmarks both complete synchronously with result `42`. The Task delegate uses `Task.FromResult(42)`, avoiding the runtime's small-integer task cache so the selected Task representation is visible in allocation results.
- Captured-lambda construction occurs inside the measured call. The state-passing comparison uses a static delegate and passes state explicitly.
- Transition preconditions are prepared outside measured work. Threshold and Open-to-HalfOpen transitions use separate breaker instances for each measured operation. Batching 32,768 operations per invocation prevents the single-operation benchmark harness from dominating transition timings.
- HalfOpen contention measures the rejected contender path while one lease owns the single HalfOpen probe. It intentionally measures the path under ownership contention without adding scheduler or thread-pool noise to the breaker operation.
- Callback measurements compare repeated, valid `Reset()` boundaries with and without a synchronous callback; both include the same transition work.
- A dash in BenchmarkDotNet output means no collection/allocation was detected or the statistic was not emitted. It is recorded as `0` for allocated bytes when MemoryDiagnoser detected none, and as `-` for unavailable Gen0 activity.

## Results

### Execution, allocation, and diagnostics

The relative column is BenchmarkDotNet's ratio to the direct invocation baseline. Because the direct dependency call is less than one nanosecond on this machine, the absolute means are more useful than the large ratios for engineering decisions.

| Benchmark | Mean | Relative | Gen0 / 1,000 ops | Allocated / op |
|---|---:|---:|---:|---:|
| Direct invocation | 0.7144 ns | 1.01x | - | 0 B |
| Closed + success | 31.1653 ns | 43.90x | - | 0 B |
| Closed + handled failure | 47.6943 ns | 67.19x | - | 0 B |
| Task-based execution | 83.2654 ns | 117.29x | 0.0688 | 144 B |
| ValueTask-based execution | 49.4914 ns | 69.72x | - | 0 B |
| Captured lambda | 43.2720 ns | 60.96x | 0.0306 | 64 B |
| Static/state-passing delegate | 28.0211 ns | 39.47x | - | 0 B |
| Snapshot capture | 8.8194 ns | 12.42x | - | 0 B |

Task-based execution measured 1.68x the ValueTask-native mean and 144 B/op versus no detected allocation for ValueTask. The 144 B includes the Task selected by the protected delegate and the Task facade's complete breaker operation. The captured-lambda case measured 1.54x the static/state-passing mean and allocated 64 B/op versus no detected allocation for state passing.

### Rejection

The relative column compares both Open rejection APIs directly.

| Benchmark | Mean | Relative | Gen0 / 1,000 ops | Allocated / op |
|---|---:|---:|---:|---:|
| Open + `TryAcquire` rejection | 46.94 ns | 1.00x | - | 0 B |
| Open + throwing rejection | 18,128.94 ns | 386.55x | 0.3662 | 800 B |

Exception construction and throwing dominate the throwing rejection measurement. This is expected public behavior, and V1 already exposes the non-throwing `TryAcquire` path for callers that need to avoid that cost.

### State transitions and HalfOpen contention

Transition setup is excluded from measured work. These rows are batched per-operation means; they were not ratioed against the direct invocation because they use a separate state-preparation job.

| Benchmark | Mean | Relative | Gen0 / 1,000 ops | Allocated / op |
|---|---:|---:|---:|---:|
| Threshold transition | 533.84 ns | n/a | - | 104 B |
| Open-to-HalfOpen transition | 132.71 ns | n/a | - | 160 B |
| HalfOpen contention rejection | 73.55 ns | n/a | - | 0 B |

The threshold transition includes result-failure accounting and Closed-to-Open publication. The HalfOpen transition includes expiry evaluation, Open-to-HalfOpen publication, and creation of the successful public lease. Its allocation therefore includes both transition and lease ownership costs. The contention row is the rejection path after a different lease already owns the probe.

### State-change callback

The relative column compares callback-enabled transitions to the identical transition without a callback.

| Benchmark | Mean | Relative | Gen0 / 1,000 ops | Allocated / op |
|---|---:|---:|---:|---:|
| State change without callback | 76.24 ns | 1.01x | 0.0497 | 104 B |
| State change with callback | 71.58 ns | 0.95x | 0.0497 | 104 B |

The confidence intervals overlap. This run therefore found no measurable callback overhead for the minimal counter callback and no callback-specific allocation; both rows allocate the same 104 B transition machinery.

## Interpretation and optimization decision

The measurements show the intended tradeoffs in the accepted public surface:

- Closed success is allocation-free in this scenario.
- Closed result-failure accounting is allocation-free until an actual state boundary is crossed.
- ValueTask-native execution avoids the Task allocations measured for the equivalent synchronously completed operation.
- State-passing static delegates avoid the captured delegate's 64 B/op.
- Non-throwing Open rejection avoids exception cost and allocation.
- Snapshot capture is allocation-free in this scenario.
- Logical transitions allocate, while steady-state HalfOpen rejection does not.

No performance target exists in the V1 specification, and the run did not reveal a correctness-related performance defect. The measured allocation sites correspond to explicit API choices or infrequent logical boundaries. Removing them would require production redesign or added complexity without an accepted target demonstrating that the tradeoff is necessary. Consequently, no optimization was proposed or implemented, and no before/after section is applicable.

Results are specific to this runtime, hardware, and benchmark configuration. They should be rerun when the implementation, runtime, or target deployment hardware changes; they are not throughput guarantees.
