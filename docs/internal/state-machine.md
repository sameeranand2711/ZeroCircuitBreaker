# ZeroCircuitBreaker V1 State Machine

## Model

The public state set is exactly `Closed`, `Open`, and `HalfOpen`. Each logical boundary also creates a new generation. An internal `Transitioning` packed code exists only while publishing a boundary and is helped/retried by observers; it is never a public state or snapshot value.

An admitted operation belongs to the exact `(state, generation)` from which it was acquired. Any completion whose token no longer equals the current packed word is stale and has no state, count, timestamp, or callback effect.

“Success” below includes a normal void completion, a result not classified as failure, and an exception classified as non-failure (including caller-requested cancellation). The original exception is still propagated by Execute APIs. “Failure” includes an exception classified as failure, an unconditional lease failure, or a result predicate returning true.

The synchronous `Execute`, Task convenience `ExecuteTaskAsync`, ValueTask-native `ExecuteAsync`, and lease APIs all feed this same state machine. Task-versus-ValueTask selection changes only the public async representation and adapter cost; it never changes admission, classification, generation, transition, rejection, or callback semantics.

## Complete transition and action table

| Current public state | Trigger/action | Next public state | Generation | Consecutive-failure effect | Open-timestamp effect | Callback reason / rejection |
|---|---|---|---|---|---|---|
| Closed | Admit operation | Closed | Unchanged; lease captures it | Unchanged | Unchanged/neutral | None |
| Closed | Current-generation success when count is 0 | Closed | Unchanged | Remains 0 | Unchanged/neutral | None |
| Closed | Current-generation success when count is greater than 0 | Closed | Unchanged | Atomically reset to 0 | Unchanged/neutral | None |
| Closed | Current-generation failure and new count is below threshold | Closed | Unchanged | Atomically increment by 1 | Unchanged/neutral | None |
| Closed | Current-generation failure reaches threshold | Open | Increment by 1 | Old epoch is sealed; target epoch is 0 | Set to current provider timestamp before Open publication | `FailureThresholdReached` |
| Open | Admission before break duration expires | Open | Unchanged | Remains 0 | Unchanged | Reject Open with non-null remaining `RetryAfter` |
| Open | First eligible admission at/after break duration | HalfOpen | Increment by 1 | Target epoch is 0 | Cleared before HalfOpen publication | `BreakDurationElapsed`; winning admission owns sole probe |
| Open | Competing admission loses expiry transition | HalfOpen as observed after retry | Changed only by winner | Remains 0 | Cleared by winner | Reject HalfOpen with null `RetryAfter` |
| HalfOpen | Admission other than the owner | HalfOpen | Unchanged | Remains 0 | Neutral | Reject HalfOpen with null `RetryAfter` |
| HalfOpen | Current-generation owner completes with success | Closed | Increment by 1 | Target epoch is 0 | Cleared before Closed publication | `HalfOpenProbeSucceeded` |
| HalfOpen | Current-generation owner completes with failure | Open | Increment by 1 | Target epoch is 0 | Set to current provider timestamp before Open publication | `HalfOpenProbeFailed` |
| Closed | `Open()` | Open | Increment by 1 | Target epoch is 0 | Set fresh; break duration starts/restarts | `ManuallyOpened` |
| Open | `Open()` | Open | Increment by 1 | Target epoch is 0 | Replaced with fresh timestamp; duration restarts | `ManuallyOpened` |
| HalfOpen | `Open()` | Open | Increment by 1 | Target epoch is 0 | Set fresh; duration starts/restarts | `ManuallyOpened` |
| Closed | `Reset()` | Closed | Increment by 1 | Target epoch is 0 | Cleared/neutral | `ManuallyReset` |
| Open | `Reset()` | Closed | Increment by 1 | Target epoch is 0 | Cleared/neutral | `ManuallyReset` |
| HalfOpen | `Reset()` | Closed | Increment by 1 | Target epoch is 0 | Cleared/neutral | `ManuallyReset` |
| Any | Completion from an older generation | Current state | Unchanged | No effect on current epoch | No effect | None |
| Any | Duplicate/default lease completion | Current state | Unchanged | No effect | No effect | Throws `InvalidOperationException`; no callback |
| Any | `GetSnapshot()` | Current state | Unchanged | No effect | No effect | Diagnostic read only |

These rows exhaust all V1 triggers. There is no automatic Closed-to-HalfOpen, HalfOpen-to-HalfOpen completion transition, Open-to-Closed expiry transition, multiple-probe path, or background transition.

## Transition invariants

1. A boundary is owned by CAS from the exact source packed word; failed contenders reread and cannot publish their proposed target.
2. Generation increments for every table row that changes logical state and for repeated manual Open/Reset rows where the state name is unchanged.
3. Counter-only Closed outcomes do not increment generation and do not produce callbacks.
4. Target failure count is zero at every boundary.
5. A public Open state is published only after its fresh timestamp is stored. Public Closed/HalfOpen is published only after the timestamp is cleared and target failure epoch is installed.
6. Every Open-to-HalfOpen boundary has one initiating acquisition. Only it receives the probe lease/token; helpers and competitors reject after publication.
7. Rejection never calls protected work and never changes generation or failure count.
8. Snapshots never trigger expiry transitions.
9. Callback invocation happens after final public publication and after transition ownership is released. Callback exceptions do not alter these invariants.

## Race ordering

- Closed success versus Closed failure is ordered by CAS on the generation's count. Threshold sealing prevents a later success from erasing an already won threshold trigger.
- Automatic versus manual transitions are ordered by the single transition-descriptor slot and packed-state CAS. A loser rereads; manual methods retry because they must always establish their own new boundary, while stale operation completions stop.
- Multiple expired Open admissions compete for one Open-to-HalfOpen descriptor. One wins ownership; all others eventually observe HalfOpen and reject.
- Manual Open/Reset makes existing Closed and HalfOpen operation tokens stale immediately at the boundary CAS. Their later completions cannot modify target-generation auxiliary data.
- Reentrant callback operations see the fully published state and may create later generations. No callback is invoked while the transition descriptor is held.

## Retry-after rules

For a stable Open generation:

`RetryAfter = max(TimeSpan.Zero, BreakDuration - TimeProvider.GetElapsedTime(openTimestamp, now))`.

Open rejection always carries that non-null value. HalfOpen rejection always carries null. Closed has no rejection. A snapshot uses the same diagnostic calculation but does not attempt Open-to-HalfOpen.
