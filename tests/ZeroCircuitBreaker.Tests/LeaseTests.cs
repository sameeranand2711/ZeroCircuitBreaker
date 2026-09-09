using ZeroCircuitBreaker.Testing;
using Breaker = global::ZeroCircuitBreaker.CircuitBreaker;

namespace ZeroCircuitBreaker.Tests;

public sealed class LeaseTests
{
    [Fact]
    public void TryAcquire_in_closed_returns_lease_and_default_rejection()
    {
        var breaker = new Breaker();

        Assert.True(breaker.TryAcquire(out var lease, out var rejection));
        Assert.Equal(default, rejection);

        lease.CompleteSuccess();
        Assert.Equal(CircuitState.Closed, breaker.GetSnapshot().State);
    }

    [Fact]
    public void TryAcquire_in_open_returns_default_lease_and_open_rejection()
    {
        var breaker = new Breaker();
        breaker.Open();

        Assert.False(breaker.TryAcquire(out var lease, out var rejection));
        Assert.Equal(default, lease);
        Assert.Equal(CircuitState.Open, rejection.State);
        Assert.NotNull(rejection.RetryAfter);
    }

    [Fact]
    public void Lease_success_resets_failures_and_failure_counts()
    {
        var breaker = new Breaker(new CircuitBreakerOptions { FailureThreshold = 2 });
        Assert.True(breaker.TryAcquire(out var first));
        first.CompleteFailure();
        Assert.Equal(1, breaker.GetSnapshot().ConsecutiveFailures);

        Assert.True(breaker.TryAcquire(out var success));
        success.CompleteSuccess();
        Assert.Equal(0, breaker.GetSnapshot().ConsecutiveFailures);

        Assert.True(breaker.TryAcquire(out var failure));
        failure.CompleteFailure();
        Assert.Equal(1, breaker.GetSnapshot().ConsecutiveFailures);
    }

    [Fact]
    public void Lease_exception_completion_uses_cancellation_and_configured_filter()
    {
        var breaker = new Breaker(new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            IsFailureException = static exception => exception is InvalidOperationException
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.True(breaker.TryAcquire(out var canceled));
        canceled.CompleteFailure(new OperationCanceledException(cancellation.Token));
        Assert.Equal(0, breaker.GetSnapshot().ConsecutiveFailures);

        Assert.True(breaker.TryAcquire(out var ignored));
        ignored.CompleteFailure(new ArgumentException("ignored"));
        Assert.Equal(0, breaker.GetSnapshot().ConsecutiveFailures);

        Assert.True(breaker.TryAcquire(out var counted));
        counted.CompleteFailure(new InvalidOperationException("counted"));
        Assert.Equal(1, breaker.GetSnapshot().ConsecutiveFailures);
    }

    [Fact]
    public void Stale_lease_completion_is_a_normal_no_op()
    {
        var breaker = new Breaker();
        Assert.True(breaker.TryAcquire(out var stale));
        breaker.Open();
        breaker.Reset();

        var exception = Record.Exception(stale.CompleteFailure);

        Assert.Null(exception);
        var snapshot = breaker.GetSnapshot();
        Assert.Equal(CircuitState.Closed, snapshot.State);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
    }

    [Fact]
    public void Lease_copies_share_duplicate_completion_gate()
    {
        var breaker = new Breaker();
        Assert.True(breaker.TryAcquire(out var original));
        var copy = original;

        original.CompleteSuccess();

        Assert.Throws<InvalidOperationException>(copy.CompleteFailure);
        Assert.Equal(CircuitState.Closed, breaker.GetSnapshot().State);
    }

    [Fact]
    public void Double_completion_and_default_lease_completion_are_rejected()
    {
        var breaker = new Breaker();
        Assert.True(breaker.TryAcquire(out var lease));
        lease.CompleteSuccess();

        Assert.Throws<InvalidOperationException>(lease.CompleteSuccess);
        Assert.Throws<InvalidOperationException>(() => default(CircuitLease).CompleteSuccess());
        Assert.Throws<InvalidOperationException>(() => default(CircuitLease).CompleteFailure());
    }

    [Fact]
    public void CompleteFailure_rejects_null_before_mutating_breaker()
    {
        var breaker = new Breaker();
        Assert.True(breaker.TryAcquire(out var lease));

        Assert.Throws<ArgumentNullException>(() => lease.CompleteFailure(null!));
        Assert.Equal(0, breaker.GetSnapshot().ConsecutiveFailures);

        lease.CompleteSuccess();
    }

    [Fact]
    public void Stale_half_open_probe_cannot_reopen_after_reset()
    {
        var time = new ManualTimeProvider();
        var breaker = new Breaker(
            new CircuitBreakerOptions { BreakDuration = TimeSpan.FromSeconds(1) },
            time);
        breaker.Open();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(breaker.TryAcquire(out var probe));
        breaker.Reset();

        probe.CompleteFailure();

        Assert.Equal(CircuitState.Closed, breaker.GetSnapshot().State);
        Assert.Equal(0, breaker.GetSnapshot().ConsecutiveFailures);
    }
}
