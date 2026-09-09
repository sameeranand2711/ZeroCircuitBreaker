using ZeroCircuitBreaker.Testing;
using Breaker = global::ZeroCircuitBreaker.CircuitBreaker;

namespace ZeroCircuitBreaker.Tests;

public sealed class TransitionAndRejectionTests
{
    [Fact]
    public void Threshold_failure_opens_and_resets_failure_count()
    {
        var breaker = CreateBreaker(threshold: 2, out _);

        ClassificationTests.RecordFailure(breaker);
        ClassificationTests.RecordFailure(breaker);

        var snapshot = breaker.GetSnapshot();
        Assert.Equal(CircuitState.Open, snapshot.State);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.NotNull(snapshot.RetryAfter);
    }

    [Fact]
    public void Open_rejects_without_invoking_protected_work_and_reports_retry_after()
    {
        var breaker = CreateBreaker(threshold: 1, out _);
        ClassificationTests.RecordFailure(breaker);
        var invoked = false;

        var exception = Assert.Throws<CircuitBreakerOpenException>(() =>
            breaker.Execute(() => invoked = true));

        Assert.False(invoked);
        Assert.Equal(CircuitState.Open, exception.State);
        Assert.NotNull(exception.RetryAfter);
        Assert.InRange(exception.RetryAfter.Value, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void First_acquisition_after_duration_owns_the_half_open_probe()
    {
        var breaker = CreateBreaker(threshold: 1, out var time);
        ClassificationTests.RecordFailure(breaker);
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.True(breaker.TryAcquire(out var probe, out var successfulRejection));
        Assert.Equal(default, successfulRejection);
        Assert.Equal(CircuitState.HalfOpen, breaker.GetSnapshot().State);

        Assert.False(breaker.TryAcquire(out var rejectedLease, out var rejection));
        Assert.Equal(default, rejectedLease);
        Assert.Equal(CircuitState.HalfOpen, rejection.State);
        Assert.Null(rejection.RetryAfter);

        probe.CompleteSuccess();
        Assert.Equal(CircuitState.Closed, breaker.GetSnapshot().State);
    }

    [Fact]
    public void Half_open_probe_failure_reopens_with_a_fresh_duration()
    {
        var breaker = CreateBreaker(threshold: 1, out var time);
        ClassificationTests.RecordFailure(breaker);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(breaker.TryAcquire(out var probe));

        probe.CompleteFailure();

        var snapshot = breaker.GetSnapshot();
        Assert.Equal(CircuitState.Open, snapshot.State);
        Assert.Equal(TimeSpan.FromSeconds(10), snapshot.RetryAfter);
    }

    [Fact]
    public void Half_open_execute_success_closes_and_failure_reopens()
    {
        var successful = CreateBreaker(threshold: 1, out var successfulTime);
        ClassificationTests.RecordFailure(successful);
        successfulTime.Advance(TimeSpan.FromSeconds(10));

        successful.Execute(static () => { });
        Assert.Equal(CircuitState.Closed, successful.GetSnapshot().State);

        var failed = CreateBreaker(threshold: 1, out var failedTime);
        ClassificationTests.RecordFailure(failed);
        failedTime.Advance(TimeSpan.FromSeconds(10));

        Assert.Throws<InvalidOperationException>(() =>
            failed.Execute(static () => throw new InvalidOperationException("probe failed")));
        Assert.Equal(CircuitState.Open, failed.GetSnapshot().State);
    }

    [Fact]
    public void Snapshot_does_not_trigger_open_to_half_open_transition()
    {
        var breaker = CreateBreaker(threshold: 1, out var time);
        ClassificationTests.RecordFailure(breaker);
        time.Advance(TimeSpan.FromSeconds(11));

        var snapshot = breaker.GetSnapshot();

        Assert.Equal(CircuitState.Open, snapshot.State);
        Assert.Equal(TimeSpan.Zero, snapshot.RetryAfter);
    }

    private static Breaker CreateBreaker(int threshold, out ManualTimeProvider time)
    {
        time = new ManualTimeProvider();
        return new Breaker(
            new CircuitBreakerOptions
            {
                FailureThreshold = threshold,
                BreakDuration = TimeSpan.FromSeconds(10)
            },
            time);
    }
}
