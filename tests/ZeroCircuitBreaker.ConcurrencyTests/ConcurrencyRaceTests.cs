using ZeroCircuitBreaker.Testing;
using Breaker = global::ZeroCircuitBreaker.CircuitBreaker;

namespace ZeroCircuitBreaker.ConcurrencyTests;

public sealed class ConcurrencyRaceTests
{
    private static readonly TimeSpan HangTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Simultaneous_threshold_failures_publish_one_open_transition()
    {
        const int workerCount = 8;
        using var allOperationsAdmitted = new Barrier(workerCount);
        var changes = new List<CircuitStateChange>();
        var changesGate = new object();
        var breaker = new Breaker(new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            OnStateChanged = change =>
            {
                lock (changesGate)
                {
                    changes.Add(change);
                }
            }
        });

        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => StartLongRunning(() =>
            {
                try
                {
                    breaker.Execute(() =>
                    {
                        allOperationsAdmitted.SignalAndWait();
                        throw new DependencyException();
                    });
                }
                catch (DependencyException)
                {
                }
            }))
            .ToArray();

        await Task.WhenAll(workers);

        Assert.Equal(CircuitState.Open, breaker.GetSnapshot().State);
        Assert.Equal(0, breaker.GetSnapshot().ConsecutiveFailures);
        lock (changesGate)
        {
            var change = Assert.Single(changes);
            Assert.Equal(CircuitTransitionReason.FailureThresholdReached, change.Reason);
        }
    }

    [Fact]
    public async Task Success_racing_with_threshold_failure_has_a_serializable_outcome()
    {
        using var bothOperationsAdmitted = new Barrier(2);
        var breaker = new Breaker(new CircuitBreakerOptions { FailureThreshold = 2 });
        RecordFailure(breaker);

        var success = StartLongRunning(() => breaker.Execute(() =>
        {
            bothOperationsAdmitted.SignalAndWait();
        }));
        var failure = StartLongRunning(() =>
        {
            try
            {
                breaker.Execute(() =>
                {
                    bothOperationsAdmitted.SignalAndWait();
                    throw new DependencyException();
                });
            }
            catch (DependencyException)
            {
            }
        });

        await Task.WhenAll(success, failure);

        var snapshot = breaker.GetSnapshot();
        if (snapshot.State == CircuitState.Open)
        {
            Assert.Equal(0, snapshot.ConsecutiveFailures);
        }
        else
        {
            Assert.Equal(CircuitState.Closed, snapshot.State);
            Assert.Equal(1, snapshot.ConsecutiveFailures);
        }
    }

    [Fact]
    public async Task Many_callers_after_expiry_produce_exactly_one_probe_owner()
    {
        const int callerCount = 16;
        var time = new ManualTimeProvider();
        var breaker = new Breaker(
            new CircuitBreakerOptions { BreakDuration = TimeSpan.FromSeconds(1) },
            time);
        breaker.Open();
        time.Advance(TimeSpan.FromSeconds(1));
        using var callersReady = new Barrier(callerCount);

        var callers = Enumerable.Range(0, callerCount)
            .Select(_ => StartLongRunning(() =>
            {
                callersReady.SignalAndWait();
                var acquired = breaker.TryAcquire(out var lease, out var rejection);
                return new Acquisition(acquired, lease, rejection);
            }))
            .ToArray();

        var acquisitions = await Task.WhenAll(callers);

        var owner = Assert.Single(acquisitions, acquisition => acquisition.Acquired);
        Assert.Equal(
            callerCount - 1,
            acquisitions.Count(acquisition =>
                !acquisition.Acquired &&
                acquisition.Rejection.State == CircuitState.HalfOpen &&
                acquisition.Rejection.RetryAfter is null));
        Assert.Equal(CircuitState.HalfOpen, breaker.GetSnapshot().State);

        owner.Lease.CompleteSuccess();
        Assert.Equal(CircuitState.Closed, breaker.GetSnapshot().State);
    }

    [Fact]
    public void Reset_during_half_open_probe_makes_probe_completion_stale()
    {
        var time = new ManualTimeProvider();
        var breaker = new Breaker(
            new CircuitBreakerOptions { BreakDuration = TimeSpan.FromSeconds(1) },
            time);
        breaker.Open();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(breaker.TryAcquire(out var probe));
        Assert.Equal(CircuitState.HalfOpen, breaker.GetSnapshot().State);

        breaker.Reset();
        probe.CompleteFailure();

        Assert.Equal(CircuitState.Closed, breaker.GetSnapshot().State);
        Assert.Equal(0, breaker.GetSnapshot().ConsecutiveFailures);
    }

    [Fact]
    public async Task Open_while_calls_are_in_flight_invalidates_every_old_completion()
    {
        const int callCount = 6;
        using var releaseOperations = new ManualResetEventSlim();
        var entered = Enumerable.Range(0, callCount)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var breaker = new Breaker(new CircuitBreakerOptions { FailureThreshold = 2 });

        var calls = Enumerable.Range(0, callCount)
            .Select(index => StartLongRunning(() =>
            {
                try
                {
                    breaker.Execute((entered[index], releaseOperations, index), static (state, _) =>
                    {
                        state.Item1.SetResult();
                        state.releaseOperations.Wait();
                        if ((state.index & 1) != 0)
                        {
                            throw new DependencyException();
                        }
                    });
                }
                catch (DependencyException)
                {
                }
            }))
            .ToArray();

        await Task.WhenAll(entered.Select(signal => signal.Task)).WaitAsync(HangTimeout);
        breaker.Open();
        releaseOperations.Set();
        await Task.WhenAll(calls);

        var snapshot = breaker.GetSnapshot();
        Assert.Equal(CircuitState.Open, snapshot.State);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
    }

    [Fact]
    public async Task Stale_success_after_open_cannot_close_or_reset_new_generation()
    {
        using var releaseOperation = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var breaker = new Breaker(new CircuitBreakerOptions { FailureThreshold = 3 });
        RecordFailure(breaker);

        var call = StartLongRunning(() => breaker.Execute(() =>
        {
            entered.SetResult();
            releaseOperation.Wait();
        }));

        await entered.Task.WaitAsync(HangTimeout);
        breaker.Open();
        releaseOperation.Set();
        await call;

        var snapshot = breaker.GetSnapshot();
        Assert.Equal(CircuitState.Open, snapshot.State);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
    }

    [Fact]
    public async Task Stale_failure_after_reset_cannot_increment_or_open_new_generation()
    {
        using var releaseOperation = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var breaker = new Breaker(new CircuitBreakerOptions { FailureThreshold = 1 });

        var call = StartLongRunning(() =>
        {
            try
            {
                breaker.Execute(() =>
                {
                    entered.SetResult();
                    releaseOperation.Wait();
                    throw new DependencyException();
                });
            }
            catch (DependencyException)
            {
            }
        });

        await entered.Task.WaitAsync(HangTimeout);
        breaker.Reset();
        releaseOperation.Set();
        await call;

        var snapshot = breaker.GetSnapshot();
        Assert.Equal(CircuitState.Closed, snapshot.State);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
    }

    [Fact]
    public void Callback_can_reenter_after_transition_publication()
    {
        var changes = new List<CircuitStateChange>();
        Breaker? breaker = null;
        breaker = new Breaker(new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            OnStateChanged = change =>
            {
                changes.Add(change);
                if (change.Reason == CircuitTransitionReason.FailureThresholdReached)
                {
                    Assert.Equal(CircuitState.Open, breaker!.GetSnapshot().State);
                    breaker.Reset();
                }
            }
        });

        RecordFailure(breaker);

        Assert.Equal(CircuitState.Closed, breaker.GetSnapshot().State);
        Assert.Collection(
            changes,
            change => Assert.Equal(CircuitTransitionReason.FailureThresholdReached, change.Reason),
            change => Assert.Equal(CircuitTransitionReason.ManuallyReset, change.Reason));
    }

    [Fact]
    public async Task Concurrent_closed_success_stress_preserves_closed_zero_failure_snapshot()
    {
        const int workerCount = 64;
        const int iterationsPerWorker = 100;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var breaker = new Breaker();

        var workers = Enumerable.Range(0, workerCount)
            .Select(async _ =>
            {
                await start.Task;
                for (var iteration = 0; iteration < iterationsPerWorker; iteration++)
                {
                    Assert.True(breaker.TryAcquire(out var lease));
                    lease.CompleteSuccess();
                }
            })
            .ToArray();

        start.SetResult();
        await Task.WhenAll(workers);

        var snapshot = breaker.GetSnapshot();
        Assert.Equal(CircuitState.Closed, snapshot.State);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
    }

    private static void RecordFailure(Breaker breaker)
    {
        Assert.Throws<DependencyException>(() =>
            breaker.Execute(static () => throw new DependencyException()));
    }

    private static Task StartLongRunning(Action action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static Task<TResult> StartLongRunning<TResult>(Func<TResult> action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private readonly record struct Acquisition(
        bool Acquired,
        CircuitLease Lease,
        CircuitRejection Rejection);

    private sealed class DependencyException : Exception;
}
