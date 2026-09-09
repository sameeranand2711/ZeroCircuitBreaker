# Agent 04 test design

The functional project covers option validation; Closed execution; success reset; threshold opening; Open rejection and `RetryAfter`; on-demand Open-to-HalfOpen; probe success/failure; exception and cancellation classification; result failures that are still returned; manual and repeated Open/Reset; transition reasons; callback isolation; snapshot semantics; `TryAcquire`; rejection diagnostics; lease success/failure; stale lease completion; and copied, duplicate, and default lease safety.

It also compiles calls across the accepted synchronous, `ExecuteTaskAsync`, ValueTask-native `ExecuteAsync`, and static state-passing surfaces.

The concurrency project uses `Barrier`, `ManualResetEventSlim`, and `TaskCompletionSource` milestones to cover simultaneous threshold failures, success racing a threshold failure, callers racing after expiry, reset during a half-open probe, manual Open while calls are in flight, stale success after Open, stale failure after Reset, callback reentrancy, and concurrent Closed-path stress. It contains no `Thread.Sleep` or delay-based scheduling assertion. Time-dependent behavior uses a manually advanced `TimeProvider`. A fixed timeout only guards milestone waits against a hung test; it does not arrange or prove a race outcome.

At Agent 04 completion the production project is intentionally only an exact public-API compilation shell. Behavioral tests are therefore expected to fail until Agent 05 implements the approved state machine. Compilation is the Agent 04 structural gate; a red behavioral run is the intended TDD baseline rather than a gate failure.
