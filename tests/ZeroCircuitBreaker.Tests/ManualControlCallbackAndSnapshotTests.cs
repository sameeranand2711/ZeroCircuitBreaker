using ZeroCircuitBreaker.Testing;
using Breaker = global::ZeroCircuitBreaker.CircuitBreaker;

namespace ZeroCircuitBreaker.Tests;

public sealed class ManualControlCallbackAndSnapshotTests
{
    [Fact]
    public void Manual_open_resets_failures_and_emits_the_documented_change()
    {
        var changes = new List<CircuitStateChange>();
        var breaker = new Breaker(new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            OnStateChanged = changes.Add
        });
        ClassificationTests.RecordFailure(breaker);

        breaker.Open();

        var change = Assert.Single(changes);
        Assert.Equal(CircuitState.Closed, change.PreviousState);
        Assert.Equal(CircuitState.Open, change.CurrentState);
        Assert.Equal(CircuitTransitionReason.ManuallyOpened, change.Reason);
        Assert.Equal(0, breaker.GetSnapshot().ConsecutiveFailures);
    }

    [Fact]
    public void Repeated_open_restarts_duration_and_emits_a_new_boundary()
    {
        var time = new ManualTimeProvider();
        var changes = new List<CircuitStateChange>();
        var breaker = new Breaker(
            new CircuitBreakerOptions
            {
                BreakDuration = TimeSpan.FromSeconds(10),
                OnStateChanged = changes.Add
            },
            time);

        breaker.Open();
        time.Advance(TimeSpan.FromSeconds(7));
        breaker.Open();

        var snapshot = breaker.GetSnapshot();
        Assert.Equal(TimeSpan.FromSeconds(10), snapshot.RetryAfter);
        Assert.Equal(2, changes.Count);
        Assert.All(changes, change => Assert.Equal(CircuitTransitionReason.ManuallyOpened, change.Reason));
        Assert.Equal(CircuitState.Open, changes[1].PreviousState);
        Assert.Equal(CircuitState.Open, changes[1].CurrentState);
    }

    [Fact]
    public void Reset_from_open_closes_and_repeated_reset_emits_a_new_boundary()
    {
        var changes = new List<CircuitStateChange>();
        var breaker = new Breaker(new CircuitBreakerOptions { OnStateChanged = changes.Add });
        breaker.Open();

        breaker.Reset();
        breaker.Reset();

        Assert.Equal(CircuitState.Closed, breaker.GetSnapshot().State);
        Assert.Collection(
            changes,
            change => Assert.Equal(CircuitTransitionReason.ManuallyOpened, change.Reason),
            change =>
            {
                Assert.Equal(CircuitState.Open, change.PreviousState);
                Assert.Equal(CircuitState.Closed, change.CurrentState);
                Assert.Equal(CircuitTransitionReason.ManuallyReset, change.Reason);
            },
            change =>
            {
                Assert.Equal(CircuitState.Closed, change.PreviousState);
                Assert.Equal(CircuitState.Closed, change.CurrentState);
                Assert.Equal(CircuitTransitionReason.ManuallyReset, change.Reason);
            });
    }

    [Fact]
    public void Automatic_transitions_emit_exact_reasons()
    {
        var time = new ManualTimeProvider();
        var changes = new List<CircuitStateChange>();
        var breaker = new Breaker(
            new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromSeconds(5),
                OnStateChanged = changes.Add
            },
            time);

        ClassificationTests.RecordFailure(breaker);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.True(breaker.TryAcquire(out var probe));
        probe.CompleteSuccess();

        Assert.Collection(
            changes,
            change => Assert.Equal(CircuitTransitionReason.FailureThresholdReached, change.Reason),
            change => Assert.Equal(CircuitTransitionReason.BreakDurationElapsed, change.Reason),
            change => Assert.Equal(CircuitTransitionReason.HalfOpenProbeSucceeded, change.Reason));
    }

    [Fact]
    public void Callback_exception_is_suppressed_after_publication()
    {
        var breaker = new Breaker(new CircuitBreakerOptions
        {
            OnStateChanged = static _ => throw new InvalidOperationException("callback")
        });

        var exception = Record.Exception(breaker.Open);

        Assert.Null(exception);
        Assert.Equal(CircuitState.Open, breaker.GetSnapshot().State);
        exception = Record.Exception(breaker.Reset);
        Assert.Null(exception);
        Assert.Equal(CircuitState.Closed, breaker.GetSnapshot().State);
    }

    [Fact]
    public void Snapshot_reports_closed_count_without_changing_admission()
    {
        var breaker = ClassificationTests.CreateBreaker(threshold: 3);
        ClassificationTests.RecordFailure(breaker);

        var first = breaker.GetSnapshot();
        var second = breaker.GetSnapshot();

        Assert.Equal(CircuitState.Closed, first.State);
        Assert.Equal(1, first.ConsecutiveFailures);
        Assert.Null(first.RetryAfter);
        Assert.Equal(first.State, second.State);
        Assert.Equal(first.ConsecutiveFailures, second.ConsecutiveFailures);
    }
}
