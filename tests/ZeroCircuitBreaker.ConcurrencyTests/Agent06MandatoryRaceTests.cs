using System.Reflection;
using ZeroCircuitBreaker.Testing;
using Breaker = global::ZeroCircuitBreaker.CircuitBreaker;

namespace ZeroCircuitBreaker.ConcurrencyTests;

public sealed class Agent06MandatoryRaceTests
{
    private static readonly TimeSpan HangTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Generation_ten_operation_success_is_stale_after_manual_open_generation_eleven()
    {
        using var releaseOperation = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var breaker = new Breaker();
        for (var generation = 0; generation < 10; generation++)
        {
            breaker.Reset();
        }

        Assert.Equal<ulong>(10, ReadGeneration(breaker));
        var operation = StartLongRunning(() =>
            breaker.Execute(() =>
            {
                entered.SetResult();
                releaseOperation.Wait();
            }));

        await entered.Task.WaitAsync(HangTimeout);
        breaker.Open();
        Assert.Equal<ulong>(11, ReadGeneration(breaker));
        releaseOperation.Set();
        await operation.WaitAsync(HangTimeout);

        var snapshot = breaker.GetSnapshot();
        Assert.Equal(CircuitState.Open, snapshot.State);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
    }

    [Fact]
    public async Task Concurrent_repeated_resets_each_create_one_closed_generation()
    {
        const int callerCount = 12;
        using var callersReady = new Barrier(callerCount);
        var resetCallbacks = 0;
        var breaker = new Breaker(new CircuitBreakerOptions
        {
            OnStateChanged = change =>
            {
                Assert.Equal(CircuitState.Closed, change.PreviousState);
                Assert.Equal(CircuitState.Closed, change.CurrentState);
                Assert.Equal(CircuitTransitionReason.ManuallyReset, change.Reason);
                Interlocked.Increment(ref resetCallbacks);
            }
        });

        var resets = Enumerable.Range(0, callerCount)
            .Select(_ => StartLongRunning(() =>
            {
                callersReady.SignalAndWait();
                breaker.Reset();
            }))
            .ToArray();

        await Task.WhenAll(resets).WaitAsync(HangTimeout);

        var snapshot = breaker.GetSnapshot();
        Assert.Equal(CircuitState.Closed, snapshot.State);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.Equal(callerCount, Volatile.Read(ref resetCallbacks));
        Assert.Equal<ulong>(callerCount, ReadGeneration(breaker));
    }

    [Fact]
    public void Stale_copied_lease_after_many_boundaries_cannot_mutate_active_generation()
    {
        var breaker = new Breaker();
        Assert.True(breaker.TryAcquire(out var stale));
        var copy = stale;

        for (var boundary = 0; boundary < 3; boundary++)
        {
            breaker.Open();
            breaker.Reset();
        }

        var firstCompletion = Record.Exception(stale.CompleteFailure);

        Assert.Null(firstCompletion);
        Assert.Throws<InvalidOperationException>(copy.CompleteSuccess);
        var snapshot = breaker.GetSnapshot();
        Assert.Equal(CircuitState.Closed, snapshot.State);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.Equal<ulong>(6, ReadGeneration(breaker));
    }

    [Fact]
    public async Task Concurrent_completion_through_lease_copies_mutates_breaker_once()
    {
        using var completionsReady = new Barrier(2);
        var breaker = new Breaker(new CircuitBreakerOptions { FailureThreshold = 3 });
        Assert.True(breaker.TryAcquire(out var original));
        var copy = original;

        var first = StartLongRunning(() =>
        {
            completionsReady.SignalAndWait();
            return Record.Exception(original.CompleteFailure);
        });
        var second = StartLongRunning(() =>
        {
            completionsReady.SignalAndWait();
            return Record.Exception(copy.CompleteFailure);
        });

        var exceptions = await Task.WhenAll(first, second).WaitAsync(HangTimeout);

        Assert.Single(exceptions, exception => exception is null);
        Assert.Single(exceptions, exception => exception is InvalidOperationException);
        var snapshot = breaker.GetSnapshot();
        Assert.Equal(CircuitState.Closed, snapshot.State);
        Assert.Equal(1, snapshot.ConsecutiveFailures);
    }

    [Fact]
    public void State_change_callbacks_can_reenter_with_open_and_reset()
    {
        var openChanges = new List<CircuitStateChange>();
        Breaker? openBreaker = null;
        openBreaker = new Breaker(new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            OnStateChanged = change =>
            {
                openChanges.Add(change);
                if (change.Reason == CircuitTransitionReason.FailureThresholdReached)
                {
                    openBreaker!.Open();
                }
            }
        });

        Assert.Throws<DependencyException>(() =>
            openBreaker.Execute(static () => throw new DependencyException()));

        Assert.Equal(CircuitState.Open, openBreaker.GetSnapshot().State);
        Assert.Collection(
            openChanges,
            change => Assert.Equal(CircuitTransitionReason.FailureThresholdReached, change.Reason),
            change => Assert.Equal(CircuitTransitionReason.ManuallyOpened, change.Reason));

        var resetChanges = new List<CircuitStateChange>();
        Breaker? resetBreaker = null;
        resetBreaker = new Breaker(new CircuitBreakerOptions
        {
            OnStateChanged = change =>
            {
                resetChanges.Add(change);
                if (change.Reason == CircuitTransitionReason.ManuallyOpened)
                {
                    resetBreaker!.Reset();
                }
            }
        });

        resetBreaker.Open();

        Assert.Equal(CircuitState.Closed, resetBreaker.GetSnapshot().State);
        Assert.Collection(
            resetChanges,
            change => Assert.Equal(CircuitTransitionReason.ManuallyOpened, change.Reason),
            change => Assert.Equal(CircuitTransitionReason.ManuallyReset, change.Reason));
    }

    [Fact]
    public async Task Concurrent_open_rejections_never_invoke_protected_work()
    {
        const int callerCount = 12;
        using var callersReady = new Barrier(callerCount);
        var invoked = 0;
        var breaker = new Breaker();
        breaker.Open();

        var calls = Enumerable.Range(0, callerCount)
            .Select(_ => StartLongRunning(() =>
            {
                callersReady.SignalAndWait();
                return Assert.Throws<CircuitBreakerOpenException>(() =>
                    breaker.Execute(() => Interlocked.Increment(ref invoked)));
            }))
            .ToArray();

        var rejections = await Task.WhenAll(calls).WaitAsync(HangTimeout);

        Assert.Equal(0, Volatile.Read(ref invoked));
        Assert.All(rejections, rejection =>
        {
            Assert.Equal(CircuitState.Open, rejection.State);
            Assert.NotNull(rejection.RetryAfter);
        });
    }

    [Fact]
    public async Task Concurrent_snapshots_help_active_transition_and_never_observe_partial_state()
    {
        const int readerCount = 8;
        var time = new ManualTimeProvider();
        var breaker = new Breaker(
            new CircuitBreakerOptions { BreakDuration = TimeSpan.FromSeconds(5) },
            time);
        InstallDescriptor(
            breaker,
            sourcePacked: 0L,
            transitioningPacked: 7L,
            targetPacked: 5L,
            targetState: CircuitState.Open,
            reason: CircuitTransitionReason.ManuallyOpened,
            targetOpenTimestamp: time.GetTimestamp());
        using var readersReady = new Barrier(readerCount);

        var readers = Enumerable.Range(0, readerCount)
            .Select(_ => StartLongRunning(() =>
            {
                readersReady.SignalAndWait();
                return breaker.GetSnapshot();
            }))
            .ToArray();

        var snapshots = await Task.WhenAll(readers).WaitAsync(HangTimeout);

        Assert.All(snapshots, snapshot =>
        {
            Assert.Equal(CircuitState.Open, snapshot.State);
            Assert.Equal(0, snapshot.ConsecutiveFailures);
            Assert.Equal(TimeSpan.FromSeconds(5), snapshot.RetryAfter);
        });
        Assert.Null(ReadTransition(breaker));
        Assert.Equal<ulong>(1, ReadGeneration(breaker));
    }

    [Fact]
    public void Packed_generation_wrap_is_explicit_and_preserves_the_public_state_code()
    {
        const ulong maximumGeneration = (1UL << 62) - 1;
        var packed = InvokeStatic<long>("Pack", maximumGeneration, (int)CircuitState.HalfOpen);
        var next = InvokeStatic<ulong>("NextGeneration", packed);

        Assert.Equal(maximumGeneration, InvokeStatic<ulong>("GetGeneration", packed));
        Assert.Equal((int)CircuitState.HalfOpen, InvokeStatic<int>("GetStateCode", packed));
        Assert.Equal<ulong>(0, next);
        Assert.Equal(0L, InvokeStatic<long>("Pack", next, (int)CircuitState.Closed));
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

    private static ulong ReadGeneration(Breaker breaker)
    {
        var packed = (long)typeof(Breaker)
            .GetField("_packedState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(breaker)!;
        return unchecked((ulong)packed) >> 2;
    }

    private static object? ReadTransition(Breaker breaker) =>
        typeof(Breaker)
            .GetField("_transition", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(breaker);

    private static void InstallDescriptor(
        Breaker breaker,
        long sourcePacked,
        long transitioningPacked,
        long targetPacked,
        CircuitState targetState,
        CircuitTransitionReason reason,
        long targetOpenTimestamp)
    {
        var breakerType = typeof(Breaker);
        var assembly = breakerType.Assembly;
        var epochType = assembly.GetType("ZeroCircuitBreaker.Internal.FailureEpoch", throwOnError: true)!;
        var descriptorType = assembly.GetType("ZeroCircuitBreaker.Internal.TransitionDescriptor", throwOnError: true)!;
        var targetEpoch = Activator.CreateInstance(epochType, nonPublic: true)!;
        var descriptor = Activator.CreateInstance(
            descriptorType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                sourcePacked,
                transitioningPacked,
                targetPacked,
                CircuitState.Closed,
                targetState,
                reason,
                targetEpoch,
                targetOpenTimestamp
            ],
            culture: null)!;

        breakerType
            .GetField("_transition", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(breaker, descriptor);
    }

    private static TResult InvokeStatic<TResult>(string methodName, params object[] arguments) =>
        (TResult)typeof(Breaker)
            .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;

    private sealed class DependencyException : Exception;
}
