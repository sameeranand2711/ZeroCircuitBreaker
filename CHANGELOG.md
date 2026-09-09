# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [1.0.0-rc.1] - 2026-09-09

### Added

- Initial .NET 8 ZeroCircuitBreaker implementation with zero third-party runtime dependencies.
- Thread-safe `Closed`, `Open`, and single-probe `HalfOpen` lifecycle with lock-free, generation-aware state transitions.
- Consecutive handled-failure accounting, configurable threshold and break duration, exception filtering, caller-cancellation handling, and per-call result-failure predicates.
- Synchronous `Execute`, ValueTask-native `ExecuteAsync`, and Task-based `ExecuteTaskAsync` API families, including allocation-conscious state-passing overloads.
- Non-throwing `TryAcquire` admission with exactly-once `CircuitLease` completion and rejection diagnostics.
- Manual `Open()` and `Reset()`, diagnostic snapshots, and reentrant synchronous state-change callbacks with exception isolation.
- Deterministic unit, concurrency, stale-generation, callback-reentrancy, and lease-misuse coverage.
- BenchmarkDotNet timing and allocation measurements for execution, rejection, transitions, async representations, delegate shapes, snapshots, and callbacks.
- Consumer documentation covering lifecycle, API usage, cancellation, thread safety, performance evidence, and V1 scope.

### Fixed

- Ensured a stale, unreserved transition descriptor is cleared so helpers cannot indefinitely block progress while publishing lock-free state transitions.

### Release status

- All functional, concurrency, stress, API-quality, regression, benchmark, sample, and packaging gates pass.
- The independent final review scored version `1.0.0-rc.1` at 96/100: release candidate.
- Validated `.nupkg` and `.snupkg` artifacts are available under `artifacts/agent12-remediation`; publication to NuGet has not been performed.
