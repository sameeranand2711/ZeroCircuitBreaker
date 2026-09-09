# ZeroCircuitBreaker V1 Architecture

## Status and authority

This document is the architecture deliverable for Agent 03. It translates `docs/internal/v1-technical-specification.md` into an implementation design. The specification remains authoritative; this document does not broaden V1.

The production assembly targets `net8.0`, enables nullable reference types, treats compiler warnings as errors, and has no runtime dependency outside the .NET Base Class Library. It contains one concrete breaker implementation and the public value/configuration types listed in `public-api.md`. It has no policy pipeline, registry, factory, dependency-injection integration, timer, worker, or background task.

## Source organization

Production source is grouped by responsibility:

- `src/ZeroCircuitBreaker/Public` contains one file for each supporting public type;
- `src/ZeroCircuitBreaker/CircuitBreaker` contains cohesive partials for the public API, state-transition core, and execution core of the single `CircuitBreaker` type;
- `src/ZeroCircuitBreaker/Internal` contains one file for each private implementation-support type under the `ZeroCircuitBreaker.Internal` namespace.

All accepted public types remain in the `ZeroCircuitBreaker` namespace. The folders are a source-maintenance boundary, not an additional consumer namespace hierarchy; moving the public types would break the fixed V1 API. Only implementation helpers use the `ZeroCircuitBreaker.Internal` namespace.

## Object model

`CircuitBreaker` is a sealed object that owns all state for one circuit:

- immutable validated option values;
- one `TimeProvider`;
- one packed `long` containing the generation and current state code;
- one generation-scoped failure counter stored separately from the packed state;
- one open timestamp stored separately from the packed state;
- at most one internal transition descriptor used to coordinate and help an in-progress boundary.

The options object is copied into immutable fields during construction. Later mutation of the caller's options object cannot reconfigure a breaker. V1 has no dynamic configuration.

## Packed state and generation

The packed state is a single atomically accessed `long`:

- bits 0-1 hold an internal state code;
- bits 2-63 hold an unsigned 62-bit generation;
- public codes are `Closed = 0`, `Open = 1`, and `HalfOpen = 2`;
- internal code `Transitioning = 3` is a publication marker and is never returned by a public API.

Packing is `(generation << 2) | stateCode`; unpacking masks the low two bits and shifts right by two. Initial state is `(generation 0, Closed)`.

Every logical state boundary reserves the next generation by compare-and-swap (CAS) from the exact observed public packed word to `(next generation, Transitioning)`. After auxiliary data is initialized, the winner or a helper publishes the corresponding public state with the same new generation. The generation therefore changes exactly once per logical boundary. A transition descriptor prevents a second transition from starting during publication.

Generation arithmetic uses the 62-bit field. Exhausting all 62-bit generation values is outside the realizable V1 lifetime; the implementation must nevertheless make the wrap operation explicit and tested at the packing-unit level rather than depend on signed overflow behavior.

## Transition protocol and CAS loops

An internal immutable transition descriptor records the exact source packed word, target packed word, previous and next public states, transition reason, target failure-counter epoch, and target open-timestamp value. It also contains atomic one-time flags for final publication/callback ownership. It is a concrete implementation detail, not a public extensibility point.

All logical transitions use this protocol:

1. Read a stable public packed state. If it is `Transitioning`, help the published descriptor finish and retry.
2. Create the descriptor for the required source, target, next generation, reset failure epoch, timestamp effect, and reason.
3. CAS the single transition-descriptor slot from null to that descriptor. If another descriptor is present, help it and retry from current state.
4. Revalidate the exact source packed word and all trigger-specific preconditions. If they no longer hold, remove the unused descriptor and retry or report a stale completion.
5. CAS the packed state from the exact source word to `(next generation, Transitioning)`. Failure means the operation helps/cleans up and restarts from a fresh read.
6. The winner or any helper publishes the descriptor's new failure epoch and open timestamp, then release-publishes the final public packed state for the target generation.
7. Clear the descriptor before invoking user code. Exactly one participant claims callback delivery, then invokes it synchronously after the public state and auxiliary fields are complete.

The descriptor makes a partially completed transition helpable: a delayed thread cannot leave other callers permanently spinning solely because it won the first CAS. CAS loops always reread on contention; they do not assume fairness or execution order. There is no `lock`, `Monitor`, `SemaphoreSlim`, blocking wait, timer, or background activity on admission or transition paths.

The `Transitioning` marker is not a fourth circuit state. Public readers help or retry until one of the three public states is observable.

## Generation equality and stale operations

Every admitted operation captures the exact packed public word, not merely the public enum. Its completion is eligible to act only while:

- the current packed word exactly equals the captured word;
- no transition descriptor has reserved a boundary from that word; and
- for Closed work, the captured failure epoch is still the epoch for the captured generation.

Failure of any equality check makes the completion stale. A stale completion is a no-op: it cannot update the current failure count, start a transition, replace an open timestamp, or invoke a callback. Equality is never based only on `CircuitState`, so returning later to the same named state does not revive an old completion.

Manual `Open()` and `Reset()` use the same boundary protocol even when the named state already matches. They therefore invalidate every in-flight operation from the old generation.

## Failure count

The consecutive-failure count is not stored in the packed state. It lives in a private generation-scoped failure epoch. A lease/admitted call holds the epoch that belonged to its captured generation; replacing the current epoch at a boundary makes later writes to an old epoch harmless.

Within a Closed epoch, the count and a sealed flag are one atomically updated value:

- a classified success CAS-resets a nonzero count to zero;
- a classified failure CAS-increments the count, saturating at `FailureThreshold`;
- the update that reaches the threshold atomically seals the epoch;
- a sealed epoch admits no further Closed work or counter updates and causes a threshold-to-Open transition to be installed or helped;
- the transition creates a new target-generation epoch with count zero.

Sealing orders a threshold failure against a concurrent success. A success that resets first causes the threshold CAS to retry from the reset count; a threshold CAS that seals first prevents the success from erasing the transition trigger. Counter operations recheck the packed generation/descriptor around their CAS. Mutations that race with an already reserved manual or automatic boundary affect only the old epoch.

Failures are consecutive only. There is no sample window, percentage, history buffer, or rolling calculation. Open, HalfOpen, and every manual boundary expose/reset the current generation's count to zero.

## Open timestamp and time

The open timestamp is a separate `long` containing a value from `TimeProvider.GetTimestamp()`. It is meaningful only in a published Open generation. A transition to Open records its descriptor's timestamp after transition ownership is established and publishes that timestamp before release-publishing the public Open packed state. Helpers write the same descriptor value, so no losing contender can replace the winner's timestamp.

This ordering guarantees that a reader cannot observe a public Open generation without its corresponding timestamp. `Open()` always takes a fresh timestamp, including Open-to-Open, so it restarts `BreakDuration`. Threshold and failed-probe transitions also take a fresh timestamp. Transitions to Closed or HalfOpen clear the stored timestamp to its neutral value before publishing the target state.

Elapsed time is calculated with the injected provider's timestamp APIs. Remaining duration is clamped to zero. There are no timers: only an admission attempt evaluates expiry and can transition Open to HalfOpen. `GetSnapshot()` may calculate a diagnostic remaining duration but never performs admission or a transition.

## Admission and half-open ownership

Admission repeatedly reads a stable public state and its generation-consistent auxiliary data:

- `Closed`: acquire an operation token for that exact generation and its failure epoch.
- `Open` before expiry: reject with the computed non-null `RetryAfter` and do not invoke protected work.
- `Open` at or after expiry: compete to install the single Open-to-HalfOpen descriptor. The descriptor's initiating admission owns the probe if that transition wins. Competitors help publication, then observe HalfOpen and reject.
- `HalfOpen`: reject with null `RetryAfter` unless the caller is the unique owner returned by the winning Open-to-HalfOpen acquisition.

Probe ownership is represented by the admitted operation/lease token associated with the new HalfOpen packed word. It is not transferable through a new acquisition. Probe success transitions that exact HalfOpen generation to Closed; probe failure transitions it to Open. Abandoning an acquired probe leaves the circuit HalfOpen; V1 has no timeout or background recovery for an uncompleted lease.

No fairness or protected-work execution order is promised.

## Execution and completion

The `Execute`, ValueTask-native `ExecuteAsync`, and Task convenience `ExecuteTaskAsync` families share the same internal admission/completion routines. The operation is never called after rejection. The native async core awaits `ValueTask` directly and uses `ConfigureAwait(false)` where an await is required; it never blocks.

Task and ValueTask delegates intentionally do not overload the same method name. `async` lambdas can be target-typed to either delegate return type, so same-name overloads would introduce ambiguity. `ExecuteAsync` exclusively identifies the ValueTask-native path; `ExecuteTaskAsync` exclusively identifies the Task facade. No public adapter delegate type is added.

Each Task convenience overload adapts its returned `Task`/`Task<TResult>` to `ValueTask`/`ValueTask<TResult>`, invokes the corresponding ValueTask-native core, and returns the complete core operation as `Task`/`Task<TResult>` via `AsTask()`. The returned Task therefore completes only after breaker accounting completes and need not be the protected operation's original Task. Synchronous throws from the Task delegate, asynchronous faults, and cancellation all pass through the same classifier as the native path. This adapter is one-way: the native path never converts a ValueTask to Task unless the caller selected `ExecuteTaskAsync`.

Outcome handling is:

- a normal void completion is success;
- a returned result is failure only when that call supplied a predicate and it returns true;
- caller-requested cancellation is a non-failure outcome;
- every other exception is a failure by default;
- when configured, `IsFailureException` replaces the default classification for non-cancellation exceptions;
- an exception classified as non-failure follows success/reset semantics for the circuit and is still rethrown unchanged to the caller;
- an exception classified as failure updates the circuit and is still rethrown unchanged;
- a failed result updates the circuit and is returned normally.

For `Execute`, `ExecuteAsync`, and `ExecuteTaskAsync`, an `OperationCanceledException` is caller-requested when the supplied call token is requested. For `CircuitLease.CompleteFailure(Exception)`, which has no separate call token, it is caller-requested only when the exception carries a requested cancellation token. The configured exception predicate is never allowed to turn caller-requested cancellation into a dependency failure.

Argument validation occurs before admission. User predicates are invoked only after protected work produces the corresponding result/exception. Unlike `OnStateChanged`, classification predicates have no V1 exception-isolation guarantee and are not an extensibility or recovery mechanism.

## Public lease safety

`CircuitLease` is a readonly value type, but value copies must share one private reference-type completion gate. The first completion atomically claims that gate. A duplicate call, including a call through another struct copy, throws `InvalidOperationException` without touching breaker state. A completion on `default(CircuitLease)` also throws `InvalidOperationException`.

After the gate is claimed, a completion may still be stale because its breaker generation has changed. That stale completion returns normally and performs no mutation. `CompleteFailure(Exception)` validates a non-null exception and uses the breaker's cancellation/exception classifier. The convenience execution paths may use an internal trusted completion token rather than allocate the public copy-safety gate; those paths own completion exactly once by construction.

## Callbacks

`OnStateChanged` is optional and synchronous. It runs once for each completed logical transition in the transition table, including repeated manual Open and Reset boundaries. It does not run for counter-only changes, rejection, stale/duplicate completion, or failed CAS attempts.

The transition is fully published and the internal descriptor is cleared before callback invocation. The callback may therefore call any breaker API reentrantly. Callback order across concurrent transition threads is not an execution-order guarantee. All exceptions thrown by `OnStateChanged` are caught and suppressed; they cannot roll back a transition, escape `Open()`/`Reset()`/execution APIs, or prevent later breaker use.

## Diagnostics

`GetSnapshot()` loops until it has read a stable public packed word plus auxiliary data belonging to that generation. It returns the public state, current generation's consecutive failures, and diagnostic retry-after. Retry-after is non-null only for Open; it may be zero after expiry if no admission has yet performed Open-to-HalfOpen. The snapshot never drives admission and callers must not use it as an acquire token.

## Validation and scope firewall

Construction rejects `FailureThreshold <= 0`, `BreakDuration <= TimeSpan.Zero`, null options, and a null explicitly supplied `TimeProvider`. Delegate parameters and exception arguments are null-checked. Defaults are threshold 5, duration 30 seconds, and `TimeProvider.System`.

This design contains no retry, timeout policy, fallback, hedging, bulkhead, rate limiter, sliding/percentage window, persistence, distributed state, adaptive duration, logging, telemetry, DI package, registry, manager, factory, multiple probe, or dynamic reload behavior.
