# ZeroCircuitBreaker V1 Development Rules

## Status and authority

This document is the development-policy deliverable for Agent 02. It governs implementation, tests, benchmarks, and reviews for V1.

The authoritative source for product scope, architecture, and public behavior remains `docs/internal/v1-technical-specification.md`. The traceability baseline is `docs/internal/v1-requirements-checklist.md`. If this document appears to conflict with either source, implementation on the affected path must stop and the conflict must be recorded; this document does not authorize changing or expanding V1.

## Decision priority

Make engineering decisions in this order:

1. Correctness and concurrency safety.
2. Human readability and ease of debugging, while preserving correctness and thread safety.
3. Measured performance optimization supported by benchmark evidence.

Do not trade a higher-priority property for a lower-priority one. Complexity-increasing performance work requires benchmark evidence that identifies a relevant bottleneck and demonstrates a material improvement.

## Project and compiler settings

- Enable nullable reference types in every production project.
- Treat compiler warnings as errors in every production project.
- Add XML documentation to every public API. Documentation must accurately describe the API's V1 contract, including behavior that callers need in order to use it correctly.
- Do not suppress warnings merely to pass a build. Fix the cause, or document and narrowly scope a suppression when the warning is demonstrably inapplicable.

These settings are required for production projects. Test and benchmark project settings may be chosen for their purpose, but must not weaken production compilation.

## Asynchronous code

- Do not block on asynchronous work. `.Wait()`, `.Result`, and equivalent sync-over-async mechanisms are prohibited.
- Use `ConfigureAwait(false)` on library-owned awaits where it is applicable.
- Keep cancellation and completion behavior faithful to the V1 specification; convenience is not grounds for changing public behavior.
- Do not create linked `CancellationTokenSource` instances unless the accepted scope is explicitly revised to require them.

## Concurrency and lifecycle

- Prefer explicit, inspectable state transitions and the simplest synchronization mechanism that is correct.
- Do not place locks, semaphores, or equivalent blocking coordination on the hot path.
- Do not introduce hidden threads, background tasks, or timers.
- Resource ownership and cleanup must be explicit and deterministic.
- When an implementation choice has uncertain concurrency semantics, favor the clearer correct design and prove it with deterministic tests before considering optimization.

## Production-code design

- Keep methods small, cohesive, and easy to step through in a debugger.
- Express state transitions explicitly; avoid control flow that obscures which state is read, changed, or published.
- Avoid unnecessary LINQ and avoid allocation-heavy abstractions on hot paths. Straightforward loops and direct operations are preferred when they improve clarity and allocation behavior.
- Do not use reflection or `dynamic` unless an approved V1 requirement explicitly needs it.
- Do not use a service-locator pattern.
- Do not add speculative interfaces, factories, managers, wrappers, extension points, or abstraction layers. Add an abstraction only when an accepted V1 requirement or a concrete test seam requires it.
- Do not add fallback behaviors, configuration, diagnostics, policies, or convenience APIs that are absent from the V1 specification.
- Do not create consumer sample applications before the workflow reaches the sample-application stage.

## Testing rules

- Concurrency tests must be deterministic. Coordinate competing operations with explicit synchronization primitives, observable milestones, or controlled test doubles.
- Do not use arbitrary `Thread.Sleep` calls or timing races as evidence of correctness.
- A timeout may guard a test suite against a hang, but it must not be the mechanism that arranges the behavior under test or establishes the assertion.
- Tests must verify the public behavior and edge cases traced in `docs/internal/v1-requirements-checklist.md` without redefining the contract.
- Tests for state transitions should make the precondition, transition trigger, and expected resulting state observable and unambiguous.

## Performance rules

- Correctness changes do not require benchmark justification.
- A performance optimization that increases implementation complexity requires before-and-after benchmark evidence.
- Benchmark evidence must use a representative scenario, isolate the behavior being optimized, and report enough information for another developer to reproduce the comparison.
- An optimization must not weaken thread safety, change accepted public behavior, or broaden V1 scope.
- If evidence does not show a material benefit, retain the simpler implementation.

## Review checklist

Before an implementation gate can pass, reviewers must confirm:

- The change maps to an accepted V1 requirement and does not implement a V1 non-goal.
- Public behavior and accepted architecture remain unchanged unless the orchestration workflow has explicitly approved a specification revision.
- Production projects enable nullable reference types and treat warnings as errors.
- Every public API has accurate XML documentation.
- The code contains no sync-over-async and uses `ConfigureAwait(false)` for applicable library-owned awaits.
- Hot paths contain no unnecessary LINQ, locks, semaphores, or unmeasured complexity.
- The change introduces no unapproved reflection, `dynamic`, service location, speculative abstractions, hidden execution resources, or linked cancellation sources.
- Concurrency tests use deterministic coordination and no arbitrary sleeps or timing races.
- State transitions and resource ownership are explicit and easy to debug.
- Every complexity-increasing optimization is supported by reproducible benchmark evidence.

## Conflict handling

If a rule cannot be followed without contradicting the authoritative V1 specification or an accepted architectural decision, record the exact conflict and affected requirement in `.codex/workflow-state.md`, mark the affected gate as failed, and stop that path. Do not silently revise architecture, public behavior, or scope.
